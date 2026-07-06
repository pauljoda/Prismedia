# Prismedia ← Sonarr + Prowlarr Drop-In Parity — Gap Matrix

**Goal being audited:** Prismedia (self-hosted media library, branch `feat/book-acquisition`) as a full
drop-in replacement for the **Sonarr + Prowlarr** pair, generalized across media kinds (books, movies,
series, music) via plugins.

**Sources synthesized:** every report present under `parity/` and `undefined/parity/`:
`sonarr-decision-engine.md`, `sonarr-indexers-search.md`, `sonarr-download-clients.md`,
`sonarr-monitoring-tasks.md`, `live-instances.md`, `prismedia-frontend.md`,
`prismedia-plugins-indexers.md`, `parity/prismedia-acquisition-backend.md`,
`parity/prismedia-download-import.md`.

> **Two of the eleven reports named in the audit brief do not exist on disk:** `sonarr-import-rename.md`
> and `prowlarr-delta.md`. Their subject matter is covered indirectly here — import/rename by the two
> Prismedia backend reports plus `live-instances.md` §2.8/§3.6/§4.6 (Sonarr/Radarr/Lidarr naming), and
> the Prowlarr delta by `live-instances.md` §1 (the real Prowlarr install) plus
> `prismedia-plugins-indexers.md` §3–4. Where a claim would have leaned on one of the two missing
> reports, it is flagged. `undefined/` is a literal template-substitution artifact from the report
> generators, not a real path component; the real Sonarr source lives at
> `/Users/pauldavis/Dev/_ARCHIVE/Sonarr`.

> **Status note (2026-07-06):** this matrix is the pre-build gap snapshot used to
> plan the branch. The P0/P1 build-plan items have since landed in code, including
> SABnzbd/usenet, Transmission/client routing, direct Torznab/Newznab, remote path
> mappings, seed goals, hardlink import, quality ladders, custom formats, history,
> Wanted lists, and multi-kind imports. Use `BUILD-PLAN.md` and
> `E2E-VERIFICATION.md` for current ship-readiness; the final live grab/import
> gate is still intentionally called out there.

**Severity key for the drop-in goal:**
- **P0** — the core request→search→grab→import→monitor loop is broken or a real Sonarr user's data
  can't come across without it.
- **P1** — expected by essentially any Sonarr user; its absence is immediately noticed.
- **P2** — should-have; a meaningful segment of users rely on it.
- **P3** — niche / deferrable.

**Prismedia status key:** DONE (matches Sonarr in substance) / PARTIAL (present but materially narrower)
/ MISSING (no implementation). Evidence cites the Prismedia file paths the acquisition/import/frontend
mappers found.

---

## 1. Feature-domain matrix

### 1.1 Indexers & search aggregation

| Capability | Sonarr/Prowlarr behavior | Prismedia status | Sev |
|---|---|---|---|
| Talk to an indexer aggregator | Prowlarr aggregates N indexers into one Torznab/Newznab feed; Sonarr consumes the synced feed. | **DONE** — `ProwlarrIndexerClient` calls Prowlarr's aggregate `api/v1/search` JSON (not raw Torznab), maps to `IndexerRelease`. `Prismedia.Infrastructure/Acquisition/ProwlarrIndexerClient.cs`. | P0 |
| Native tracker definitions (Cardigann) | Prowlarr owns ~27 Cardigann YAML-defined public trackers + native clients (Knaben/SubsPlease) — 32 indexers on the live box. | **MISSING (delegated)** — Prismedia has no tracker-definition engine; it *requires an external Prowlarr* to reach any tracker. So it replaces Sonarr's role but **not Prowlarr's**. `prismedia-plugins-indexers.md` §4. | P1 |
| Second aggregator path (Jackett) | Prowlarr/Jackett interchangeable. | **MISSING** — `IndexerKind.Jackett` enum member exists, no client registered; `IndexerSearchClientFactory.Get` throws `NotSupportedException`. `AcquisitionServiceKinds.cs`. | P3 |
| Multiple indexers, fan-out by category subscription | Each consuming app subscribes to a Torznab category subtree (Sonarr→5000s TV, Radarr→2000s, etc.); Prowlarr routes only matching indexers. | **PARTIAL** — `TorznabCategories.ForKind` narrows per-kind (Movies 2000/Audio 3000/TV 5000/Books 7000). But it's one Prowlarr feed, not per-indexer category capability negotiation. `MediaReleaseDecisionEngines.cs`. Known bug: category `8000` is silently dropped from every Book search even though the UI defaults to `7000,8000`. | P2 |
| Per-indexer live capability discovery (`?t=caps`) | Newznab/Torznab caps endpoint parsed + cached 7 days; page size, supported params, category tree adapt per indexer. | **MISSING** — flat user-entered category list; no caps negotiation (Prowlarr does it upstream). `sonarr-indexers-search.md` §1.2. | P2 |
| Indexer priority / tiering | `IndexerPriority` (lower=preferred) is a real tie-break axis in `DownloadDecisionComparer`; download-client selection groups by priority then round-robins. | **PARTIAL/DEAD** — `Priority` field persisted + in API but **never consulted**; every enabled indexer queried concurrently on every ladder rung. `parity/prismedia-acquisition-backend.md` §10, §12. | P2 |
| Indexer health / backoff | 5-min suppression escalating to 5 levels; a flapping indexer is temporarily skipped. | **MISSING** — a failing indexer contributes an error string every single search, forever; no disable/backoff. `parity/prismedia-download-import.md` §2. | P2 |
| Per-indexer rate limiting | `queryLimit`/`grabLimit`/`limitsUnit` per indexer; global RSS rate limits; 2s/host grab rate limit. | **MISSING** (Prowlarr handles it upstream). | P3 |
| FlareSolverr / Cloudflare challenge proxy | Tag-scoped indexer proxy (`indexerproxy`) routes flagged indexers through a headless-browser solver. | **MISSING (delegated to Prowlarr).** `live-instances.md` §1.7. | P3 |
| Download-URL proxy (credential hiding) | Prowlarr rewrites every release download URL to route back through itself; consuming apps never see raw indexer secrets. | **DONE (inherited)** — Prismedia passes Prowlarr's self-authenticating proxy URL straight to qBittorrent. `QBittorrentDownloadClient.AddAsync`. | P1 |
| Query construction per numbering scheme | Sonarr has a `Fetch()` overload per criteria type (single/season/daily/daily-season/anime-episode/anime-season/special); daily keyed by air date, anime by absolute number. | **PARTIAL** — `ReleaseQueryLadder` builds ordered query strings per kind (music/TV-episode/movie/season-pack/series/book), stopping at first accepted rung. No daily/air-date search, no anime absolute-number search, no scene-name mapping. `ReleaseQueryLadder.cs`, `TvReleaseTokens.cs`. | P1 |

