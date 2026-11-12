namespace ServiceLib.Tests.Common;

public class UpdateSignatureTests
{
    // A throwaway TEST key made with the same openssl commands CI uses, to prove the formats match:
    //   openssl ecparam -name prime256v1 -genkey -noout -out test.pem
    //   openssl dgst -sha256 -sign test.pem payload.bin | base64 -w0
    // Its private half was discarded; it signs nothing real.
    private const string OpensslTestPublicKey = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE/xiV58MjigfCImgElFrl5/bJ7goc
        Ufd3McreL+FEN4wrnx2D6H+0isHTkjlAyu3ZmjckGXCvYV0XyX6KlZ8t7A==
        -----END PUBLIC KEY-----
        """;
    private const string OpensslTestPayload = "glide update test payload\n";
    private const string OpensslTestSignature = "MEQCIH2MigFvEZmiRH0AhV69Pg+R0HfFq2ZPkJi+dCIn2xC0AiAg4qR12XV637Xtoh7XiO35VDeihmBvzTuOICxNVsHtog==";

    private static string TempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"glide-sig-{Guid.NewGuid():N}.zip");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(content));
        return path;
    }

    [Test]
    public async Task Verify_OpensslSignature_IsAccepted()
    {
        var file = TempFile(OpensslTestPayload);
        try
        {
            await UpdateSignature.Verify(file, OpensslTestSignature, OpensslTestPublicKey).Should().BeTrue();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    public async Task Verify_TamperedFile_IsRefused()
    {
        var file = TempFile(OpensslTestPayload + "x");
        try
        {
            await UpdateSignature.Verify(file, OpensslTestSignature, OpensslTestPublicKey).Should().BeFalse();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    public async Task Verify_OtherKey_IsRefused()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var file = TempFile(OpensslTestPayload);
        try
        {
            await UpdateSignature.Verify(file, OpensslTestSignature, other.ExportSubjectPublicKeyInfoPem()).Should().BeFalse();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("not base64 !!")]
    [Arguments("AAAA")]
    public async Task Verify_MissingOrGarbageSignature_IsRefused(string? signature)
    {
        var file = TempFile(OpensslTestPayload);
        try
        {
            await UpdateSignature.Verify(file, signature, OpensslTestPublicKey).Should().BeFalse();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    public async Task Verify_NoKeyBuiltIn_RefusesEverything()
    {
        var file = TempFile(OpensslTestPayload);
        try
        {
            await UpdateSignature.Verify(file, OpensslTestSignature, "").Should().BeFalse();
            await UpdateSignature.Verify(file, OpensslTestSignature, "-----BEGIN PUBLIC KEY-----\ngarbage\n-----END PUBLIC KEY-----").Should().BeFalse();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    public async Task EmbeddedKey_IsAP256PublicKey()
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(UpdateSignatureKey.PublicKeyPem);
        await ecdsa.KeySize.Should().BeEqualTo(256);
    }

    [Test]
    public async Task EmbeddedKey_IsInTheExactTextOpensslEmits()
    {
        // package-zip.yml compares this text with `openssl ec -pubout` of the CI secret; any other
        // layout (line width, extra lines) would make a correct secret fail that check.
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(UpdateSignatureKey.PublicKeyPem);
        var embedded = string.Join('\n', UpdateSignatureKey.PublicKeyPem.Split('\n').Select(l => l.Trim()));
        await embedded.Should().BeEqualTo(ecdsa.ExportSubjectPublicKeyInfoPem().Trim());
    }

    [Test]
    public async Task EmbeddedKey_RefusesSignatureFromAnotherKey()
    {
        // The openssl test signature is valid for its own key, never for the release key.
        var file = TempFile(OpensslTestPayload);
        try
        {
            await UpdateSignature.Verify(file, OpensslTestSignature, UpdateSignatureKey.PublicKeyPem).Should().BeFalse();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    public async Task VerifyDownloaded_MissingSigFile_IsRefused()
    {
        var file = TempFile(OpensslTestPayload);
        try
        {
            await UpdateSignature.VerifyDownloaded(file).Should().BeFalse();
        }
        finally
        {
            File.Delete(file);
        }
    }
}
