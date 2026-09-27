using Jellyfin.Plugin.JellyFilter.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Model;
using MediaBrowser.Model.MediaSegments;

namespace Jellyfin.Plugin.JellyFilter.Providers;

/// <summary>
/// Publishes filtered scenes as Jellyfin media segments, so clients that understand segments can
/// offer their own skip affordance.
/// </summary>
/// <remarks>
/// <para>
/// Segments belong to an item rather than to a viewer, so everything published here is visible to
/// every user of the server. That is why this is off by default and why the server-side watcher,
/// which can tell users apart, remains the primary mechanism.
/// </para>
/// <para>
/// Initializes a new instance of the <see cref="FilterSegmentProvider"/> class.
/// </para>
/// </remarks>
/// <param name="libraryManager">Library manager.</param>
/// <param name="repository">Filter file repository.</param>
public class FilterSegmentProvider(ILibraryManager libraryManager, FilterFileRepository repository)
    : IMediaSegmentProvider
{
    private readonly ILibraryManager _libraryManager = libraryManager;
    private readonly FilterFileRepository _repository = repository;

    /// <inheritdoc />
    public string Name => "JellyFilter";

    /// <inheritdoc />
    public Task<IReadOnlyList<MediaSegmentDto>> GetMediaSegments(
        MediaSegmentGenerationRequest request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MediaSegmentDto> empty = [];

        var configuration = Plugin.Instance?.Configuration;
        if (configuration is null || !configuration.EnableMediaSegments)
        {
            return Task.FromResult(empty);
        }

        var item = _libraryManager.GetItemById(request.ItemId);
        var filter = _repository.GetForMediaPath(item?.Path);
        if (!filter.HasScenes)
        {
            return Task.FromResult(empty);
        }

        var segments = new List<MediaSegmentDto>(filter.Scenes.Count);
        foreach (var scene in filter.Scenes)
        {
            if (scene.DurationSeconds < configuration.MinimumSceneSeconds)
            {
                continue;
            }

            segments.Add(new MediaSegmentDto
            {
                ItemId = request.ItemId,
                Type = configuration.PublishedSegmentType,
                StartTicks = (long)(scene.Start * TimeSpan.TicksPerSecond),
                EndTicks = (long)(scene.End * TimeSpan.TicksPerSecond),
            });
        }

        return Task.FromResult<IReadOnlyList<MediaSegmentDto>>(segments);
    }

    /// <inheritdoc />
    public ValueTask<bool> Supports(BaseItem item) =>
        ValueTask.FromResult(item is Video && !string.IsNullOrEmpty(item.Path));
}
