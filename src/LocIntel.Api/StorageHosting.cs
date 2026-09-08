using LocIntel.Modules.Storage;
using LocIntel.Platform.Secrets;
using LocIntel.Platform.Storage;
using static LocIntel.Api.ProviderOptionsValidation;

namespace LocIntel.Api;

internal static class StorageHosting
{
    public static string AddStorageHosting(this WebApplicationBuilder builder)
    {
        // Object storage (ADR 19): selected by config like every other seam. The
        // local adapter keeps tickets in process memory and bytes on local disk -
        // dev/test only, and it is what the maturity review found registered
        // unconditionally while the production guide claimed a guard. Both cloud
        // adapters are smoke-tested against MinIO/Azurite in the integration suite.
        var storageProvider = builder.Configuration["Storage:Provider"] ?? "local";
        switch (storageProvider)
        {
            case "s3":
                builder
                    .Services.AddOptions<LocIntel.Integrations.AmazonS3.S3Options>()
                    .Bind(builder.Configuration.GetSection("Storage:S3"))
                    .Validate(
                        o => !string.IsNullOrWhiteSpace(o.BucketName),
                        "Storage:S3:BucketName is required."
                    )
                    .Validate(
                        o => IsProviderUrl(o.ServiceUrl, builder.Environment),
                        "Storage:S3:ServiceUrl must be an absolute HTTPS URL outside Development/Testing."
                    )
                    .Validate(
                        o => CredentialsMatch(o.AccessKey, o.SecretKey),
                        "Storage:S3:AccessKey and SecretKey must be configured together."
                    )
                    .ValidateOnStart();
                builder.Services.AddSingleton<
                    IObjectStore,
                    LocIntel.Integrations.AmazonS3.S3ObjectStore
                >();
                break;
            case "azure":
                builder
                    .Services.AddOptions<LocIntel.Integrations.AzureBlob.AzureBlobOptions>()
                    .Bind(builder.Configuration.GetSection("Storage:Azure"))
                    .Validate(
                        o => !string.IsNullOrWhiteSpace(o.ConnectionString),
                        "Storage:Azure:ConnectionString is required."
                    )
                    .Validate(
                        o => !string.IsNullOrWhiteSpace(o.ContainerName),
                        "Storage:Azure:ContainerName is required."
                    )
                    .Validate(
                        o =>
                        {
                            try
                            {
                                var client = new Azure.Storage.Blobs.BlobContainerClient(
                                    o.ConnectionString,
                                    o.ContainerName
                                );
                                return IsDevelopmentOrTesting(builder.Environment)
                                    || client.Uri.Scheme == "https";
                            }
                            catch (Exception)
                            {
                                return false;
                            }
                        },
                        "Storage:Azure configuration is malformed or uses a non-HTTPS endpoint outside Development/Testing."
                    )
                    .ValidateOnStart();
                builder.Services.AddSingleton<
                    IObjectStore,
                    LocIntel.Integrations.AzureBlob.AzureBlobObjectStore
                >();
                break;
            case "local" when IsDevelopmentOrTesting(builder.Environment):
                builder.Services.AddSingleton<IObjectStore, LocalObjectStore>();
                break;
            default:
                throw new InvalidOperationException(
                    $"Storage:Provider '{storageProvider}' is not valid for {builder.Environment.EnvironmentName}. "
                        + "Use 's3' or 'azure' in Production; 'local' is dev/test only (ADR 19)."
                );
        }

        // Malware scanning (ADR 19: quarantine + scan before visibility). The EICAR
        // scanner reads 128 KiB and knows one signature - dev/test only. clamd is
        // the built-in production scanner; a fork with a commercial one implements
        // IVirusScanner behind the same port and adds a case here.
        var scannerProvider = builder.Configuration["Scanner:Provider"] ?? "eicar";
        switch (scannerProvider)
        {
            case "clamav":
                builder
                    .Services.AddOptions<LocIntel.Integrations.ClamAV.ClamAvOptions>()
                    .Bind(builder.Configuration.GetSection("Scanner:ClamAv"))
                    .Validate(
                        o => !string.IsNullOrWhiteSpace(o.Host),
                        "Scanner:ClamAv:Host is required."
                    )
                    .Validate(
                        o => o.Port is >= 1 and <= 65535,
                        "Scanner:ClamAv:Port must be between 1 and 65535."
                    )
                    .Validate(
                        o => o.TimeoutSeconds > 0,
                        "Scanner:ClamAv:TimeoutSeconds must be positive."
                    )
                    .ValidateOnStart();
                builder.Services.AddSingleton<
                    IVirusScanner,
                    LocIntel.Integrations.ClamAV.ClamAvScanner
                >();
                break;
            case "eicar" when IsDevelopmentOrTesting(builder.Environment):
                builder.Services.AddSingleton<IVirusScanner, EicarScanner>();
                break;
            default:
                throw new InvalidOperationException(
                    $"Scanner:Provider '{scannerProvider}' is not valid for {builder.Environment.EnvironmentName}. "
                        + "Use 'clamav' or a fork adapter in Production; 'eicar' is dev/test only (ADR 19)."
                );
        }

        // Secrets (ADR 31): the local wrapper is DEV/TEST ONLY. Before this switch
        // the KMS adapter had no registration path at all - a Production boot with
        // the documented configuration registered no IKeyWrapper and failed on the
        // first secret. Secrets:Provider defaults to 'local' when a local master key
        // is configured (the existing dev/test shape) and to 'kms' otherwise.
        var secretsProvider =
            builder.Configuration["Secrets:Provider"]
            ?? (builder.Configuration["Secrets:LocalMasterKey"] is null ? "kms" : "local");
        switch (secretsProvider)
        {
            case "kms":
                builder
                    .Services.AddOptions<LocIntel.Integrations.AmazonS3.KmsOptions>()
                    .Bind(builder.Configuration.GetSection("Secrets:Kms"))
                    .Validate(
                        o => !string.IsNullOrWhiteSpace(o.KeyId),
                        "Secrets:Kms:KeyId is required."
                    )
                    .Validate(
                        o => IsProviderUrl(o.ServiceUrl, builder.Environment),
                        "Secrets:Kms:ServiceUrl must be an absolute HTTPS URL outside Development/Testing."
                    )
                    .Validate(
                        o => CredentialsMatch(o.AccessKey, o.SecretKey),
                        "Secrets:Kms:AccessKey and SecretKey must be configured together."
                    )
                    .ValidateOnStart();
                builder.Services.AddSingleton<
                    IKeyWrapper,
                    LocIntel.Integrations.AmazonS3.KmsKeyWrapper
                >();
                break;
            case "local" when IsDevelopmentOrTesting(builder.Environment):
                builder.Services.AddSingleton<IKeyWrapper>(
                    new LocalKeyWrapper(
                        Convert.FromBase64String(
                            builder.Configuration["Secrets:LocalMasterKey"]
                                ?? throw new InvalidOperationException(
                                    "Secrets:Provider 'local' needs Secrets:LocalMasterKey (base64, 32 bytes)."
                                )
                        )
                    )
                );
                break;
            default:
                throw new InvalidOperationException(
                    $"Secrets:Provider '{secretsProvider}' is not valid for {builder.Environment.EnvironmentName}. "
                        + "Use 'kms' or a fork adapter in Production; 'local' is dev/test only (ADR 31)."
                );
        }
        return storageProvider;
    }
}
