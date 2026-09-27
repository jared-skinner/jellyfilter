using Jellyfin.Plugin.JellyFilter.Data;
using Jellyfin.Plugin.JellyFilter.Services;

namespace Jellyfin.Plugin.JellyFilter.Tests;

public class FilterFileParserTests
{
    [Fact]
    public void Parses_bare_array_of_scenes()
    {
        var filter = FilterFileParser.Parse(
            """
            [
                { "type": "sexuality", "start": 1244, "end": 1345 },
                { "type": "violence", "start": 300, "end": 330 }
            ]
            """,
            "/movies/example.filter.json");

        Assert.Equal(2, filter.Scenes.Count);

        // Scenes are sorted by start time so the playback watcher can stop scanning early.
        Assert.Equal("violence", filter.Scenes[0].Category);
        Assert.Equal(300, filter.Scenes[0].Start);
        Assert.Equal("sexuality", filter.Scenes[1].Category);
        Assert.Equal(1244, filter.Scenes[1].Start);
        Assert.Equal(1345, filter.Scenes[1].End);
        Assert.Empty(filter.Warnings);
    }

    [Fact]
    public void Parses_object_with_scene_list_and_metadata()
    {
        var filter = FilterFileParser.Parse(
            """
            {
                "version": 1,
                "title": "Example Movie",
                "scenes": [
                    {
                        "type": "profanity",
                        "start": "00:20:44",
                        "end": "00:20:47.5",
                        "action": "mute",
                        "severity": "mild",
                        "description": "one word"
                    }
                ]
            }
            """,
            "/movies/example.filter.json");

        Assert.Equal("Example Movie", filter.Title);
        var scene = Assert.Single(filter.Scenes);
        Assert.Equal("profanity", scene.Category);
        Assert.Equal(1244, scene.Start);
        Assert.Equal(1247.5, scene.End);
        Assert.Equal(FilterAction.Mute, scene.Action);
        Assert.Equal("mild", scene.Severity);
        Assert.Equal("one word", scene.Description);
    }

    [Fact]
    public void Accepts_alternative_field_names()
    {
        var filter = FilterFileParser.Parse(
            """
            { "segments": [ { "category": "gore", "startTime": 10, "endTime": 20 } ] }
            """,
            "/movies/example.filter.json");

        var scene = Assert.Single(filter.Scenes);
        Assert.Equal("gore", scene.Category);
        Assert.Equal(10, scene.Start);
        Assert.Equal(20, scene.End);
    }

    [Fact]
    public void Tolerates_comments_and_trailing_commas()
    {
        var filter = FilterFileParser.Parse(
            """
            [
                // the barn scene
                { "type": "violence", "start": 5, "end": 9 },
            ]
            """,
            "/movies/example.filter.json");

        Assert.Single(filter.Scenes);
    }

    [Fact]
    public void Drops_unusable_scenes_and_explains_why()
    {
        var filter = FilterFileParser.Parse(
            """
            [
                { "type": "violence", "start": 10, "end": 5 },
                { "type": "violence", "end": 5 },
                { "type": "violence", "start": 1, "end": 5 }
            ]
            """,
            "/movies/example.filter.json");

        Assert.Single(filter.Scenes);
        Assert.Equal(2, filter.Warnings.Count);
        Assert.Contains(filter.Warnings, w => w.Contains("not after start", StringComparison.Ordinal));
        Assert.Contains(filter.Warnings, w => w.Contains("start time", StringComparison.Ordinal));
    }

    [Fact]
    public void Reports_a_file_with_no_scene_list()
    {
        var filter = FilterFileParser.Parse("""{ "title": "nothing here" }""", "/movies/example.filter.json");

        Assert.False(filter.HasScenes);
        Assert.Single(filter.Warnings);
    }

    [Theory]
    [InlineData("90", 90)]
    [InlineData("1:30", 90)]
    [InlineData("01:30", 90)]
    [InlineData("1:00:00", 3600)]
    [InlineData("00:20:44.5", 1244.5)]
    public void Parses_time_codes(string raw, double expected)
    {
        Assert.True(FilterFileParser.TryParseTimeCode(raw, out var seconds));
        Assert.Equal(expected, seconds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1:2:3:4")]
    [InlineData("1.5:30")]
    public void Rejects_unparseable_time_codes(string raw)
    {
        Assert.False(FilterFileParser.TryParseTimeCode(raw, out _));
    }

    [Theory]
    [InlineData("Sexual Content", "sexual content")]
    [InlineData("sexual-content", "sexual content")]
    [InlineData("SEXUAL_CONTENT", "sexual content")]
    [InlineData("  violence  ", "violence")]
    [InlineData("", "unknown")]
    [InlineData("!!!", "unknown")]
    public void Normalizes_categories(string raw, string expected)
    {
        Assert.Equal(expected, FilterFileParser.NormalizeCategory(raw));
    }
}
