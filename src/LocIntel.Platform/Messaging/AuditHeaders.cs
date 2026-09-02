namespace LocIntel.Platform.Messaging;

/// <summary>
/// The envelope headers carrying audit attribution (ADR 13/24). THE only
/// place these strings are spelled: the writer (AuditPublishing) and the
/// reader (the audit module's handlers) share them, and an architecture test
/// fails on the literal appearing anywhere else.
///
/// A typo in a hand-written header does not fail anything - the record simply
/// lands unattributed, which is the quietest possible way to lose an audit
/// trail, so the literal is worth removing entirely rather than reviewing.
/// </summary>
public static class AuditHeaders
{
    public const string Tier = "locintel-actor-tier";
    public const string ActorId = "locintel-actor-id";
}
