namespace LocIntel.Platform.Auth;

/// <summary>
/// Optional capability (ADR 41): the provider hosts the IT-admin UI for SSO
/// and directory-sync configuration. We only mint a short-lived link scoped
/// to the org's external id and redirect - IdP metadata and SCIM credentials
/// never touch our code (same shape as the hosted billing pages, ADR 39).
/// </summary>
public interface IAdminPortal
{
    Task<Uri> GeneratePortalLinkAsync(
        string externalOrgId,
        AdminPortalIntent intent,
        string returnUrl,
        CancellationToken ct = default
    );
}
