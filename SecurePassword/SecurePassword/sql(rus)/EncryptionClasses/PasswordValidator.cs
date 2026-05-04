using System.Reflection;

namespace SecurePassword;

public static class PasswordValidator
{
    private static HashSet<string>? _badPasswords;
    private static readonly object SyncRoot = new();

    public static int CalculateCryptographicStrength(string password)
    {
        double entropyBits = CalculateEntropyBits(password);

        if (entropyBits < 28)
            return 1;
        if (entropyBits < 44)
            return 2;
        if (entropyBits < 64)
            return 3;
        return 4;
    }

    public static int CalculateStrengthPercentage(string password)
    {
        double entropyBits = CalculateEntropyBits(password);
        double cappedEntropy = Math.Min(entropyBits, 80d);
        return (int)Math.Round(cappedEntropy / 80d * 100d);
    }

    public static double CalculateEntropyBits(string password)
    {
        if (string.IsNullOrEmpty(password))
            return 0;

        int alphabet = EstimateAlphabetSize(password);
        if (alphabet <= 1)
            return 0;

        double baseEntropy = password.Length * Math.Log2(alphabet);
        double repetitionPenalty = CalculateRepetitionPenalty(password);
        double sequencePenalty = CalculateSequencePenalty(password);
        double commonPenalty = IsPasswordCommon(password) ? 18d : 0d;
        double entropyBits = Math.Max(0d, baseEntropy - repetitionPenalty - sequencePenalty - commonPenalty);

        return Math.Round(entropyBits, 1);
    }

    public static bool IsPasswordCommon(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return false;

        EnsureBadPasswordsLoaded();
        return _badPasswords?.Contains(password.Trim()) == true;
    }

    public static void ClearBadPasswords()
    {
        lock (SyncRoot)
        {
            _badPasswords?.Clear();
            _badPasswords = null;
        }
    }

    private static int EstimateAlphabetSize(string password)
    {
        int alphabet = 0;

        if (password.Any(char.IsLower))
            alphabet += 26;
        if (password.Any(char.IsUpper))
            alphabet += 26;
        if (password.Any(char.IsDigit))
            alphabet += 10;
        if (password.Any(ch => !char.IsLetterOrDigit(ch)))
            alphabet += 33;

        if (alphabet == 0)
            alphabet = password.Distinct().Count();

        return alphabet;
    }

    private static double CalculateRepetitionPenalty(string password)
    {
        int uniqueChars = password.Distinct().Count();
        double uniquenessRatio = uniqueChars / (double)password.Length;
        return (1d - uniquenessRatio) * 14d;
    }

    private static double CalculateSequencePenalty(string password)
    {
        double penalty = 0;

        for (int i = 2; i < password.Length; i++)
        {
            char a = password[i - 2];
            char b = password[i - 1];
            char c = password[i];

            if (a == b && b == c)
            {
                penalty += 6;
                continue;
            }

            if ((b == a + 1 && c == b + 1) || (b == a - 1 && c == b - 1))
                penalty += 4;
        }

        return penalty;
    }

    private static void EnsureBadPasswordsLoaded()
    {
        if (_badPasswords is not null)
            return;

        lock (SyncRoot)
        {
            if (_badPasswords is not null)
                return;

            var assembly = Assembly.GetExecutingAssembly();
            string? resourceName = assembly
                .GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith("badwords.txt", StringComparison.OrdinalIgnoreCase));

            if (resourceName is null)
            {
                _badPasswords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            using var reader = stream is null ? null : new StreamReader(stream);
            string content = reader?.ReadToEnd() ?? string.Empty;

            _badPasswords = content
                .Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }
}
