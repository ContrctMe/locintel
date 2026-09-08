using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Tenancy.Hierarchy;

/// <summary>
/// Sent by org creation, delivered under the NEW org's tenant (the creating
/// request runs under the founder's previous org, or none, so its own
/// connection could not write the new org's rows past RLS). Idempotent: an
/// org that already has an authoritative hierarchy is left alone.
/// </summary>
public sealed record ProvisionDefaultHierarchy(OrgId OrgId);
