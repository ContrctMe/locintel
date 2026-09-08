using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

namespace LocIntel.IntegrationTests;

/// <summary>
/// docs/production.md promises the image refuses to start in Production
/// with any dev-only adapter selected. The maturity review found two seams
/// (object storage, malware scanning) registered unconditionally behind that
/// promise, and a third (secrets) with no production registration path at
/// all. This proves the promise seam by seam: each case takes a
/// production-valid configuration and flips ONE seam back to its dev
/// adapter, and the host must refuse before it starts. The refusal happens
/// while the composition root runs, so no database or fixture is needed.
/// </summary>
public class ProductionBootGuardTests : IDisposable
{
    private readonly string KeyPath = Path.Combine(
        Path.GetTempPath(),
        $"locintel-boot-guard-{Guid.NewGuid():N}"
    );

    public ProductionBootGuardTests()
    {
        Directory.CreateDirectory(KeyPath);
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=locintel-boot-guard",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1)
        );
        File.WriteAllBytes(
            Path.Combine(KeyPath, "keyring.pfx"),
            certificate.Export(X509ContentType.Pfx)
        );
    }

    public void Dispose() => Directory.Delete(KeyPath, recursive: true);

    /// <summary>Every seam on its production adapter (registration is lazy; nothing connects).</summary>
    private Dictionary<string, string?> ProductionValid() =>
        new()
        {
            ["ROLE"] = "api",
            ["ConnectionStrings:locintel"] = "Host=localhost;Database=unused",
            ["DataProtection:KeyPath"] = KeyPath,
            ["DataProtection:CertificatePath"] = Path.Combine(KeyPath, "keyring.pfx"),
            ["AllowedHosts"] = "localhost;*.locintel.test",
            ["Auth:Provider"] = "workos",
            ["Auth:WorkOS:ApiKey"] = "sk_unused",
            ["Auth:WorkOS:ClientId"] = "client_unused",
            ["Auth:WorkOS:WebhookSecret"] = "whsec_unused",
            ["Storage:Provider"] = "s3",
            ["Storage:S3:BucketName"] = "unused",
            ["Storage:Azure:ConnectionString"] =
                "DefaultEndpointsProtocol=https;AccountName=unused;AccountKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=;EndpointSuffix=core.windows.net",
            ["Storage:Azure:ContainerName"] = "unused",
            ["Scanner:Provider"] = "clamav",
            ["Scanner:ClamAv:Host"] = "unused",
            ["Secrets:Provider"] = "kms",
            ["Secrets:Kms:KeyId"] = "unused",
            ["Billing:Provider"] = "stripe",
            ["Billing:Stripe:ApiKey"] = "sk_unused",
            ["Billing:Stripe:WebhookSecret"] = "whsec_unused",
            ["Billing:Stripe:PriceIds:growth"] = "price_unused",
            ["Billing:Stripe:PriceIds:scale"] = "price_unused",
            ["Notifications:Transport"] = "smtp",
            ["Notifications:Smtp:Host"] = "unused",
            ["Notifications:Smtp:FromAddress"] = "noreply@example.test",
            ["Notifications:Sms"] = "off",
        };

    [Theory]
    [InlineData(
        "Storage:Provider",
        "local",
        "Storage:Provider 'local' is not valid for Production"
    )]
    [InlineData(
        "Scanner:Provider",
        "eicar",
        "Scanner:Provider 'eicar' is not valid for Production"
    )]
    [InlineData(
        "Secrets:Provider",
        "local",
        "Secrets:Provider 'local' is not valid for Production"
    )]
    [InlineData("Auth:Provider", "local", "Auth:Provider 'local' is not valid for Production")]
    [InlineData("Billing:Provider", "local", "Billing:Provider 'local' is dev/test only")]
    [InlineData(
        "Notifications:Transport",
        "local",
        "Notifications:Transport 'local' is dev/test only"
    )]
    [InlineData("Notifications:Sms", "local", "Notifications:Sms 'local' is dev/test only")]
    [InlineData("DataProtection:KeyPath", null, "DataProtection:KeyPath is required in Production")]
    [InlineData("DataProtection:CertificatePath", null, "DataProtection:CertificatePath")]
    [InlineData("AllowedHosts", "*", "AllowedHosts must list")]
    [InlineData(
        "Proxy:TrustForwardedHeaders",
        "true",
        "Proxy:KnownProxies or Proxy:KnownNetworks is required"
    )]
    public void Production_refuses_to_boot_with_a_dev_adapter(
        string key,
        string? devValue,
        string refusal
    )
    {
        var settings = ProductionValid();
        settings[key] = devValue;

        var refused = BootRefusal(settings);

        Assert.NotNull(refused);
        Assert.Contains(refusal, refused.Message);
    }

    [Fact]
    public void An_unknown_role_is_refused_before_anything_else()
    {
        // a typo in a manifest used to start a host that mapped nothing
        var settings = ProductionValid();
        settings["ROLE"] = "apu";

        Assert.Contains("ROLE 'apu' is not a role this image runs", BootRefusal(settings)!.Message);
    }

    [Fact]
    public void An_unknown_provider_is_refused_in_every_environment()
    {
        // a typo must not fall through to a default adapter
        var settings = ProductionValid();
        settings["Storage:Provider"] = "s4";

        Assert.Contains("Storage:Provider 's4'", BootRefusal(settings)!.Message);
    }

    [Theory]
    [InlineData(
        "Auth:Provider",
        "workos",
        "Auth:WorkOS:ApiKey",
        null,
        "Auth:WorkOS:ApiKey is required"
    )]
    [InlineData(
        "Auth:Provider",
        "workos",
        "Auth:WorkOS:ClientId",
        null,
        "Auth:WorkOS:ClientId is required"
    )]
    [InlineData(
        "Auth:Provider",
        "workos",
        "Auth:WorkOS:ApiBaseUrl",
        "not-a-url",
        "Auth:WorkOS:ApiBaseUrl"
    )]
    [InlineData(
        "Storage:Provider",
        "s3",
        "Storage:S3:BucketName",
        null,
        "Storage:S3:BucketName is required"
    )]
    [InlineData(
        "Storage:Provider",
        "s3",
        "Storage:S3:ServiceUrl",
        "not-a-url",
        "Storage:S3:ServiceUrl"
    )]
    [InlineData(
        "Storage:Provider",
        "s3",
        "Storage:S3:AccessKey",
        "access",
        "Storage:S3:AccessKey and SecretKey"
    )]
    [InlineData(
        "Storage:Provider",
        "azure",
        "Storage:Azure:ConnectionString",
        null,
        "Storage:Azure:ConnectionString is required"
    )]
    [InlineData(
        "Storage:Provider",
        "azure",
        "Storage:Azure:ContainerName",
        null,
        "Storage:Azure:ContainerName is required"
    )]
    [InlineData(
        "Storage:Provider",
        "azure",
        "Storage:Azure:ConnectionString",
        "malformed",
        "Storage:Azure configuration is malformed"
    )]
    [InlineData(
        "Scanner:Provider",
        "clamav",
        "Scanner:ClamAv:Host",
        null,
        "Scanner:ClamAv:Host is required"
    )]
    [InlineData(
        "Scanner:Provider",
        "clamav",
        "Scanner:ClamAv:Port",
        "0",
        "Scanner:ClamAv:Port must be between"
    )]
    [InlineData(
        "Scanner:Provider",
        "clamav",
        "Scanner:ClamAv:Port",
        "65536",
        "Scanner:ClamAv:Port must be between"
    )]
    [InlineData(
        "Scanner:Provider",
        "clamav",
        "Scanner:ClamAv:TimeoutSeconds",
        "0",
        "Scanner:ClamAv:TimeoutSeconds must be positive"
    )]
    [InlineData(
        "Secrets:Provider",
        "kms",
        "Secrets:Kms:KeyId",
        null,
        "Secrets:Kms:KeyId is required"
    )]
    [InlineData(
        "Secrets:Provider",
        "kms",
        "Secrets:Kms:ServiceUrl",
        "not-a-url",
        "Secrets:Kms:ServiceUrl"
    )]
    [InlineData(
        "Secrets:Provider",
        "kms",
        "Secrets:Kms:AccessKey",
        "access",
        "Secrets:Kms:AccessKey and SecretKey"
    )]
    [InlineData(
        "Billing:Provider",
        "stripe",
        "Billing:Stripe:ApiKey",
        null,
        "Billing:Stripe:ApiKey is required"
    )]
    [InlineData(
        "Billing:Provider",
        "stripe",
        "Billing:Stripe:WebhookSecret",
        null,
        "Billing:Stripe:WebhookSecret is required"
    )]
    [InlineData(
        "Billing:Provider",
        "stripe",
        "Billing:Stripe:ApiBase",
        "not-a-url",
        "Billing:Stripe:ApiBase"
    )]
    [InlineData(
        "Billing:Provider",
        "stripe",
        "Billing:Stripe:PriceIds:growth",
        null,
        "Billing:Stripe:PriceIds must contain every"
    )]
    [InlineData(
        "Notifications:Transport",
        "smtp",
        "Notifications:Smtp:Host",
        null,
        "Notifications:Smtp:Host is required"
    )]
    [InlineData(
        "Notifications:Transport",
        "smtp",
        "Notifications:Smtp:Port",
        "0",
        "Notifications:Smtp:Port must be between"
    )]
    [InlineData(
        "Notifications:Transport",
        "smtp",
        "Notifications:Smtp:Port",
        "65536",
        "Notifications:Smtp:Port must be between"
    )]
    [InlineData(
        "Notifications:Transport",
        "smtp",
        "Notifications:Smtp:FromAddress",
        "invalid",
        "Notifications:Smtp:FromAddress"
    )]
    [InlineData(
        "Notifications:Transport",
        "smtp",
        "Notifications:Smtp:UserName",
        "user",
        "Notifications:Smtp:UserName and Password"
    )]
    [InlineData(
        "Auth:Provider",
        "workos",
        "Auth:WorkOS:ApiBaseUrl",
        "http://example.test",
        "Auth:WorkOS:ApiBaseUrl"
    )]
    [InlineData(
        "Auth:Provider",
        "workos",
        "Auth:WorkOS:WebhookSecret",
        null,
        "Auth:WorkOS:WebhookSecret"
    )]
    [InlineData(
        "Storage:Provider",
        "s3",
        "Storage:S3:ServiceUrl",
        "http://example.test",
        "Storage:S3:ServiceUrl"
    )]
    [InlineData(
        "Storage:Provider",
        "azure",
        "Storage:Azure:ConnectionString",
        "UseDevelopmentStorage=true",
        "Storage:Azure configuration is malformed"
    )]
    [InlineData(
        "Secrets:Provider",
        "kms",
        "Secrets:Kms:ServiceUrl",
        "http://example.test",
        "Secrets:Kms:ServiceUrl"
    )]
    [InlineData(
        "Billing:Provider",
        "stripe",
        "Billing:Stripe:ApiBase",
        "http://example.test",
        "Billing:Stripe:ApiBase"
    )]
    [InlineData(
        "Notifications:Transport",
        "smtp",
        "Notifications:Smtp:UseStartTls",
        "false",
        "Notifications:Smtp:UseStartTls"
    )]
    public void Production_refuses_invalid_provider_options(
        string selectorKey,
        string selectorValue,
        string invalidKey,
        string? invalidValue,
        string refusal
    )
    {
        var settings = ProductionValid();
        settings[selectorKey] = selectorValue;
        settings[invalidKey] = invalidValue;

        Assert.Contains(refusal, BootRefusal(settings)!.Message);
    }

    [Theory]
    [InlineData("Proxy:KnownNetworks:0", "0.0.0.0/0")]
    [InlineData("Proxy:KnownNetworks:0", "::/0")]
    [InlineData("Proxy:KnownProxies:0", "0.0.0.0")]
    [InlineData("Proxy:KnownProxies:0", "bad-address")]
    public void Wildcard_or_malformed_proxy_peers_are_refused(string key, string value)
    {
        var settings = ProductionValid();
        settings["Proxy:TrustForwardedHeaders"] = "true";
        settings[key] = value;
        Assert.Contains("Proxy:Known", BootRefusal(settings)!.Message);
    }

    [Theory]
    [InlineData("Auth:Provider", "local")]
    [InlineData("Storage:Provider", "local")]
    [InlineData("Scanner:Provider", "eicar")]
    [InlineData("Secrets:Provider", "local")]
    [InlineData("Billing:Provider", "local")]
    [InlineData("Notifications:Transport", "local")]
    [InlineData("Notifications:Sms", "local")]
    public void Staging_does_not_enable_development_adapters(string key, string value)
    {
        var settings = ProductionValid();
        settings[key] = value;
        Assert.Contains(key, BootRefusal(settings, "Staging")!.Message);
    }

    /// <summary>The composition root's refusal, or null when the host built.</summary>
    private static Exception? BootRefusal(
        Dictionary<string, string?> settings,
        string environment = "Production"
    )
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            foreach (var (key, value) in settings)
                b.UseSetting(key, value);
        });
        var thrown = Record.Exception(() => factory.Server);
        for (var e = thrown; e is not null; e = e.InnerException)
            if (e is InvalidOperationException or OptionsValidationException)
                return e;
        return thrown;
    }
}