### 1.2 Release parsing & decision engine

| Capability | Sonarr behavior | Prismedia status | Sev |
|---|---|---|---|
| Release title parse (quality/source/resolution/revision/group) | Rich `Parser` + `QualityParser`: 22-entry quality catalog, source axis, resolution, PROPER/REPACK/REAL revision, release-group extraction. | **PARTIAL** — pure title-regex token detection only (`ReleaseLanguageDetection`, `BookFormatDetection`, `TvReleaseTokens`, video/music resolution/codec tokens). No release-group extraction, **no proper/repack/REAL detection**, no NFO/attribute parsing, multi-episode only simple SxxEyy/NxN. `prismedia-plugins-indexers.md` §3.6, gap #6. | P1 |
| Specification gauntlet (accept/reject with reasons) | 37 `IDownloadDecisionEngineSpecification`s, tiered by cost (parsing/DB tier 0, disk tier 1), all rejections surfaced. | **PARTIAL** — 11–12 pure specs per kind (`BookReleaseDecisionEngine` + `MediaReleaseDecisionEngines`), no I/O, no cost tiering (all pure functions). Covers protocol/link/seeders/size/required/ignored/language + book format/quality/upgrade + TV unit. `parity/prismedia-acquisition-backend.md` §5.1. | P1 |
| Structured, enumerable rejection reasons | 72-value `DownloadRejectionReason`; live search showed 7 distinct reason strings on one query. | **PARTIAL/DONE-in-spirit** — 13-value `ReleaseRejectionReason`, always surfaced to the review UI (never hidden). Much smaller vocabulary but the transparency contract matches. `AcquisitionStatus.cs`; frontend `ReleaseTable.svelte`. | P1 |
| Candidate ranking between releases | `DownloadDecisionComparer`: 9-stage tie-break (quality→revision→CF score→protocol→episode count→episode number→indexer priority→peers(log10)→size-closest-to-preferred). | **PARTIAL** — single fixed scalar score per kind (quality/resolution/codec dominates, then preference, then log-scaled seeders, then peers). No preferred-size targeting, no indexer-priority axis, no anime episode-count logic. `BookReleaseScore.cs`, `MediaReleaseEvaluation`. | P1 |
| Quality profiles (allowed set + ordered ranking) | Ordered `Items` list = the ranking; quality **groups** (WEB 1080p bundling WEBDL+WEBRip); per-quality allowed toggle; 6 profiles on the live box. | **PARTIAL** — per-kind profiles exist but quality is a **fixed heuristic tier ladder**, not a user-orderable allowed-quality list with groups. Book has a real 2-axis (source×format) rank; video/music rank by regex resolution/codec only. `AcquisitionProfileKinds.cs`; `live-instances.md` §2.1. | P1 |
| Cutoff / upgrade-until-cutoff | `Cutoff` quality + `UpgradeAllowed`; stops upgrading once met; **5 of 6 live profiles pin quality with `upgradeAllowed:false`**. | **PARTIAL** — upgrade-until-cutoff is **book-only** (`UpgradeUntilCutoff`, `CutoffSourceTier`/`CutoffFormatTier`, child-acquisition swap via `OwnedFileReplacer`). Movies/TV/music fulfill on import, **no owned-quality concept, no upgrade loop**. `parity/prismedia-acquisition-backend.md` §8, §12. | P1 |
| Quality-definition size gates (MB/min) | Per-quality `minSize`/`maxSize`/`preferredSize` in MB per runtime minute; rejects mis-tagged/fake releases; 22/30/38 defs across Sonarr/Radarr/Lidarr. | **MISSING** — only absolute `MinSizeBytes`/`MaxSizeBytes` per profile, no per-quality per-minute size sanity gate. `SizeSpecification`. | P2 |
| Custom Formats (composable scored classifier) | Named bundles of specification conditions (release-title regex, language, source, resolution, indexer flags, size, release group, release type); per-profile score; feeds min/cutoff/upgrade thresholds. Live: Sonarr `(?i)dual\|eng\|english` ORed; Radarr adds `LanguageSpecification`. | **PARTIAL** — `PreferredTerms` (100 pts), `WeightedTerms` (`term: weight`, ±), `RequiredTerms`, `IgnoredTerms`, preferred languages. Regex-on-title and language-preference only — no indexer-flag/source/resolution/release-group condition types, no reusable named format, no min/cutoff/upgrade **score** thresholds. `parity/prismedia-acquisition-backend.md` §5.2; `live-instances.md` §2.3. | P1 |
| Release Profiles (must-contain / must-not-contain, tag+indexer scoped, air-date gate) | Perl-flavored regex terms, `AirDateRestriction`, tag/indexer scoping, `ExcludedTags`. (Empty on the live box.) | **PARTIAL** — required/ignored terms exist per profile but **not tag/indexer-scoped**, no air-date restriction, no Perl-regex dialect. `live-instances.md` §2.10 (unused live). | P2 |
| Proper/Repack handling (`DownloadPropersAndRepacks`) | Tri-state `PreferAndUpgrade`/`DoNotUpgrade`/`DoNotPrefer` across 7 call sites; live box = `preferAndUpgrade`. | **MISSING** — no revision/proper/repack awareness at all (see parsing gap). A user could approximate via a `WeightedTerm` on "PROPER". `sonarr-decision-engine.md` §9. | P2 |
| Delay profiles (protocol preference + bypass) | Per-tag protocol preference, usenet/torrent delay windows, `bypassIfHighestQuality`, `bypassIfAboveCustomFormatScore`; mandatory catch-all default. | **MISSING** — no delay window concept; searches grab the best accepted candidate immediately (or park for manual pick). `sonarr-decision-engine.md` §8. | P2 |
| Anime-specific logic | Absolute-episode numbering, release-group-match version upgrades, fewer-episodes-preferred comparer. | **MISSING** — no anime series type, no absolute numbering, no scene mapping. `sonarr-decision-engine.md` §6.3 item 4. | P3 |
| Blocklist (feeds back into search) | Series-scoped per-release blocklist; torrent-hash & title matching; pre-grab hash check. | **DONE** — `AcquisitionBlocklistRow`, content-addressed `hash:` identity else normalized `title:`; enforced at search, at queue, and in auto-redownload candidate selection. `parity/prismedia-download-import.md` §4.3–4.5. | P1 |

