using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellyFilter.Configuration;

/// <summary>
/// Plugin settings.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether the server watches playback and seeks past
    /// filtered scenes. This is the mechanism that works on every client.
    /// </summary>
    public bool EnableAutoSkip { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether scenes are also published as Jellyfin media
    /// segments. Segments are per item rather than per user, so clients that act on them will
    /// offer to skip every published scene regardless of who is watching.
    /// </summary>
    public bool EnableMediaSegments { get; set; }

    /// <summary>
    /// Gets or sets the segment type reported for published scenes. Jellyfin's segment vocabulary
    /// has no entry for content warnings, so scenes have to borrow one of the existing types.
    /// </summary>
    public MediaSegmentType PublishedSegmentType { get; set; } = MediaSegmentType.Commercial;

    /// <summary>
    /// Gets or sets how often playback positions are checked, in milliseconds. Lower values cut
    /// closer to the start of a scene at the cost of more polling.
    /// </summary>
    public int PollIntervalMs { get; set; } = 400;

    /// <summary>
    /// Gets or sets a value indicating whether a short message is shown on the client when a
    /// scene is skipped.
    /// </summary>
    public bool ShowSkipNotification { get; set; } = true;

    /// <summary>
    /// Gets or sets the message shown when a scene is skipped. <c>{category}</c> is replaced with
    /// the scene's category.
    /// </summary>
    public string SkipNotificationFormat { get; set; } = "Skipped {category}";

    /// <summary>
    /// Gets or sets how far past the end of a scene playback resumes, in seconds. A small amount
    /// of padding keeps a slow client from landing back inside the scene it just left.
    /// </summary>
    public double SkipPaddingSeconds { get; set; } = 0.5;

    /// <summary>
    /// Gets or sets the shortest scene that will be acted on, in seconds. Scenes below this are
    /// ignored because a seek would be more disruptive than the content.
    /// </summary>
    public double MinimumSceneSeconds { get; set; } = 0.5;

    /// <summary>
    /// Gets or sets the filename patterns searched next to a video file, in priority order.
    /// </summary>
    public string[] FilterFileNamePatterns { get; set; } = PluginConfigurationDefaults.FilterFileNamePatterns;

    /// <summary>
    /// Gets or sets an optional directory searched in addition to the video's own folder, for
    /// libraries that are mounted read-only.
    /// </summary>
    public string ExternalFilterDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the categories offered in the configuration page. Categories seen in filter
    /// files are appended to this list as they are encountered.
    /// </summary>
    public string[] KnownCategories { get; set; } = PluginConfigurationDefaults.KnownCategories;

    /// <summary>
    /// Gets or sets the per-user filtering choices.
    /// </summary>
    public UserFilterPreferences[] UserPreferences { get; set; } = [];

    /// <summary>
    /// Finds the stored preferences for a user.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <returns>The preferences, or <c>null</c> when the user has never configured filtering.</returns>
    public UserFilterPreferences? FindUser(Guid userId)
    {
        var key = userId.ToString("N");
        foreach (var preferences in UserPreferences)
        {
            if (string.Equals(preferences.UserId.Replace("-", string.Empty, StringComparison.Ordinal), key, StringComparison.OrdinalIgnoreCase))
            {
                return preferences;
            }
        }

        return null;
    }
}
