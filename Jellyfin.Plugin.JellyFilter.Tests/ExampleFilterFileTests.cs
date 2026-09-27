using Jellyfin.Plugin.JellyFilter.Data;
using Jellyfin.Plugin.JellyFilter.Services;

namespace Jellyfin.Plugin.JellyFilter.Tests;

/// <summary>
/// Guards the example shipped in the repository against drifting away from what the parser accepts.
/// </summary>
public class ExampleFilterFileTests
{
    [Fact]
    public void The_shipped_example_parses_cleanly()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "examples", "Example (2020).filter.json");
        var filter = FilterFileParser.Parse(File.ReadAllText(path), path);

        Assert.Empty(filter.Warnings);
        Assert.Equal("Example Movie (2020)", filter.Title);
        Assert.Equal(4, filter.Scenes.Count);

        Assert.Equal(
            ["violence", "sexuality", "profanity", "gore"],
            filter.Scenes.Select(s => s.Category));

        var profanity = filter.Scenes.Single(s => s.Category == "profanity");
        Assert.Equal(FilterAction.Mute, profanity.Action);
        Assert.Equal(1862.5, profanity.Start);
    }
}
