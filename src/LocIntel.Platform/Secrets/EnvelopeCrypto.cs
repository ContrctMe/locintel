using System.Security.Cryptography;

namespace LocIntel.Platform.Secrets;

/// <summary>
/// KMS seam (ADR 31): wraps/unwraps per-secret data keys. Cloud adapters (AWS
/// KMS, Azure Key Vault) implement this against their HSM-backed keys; the
/// local implementation is for dev/test ONLY and refuses Production.
/// </summary>
public interface IKeyWrapper
{
    ValueTask<byte[]> WrapAsync(byte[] dataKey, CancellationToken ct = default);
    ValueTask<byte[]> UnwrapAsync(byte[] wrappedKey, CancellationToken ct = default);
}

/// <summary>
/// Envelope encryption (ADR 31): fresh AES-256-GCM data key per secret,
/// wrapped by the KMS seam. Blob layout: [len][wrapped key][12 nonce][16 tag][cipher].
/// </summary>
public static class EnvelopeCrypto
{
    public static async ValueTask<byte[]> EncryptAsync(
        string plaintext,
        IKeyWrapper wrapper,
        CancellationToken ct = default
    )
    {
        var dataKey = RandomNumberGenerator.GetBytes(32);
        var wrapped = await wrapper.WrapAsync(dataKey, ct);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(dataKey, 16))
            aes.Encrypt(nonce, plainBytes, cipher, tag);
        CryptographicOperations.ZeroMemory(dataKey);

        var blob = new byte[4 + wrapped.Length + 12 + 16 + cipher.Length];
        BitConverter.GetBytes(wrapped.Length).CopyTo(blob, 0);
        wrapped.CopyTo(blob, 4);
        nonce.CopyTo(blob, 4 + wrapped.Length);
        tag.CopyTo(blob, 4 + wrapped.Length + 12);
        cipher.CopyTo(blob, 4 + wrapped.Length + 28);
        return blob;
    }

    public static async ValueTask<string> DecryptAsync(
        byte[] blob,
        IKeyWrapper wrapper,
        CancellationToken ct = default
    )
    {
        var wrappedLength = BitConverter.ToInt32(blob, 0);
        var wrapped = blob[4..(4 + wrappedLength)];
        var nonce = blob[(4 + wrappedLength)..(4 + wrappedLength + 12)];
        var tag = blob[(4 + wrappedLength + 12)..(4 + wrappedLength + 28)];
        var cipher = blob[(4 + wrappedLength + 28)..];
        var dataKey = await wrapper.UnwrapAsync(wrapped, ct);
        try
        {
            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(dataKey, 16);
            aes.Decrypt(nonce, cipher, tag, plain);
            return System.Text.Encoding.UTF8.GetString(plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }
}

/// <summary>
/// DEV/TEST ONLY (ADR 31): wraps with a config-supplied AES key. No HSM
/// boundary, no rotation - unmistakably unsafe for production, and the host
/// refuses to boot with it there.
/// </summary>
public sealed class LocalKeyWrapper(byte[] masterKey) : IKeyWrapper
{
    public ValueTask<byte[]> WrapAsync(byte[] dataKey, CancellationToken ct = default)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[dataKey.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(masterKey, 16))
            aes.Encrypt(nonce, dataKey, cipher, tag);
        return ValueTask.FromResult<byte[]>([.. nonce, .. tag, .. cipher]);
    }

    public ValueTask<byte[]> UnwrapAsync(byte[] wrappedKey, CancellationToken ct = default)
    {
        var nonce = wrappedKey[..12];
        var tag = wrappedKey[12..28];
        var cipher = wrappedKey[28..];
        var dataKey = new byte[cipher.Length];
        using var aes = new AesGcm(masterKey, 16);
        aes.Decrypt(nonce, cipher, tag, dataKey);
        return ValueTask.FromResult(dataKey);
    }
}
