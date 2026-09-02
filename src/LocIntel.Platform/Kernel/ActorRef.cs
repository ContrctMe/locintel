namespace LocIntel.Platform.Kernel;

/// <summary>
/// Who is writing, for modules whose writes accept both people and API keys
/// (a service principal OF an org, ADR 40). Guests and contacts never
/// resolve here - their surfaces are their own endpoints. Org rides along
/// so it is never ambient on the audit envelope.
/// </summary>
public readonly record struct ActorRef(OrgId Org, Guid Id, string Tier)
{
    public static ActorRef? From(Principal principal) =>
        principal switch
        {
            Principal.User { ActiveOrg: { } org, UserId: var id } => new ActorRef(org, id, "user"),
            Principal.Service service => new ActorRef(service.Org, service.KeyId, "service"),
            _ => null,
        };

    public bool IsService => Tier == "service";
}