### 1.3 Download clients & transfer

| Capability | Sonarr behavior | Prismedia status | Sev |
|---|---|---|---|
| Download client breadth | **18 client implementations** across torrent RPC, Usenet RPC, blackhole, NAS (Download Station), Kodi. Live box: qBittorrent + SABnzbd. | **PARTIAL** — **one client: qBittorrent** (WebUI v2). `Transmission` enum member, no impl (throws `NotSupportedException`). `parity/prismedia-download-import.md` §1.1. | P1 |
| Usenet protocol (SABnzbd/NZBGet + NZB) | Full second protocol; NZB validation, CDH; live box runs SABnzbd + 2 Newznab indexers (DrunkenSlug, AnimeTosho). | **MISSING (actively rejected)** — `DownloadProtocol.Usenet` decoded only to be rejected by `ProtocolSpecification` in every engine. No NZB client, no NZB parsing. `prismedia-plugins-indexers.md` §5. | **P0 (live box uses it)** |
| Add release / poll / remove client abstraction | `IDownloadClient` (Download/GetItems/RemoveItem/GetStatus/GetImportItem/MarkItemAsImported). | **DONE** (torrent-shaped) — `IDownloadClient` with Add/AddTorrentFile/GetItem/ListItems/GetFiles/GetProperties/GetPieceStates/Remove/Test. `DownloadClientPort.cs`. | P0 |
| Magnet-vs-.torrent handling, hash discovery | Magnet hash parsed client-side; redirect-to-magnet handling; `.torrent` retry pipeline. | **DONE** — magnet or proxy URL add; info-hash from release else before/after category diff (15×1s poll). Manual `.torrent` upload fallback. `QBittorrentDownloadClient`. | P1 |
| In-flight poll / queue tracking | 5s debounced poll of all clients; `TrackedDownload` state machine (8 states); rebuilt from history on restart. | **PARTIAL** — `AcquisitionMonitorJobHandler` polls on the fixed 60s scheduler tick (**no configurable interval**), only while active transfers exist. `DownloadTransferRow` per acquisition. `parity/prismedia-download-import.md` §3. | P1 |
| Stall / removal detection | Per-client native seed/stall signals; encrypted-download detection (SABnzbd). | **DONE (careful)** — 5-min removal grace, 60-min stall grace with "moved since last poll" tolerance; `stalledDL/metaDL/error/missingFiles`. `parity/prismedia-download-import.md` §4.1. | P1 |
| Completed Download Handling (CDH) | `CompletedDownloadService` gate + import + verify; category/history gating; dangerous-file hold. | **PARTIAL** — completion routes to per-kind import engine or upgrade-replace; no history-based re-import verification; **no dangerous-extension hold** (see 1.5). `AcquisitionImportJobHandler`. | P1 |
| Failed Download Handling + auto-redownload | Blocklist + `EpisodeSearchCommand`/`SeasonSearchCommand` re-search; separate off-switches for interactive-search grabs; live box: both on. | **DONE (simplified)** — `AcquisitionFailedHandleJobHandler` blocklists then, if profile `AutoRedownload`, grabs next-best non-blocklisted candidate. No separate interactive-search off-switch. `parity/prismedia-download-import.md` §4.2. | P1 |
| Download client selection / priority / round-robin | Protocol filter + tag matching + indexer-pinned client + priority grouping + round-robin + fallback-on-failure across clients. | **MISSING** — "first enabled client by CreatedAt" wins; no per-kind/profile client routing, no priority, no fallback. `parity/prismedia-download-import.md` §1.4. | P2 |
| Per-client category | Category-per-consumer (`tv-sonarr`/`radarr`/`lidarr`/`prowlarr`) segregates downloads. | **PARTIAL** — one category per client config, not per-kind (all kinds land in one category, default `prismedia-books`). `parity/prismedia-download-import.md` §1.5. | P2 |
| Seed ratio / seed time management | Per-release seed config (ratio/time, separate `packSeedTime`); per-client "has reached seed limit"; remove only when satisfied; live box sets seedRatio/seedTime per indexer. | **MISSING** — no seed concept whatsoever. Every move-mode import calls `RemoveAsync(deleteData:true)` **immediately on completion** — ratio-hostile on private trackers. `parity/prismedia-download-import.md` §5. | P1 |
| Post-import category / relabel | `MarkItemAsImported` relabels done torrents in the client UI. | **MISSING** — no post-import relabel; torrent is deleted instead. | P3 |
| Remote path mapping | `{Host, RemotePath, LocalPath}` prefix rewrite for split-host/container deployments. | **MISSING** — assumes the client's `content_path` is directly readable by the Prismedia process; divergent paths just fail the import. `parity/prismedia-download-import.md` §6. | P1 |

