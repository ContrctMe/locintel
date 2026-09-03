using LocIntel.Contracts;
using LocIntel.Modules.Marketplace.Data;
using LocIntel.Modules.Marketplace.Marketplace;
using LocIntel.Modules.Marketplace.Requests.Messages;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Marketplace.Requests.Handlers;

/// <summary>
/// Runs as the REQUESTER: arbitrates a vendor's action against the request's
/// current state (ADR 48: authority lives on the owned object), records the
/// timeline entry, notifies the buyer, and answers every participant with
/// the resulting state - which is what corrects a vendor's optimistic row
/// when the action no longer applied.
/// </summary>
public static class VendorRespondedHandler
{
    [Transactional(typeof(MarketplaceDbContext))]
    public static async Task Handle(
        VendorResponded m,
        Envelope envelope,
        ITenantContext tenant,
        MarketplaceDbContext db,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"VendorResponded arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );
        await db.TakeAsync(m.RequestId, ct);
        var row = await db.Requests.FirstOrDefaultAsync(r => r.Id == m.RequestId, ct);
        if (row is null)
            return;
        var applied = Apply(row, m);
        if (m.Response == VendorResponse.DeclinedToQuote)
        {
            // the recipient list is the requester's data: mirror the vendor's standing
            var recipient = await db.Recipients.FirstOrDefaultAsync(
                x => x.RequestId == row.Id && x.VendorOrgId == m.VendorOrgId,
                ct
            );
            if (recipient is not null && recipient.Status != RecipientStatus.Declined)
            {
                recipient.Status = RecipientStatus.Declined;
                recipient.RespondedAt = m.At;
            }
            var quote = await db.ReceivedQuotes.FirstOrDefaultAsync(
                q =>
                    q.RequestId == row.Id
                    && q.VendorOrgId == m.VendorOrgId
                    && q.Status == QuoteStatus.Submitted,
                ct
            );
            if (quote is not null)
                quote.Status = QuoteStatus.Withdrawn;
        }
        string? auditEvent = null;
        string? subject = null;
        string[] lines = [];
        if (applied)
        {
            if (!await db.Events.AnyAsync(e => e.SourceId == m.SourceId, ct))
                db.Events.Add(
                    new RequestEvent
                    {
                        Id = Guid.CreateVersion7(),
                        OrgId = org,
                        SourceId = m.SourceId,
                        RequestId = row.Id,
                        ActorOrgId = m.VendorOrgId,
                        ActorId = m.ActorId,
                        Kind =
                            m.Response == VendorResponse.DeclinedToQuote
                                ? RequestEventKind.Message
                                : RequestEventKind.StatusChange,
                        Body = Body(m),
                        At = m.At,
                    }
                );
            (auditEvent, subject, lines) = m.Response switch
            {
                VendorResponse.Accepted => (
                    "marketplace.request_accepted",
                    $"Request accepted: {row.Title}",
                    new[] { $"{m.VendorName} accepted the request for {row.SiteName}." }
                ),
                VendorResponse.Declined => (
                    "marketplace.request_declined",
                    $"Request declined: {row.Title}",
                    new[] { $"{m.VendorName} declined: {m.Text}" }
                ),
                VendorResponse.Started => (
                    "marketplace.request_started",
                    $"Work started: {row.Title}",
                    new[] { $"{m.VendorName} started work at {row.SiteName}." }
                ),
                VendorResponse.Completed => (
                    "marketplace.request_completed",
                    $"Work completed: {row.Title}",
                    new[]
                    {
                        $"{m.VendorName} reports the work at {row.SiteName} complete.",
                        "Verify or dispute it in the console.",
                    }
                ),
                _ => (null, null, Array.Empty<string>()),
            };
        }
        await db.SaveChangesAsync(ct);
        if (subject is not null)
            await bus.PublishForOrgAsync(
                org,
                new SendOrgNotice(subject, lines, "marketplace", subject)
            );
        if (auditEvent is not null)
            await bus.AuditAsync(
                org,
                AuditActor.System,
                auditEvent,
                new
                {
                    row.Id,
                    VendorOrgId = m.VendorOrgId.Value,
                    Status = row.Status.ToString(),
                }
            );
        // the requester's state is the answer, applied or not
        await RequestFanOut.StateAsync(db, bus, row, ct);
    }

    /// <summary>
    /// The transition, if it still applies. Vendor actions may arrive out of
    /// order (Started before Accepted on a busy outbox), so an action is read
    /// as "the vendor reached this point": it applies when the request is at
    /// or before the step it implies, and a later-arriving earlier step is
    /// simply stale. Decline applies only to an open request.
    /// </summary>
    private static bool Apply(ServiceRequest row, VendorResponded m)
    {
        var mine = row.VendorOrgId == m.VendorOrgId;
        switch (m.Response)
        {
            case VendorResponse.Accepted
                when row.Mode == RequestMode.Direct
                    && mine
                    && Rank(row.Status) < Rank(RequestStatus.Accepted)
                    && row.Status == RequestStatus.Submitted:
                row.Status = RequestStatus.Accepted;
                row.AcceptedAt = m.At;
                break;
            case VendorResponse.Declined
                when row.Mode == RequestMode.Direct
                    && mine
                    && row.Status == RequestStatus.Submitted:
                row.Status = RequestStatus.Declined;
                row.DeclineReason = m.Text;
                break;
            case VendorResponse.DeclinedToQuote
                when row.Mode == RequestMode.Broadcast && row.Status == RequestStatus.Submitted:
                break; // the recipient row is mirrored above; the request stays open
            case VendorResponse.Started
                when mine && row.Status is RequestStatus.Submitted or RequestStatus.Accepted:
                row.AcceptedAt ??= m.At;
                row.Status = RequestStatus.InProgress;
                row.StartedAt = m.At;
                break;
            case VendorResponse.Completed
                when mine
                    && row.Status
                        is RequestStatus.Submitted
                            or RequestStatus.Accepted
                            or RequestStatus.InProgress:
                row.AcceptedAt ??= m.At;
                row.StartedAt ??= m.At;
                row.Status = RequestStatus.Completed;
                row.CompletedAt = m.At;
                row.CompletionSummary = m.Text;
                break;
            default:
                return false;
        }
        row.UpdatedAt = m.At;
        return true;
    }

    private static int Rank(RequestStatus status) =>
        status switch
        {
            RequestStatus.Draft => 0,
            RequestStatus.Submitted => 1,
            RequestStatus.Accepted => 2,
            RequestStatus.InProgress => 3,
            RequestStatus.Completed => 4,
            _ => 9,
        };

    private static string Body(VendorResponded m) =>
        m.Response switch
        {
            VendorResponse.Accepted => "Accepted",
            VendorResponse.Declined => $"Declined: {m.Text}",
            VendorResponse.DeclinedToQuote => $"Declined to quote: {m.Text}",
            VendorResponse.Started => "Started",
            VendorResponse.Completed => m.Text is null ? "Completed" : $"Completed: {m.Text}",
            _ => m.Response.ToString(),
        };
}
