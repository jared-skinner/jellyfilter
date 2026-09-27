using System.Collections.ObjectModel;

namespace Jellyfin.Plugin.JellyFilter.Data;

/// <summary>
/// The parsed contents of one sidecar filter file.
/// </summary>
public sealed class MediaFilter
{
    /// <summary>
    /// A filter that matches nothing, used for media with no sidecar file.
    /// </summary>
    public static readonly MediaFilter Empty = new();

    /// <summary>
    /// Gets or sets the full path of the filter file this was read from, or <c>null</c> when empty.
    /// </summary>
    public string? SourcePath { get; set; }

    /// <summary>
    /// Gets or sets the last write time of the filter file, used to detect edits on disk.
    /// </summary>
    public DateTime LastWriteTimeUtc { get; set; }

    /// <summary>
    /// Gets or sets the size of the filter file in bytes, used alongside the timestamp to detect edits.
    /// </summary>
    public long Length { get; set; }

    /// <summary>
    /// Gets or sets the optional title declared in the filter file.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets the scenes, sorted by start time.
    /// </summary>
    public Collection<FilterScene> Scenes { get; } = new();

    /// <summary>
    /// Gets messages about entries that were skipped because they could not be understood.
    /// Surfaced through the API so a bad filter file is diagnosable without reading the server log.
    /// </summary>
    public Collection<string> Warnings { get; } = new();

    /// <summary>
    /// Gets a value indicating whether this filter has any usable scenes.
    /// </summary>
    public bool HasScenes => Scenes.Count > 0;
}
