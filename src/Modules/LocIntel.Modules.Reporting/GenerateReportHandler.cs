using LocIntel.Platform.Kernel;
using Wolverine.Attributes;

namespace LocIntel.Modules.Reporting;

public static class GenerateReportHandler
{
    [NonTransactional]
    public static Task Handle(
        GenerateReport message,
        ITenantContext tenant,
        ReportRunner runner,
        CancellationToken ct
    ) =>
        runner.RunAsync(
            tenant.OrgId
                ?? throw new InvalidOperationException("Report message requires a tenant."),
            tenant.Region,
            message.JobId,
            ct
        );
}
