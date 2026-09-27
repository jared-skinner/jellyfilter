namespace Jellyfin.Plugin.JellyFilter;

/// <summary>
/// Defaults shared by the configuration object and by code that runs before the plugin
/// instance exists.
/// </summary>
public static class PluginConfigurationDefaults
{
    /// <summary>
    /// Gets the filename patterns searched next to a video file, in priority order.
    /// <c>{name}</c> expands to the video filename without its extension and <c>{filename}</c>
    /// expands to the full filename.
    /// </summary>
    public static string[] FilterFileNamePatterns =>
    [
        "{name}.jellyfilter.json",
        "{name}.filter.json",
        "{filename}.jellyfilter.json",
        "{filename}.filter.json",
        "jellyfilter.json",
    ];

    /// <summary>
    /// Gets the categories offered in the configuration page before any filter files have been
    /// seen. Categories found in real filter files are added to this list automatically.
    /// </summary>
    public static string[] KnownCategories =>
    [
        "sexuality",
        "nudity",
        "violence",
        "gore",
        "profanity",
        "blasphemy",
        "drugs",
        "alcohol",
        "smoking",
        "frightening",
        "disturbing imagery",
        "discrimination",
        "self harm",
        "gambling",
        "flashing lights",
    ];
}
