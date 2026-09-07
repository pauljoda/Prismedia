---
sidebar_position: 2
title: Troubleshooting
description: Diagnose server connection, empty libraries, incorrect grouping, failed imports, playback problems, and stuck jobs in Prismedia.
---

# Troubleshooting

Start with the symptom you can see. You usually only need the web app's **Jobs** page and the container logs to narrow down the problem.

| What is happening? | Start here |
| --- | --- |
| The web app does not open | [Server and connection checks](#the-web-app-does-not-open) |
| A phone or TV cannot connect | [Native app connection](#the-native-app-cannot-connect) |
| A scan finds no files | [Library paths and scan settings](#library-scan-finds-nothing) |
| Movies appear as videos or series | [Folder grouping](#videos-become-moviesepisodes-when-they-shouldnt) |
| Search finds a title but it stays Wanted | [Requests and imports](#a-request-does-not-become-playable) |
| Artwork or provider matches are missing | [Identify](#identify-returns-nothing) |
| Video stalls or will not play | [Playback](#hls--transcoded-playback-stalls) |
| Work is queued but nothing moves | [Jobs](#jobs-are-stuck-running-forever) |

## The web app does not open

On the computer running Docker, check whether the container is up:

```bash
docker compose ps
docker compose logs --tail 200 prismedia
```

Run these commands from the directory containing your `compose.yaml`. If you used the guide's **Docker run** command instead:

```bash
docker ps -a --filter name=prismedia
docker logs --tail 200 prismedia
```

Open `http://localhost:8008` on that computer. From a different device, use the server's reachable network address and published port, such as `http://media-server.local:8008`. `localhost` always refers to the device opening the address.

If the container is running but the page cannot load, check the published port, firewall, and whether the device is on the same reachable network. If a reverse proxy is involved, test the direct server address from the local network first, then check [Reverse Proxy](../deployment/reverse-proxy.md).

## The container won't start

Read the first relevant error in the container logs:

- **Port already in use:** another process has claimed the host port. Stop that process or change the host side of the port mapping, for example `8009:8008`, then use port `8009` in the browser.
- **Permission denied:** check the path named in the error and the permissions on its host folder. `/data` must be writable. Keep the existing volume while investigating.
- **No space left on device:** check the storage backing `/data` as well as the Docker host's available disk space.
- **Migration or database startup failure:** keep the error and your previous image version, then read [Upgrading & Rollback](../deployment/upgrading.md). Recreating the container with the same persistent volume is different from deleting the volume; deleting it removes your saved state.

## The native app cannot connect

Open the same server address in the device's browser. If that also fails, resolve the network connection before changing the app's account settings.

If the browser works, check local network permission, the exact address and port entered in Prismedia, and the server's HTTPS certificate if you use HTTPS. Sign in with your **Prismedia** account. The [native connection guide](../using/native-apps.md#connection-troubleshooting) covers these checks and account access.

For a forgotten administrator password, follow [Password recovery](../deployment/authentication.md#password-recovery).

## Library scan finds nothing

1. Open **Files** and check that the watched root contains the files you expect. If the folder is empty there too, check the Docker mount before scanning again.
2. In **Settings → Watched Libraries**, check the root path. If `/srv/movies` is mounted at `/media/movies`, enter `/media/movies` in Prismedia.
3. Check that the root is **Enabled**, that the matching scan type is on, and that **Recursive** is on when files are in subfolders.
4. Check the [supported formats and skipped paths](../library/overview.md). Excluded paths, hidden folders, and generated preview files are not ordinary library inputs.
5. Run a scan and open its result in **Jobs**. Read any permission, missing-path, or format error before retrying.

If an administrator sees the library but a member does not, check that account's library access and content visibility. See [Settings](../using/settings.md).

The [folder guide](../getting-started/organize-folders.md) includes complete layouts and the matching root settings.

## Videos become movies/episodes when they shouldn't

Classification uses folder layout **and filename episode markers**:

- A video loose at the watched root appears as a standalone video.
- A single video in its own folder directly below the root can become a movie, provided it has no season or episode markers.
- Several videos in a subfolder can become a series.
- TV folders and `SxxEyy` filename tokens identify seasons and episodes.

Compare your layout with [Videos, Movies & Series](../library/videos.md), including where the watched root begins. Correct the layout and rescan. Keep a backup before moving or renaming a large collection.

## A request does not become playable

Open the wanted item's **Acquisition** section and **Request → History** to find the stage that needs attention.

| Stage | Check |
| --- | --- |
| Title found, no release | A metadata match identifies the work. Check the indexer and the acquisition profile's matching and quality rules for available files. |
| Download does not start | Check the selected download client's connection and the error in request history. |
| Download finished, import cannot find files | Check the path reported by the client, Prismedia's Docker mounts, and the remote path mapping. Both services must be referring to the same files. |
| Import cannot write | Check the destination's permissions and whether its media mount is read-only. |
| Import is waiting for review | Open the proposed file matches on the item's acquisition section and review them. |

Use the [separate download-client example](../getting-started/organize-folders.md#an-example-with-a-separate-download-client) to compare paths. Keep staging folders outside watched roots. See [Requests](../using/requests.md) for setup and permissions.

## Identify returns nothing

- Choose a metadata provider that supports the medium and the title you are looking for.
- Open **Plugins → Installed** and check that the provider is enabled and any required credentials are configured.
- Try a simpler title query and check the year, author, or other fields the provider offers.
- Read the failed job or plugin error for network, authentication, or rate-limit problems.

A scan can find files before a metadata provider has matched them. See the [Identify walkthrough](../getting-started/identify-walkthrough.md) for reviewing and accepting a result.

## HLS / transcoded playback stalls

Check whether one file fails or several do, and whether the same file plays in another browser or native client. Clients support different codecs, so one may play a source directly while another needs conversion.

- Read the playback error and check **Jobs** for related failures.
- Check container logs for the failing media operation or ffmpeg error.
- Check available space under `/data`, where generated playback files are stored.
- If you configured hardware transcoding, compare the configured device and encoder with the [HLS Streaming](../developers/hls-streaming.md) guide.

See [Playback & Reading](../using/playback.md) for direct playback, remuxing, subtitles, and quality controls. In a report, include the file's container and audio/video codecs; you do not need to share the media itself.

## Trickplay (timeline hover) doesn't show thumbnails

Check the video's preview work in **Jobs**. If generation failed, resolve its error first. **Settings → Diagnostics** provides diagnostics and rebuild actions for generated assets.

## Subtitles aren't auto-loading

Check **Settings → Subtitles** for automatic selection and preferred languages, then check the player's subtitle menu for available tracks. Confirm the subtitles job completed in **Jobs**.

For a subtitle file beside a video, use the same filename stem, such as `Movie.en.srt` beside `Movie.mkv`, and rescan. SRT, VTT, ASS, and SSA are supported. Acquisition imports do not currently move subtitle companion files into the library; see [Subtitles](../using/playback.md#subtitles).

## Plugin credentials lost after an update

Plugin credentials are encrypted using `PRISMEDIA_SECRET`. The container normally creates this key and saves it at `/data/.prismedia-secret`. Keep the same persistent `/data` volume across updates. If you set the variable yourself, keep its value stable.

A database dump alone does not include that key. If the original key is lost, stored credentials cannot be decrypted and must be entered again. See [the encryption secret](../deployment/authentication.md#the-encryption-secret-prismedia_secret) and [Backups & Restore](../deployment/backups.md).

## Jobs are stuck "running" forever

Check the worker heartbeat, then the active job's message and container logs. An offline worker cannot process queued work even if the web app still loads.

If the worker stopped unexpectedly, a container restart can let it recover expired job leases. Repeated failures need their underlying cause fixed; restarting alone will not resolve a missing file, full disk, or unavailable provider. See [Jobs & Operations](../using/jobs.md).

## "Failures" count keeps growing

Open the failed row and read the error. Common causes are unreadable files, missing source paths, full storage, and provider errors. Fix the cause, then retry the affected action. Clearing a failure acknowledges it; it does not repair the operation.

## How to file a useful bug report

[Open a bug report](https://github.com/pauljoda/Prismedia/issues/new?template=bug-report.yml) with:

- The running server version and image channel or tag; include the native build or browser version when relevant.
- The steps that reproduce the issue, the expected result, and what happened instead.
- The relevant job error or a short excerpt of container logs around the failure.
- For storage issues, an example of the folder layout and container mounts with private names replaced.

Remove passwords, tokens, private hostnames, and private media details from logs and screenshots before posting. For setup questions, use [r/Prismedia](https://www.reddit.com/r/Prismedia/). The [support page](/support) links to the appropriate guides and report forms.
