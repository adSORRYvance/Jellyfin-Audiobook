# Audiobook Library

A Jellyfin 12.1 plugin that gives audiobooks a chapter-aware player modeled on Audiobookshelf's, with Audible chapters and Author/Series pages.

## Install

In Jellyfin, go to Dashboard > Plugins > Repositories, add one of these URLs, then install **Audiobook Library** from the Catalog.

**Stable**, updated once per finished milestone:

```
https://raw.githubusercontent.com/adSORRYvance/Jellyfin-Audiobook/main/manifest.json
```

**Beta**, every feature and fix as it lands, plus the stable releases:

```
https://raw.githubusercontent.com/adSORRYvance/Jellyfin-Audiobook/main/manifest-beta.json
```

Add only one. Beta already includes everything in stable.

Requires Jellyfin 12.1 or newer.

## Release

A tag publishes a build that already passed its checklist on a test server. Nothing is tagged to find out whether it works.

1. **Pick the version** you'll release, for example `0.1.5.0`.
2. **Deploy a dev build** of your branch to the test server, stamped with that version:

   ```sh
   cp scripts/deploy-test.env.example scripts/deploy-test.env   # once, then fill it in
   scripts/deploy-test.sh 0.1.5.0
   ```

   The script builds into `dist/dev/` and prints a one-line command to run on a **relay machine**, one that can SSH to both your build machine and the test server without a password. The relay streams the build into the Jellyfin container and restarts it, so the build machine and the server never need access to each other. With `TEST_API_KEY` set, the script then waits until the server reports the new version.

   To check the relay once: `ssh -o BatchMode=yes <workspace host> true && ssh -o BatchMode=yes <server host> 'docker ps -q >/dev/null' && echo ok`

   Jellyfin keeps only the newest version of a plugin, so the dev build replaces the installed release. Redeploy as often as you like. `--local-only` only builds.
3. **Run the checklist** and fix anything that fails on the branch.
4. **Update `changelog` in `build.yaml`**, merge, and tag the merge commit:

| Tag | Channel | Result |
|---|---|---|
| `v0.1.5.0-beta` | beta | Pre-release, added to `manifest-beta.json` |
| `v0.2.0.0` | stable | Release, added to both manifests |
| `v0.1.5.0` after `v0.1.5.0-beta` | stable | **Promotion**: the beta zip is republished unchanged and added to `manifest.json` |

The workflow refuses any other tag shape, a version already published on the same channel, a beta of a version that's already stable, and a changelog matching any published version. A promotion reuses the beta's changelog, so it needs no `build.yaml` change.

The catalog release of the version you tested counts as already installed on the test server, so there's nothing to reinstall afterwards.

## Build

Needs the .NET 10 SDK.

```sh
dotnet build Jellyfin.Plugin.AudiobookLibrary.slnx -c Release
```

The plugin is `Jellyfin.Plugin.AudiobookLibrary/bin/Release/net10.0/Jellyfin.Plugin.AudiobookLibrary.dll`.

## Manual install

Copy the DLL into `<jellyfin config>/data/plugins/AudiobookLibrary/` and restart Jellyfin. The plugin must be built against the same or an older Jellyfin version than the server runs.

The folder must be writable by the user Jellyfin runs as, because Jellyfin writes `meta.json` there on first load. A root-owned folder stops the server from starting.
