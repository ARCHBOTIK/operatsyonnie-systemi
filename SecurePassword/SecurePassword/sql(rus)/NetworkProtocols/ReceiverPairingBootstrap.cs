namespace SecurePassword;

/// <summary>
/// Creates and owns the short-lived receiver bootstrap shared by authenticated
/// and pre-authentication import flows.
/// </summary>
public sealed class ReceiverPairingBootstrap : IDisposable
{
    private bool _disposed;

    private ReceiverPairingBootstrap(
        string receiverAddress,
        PairingSecret pairingSecret,
        string qrPayload)
    {
        ReceiverAddress = receiverAddress;
        PairingSecret = pairingSecret;
        QrPayload = qrPayload;
    }

    public string ReceiverAddress { get; }
    public PairingSecret PairingSecret { get; }
    public string PairingCode => PairingSecret.FormattedCode;
    public DateTimeOffset ExpiresAt => PairingSecret.ExpiresAt;
    public string QrPayload { get; }

    public static ReceiverPairingBootstrap Create(
        string? receiverAddress,
        Func<PairingSecret>? pairingSecretFactory = null)
    {
        if (string.IsNullOrWhiteSpace(receiverAddress))
            throw new InvalidOperationException("A private receiver address is required for pairing.");

        PairingSecret pairingSecret = (pairingSecretFactory ?? (() => PairingSecret.Generate())).Invoke();
        try
        {
            string normalizedAddress = receiverAddress.Trim();
            string qrPayload = QrPairingPayload.Create(normalizedAddress, pairingSecret).Serialize();
            return new ReceiverPairingBootstrap(normalizedAddress, pairingSecret, qrPayload);
        }
        catch
        {
            pairingSecret.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        PairingSecret.Dispose();
    }
}
