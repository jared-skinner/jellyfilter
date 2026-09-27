namespace Jellyfin.Plugin.JellyFilter.Data;

/// <summary>
/// A scene as returned by the API.
/// </summary>
public class FilterSceneDto
{
    /// <summary>
    /// Gets or sets the category as written in the filter file.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the category folded to its canonical form.
    /// </summary>
    public string NormalizedCategory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the start offset in seconds.
    /// </summary>
    public double Start { get; set; }

    /// <summary>
    /// Gets or sets the end offset in seconds.
    /// </summary>
    public double End { get; set; }

    /// <summary>
    /// Gets or sets the action requested by the filter file, if any.
    /// </summary>
    public FilterAction? Action { get; set; }

    /// <summary>
    /// Gets or sets the severity label, if any.
    /// </summary>
    public string? Severity { get; set; }

    /// <summary>
    /// Gets or sets the description, if any.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Creates a DTO from a scene.
    /// </summary>
    /// <param name="scene">Source scene.</param>
    /// <returns>The DTO.</returns>
    public static FilterSceneDto FromScene(FilterScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        return new FilterSceneDto
        {
            Category = scene.Category,
            NormalizedCategory = scene.NormalizedCategory,
            Start = scene.Start,
            End = scene.End,
            Action = scene.Action,
            Severity = scene.Severity,
            Description = scene.Description,
        };
    }
}
