namespace Jellyfin.Plugin.JellyFilter.Data;

/// <summary>
/// What the plugin currently knows about one library item, for troubleshooting from the
/// configuration page.
/// </summary>
public class FilterPreviewDto
{
    /// <summary>
    /// Gets or sets the name of the library item.
    /// </summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the path of the video file.
    /// </summary>
    public string? MediaPath { get; set; }

    /// <summary>
    /// Gets or sets the filter file that was used, or <c>null</c> when none was found.
    /// </summary>
    public string? FilterPath { get; set; }

    /// <summary>
    /// Gets or sets the title declared inside the filter file, if any.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the paths that were checked, in order. Useful for working out why a filter
    /// file was not picked up.
    /// </summary>
    public IReadOnlyList<string> SearchedPaths { get; set; } = [];

    /// <summary>
    /// Gets or sets the scenes that were loaded.
    /// </summary>
    public IReadOnlyList<FilterSceneDto> Scenes { get; set; } = [];

    /// <summary>
    /// Gets or sets any entries that could not be understood.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; set; } = [];
}
