namespace ARC.Web.Services;

public sealed class ArcWebBusinessChatOptions
{
    public const string SectionName = "ArcWeb:BusinessChat";

    public string ApiBaseUrl { get; set; } = "http://localhost:5187";
    public string ActorUpn { get; set; } = "demo.manager@arc.local";
    public string ActorRole { get; set; } = "Finance";
    public string? ActorRegion { get; set; }
    public string? ActorDepot { get; set; }
}
