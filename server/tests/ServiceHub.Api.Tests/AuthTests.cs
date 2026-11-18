using ServiceHub.Api.Tests.Infrastructure;

namespace ServiceHub.Api.Tests;

[Collection(ApiCollection.Name)]
public class AuthTests(ApiFactory factory)
{
    [Fact]
    public async Task Register_login_and_me()
    {
        var phone = TestData.NextPhone();
        var api = await factory.RegisterAsync("রহিম উদ্দিন", phone);

        var me = await api.GetJson("/api/me");
        Assert.Equal("রহিম উদ্দিন", me.Str("fullName"));
        Assert.Equal("+88" + phone, me.Str("phone"));

        // Bangla digits and +880 prefix normalize to the same account.
        var bangla = string.Concat(phone.Select(c => "০১২৩৪৫৬৭৮৯"[c - '0']));
        await factory.LoginAsync(bangla, "Passw0rd!");
        await factory.LoginAsync("+88" + phone, "Passw0rd!");
    }

    [Fact]
    public async Task Duplicate_phone_and_wrong_password_are_rejected()
    {
        var phone = TestData.NextPhone();
        await factory.RegisterAsync("করিম", phone);
        await factory.Anonymous().PostAsync("/api/auth/register", new { fullName = "অন্য", phone, password = "Passw0rd!" })
            .ExpectStatus(409, "phone_taken");
        await factory.Anonymous().PostAsync("/api/auth/login", new { login = phone, password = "wrong-password" })
            .ExpectStatus(401, "invalid_credentials");
        await factory.Anonymous().PostAsync("/api/auth/register", new { fullName = "X Y", phone = "12345", password = "Passw0rd!" })
            .ExpectStatus(400, "invalid_phone");
    }

    [Fact]
    public async Task Protected_endpoints_need_a_token()
    {
        await factory.Anonymous().GetAsync("/api/me").ExpectStatus(401);
    }

    [Fact]
    public async Task Refresh_token_rotates_and_old_token_is_revoked()
    {
        var phone = TestData.NextPhone();
        await factory.RegisterAsync("সুমি", phone);
        var anon = factory.Anonymous();
        var login = await anon.PostAsync("/api/auth/login", new { login = phone, password = "Passw0rd!", rememberMe = true }).ExpectJson();
        var refresh = login["tokens"].Str("refreshToken");

        var rotated = await anon.PostAsync("/api/auth/refresh", new { refreshToken = refresh }).ExpectJson();
        Assert.NotEqual(refresh, rotated.Str("refreshToken"));
        await anon.PostAsync("/api/auth/refresh", new { refreshToken = refresh }).ExpectStatus(401, "invalid_refresh_token");

        await anon.PostAsync("/api/auth/logout", new { refreshToken = rotated.Str("refreshToken") }).ExpectStatus(204);
        await anon.PostAsync("/api/auth/refresh", new { refreshToken = rotated.Str("refreshToken") }).ExpectStatus(401);
    }

    [Fact]
    public async Task Phone_verification_and_password_reset_via_otp()
    {
        var phone = TestData.NextPhone();
        var api = await factory.RegisterAsync("নাসির", phone);

        await api.PostAsync("/api/auth/otp", new { phone, purpose = "phone_verification" }).ExpectStatus(202);
        await api.PostAsync("/api/auth/verify-phone", new { code = "000000" }).ExpectStatus(400, "invalid_code");
        var code = factory.Sms.LastCodeFor("+88" + phone);
        var me = await api.PostAsync("/api/auth/verify-phone", new { code }).ExpectJson();
        Assert.True(me["phoneVerified"]!.GetValue<bool>());

        await factory.Anonymous().PostAsync("/api/auth/otp", new { phone, purpose = "password_reset" }).ExpectStatus(202);
        var reset = factory.Sms.LastCodeFor("+88" + phone);
        await factory.Anonymous().PostAsync("/api/auth/password/reset", new { phone, code = reset, newPassword = "NewPassw0rd!" })
            .ExpectStatus(204);
        await factory.LoginAsync(phone, "NewPassw0rd!");
        // Codes are single-use.
        await factory.Anonymous().PostAsync("/api/auth/password/reset", new { phone, code = reset, newPassword = "Another1!" })
            .ExpectStatus(400, "invalid_code");
    }

    [Fact]
    public async Task Suspended_user_loses_access_immediately()
    {
        var phone = TestData.NextPhone();
        var user = await factory.RegisterAsync("স্থগিত ব্যবহারকারী", phone);
        var admin = await factory.AdminAsync();
        await admin.PostAsync($"/api/admin/users/{user.UserId}/suspend", new { reason = "জাল তথ্য দিয়ে নিবন্ধন" }).ExpectStatus(204);

        await user.GetAsync("/api/me").ExpectStatus(401);
        await factory.Anonymous().PostAsync("/api/auth/login", new { login = phone, password = "Passw0rd!" })
            .ExpectStatus(403, "account_suspended");
    }
}
