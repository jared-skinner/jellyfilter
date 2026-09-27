using Jellyfin.Plugin.JellyFilter.Configuration;
using Jellyfin.Plugin.JellyFilter.Data;
using Jellyfin.Plugin.JellyFilter.Services;

namespace Jellyfin.Plugin.JellyFilter.Tests;

public class SceneSelectionTests
{
    private static readonly PluginConfiguration _configuration = new()
    {
        MinimumSceneSeconds = 0.5,
        SkipPaddingSeconds = 0,
    };

    private static MediaFilter BuildFilter(params (string Category, double Start, double End)[] scenes)
    {
        var filter = new MediaFilter();
        foreach (var (category, start, end) in scenes.OrderBy(s => s.Start))
        {
            filter.Scenes.Add(new FilterScene
            {
                Category = category,
                NormalizedCategory = FilterFileParser.NormalizeCategory(category),
                Start = start,
                End = end,
            });
        }

        return filter;
    }

    private static UserFilterPreferences BuildPreferences(params (string Category, FilterAction Action)[] categories)
        => new()
        {
            UserId = Guid.NewGuid().ToString("N"),
            Enabled = true,
            Categories = categories
                .Select(c => new CategoryPreference { Category = c.Category, Action = c.Action })
                .ToArray(),
        };

    [Fact]
    public void Finds_the_scene_covering_the_current_position()
    {
        var filter = BuildFilter(("violence", 100, 200), ("sexuality", 300, 400));
        var preferences = BuildPreferences(("sexuality", FilterAction.Skip), ("violence", FilterAction.Skip));

        var scene = PlaybackFilterService.FindScene(filter, preferences, _configuration, 350, out var action);

        Assert.NotNull(scene);
        Assert.Equal("sexuality", scene.Category);
        Assert.Equal(FilterAction.Skip, action);
    }

    [Fact]
    public void Ignores_categories_the_user_did_not_choose()
    {
        var filter = BuildFilter(("violence", 100, 200));
        var preferences = BuildPreferences(("sexuality", FilterAction.Skip));

        var scene = PlaybackFilterService.FindScene(filter, preferences, _configuration, 150, out var action);

        Assert.Null(scene);
        Assert.Equal(FilterAction.Allow, action);
    }

    [Fact]
    public void Ignores_everything_when_the_user_switch_is_off()
    {
        var filter = BuildFilter(("violence", 100, 200));
        var preferences = BuildPreferences(("violence", FilterAction.Skip));
        preferences.Enabled = false;

        Assert.Null(PlaybackFilterService.FindScene(filter, preferences, _configuration, 150, out _));
    }

    [Fact]
    public void Ignores_scenes_shorter_than_the_configured_minimum()
    {
        var filter = BuildFilter(("profanity", 100, 100.2));
        var preferences = BuildPreferences(("profanity", FilterAction.Skip));

        Assert.Null(PlaybackFilterService.FindScene(filter, preferences, _configuration, 100.1, out _));
    }

    [Fact]
    public void Skipping_wins_over_muting_when_scenes_overlap()
    {
        var filter = BuildFilter(("profanity", 100, 200), ("violence", 120, 180));
        var preferences = BuildPreferences(("profanity", FilterAction.Mute), ("violence", FilterAction.Skip));

        var scene = PlaybackFilterService.FindScene(filter, preferences, _configuration, 150, out var action);

        Assert.NotNull(scene);
        Assert.Equal("violence", scene.Category);
        Assert.Equal(FilterAction.Skip, action);
    }

    [Fact]
    public void A_scene_asking_to_be_muted_is_muted_even_when_the_user_asked_to_skip()
    {
        var scene = new FilterScene
        {
            Category = "profanity",
            NormalizedCategory = "profanity",
            Start = 10,
            End = 12,
            Action = FilterAction.Mute,
        };

        var preferences = BuildPreferences(("profanity", FilterAction.Skip));

        Assert.Equal(FilterAction.Mute, preferences.ResolveAction(scene));
    }

    [Fact]
    public void Category_preferences_match_regardless_of_spelling()
    {
        var scene = new FilterScene
        {
            Category = "Sexual Content",
            NormalizedCategory = FilterFileParser.NormalizeCategory("Sexual Content"),
            Start = 10,
            End = 20,
        };

        var preferences = BuildPreferences(("sexual-content", FilterAction.Skip));

        Assert.Equal(FilterAction.Skip, preferences.ResolveAction(scene));
    }

    [Fact]
    public void Back_to_back_scenes_are_skipped_in_a_single_seek()
    {
        var filter = BuildFilter(("violence", 100, 200), ("gore", 190, 260), ("gore", 255, 300));
        var preferences = BuildPreferences(("violence", FilterAction.Skip), ("gore", FilterAction.Skip));
        var scene = filter.Scenes[0];

        Assert.Equal(300, PlaybackFilterService.ResolveSkipTarget(filter, preferences, _configuration, scene));
    }

    [Fact]
    public void A_following_scene_the_user_allows_does_not_extend_the_seek()
    {
        var filter = BuildFilter(("violence", 100, 200), ("smoking", 190, 260));
        var preferences = BuildPreferences(("violence", FilterAction.Skip));
        var scene = filter.Scenes[0];

        Assert.Equal(200, PlaybackFilterService.ResolveSkipTarget(filter, preferences, _configuration, scene));
    }

    [Fact]
    public void A_gap_between_scenes_stops_the_seek_from_extending()
    {
        var filter = BuildFilter(("violence", 100, 200), ("violence", 210, 260));
        var preferences = BuildPreferences(("violence", FilterAction.Skip));
        var scene = filter.Scenes[0];

        Assert.Equal(200, PlaybackFilterService.ResolveSkipTarget(filter, preferences, _configuration, scene));
    }

    [Fact]
    public void Padding_is_added_to_the_seek_target()
    {
        var filter = BuildFilter(("violence", 100, 200));
        var preferences = BuildPreferences(("violence", FilterAction.Skip));
        var configuration = new PluginConfiguration { MinimumSceneSeconds = 0.5, SkipPaddingSeconds = 1.5 };

        Assert.Equal(201.5, PlaybackFilterService.ResolveSkipTarget(filter, preferences, configuration, filter.Scenes[0]));
    }
}
