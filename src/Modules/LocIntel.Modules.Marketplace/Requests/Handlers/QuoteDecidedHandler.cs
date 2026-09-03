using LocIntel.Modules.Marketplace.Data;
using LocIntel.Modules.Marketplace.Marketplace;
using LocIntel.Modules.Marketplace.Requests.Messages;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Marketplace.Requests.Handlers;

/// <summary>Runs as the VENDOR: its own quote learns the outcome (the assignment learns it from RequestStateChanged).</summary>
public static class QuoteDecidedHandler
{
    [Transactional(typeof(MarketplaceDbContext))]
    public static async Task Handle(
        QuoteDecided m,
        Envelope envelope,
        ITenantContext tenant,
        MarketplaceDbContext db,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is null)
            throw new InvalidOperationException(
                $"QuoteDecided arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );
        var quote = await db.Quotes.FirstOrDefaultAsync(q => q.Id == m.QuoteId, ct);
        if (quote is null)
            return;
        quote.Status = m.Accepted ? QuoteStatus.Accepted : QuoteStatus.Rejected;
        quote.UpdatedAt = m.At;
        await db.SaveChangesAsync(ct);
    }
}
