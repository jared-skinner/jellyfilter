using System.Globalization;

namespace Jellyfin.Plugin.JellyFilter.Data;

/// <summary>
/// A single span of a video that carries one kind of objectionable content.
/// </summary>
public sealed class FilterScene
{
    /// <summary>
    /// Gets or sets the content category exactly as it was written in the filter file,
    /// for example <c>sexuality</c>. Used for display.
    /// </summary>
    public string Category { get; set; } = "unknown";

    /// <summary>
    /// Gets or sets the category folded to a canonical form so that <c>Sexual Content</c>,
    /// <c>sexual-content</c> and <c>sexual_content</c> all match one preference.
    /// </summary>
    public string NormalizedCategory { get; set; } = "unknown";

    /// <summary>
    /// Gets or sets the offset into the video where the scene starts, in seconds.
    /// </summary>
    public double Start { get; set; }

    /// <summary>
    /// Gets or sets the offset into the video where the scene ends, in seconds.
    /// </summary>
    public double End { get; set; }

    /// <summary>
    /// Gets or sets the action the filter file requests for this scene. A user preference
    /// overrides this; it only applies when the user has enabled the category without
    /// choosing an action of their own.
    /// </summary>
    public FilterAction? Action { get; set; }

    /// <summary>
    /// Gets or sets an optional intensity label such as <c>mild</c> or <c>severe</c>.
    /// </summary>
    public string? Severity { get; set; }

    /// <summary>
    /// Gets or sets an optional human readable note about the scene.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets the length of the scene in seconds.
    /// </summary>
    public double DurationSeconds => Math.Max(0, End - Start);

    /// <summary>
    /// Gets a stable identifier for the scene, used to avoid acting on the same scene twice.
    /// </summary>
    public string Key => string.Create(
        CultureInfo.InvariantCulture,
        $"{NormalizedCategory}@{Start:F3}-{End:F3}");

    /// <summary>
    /// Determines whether a playback position falls inside this scene.
    /// </summary>
    /// <param name="positionSeconds">Playback position in seconds.</param>
    /// <returns><c>true</c> when the position is within the scene.</returns>
    public bool Contains(double positionSeconds) => positionSeconds >= Start && positionSeconds < End;
}
