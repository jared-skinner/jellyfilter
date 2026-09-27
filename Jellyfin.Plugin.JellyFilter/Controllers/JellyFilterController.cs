using System.Net.Mime;
using Jellyfin.Plugin.JellyFilter.Configuration;
using Jellyfin.Plugin.JellyFilter.Data;
using Jellyfin.Plugin.JellyFilter.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyFilter.Controllers;

/// <summary>
/// Endpoints backing the configuration page.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="JellyFilterController"/> class.
/// </remarks>
/// <param name="userManager">User manager.</param>
/// <param name="libraryManager">Library manager.</param>
/// <param name="repository">Filter file repository.</param>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("JellyFilter")]
[Produces(MediaTypeNames.Application.Json)]
public class JellyFilterController(
    IUserManager userManager,
    ILibraryManager libraryManager,
    FilterFileRepository repository) : ControllerBase
{
    private readonly IUserManager _userManager = userManager;
    private readonly ILibraryManager _libraryManager = libraryManager;
    private readonly FilterFileRepository _repository = repository;

    /// <summary>
    /// Lists the content categories offered in the configuration page.
    /// </summary>
    /// <response code="200">Categories returned.</response>
    /// <returns>The known categories, in alphabetical order.</returns>
    [HttpGet("Categories")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<string>> GetCategories()
    {
        var configuration = Plugin.Instance!.Configuration;
        var categories = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var category in configuration.KnownCategories)
        {
            categories.Add(FilterFileParser.NormalizeCategory(category));
        }

        // Categories a user already has a choice for must stay visible even if they were removed
        // from the known list, otherwise the page would silently drop a working preference.
        foreach (var user in configuration.UserPreferences)
        {
            foreach (var preference in user.Categories)
            {
                categories.Add(FilterFileParser.NormalizeCategory(preference.Category));
            }
        }

        return Ok(categories.ToList());
    }

    /// <summary>
    /// Lists every Jellyfin user along with their filtering choices.
    /// </summary>
    /// <response code="200">Users returned.</response>
    /// <returns>All users.</returns>
    [HttpGet("Users")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<UserPreferencesDto>> GetUsers()
    {
        var configuration = Plugin.Instance!.Configuration;
        var results = new List<UserPreferencesDto>();

        foreach (var user in _userManager.GetUsers())
        {
            var stored = configuration.FindUser(user.Id);
            results.Add(new UserPreferencesDto
            {
                UserId = user.Id.ToString("N"),
                UserName = user.Username,
                Enabled = stored?.Enabled ?? false,
                Categories = stored?.Categories ?? [],
            });
        }

        return Ok(results);
    }

    /// <summary>
    /// Replaces one user's filtering choices.
    /// </summary>
    /// <param name="userId">The user to update.</param>
    /// <param name="preferences">The new choices.</param>
    /// <response code="204">Choices saved.</response>
    /// <response code="404">No such user.</response>
    /// <returns>No content.</returns>
    [HttpPost("Users/{userId}/Preferences")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult SetUserPreferences([FromRoute] Guid userId, [FromBody] UserPreferencesDto preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        if (_userManager.GetUserById(userId) is null)
        {
            return NotFound();
        }

        var plugin = Plugin.Instance!;
        var configuration = plugin.Configuration;
        var key = userId.ToString("N");

        var categories = preferences.Categories
            .Select(c => new CategoryPreference
            {
                Category = FilterFileParser.NormalizeCategory(c.Category),
                Action = c.Action,
            })
            .Where(c => c.Action != FilterAction.Allow)
            .ToArray();

        var updated = configuration.UserPreferences
            .Where(p => !string.Equals(
                p.UserId.Replace("-", string.Empty, StringComparison.Ordinal),
                key,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        updated.Add(new UserFilterPreferences
        {
            UserId = key,
            Enabled = preferences.Enabled,
            Categories = categories,
        });

        configuration.UserPreferences = [.. updated];
        plugin.UpdateConfiguration(configuration);

        return NoContent();
    }

    /// <summary>
    /// Shows what the plugin has loaded for one library item.
    /// </summary>
    /// <param name="itemId">The library item.</param>
    /// <response code="200">Preview returned.</response>
    /// <response code="404">No such item.</response>
    /// <returns>The filter that would be applied.</returns>
    [HttpGet("Items/{itemId}/Filter")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<FilterPreviewDto> GetItemFilter([FromRoute] Guid itemId)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return NotFound();
        }

        // Always read from disk here: this endpoint exists to answer "why is my file not being
        // picked up", and a cached miss would give a misleading answer.
        _repository.Invalidate(item.Path);
        var filter = _repository.GetForMediaPath(item.Path);

        return Ok(new FilterPreviewDto
        {
            ItemName = item.Name,
            MediaPath = item.Path,
            FilterPath = filter.SourcePath,
            Title = filter.Title,
            SearchedPaths = string.IsNullOrEmpty(item.Path)
                ? []
                : FilterFileRepository.GetCandidatePaths(item.Path),
            Scenes = filter.Scenes.Select(FilterSceneDto.FromScene).ToList(),
            Warnings = filter.Warnings.ToList(),
        });
    }

    /// <summary>
    /// Drops every cached filter file so the next playback re-reads from disk.
    /// </summary>
    /// <response code="204">Cache cleared.</response>
    /// <returns>No content.</returns>
    [HttpPost("Cache/Refresh")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult RefreshCache()
    {
        _repository.InvalidateAll();
        return NoContent();
    }
}