### 1.4 Import, rename & placement

| Capability | Sonarr behavior | Prismedia status | Sev |
|---|---|---|---|
| Per-kind import engine (place + scan) | `DownloadedEpisodesImportService`; move/hardlink/copy; multi-episode handling. | **DONE** — 4 engines (Book/Movie/TV/Music) covering 5 `EntityKind`s, each with ambiguity blocking, path-traversal guard, collision-suffix. Writes `AcquisitionImportHintRow` → scan binds to the exact wanted entity (no dupes). `parity/prismedia-acquisition-backend.md` §6; `parity/prismedia-download-import.md` §7–8. | P0 |
| Hardlink import | `copyUsingHardlinks:true` on the live box — hardlink within a filesystem so the file keeps seeding while living in the library. | **MISSING** — only rename-move (copy+delete cross-device fallback) or full copy; no hardlink. Combined with immediate-delete seeding, no way to keep seeding a moved file. `parity/prismedia-download-import.md` §7.6. | P1 |
| Configurable naming templates per content shape | Token templates per shape: Sonarr 3 (standard/daily/anime) + 3 folder templates; Radarr 1; Lidarr 2 (single/multi-disc); colon-replacement, multi-episode style, illegal-char replacement. | **PARTIAL** — **books only** have a user `PathTemplate`. Movie/TV/music layouts are **hardcoded** in their engines (`Title (Year)/…`, `Series/Season NN/…`, `Artist/Album/`). No token dictionary, no per-shape/daily/anime templates, no colon-replacement/multi-episode-style knobs. `parity/prismedia-download-import.md` §7, gap #10. | P1 |
| Dangerous / executable file import hold | `RejectedImportService` holds (warning state) on `.scr`/executable/dangerous extensions; per-indexer opt-in hard-fail. **Live box has a `.scr` item held right now.** | **MISSING** — no dangerous-extension gate; `BlocklistReason.NoImportableFiles` is declared but **never raised**. `parity/prismedia-download-import.md` §4.6; `live-instances.md` §2.11/§5.4. | **P1 (live-verified real scenario)** |
| Import extra files (subs/nfo) | `importExtraFiles`, `extraFileExtensions` (srt,nfo). | **MISSING** — engines place only the primary payload (+ cover art for music); no sidecar-file import. | P2 |
| Recycle bin on delete/replace | `recycleBin` + `recycleBinCleanupDays` (Sonarr on, Radarr/Lidarr off — per-kind opt-in). | **MISSING (deliberate divergence)** — repo is hard-delete-only; upgrade-replace keeps a `.prismedia-bak` sidecar but no recycle bin. `parity/prismedia-download-import.md` §4.4. | P3 |
| Manual import (map loose files) | Interactive: browse folder, map files to episodes, pick quality per file. | **MISSING** — only per-acquisition `.torrent` upload; no "point at on-disk files and tell Prismedia what they are" UI. `prismedia-frontend.md` §8. | P2 |
| Cross-format upgrade replacement | N/A (Sonarr replaces any quality in-place). | **PARTIAL/refused** — `OwnedFileReplacer` refuses a format/extension change (pdf→epub) to protect the entity/progress row; surfaced only as a job failure, no manual-replace UI. `parity/prismedia-acquisition-backend.md` §8. | P3 |

