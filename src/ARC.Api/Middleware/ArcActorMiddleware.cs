using System.Security.Claims;
using System.Text.Json;
using ARC.Domain.Enums;
using ARC.Domain.Rules;
using Microsoft.Extensions.Hosting;

namespace ARC.Api.Auth;

/// <summary>
/// Server-side actor. JWT claims when Entra is configured; otherwise Development headers.
/// Role is never taken from the gate POST body.
/// </summary>
public sealed class ArcActorMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<ArcActorMiddleware> _logger;

    public ArcActorMiddleware(RequestDelegate next, IHostEnvironment environment, ILogger<ArcActorMiddleware> logger)
    {
        _next = next;
        _environment = environment;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        var actor = FromUser(context.User) ?? (_environment.IsDevelopment() ? FromHeaders(context.Request) : null);
        if (actor is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Sign in required." }));
            return;
        }

        if (!R4SegregationOfDuties.CanApprove(actor.Role) && !HttpMethods.IsGet(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "R4: the recommending agent cannot approve." }));
            return;
        }

        if (actor.Role == ActorRole.Tsi && string.IsNullOrWhiteSpace(actor.Region))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "TSI region is required for server-side isolation." }));
            return;
        }

        if (actor.Role == ActorRole.DepotManager && string.IsNullOrWhiteSpace(actor.Depot))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Depot Manager depot is required for server-side isolation." }));
            return;
        }

        context.Items[ArcActorHttp.ItemKey] = actor;
        _logger.LogInformation("ARC actor {Upn} role {Role} region {Region}", actor.Upn, actor.Role, actor.Region);
        await _next(context);
    }

    private static ArcActor? FromUser(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
            return null;

        var upn = user.FindFirstValue("preferred_username")
            ?? user.FindFirstValue(ClaimTypes.Upn)
            ?? user.FindFirstValue(ClaimTypes.Email)
            ?? user.Identity.Name;
        var roleValue = user.FindFirstValue("ArcRole")
            ?? user.FindFirstValue(ClaimTypes.Role)
            ?? user.FindFirstValue("roles");
        if (string.IsNullOrWhiteSpace(upn) || string.IsNullOrWhiteSpace(roleValue))
            return null;
        if (!Enum.TryParse<ActorRole>(roleValue, ignoreCase: true, out var role))
            return null;

        return new ArcActor(
            upn,
            role,
            user.FindFirstValue("ArcRegion") ?? user.FindFirstValue("region"),
            user.FindFirstValue("ArcDepot") ?? user.FindFirstValue("depot"));
    }

    private static ArcActor? FromHeaders(HttpRequest request)
    {
        if (!request.Headers.TryGetValue(ArcActorHttp.UpnHeader, out var upnValues)
            || !request.Headers.TryGetValue(ArcActorHttp.RoleHeader, out var roleValues))
            return null;

        var upn = upnValues.ToString();
        if (string.IsNullOrWhiteSpace(upn) || !Enum.TryParse<ActorRole>(roleValues.ToString(), ignoreCase: true, out var role))
            return null;

        request.Headers.TryGetValue(ArcActorHttp.RegionHeader, out var region);
        request.Headers.TryGetValue(ArcActorHttp.DepotHeader, out var depot);
        return new ArcActor(upn, role, EmptyToNull(region.ToString()), EmptyToNull(depot.ToString()));
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
