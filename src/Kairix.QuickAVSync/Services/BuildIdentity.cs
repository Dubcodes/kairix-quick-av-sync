using System.Reflection;

namespace Kairix.QuickAVSync.Services;

public sealed record BuildIdentity(string BuildId, string SourceCommit, string Channel)
{
    public static BuildIdentity Current { get; } = FromAssembly(Assembly.GetExecutingAssembly());

    public string ShortCommit => SourceCommit.Length >= 7 ? SourceCommit[..7] : SourceCommit;

    internal static BuildIdentity FromAssembly(Assembly assembly)
    {
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .GroupBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        return new BuildIdentity(
            ValueOrDefault(metadata, "BuildId", "local-development"),
            ValueOrDefault(metadata, "SourceCommit", "unknown"),
            ValueOrDefault(metadata, "BuildChannel", "Development"));
    }

    private static string ValueOrDefault(IReadOnlyDictionary<string, string> metadata, string key, string fallback) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
}
