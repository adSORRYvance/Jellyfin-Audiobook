# Audiobook Library

A Jellyfin 12.1 plugin that gives audiobooks a chapter-aware player modeled on Audiobookshelf's, with Audible chapters and Author/Series pages.

## Build

Needs the .NET 10 SDK.

```sh
dotnet build Jellyfin.Plugin.AudiobookLibrary.slnx -c Release
```

The plugin is `Jellyfin.Plugin.AudiobookLibrary/bin/Release/net10.0/Jellyfin.Plugin.AudiobookLibrary.dll`.

## Manual install

Copy the DLL into `<jellyfin config>/data/plugins/AudiobookLibrary/` and restart Jellyfin. The plugin must be built against the same or an older Jellyfin version than the server runs.
