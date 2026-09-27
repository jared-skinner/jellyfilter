# JellyFilter

A Jellyfin plugin that skips or mutes scenes listed in a JSON file stored next to the video, based
on each user's content preferences.

Inspired by [jellyfin-plugin-moviecontentfilter](https://github.com/jacob-willden/jellyfin-plugin-moviecontentfilter)
and [Intro Skipper](https://github.com/intro-skipper/intro-skipper).

## How it works

The server watches each playing session. When playback enters a scene the viewer has asked to
filter, the server sends that client a seek command past the end of the scene — or mutes it for the
length of the scene, if that is what was asked for.

Doing this server side rather than in the client is what makes per-user preferences possible: two
people watching the same film get different cuts of it. It also means no client-side plugin is
needed, so it works on the web player, mobile apps, and TV clients alike.

## Filter files

A filter file is JSON stored alongside the video. The simplest form is an array of scenes, with
times in seconds:

```json
[
  { "type": "sexuality", "start": 1244, "end": 1345 },
  { "type": "violence",  "start": 302,  "end": 331 }
]
```

The longer form carries a title and per-scene detail:

```json
{
  "version": 1,
  "title": "Example Movie",
  "scenes": [
    { "type": "sexuality", "start": "00:20:44", "end": "00:22:25" },
    {
      "type": "profanity",
      "start": "00:31:02.5",
      "end": "00:31:04",
      "action": "mute",
      "severity": "mild",
      "description": "one word"
    }
  ]
}
```

### Fields

| Field | Required | Notes |
| --- | --- | --- |
| `type` | no | Content category, for example `sexuality`. Defaults to `unknown`. Also accepted as `category`, `tag`, `label`, or `kind`. |
| `start` | yes | Where the scene begins. Also accepted as `startTime`, `from`, `begin`, or `in`. |
| `end` | yes | Where the scene ends. Also accepted as `endTime`, `to`, `finish`, or `out`. |
| `action` | no | `skip` or `mute`. Only takes effect when the viewer has enabled the category; see below. |
| `severity` | no | Free text such as `mild` or `severe`. Recorded but not currently acted on. |
| `description` | no | Free text note, shown when inspecting an item from the dashboard. |

Times may be a number of seconds (`1244`) or a timecode string (`20:44`, `00:20:44`,
`00:20:44.500`). Category names are matched loosely, so `Sexual Content`, `sexual-content`, and
`sexual_content` are all the same category. Comments and trailing commas are tolerated, but property
names still need their quotes.

A scene's `action` only narrows what the viewer asked for: if the viewer set a category to *Skip*
and a scene in that category asks to be muted, it is muted rather than cut. This is meant for a
stray word inside an otherwise fine stretch of film. A scene can never widen a viewer's choice, and
a category the viewer left off is never touched.

### Where files are looked for

For `/movies/Example (2020)/Example (2020).mkv`, these paths are checked in order:

1. `Example (2020).jellyfilter.json`
2. `Example (2020).filter.json`
3. `Example (2020).mkv.jellyfilter.json`
4. `Example (2020).mkv.filter.json`
5. `jellyfilter.json` (applies to any video in that folder)

The patterns are editable in the plugin settings. If your library is mounted read-only, set an
**Additional filter directory** and the same names will be searched there too.

Edits to a filter file are picked up within a few seconds, without restarting the server or
rescanning the library.

## Setting up users

Filtering is off for everyone until it is switched on. In **Dashboard → Plugins → JellyFilter**,
pick a user, tick *Filter content for this user*, and set each category to **Off**, **Skip**, or
**Mute**. Categories not listed can be added at the bottom of that section — use the same spelling
that appears in your filter files.

Preferences are per user, so a user with nothing configured watches everything unfiltered.

## Media segments

Optionally, scenes can also be published as Jellyfin media segments, which lets clients that
understand segments render their own skip button. This is off by default and worth understanding
before turning it on: **media segments belong to an item, not to a viewer.** Every scene published
this way is visible to everyone on the server, regardless of the per-user preferences above. Jellyfin's
segment vocabulary also has no entry for content warnings, so scenes have to borrow one of the
existing types (`Commercial` by default).

The server-side watcher is the mechanism that respects per-user preferences, and it works whether or
not segments are enabled.

## Building

Requires the .NET 9 SDK. Targets Jellyfin 10.11 — the server refuses plugins built against a newer
ABI than it runs, so on 10.10 this will not appear in the plugin list.

```sh
dotnet build -c Release
dotnet test
```

## Installing

### From the dashboard

Jellyfin installs plugins from repositories rather than from uploaded files, so this route means
publishing two files — a zip of the plugin and a `manifest.json` describing it — somewhere the
server can reach over HTTP. `scripts/package.sh` produces both:

```sh
BASE_URL=https://example.com/jellyfin scripts/package.sh
```

`BASE_URL` is the directory the zip will be served from; it is baked into the manifest as the
download URL, which is why it has to be known when packaging. The script writes
`artifacts/jellyfilter_<version>.zip` and `artifacts/manifest.json`, and prints the URL it expects
the zip to end up at.

Publish both files, then in **Dashboard → Plugins → Repositories** add a repository whose URL is
the published `manifest.json`. JellyFilter then appears in the catalogue under *General*, and
installs and updates like any other plugin.

For a GitHub-hosted copy, attach the zip to a release and point `BASE_URL` at that release's
download directory, then serve `manifest.json` from the raw file URL. For a home server, any static
file host on the same network works — the Jellyfin server is the only thing that needs to reach it.

### By hand

Copy `Jellyfin.Plugin.JellyFilter/bin/Release/net9.0/Jellyfin.Plugin.JellyFilter.dll` into a
`plugins/JellyFilter/` folder inside your Jellyfin data directory, make sure the server's user can
read it, and restart. The configuration page is embedded in the DLL, so there is nothing else to
copy.

Either way, confirm it loaded by looking for `JellyFilter playback watcher started` in the server
log. That line comes from the background service, so it tells you more than the plugin list does.

## Troubleshooting

The plugin settings page has an **Inspect item** box. Paste a library item id — the `id` query
parameter in the URL when viewing an item in the web UI — and it will report which filter file was
used, every path that was searched, the scenes that were loaded, and any entries that could not be
read.

Two things are worth knowing when a skip does not land where you expect:

- Playback positions are polled, so a skip starts up to one poll interval (400 ms by default) after
  the scene begins. Lower the interval, or start scenes slightly early in the filter file.
- The server can only ask a client to seek. A client that ignores seek commands cannot be filtered;
  the plugin gives up on a scene after three attempts and logs a warning naming the client.

## Limitations

- Per-user preferences are set by an administrator from the dashboard. Jellyfin has no per-user
  plugin settings UI, so users cannot currently set their own.
- Filter files have to be written by hand or generated by some other tool; the plugin does not
  detect content itself.
