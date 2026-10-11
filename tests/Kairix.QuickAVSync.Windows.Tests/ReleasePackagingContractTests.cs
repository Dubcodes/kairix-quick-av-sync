using System.IO;

namespace Kairix.QuickAVSync.Windows.Tests;

public sealed class ReleasePackagingContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void PackagerProducesTheExactStablePublicAssetContractInsideTheRepository()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot, "scripts", "package-windows.ps1"));

        Assert.Contains("[ValidateSet('Release', 'Development')]", script);
        Assert.Contains("artifacts/staging", script);
        Assert.DoesNotContain("GetTempPath", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Kairix.QuickAVSync-$BuildId-win-x64.exe", script);
        Assert.Contains("Kairix-Quick-AV-Sync-$BuildId-win-x64.zip", script);
        Assert.Contains("Kairix-AV-Sync-Test-Signal.html", script);
        Assert.Contains("$publicAssets = @($selfContainedAsset, $zipAsset, $htmlAsset)", script);
        Assert.Contains("Packaged browser test signal differs from the canonical repository file", script);
    }

    [Fact]
    public void StableReleaseWorkflowVerifiesExactNamesChecksumsAndReleaseChannel()
    {
        var workflow = File.ReadAllText(Path.Combine(RepositoryRoot, ".github", "workflows", "release.yml"));

        Assert.Contains("-BuildChannel Release", workflow);
        Assert.Contains("Kairix.QuickAVSync-$tag-win-x64.exe", workflow);
        Assert.Contains("Kairix-Quick-AV-Sync-$tag-win-x64.zip", workflow);
        Assert.Contains("Kairix-AV-Sync-Test-Signal.html", workflow);
        Assert.Contains("SHA256SUMS.txt", workflow);
        Assert.Contains("does not cover the exact three public assets", workflow);
        Assert.Contains("Create release and upload verified assets", workflow);
        Assert.DoesNotContain("Create prerelease", workflow);
    }

    [Fact]
    public void StableReleaseIdentityHasNoPrereleaseQualifier()
    {
        var project = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "Kairix.QuickAVSync", "Kairix.QuickAVSync.csproj"));
        var releaseNotes = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "RELEASE_NOTES_v1.0.0.md"));

        Assert.Contains("<VersionPrefix>1.0.0</VersionPrefix>", project);
        Assert.DoesNotContain("alpha", releaseNotes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("beta", releaseNotes, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("release candidate", releaseNotes, StringComparison.OrdinalIgnoreCase);
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
