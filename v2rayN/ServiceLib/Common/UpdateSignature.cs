namespace ServiceLib.Common;

/// <summary>
/// Glide (S-02): an app update is installed only when its zip carries a valid ECDSA P-256 signature
/// from Glide's release key. CI signs each Glide-windows-*.zip and publishes "&lt;zip&gt;.sig256"
/// (base64 of the DER signature from <c>openssl dgst -sha256 -sign</c>). Anything missing or invalid
/// is refused: a checksum from the same release page would prove nothing. The key and the check itself
/// live in <see cref="UpdateSignatureKey"/>, which AmazTool compiles too and checks again before extracting.
/// </summary>
public static class UpdateSignature
{
    public const string Extension = ".sig256";

    /// <summary>True when the zip next to its downloaded ".sig256" verifies against the embedded key.</summary>
    public static bool VerifyDownloaded(string zipPath)
    {
        var sigPath = zipPath + Extension;
        var signature = File.Exists(sigPath) ? File.ReadAllText(sigPath) : null;
        return Verify(zipPath, signature, UpdateSignatureKey.PublicKeyPem);
    }

    public static bool Verify(string zipPath, string? signatureBase64, string? publicKeyPem)
    {
        if (publicKeyPem.IsNullOrEmpty() || signatureBase64.IsNullOrEmpty() || !File.Exists(zipPath))
        {
            Logging.SaveLog($"{nameof(UpdateSignature)}: {(publicKeyPem.IsNullOrEmpty() ? "no public key built in" : "signature or file missing")}");
            return false;
        }
        try
        {
            using var stream = File.OpenRead(zipPath);
            var ok = UpdateSignatureKey.VerifyStream(stream, signatureBase64, publicKeyPem);
            if (!ok)
            {
                Logging.SaveLog($"{nameof(UpdateSignature)}: signature does not match");
            }
            return ok;
        }
        catch (IOException ex)
        {
            Logging.SaveLog(nameof(UpdateSignature), ex);
            return false;
        }
    }
}
