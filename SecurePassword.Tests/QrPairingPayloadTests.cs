using Xunit;

namespace SecurePassword.Tests;

public class QrPairingPayloadTests
{
    [Fact]
    public void QrPayload_RoundTrip_Valid()
    {
        using var secret = PairingSecret.Generate();
        QrPairingPayload source = QrPairingPayload.Create("192.168.10.25", secret);

        bool parsed = QrPairingPayload.TryParse(source.Serialize(), out QrPairingPayload? result, out string error);

        Assert.True(parsed, error);
        Assert.NotNull(result);
        Assert.Equal("192.168.10.25", result!.Host);
        Assert.Equal(NetworkService.SyncPort, result.Port);
        Assert.Equal(secret.FormattedCode, result.PairingCode);
    }

    [Fact]
    public void QrPayload_InvalidScheme_Rejected() =>
        AssertRejected(ValidPayload().Replace("vaultpass", "other", StringComparison.Ordinal));

    [Fact]
    public void QrPayload_UnsupportedVersion_Rejected() =>
        AssertRejected(ValidPayload().Replace("v=1", "v=2", StringComparison.Ordinal));

    [Fact]
    public void QrPayload_InvalidIp_Rejected() =>
        AssertRejected(ValidPayload().Replace("host=192.168.10.25", "host=example.com", StringComparison.Ordinal));

    [Fact]
    public void QrPayload_InvalidPort_Rejected() =>
        AssertRejected(ValidPayload().Replace("port=50555", "port=50554", StringComparison.Ordinal));

    [Fact]
    public void QrPayload_InvalidPairingCode_Rejected() =>
        AssertRejected(ValidPayload().Replace("code=ABCD-EFGH-JKLM", "code=1111-1111-1111", StringComparison.Ordinal));

    [Fact]
    public void QrPayload_Oversized_Rejected() =>
        AssertRejected(new string('x', QrPairingPayload.MaximumLength + 1));

    [Fact]
    public void ExpiredQrSession_Rejected()
    {
        string payload = $"vaultpass://pair?v=1&host=192.168.10.25&port=50555&code=ABCD-EFGH-JKLM&exp={DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeSeconds()}";
        AssertRejected(payload);
    }

    [Fact]
    public void QrPayload_UnexpectedOrDuplicateField_Rejected() =>
        AssertRejected(ValidPayload() + "&extra=value");

    private static string ValidPayload()
    {
        return $"vaultpass://pair?v=1&host=192.168.10.25&port=50555&code=ABCD-EFGH-JKLM&exp={DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds()}";
    }

    private static void AssertRejected(string payload)
    {
        bool parsed = QrPairingPayload.TryParse(payload, out _, out string error);
        Assert.False(parsed);
        Assert.NotEmpty(error);
    }
}
