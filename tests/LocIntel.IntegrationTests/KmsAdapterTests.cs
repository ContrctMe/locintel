using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Microsoft.Extensions.Options;
using LocIntel.Integrations.AmazonS3;
using LocIntel.Platform.Secrets;
using Testcontainers.LocalStack;

namespace LocIntel.IntegrationTests;

/// <summary>
/// Smokes the REAL KMS adapter against LocalStack: the same Encrypt/Decrypt
/// code path production uses against AWS KMS - no mocks (ADR 31/38).
/// </summary>
public sealed class LocalStackFixture : IAsyncLifetime
{
    private readonly LocalStackContainer _localstack = new LocalStackBuilder(
        "localstack/localstack:4"
    ).Build();

    public KmsKeyWrapper Wrapper { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _localstack.StartAsync();
        var serviceUrl = _localstack.GetConnectionString();
        using var admin = new AmazonKeyManagementServiceClient(
            "test",
            "test",
            new AmazonKeyManagementServiceConfig
            {
                ServiceURL = serviceUrl,
                UseHttp = serviceUrl.StartsWith("http://"),
                AuthenticationRegion = "us-east-1",
            }
        );
        var key = await admin.CreateKeyAsync(
            new CreateKeyRequest { Description = "locintel adapter smoke" }
        );
        Wrapper = new KmsKeyWrapper(
            Options.Create(
                new KmsOptions
                {
                    KeyId = key.KeyMetadata.KeyId,
                    ServiceUrl = serviceUrl,
                    AccessKey = "test",
                    SecretKey = "test",
                }
            )
        );
    }

    public async Task DisposeAsync() => await _localstack.DisposeAsync();
}

public class KmsAdapterTests(LocalStackFixture fixture) : IClassFixture<LocalStackFixture>
{
    [Fact]
    public async Task Wrap_unwrap_round_trips_through_the_hsm_seam()
    {
        var dataKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var wrapped = await fixture.Wrapper.WrapAsync(dataKey);
        Assert.NotEqual(dataKey, wrapped); // the wrapped form is KMS ciphertext
        Assert.Equal(dataKey, await fixture.Wrapper.UnwrapAsync(wrapped));
    }

    [Fact]
    public async Task Envelope_crypto_runs_end_to_end_over_kms()
    {
        // the exact path connector credentials take (ADR 31)
        var blob = await EnvelopeCrypto.EncryptAsync("sk-connector-secret", fixture.Wrapper);
        Assert.Equal(
            "sk-connector-secret",
            await EnvelopeCrypto.DecryptAsync(blob, fixture.Wrapper)
        );
    }
}
