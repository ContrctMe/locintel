namespace LocIntel.Platform.Kernel;

/// <summary>
/// A row two orgs can both see: the owner and a named counterparty (a
/// request and its vendor, a referral and its recipient, a shared case). The
/// convention gives it a "Tenant" query filter matching EITHER side, and
/// <c>EnableTwoPartyRls</c> writes the matching database policy.
///
/// It exists because <see cref="IOrgScoped"/> only expresses single-owner
/// tenancy, so every cross-org table meant hand-writing a query filter AND a
/// raw-SQL policy - which a fork did four times. Hand-rolled cross-org
/// policies are precisely where a tenant-isolation bug hides, so the shape
/// belongs in one reviewed, tested place.
///
/// Counterparty is nullable: a row often starts owner-only (a draft) and
/// gains its counterparty later.
/// </summary>
public interface ITwoPartyScoped
{
    OrgId OrgId { get; }
    OrgId? CounterpartyOrgId { get; }
}

/// <summary>
/// A row its owner controls but any org may read once published (a public
/// catalog listing, a directory profile). Reads are open to all tenants;
/// writes stay with the owner - two policies, never one.
/// </summary>
public interface IPublishedCatalogScoped
{
    OrgId OrgId { get; }
    bool Published { get; }
}
