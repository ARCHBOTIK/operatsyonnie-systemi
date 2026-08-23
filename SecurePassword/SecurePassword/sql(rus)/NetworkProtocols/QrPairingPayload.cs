using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace SecurePassword;

/// <summary>
/// The non-secret bootstrap data carried by a receiver pairing QR code.
/// It deliberately contains no vault data, cryptographic key material or SPP1 session keys.
/// </summary>
public sealed class QrPairingPayload
{
    public const string Scheme = "vaultpass";
    public const string MagicHost = "pair";
    public const int Version = 1;
    public const int MaximumLength = 256;
    private static readonly TimeSpan MaximumLifetime = TimeSpan.FromSeconds(PairingSecret.DefaultExpirationSeconds + 30);
    private static readonly Regex FormattedCodePattern = new(
        "^[23456789ABCDEFGHJKLMNPQRSTUVWXYZ]{4}-[23456789ABCDEFGHJKLMNPQRSTUVWXYZ]{4}-[23456789ABCDEFGHJKLMNPQRSTUVWXYZ]{4}$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

    private QrPairingPayload(string host, int port, string pairingCode, DateTimeOffset expiresAt)
    {
        Host = host;
        Port = port;
        PairingCode = pairingCode;
        ExpiresAt = expiresAt;
    }

    public string Host { get; }
    public int Port { get; }
    public string PairingCode { get; }
    public DateTimeOffset ExpiresAt { get; }

    public static QrPairingPayload Create(string host, PairingSecret pairingSecret)
    {
        ArgumentNullException.ThrowIfNull(pairingSecret);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (pairingSecret.IsExpired || pairingSecret.ExpiresAt <= now)
            throw new InvalidOperationException("Pairing code has expired.");

        if (!IsPrivateIpv4(host))
            throw new ArgumentException("A valid private IPv4 address is required for the pairing QR.", nameof(host));

        return new QrPairingPayload(host, NetworkService.SyncPort, pairingSecret.FormattedCode, pairingSecret.ExpiresAt);
    }

    /// <summary>Produces one canonical form so payloads are deterministic and easy to audit.</summary>
    public string Serialize() => string.Create(CultureInfo.InvariantCulture,
        $"{Scheme}://{MagicHost}?v={Version}&host={Host}&port={Port}&code={PairingCode}&exp={ExpiresAt.ToUnixTimeSeconds()}");

    public static bool TryParse(string? payload, out QrPairingPayload? result, out string error, DateTimeOffset? now = null)
    {
        result = null;
        error = string.Empty;
        DateTimeOffset currentTime = now ?? DateTimeOffset.UtcNow;

        if (string.IsNullOrWhiteSpace(payload) || payload.Length > MaximumLength)
        {
            error = "The pairing QR is empty or too long.";
            return false;
        }

        // Do not accept URI normalisation, alternate magic values, fragments or encoded fields.
        if (!payload.StartsWith($"{Scheme}://{MagicHost}?", StringComparison.Ordinal) ||
            payload.Contains('#') || payload.Contains('%'))
        {
            error = "This is not a VaultPass pairing QR.";
            return false;
        }

        if (!Uri.TryCreate(payload, UriKind.Absolute, out Uri? uri) ||
            !string.Equals(uri.Scheme, Scheme, StringComparison.Ordinal) ||
            !string.Equals(uri.Host, MagicHost, StringComparison.Ordinal) || uri.Port != -1 ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/")
        {
            error = "The pairing QR has an invalid structure.";
            return false;
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        string rawQuery = payload[(payload.IndexOf('?') + 1)..];
        foreach (string segment in rawQuery.Split('&', StringSplitOptions.None))
        {
            int equals = segment.IndexOf('=');
            if (equals <= 0 || equals != segment.LastIndexOf('='))
            {
                error = "The pairing QR has invalid fields.";
                return false;
            }

            string key = segment[..equals];
            string value = segment[(equals + 1)..];
            if (string.IsNullOrEmpty(value) || !fields.TryAdd(key, value))
            {
                error = "The pairing QR has duplicate or empty fields.";
                return false;
            }
        }

        string[] required = ["v", "host", "port", "code", "exp"];
        if (fields.Count != required.Length || required.Any(key => !fields.ContainsKey(key)))
        {
            error = "The pairing QR contains unsupported fields.";
            return false;
        }

        if (fields["v"] != "1")
        {
            error = "This pairing QR version is not supported.";
            return false;
        }

        string host = fields["host"];
        if (!IsPrivateIpv4(host))
        {
            error = "The pairing QR has an invalid receiver address.";
            return false;
        }

        if (!int.TryParse(fields["port"], NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port != NetworkService.SyncPort)
        {
            error = "The pairing QR has an invalid pairing port.";
            return false;
        }

        string pairingCode = fields["code"];
        if (!FormattedCodePattern.IsMatch(pairingCode))
        {
            error = "The pairing QR has an invalid pairing code.";
            return false;
        }

        if (!long.TryParse(fields["exp"], NumberStyles.None, CultureInfo.InvariantCulture, out long unixSeconds))
        {
            error = "The pairing QR has an invalid expiry.";
            return false;
        }

        DateTimeOffset expiresAt;
        try { expiresAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds); }
        catch (ArgumentOutOfRangeException)
        {
            error = "The pairing QR has an invalid expiry.";
            return false;
        }

        if (expiresAt <= currentTime)
        {
            error = "This pairing QR has expired. Create a new QR on the receiving device.";
            return false;
        }

        if (expiresAt - currentTime > MaximumLifetime)
        {
            error = "The pairing QR has an invalid lifetime.";
            return false;
        }

        result = new QrPairingPayload(host, port, pairingCode, expiresAt);
        return true;
    }

    private static bool IsPrivateIpv4(string? value)
    {
        if (!IPAddress.TryParse(value, out IPAddress? address) || address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        byte[] bytes = address.GetAddressBytes();
        return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168);
    }
}
