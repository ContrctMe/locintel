using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Storage.Data;

/// <summary>Durable purge fence. Retained to reject delayed publication after organization offboarding.</summary>
public sealed class PurgedFileOrganization : IOrgScoped
{
    public required OrgId OrgId { get; init; }
}
