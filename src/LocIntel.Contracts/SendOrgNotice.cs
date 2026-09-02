namespace LocIntel.Contracts;

/// <summary>
/// Cross-module notification (ADR 32): "tell the organization" about a
/// domain event (a vendor's request, a share invitation, a critical
/// incident). Handled by Identity - the one module that knows who holds
/// which capability and how to reach them. Email goes to org managers as
/// before; when Kind names a channel and Sms carries a short text, members
/// who opted in to that kind (and hold its capability) also get an SMS.
/// Tenant rides the envelope; publishers never resolve recipients.
/// </summary>
public sealed record SendOrgNotice(
    string Subject,
    string[] BodyLines,
    string Kind = "general",
    string? Sms = null
);
