using System.Text.RegularExpressions;
using Xunit;

namespace SecurePassword.Tests;

public sealed class XamlStaticResourceTests
{
    [Fact]
    public void EveryProjectXaml_ResourcesResolveInItsActualScope()
    {
        string colors = ReadApplicationFile(Path.Combine("Resources", "Styles", "Colors.xaml"));
        string styles = ReadApplicationFile(Path.Combine("Resources", "Styles", "Styles.xaml"));
        HashSet<string> applicationKeys = ExtractDefinedKeys(colors)
            .Concat(ExtractDefinedKeys(styles))
            .ToHashSet(StringComparer.Ordinal);

        var failures = new List<string>();
        foreach (string relativePath in GetProjectXamlFiles())
        {
            string xaml = ReadApplicationFile(relativePath);
            HashSet<string> availableKeys = applicationKeys
                .Concat(ExtractDefinedKeys(xaml))
                .ToHashSet(StringComparer.Ordinal);

            string[] unresolvedKeys = ExtractResourceReferences(xaml)
                .Where(key => !availableKeys.Contains(key))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();

            if (unresolvedKeys.Length > 0)
                failures.Add($"{relativePath}: {string.Join(", ", unresolvedKeys)}");
        }

        Assert.True(
            failures.Count == 0,
            "XAML files reference resources unavailable in their scope:" +
            Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void EveryXamlEventHandler_HasMatchingCodeBehindMethod()
    {
        var failures = new List<string>();
        foreach (string relativePath in GetProjectXamlFiles())
        {
            string xaml = ReadApplicationFile(relativePath);
            string[] handlers = Regex.Matches(
                    xaml,
                    "(?:Clicked|Tapped|SelectionChanged|TextChanged|Completed|CheckedChanged|ValueChanged|Loaded|Unloaded)\\s*=\\s*\"([^\"]+)\"")
                .Select(match => match.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (handlers.Length == 0)
                continue;

            string codeBehind = ReadApplicationFile(relativePath + ".cs");
            foreach (string handler in handlers)
            {
                if (!Regex.IsMatch(codeBehind, $"\\b{Regex.Escape(handler)}\\s*\\("))
                    failures.Add($"{relativePath}: {handler}");
            }
        }

        Assert.True(
            failures.Count == 0,
            "XAML event handlers without code-behind methods:" +
            Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void EveryRegisteredXamlConverter_ClassExists()
    {
        var failures = new List<string>();
        foreach (string relativePath in GetProjectXamlFiles())
        {
            string xaml = ReadApplicationFile(relativePath);
            foreach (Match match in Regex.Matches(xaml, "<converters:([A-Za-z_][A-Za-z0-9_]*)"))
            {
                string typeName = $"SecurePassword.Converters.{match.Groups[1].Value}";
                if (typeof(SecurePassword.Converters.InverseBoolConverter).Assembly.GetType(typeName) is null)
                    failures.Add($"{relativePath}: {typeName}");
            }
        }

        Assert.True(
            failures.Count == 0,
            "XAML converter elements without CLR types:" +
            Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void TextProducingVisibilityConverter_IsNotUsedForBooleanTargetProperties()
    {
        string[] booleanTargets = ["IsVisible", "IsEnabled", "IsToggled", "IsPassword"];
        var failures = new List<string>();

        foreach (string relativePath in GetProjectXamlFiles())
        {
            string xaml = ReadApplicationFile(relativePath);
            foreach (string target in booleanTargets)
            {
                if (Regex.IsMatch(
                    xaml,
                    $"{target}\\s*=\\s*\"{{Binding[^\"]*BoolToVisibilityTextConverter[^\"]*\""))
                {
                    failures.Add($"{relativePath}: {target}");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            "BoolToVisibilityTextConverter returns text and cannot feed boolean target properties:" +
            Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void TwoWayBindings_DoNotUseOneWayOnlyConverters()
    {
        var failures = new List<string>();
        foreach (string relativePath in GetProjectXamlFiles())
        {
            string xaml = ReadApplicationFile(relativePath);
            foreach (Match match in Regex.Matches(xaml, "\"{Binding[^\"]*Converter={StaticResource ([^}]+)}[^\"]*Mode=TwoWay[^\"]*\""))
                failures.Add($"{relativePath}: {match.Groups[1].Value}");
            foreach (Match match in Regex.Matches(xaml, "\"{Binding[^\"]*Mode=TwoWay[^\"]*Converter={StaticResource ([^}]+)}[^\"]*\""))
                failures.Add($"{relativePath}: {match.Groups[1].Value}");
        }

        Assert.True(
            failures.Count == 0,
            "TwoWay bindings use converters whose ConvertBack contract is not guaranteed:" +
            Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void LiteralXamlImageReferences_HavePackagedSourceAssets()
    {
        string imageDirectory = GetApplicationPath(Path.Combine("Resources", "Images"));
        HashSet<string> packagedNames = Directory.GetFiles(imageDirectory)
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();

        foreach (string relativePath in GetProjectXamlFiles())
        {
            string xaml = ReadApplicationFile(relativePath);
            foreach (Match match in Regex.Matches(xaml, "(?:Icon|Source)\\s*=\\s*\"([^\"{}]+\\.(?:png|svg))\""))
            {
                string assetName = Path.GetFileNameWithoutExtension(match.Groups[1].Value);
                if (!packagedNames.Contains(assetName))
                    failures.Add($"{relativePath}: {match.Groups[1].Value}");
            }
        }

        Assert.True(
            failures.Count == 0,
            "Literal XAML image references without packaged assets:" +
            Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void ViewModelBoundPages_UseCompiledBindings()
    {
        string[] boundPages =
        [
            Path.Combine("Views", "Vault", "VaultPage.xaml"),
            Path.Combine("Views", "Vault", "ItemDetailPage.xaml"),
            Path.Combine("Views", "Vault", "ItemEditPage.xaml"),
            Path.Combine("Views", "Generator", "GeneratorPage.xaml"),
            Path.Combine("Views", "Settings", "SettingsPage.xaml"),
            Path.Combine("Views", "Sync", "SyncPage.xaml"),
            Path.Combine("Views", "Import", "ImportPage.xaml")
        ];

        foreach (string relativePath in boundPages)
            Assert.Contains("x:DataType=", ReadApplicationFile(relativePath), StringComparison.Ordinal);
    }

    [Fact]
    public void ShellNavigationTargets_AreDeclaredOrRegistered()
    {
        string shell = ReadApplicationFile("AppShell.xaml");
        string shellCode = ReadApplicationFile("AppShell.xaml.cs");
        string settingsCode = ReadApplicationFile(Path.Combine("Views", "Settings", "SettingsPage.xaml.cs"));

        Assert.Contains("Route=\"sync\"", shell, StringComparison.Ordinal);
        Assert.Contains("RegisterRoute(\"import\"", shellCode, StringComparison.Ordinal);
        Assert.Contains("GoToAsync(\"//sync\")", settingsCode, StringComparison.Ordinal);
        Assert.Contains("GoToAsync(\"import\")", settingsCode, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidMinimumSdk_RemainsApi24()
    {
        string project = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "SecurePassword", "SecurePassword", "SecurePassword.csproj"));
        Assert.Matches(
            "SupportedOSPlatformVersion[^>]*android[^>]*>24\\.0</SupportedOSPlatformVersion>",
            project);
    }

    [Fact]
    public void PreAuthenticationImport_UsesSharedQrBootstrapAndManualFallback()
    {
        string xaml = ReadApplicationFile("MasterPasswordPage.xaml");
        string codeBehind = ReadApplicationFile("MasterPasswordPage.xaml.cs");

        Assert.Contains("<zxing:BarcodeGeneratorView", xaml);
        Assert.Contains("x:Name=\"ReceiverQrCode\"", xaml);
        Assert.Contains("x:Name=\"ReceiverAddressLabel\"", xaml);
        Assert.Contains("x:Name=\"ReceiverPairingCodeLabel\"", xaml);
        Assert.Contains("ReceiverPairingBootstrap.Create", codeBehind);
        Assert.Contains("ReceiverQrCode.Value = bootstrap.QrPayload", codeBehind);
    }

    private static IEnumerable<string> ExtractDefinedKeys(string xaml) =>
        Regex.Matches(xaml, "x:Key\\s*=\\s*\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value);

    private static IEnumerable<string> ExtractResourceReferences(string xaml) =>
        Regex.Matches(xaml, "\\{(?:StaticResource|DynamicResource)\\s+([^},\\s]+)")
            .Select(match => match.Groups[1].Value);

    private static IEnumerable<string> GetProjectXamlFiles()
    {
        string sourceRoot = GetApplicationPath(string.Empty);
        return Directory.GetFiles(sourceRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(sourceRoot, path))
            .OrderBy(path => path, StringComparer.Ordinal);
    }

    private static string ReadApplicationFile(string relativePath)
    {
        string sourcePath = GetApplicationPath(relativePath);
        Assert.True(File.Exists(sourcePath), $"Expected application source file was not found: {sourcePath}");
        return File.ReadAllText(sourcePath);
    }

    private static string GetApplicationPath(string relativePath) =>
        Path.Combine(GetRepositoryRoot(), "SecurePassword", "SecurePassword", relativePath);

    private static string GetRepositoryRoot() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", ".."));
}
