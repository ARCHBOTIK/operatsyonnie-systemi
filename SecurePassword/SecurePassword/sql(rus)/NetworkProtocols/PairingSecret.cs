using System.Security.Cryptography;
using System.Text;

namespace SecurePassword;

public sealed class PairingSecret : IDisposable
{
    private const string Base32Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ"; // 32 unambiguous chars (5 bits each)
    private const int SecretLength = 12; // 12 chars = 60 bits entropy
    public const int DefaultExpirationSeconds = 180; // 3 minutes

    private byte[]? _secretBytes;
    private bool _disposed;

    public string FormattedCode { get; }
    public string RawCode { get; }
    public DateTimeOffset ExpiresAt { get; }
    public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;

    private PairingSecret(string rawCode, string formattedCode, int expirationSeconds)
    {
        RawCode = rawCode;
        FormattedCode = formattedCode;
        _secretBytes = Encoding.UTF8.GetBytes(rawCode);
        ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expirationSeconds);
    }

    public static PairingSecret Generate(int expirationSeconds = DefaultExpirationSeconds)
    {
        byte[] randomBytes = new byte[SecretLength];
        RandomNumberGenerator.Fill(randomBytes);

        var chars = new char[SecretLength];
        for (int i = 0; i < SecretLength; i++)
        {
            chars[i] = Base32Alphabet[randomBytes[i] % Base32Alphabet.Length];
        }

        string rawCode = new(chars);
        // Format as XXXX-XXXX-XXXX
        string formattedCode = $"{rawCode[..4]}-{rawCode.Substring(4, 4)}-{rawCode[8..]}";

        return new PairingSecret(rawCode, formattedCode, expirationSeconds);
    }

    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var sb = new StringBuilder();
        foreach (char c in input.ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                // Map common lookalikes if entered by user
                char mapped = c switch
                {
                    '0' => 'O',
                    '1' => 'L',
                    _ => c
                };
                if (Base32Alphabet.Contains(mapped))
                {
                    sb.Append(mapped);
                }
            }
        }
        return sb.ToString();
    }

    public static bool TryNormalize(string? input, out string normalizedCode)
    {
        normalizedCode = Normalize(input ?? string.Empty);
        return normalizedCode.Length == SecretLength;
    }

    public byte[] GetSecretBytes()
    {
        if (_disposed || _secretBytes == null)
            throw new ObjectDisposedException(nameof(PairingSecret));

        return (byte[])_secretBytes.Clone();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_secretBytes != null)
        {
            CryptographicOperations.ZeroMemory(_secretBytes);
            _secretBytes = null;
        }
    }
}
