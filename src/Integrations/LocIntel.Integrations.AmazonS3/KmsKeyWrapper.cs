using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using LocIntel.Platform.Secrets;
using Microsoft.Extensions.Options;

namespace LocIntel.Integrations.AmazonS3;

public sealed class KmsOptions
{
    /// <summary>KMS key id, ARN, or alias (e.g. alias/locintel-secrets).</summary>
    public required string KeyId { get; set; }

    /// <summary>Point at LocalStack for dev/test; null = real AWS.</summary>
    public string? ServiceUrl { get; set; }
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
}

/// <summary>
/// AWS KMS implementation of the key-wrapping seam (ADR 31): the data key is
/// wrapped by an HSM-backed key that never leaves KMS. The same code path
/// runs against LocalStack in the adapter smoke - no mocks.
/// </summary>
public sealed class KmsKeyWrapper : IKeyWrapper
{
    private readonly IAmazonKeyManagementService _client;
    private readonly string _keyId;

    public KmsKeyWrapper(IOptions<KmsOptions> options)
    {
        _keyId = options.Value.KeyId;
        var config = new AmazonKeyManagementServiceConfig();
        if (options.Value.ServiceUrl is { } serviceUrl)
        {
            config.ServiceURL = serviceUrl;
            config.UseHttp = serviceUrl.StartsWith("http://");
            config.AuthenticationRegion = "us-east-1";
        }
        _client = options.Value.AccessKey is { } accessKey
            ? new AmazonKeyManagementServiceClient(accessKey, options.Value.SecretKey, config)
            : new AmazonKeyManagementServiceClient(config);
    }

    public async ValueTask<byte[]> WrapAsync(byte[] dataKey, CancellationToken ct = default)
    {
        using var plaintext = new MemoryStream(dataKey);
        var response = await _client.EncryptAsync(
            new EncryptRequest { KeyId = _keyId, Plaintext = plaintext },
            ct
        );
        return response.CiphertextBlob.ToArray();
    }

    public async ValueTask<byte[]> UnwrapAsync(byte[] wrappedKey, CancellationToken ct = default)
    {
        using var cipher = new MemoryStream(wrappedKey);
        var response = await _client.DecryptAsync(
            new DecryptRequest { KeyId = _keyId, CiphertextBlob = cipher },
            ct
        );
        return response.Plaintext.ToArray();
    }
}
