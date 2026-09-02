using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Incidents.Incidents;

/// <summary>
/// Who is acting, for writes: a person in their active org, or an API key
/// (a service principal OF an org, ADR 40) - connectors file incidents too.
/// Guests and contacts never write here; tips arrive through their own
/// public endpoint later.
/// </summary>
public readonly record struct IncidentActor(OrgId Org, Guid Id, string Tier)
{
    public static IncidentActor? From(Principal principal) =>
        principal switch
        {
            Principal.User { ActiveOrg: { } org, UserId: var id } => new IncidentActor(
                org,
                id,
                "user"
            ),
            Principal.Service service => new IncidentActor(service.Org, service.KeyId, "service"),
            _ => null,
        };

    public IncidentSource Source => Tier == "service" ? IncidentSource.Api : IncidentSource.Console;
}