### 1.5 Monitoring, wanted & scheduled tasks

| Capability | Sonarr behavior | Prismedia status | Sev |
|---|---|---|---|
| Monitored flags (series/season/episode) | Three independent flags rolling up; `MonitorTypes` dropdown (All/Future/Missing/Existing/Pilot/FirstSeason/LastSeason/…); Season Pass bulk editor. | **PARTIAL** — container monitor (author/artist/series) + per-leaf wanted; no All/Future/Pilot/FirstSeason monitor-type presets, no Season Pass bulk monitor editor. `EfMonitorStore`, `useEntityMonitorAction`. | P1 |
| Container "watch for new works" | `MonitorNewItems` + refresh discovers new seasons/episodes and auto-monitors. | **DONE** — container monitors re-resolve provider and materialize missing works as wanted phantoms via `SyncContainerAsync` (daily); manual "Check for new works". `parity/prismedia-acquisition-backend.md` §7.2; `prismedia-frontend.md` §7.2. | P1 |
| "Keep searching until acquired" loop | RSS sync + event-driven targeted searches + optional user-added recurring Missing/Cutoff search tasks. | **DONE** — `MonitoredSearchJobHandler` sweep over due monitors; re-search only genuinely-missing (`Failed`/`AwaitingSelection`-zero); exponential backoff on barren searches. `parity/prismedia-download-import.md` §9. | P0 |
| Wanted: Missing view | Query of aired episodes with no file; `/wanted/missing` — **4,945 tracked on the live box**; `lastSearchTime` throttle. | **PARTIAL** — wanted entities appear in the normal library grid (real entities, `IsWanted=true`), and in `/request` Requests tab grouped by status. **No dedicated Missing list**, no `lastSearchTime` per-item column. `prismedia-frontend.md` §3, §8. | P1 |
| Wanted: Cutoff Unmet view | Episodes with a file below profile cutoff; dedicated list. | **MISSING** — book upgrade loop is the only "below cutoff" concept and it has no list view; no cutoff-unmet surface for any kind. `prismedia-frontend.md` §8. | P2 |
| RSS sync (organic discovery) | `RssSyncCommand` every `RssSyncInterval` (15 min live) pulls new releases as indexers post them — the main automatic acquisition path. | **MISSING** — Prismedia only *searches* on interval per monitor; no RSS feed consumption. Newly-posted releases are found only when the next monitored search runs. `sonarr-monitoring-tasks.md` §4.1, §6. | P2 |
| Calendar (upcoming air dates) | Air-date calendar view. | **MISSING** — no calendar surface. `prismedia-frontend.md` §8. | P2 |
| Scheduled task system + command queue | `TaskManager` fixed table + user `ScheduledTask`s; 3-worker command executor; structural dedup; disk/exclusive/long-running interleaving; orphan recovery. | **PARTIAL** — generic `JobScheduler` (60s tick) + `JobType` handlers with `EnqueueIfNeededAsync` dedup; no priority/exclusive/disk-contention interleaving, no user-schedulable arbitrary commands. `JobScheduler.cs`. | P2 |
| Configurable intervals | RSS interval, backup interval, per-app poll cadence all user-set. | **PARTIAL** — only `monitoring.intervalMinutes` (default 1440) + `searchEnabled`. Download poll (60s), stall/removal grace (5/60 min), upgrade caps (3/6/24h) are **hardcoded constants**. `parity/prismedia-download-import.md` §10. | P2 |
| History / activity log | Grabbed/imported/failed/deleted events with timestamps, durable. | **MISSING** — no acquisition history surface; deleting an acquisition erases its record. Jobs page is a worker-run log, not a grab/import log. `prismedia-frontend.md` §8. | P1 |
| Import Lists (Trakt/TMDB list auto-add) | List-driven auto-add with per-list monitor + search flags. (Empty on live box.) | **MISSING** — no import-list concept. `sonarr-monitoring-tasks.md`; `live-instances.md` §2.10. | P3 |
| Tags / auto-tagging | Tag rules gate delay/release profiles, indexer applicability, download-client routing. | **MISSING** in the acquisition layer — Prismedia has library tags but they don't scope indexers/profiles/clients. | P3 |
| Health checks catalog | Broad scheduled + event health-check catalog. | **PARTIAL** — connectivity `TestAsync` per indexer/client on demand; no standing health-check catalog. | P3 |
| Housekeeping / orphan cleanup | 31 housekeepers + DB vacuum nightly. | **PARTIAL** — FK cascades + monitor auto-pause on orphan; no equivalent broad housekeeping sweep. | P3 |

