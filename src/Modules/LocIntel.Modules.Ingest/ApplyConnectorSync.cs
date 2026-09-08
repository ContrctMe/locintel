using System.Security.Cryptography;
using System.Text.Json;
using LocIntel.Modules.Ingest.Data;

namespace LocIntel.Modules.Ingest;

public sealed record ApplyConnectorSync(
    Guid ConnectorId,
    Guid BatchId,
    string Fingerprint,
    SourceRow[] Rows
)
{
    public static string SnapshotFingerprint(SiteConnector connector) =>
        Convert.ToHexString(
            SHA256.HashData(
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        connector.Name,
                        connector.Type,
                        connector.Url,
                        connector.EncryptedCredentials,
                        connector.SyncIntervalHours,
                        connector.LastSyncedAt,
                    }
                )
            )
        );
}
