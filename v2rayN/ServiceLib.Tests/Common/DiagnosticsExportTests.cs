namespace ServiceLib.Tests.Common;

public class DiagnosticsExportTests
{
    [Test]
    public async Task RedactJson_RemovesSecretsAndAddresses_KeepsTheRest()
    {
        const string json = """
            {
              "IndexId": "abc123",
              "Inbound": [ { "LocalPort": 10808, "User": "me", "Pass": "hunter2" } ],
              "WebDavItem": { "Url": "https://dav.example/me", "Password": "pw" },
              "ConstItem": { "SubConvertUrl": "https://conv.example/x?token=SECRET" },
              "UiItem": { "CurrentLanguage": "fa", "Note": "see https://x.example/path?token=SECRET2" }
            }
            """;
        var redacted = DiagnosticsExport.RedactJson(json);
        await redacted.Contains("hunter2").Should().BeFalse();
        await redacted.Contains("SECRET").Should().BeFalse();
        await redacted.Contains("abc123").Should().BeFalse();
        await redacted.Contains("dav.example/me").Should().BeFalse();
        await redacted.Contains("10808").Should().BeTrue();
        await redacted.Contains("\"fa\"").Should().BeTrue();
    }

    [Test]
    public async Task RedactJson_StringsInsideArrays_AreRedacted()
    {
        const string json = """{ "Hosts": [ "https://h.example/x?token=SECRET", "vless://uuid-secret@h:1" ], "Ok": [ "fa" ] }""";
        var redacted = DiagnosticsExport.RedactJson(json);
        await redacted.Contains("SECRET").Should().BeFalse();
        await redacted.Contains("uuid-secret").Should().BeFalse();
        await redacted.Contains("\"fa\"").Should().BeTrue();
    }
}
