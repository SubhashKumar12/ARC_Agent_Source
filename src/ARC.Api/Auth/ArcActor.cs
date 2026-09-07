using ARC.Domain.Enums;

namespace ARC.Api.Auth;

public sealed record ArcActor(string Upn, ActorRole Role, string? Region, string? Depot);

public static class ArcActorHttp
{
    public const string ItemKey = "ArcActor";
    public const string UpnHeader = "X-Arc-Upn";
    public const string RoleHeader = "X-Arc-Role";
    public const string RegionHeader = "X-Arc-Region";
    public const string DepotHeader = "X-Arc-Depot";

    public static ArcActor GetRequired(HttpContext context)
        => context.Items[ItemKey] as ArcActor
            ?? throw new InvalidOperationException("Authenticated ARC actor is missing.");
}
