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

/// <summary>Runs as the REQUESTER: projects the vendor's quote and marks the recipient as having quoted.</summary>
public static class QuoteSubmittedHandler
{
    [Transactional(typeof(MarketplaceDbContext))]
    public static async Task Handle(
        QuoteSubmitted m,
        Envelope envelope,
        ITenantContext tenant,
        MarketplaceDbContext db,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"QuoteSubmitted arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );
        await db.TakeAsync(m.RequestId, ct);
        var row = await db.Requests.FirstOrDefaultAsync(r => r.Id == m.RequestId, ct);
        if (row is null || row.Mode != RequestMode.Broadcast)
            return;
        var quote = await db.ReceivedQuotes.FirstOrDefaultAsync(q => q.QuoteId == m.QuoteId, ct);
        if (quote is null)
        {
            quote = new QuoteReceived
            {
                Id = Guid.CreateVersion7(),
                OrgId = org,
                RequestId = m.RequestId,
                QuoteId = m.QuoteId,
                VendorOrgId = m.VendorOrgId,
                VendorName = m.VendorName,
                Amount = m.Amount,
                Currency = m.Currency,
            };
            db.ReceivedQuotes.Add(quote);
        }
        else if (quote.Status is not (QuoteStatus.Submitted or QuoteStatus.Withdrawn))
            return; // decided already: a late resubmission changes nothing
        else if (quote.SubmittedAt > m.SubmittedAt)
            return; // a resubmission that overtook its predecessor on the outbox: keep the newer
        quote.VendorName = m.VendorName;
        quote.Amount = m.Amount;
        quote.Notes = m.Notes;
        quote.ValidUntil = m.ValidUntil;
        quote.Status = QuoteStatus.Submitted;
        quote.SubmittedAt = m.SubmittedAt;
        quote.UpdatedAt = m.SubmittedAt;
        var recipient = await db.Recipients.FirstOrDefaultAsync(
            x => x.RequestId == m.RequestId && x.VendorOrgId == m.VendorOrgId,
            ct
        );
        if (recipient is not null)
        {
            recipient.Status = RecipientStatus.Quoted;
            recipient.RespondedAt = m.SubmittedAt;
        }
        row.UpdatedAt = m.SubmittedAt;
        await db.SaveChangesAsync(ct);
        await bus.PublishForOrgAsync(
            org,
            new SendOrgNotice(
                $"Quote received: {row.Title}",
                [
                    $"{m.VendorName} quoted {m.Currency} {m.Amount:0.00}.",
                    "Open the console to compare quotes and award.",
                ],
                "marketplace",
                $"Quote received: {row.Title}"
            )
        );
    }
}
