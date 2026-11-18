using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using ServiceHub.Api.Common;
using ServiceHub.Api.Data;

namespace ServiceHub.Api.Tests.Infrastructure;

/// <summary>Captures SMS messages so tests can read OTP codes and invitation tokens.</summary>
public class CapturingSmsSender : ISmsSender
{
    public ConcurrentQueue<(string Phone, string Message)> Sent { get; } = new();

    public Task SendAsync(string phone, string message, CancellationToken ct = default)
    {
        Sent.Enqueue((phone, message));
        return Task.CompletedTask;
    }

    public string LastCodeFor(string phone, string pattern = @"(\d{6})")
    {
        var msg = Sent.Last(m => m.Phone == phone).Message;
        return Regex.Match(msg, pattern).Groups[1].Value;
    }
}

/// <summary>
/// Boots the real API against a throwaway PostgreSQL database (created per test run, dropped afterwards).
/// Override the server connection with the SERVICEHUB_TEST_PG environment variable.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminPhone = "01700000000";
    public const string AdminPassword = "Admin@12345";

    private readonly string _dbName = $"servicehub_test_{Guid.NewGuid():N}"[..30];
    private readonly string _server = Environment.GetEnvironmentVariable("SERVICEHUB_TEST_PG")
                                      ?? "Host=/var/run/postgresql;Username=" + Environment.UserName;

    public CapturingSmsSender Sms { get; } = new();

    private string ConnectionString => $"{_server};Database={_dbName}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-0123456789-0123456789-abcdef");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Seed:SampleZones", "true");
        builder.UseSetting("Seed:SuperAdminPhone", AdminPhone);
        builder.UseSetting("Seed:SuperAdminPassword", AdminPassword);
        builder.UseSetting("RateLimits:AuthPerMinute", "100000");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISmsSender>();
            services.AddSingleton<ISmsSender>(Sms);
        });
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        await using var conn = new NpgsqlConnection($"{_server};Database=postgres");
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{_dbName}\"", conn);
        await cmd.ExecuteNonQueryAsync();
        _ = Server; // start the host → runs migrations + seed
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection($"{_server};Database=postgres");
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_dbName}\" WITH (FORCE)", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Runs code against the API's DbContext (e.g. to move a booking into the past).</summary>
    public async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public Api Anonymous() => new(CreateClient());

    public async Task<Api> RegisterAsync(string name, string phone, string password = "Passw0rd!")
    {
        var api = Anonymous();
        var res = await api.PostAsync("/api/auth/register", new { fullName = name, phone, password });
        await res.Expect(201);
        var body = await res.Json();
        api.SetToken(body["tokens"]!["accessToken"]!.GetValue<string>());
        api.UserId = Guid.Parse(body["user"]!["id"]!.GetValue<string>());
        api.Phone = body["user"]!["phone"]!.GetValue<string>();
        return api;
    }

    public async Task<Api> LoginAsync(string login, string password)
    {
        var api = Anonymous();
        var res = await api.PostAsync("/api/auth/login", new { login, password });
        await res.Expect(200);
        var body = await res.Json();
        api.SetToken(body["tokens"]!["accessToken"]!.GetValue<string>());
        api.UserId = Guid.Parse(body["user"]!["id"]!.GetValue<string>());
        return api;
    }

    public Task<Api> AdminAsync() => LoginAsync(AdminPhone, AdminPassword);
}

/// <summary>Small typed wrapper around HttpClient using the API's JSON conventions.</summary>
public class Api(HttpClient client)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public HttpClient Client { get; } = client;
    public Guid UserId { get; set; }
    public string Phone { get; set; } = "";

    public void SetToken(string token) => Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    public Task<HttpResponseMessage> GetAsync(string url) => Client.GetAsync(url);
    public Task<HttpResponseMessage> PostAsync(string url, object? body = null) => Client.PostAsJsonAsync(url, body ?? new { }, Json);
    public Task<HttpResponseMessage> PutAsync(string url, object body) => Client.PutAsJsonAsync(url, body, Json);
    public Task<HttpResponseMessage> PatchAsync(string url, object body) => Client.PatchAsJsonAsync(url, body, Json);
    public Task<HttpResponseMessage> DeleteAsync(string url) => Client.DeleteAsync(url);

    public async Task<JsonNode> GetJson(string url)
    {
        var res = await GetAsync(url);
        await res.Expect(200);
        return await res.Json();
    }
}

public static class HttpAssertions
{
    public static async Task<JsonNode> Json(this HttpResponseMessage res) =>
        JsonNode.Parse(await res.Content.ReadAsStringAsync()) ?? new JsonObject();

    /// <summary>Asserts the status code, printing the response body on failure.</summary>
    public static async Task Expect(this HttpResponseMessage res, int status, string? code = null)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True((int)res.StatusCode == status, $"Expected {status}, got {(int)res.StatusCode} for {res.RequestMessage?.Method} {res.RequestMessage?.RequestUri}: {body}");
        if (code is not null)
            Assert.Equal(code, JsonNode.Parse(body)?["code"]?.GetValue<string>());
    }

    public static async Task<JsonNode> ExpectJson(this Task<HttpResponseMessage> call, int status = 200)
    {
        var res = await call;
        await res.Expect(status);
        return await res.Json();
    }

    public static async Task ExpectStatus(this Task<HttpResponseMessage> call, int status, string? code = null)
    {
        var res = await call;
        await res.Expect(status, code);
    }

    public static string Str(this JsonNode? node, string key) => node![key]!.GetValue<string>();
    public static Guid Id(this JsonNode? node, string key = "id") => Guid.Parse(node.Str(key));
}

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
