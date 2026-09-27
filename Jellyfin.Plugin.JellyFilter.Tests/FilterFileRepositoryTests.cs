using Jellyfin.Plugin.JellyFilter.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.JellyFilter.Tests;

public class FilterFileRepositoryTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("jellyfilter").FullName;

    public void Dispose()
    {
        Directory.Delete(_directory, true);
        GC.SuppressFinalize(this);
    }

    private FilterFileRepository CreateRepository() => new(NullLogger<FilterFileRepository>.Instance);

    private string WriteMedia(string fileName = "Example (2020).mkv")
    {
        var path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, "not really a video");
        return path;
    }

    [Fact]
    public void Finds_a_sidecar_named_after_the_video()
    {
        var media = WriteMedia();
        File.WriteAllText(
            Path.Combine(_directory, "Example (2020).filter.json"),
            """[{ "type": "sexuality", "start": 1244, "end": 1345 }]""");

        var filter = CreateRepository().GetForMediaPath(media);

        Assert.True(filter.HasScenes);
        Assert.Equal("sexuality", filter.Scenes[0].Category);
    }

    [Fact]
    public void Finds_a_sidecar_that_keeps_the_video_extension()
    {
        var media = WriteMedia();
        File.WriteAllText(
            Path.Combine(_directory, "Example (2020).mkv.jellyfilter.json"),
            """[{ "type": "violence", "start": 1, "end": 2 }]""");

        Assert.True(CreateRepository().GetForMediaPath(media).HasScenes);
    }

    [Fact]
    public void Falls_back_to_a_folder_level_filter_file()
    {
        var media = WriteMedia();
        File.WriteAllText(
            Path.Combine(_directory, "jellyfilter.json"),
            """[{ "type": "violence", "start": 1, "end": 2 }]""");

        Assert.True(CreateRepository().GetForMediaPath(media).HasScenes);
    }

    [Fact]
    public void Prefers_the_per_file_sidecar_over_the_folder_level_one()
    {
        var media = WriteMedia();
        File.WriteAllText(
            Path.Combine(_directory, "Example (2020).jellyfilter.json"),
            """[{ "type": "sexuality", "start": 1, "end": 2 }]""");
        File.WriteAllText(
            Path.Combine(_directory, "jellyfilter.json"),
            """[{ "type": "violence", "start": 1, "end": 2 }]""");

        Assert.Equal("sexuality", CreateRepository().GetForMediaPath(media).Scenes[0].Category);
    }

    [Fact]
    public void Returns_an_empty_filter_when_nothing_is_alongside_the_video()
    {
        Assert.False(CreateRepository().GetForMediaPath(WriteMedia()).HasScenes);
    }

    [Fact]
    public void Returns_an_empty_filter_for_media_with_no_path()
    {
        Assert.False(CreateRepository().GetForMediaPath(null).HasScenes);
    }

    [Fact]
    public void Survives_a_malformed_filter_file()
    {
        var media = WriteMedia();
        File.WriteAllText(Path.Combine(_directory, "Example (2020).filter.json"), "{ this is not json");

        Assert.False(CreateRepository().GetForMediaPath(media).HasScenes);
    }

    [Fact]
    public void Rereads_a_filter_file_after_it_changes_on_disk()
    {
        var media = WriteMedia();
        var filterPath = Path.Combine(_directory, "Example (2020).filter.json");
        File.WriteAllText(filterPath, """[{ "type": "violence", "start": 1, "end": 2 }]""");

        var repository = CreateRepository();
        Assert.Single(repository.GetForMediaPath(media).Scenes);

        File.WriteAllText(
            filterPath,
            """[{ "type": "violence", "start": 1, "end": 2 }, { "type": "gore", "start": 5, "end": 9 }]""");

        // The cache holds a result for a few seconds before stat'ing again, so an explicit
        // invalidation is what the API endpoint and the dashboard button rely on.
        repository.Invalidate(media);

        Assert.Equal(2, repository.GetForMediaPath(media).Scenes.Count);
    }

    [Fact]
    public void Lists_the_paths_it_searched()
    {
        var media = WriteMedia();
        var candidates = FilterFileRepository.GetCandidatePaths(media);

        Assert.Contains(Path.Combine(_directory, "Example (2020).jellyfilter.json"), candidates);
        Assert.Contains(Path.Combine(_directory, "Example (2020).filter.json"), candidates);
        Assert.Contains(Path.Combine(_directory, "jellyfilter.json"), candidates);
    }
}
