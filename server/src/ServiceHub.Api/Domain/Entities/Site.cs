namespace ServiceHub.Api.Domain.Entities;

/// <summary>
/// Editable copy and layout for a public page (currently only "home"), stored as one JSON document.
/// The client owns the shape and merges it over its built-in defaults, so a missing row means "use defaults".
/// </summary>
public class SiteContent : ITimestamped
{
    public string Key { get; set; } = "";
    public string Content { get; set; } = "{}";
    public Guid? UpdatedById { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
