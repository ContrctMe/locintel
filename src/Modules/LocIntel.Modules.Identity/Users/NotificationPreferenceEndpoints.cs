using System.Text.RegularExpressions;
using LocIntel.Contracts;
using LocIntel.Modules.Identity.Data;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Identity.Users;

public sealed record NotificationPreferenceView(
    string? Phone,
    bool SmsAlerts,
    bool SmsMarketplace,
    bool SmsNetwork,
    bool BrowserAlerts,
    bool SmsAvailable
);

public sealed record UpdateNotificationPreferenceRequest(
    string? Phone,
    bool SmsAlerts,
    bool SmsMarketplace,
    bool SmsNetwork,
    bool BrowserAlerts
);

/// <summary>The member's own reachability in the active org: read and replace.</summary>
public static class NotificationPreferenceEndpoints
{
    private static readonly Regex E164 = new(@"^\+[1-9]\d{6,14}$", RegexOptions.Compiled);

    [Transactional(
        typeof(IdentityDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/me/notifications")]
    [ProducesResponseType(typeof(NotificationPreferenceView), StatusCodes.Status200OK)]
    public static async Task<IResult> Get(
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        CancellationToken ct
    )
    {
        if (accessor.Current is not Principal.User { ActiveOrg: { } org, UserId: var userId })
            return Results.Unauthorized();
        var pref = await db.NotificationPreferences.FirstOrDefaultAsync(
            p => p.UserId == userId,
            ct
        );
        return Results.Ok(View(pref, SmsAvailable(configuration)));
    }

    [Transactional(typeof(IdentityDbContext))]
    [WolverinePut("/api/me/notifications")]
    [ProducesResponseType(typeof(NotificationPreferenceView), StatusCodes.Status200OK)]
    public static async Task<IResult> Update(
        UpdateNotificationPreferenceRequest request,
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        CancellationToken ct
    )
    {
        if (accessor.Current is not Principal.User { ActiveOrg: { } org, UserId: var userId })
            return Results.Unauthorized();
        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        if (phone is not null && !E164.IsMatch(phone))
            return ApiErrors.BadRequest("phone must be E.164, like +14155551234");
        if (phone is null && (request.SmsAlerts || request.SmsMarketplace || request.SmsNetwork))
            return ApiErrors.BadRequest("add a phone number to receive SMS");
        var pref = await db.NotificationPreferences.FirstOrDefaultAsync(
            p => p.UserId == userId,
            ct
        );
        if (pref is null)
        {
            pref = new NotificationPreference
            {
                Id = Guid.CreateVersion7(),
                OrgId = org,
                UserId = userId,
            };
            db.NotificationPreferences.Add(pref);
        }
        pref.Phone = phone;
        pref.SmsAlerts = request.SmsAlerts;
        pref.SmsMarketplace = request.SmsMarketplace;
        pref.SmsNetwork = request.SmsNetwork;
        pref.BrowserAlerts = request.BrowserAlerts;
        pref.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(View(pref, SmsAvailable(configuration)));
    }

    private static bool SmsAvailable(
        Microsoft.Extensions.Configuration.IConfiguration configuration
    ) => (configuration["Notifications:Sms"] ?? "off") != "off";

    private static NotificationPreferenceView View(NotificationPreference? p, bool smsAvailable) =>
        new(
            p?.Phone,
            p?.SmsAlerts ?? false,
            p?.SmsMarketplace ?? false,
            p?.SmsNetwork ?? false,
            p?.BrowserAlerts ?? false,
            smsAvailable
        );
}
