using System.Reflection;

namespace Kairix.QuickAVSync.Services;

public sealed record BuildIdentity(string BuildId, string SourceCommit, string Channel, string SemanticVersion)
{
    public static BuildIdentity Current { get; } = FromAssembly(Assembly.GetExecutingAssembly());

    public string ShortCommit => SourceCommit.Length >= 7 ? SourceCommit[..7] : SourceCommit;

    internal static BuildIdentity FromAssembly(Assembly assembly)
    {
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .GroupBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return new BuildIdentity(
            ValueOrDefault(metadata, "BuildId", "local-development"),
            ValueOrDefault(metadata, "SourceCommit", "unknown"),
            ValueOrDefault(metadata, "BuildChannel", "Development"),
            ValueOrDefault(metadata, "SemanticVersion", SemanticVersionOrUnknown(informationalVersion)));
    }

    private static string ValueOrDefault(IReadOnlyDictionary<string, string> metadata, string key, string fallback) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static string SemanticVersionOrUnknown(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion)) return "unknown";
        var metadataSeparator = informationalVersion.IndexOf('+');
        return metadataSeparator < 0 ? informationalVersion : informationalVersion[..metadataSeparator];
    }
}
