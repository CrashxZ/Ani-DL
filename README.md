# AniDL for Jellyfin

AniDL is a Jellyfin 10.11.11 plugin for authorized users to search, browse, queue, and import anime into a configured Jellyfin library. The first source is an **experimental AniSuge adapter**. Japanese audio with English subtitles and English dub are offered as distinct choices when the source advertises them.

Only download media you are legally entitled to save. AniDL does not bypass DRM, logins, CAPTCHAs, paywalls, or other access controls. A provider that does not expose a direct public HLS, DASH, or MP4 resource fails closed.

## Architecture

- `IAnimeSource` isolates site-specific search, parsing, episode enumeration, and media resolution.
- Authenticated ASP.NET endpoints re-fetch source metadata before accepting a job. Clients cannot choose an output path or supply a media URL.
- A bounded `BackgroundService` queue persists jobs in the plugin data directory, resumes interrupted jobs, limits concurrency, retries transient failures, and supports cancellation.
- `ffmpeg` is taken from Jellyfin's configured media encoder unless an administrator explicitly overrides it. Arguments never pass through a shell.
- Output paths are normalized and constrained beneath one administrator-configured library root.
- Remote media URLs require HTTPS and are rejected when DNS resolves to loopback, private, link-local, carrier-grade NAT, benchmarking, multicast, or other non-public ranges.

## Build without installing anything locally

This repository's GitHub Actions workflow restores, builds, tests, publishes, and produces `AniDL-0.1.0.zip` using an ephemeral .NET 9 runner. No .NET SDK, ffmpeg, Node.js, or browser automation is required on the development machine.

To build elsewhere:

```sh
dotnet restore AniDL.slnx
dotnet test AniDL.slnx -c Release
dotnet publish src/Jellyfin.Plugin.AniDL/Jellyfin.Plugin.AniDL.csproj -c Release -o artifacts/AniDL
```

## Install on a test Jellyfin server

### Jellyfin plugin repository

In Dashboard → Plugins → Repositories, add:

```text
https://raw.githubusercontent.com/CrashxZ/Ani-DL/main/manifest.json
```

Then open Catalog, install AniDL, and restart Jellyfin.

### Manual installation

1. Confirm the server is Jellyfin 10.11.11 and has its normal bundled/configured ffmpeg.
2. Extract the release ZIP into a new plugin directory such as `plugins/AniDL_0.1.0.0/`.
3. Restart Jellyfin.
4. In Dashboard → Plugins → AniDL, set an absolute anime library root that the Jellyfin service account can write.
5. Optionally enable non-admin access and list exact Jellyfin usernames or user IDs.
6. Restart after changing concurrency; workers are sized at plugin startup.

The plugin page appears under the server section. Downloads are named in Jellyfin's television convention:

```text
Series Title/Season 01/Series Title - S01E01.mkv
```

## Current experimental limits

- AniSuge changes undocumented HTML/AJAX contracts. Contract changes produce a clear failed job instead of guessing.
- The resolver handles direct HLS, DASH, and MP4 URLs exposed in the selected provider page. Provider-specific encrypted/obfuscated players are intentionally unsupported.
- AniSuge models anime as a flat episode list. The initial adapter imports it as season 1; a later metadata adapter can map split cours and sequel seasons.
- Release tags publish a ZIP and update `manifest.json` with the real package checksum and download URL.

## Security notes

Administrators are always authorized. Non-administrators require both the enable switch and an explicit username/user-ID allowlist match. Queue and cancellation responses are owner-filtered, while administrators can see and cancel all jobs. The plugin never injects scripts into Jellyfin's global `index.html`.

Source research was performed against [AniSuge](https://anisuge.tv/), the [Jellyfin plugin template](https://github.com/jellyfin/jellyfin-plugin-template), and the requested [AniWorld Downloader reference](https://github.com/SiroxCW/Jellyfin-AniWorld-Downloader).
