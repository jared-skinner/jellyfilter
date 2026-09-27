using Jellyfin.Plugin.JellyFilter.Data;

namespace Jellyfin.Plugin.JellyFilter.Configuration;

/// <summary>
/// What one user wants done with one content category.
/// </summary>
public class CategoryPreference
{
    /// <summary>
    /// Gets or sets the normalized category name, as produced by
    /// <see cref="Services.FilterFileParser.NormalizeCategory"/>.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the action to take. <see cref="FilterAction.Allow"/> means the category is
    /// switched off for this user.
    /// </summary>
    public FilterAction Action { get; set; } = FilterAction.Skip;
}
