namespace Kairix.QuickAVSync.Services;

public static class AppConstants
{
    public const string BuyMeACoffeeUrl = "https://buymeacoffee.com/dubcodes";
    public const string GitHubUrl = "https://github.com/Dubcodes/kairix-quick-av-sync";
    public const string ProjectWebsiteUrl = "https://www.dubcodesmedia.com/kairix/kairix-av-sync";
}

public static class ExternalUrlLauncher
{
    public static bool TryOpen(
        string url,
        Action<System.Diagnostics.ProcessStartInfo>? start = null,
        Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true };
            (start ?? (info => System.Diagnostics.Process.Start(info)))(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"External link failed for '{url}': {ex.Message}");
            return false;
        }
    }
}
