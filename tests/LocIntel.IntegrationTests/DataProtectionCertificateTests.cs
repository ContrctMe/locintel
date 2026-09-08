using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.IntegrationTests;

public class DataProtectionCertificateTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Encrypted_keyring_survives_a_new_host_and_requires_the_certificate()
    {
        var root = Path.Combine(Path.GetTempPath(), $"locintel-keyring-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var ring = Path.Combine(root, "keys");
        var certificatePath = Path.Combine(root, "keys.pfx");
        const string password = "test-certificate-password";
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=locintel-keyring-test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1)
        );
        await File.WriteAllBytesAsync(
            certificatePath,
            certificate.Export(X509ContentType.Pfx, password)
        );
        try
        {
            string protectedValue;
            await using (
                var first = fixture.Factory.WithWebHostBuilder(b =>
                {
                    b.UseSetting("DataProtection:KeyPath", ring);
                    b.UseSetting("DataProtection:CertificatePath", certificatePath);
                    b.UseSetting("DataProtection:CertificatePassword", password);
                })
            )
            {
                protectedValue = first
                    .Services.GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("compatibility-session")
                    .Protect("session-payload");
            }
            var files = Directory.GetFiles(ring, "key-*.xml");
            Assert.NotEmpty(files);
            foreach (var file in files)
            {
                var xml = XDocument.Load(file);
                Assert.Contains(xml.Descendants(), e => e.Name.LocalName == "encryptedSecret");
                Assert.DoesNotContain(xml.Descendants(), e => e.Name.LocalName == "masterKey");
            }
            await using (
                var second = fixture.Factory.WithWebHostBuilder(b =>
                {
                    b.UseSetting("DataProtection:KeyPath", ring);
                    b.UseSetting("DataProtection:CertificatePath", certificatePath);
                    b.UseSetting("DataProtection:CertificatePassword", password);
                })
            )
            {
                Assert.Equal(
                    "session-payload",
                    second
                        .Services.GetRequiredService<IDataProtectionProvider>()
                        .CreateProtector("compatibility-session")
                        .Unprotect(protectedValue)
                );
            }
            var noCertificate = DataProtectionProvider.Create(
                new DirectoryInfo(ring),
                b => b.SetApplicationName("locintel")
            );
            Assert.Throws<CryptographicException>(() =>
                noCertificate.CreateProtector("compatibility-session").Unprotect(protectedValue)
            );
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
