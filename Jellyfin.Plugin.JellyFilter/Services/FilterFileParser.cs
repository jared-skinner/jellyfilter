using System.Globalization;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.JellyFilter.Data;

namespace Jellyfin.Plugin.JellyFilter.Services;

/// <summary>
/// Reads sidecar filter files.
/// </summary>
/// <remarks>
/// The format is deliberately forgiving because these files are written by hand. The root may be
/// either an array of scenes or an object with a <c>scenes</c> array. Times may be given as a
/// number of seconds or as a <c>hh:mm:ss.fff</c> / <c>mm:ss</c> timecode string, and the common
/// alternative spellings of each field name are accepted.
/// </remarks>
public static class FilterFileParser
{
    private static readonly string[] _sceneListNames = ["scenes", "filters", "segments", "entries", "items"];
    private static readonly string[] _categoryNames = ["type", "category", "tag", "label", "kind"];
    private static readonly string[] _startNames = ["start", "starttime", "startseconds", "from", "begin", "in"];
    private static readonly string[] _endNames = ["end", "endtime", "endseconds", "to", "finish", "out"];
    private static readonly string[] _actionNames = ["action", "mode", "behavior", "behaviour"];
    private static readonly string[] _severityNames = ["severity", "level", "intensity"];
    private static readonly string[] _descriptionNames = ["description", "note", "comment", "text", "reason"];
    private static readonly string[] _titleNames = ["title", "name", "media", "mediatitle"];

    private static readonly JsonDocumentOptions _documentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Parses the contents of a filter file.
    /// </summary>
    /// <param name="json">Raw file contents.</param>
    /// <param name="sourcePath">Path the contents came from, recorded on the result.</param>
    /// <returns>The parsed filter. Unreadable scenes are dropped and recorded in <see cref="MediaFilter.Warnings"/>.</returns>
    /// <exception cref="JsonException">The file is not valid JSON.</exception>
    public static MediaFilter Parse(string json, string sourcePath)
    {
        var filter = new MediaFilter { SourcePath = sourcePath };

        using var document = JsonDocument.Parse(json, _documentOptions);
        var root = document.RootElement;

        JsonElement sceneList;
        if (root.ValueKind == JsonValueKind.Array)
        {
            sceneList = root;
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            filter.Title = TryGetString(root, _titleNames);

            if (!TryGetProperty(root, _sceneListNames, out sceneList) || sceneList.ValueKind != JsonValueKind.Array)
            {
                // A file holding a single scene object is still worth honouring.
                if (TryGetProperty(root, _startNames, out _))
                {
                    AddScene(filter, root, 0);
                    Finish(filter);
                    return filter;
                }

                filter.Warnings.Add("No scene list found. Expected a top-level array or a \"scenes\" array.");
                return filter;
            }
        }
        else
        {
            filter.Warnings.Add($"Unexpected root element {root.ValueKind}. Expected an array or an object.");
            return filter;
        }

        var index = 0;
        foreach (var element in sceneList.EnumerateArray())
        {
            AddScene(filter, element, index);
            index++;
        }

        Finish(filter);
        return filter;
    }

    /// <summary>
    /// Folds a category name to the form used for matching against user preferences, so that
    /// <c>Sexual Content</c>, <c>sexual-content</c> and <c>sexual_content</c> are one category.
    /// </summary>
    /// <param name="raw">Category as written.</param>
    /// <returns>The canonical form.</returns>
    public static string NormalizeCategory(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "unknown";
        }

        var builder = new StringBuilder(raw.Length);
        var pendingSeparator = false;
        foreach (var c in raw)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSeparator && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                pendingSeparator = false;
                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                pendingSeparator = true;
            }
        }

        return builder.Length == 0 ? "unknown" : builder.ToString();
    }

    private static void Finish(MediaFilter filter)
    {
        var ordered = filter.Scenes.OrderBy(s => s.Start).ThenBy(s => s.End).ToList();
        filter.Scenes.Clear();
        foreach (var scene in ordered)
        {
            filter.Scenes.Add(scene);
        }
    }

    private static void AddScene(MediaFilter filter, JsonElement element, int index)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            filter.Warnings.Add($"Scene {index}: expected an object but found {element.ValueKind}.");
            return;
        }

        if (!TryGetProperty(element, _startNames, out var startElement) || !TryReadSeconds(startElement, out var start))
        {
            filter.Warnings.Add($"Scene {index}: missing or unreadable start time.");
            return;
        }

        if (!TryGetProperty(element, _endNames, out var endElement) || !TryReadSeconds(endElement, out var end))
        {
            filter.Warnings.Add($"Scene {index}: missing or unreadable end time.");
            return;
        }

        if (end <= start)
        {
            filter.Warnings.Add($"Scene {index}: end ({end}) is not after start ({start}).");
            return;
        }

        var category = TryGetString(element, _categoryNames) ?? "unknown";

        filter.Scenes.Add(new FilterScene
        {
            Category = category,
            NormalizedCategory = NormalizeCategory(category),
            Start = start,
            End = end,
            Action = ParseAction(TryGetString(element, _actionNames)),
            Severity = TryGetString(element, _severityNames),
            Description = TryGetString(element, _descriptionNames),
        });
    }

    private static FilterAction? ParseAction(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "skip" or "cut" or "seek" => FilterAction.Skip,
            "mute" or "silence" => FilterAction.Mute,
            "allow" or "none" or "ignore" or "keep" => FilterAction.Allow,
            _ => null,
        };
    }

    private static bool TryGetProperty(JsonElement element, string[] names, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            var key = NormalizeKey(property.Name);
            foreach (var name in names)
            {
                if (string.Equals(key, name, StringComparison.Ordinal))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string NormalizeKey(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    private static string? TryGetString(JsonElement element, string[] names)
    {
        if (!TryGetProperty(element, names, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static bool TryReadSeconds(JsonElement element, out double seconds)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                if (element.TryGetDouble(out seconds))
                {
                    return seconds >= 0;
                }

                break;

            case JsonValueKind.String:
                return TryParseTimeCode(element.GetString(), out seconds);
        }

        seconds = 0;
        return false;
    }

    /// <summary>
    /// Parses a time expressed as plain seconds, <c>mm:ss</c>, or <c>hh:mm:ss</c>, with optional decimals.
    /// </summary>
    /// <param name="raw">The text to parse.</param>
    /// <param name="seconds">The parsed offset in seconds.</param>
    /// <returns><c>true</c> when the text was understood.</returns>
    public static bool TryParseTimeCode(string? raw, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var parts = raw.Trim().Split(':');
        if (parts.Length > 3)
        {
            return false;
        }

        double total = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var part) || part < 0)
            {
                return false;
            }

            // Only the final component may be fractional; the others are whole minutes or hours.
            if (i < parts.Length - 1 && part != Math.Floor(part))
            {
                return false;
            }

            total = (total * 60) + part;
        }

        seconds = total;
        return true;
    }
}
