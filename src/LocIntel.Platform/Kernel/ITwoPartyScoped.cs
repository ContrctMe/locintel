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
/// The two-party shape when BOTH parties are always present - a quote (there
/// is no quote without a vendor), a recipient row, an accepted referral.
///
/// It is a separate interface rather than a non-nullable override because
/// C# cannot narrow a property's type in a derived interface without
/// explicit implementation. Forks that used <see cref="ITwoPartyScoped"/>
/// for a NOT NULL column ended up carrying `required OrgId?` plus
/// `.IsRequired()` plus a null-forgiving accessor on every such entity -
/// three pieces of ceremony and a `!` that lies about the model. Implementing
/// this instead costs nothing:
///
/// <code>
/// public sealed class Quote : IRequiredCounterpartyScoped
/// {
///     public required OrgId OrgId { get; init; }
///     public required OrgId CounterpartyOrgId { get; init; }
/// }
/// </code>
///
/// The database policy is identical either way - <c>EnableTwoPartyRls</c>
/// does not care about nullability - so this is purely about the model
/// telling the truth.
/// </summary>
public interface IRequiredCounterpartyScoped
{
    OrgId OrgId { get; }
    OrgId CounterpartyOrgId { get; }
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
