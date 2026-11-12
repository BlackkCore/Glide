using System.Security.Cryptography;

namespace ServiceLib.Common;

/// <summary>
/// Glide (S-02, S-01): the release public key and the signature check, with no dependencies, so AmazTool
/// (which can't reference ServiceLib) compiles this same file and verifies the very zip it extracts.
/// </summary>
public static class UpdateSignatureKey
{
    /// <summary>
    /// The public half of the release signing key (PEM, "BEGIN PUBLIC KEY"), made with
    /// scripts/signing/new-update-signing-key.sh.
    /// Empty means no update can be verified: every update is refused, and release builds fail.
    /// </summary>
    public const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAExao6N9tpWwCcg6W9TjIXGflPp449
        WoWMAu/fZDeo8uDYwvhi0b4hl6ZyH1KNY1JSLhNWSlDvMF7qJNCl3PlVhA==
        -----END PUBLIC KEY-----
        """;

    /// <summary>True when the stream's content carries a valid ECDSA P-256 / SHA-256 signature (base64 DER).</summary>
    public static bool VerifyStream(Stream data, string? signatureBase64, string? publicKeyPem)
    {
        if (string.IsNullOrEmpty(publicKeyPem) || string.IsNullOrWhiteSpace(signatureBase64))
        {
            return false;
        }
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(publicKeyPem);
            if (ecdsa.KeySize != 256)
            {
                return false;
            }
            var signature = Convert.FromBase64String(signatureBase64.Trim());
            return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException or IOException)
        {
            return false;
        }
    }
}
