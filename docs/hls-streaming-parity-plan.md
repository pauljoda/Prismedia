# Prismedia HLS — Reconciled Streaming Parity Plan (vs Jellyfin)

Status: **planned** — remediation roadmap to (A) stop CPU pinning on web transcode and (B) reach full Jellyfin playback parity so apps (Infuse, official Jellyfin apps) direct-play/remux the way they do against real Jellyfin. Authored 2026-06-04 from two cross-codebase audits + a verification pass; every load-bearing claim checked against source and, where empirical, against jellyfin-ffmpeg 7.1.3.

Goal: match Jellyfin — the battle-tested reference — as it runs in a stock CPU-only Docker container and as its clients experience it. Jellyfin plays more content via direct-play/remux (so the heavy 4K/HDR/DoVi content never transcodes) and, when it must transcode, never pins the CPU. Prismedia transcodes too eagerly and pins immediately.

This plan is the **playback/streaming** half of Jellyfin parity. Its app-facing catalog/DTO half lives in `docs/jellyfin-parity-audit.md`; the two overlap on MediaStream HDR/DoVi metadata and DeviceProfile handling, called out below.

**Decided defaults (from review):**
- Bounded encode follows **Jellyfin's real mechanism**: single job + `24 / SegmentLength` seek-gap **kill/restart window** (not an invented `-t` window). [#1]
- **Implement the throttle** (Jellyfin parity) and the small client buffer — don't leave the capability on the table. [#2, #3]
- **Reduce the web buffer to Jellyfin's `30s` / `6s`-high-bitrate**, coupled with the window + throttle. [#3 — my call]
- **Full app parity is in scope** — promote the deferred URL-plan/DeviceProfile/MediaStream-metadata work into the roadmap (Track B). [#4]
- Thread cap defaults to `ProcessorCount - 1`; single-variant is unconditional behind one global setting (no LAN-subnet detection); bitrate-test endpoint skipped.

---

## Implementation status — branch `feat/hls-streaming-parity`

- **P0.1 single main HLS variant — DONE.** Master playlist advertises one top, source-capped rung by default; full ladder behind the new `hls.enableAdaptiveBitrate` setting (default off). This alone collapses N concurrent transcodes to one per session and neutralises the audit's "concurrent rendition" finding.
- **P0.4 thread cap — DONE.** Software transcode emits `-threads` (`ProcessorCount - 1` default, configurable via `hls.encodingThreadCount`); remux copies cap at `-threads 2`.
- **P0.2 teardown/window/guard — re-scoped.** With single-variant default there is only one rendition, so the concurrent-rendition orphan can't arise and the existing per-rendition teardown already cancels the same-rendition job on a forward-seek restart. Broadening teardown across renditions broke adaptive switching (test `FarVirtualSegmentDoesNotCancelOtherActiveRenditions...`) for zero benefit in the default — **reverted**. The hard concurrency semaphore is **skipped** (real starvation risk for a 2nd viewer; marginal for a single-user household). The 24s seek-window tighten is **coupled to the buffer reduction** and folded into P0.3.
- **P0.5 tonemapx health check — deferred.** tonemapx is guaranteed present in the shipped jellyfin-ffmpeg image, and the degraded software-no-tonemap path already logs a runtime warning (`HlsAssetService.Generation.cs:122`). Revisit only if a non-jellyfin ffmpeg override becomes supported.
- **P2.4 remux keyframe-grid bug — DONE.** `BuildRemuxSegmentDurations` now advances the cut threshold by one segment from its previous value (ffmpeg's rule) instead of jumping past the cut, so the VOD playlist matches ffmpeg on long-GOP sources. Unit-tested incl. the empirical `[0,19,20,25] → [19,1,5,5]` case.
- **VALIDATED LIVE (dev stack).** master.m3u8 serves one STREAM-INF; forced-Software transcode emits `-c:v libx264 -threads 11 -preset veryfast`, one ffmpeg per session, ~710–860% CPU (bounded, not the all-12-core pin), API responsive throughout. macOS dev uses VideoToolbox by default, so the pin only reproduces under forced Software (= the Linux-Docker path), which is what was measured.
- **P2.x micro-items — decided.** Remux readrate kept at 60 (lowering to 10 would regress deep-seek-resume from ~25s to ~2.5min; a copy is ~1 core regardless, so no CPU gain). 3s transcode segments deferred (neutral on encode cost; ripples through shared segment/GOP math). StartTimeTicks folded into Track B (web is segment-driven, so position is already honored).
- **P0.3 (buffer 30s/6s + 24s seek window + stdin throttle) — REMAINING.** Coupled (a 240s buffer defeats any pause; a tight window without the small buffer causes restart churn). The stdin throttle needs net-new `ProcessExecutor` streaming-stdin support + **real-playback validation** (a blind throttle can stall playback). Lower priority than expected: for the heavy 4K-HDR pin a slow software encode never races ahead, so P0.1 + P0.4 already deliver the bulk of the win; P0.3 mainly bounds easy-encode burst and tightens seeks.
- **Track B (app direct-play parity) — REMAINING; gap re-scoped by investigation.** All HDR/DoVi/profile/level/bit-depth metadata is **already persisted** (`MediaStreamRow`, no EF migration needed) and **already surfaced on `/Items/{id}/PlaybackInfo`** via `MediaStreamInfoResult` (`PlaybackInfoService.BuildStreams:254-292`) — the path apps use to negotiate. So the catalog-DTO badge gap (`JellyfinCatalogMediaStreamDto`, the item deferred in `docs/jellyfin-parity-audit.md`) is cosmetic, not the CPU lever. **The real app-direct-play gap:** `ApplicationContractMapping.cs:19-32` reads HDR support only from a top-level `SupportedVideoRangeTypes` field Infuse doesn't send, and maps only `DirectPlayProfiles` — **not the DeviceProfile `CodecProfiles`** where Jellyfin clients declare HDR/DoVi + profile/level support. So an Infuse client gets HDR10 *remuxed* and DoVi P5 *transcoded* (heavy) where real Jellyfin direct-plays. Fixing it = model `CodecProfiles` in `DeviceProfileRequest`, replicate Jellyfin `StreamBuilder` CodecProfile/VideoRangeType condition evaluation, and feed it into `VideoDirectPlayPolicy.Decide`. **Drive this from a fresh Mode-A Infuse capture** (`scripts/dev/jellyfin-capture-proxy.mjs`) — the current `~/jf-real2.jsonl` capture did not log request DeviceProfile bodies, so the exact Infuse profile shape must be re-captured before implementing (per the repo's own parity methodology). **CopyAudio in remux** is the safe, capture-independent sub-win (gated by the already-computed `decision.CopyAudio`): emit `-c:a copy` for client-accepted codecs instead of always re-encoding to stereo AAC. It needs the copy flag threaded into the remux URL + the `audioCacheKey` (so a copy and a transcode of the same track don't collide on one cache dir) — a bounded but careful change.

---

## 0. Verification summary (re-confirmed against source + real ffmpeg)

**Prismedia current state**
- **Transcode job runs flat-out to EOF, no governor.** `Generation.cs:26` computes `endSegment = SegmentCount(duration)-1`; `VirtualRenditionArguments` (`Encoding.cs:19-120`) emits **no `-t`/`-to`, no `-threads`, no `-readrate`**. One `-ss` seek, encode to EOF.
- **No thread cap anywhere** (grep clean).
- **Master playlist always advertises the full ABR ladder** — `BuildVirtualMasterPlaylist` (`HlsAssetService.cs:582-605`) iterates all renditions with no single-variant/setting gate.
- **Reuse window is 50 segments / 300s** (`ActiveGenerationReuseWindowSegments`, `HlsAssetService.cs:29`); `CancelActiveRenditionGenerations` is **per-rendition-name only** (`:392-398`) — a forward seek past the window orphans the old EOF-bound job until the reaper.
- **Segment length 6s for transcode too** (`SegmentDurationSeconds = 6`, `HlsAssetService.cs:19`).
- **Remux always re-encodes audio to stereo AAC** (`RemuxArguments`, `Remux.cs:254-265`); the computed `VideoPlaybackDecision.CopyAudio` is **dead code, read nowhere**. Remux already paces with `-readrate 60` + `-readrate_initial_burst 30` (`Remux.cs:27-28`).
- **Stream plan is lost** — `BuildTranscodingUrl`/`BuildRemuxUrl` (`PlaybackInfoService.cs:129-159`) carry only `MediaSourceId`, `PlaySessionId`, `AudioStreamIndex`, `ApiKey`; `StartTimeTicks`/`MaxStreamingBitrate`/`SubtitleStreamIndex` are bound but never consumed.
- **Client capabilities not sent** — `playback-negotiation.ts:31-40` sends no `MaxStreamingBitrate`, no `SupportedVideoRangeTypes`; web buffers **240s/800MB** (`video-player-load.ts:55-58`).
- **MediaStream HDR/DoVi metadata not surfaced to the Jellyfin DTO** — `docs/jellyfin-parity-audit.md` flags this as deferred: the probe already computes `VideoRange`/`VideoRangeType`/`Dv*`/`Profile`/`Level`/`BitDepth` but they aren't on the API-facing DTO, so an app can't decide to direct-play HDR/DoVi.
- **GPU is opportunistic-with-fallback already** (`ResolveTranscoderProfile`, `Encoding.cs:406-420`; retry ladder `Generation.cs:98-146`). Default image maps no `/dev/dri` → Software. **Even with `/dev/dri`, HDR/DoVi tonemap is forced back to software on Linux** (`ResolveEffectiveTranscoderProfile`, `Encoding.cs:322-335`) — a device only offloads SDR encode.
- **Keyframe-grid bug — reproduced live.** Keyframes `[0,19,20,25]` through real `ffmpeg -c copy -f hls` (jellyfin-ffmpeg 7.1.3) wrote `[19,1,5,5]` (4 segments); `BuildRemuxSegmentDurations` (`Remux.cs:406-434`) computes `[19,6,5]` (3). The existing test (`HlsAssetServiceTests.cs:54+`) doesn't cover the long-GOP case.
- **No stdin on the process executor** — a throttle needs net-new executor support.

**Jellyfin reference mechanism (the parity target)**
- **One ffmpeg per session, bounded by a tight seek window.** `DynamicHlsController.cs:1477-1521`: `segmentGapRequiringTranscodingChange = 24 / state.SegmentLength`. On a segment request it **kills + restarts** the transcode when `segmentId < currentTranscodingIndex` (backward seek) or `segmentId - currentTranscodingIndex > 24/SegmentLength` (forward jump > **24s**); otherwise it serves from the running job. This is the real "bounded window" — **24 seconds**, vs our 300s.
- **Throttle implemented, default off.** `EncodingOptions.cs:24-25`: `EnableThrottling = false`, `ThrottleDelaySeconds = 180`. When on, `TranscodingThrottler` writes `p`/`u` to ffmpeg stdin once the producer is ≥ `max(ThrottleDelaySeconds, 60)`s ahead of the download position.
- **Small client buffer.** jellyfin-web `htmlVideoPlayer/plugin.js:443,450`: `maxBufferLength = 30`, dropped to `6` for high-bitrate; `maxMaxBufferLength` = same. `backBufferLength = Infinity` for VOD (`:120`). The small forward buffer is what keeps the player inside the 24s window so the kill/restart isn't tripped in steady playback.
- **Single main variant by default**, ABR opt-in (`EnableAdaptiveBitrateStreaming`), `EnableSegmentDeletion = false` (`EncodingOptions.cs:26`), `-threads 0` default.
- **Apps direct-play/remux HEVC/HDR/DoVi** when their DeviceProfile + the source's MediaStream metadata say they can — so the heavy content the user sees Jellyfin "handle better" is usually **not transcoded at all**.

---

## 1. Why Jellyfin wins — two tracks

### Track A — when it must transcode (mostly web), it doesn't pin
Jellyfin per session = **one** ffmpeg, **bounded to ~24-30s ahead of the playhead** (tight seek window + small client buffer), single variant. Prismedia multiplies that: it advertises the **whole ~13-rung ladder** with no gate, each rendition is a **separate full-tail ffmpeg to EOF**, an ABR switch *or* a forward seek past the **300s** window **spawns a second job without killing the first**, with **no thread cap**, and the player pulls **240s** ahead. Practical worst case is a transient 2-4 concurrent software x264 transcodes per session, collapsing to 1× steady-state. Pin = `N_renditions × encode-to-EOF × no-thread-cap`. The fix is to collapse onto Jellyfin's shape: single variant + the 24s window + small buffer + throttle + thread cap.

**Honest caveat:** a 4K HDR→SDR software tonemap+encode is inherently heavy for *both* servers on a CPU-only box — the window/buffer/throttle reduce *wasted* work (racing to EOF, orphaned jobs), not the floor cost of one sustained 4K transcode. Which is why Track B matters more for the heavy content.

### Track B — the bigger win: it mostly doesn't transcode at all
For apps (Infuse, official Jellyfin clients) that natively decode HEVC/HDR/Dolby-Vision, Jellyfin **direct-plays the original file or stream-copy-remuxes** it — near-zero CPU. Prismedia transcodes the same content because (1) it doesn't surface the MediaStream HDR/DoVi metadata an app needs to assert direct-play, (2) it doesn't fully honor the app's DeviceProfile, and (3) the web client never advertises range/codec support so HDR is denied direct-play. Closing this means the 4K/HDR/DoVi titles — exactly the ones that pin — **stop transcoding entirely** for capable clients. This is the largest CPU + capability win and is co-primary with Track A.

**Ranked levers:**

| Rank | Track | Lever |
|---|---|---|
| **i** | B | **Direct-play / remux for capable apps** — surface full MediaStream HDR/DoVi metadata + honor the DeviceProfile so HEVC/HDR/DoVi is decoded natively, not transcoded. Removes the heavy transcode entirely. |
| **ii** | A | **Single main HLS stream by default** (setting-gated) — kills the multi-job fan-out. |
| **iii** | A | **One transcode per session: adopt Jellyfin's `24/SegmentLength` kill/restart window** (replace the 300s reuse window) + cross-rendition/cross-seek teardown + a global concurrency guard. |
| **iv** | A | **Small client buffer (30s / 6s-high-bitrate) + throttle (180s stdin pause)** — coupled; the buffer keeps the player inside the window, the throttle stops a fast encode racing to EOF. |
| **v** | A | **Thread cap (`-threads ProcessorCount-1`)** — one transcode can't claim the whole box. |
| **vi** | A/B | **Transcode-LESS on the margins** — honor `CopyAudio`, lower remux readrate, send range/bitrate caps. |

GPU stays opportunistic-with-fallback (already implemented); even with `/dev/dri` mapped, HDR tonemap stays on CPU on Linux, so GPU never fixes the 4K-HDR pin. Not a lever.

---

## 2. Reconciliation decisions

| Conflict | Resolution |
|---|---|
| **What leads** | **Two tracks, co-primary.** Track A (web CPU discipline) and Track B (app direct-play parity). The original "GPU-first" framing is wrong — Docker default is CPU-only and GPU can't tonemap HDR on Linux anyway. |
| **Bounded encode** [#1] | **Jellyfin's real mechanism, not a `-t` window.** Replace the 50-seg/300s reuse window with `24 / SegmentLength` and kill/restart on backward-seek or >24s-forward-gap, mirroring `DynamicHlsController.cs:1477-1521`. |
| **Throttle** [#2] | **Implement it** (Jellyfin parity): single job + stdin `p`/`u` pause at `max(ThrottleDelaySeconds,60)`s ahead. Coupled with the small buffer. Default: recommend **on** for Prismedia (justified divergence from Jellyfin's default-off, since our structural risk is higher) — confirm after measuring. |
| **Client buffer** [#3] | **Reduce to Jellyfin's `30s` / `6s`-high-bitrate** (`htmlVideoPlayer/plugin.js:443,450`). Required for the 24s window + throttle to engage without restart churn. Tradeoff: less instant far-seek buffer — acceptable, it's exactly what Jellyfin clients use. |
| **readrate value** | Different paths. Transcode gets the window+throttle, not a readrate. The **remux/copy** path lowers `RemuxReadRate` 60 → 10. |
| **Full app parity** [#4] | **In scope (Track B).** Surface MediaStream HDR/DoVi metadata, honor the full DeviceProfile, complete the PlaybackInfo contract + MediaSource fields, serialize the stream plan into the HLS URL, add the `master→main.m3u8` chain. Coordinate with `docs/jellyfin-parity-audit.md`. |
| **Bitrate-test endpoint + 70% factor** | **Skip** — over-scope for a private single-user LAN; treat LAN as unbounded with one optional user cap. |
| **LAN-subnet ABR gate** | **Cut** — single-variant is the unconditional default behind one global setting; no IP detection. |
| **CopyAudio ordering** | Gate on the decodable-audio capability list (or a conservative hardcoded allowlist) before emitting `-c:a copy`. Never copy an undecodable codec. |

---

## 3. Scope calls

| Bucket | Item |
|---|---|
| **DO-NOW (P0, Track A)** | Single main HLS variant (default; ABR behind one global setting). |
| | Adopt Jellyfin's `24/SegmentLength` kill/restart window + cross-rendition/cross-seek teardown + one global transcode semaphore. |
| | Small client buffer (30s / 6s) + throttle (stdin pause, 180s), shipped together. |
| | `-threads ProcessorCount-1` cap on transcode; `-threads 2` on remux. |
| | `tonemapx` startup health check. |
| **DO-NOW (P1, Track B — the direct-play win)** | Surface full MediaStream HDR/DoVi metadata to the Jellyfin DTO (`VideoRange`, `VideoRangeType`, `ColorTransfer/Primaries/Space`, `Dv*`, `Profile`, `Level`, `BitDepth`, `PixelFormat`, `RefFrames`). Bridges to `docs/jellyfin-parity-audit.md` deferred item. |
| | Honor the app's full DeviceProfile (DirectPlay + Codec + Transcoding + Subtitle profiles) server-side. |
| | Web client: send `SupportedVideoRangeTypes`, `MaxStreamingBitrate`, audio-codec list; bind GET PlaybackInfo (GET+POST merge, body wins). |
| **DO-SOON (P2)** | Honor `CopyAudio` in remux (gated on the audio allowlist). |
| | Lower `RemuxReadRate` 60 → 10. |
| | Carry `StartTimeTicks` through the forced-transcode URL. |
| | Serialize the stream plan into the HLS URL + `master→main.m3u8` canonical chain. |
| | Keyframe-grid bug fix + test. |
| | 3s transcode segments (separate const from 6s remux). |
| | Subtitle stream probing/selection; burn-in forces transcode. |
| **DEFER (P3)** | Full DeviceProfile *contract* expansion (ContainerProfiles, ResponseProfiles, levels/ref-frames/anamorphic conditions) beyond what direct-play needs. |
| | fMP4-for-HEVC/AV1 vs MPEG-TS-for-H264 from `SegmentContainer`. |
| **SKIP / LOW** | `Playback/BitrateTest` + 70% factor; `IsInLocalNetwork`/`LocalNetworkSubnets` gate; client-supplied `CpuCoreLimit`. |

---

## 4. Prioritized roadmap

### P0 — Track A: stop the web-transcode pin (self-contained; ship together)

**P0.1 — Single main HLS variant (unconditional default, setting-gated)**
- **Files:** `HlsAssetService.cs` (`BuildVirtualMasterPlaylist:582-605`, `RenditionsFor:552-560`), `HlsAssetServiceOptions.cs`, `VideoHlsEndpoints.cs` (read `EnableAdaptiveBitrateStreaming`, default false), settings registry.
- **Change:** Emit **one** `#EXT-X-STREAM-INF` (the top source-capped rung) by default; gate extra variants behind `EnableAdaptiveBitrateStreaming == true`. No LAN/subnet detection. `MaxStreamingBitrate`-driven rung selection is a P1 refinement.
- **Validate:** `curl master.m3u8` → exactly one STREAM-INF; ffmpeg process count stays 1/session through manual quality nudges.

**P0.2 — One transcode per session: Jellyfin's seek window + teardown + guard**
- **Files:** `HlsAssetService.cs` (`GetVirtualSegmentAsync:287-330`, reuse-window const `:29`, `FindActiveRenditionGeneration:335-351`, `CancelActiveRenditionGenerations:392-398`), `Generation.cs`.
- **Change:**
  - (a) **Replace the 50-segment / 300s reuse window with `24 / SegmentDurationSeconds`** (= 4 segments at 6s, 8 at 3s), mirroring `DynamicHlsController.cs:1478`. Kill + restart the transcode when a requested segment is **behind** the running transcoder index or **more than 24s ahead**; otherwise serve from the running job.
  - (b) **Cross-rendition teardown:** cancel **all** active renditions for `{id}/{audioCacheKey}` (broaden the prefix from `.../{rendition.Name}/`), not just the same name.
  - (c) **Global concurrency guard:** one process-wide `SemaphoreSlim` (default `max(1, ProcessorCount/4)` full transcodes) before spawning ffmpeg. One guard only.
- **CPU effect:** Bounds a session to one transcode held near the playhead; a far seek kills+restarts instead of orphaning an EOF-bound job; N sessions can't oversubscribe.
- **Validate:** Rapid ABR oscillation + a forward seek across the window → concurrent x264 PIDs ≤ guard; superseded PID dies **immediately**, not at the reaper; steady-state = 1 PID/session.

**P0.3 — Small client buffer + throttle (coupled; ship as one change)**
- **Files:** web `video-player-load.ts:55-58` + `adaptiveHlsBufferConfig`; backend `Prismedia.Infrastructure/Processes/*` (**add stdin write support**), `Generation.cs`, new throttle helper, `HlsAssetServiceOptions.cs`/settings.
- **Change:**
  - **Buffer (web):** `maxBufferLength: 30`, `maxMaxBufferLength: 30`, drop to `6` for high-bitrate sources (mirror `htmlVideoPlayer/plugin.js:443,450`), `maxBufferSize: 60_000_000`, keep `backBufferLength` generous, keep `ExtendedHlsMaxTimeToFirstByteMs`.
  - **Throttle (backend):** redirect ffmpeg stdin; a 5s loop writes `p` when the produced frontier is ≥ `max(ThrottleDelaySeconds=180, 60)`s ahead of the last served segment, `u` on the next advancing request. Gate behind `Prismedia:Hls:EnableThrottling` (recommend **default on**; confirm after measuring).
- **Why coupled:** a 240s buffer defeats any pause; a shrunk buffer without a working pause just risks rebuffering. The buffer also keeps the player inside the P0.2 24s window so it doesn't trip restart churn.
- **Validate:** With the 30s buffer + throttle, ffmpeg CPU drops toward ~0 during steady buffered playback and resumes on drain; no rebuffer before the buffer empties; far seeks kill/restart cleanly.

**P0.4 — Thread cap**
- **Files:** `Encoding.cs` (`VirtualRenditionArguments`, `RemuxArguments`), `HlsAssetServiceOptions.cs`, settings.
- **Change:** `-threads {n}`. Transcode: `n = encodingThreadCount > 0 ? min(encodingThreadCount, ProcessorCount) : ProcessorCount - 1`. Remux: `-threads 2`. Expose `Prismedia:Hls:EncodingThreadCount` (default 0 = auto → `ProcessorCount-1`).
- **Validate:** `htop` shows ≤ `ProcessorCount-1` cores saturated for one transcode; API latency unaffected.

**P0.5 — `tonemapx` startup health check**
- **Files:** the transcoder-profile resolution / startup path feeding `Encoding.cs`; health surface.
- **Change:** At startup run `ffmpeg -h filter=tonemapx` (or `-filters`) and assert `tonemapx` exists; if absent, surface a loud health-check warning that HDR-on-CPU parity is degraded (the software-no-tonemap fallback silently produces magenta-DV and burns CPU). Don't hard-fail playback.
- **Validate:** Non-tonemapx binary flags; jellyfin-ffmpeg 7.1.3 passes.

### P1 — Track B: make capable apps direct-play (the bigger CPU win)

**P1.1 — Surface full MediaStream HDR/DoVi metadata to the Jellyfin DTO**
- **Files:** `ApplicationContractMapping.cs`, `JellyfinCatalogService.MediaStreams.cs`, the API-facing `TechnicalCapability` / `JellyfinCatalogMediaStreamDto`. (Bridges the deferred item in `docs/jellyfin-parity-audit.md`.)
- **Change:** Map the already-probed `VideoRange`, `VideoRangeType`, `ColorSpace/Transfer/Primaries`, `DvProfile/DvLevel/DvVersionMajor/Minor/RpuPresentFlag/BlPresentFlag`, `Profile`, `Level`, `PixelFormat`, `BitDepth`, `RefFrames`, `IsAVC`, audio `ChannelLayout/Channels/SampleRate`, `DisplayTitle` into the DTO. No new probing — mapping only.
- **Validate:** Mode-B capture (proxy upstream = Prismedia) of a 4K HDR/DoVi title diffs equal to `~/jf-ground-truth.json` MediaStream; Infuse shows the correct HDR/DoVi badge.

**P1.2 — Honor the app's full DeviceProfile server-side**
- **Files:** `VideoDirectPlayPolicy.cs`, `PlaybackInfoService.cs`, the DeviceProfile contract.
- **Change:** Evaluate DirectPlay against the app's DirectPlayProfiles + CodecProfile conditions (range/profile/level/bit-depth) + audio; fall to stream-copy remux when only container/audio fails; transcode last. Make HEVC/HDR/DoVi direct-play when the app advertises it. This is what makes Infuse stop triggering a transcode.
- **Validate:** A capture-diff shows `PlaybackInfo` returns DirectPlay (no `TranscodingUrl`) for HEVC/HDR/DoVi on a capable profile; CPU near zero during playback.

**P1.3 — Web client advertises capabilities; bind GET PlaybackInfo**
- **Files:** `browser-device-profile.ts`, `playback-negotiation.ts:31-40`, `PlaybackInfoEndpoints.cs` (GET query binding + body-wins merge), `PlaybackInfoService.cs`.
- **Change:** Send `SupportedVideoRangeTypes` (so browser HDR isn't force-remuxed), optional `MaxStreamingBitrate` (default unbounded on LAN; selects the P0.1 single rung), and a decodable audio-codec allowlist. Support GET PlaybackInfo with query/body merge.
- **Validate:** HDR source negotiates DirectPlay on a capable browser; a user cap selects a lower single rung.

### P2 — Transcode-less margins + correctness + URL plan

**P2.1 — Honor `CopyAudio` in remux** (depends on P1.3 audio allowlist). `Remux.cs:220-284` — emit `-c:a copy` for allowlisted codecs (AAC always; AC3/EAC3 when advertised), down-mix/encode only for incompatible (DTS/TrueHD). Wire the dead `CopyAudio` decision. **Never copy an undecodable codec.** Validate: AAC/EAC3-advertised → copy (channels preserved); DTS → AAC fallback.

**P2.2 — Lower remux readrate** `Remux.cs:27-28`: `RemuxReadRate` 60 → 10, keep burst 30. Validate: deep seek reaches position in a couple seconds; no I/O spike.

**P2.3 — Carry StartTimeTicks through forced-transcode fallback** `VideoPlayer.svelte`, `playback-negotiation.ts`, `BuildTranscodingUrl`, `Encoding.cs` (`-ss`). Important once the window/throttle is in: a fallback starts its window at the resume offset, not 0.

**P2.4 — Keyframe-grid bug fix + test** `Remux.cs:406-434`, `HlsAssetServiceTests.cs`. Replace the floor-jump advance with Jellyfin's advance-by-one-segment rule: running `desired = first + SEG`; per cut take the first keyframe `>= desired && > segmentStart`, append `cut - segmentStart`, `segmentStart = cut`, then `desired += SEG`. Test `[0,19,20,25]` dur 30 → `[19,1,5,5]`. Validate: diff `BuildRemuxVodPlaylist` vs real `ffmpeg -c copy -f hls`; seek lands, buffer reaches true EOF.

**P2.5 — 3s transcode segments** Split `SegmentDurationSeconds` into `TranscodeSegmentSeconds = 3` / `RemuxSegmentSeconds = 6`; update GOP math. Neutral on encode cost; TTFF/granularity + makes the `24/SegmentLength` window 8 segments. Validate: `EXTINF` ≈ 3.0.

**P2.6 — Serialize the stream plan into the HLS URL + `master→main.m3u8` chain** `PlaybackInfoService.cs`, `VideoHlsEndpoints.cs`, `IHlsAssetService`. Carry codec/bitrate/dimensions/copy-flags/segment-container/subtitle/play-session in the URL; add the `main.m3u8` canonical step so an app's negotiated choice is rehydrated per request instead of re-derived. Required for strict app parity. Validate: capture-diff URL shape vs ground truth.

**P2.7 — Subtitle stream probing/selection** Probe/select subs as part of the plan; burn-in forces full transcode (note the CPU cost). Validate: external/soft subs select without forcing transcode.

### P3 — Opportunistic GPU (doc) + deferred contract

**P3.1 — Keep GPU opportunistic (document; no code change).** VAAPI auto-detect + retry ladder already implement fallback. Document the opt-in (`devices: ['/dev/dri:/dev/dri']` + render-GID `group_add`; NVENC needs the NVIDIA runtime + `PRISMEDIA_HLS_TRANSCODER=Nvenc`) and the honest limit: **HDR/DoVi tonemap stays on CPU even with a device on Linux** (`Encoding.cs:322-335`); a device only offloads SDR encode. The software path inherits P0 discipline.

**P3.2 (DEFER).** Full DeviceProfile contract expansion beyond direct-play needs (ContainerProfiles, ResponseProfiles, level/ref-frame/anamorphic conditions); fMP4/TS container selection from `SegmentContainer`.

---

## 5. Risks & test plan

**Track A (web CPU):**
- *Single-variant removes client step-down on a heavy 4K transcode.* Mitigation: P1.3 `MaxStreamingBitrate` picks a lower single rung; thread cap keeps one rung sustainable. Test: full 4K HEVC HDR web playthrough on a representative CPU-only container — utilization below saturation, encode CPU drops at the throttle/buffer boundary.
- *24s window + small buffer → restart churn.* Test: steady playback re-spawns at most on a real seek, not while buffering; the 30s buffer stays inside the 24s window without tripping kill/restart. Tune buffer/window together; widen slightly if churn appears.
- *Throttle + 30s buffer → rebuffering on a slow encoder.* Test: throttle on, CPU-bound 4K — ffmpeg pauses on buffer-full, resumes before the buffer drains. Never ship the buffer shrink without the working pause.
- *Concurrency guard starves a 2nd viewer.* Test: two sessions each get one transcode; the guard queues, no stalls beyond TTFF.
- *`tonemapx` absent.* Mitigation: P0.5 health check.

**Track B (app parity):**
- *Over-claiming direct-play breaks playback on a less-capable app.* Mitigation: direct-play strictly gated on the app's DeviceProfile + the now-accurate MediaStream metadata; never assert a range/codec the profile didn't advertise. Test: Mode-A (real Jellyfin) vs Mode-B (Prismedia) capture-diff via `scripts/dev/jellyfin-capture-proxy.mjs` for Infuse on HEVC/HDR/DoVi/multichannel titles — zero decision disagreements; playback CPU near zero where Jellyfin direct-plays.
- *DoVi cast.* DoVi P5 must stay tonemapped (never direct-played to a non-DoVi target). Test P5 + P7/8-HDR10-fallback titles.

**Audio-copy (P2.1):** matrix AAC stereo (copy), EAC3 5.1 advertised (copy, channels preserved), EAC3 5.1 not advertised (AAC fallback), DTS/TrueHD (transcode). No silent/undecodable output ever ships.

**Seek accuracy (keyframe fix):** new `[0,19,20,25]` unit case + a real long-GOP sample diffed against live `ffmpeg -c copy -f hls`; playhead lands, no snap-back, buffer reaches true EOF.

**Harness:** backend/ffmpeg changes are non-HMR → full-stack restart, test through `http://localhost:8008`. Add unit tests for the seek-window/teardown, the `CopyAudio` branch, and the keyframe grid. Re-run the capture-proxy parity diff for Infuse + Chrome/Firefox/Safari.

---

## 6. Decisions & remaining questions

**Resolved (this review):**
- [#1] Bounded encode = Jellyfin's `24/SegmentLength` kill/restart window (not a `-t` window).
- [#2] Implement the throttle (recommend default on; confirm after measuring).
- [#3] Reduce the web buffer to Jellyfin's 30s / 6s-high-bitrate, coupled with the window + throttle.
- [#4] Full app parity is in scope (Track B): MediaStream metadata + DeviceProfile + URL plan.
- Thread cap `ProcessorCount-1`; single-variant unconditional behind one setting; bitrate-test skipped.

**Remaining (non-blocking; can default and adjust):**
1. **Throttle default on vs off** — recommend on (we're more exposed than Jellyfin); revisit after P0 CPU measurement.
2. **Track ordering** — P0 (web CPU) and P1 (app direct-play) are independent; do them in parallel, or land P0 first to stop the visible pin, then P1 for the bigger structural win? Recommend P0 first (fast, self-contained), P1 immediately after.
3. **How far to push P2.6 URL-plan serialization** now vs after a capture-diff confirms what Infuse actually needs — recommend driving it from a fresh Mode-A/Mode-B capture rather than building speculatively.

---

**Key files:** `apps/backend/src/Prismedia.Infrastructure/Videos/HlsAssetService*.cs`, `HlsAssetServiceOptions.cs`, `Prismedia.Infrastructure/Processes/*` (stdin for throttle); `Prismedia.Application/Videos/{PlaybackInfoService,VideoDirectPlayPolicy}.cs`; `Prismedia.Application/Jellyfin/JellyfinCatalogService.MediaStreams.cs` + `ApplicationContractMapping.cs` (MediaStream metadata); `Prismedia.Api/Endpoints/Jellyfin/{Items/PlaybackInfoEndpoints,Videos/VideoHlsEndpoints}.cs`; `apps/web-svelte/src/lib/player/{video-player-load,playback-negotiation,browser-device-profile}.ts`; tests in `Prismedia.Infrastructure.Tests/HlsAssetServiceTests.cs`. Coordinates with `docs/jellyfin-parity-audit.md` (client/DTO parity).