### 1.6 Metadata providers (plugin layer — the "generalized across kinds" story)

| Capability | Sonarr/Radarr/Lidarr | Prismedia status | Sev |
|---|---|---|---|
| Metadata identify per kind | TheTVDB/TMDB/MusicBrainz agents. | **DONE (plugin SDK)** — real `dotnet-process` plugin runtime, manifest + semver compat gates + community index + install flow. TMDB (movies/TV/people/studios), AniList (anime), MusicBrainz + YouTube (music), Open Library (books), MangaDex (manga), Stash-compat scrapers. `prismedia-plugins-indexers.md` §1–2. | P0 |
| Metadata fallback per kind | Occasionally layered fallbacks. | **PARTIAL** — single provider for movies (TMDB), prose books (Open Library), manga (MangaDex); no automatic failover. `prismedia-plugins-indexers.md` §2.7. | P2 |
| Lidarr metadata profiles (album-type/release-status taxonomy) | Second profile axis: which MusicBrainz release-group types to track. | **MISSING** — no secondary taxonomy profile for music (or book editions). `live-instances.md` §4.2. | P2 |
| Indexer *plugin* SDK | N/A (Sonarr indexers are built-in C#; Prowlarr Cardigann is the extensibility). | **MISSING** — indexer side has no plugin SDK; adding an indexer family is a C# change + deploy. `prismedia-plugins-indexers.md` §6 gap #4. | P2 |

---

## 2. What the live install actually uses (ground truth → severity bumps)

From `live-instances.md` (production stack at `10.10.10.100`, captured 2026-07-03). These are not
hypothetical Sonarr features — they are in active use, so their gaps are bumped up.

1. **Usenet is in real use.** Prowlarr has 2 Newznab indexers (DrunkenSlug priority 20, AnimeTosho),
   SABnzbd is a configured download client on all four apps, and a live per-episode search returned
   **16 of 175 results over usenet** (the *only* result for a thin-tail kids episode was a usenet hit on
   DrunkenSlug). Prismedia rejects usenet outright. **→ bumped to P0.** Any drop-in for this exact user
   loses content that is only available via usenet.
2. **A dangerous-file import hold is actively blocking an import right now.** `Rick.and.Morty.S09E07…​.scr`
   sits in `importPending`/`warning` state on the live Sonarr. Prismedia would have imported the `.scr`
   or left it stuck with no warning. **→ bumped to P1 (live-verified, not theoretical).**
3. **Seed ratio/time is configured per indexer** (`seedRatio`, `seedTime`, separate `packSeedTime`) and
   qBittorrent categories segregate per-app. Prismedia deletes downloaded data immediately on move-import.
   **→ P1 holds; ratio-hostile behavior would harm this user on private trackers.**
4. **Hardlink import is on** (`copyUsingHardlinks:true`). Prismedia has no hardlink. **→ P1 holds.**
5. **`downloadPropersAndRepacks: preferAndUpgrade`** is set on Sonarr. Prismedia has no proper/repack
   awareness. **→ P2 holds (real, but a WeightedTerm workaround exists).**
6. **`upgradeAllowed:false` on 5 of 6 Sonarr profiles** — this user pins quality tiers and does NOT chase
   upgrades. Prismedia's fixed heuristic ladder can't express "allow exactly these qualities, never
   upgrade" for video/music. **→ P1 holds.**
7. **Custom Format scoring is live and load-bearing** — the `DUAL`-matching format scored a real release
   +500 and made it outrank an otherwise-identical non-dual release in the captured search. Prismedia's
   WeightedTerms cover the regex-on-title case but not the min/cutoff/upgrade-score thresholds. **→ P1 holds.**
8. **32 indexers via Prowlarr, 27 of them Cardigann-defined.** Confirms Prismedia's Prowlarr dependency is
   load-bearing and not removable without a Cardigann engine. **→ P1 holds (the Prowlarr-still-required gap
   is real for this user).**
9. **Backlog scale: 4,945 wanted/missing episodes on Sonarr.** A Missing-list view with per-item
   `lastSearchTime` throttling matters at this scale. **→ Missing-list bumped toward P1.**
10. **Lidarr metadata profiles are configured** (Standard vs None). The music secondary-taxonomy axis is in
    real use. **→ P2 holds.**
11. **Not in use on the live box (keeps their severity lower):** Import Lists (empty), Release Profiles
    (empty), Sonarr Custom Formats beyond the one DUAL/eng format, Calendar (not inspected as used),
    tags on most indexers. These stay P2/P3.

---

## 3. Prioritized build plan to reach drop-in parity

Ordered by impact for *this* user's drop-in goal. Scope: **S** = days, **M** = 1–2 weeks, **L** = multi-week.
Each item names the existing Prismedia code it extends.

### P0 — core loop gaps for the live install

1. **Usenet protocol + SABnzbd download client.** **[L]**
   Add `DownloadClientKind.Sabnzbd` + `SabnzbdDownloadClient : IDownloadClient` (queue/history API differs
   from qBittorrent); conditionalize/remove `ProtocolSpecification`'s hard usenet rejection; add NZB
   transfer polling and NZB-payload import (no per-piece map). Extends `DownloadClientPort.cs`,
   `QBittorrentDownloadClient.cs` (as sibling), `MediaReleaseDecisionEngines.cs`/`BookReleaseDecisionEngine.cs`
   (`ProtocolSpecification`), `AcquisitionMonitorJobHandler`. Prowlarr already normalizes usenet indexers, so
   search-side needs only the protocol gate lifted.

### P1 — expected by any Sonarr user; several live-verified

2. **Dangerous-extension import hold.** **[S]**
   Add an executable/dangerous-extension denylist that puts the acquisition into `ManualImportRequired`
   (warning, not silent) and *raise the already-declared* `BlocklistReason.NoImportableFiles`. Extends
   `ImportPlanBuilder.cs` / `AcquisitionImportJobHandler`; wire a review-UI warning in `AcquisitionPanel.svelte`.
3. **Seed lifecycle: ratio/time + defer delete.** **[M]**
   Add per-profile (or per-client) seed-ratio/seed-time; stop passing `deleteData:true` on move-import until
   the seed goal is met; add a seeding-satisfaction poll. Extends `ImportFileMover`/`ImportedTorrentRemover`,
   `AcquisitionMonitorJobHandler`, profile schema (`BookAcquisitionProfileRow`), `QBittorrentDownloadClient`
   (read ratio/state).
4. **Hardlink import mode.** **[S]** (pairs with #3)
   Add hardlink as an `ImportMode` (or auto-prefer within-filesystem hardlink) so the file lives in the
   library while still seeding. Extends `ImportFileMover.PlaceAsync`, `ImportMode` enum, profile UI.
5. **Owned-quality + upgrade-until-cutoff for video/music.** **[L]**
   Generalize the book source×format `BookQualityRank`/`OwnedFileReplacer`/upgrade-loop to a per-kind owned
   quality (resolution/source for video, codec for music), a user-orderable allowed-quality list with cutoff,
   and a `upgradeAllowed:false` "pin this tier" mode. Extends `AcquisitionRow` owned-quality columns,
   `MediaReleaseDecisionEngines`, `EfMonitorStore` upgrade sweep, `AcquisitionUpgradeReplaceJobHandler`,
   profile schema + `AcquisitionSection.svelte`.
6. **Configurable naming templates for movie/TV/music.** **[M]**
   Promote the book `PathTemplate` mechanism to all kinds with a per-shape token dictionary (standard/daily
   equivalents), colon-replacement and multi-episode-style options. Extends `PathTemplate` rendering in the
   engines, profile schema, `AcquisitionSection.svelte` (currently shows fixed copy for non-book kinds).
7. **Proper/Repack detection + `DownloadPropersAndRepacks` setting.** **[M]**
   Add revision (PROPER/REPACK/REAL) parsing to the title-token layer and a tri-state prefer/upgrade/ignore
   setting feeding scoring + a same-quality-revision upgrade path. Extends `TvReleaseTokens`/detection helpers,
   `MediaReleaseEvaluation` scoring, upgrade specs.
8. **Custom-Format-style scored classifier with min/cutoff/upgrade thresholds.** **[M]**
   Extend WeightedTerms into reusable named formats with more condition types (source/resolution/indexer-flag)
   and add per-profile `MinFormatScore`/`CutoffFormatScore`/`MinUpgradeFormatScore`. Extends profile schema,
   `PreferenceScore`/`BookReleaseScore`/`VideoReleaseScore`.
9. **Remote path mapping.** **[S]**
   Add a `{Host, RemotePath, LocalPath}` prefix-rewrite table applied to the client's reported `content_path`
   before import. Extends `DownloadPayloadReader`/`AcquisitionImportPlanner`, a new settings table + UI.
10. **Acquisition history / activity log.** **[M]**
    Persist a durable append-only grab/import/failure log (survives acquisition delete) + a History surface.
    Extends a new history row + store; surface in `/request` and per-entity `AcquisitionPanel`.
11. **Dedicated Wanted (Missing) list with per-item `lastSearchTime`.** **[M]** (matters at 4,945-item scale)
    Add a Missing/Cutoff-Unmet list view driven off wanted entities + monitor state with a last-searched
    throttle column. Extends `EfMonitorStore` (expose last-searched), a new frontend list route.
12. **Second download client (Transmission) + basic client selection.** **[M]**
    Implement the declared `Transmission` enum; add protocol-filter + first-fallback client selection. Extends
    `DownloadClientFactory`, new `TransmissionDownloadClient`.
13. **Monitor-type presets + Season Pass bulk editor.** **[M]**
    Add All/Future/Missing/Pilot/FirstSeason/LastSeason presets and a bulk per-season monitor editor. Extends
    `MonitorService`/`EfMonitorStore`, series detail frontend.

### P2 — should-have

14. **Indexer priority (actually consume it) + health/backoff.** **[M]** — `AcquisitionSearchRunner` (ignore→
    honor `Priority`), new indexer-status/backoff store. (Bug-fix + feature: `Priority` already exists unused.)
15. **RSS sync path.** **[L]** — consume Prowlarr's RSS/newest feed on interval to catch new releases
    organically instead of only interval-searching each monitor. New job + `ProwlarrIndexerClient` RSS call.
16. **Per-quality size gates (MB/min).** **[S]** — add a per-quality-tier size sanity gate. Extends
    `SizeSpecification` + quality-definition data.
17. **Extra-file import (subs/nfo).** **[S]** — sidecar-file placement. Extends the import engines.
18. **Manual import UI.** **[L]** — browse on-disk files, map to entities, choose quality. New frontend + API.
19. **Calendar view.** **[M]** — upcoming air dates from entity metadata. New frontend surface.
20. **Music metadata (secondary taxonomy) profile.** **[M]** — Lidarr-style album-type/release-status filter.
21. **Category-narrowing `8000` bug fix.** **[S]** — `TorznabCategories.ForKind` drops `8000` from Book
    searches; fix the range check. `MediaReleaseDecisionEngines.cs`.
22. **Per-kind download-client category.** **[S]** — let profiles pick a client category per kind.
23. **Tag-scoped release/delay profiles.** **[M]** — scope required/ignored terms + delay by tag/indexer.
24. **Delay profiles.** **[M]** — protocol-preference wait window with bypass escapes.
25. **Recycle bin (opt-in).** **[S]** — but note this contradicts the repo's deliberate hard-delete policy;
    surface as a decision, not a silent copy.

### P3 — niche / deferrable

26. Jackett client · 27. Anime series type + absolute numbering + scene mapping · 28. Import Lists ·
    29. Auto-tagging in the acquisition layer · 30. Post-import client relabel · 31. Indexer-plugin SDK ·
    32. Standing health-check catalog · 33. Native Cardigann engine (would let Prismedia drop the Prowlarr
    dependency entirely — very large, security-sensitive; explicitly out of current scope).

---

## 4. Already at or beyond parity

Where Prismedia matches or exceeds Sonarr/Prowlarr today:

- **Generalized-across-kinds acquisition from day one.** One `RequestKindRegistry` + per-kind engines drive
  books, movies, series/season/episode, and music through the same search→grab→import→monitor loop. Sonarr
  is TV-only; matching this needs the whole Radarr+Lidarr+Readarr set. `parity/prismedia-acquisition-backend.md`
  §3.2, §12.
- **Wanted items are real library entities, not shadow rows.** A wanted book/movie/episode appears in the
  normal library grid with real metadata/artwork immediately, hidden from the Jellyfin projection until it
  has a file. Sonarr's "monitored but missing" is a separate list, not a first-class library object.
  `parity/prismedia-acquisition-backend.md` §3.1.
- **Container→child phantom materialization.** Requesting a series/author/artist fans out into
  season/book/album phantoms with metadata but no download until each is explicitly acquired — a cleaner
  model than Sonarr's flat episode monitoring. `parity/prismedia-acquisition-backend.md` §3.3.
- **Genuine metadata plugin SDK with a community registry.** Manifest schema, semver compat gates, install
  flow, credential handling, cascade proposals, NSFW propagation — Sonarr's metadata agents are built-in and
  not community-extensible in this way. `prismedia-plugins-indexers.md` §1–2.
- **Per-piece torrent progress visualization** (`PieceStateBar`) inline on the entity/acquisition page —
  Sonarr has no piece map (it doesn't talk to the client that intimately). `prismedia-frontend.md` §6.1.
- **Interactive release picker lives on the entity's own page**, not a separate screen — a wanted entity
  manages its own download from where it lives. `prismedia-frontend.md` §6.
- **Crash-safe in-place upgrade swap** (`OwnedFileReplacer`: stage→`.prismedia-bak`→atomic same-dir rename,
  backup always kept) — more defensive than a plain replace. `parity/prismedia-acquisition-backend.md` §8.
- **Defense-in-depth blocklist enforcement** at three points (search, queue, auto-redownload) with a
  content-addressed `hash:` identity that survives title/indexer reformatting — at least as robust as
  Sonarr's, and applied consistently across all kinds. `parity/prismedia-download-import.md` §4.5.
- **Conservative-replace upgrade safety** — a release-title "retail" token alone never overwrites a file
  (attacker-influenceable input); only the verified on-disk file extension gates the swap. A safety property
  Sonarr's title-trusting flow doesn't have. `parity/prismedia-acquisition-backend.md` §8.
- **Careful stall heuristic** — "moved since last poll" tolerance means slow-but-moving is never conflated
  with dead, materially more careful than a naive stalled-state=fail rule. `parity/prismedia-download-import.md`
  §4.1.

---

## 5. One-line verdict

Prismedia **already replaces Sonarr's orchestration role for the torrent path across four media kinds**, with
a cleaner wanted-entity model and a real plugin SDK. To be a true drop-in for *this* live install it must
close, in order: **usenet/SABnzbd (P0, live box depends on it)**, **dangerous-file hold + seeding lifecycle +
hardlink + video/music upgrade-cutoff + configurable naming + proper/repack + scored formats + remote path
mapping + history + a Missing list (P1)** — and it will still **require an external Prowlarr** for indexer
aggregation until a native Cardigann engine (P3) is built, so it is a Sonarr replacement, not yet a Prowlarr
replacement.
