# Phase 2 — Matching Infuse's Jellyfin calls (capture proxy)

Self-contained brief for a fresh thread. Goal: capture exactly what Infuse requests and what a
server returns, then make Prismedia's Jellyfin-compatibility layer match real-Jellyfin behavior —
especially around playback / resume / watched state.

## The tool

`scripts/dev/jellyfin-capture-proxy.mjs` — a zero-dependency Node logging reverse proxy. Point a
Jellyfin client (Infuse) at it; it forwards every request to an upstream server (your real Jellyfin
**or** Prismedia) and appends the full request/response to a JSONL file. It modifies neither server
and forwards auth headers/cookies untouched. JSON/text/m3u8 bodies are captured (size-capped);
binary/streaming responses (video segments, images) are streamed through and logged as metadata only.

```
node scripts/dev/jellyfin-capture-proxy.mjs --upstream <url> --port 8099 --out <file.jsonl> [--insecure]
```
Flags: `--upstream` (required, http/https base URL) · `--port` (default 8099) · `--out` (default
`./jellyfin-capture.jsonl`) · `--max-body <bytes>` (default 262144) · `--insecure` (accept self-signed
TLS on an https upstream).

Each request also prints a one-line console summary (`[12] GET /Items/… -> 200`).

## Setup notes

- This machine's LAN IP is **`10.1.20.183`**. Infuse on an iPhone/Apple TV must reach the Mac by LAN
  IP — `localhost` won't work from another device. Both must be on the same network.
- Infuse accepts a plain `http://` server, so no TLS is needed on the proxy side.
- The proxy needs the upstream reachable: for Mode B, start Prismedia first (API on `:8008`).

## Mode A — Ground truth (do this first): capture your *real* Jellyfin

Shows what Infuse expects a real server to return — the reference we match against.

1. `node scripts/dev/jellyfin-capture-proxy.mjs --upstream http://YOUR-JELLYFIN-HOST:8096 --port 8099 --out ~/jf-real.jsonl`
   (add `--insecure` if your Jellyfin is https with a self-signed cert)
2. In Infuse: add a **new** Jellyfin server → `http://10.1.20.183:8099` → sign in with your normal
   Jellyfin credentials.
3. Do the representative tour below.
4. The new thread can read `~/jf-real.jsonl` directly.

## Mode B — Our side: capture what Infuse asks *Prismedia*

Same tool, upstream is Prismedia. Shows where Prismedia 404s or returns the wrong shape.

1. `node scripts/dev/jellyfin-capture-proxy.mjs --upstream http://127.0.0.1:8008 --port 8099 --out ~/jf-prismedia.jsonl`
2. In Infuse: add `http://10.1.20.183:8099` → sign in with a **Prismedia Jellyfin profile username +
   your API key** (the API key is in Prismedia **Settings**; it's also the `prismedia-api-key` value).
3. Same tour.

## The capture tour (exercise the endpoints that matter)

- Open the library home / "Continue Watching" (resume) shelf
- Open a **movie**, and a **TV series → season → episode**
- **Start playback**, let it buffer, **scrub**, **pause**, then **back out partway** (resume case)
- Let one item **finish**, and **mark one Watched / then Unwatched**
- Tap **Start Over** on an item (we want to see exactly what call Infuse makes for it)

## What we're verifying

- Resume round-trip: `UserData.PlaybackPositionTicks` vs `RunTimeTicks`, `PlayedPercentage`,
  `LastPlayedDate`, and the `/UserItems/Resume` (resume shelf) query.
- Watched state: `UserData.Played` / `PlayCount`, and the `/UserPlayedItems/{id}` POST/DELETE.
- Session reporting: `/Sessions/Playing`, `/Sessions/Playing/Progress`, `/Sessions/Playing/Stopped`.
- **Start Over**: confirm which call Infuse issues (likely a Playing report at position 0) so our
  sub-5% → reset path matches.
- Any fields/endpoints Infuse requests that Prismedia 404s or omits.

## Reference pointers for the new thread

- Reference Jellyfin source (clean-room comparison): `~/Dev/_ARCHIVE/jellyfin-master`
- Prismedia's Jellyfin endpoints are mounted at the **server root** (not under `/api`) — e.g.
  `/Sessions/Playing/Progress`, `/UserPlayedItems/{id}`, `/Items/{id}`, `/Users/{userId}/…`.
  Endpoints: `apps/backend/src/Prismedia.Api/Endpoints/Jellyfin/`
- DTO mapping (BaseItemDto, UserData, MediaSources): `apps/backend/src/Prismedia.Application/Jellyfin/JellyfinCatalogService.cs`
- Jellyfin DTOs: `apps/backend/src/Prismedia.Contracts/Jellyfin/JellyfinDtos.cs`
  (UserData currently exposes `PlaybackPositionTicks`, `PlayCount`, `IsFavorite`, `Played`, `Key`,
  `PlayedPercentage`, `LastPlayedDate`).
- Playback persistence (shared by native player + Jellyfin): `PlaybackSessionService.cs` →
  `EntityCapabilityService.UpdatePlaybackAsync`. Thresholds: resume 5–90%, ≥90% watched, <5% start-over.
- A JSONL record has: `{ id, ts, durationMs, method, url, client{client,device,version,userAgent},
  reqHeaders, reqBody, status, resContentType, resHeaders, resBody }`.

## Cleanup

The proxy is an untracked dev tool under `scripts/dev/` — keep it or delete it; it's not part of any
commit. Capture files (`~/jf-*.jsonl`) are scratch.
