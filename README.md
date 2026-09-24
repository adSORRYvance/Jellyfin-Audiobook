# Audiobook Library

A Jellyfin 12.1 plugin that gives audiobooks a chapter-aware player modeled on Audiobookshelf's, with Audible chapters and Author/Series pages.

## Install

In Jellyfin, go to Dashboard > Plugins > Repositories, add this URL, then install **Audiobook Library** from the Catalog:

```
https://raw.githubusercontent.com/adSORRYvance/Jellyfin-Audiobook/main/manifest.json
```

Requires Jellyfin 12.1 or newer.

## Release

Push a four-part version tag. The Release workflow builds the zip, attaches it to a GitHub Release and adds it to `manifest.json`.

```sh
git tag v0.1.0.0
git push origin v0.1.0.0
```

## Build

Needs the .NET 10 SDK.

```sh
dotnet build Jellyfin.Plugin.AudiobookLibrary.slnx -c Release
```

The plugin is `Jellyfin.Plugin.AudiobookLibrary/bin/Release/net10.0/Jellyfin.Plugin.AudiobookLibrary.dll`.

## Manual install

Copy the DLL into `<jellyfin config>/data/plugins/AudiobookLibrary/` and restart Jellyfin. The plugin must be built against the same or an older Jellyfin version than the server runs.

The folder must be writable by the user Jellyfin runs as, because Jellyfin writes `meta.json` there on first load. A root-owned folder stops the server from starting.
