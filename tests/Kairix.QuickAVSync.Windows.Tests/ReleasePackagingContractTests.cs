using System.IO;

namespace Kairix.QuickAVSync.Windows.Tests;

public sealed class ReleasePackagingContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void PackagerProducesTheExactPublicAssetContractWithExplicitSemanticIdentity()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot, "scripts", "package-windows.ps1"));

        Assert.Contains("[ValidateSet('Development', 'Beta', 'Release')]", script);
        Assert.Contains("[string] $SemanticVersion", script);
        Assert.Contains("-p:SemanticVersion=$SemanticVersion", script);
        Assert.Contains("artifacts/staging", script);
        Assert.DoesNotContain("GetTempPath", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("--no-restore", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-p:NuGetAudit=false", script);
        Assert.Contains("Kairix.QuickAVSync-$BuildId-win-x64.exe", script);
        Assert.Contains("Kairix-Quick-AV-Sync-$BuildId-win-x64.zip", script);
        Assert.Contains("Kairix-AV-Sync-Test-Signal.html", script);
        Assert.Contains("$publicAssets = @($selfContainedAsset, $zipAsset, $htmlAsset)", script);
        Assert.Contains("Packaged browser test signal differs from the canonical repository file", script);

        var betaNames = script.Replace("$BuildId", "v1.0.0-beta.1", StringComparison.Ordinal);
        Assert.Contains("Kairix.QuickAVSync-v1.0.0-beta.1-win-x64.exe", betaNames);
        Assert.Contains("Kairix-Quick-AV-Sync-v1.0.0-beta.1-win-x64.zip", betaNames);
    }

    [Fact]
    public void TaggedWorkflowVerifiesExactNamesChecksumsAndBetaOrReleaseChannel()
    {
        var workflow = File.ReadAllText(Path.Combine(RepositoryRoot, ".github", "workflows", "release.yml"));

        Assert.Contains("$buildChannel = 'Beta'", workflow);
        Assert.Contains("$buildChannel = 'Release'", workflow);
        Assert.Contains("-BuildChannel '${{ steps.identity.outputs.build_channel }}'", workflow);
        Assert.Contains("-SemanticVersion '${{ steps.identity.outputs.semantic_version }}'", workflow);
        Assert.Contains("Kairix.QuickAVSync-$tag-win-x64.exe", workflow);
        Assert.Contains("Kairix-Quick-AV-Sync-$tag-win-x64.zip", workflow);
        Assert.Contains("Kairix-AV-Sync-Test-Signal.html", workflow);
        Assert.Contains("SHA256SUMS.txt", workflow);
        Assert.Contains("does not cover the exact three public assets", workflow);
        Assert.Contains("Create release and upload verified assets", workflow);
        Assert.Contains("Kairix Quick A/V Sync $tag", workflow);
        Assert.DoesNotContain("Create prerelease", workflow);
    }

    [Fact]
    public void BetaIdentityAndAboutPresentationExposeThePrereleaseVersion()
    {
        var project = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "Kairix.QuickAVSync", "Kairix.QuickAVSync.csproj"));
        var identity = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "Kairix.QuickAVSync", "Services", "BuildIdentity.cs"));
        var viewModel = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "Kairix.QuickAVSync", "ViewModels", "MainViewModel.cs"));
        var window = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "Kairix.QuickAVSync", "MainWindow.xaml"));

        Assert.Contains("<SemanticVersion Condition=\"'$(SemanticVersion)' == ''\">1.0.0-beta.1</SemanticVersion>", project);
        Assert.Contains("<FileVersion>1.0.0.0</FileVersion>", project);
        Assert.Contains("<InformationalVersion>$(SemanticVersion)</InformationalVersion>", project);
        Assert.Contains("SemanticVersion", identity);
        Assert.Contains("BuildIdentity.Current.SemanticVersion", viewModel);
        Assert.Contains("BuildChannelText", window);
        Assert.Contains("StringFormat=Version {0}", window);
        Assert.DoesNotContain("StringFormat=Assembly version", window);
    }

    [Fact]
    public void BetaReleaseNotesAndReadmePointToTheExactPublicDownload()
    {
        var readme = File.ReadAllText(Path.Combine(RepositoryRoot, "README.md"));
        var betaNotes = Path.Combine(RepositoryRoot, "docs", "RELEASE_NOTES_v1.0.0-beta.1.md");
        var stableDraft = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "RELEASE_NOTES_v1.0.0.md"));
        const string expectedUrl = "https://github.com/Dubcodes/kairix-quick-av-sync/releases/download/v1.0.0-beta.1/Kairix.QuickAVSync-v1.0.0-beta.1-win-x64.exe";

        Assert.True(File.Exists(betaNotes));
        Assert.Contains("### Current public beta", readme);
        Assert.Contains(expectedUrl, readme);
        Assert.DoesNotContain("### Stable release", readme);
        Assert.StartsWith("# Draft: Kairix Quick A/V Sync v1.0.0", stableDraft);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Kairix.QuickAVSync.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found from the test output directory.");
    }
}
