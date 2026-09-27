using Jellyfin.Plugin.JellyFilter.Data;
using Jellyfin.Plugin.JellyFilter.Services;

namespace Jellyfin.Plugin.JellyFilter.Configuration;

/// <summary>
/// One user's filtering choices.
/// </summary>
public class UserFilterPreferences
{
    /// <summary>
    /// Gets or sets the Jellyfin user id, without dashes.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether filtering runs for this user at all. Turning this
    /// off is a master switch that leaves the per-category choices intact.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the per-category choices. Categories absent from this array are not filtered.
    /// </summary>
    public CategoryPreference[] Categories { get; set; } = [];

    /// <summary>
    /// Works out what to do with a scene for this user.
    /// </summary>
    /// <param name="scene">The scene under consideration.</param>
    /// <returns>
    /// The action to take. <see cref="FilterAction.Allow"/> means the scene plays untouched.
    /// </returns>
    public FilterAction ResolveAction(FilterScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (!Enabled)
        {
            return FilterAction.Allow;
        }

        foreach (var preference in Categories)
        {
            if (string.Equals(
                    FilterFileParser.NormalizeCategory(preference.Category),
                    scene.NormalizedCategory,
                    StringComparison.Ordinal))
            {
                // A scene may ask to be muted rather than cut, which matters for a stray
                // profanity inside an otherwise fine stretch of film. Honour that request only
                // when the user asked for the broader action of skipping.
                if (preference.Action == FilterAction.Skip && scene.Action == FilterAction.Mute)
                {
                    return FilterAction.Mute;
                }

                return preference.Action;
            }
        }

        return FilterAction.Allow;
    }
}
