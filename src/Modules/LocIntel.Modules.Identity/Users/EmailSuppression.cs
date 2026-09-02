namespace LocIntel.Modules.Identity.Users;

/// <summary>
/// The bounce suppression list (ADR 32). Platform-global on purpose: an
/// undeliverable address is undeliverable for every org, and rows arrive
/// from the provider's bounce webhook before any tenant context exists.
/// Hard-delete tier (ADR 25): a row is operational state, not a record -
/// removing one re-enables sending, which is the intended "unsuppress".
/// </summary>
public sealed class EmailSuppression
{
    public required Guid Id { get; init; }
    public required string Email { get; init; }

    /// <summary>"bounce" or "complaint" - whatever the provider reported.</summary>
    public required string Reason { get; init; }

    /// <summary>UTC instant (ADR 26): when the provider reported it.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
