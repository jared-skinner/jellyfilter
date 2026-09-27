namespace Jellyfin.Plugin.JellyFilter.Data;

/// <summary>
/// What the plugin does when playback enters a filtered scene.
/// </summary>
public enum FilterAction
{
    /// <summary>
    /// Leave the scene alone.
    /// </summary>
    Allow = 0,

    /// <summary>
    /// Seek past the scene.
    /// </summary>
    Skip = 1,

    /// <summary>
    /// Mute the client for the duration of the scene, then unmute.
    /// </summary>
    Mute = 2,
}
