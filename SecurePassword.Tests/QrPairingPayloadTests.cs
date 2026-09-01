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
    public void ReceiverBootstrap_CreatesCanonicalPayloadAndManualFallback()
    {
        using var bootstrap = ReceiverPairingBootstrap.Create("192.168.10.25");

        bool parsed = QrPairingPayload.TryParse(
            bootstrap.QrPayload,
            out QrPairingPayload? result,
            out string error);

        Assert.True(parsed, error);
        Assert.NotNull(result);
        Assert.Equal(bootstrap.ReceiverAddress, result!.Host);
        Assert.Equal(bootstrap.PairingCode, result.PairingCode);
        Assert.Equal(NetworkService.SyncPort, result.Port);
        Assert.Equal(14, bootstrap.PairingCode.Length);
    }

    [Fact]
    public void ReceiverBootstrap_InvalidAddress_DisposesGeneratedSecret()
    {
        PairingSecret? generatedSecret = null;

        Assert.Throws<ArgumentException>(() => ReceiverPairingBootstrap.Create(
            "203.0.113.10",
            () => generatedSecret = PairingSecret.Generate()));

        Assert.NotNull(generatedSecret);
        Assert.Throws<ObjectDisposedException>(() => generatedSecret!.GetSecretBytes());
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
