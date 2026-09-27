using System.Collections.Concurrent;
using Jellyfin.Plugin.JellyFilter.Data;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyFilter.Services;

/// <summary>
/// Finds the filter file that belongs to a media file and keeps the parsed result cached.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="FilterFileRepository"/> class.
/// </remarks>
/// <param name="logger">Logger.</param>
public sealed class FilterFileRepository(ILogger<FilterFileRepository> logger)
{
    /// <summary>
    /// How long a lookup result is trusted before the file is stat'd again. Playback polling runs
    /// several times a second, so without this the plugin would hit the disk constantly; five
    /// seconds still means an edited filter file takes effect during the same playback.
    /// </summary>
    private static readonly TimeSpan _revalidateAfter = TimeSpan.FromSeconds(5);

    private readonly ILogger<FilterFileRepository> _logger = logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the filter for a media file, reading it from disk if needed.
    /// </summary>
    /// <param name="mediaPath">Full path of the video file.</param>
    /// <returns>The filter, or <see cref="MediaFilter.Empty"/> when no sidecar file exists.</returns>
    public MediaFilter GetForMediaPath(string? mediaPath)
    {
        if (string.IsNullOrEmpty(mediaPath))
        {
            return MediaFilter.Empty;
        }

        var now = DateTime.UtcNow;
        if (_cache.TryGetValue(mediaPath, out var cached) && now - cached.CheckedAt < _revalidateAfter)
        {
            return cached.Filter;
        }

        var filter = Load(mediaPath, cached);
        _cache[mediaPath] = new CacheEntry(filter, now);
        return filter;
    }

    /// <summary>
    /// Drops the cached filter for one media file so the next lookup re-reads it.
    /// </summary>
    /// <param name="mediaPath">Full path of the video file.</param>
    public void Invalidate(string? mediaPath)
    {
        if (!string.IsNullOrEmpty(mediaPath))
        {
            _cache.TryRemove(mediaPath, out _);
        }
    }

    /// <summary>
    /// Drops every cached filter.
    /// </summary>
    public void InvalidateAll() => _cache.Clear();

    /// <summary>
    /// Lists the paths that would be checked for a given media file, in priority order.
    /// Exposed so the configuration page can explain why a file was not picked up.
    /// </summary>
    /// <param name="mediaPath">Full path of the video file.</param>
    /// <returns>Candidate filter file paths.</returns>
    public static IReadOnlyList<string> GetCandidatePaths(string mediaPath)
    {
        var candidates = new List<string>();
        var directory = Path.GetDirectoryName(mediaPath);
        if (string.IsNullOrEmpty(directory))
        {
            return candidates;
        }

        var fileName = Path.GetFileName(mediaPath);
        var baseName = Path.GetFileNameWithoutExtension(mediaPath);
        var configuration = Plugin.Instance?.Configuration;
        var patterns = configuration?.FilterFileNamePatterns;
        if (patterns is null || patterns.Length == 0)
        {
            patterns = PluginConfigurationDefaults.FilterFileNamePatterns;
        }

        foreach (var pattern in patterns)
        {
            var resolved = pattern
                .Replace("{name}", baseName, StringComparison.OrdinalIgnoreCase)
                .Replace("{filename}", fileName, StringComparison.OrdinalIgnoreCase);

            candidates.Add(Path.Combine(directory, resolved));
        }

        var externalDirectory = configuration?.ExternalFilterDirectory;
        if (!string.IsNullOrWhiteSpace(externalDirectory))
        {
            foreach (var pattern in patterns)
            {
                var resolved = pattern
                    .Replace("{name}", baseName, StringComparison.OrdinalIgnoreCase)
                    .Replace("{filename}", fileName, StringComparison.OrdinalIgnoreCase);

                candidates.Add(Path.Combine(externalDirectory, resolved));
            }

            candidates.Add(Path.Combine(externalDirectory, baseName + ".json"));
        }

        return candidates;
    }

    private MediaFilter Load(string mediaPath, CacheEntry? previous)
    {
        FileInfo? found = null;
        foreach (var candidate in GetCandidatePaths(mediaPath))
        {
            // A directory-level filter file must not win over a per-file one, which the
            // candidate ordering already guarantees; take the first that exists.
            var info = new FileInfo(candidate);
            if (info.Exists)
            {
                found = info;
                break;
            }
        }

        if (found is null)
        {
            return MediaFilter.Empty;
        }

        // Reuse the parse when the file has not changed since the last read.
        var existing = previous?.Filter;
        if (existing is not null
            && string.Equals(existing.SourcePath, found.FullName, StringComparison.Ordinal)
            && existing.LastWriteTimeUtc == found.LastWriteTimeUtc
            && existing.Length == found.Length)
        {
            return existing;
        }

        try
        {
            var json = File.ReadAllText(found.FullName);
            var filter = FilterFileParser.Parse(json, found.FullName);
            filter.LastWriteTimeUtc = found.LastWriteTimeUtc;
            filter.Length = found.Length;

            foreach (var warning in filter.Warnings)
            {
                _logger.LogWarning("Filter file {Path}: {Warning}", found.FullName, warning);
            }

            _logger.LogDebug(
                "Loaded {Count} scene(s) from {Path} for {MediaPath}",
                filter.Scenes.Count,
                found.FullName,
                mediaPath);

            return filter;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _logger.LogError(ex, "Unable to read filter file {Path}", found.FullName);
            return MediaFilter.Empty;
        }
    }

    private sealed record CacheEntry(MediaFilter Filter, DateTime CheckedAt);
}
