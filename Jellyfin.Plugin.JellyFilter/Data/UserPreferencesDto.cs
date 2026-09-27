using Jellyfin.Plugin.JellyFilter.Configuration;

namespace Jellyfin.Plugin.JellyFilter.Data;

/// <summary>
/// A user together with their filtering choices.
/// </summary>
public class UserPreferencesDto
{
    /// <summary>
    /// Gets or sets the user id, without dashes.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name of the user.
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether filtering runs for this user.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the per-category choices.
    /// </summary>
    public CategoryPreference[] Categories { get; set; } = [];
}
