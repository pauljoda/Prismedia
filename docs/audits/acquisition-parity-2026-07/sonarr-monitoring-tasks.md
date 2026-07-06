# Sonarr Parity Map — Monitoring, Wanted & Scheduled Task System

Source root: `/Users/pauldavis/Dev/_ARCHIVE/Sonarr/src` (NzbDrone.Core + Sonarr.Api.V5).
All file paths below are relative to that root unless stated otherwise.

This document maps, exhaustively, how Sonarr decides **what is monitored**, **what
is "wanted"**, **what runs on a schedule vs. on demand**, **how the command/queue
system serializes and dedupes work**, **how automatic search actually fires**,
**how blocklisting works and feeds back into search**, **the full health-check
catalog**, **import lists**, **calendar-driven search**, **tags/auto-tagging**,
and **release profiles**. It is the reference for building Prismedia's
Sonarr-equivalent monitoring/wanted/task vertical (generalized across kinds).

---

## 1. Monitoring semantics

### 1.1 Data model

- `NzbDrone.Core/Tv/Series.cs` — `Series.Monitored` (bool, whole-series switch),
  `Series.MonitorNewItems` (`NewItemMonitorTypes`: `All` | `None` — controls
  whether **newly discovered seasons** get auto-monitored on refresh),
  `Series.SeriesType` (`SeriesTypes`: `Standard` | `Daily` | `Anime`),
  `Series.AddOptions` (an `AddSeriesOptions`, non-null only right after add /
  import, see §1.4).
- `NzbDrone.Core/Tv/Season.cs` — `Season.Monitored` (bool, per-season switch,
  embedded document on `Series.Seasons`).
- `NzbDrone.Core/Tv/Episode.cs` — `Episode.Monitored` (bool, per-episode switch).
  `Episode.HasFile => EpisodeFileId > 0`.
- Three independent monitored flags exist simultaneously: series, season,
  episode. **All three roll up multiplicatively for "wanted" purposes** — an
  episode is only actionable if episode.Monitored AND its season is
  practically consistent AND series.Monitored (the season flag is a UI/rollup
  convenience; the actual gating query, see §3, checks `Episode.Monitored` and
  `Series.Monitored` only — season monitored is not directly queried, it is kept
  in sync with episode monitored by `EpisodeMonitoredService`).

### 1.2 `MonitoringOptions` / `MonitorTypes` (the "monitor" dropdown)

File: `NzbDrone.Core/Tv/MonitoringOptions.cs`

```csharp
public class MonitoringOptions : IEmbeddedDocument
{
    public bool IgnoreEpisodesWithFiles { get; set; }     // legacy v2 fields
    public bool IgnoreEpisodesWithoutFiles { get; set; }  // legacy v2 fields
    public MonitorTypes Monitor { get; set; }
}

public enum MonitorTypes
{
    Unknown, All, Future, Missing, Existing, FirstSeason, LastSeason,
    [Obsolete] LatestSeason, Pilot, Recent, MonitorSpecials, UnmonitorSpecials,
    None, Skip
}

public enum NewItemMonitorTypes { All, None }
```

`AddSeriesOptions : MonitoringOptions` (file `NzbDrone.Core/Tv/AddSeriesOptions.cs`)
adds two more fields used only at add-time:
- `SearchForMissingEpisodes` (bool) — kick a missing-episode search immediately
  after add.
- `SearchForCutoffUnmetEpisodes` (bool) — kick a cutoff-unmet search immediately
  after add.

`Series.AddOptions` holds one of these; it is **not persisted long-term** — the
season/episode monitored flags it implies are applied once at add/import time,
then normal per-season/per-episode toggling takes over (see §1.5).

### 1.3 What each `MonitorTypes` value does

Applied in `NzbDrone.Core/Tv/EpisodeMonitoredService.cs`, method
`SetEpisodeMonitoredStatus(Series, MonitoringOptions)`. This is the single
authoritative place that turns the enum choice into `Episode.Monitored` /
`Season.Monitored` bits. Semantics (episode predicate first, `e.SeasonNumber > 0`
excludes specials unless noted):

| MonitorTypes | Episode predicate for "monitored = true" |
|---|---|
| `All` | every episode with `SeasonNumber > 0` |
| `Future` | episodes with no air date yet, or air date `>= now` |
| `Missing` | episodes without a file (`!HasFile`) |
| `Existing` | episodes that already have a file (`HasFile`) |
| `Pilot` | only `SeasonNumber == firstSeason && EpisodeNumber == 1` |
| `FirstSeason` | all episodes in the lowest non-zero season number |
| `LastSeason` (and obsolete `LatestSeason`, same code path) | all episodes in the highest season number |
| `Recent` | episodes with no air date, OR aired in the last 90 days, OR airing in the future |
| `MonitorSpecials` | sets monitored=true only for season-0 (specials) episodes; does not touch other seasons |
| `UnmonitorSpecials` | sets monitored=false only for season-0 episodes |
| `None` | unmonitors every episode |
| `Skip` | no-op — leaves episode monitored flags untouched entirely (used when season-level monitoring was already set explicitly by the caller, e.g. import lists that pass per-season data) |
| `Unknown` | falls back to `LegacySetEpisodeMonitoredStatus` (the old v2 `IgnoreEpisodesWithFiles`/`IgnoreEpisodesWithoutFiles` boolean pair) — kept for API v2 backward compatibility only |

After the per-episode pass, season monitored flags are derived (same method,
lines ~132-169):
- The **latest season** is monitored when: (a) `Monitor == All`, or
  (b) `Monitor == Future` **and** series status is `Continuing` or `Upcoming`
  (i.e. more episodes are expected).
- The **first season** is explicitly **unmonitored** when `Monitor == Pilot`
  (so only the pilot episode, not the whole season, shows as monitored).
- Any other season is monitored if it has **at least one** monitored episode
  in it, else unmonitored.
- Season 0 (specials) is included in this derivation only via the two special
  monitor types; it is otherwise driven purely by whether any of its episodes
  ended up monitored by the chosen mode.

Config surface: this dropdown is exposed at series-add time and via the
**Season Pass** bulk editor (`Sonarr.Api.V5/SeasonPass/SeasonPassController.cs`),
which lets a user set `series.Monitored`, arbitrary per-season monitored flags,
and/or re-run `MonitoringOptions` against the whole selection in one POST.

### 1.4 Auto-monitoring of newly discovered episodes/seasons

Two distinct places create "new" items and decide their default monitored
state:

1. **New season appears on refresh** — `RefreshSeriesService.UpdateSeasons`
   (`NzbDrone.Core/Tv/RefreshSeriesService.cs:137-166`): for any season number
   returned by the metadata provider that doesn't already exist locally,
     - season 0 (specials) is always added **unmonitored** regardless of
       series settings ("Ignoring season 0 ... by default"),
     - any other new season number is monitored according to
       `Series.MonitorNewItems == NewItemMonitorTypes.All` (else unmonitored).
   Existing seasons keep whatever monitored value they already had.

2. **New episode appears on refresh** — `RefreshEpisodeService.GetMonitoredStatus`
   (`NzbDrone.Core/Tv/RefreshEpisodeService.cs:146-155`): a brand-new episode
   row's monitored flag is:
     - `false` if it's episode 0 of a season other than season 1 (odd/junk
       numbering guard),
     - else it inherits its season's `Monitored` flag (or `true` if the season
       doesn't exist locally yet at all).

3. **Guard against "stale surprise" episodes** —
   `RefreshEpisodeService.UnmonitorReaddedEpisodes` (lines 157-189): if the
   series has no `AddOptions` set (i.e. this is a normal ongoing refresh, not
   a fresh add) and new episodes appear that supposedly aired **more than 14
   days ago**:
     - if the series already had episodes on disk, it just warns (`Show X had
       N old episodes appear, please check monitored status`) and leaves them
       as computed;
     - if the series had **no existing episodes at all** (brand-new metadata
       fetch without an explicit AddOptions, e.g. an edge-case refresh path),
       any of those new episodes older than **1 day** get forcibly
       **unmonitored** to avoid an unexpected mass-download, with a warning
       logged.

4. **Automatic re-search when new/updated episodes recently aired** —
   `EpisodeRefreshedService` (`NzbDrone.Core/Tv/EpisodeRefreshedService.cs`),
   an `IHandle<EpisodeInfoRefreshedEvent>`:
   - Only acts when `series.AddOptions == null` (i.e. not the initial add pass,
     which has its own explicit search flags).
   - Skips entirely if `!series.Monitored`.
   - Collects: (a) newly-added episodes whose air date falls in the window
     `[now-14d, now+1d]` and are monitored, and (b) *updated* episodes that
     just had an absolute-episode-number added (anime numbering
     backfill) whose air date is in the same window and are monitored.
   - Caches that id list per series (`ICached<List<int>>`, keyed by series id)
     rather than searching immediately.
   - `Search(Series)` is invoked by the refresh command's caller after the
     full series refresh finishes; it re-reads the cached ids, filters to
     those that are still missing a file, and pushes a single
     `EpisodeSearchCommand` for them. This is the mechanism that makes "a show
     just aired an episode and Sonarr refreshed metadata" turn into an
     automatic targeted search without waiting for the next full missing-scan.

### 1.5 Add-series flow tying it together

`NzbDrone.Core/Tv/AddSeriesService.cs`:
- `AddSeries(Series newSeries)` fetches full metadata + season/episode list
  from Skyhook (`IProvideSeriesInfo.GetSeriesInfo`), merges caller-supplied
  seasons (if any) over the fetched ones, sets path/clean-title/sort-title,
  and — critically — `SetPropertiesAndValidate` forces `newSeries.Monitored =
  false` whenever `AddOptions.Monitor == MonitorTypes.None`, regardless of
  what boolean the caller passed for the series-level monitored flag.
- Bulk add (`AddSeries(List<Series>, ignoreErrors)`, used by import lists) dedupes
  against existing TVDB ids, in-batch duplicate TVDB ids, and duplicate title
  slugs within the same batch before insert, logging and skipping any
  conflicting entries (with `ignoreErrors=true` this converts validation
  exceptions into skips instead of failing the whole batch).
- `AddSeriesValidator` (`NzbDrone.Core/Tv/AddSeriesValidator.cs`) chains path
  validity, root-folder validity, "not nested under another series' folder"
  (`SeriesAncestorValidator`), and **title-slug uniqueness**
  (`SeriesTitleSlugValidator.cs` — a series cannot share a slug used by another
  already-added series; this is the actual duplicate-add guard surfaced to the
  user, distinct from the TVDB-id dedupe used for bulk import-list adds).

---

## 2. Series types and how they change search/parse behavior

File: `NzbDrone.Core/Tv/SeriesTypes.cs` — `Standard = 0`, `Daily = 1`, `Anime = 2`.

`SeriesType` changes behavior in at least these places:

- **Episode numbering ingestion** (`RefreshEpisodeService.cs`):
  - Anime: episodes are de-duplicated and mapped by **absolute episode number**
    (`MapAbsoluteEpisodeNumbers`) — episodes with an absolute number take
    priority and dupes on that number are collapsed; episodes are ordered by
    absolute number first, then by season/episode for anything left over
    (`OrderEpisodes`).
  - Daily/Standard: plain distinct-by-(season,episode) and season/episode
    ordering.
- **Search dispatch** (`ReleaseSearchService.cs`, the single-episode and
  season-search entry points):
  - `Daily`: requires `Episode.AirDate` to be present (throws
    `SearchFailedException("Air date is missing")` otherwise) and searches via
    `DailyEpisodeSearchCriteria` keyed on the literal air date, not
    season/episode numbers. `SearchDailySeason` groups a season's daily
    episodes by **calendar year** and issues one `DailySeasonSearchCriteria`
    query per year with >1 episode, else falls back to per-episode daily
    search.
  - `Anime`: searches via `AnimeEpisodeSearchCriteria`/`AnimeSeasonSearchCriteria`
    using scene-mapped absolute episode numbers
    (`SceneAbsoluteEpisodeNumber ?? AbsoluteEpisodeNumber`) instead of
    season/episode pairs; season-search additionally issues **both** a
    season-level anime query per season **and** a per-episode anime query for
    every episode in that season (`SearchAnimeSeason`), since anime releases
    are commonly indexed as whole-season batches, per-episode individual
    releases, or (via `SearchSpecial`) as season-0 specials outside normal
    numbering.
  - `Standard`: season==0 episodes always route to `SearchSpecial` (title-based
    query built from series title + episode title, plus a season/episode
    fallback search per special episode); everything else routes through the
    generic scene-mapping path (`SearchSingle`/`SeasonSearch`) using
    season/episode numbers, optionally remapped through scene numbering.
- **Search criteria definitions** enumerated in
  `NzbDrone.Core/IndexerSearch/Definitions/` (10 files):
  `SearchCriteriaBase.cs` (shared base: series, scene titles, episodes,
  `SearchMode`, `MonitoredEpisodesOnly`, `UserInvokedSearch`,
  `InteractiveSearch`), `SingleEpisodeSearchCriteria.cs`,
  `SeasonSearchCriteria.cs`, `SpecialEpisodeSearchCriteria.cs`,
  `DailyEpisodeSearchCriteria.cs`, `DailySeasonSearchCriteria.cs`,
  `AnimeEpisodeSearchCriteria.cs`, `AnimeSeasonSearchCriteria.cs`, plus scene
  mapping DTOs `SceneEpisodeMapping.cs` and `SceneSeasonMapping.cs`.
- **Indexer-side title building** (outside this vertical, in `Parser`/`Indexers`)
  also branches on `SeriesType` when constructing query strings, but the
  dispatch/criteria layer above is the authoritative decision point for this
  report's scope.
- **Auto-tagging**: `SeriesTypeSpecification`
  (`NzbDrone.Core/AutoTagging/Specifications/SeriesTypeSpecification.cs`) lets
  a tag rule match on `(int)series.SeriesType == Value`.

---

## 3. Wanted: Missing vs Cutoff Unmet

### 3.1 Missing

**Definition**: an episode with `EpisodeFileId == 0` (`!HasFile`) whose air
date (adjusted for the series' runtime) has already passed.

- Repository query: `EpisodeRepository.EpisodesWithoutFiles`
  (`NzbDrone.Core/Tv/EpisodeRepository.cs:110-124`, builder at
  `EpisodesWithoutFilesBuilder` lines 214-228):
  ```
  Episodes JOIN Series
  WHERE Episodes.EpisodeFileId = 0
    AND Episodes.SeasonNumber >= startingSeasonNumber   -- 0 if includeSpecials else 1
    AND (AirDateUtc + Series.Runtime minutes) <= now     -- BuildAirDateUtcCutoffWhereClause
    [AND Series.Tags ⊇ one of seriesTags]                -- BuildSeriesTagsWhereClause, optional
  ```
  The "+ runtime" adjustment means an episode isn't "missing" until its
  scheduled **end** time has passed, not just its start time — avoids treating
  an episode that's still airing as already missing.
- The repository method itself does **not** filter on `Monitored` at all — that
  filter is layered on by callers via `PagingSpec.FilterExpressions`:
  - API: `Sonarr.Api.V5/Wanted/MissingController.cs` — query param
    `monitored` (default `true`). When true:
    `Episode.Monitored == true && Series.Monitored == true`. When false:
    `Episode.Monitored == false || Series.Monitored == false` (i.e. explicitly
    the *unmonitored* view, not "don't care"). Additional optional filters:
    `seriesIds`, `qualityProfileIds`, `seriesType` (list of `SeriesTypes`),
    `seriesTags` (passed through to the SQL builder), `includeSpecials`
    (default true), and `includeSubresources` (`Series`, `Images`) to control
    payload shape. Default sort: `episodes.airDateUtc` ascending.
  - Same filter logic is duplicated (by design — command vs. HTTP surface) in
    `EpisodeSearchService.Execute(MissingEpisodeSearchCommand)`.

### 3.2 Cutoff Unmet

**Definition**: an episode that **has** a file, but that file's quality is
below the quality-profile's cutoff (or, if the profile disallows upgrades, below
the first allowed quality).

- Service: `EpisodeCutoffService.EpisodesWhereCutoffUnmet`
  (`NzbDrone.Core/Tv/EpisodeCutoffService.cs`):
  1. For every quality profile, compute the cutoff quality id: if
     `profile.UpgradeAllowed`, that's `profile.Cutoff`; otherwise it's the
     profile's `FirststAllowedQuality()` (i.e. upgrades disabled ⇒ the "cutoff"
     collapses to "the worst allowed quality", so literally nothing above the
     minimum can be considered unmet).
  2. Take every quality **below** that cutoff's index in the profile's ordered
     item list — these are the "qualities that still count as needing an
     upgrade" per profile (`QualitiesBelowCutoff`, one row per profile+quality
     id).
  3. If no profile has any qualities below its cutoff (e.g. everyone's cutoff
     is the lowest quality), short-circuits to an empty page with no query at
     all.
  4. Otherwise delegates to `EpisodeRepository.EpisodesWhereCutoffUnmet`.
- Repository builder (`EpisodesWhereCutoffUnmetBuilder`, lines 242-266):
  ```
  Episodes JOIN Series LEFT JOIN EpisodeFiles
  WHERE Episodes.EpisodeFileId != 0
    AND Episodes.SeasonNumber >= startingSeasonNumber
    AND ( OR over all (profileId, qualityId) pairs:
          Series.QualityProfileId = profileId
          AND EpisodeFiles.Quality LIKE '%_quality_: qualityId,%' )   -- serialized quality blob match
    [AND seriesTags filter]
    [AND quality IN (...) filter]
  GROUP BY Episodes.Id, Series.Id
  ```
  Same `Monitored` semantics as Missing are applied by the caller
  (`Sonarr.Api.V5/Wanted/CutoffController.cs`), plus an additional `quality`
  query param (list of quality ids) to further restrict which "below cutoff"
  qualities are being asked about. Additional optional
  `includeSubresources`: `Series`, `EpisodeFile`, `Images`. Default sort:
  `episodes.airDateUtc` ascending.
- `HasSize`/tags filtering (`BuildSeriesTagsWhereClause`) is DB-dialect-aware
  (`jsonb_array_elements_text` on Postgres vs `json_each` on SQLite) since
  `Series.Tags` is stored as a JSON array column, not a join table.

### 3.3 Key distinction from a UX standpoint

Missing = **no file at all** (and aired). Cutoff Unmet = **file exists but
under quality target**. Both views default to "monitored only" and both
exclude anything currently sitting in the download queue when driving an
actual automatic search (see §5), but that queue-exclusion is a
search-command-time filter, not baked into the Wanted list queries themselves
(the Wanted lists in the UI intentionally still show queued items so the user
can see progress).

---

## 4. Scheduled / background tasks

### 4.1 The `TaskManager` default task table

File: `NzbDrone.Core/Jobs/TaskManager.cs`, `Handle(ApplicationStartedEvent)`
(lines 65-166). This is the **complete, exhaustive list** of tasks Sonarr
registers on startup — there is no other place default intervals are declared.
Any task in the DB that isn't in this list gets deleted on startup (schema
migration safety net).

| Command type | Interval | Priority | Notes |
|---|---|---|---|
| `RefreshMonitoredDownloadsCommand` | 1 min | High | Polls download client(s) for queue/status changes |
| `MessagingCleanupCommand` | 5 min | Low (default) | Prunes old command/event bookkeeping |
| `ApplicationUpdateCheckCommand` | 6 hours (360 min) | Low | Checks for a new Sonarr build |
| `UpdateSceneMappingCommand` | 3 hours (180 min) | Low | Refreshes scene-naming alias mapping table |
| `CheckHealthCommand` | 6 hours (360 min) | Low | Runs the **scheduled-only** subset of health checks (see §7) |
| `RefreshSeriesCommand` | 12 hours (720 min) | Low | Full-library metadata refresh sweep (per-series gating via `ShouldRefreshSeries`, §4.4) |
| `HousekeepingCommand` | 24 hours (1440 min) | Low | Runs all registered housekeeping tasks (§4.5) then vacuums the DB |
| `CleanUpRecycleBinCommand` | 24 hours (1440 min) | Low | Empties/trims the configured recycle bin |
| `ImportListSyncCommand` | 5 min | Low | Pulls all import lists with automatic-add enabled |
| `BackupCommand` | `GetBackupInterval()` — user's `BackupInterval` (days, clamped 1–7) × 1440 | Low | Scheduled DB+config backup |
| `RssSyncCommand` | `GetRssSyncInterval()` — user's `RssSyncInterval` (minutes); values 1–9 are clamped up to 10, negative disables (interval 0 ⇒ never runs), 0 exactly also means never | Low | The RSS-driven automatic grab loop (§5.1) |

Notably **absent** from the fixed schedule: there is **no** standalone
"missing episode search" or "cutoff unmet search" scheduled task by default.
Automatic acquisition of missing/cutoff-unmet episodes happens primarily
through the **RSS sync loop** picking up newly-published matching releases
(§5.1), plus targeted searches fired by events (new episode aired, refresh
found new episodes, failed download redownload, import-list-driven
`SearchForMissingEpisodes`/`SearchForCutoffUnmetEpisodes` at add time). Users
who want a recurring brute-force missing/cutoff *search* (as opposed to
RSS-based discovery) add it themselves as a **custom scheduled task** via the
"Missing" / "Cutoff Unmet" entries in Settings → General → this is exposed as
a persisted `ScheduledTask` row the user can create through the same command
system (any `Command` type can be scheduled generically — the UI just offers
`MissingEpisodeSearchCommand` and `CutoffUnmetEpisodeSearchCommand` as
selectable options with a user-chosen interval). This is why `TaskManager`'s
hardcoded list doesn't include them: they are **optional, user-added**
recurring tasks layered on the same generic `ScheduledTask` mechanism, not
baked-in defaults.

### 4.2 `ScheduledTask` persistence model

File: `NzbDrone.Core/Jobs/ScheduledTask.cs`:
```csharp
public class ScheduledTask : ModelBase
{
    public string TypeName { get; set; }     // full CLR type name of the Command
    public int Interval { get; set; }        // minutes
    public DateTime LastExecution { get; set; }
    public CommandPriority Priority { get; set; }
    public DateTime LastStartTime { get; set; }
}
```
`ScheduledTaskRepository.cs` provides `GetDefinition(Type)` and
`SetLastExecutionTime(id, executionTime, startTime)`.

On `TaskManager` startup reconciliation: existing DB rows are matched to the
hardcoded table by `TypeName`; any row not in the hardcoded table is deleted;
each hardcoded entry's `Interval`/`Priority` **always overwrites** whatever was
in the DB (so a user cannot permanently override a built-in task's interval by
editing the DB row directly — RSS sync interval and backup interval are the
two that ARE user-configurable, but that happens by changing `ConfigService`
values which `GetRssSyncInterval()`/`GetBackupInterval()` read, not by editing
the scheduled task row itself). A `ConfigSavedEvent` handler
(`HandleAsync(ConfigSavedEvent)`) live-recomputes just the RSS and Backup
intervals and pushes them into both the DB and the in-memory cache
immediately, without waiting for the next app restart.

### 4.3 `Scheduler` — the polling loop

File: `NzbDrone.Core/Jobs/Scheduler.cs`. A single static `System.Timers.Timer`
firing every **30 seconds** (`Timer.Interval = 1000 * 30`). Each tick:
1. Calls `TaskManager.GetPending()` — every cached `ScheduledTask` where
   `Interval > 0 && LastExecution.AddMinutes(Interval) < UtcNow`.
2. For each due task, calls
   `CommandQueueManager.Push(task.TypeName, task.LastExecution, task.LastStartTime, task.Priority, CommandTrigger.Scheduled)` —
   i.e. it doesn't run the task inline, it **enqueues a Command** with
   `CommandTrigger.Scheduled`, which then flows through the normal
   dedup/priority queue (§5). This is why a scheduled task can be silently
   "swallowed" — if an equal command is already queued/running, `Push` returns
   the existing entry instead of creating a duplicate (§5.2).
3. Re-arms the timer in a `finally` unless a shutdown was requested.

`GetPending()` is a pure poll against an in-memory cache (`ICached<ScheduledTask>`
populated by `TaskManager`) — there's no persistent cron scheduler thread per
task, just this one shared ticking loop plus the interval math above.

### 4.4 Per-series refresh gating: `ShouldRefreshSeries`

File: `NzbDrone.Core/Tv/ShouldRefreshSeries.cs`. Even though
`RefreshSeriesCommand` runs on a fixed 12-hour schedule against **all** series,
each series individually decides whether it's actually worth refetching
(`RefreshSeriesService.Execute`, non-manual trigger path, calls
`ICheckIfSeriesShouldBeRefreshed.ShouldRefresh` per series):
1. `LastInfoSync` more than 30 days ago → **refresh** (safety net).
2. Any aired episode still titled the placeholder `"TBA"` → **refresh**
   (metadata provider likely has since filled it in).
3. `LastInfoSync` within the last 6 hours → **skip** (rate limit floor).
4. Series status isn't `Ended` → **refresh** (still airing, always worth it).
5. Ended, but the last known episode aired within the last 30 days → **refresh**
   (recently ended, metadata might still be settling — finale type, etc.).
6. Otherwise (ended long ago, nothing to gain) → **skip**.
7. Any exception during evaluation → default to **refresh** (fail open).

A **manual** `RefreshSeriesCommand` trigger bypasses all of this and always
refreshes every requested series unconditionally.

### 4.5 Housekeeping tasks (run as a batch by `HousekeepingCommand`)

File: `NzbDrone.Core/Housekeeping/HousekeepingService.cs` — iterates every
registered `IHousekeepingTask` (DI-discovered), running each independently
(one throwing doesn't stop the rest), then calls `_mainDb.Vacuum()`. Full
enumerated list from `NzbDrone.Core/Housekeeping/Housekeepers/` (31 files):

| Housekeeper | What it deletes/fixes |
|---|---|
| `CleanupAbsolutePathMetadataFiles.cs` | `MetadataFiles` rows pointing at absolute paths no longer valid |
| `CleanupAdditionalNamingSpecs.cs` | Orphaned `NamingConfig` rows beyond the single expected row |
| `CleanupAdditionalUsers.cs` | Extra `Users` rows beyond the single-user model |
| `CleanupCommandQueue.cs` | Stale queued/started `Commands` rows left over from crashes |
| `CleanupDownloadClientUnavailablePendingReleases.cs` | `PendingReleases` tied to download clients that no longer exist or are disabled |
| `CleanupDuplicateMetadataFiles.cs` | Duplicate `MetadataFiles` rows (keeps `MIN(Id)` per logical key) |
| `CleanupExtraFilesInExcludedFolders.cs` | Extra-file DB rows for files under folders now excluded from import |
| `CleanupOrphanedBlocklist.cs` | `Blocklist` rows referencing a deleted `Series` |
| `CleanupOrphanedDownloadClientStatus.cs` | `DownloadClientStatus` rows for download clients no longer configured |
| `CleanupOrphanedEpisodeFiles.cs` | `EpisodeFiles` rows with no owning `Episodes` |
| `CleanupOrphanedEpisodes.cs` | `Episodes` rows with no owning `Series` |
| `CleanupOrphanedExtraFiles.cs` | `ExtraFiles` rows referencing deleted series/episodes/files |
| `CleanupOrphanedHistoryItems.cs` | `History` rows referencing deleted series/episodes |
| `CleanupOrphanedImportListStatus.cs` | `ImportListStatus` rows for deleted import lists |
| `CleanupOrphanedIndexerStatus.cs` | `IndexerStatus` rows for deleted indexers |
| `CleanupOrphanedMetadataFiles.cs` | `MetadataFiles` rows referencing deleted series/episodes/files |
| `CleanupOrphanedNotificationStatus.cs` | `NotificationStatus` rows for deleted notification connections |
| `CleanupOrphanedPendingReleases.cs` | `PendingReleases` rows referencing deleted series |
| `CleanupOrphanedSubtitleFiles.cs` | `SubtitleFiles` rows referencing deleted series/episodes/files |
| `CleanupQualityProfileFormatItems.cs` | Stale custom-format score entries in quality profiles referencing deleted custom formats |
| `CleanupTemporaryUpdateFiles.cs` | Leftover self-update temp files/folders |
| `CleanupUnusedTags.cs` | `Tags` not referenced by any series/indexer/notification/etc. table (builds a used-tag id set across every taggable table, deletes the rest) |
| `DeleteBadMediaCovers.cs` | Zero-byte / corrupt cached cover images so they get refetched |
| `FixFutureDownloadClientStatusTimes.cs` | Clock-skew guard (generic base, see below) applied to `DownloadClientStatus` |
| `FixFutureImportListStatusTimes.cs` | Same, for `ImportListStatus` |
| `FixFutureIndexerStatusTimes.cs` | Same, for `IndexerStatus` |
| `FixFutureNotificationStatusTimes.cs` | Same, for `NotificationStatus` |
| `FixFutureProviderStatusTimes.cs` | Generic base class: clamps any provider-status timestamp that is (incorrectly) in the future back to now, guarding against clock jumps poisoning backoff timers |
| `FixFutureRunScheduledTasks.cs` | Same idea for `ScheduledTasks.LastExecution`/`LastStartTime`; explicitly **skipped in debug builds** ("Not running scheduled task last execution cleanup during debug") |
| `TrimLogDatabase.cs` | Trims the separate log database down to retention policy |
| `UpdateCleanTitleForSeries.cs` | Recomputes `Series.CleanTitle` for any rows where the normalization algorithm has changed |

### 4.6 Which commands are user-triggerable

Any `Command` subclass can, in principle, be POSTed to
`Sonarr.Api.V5/Commands/CommandController.cs` (`POST /command`, resolves the
type by name via `KnownTypes.GetImplementations(typeof(Command))` reflection —
there's no explicit allow-list; whatever exists in the assembly is triggerable
by name). Practically, the UI/API surfaces these as first-class "run now"
actions:
- `RssSyncCommand`, `BackupCommand`, `CheckHealthCommand`,
  `HousekeepingCommand`, `CleanUpRecycleBinCommand`, `ApplicationUpdateCheckCommand`
  — the built-in scheduled tasks, runnable ad hoc.
- `RefreshSeriesCommand` (all series or a specific list; `IsNewSeries` flag set
  true only for just-added series to force unconditional refresh).
- `MissingEpisodeSearchCommand`, `CutoffUnmetEpisodeSearchCommand`,
  `EpisodeSearchCommand`, `SeasonSearchCommand`, `SeriesSearchCommand`
  — manual "search now" actions at every granularity.
- `MoveSeriesCommand` / `BulkMoveSeriesCommand` — relocate on-disk files when a
  series' root/path changes.
- `ImportListSyncCommand` — sync all or one specific import list.
- `ClearBlocklistCommand` — wipe the whole blocklist.
- `RefreshMonitoredDownloadsCommand` — force an immediate download-client poll.
- `TestCommand`/`UnknownCommand` exist purely for internal messaging
  self-tests, not user-facing.

Every command has `Command.Trigger` set to `CommandTrigger.Manual` when it
comes through the HTTP controller (vs. `Scheduled` from the `Scheduler`, or
`Unspecified` for internally-pushed follow-on commands like the "search for
newly aired episode" case in §1.4). Several behaviors branch on this: e.g.
`RefreshSeriesService.RescanSeries` only force-rescans-after-refresh for
`Manual` trigger under the `AfterManual` rescan-after-refresh setting;
`DelaySpecification` skips the whole delay-profile wait for anything flagged
`UserInvokedSearch` (§8 wiring, derived from `Trigger == Manual`).

Config surface for this section: **RSS Sync Interval** (minutes, config key
`RssSyncInterval`, default 15 — validated/clamped in
`GetRssSyncInterval()`), **Backup Interval** (days, config key
`BackupInterval`, default 7, clamped 1–7 in `GetBackupInterval()`),
**Backup Retention** (days, config key `BackupRetention`, default 28,
enforced in `BackupService.CleanupOldBackups`), **Backup Folder** (config key
`BackupFolder`, default `"Backups"`, relative to AppData unless rooted).

---

## 5. The Command / queue system

Directory: `NzbDrone.Core/Messaging/Commands/` (22 files, all enumerated
below).

### 5.1 Command base type

File: `Command.cs` — abstract base. Key virtual properties every concrete
command can override to change queue behavior:
- `SendUpdatesToClient` (bool, default false unless overridden) — whether
  SignalR pushes live status to the UI.
- `UpdateScheduledTask` (bool, default **true**) — whether completing this
  command updates the matching `ScheduledTask.LastExecution` row (so, e.g., a
  `RefreshSeriesCommand` targeting specific series ids does **not** update the
  scheduled task's last-run clock — only the "sweep all series" form does,
  via `UpdateScheduledTask => SeriesIds.Empty()`).
- `RequiresDiskAccess` (bool, default false) — see queue interleaving rules
  below.
- `IsExclusive` (bool, default false) — see queue interleaving rules below.
- `IsLongRunning` (bool, default false) — see queue interleaving rules below.
- `CompletionMessage` (string, default null) — user-facing completion text.
- `Name` is auto-derived from the class name with the `Command` suffix
  stripped (`GetType().Name.Replace("Command", "")`) — this is also how the
  HTTP controller resolves a command by string name.

### 5.2 Dedup / equality

File: `CommandEqualityComparer.cs` — reflection-based structural equality
across **all public properties except `Id` and anything declared directly on
the `Command` base class** (so `Trigger`, `SuppressMessages`, etc. are ignored
for equality, but subclass-specific fields like `SeriesIds`/`Monitored`
matter). Collections are compared as sets (symmetric-difference check), not
ordered sequences.

`CommandQueueManager.Push`/`PushMany` (file `CommandQueueManager.cs`) use this
comparer against everything currently `QueuedOrStarted()` with the **same
command name**: if a structurally-equal command is already queued or running,
the new push is dropped and the **existing** `CommandModel` is returned
instead of creating a duplicate. This is the core dedup guarantee — e.g.
pushing `RefreshSeriesCommand` for series 5 while one for series 5 is already
queued is a no-op; pushing it for series 5 while one for series 6 is queued is
not (different `SeriesIds` payload ⇒ not equal).

### 5.3 Priority and ordering

File: `CommandPriority.cs` — three-level enum: `Low = -1`, `Normal = 0`,
`High = 1`. Default push priority is `Normal` unless the caller specifies
otherwise (scheduled tasks each declare their own priority in the
`TaskManager` table — only `RefreshMonitoredDownloadsCommand` is `High` by
default, everything else is `Low`).

`CommandQueue.TryGet()` (file `CommandQueue.cs`, lines 147-222) is the actual
scheduler-of-one-thread-pool-slot logic:
- Candidate pool starts as all `Queued` commands.
- If any **currently started** command has `RequiresDiskAccess == true`, only
  queued commands with `RequiresDiskAccess == false` are eligible (keeps disk
  contention down to one disk-heavy job at a time while letting
  non-disk work proceed).
- If any currently started command `IsLongRunning == true`, only queued
  commands with `IsExclusive == false` are eligible (long-running jobs like
  RSS sync don't block exclusive jobs from *becoming* eligible, but...).
- Among eligible candidates, pick the one with highest `Priority`, tie-broken
  by earliest `QueuedAt` (FIFO within a priority tier).
- If **any started command is exclusive**, no new command starts at all
  (exclusive jobs run alone).
- If the **chosen** next command `IsExclusive` and anything is currently
  started, it waits (won't preempt/interleave with in-flight work) — an
  exclusive job only starts when the queue is otherwise idle.
- Otherwise, the command is marked `Started`, `StartedAt = UtcNow`, and
  returned. `CommandQueue.GetConsumingEnumerable` blocks (via
  `Monitor.Wait`) when nothing is eligible, waking on any `Add`/`RemoveMany`/
  cancellation.

`CommandPriorityComparer.cs` is a separate, UI-facing comparer used only for
**display ordering** (`Started` always sorts first, then by the
`CommandStatus` enum's own ordering) — not part of the execution scheduler.

### 5.4 Execution

File: `CommandExecutor.cs` — spins up **3 fixed worker threads**
(`THREAD_LIMIT = 3`) at `ApplicationStartedEvent`, each looping
`_commandQueueManager.Queue(cancellationToken)` (the blocking consuming
enumerable from §5.3) and dynamically dispatching to whatever
`IExecute<TCommand>` handler DI resolves for the concrete command type
(`_serviceFactory.Build(typeof(IExecute<TCommand>))` via C# `dynamic`
double-dispatch). Wraps execution with start/complete/fail bookkeeping back
into `CommandQueueManager`, publishes `CommandExecutedEvent` in a `finally`
(this is what `TaskManager.Handle(CommandExecutedEvent)` listens for to
advance `ScheduledTask.LastExecution`), and broadcasts SignalR updates when
the command opts in via `SendUpdatesToClient`.

### 5.5 Persistence & startup recovery

Files: `CommandModel.cs` (id, name, body, priority, status, result,
queued/started/ended timestamps, duration, exception text, trigger, message),
`CommandRepository.cs`, `CommandResult.cs` (`Unknown`/`Successful`/
`Indeterminate` — the latter used when a batch operation partially fails, see
`RefreshSeriesService` catching `SeriesNotFoundException` per-series and
reporting `Indeterminate` instead of failing the whole refresh command),
`CommandStatus.cs` (`Queued`, `Started`, `Completed`, `Failed`, `Aborted`,
`Cancelled`, `Orphaned`), `CommandFailedException.cs`,
`CommandNotFoundException.cs`, `UnknownCommand.cs` /
`UnknownCommandExecutor.cs` (placeholder executed for a command name the app
no longer recognizes, so old queued rows from a previous version don't crash
startup), `TestCommand.cs` / `TestCommandExecutor.cs` (used only by
integration tests), `BackendCommandAttribute.cs` (marks a command as
backend-internal, excluded from certain client-facing listings),
`MessagingCleanupCommand.cs` (the periodic command/event GC job itself),
`CleanupCommandMessagingService.cs` (its implementation — trims commands via
`CommandQueueManager.CleanCommands()`, which removes anything that ended more
than 5 minutes ago and calls `_repo.Trim()`).

On `ApplicationStartedEvent`, `CommandQueueManager.Handle` calls
`_repo.OrphanStarted()` (any row still `Started` from a previous, presumably
crashed, process run is marked `Orphaned`) then `Requeue()` (re-adds every
still-`Queued` DB row back into the in-memory `CommandQueue` so nothing queued
before a restart is lost).

### 5.6 HTTP surface

File: `Sonarr.Api.V5/Commands/CommandController.cs` — `POST /command` (body's
`name` field resolved to a `Command` subtype via reflection, deserialized,
`Trigger` forced to `Manual`, `SendUpdatesToClient` forced `true`,
`SuppressMessages` set to the *inverse* of whatever the type's own default
`SendUpdatesToClient` was — so a normally-silent command becomes visible when
manually triggered but is marked "suppress" so it doesn't also spam like a
naturally-noisy one), `GET /command` (list all, sorted started-first then by
priority descending), `DELETE /command/{id}` (cancel — only works if the
command is still `Queued`; throws `409 Conflict` otherwise via
`CommandQueue.RemoveIfQueued`/`CommandQueueManager.Cancel`).

---

## 6. Missing-episode automatic search behavior

There is no single "the missing search algorithm" — it's the union of several
triggers feeding the same underlying search primitives.

### 6.1 Command-driven bulk search

File: `NzbDrone.Core/IndexerSearch/EpisodeSearchService.cs`,
`Execute(MissingEpisodeSearchCommand message)` (lines 121-176):
- If `message.SeriesId` is set: pulls all episodes for that series, filtered
  in-memory to `Monitored == message.Monitored && !HasFile && AirDateUtc
  <= now`.
- Otherwise (whole-library sweep): builds a `PagingSpec<Episode>` with
  `PageSize = 1_000_000` (effectively unbounded single page) and the same
  monitored/seriesIds/qualityProfileIds/seriesType filters as the Wanted
  Missing API (§3.1), then calls `_episodeService.EpisodesWithoutFiles(...,
  includeSpecials: true, seriesTags)`.
- **Queue exclusion**: `GetQueuedEpisodeIds()` reads the live download queue
  (`IQueueService.GetQueue()`) and subtracts any episode id already present in
  a queue item's `Episodes` list — episodes already downloading are never
  re-searched.
- **Batching**: `SearchForBulkEpisodes` (lines 43-103) groups the remaining
  episode list first by `SeriesId`, then by `SeasonNumber`, producing one
  `EpisodeSearchGroup` per (series, season) pair. Groups are processed in
  ascending order of `Episodes.Min(LastSearchTime ?? DateTime.MinValue)` —
  i.e. episodes/seasons that have gone longest without being searched (or
  never searched) are prioritized first within the run. A group with more
  than one episode goes through `SeasonSearch` (a single combined query where
  possible); a lone episode goes through `EpisodeSearch`. Each group's
  resulting `DownloadDecision`s are immediately handed to
  `IProcessDownloadDecisions.ProcessDecisions` (so grabs happen incrementally
  per group, not batched at the end), and a running `downloadedCount` is
  logged at completion.
  - Errors on an individual season/episode search are caught, logged, and
    the loop **continues** to the next group rather than aborting the whole
    run.
- `Cutoff Unmet` (`Execute(CutoffUnmetEpisodeSearchCommand)`, lines 178-225)
  is structurally identical except it sources episodes from
  `IEpisodeCutoffService.EpisodesWhereCutoffUnmet` and supports an additional
  `Quality` id filter; it shares the same queue-exclusion and
  `SearchForBulkEpisodes` batching path.
- `userInvokedSearch` passed down to the actual indexer dispatch is
  `message.Trigger == CommandTrigger.Manual` — this is what lets
  `DelaySpecification` (§8) distinguish "a human explicitly asked for this
  search right now" (bypass delay) from "this happened to run because the
  user's custom scheduled task fired" (still respect delay, since
  `CommandTrigger.Scheduled != Manual`).

### 6.2 Event-driven single/small-batch search

- **New episode aired / absolute-number backfilled** — `EpisodeRefreshedService`
  (§1.4 item 4): pushes a plain `EpisodeSearchCommand` for the small cached id
  list once a series refresh completes. No delay-profile bypass here (goes
  through the normal `EpisodeSearchCommand` executor path, `Trigger` is
  whatever the surrounding refresh's trigger was, typically `Unspecified`/
  `Scheduled`).
- **Failed download auto-redownload** — `RedownloadFailedDownloadService`
  (`NzbDrone.Core/Download/RedownloadFailedDownloadService.cs`), an
  `IHandle<DownloadFailedEvent>` ordered to run **last**
  (`[EventHandleOrder(EventHandleOrder.Last)]`, i.e. after blocklisting has
  already happened — see §7):
  - No-ops if `message.SkipRedownload` was explicitly requested by the caller
    (e.g. "remove and blocklist, but don't search again").
  - No-ops if config `AutoRedownloadFailed` is off.
  - No-ops if the failure came from an interactive search and config
    `AutoRedownloadFailedFromInteractiveSearch` is off (lets a user disable
    auto-retry specifically for releases they hand-picked, while keeping it on
    for RSS/automatic grabs).
  - If exactly one episode was in the failed download → push
    `EpisodeSearchCommand` for it.
  - Else if the failed download's episode set matches the **entire season's**
    episode count → push `SeasonSearchCommand` (whole-season re-grab, e.g. a
    season pack failed).
  - Else (a multi-episode-but-not-full-season download, e.g. a double
    episode) → push `EpisodeSearchCommand` for the affected episode ids.
- **Import-list-driven, at add time** — `AddSeriesOptions.SearchForMissingEpisodes`
  / `SearchForCutoffUnmetEpisodes`, set per import list
  (`ImportListDefinition.SearchForMissingEpisodes`) and copied onto each newly
  added series' `AddOptions`; consumed by the series-add completion pipeline
  to immediately queue the corresponding bulk search command scoped to that
  one series.
- **RSS sync** (`RssSyncService`, §5's `RssSyncCommand`) is the main *organic*
  discovery path: it doesn't target specific missing episodes at all, it
  fetches whatever indexers currently have in their RSS feed, runs the full
  decision pipeline (including `DelaySpecification`) against it, and grabs
  anything that resolves to a monitored missing/cutoff-unmet episode. This is
  why the default schedule has no dedicated "search for missing" task — RSS
  sync running every `RssSyncInterval` minutes is expected to catch new
  releases as indexers post them, and the manual/scheduled bulk search
  commands exist as a backstop for indexers with weak RSS coverage or gaps
  from downtime.

Config surface for this section: `AutoRedownloadFailed` (bool, default true),
`AutoRedownloadFailedFromInteractiveSearch` (bool, default true),
`DownloadPropersAndRepacks` (enum `ProperDownloadTypes`, default
`PreferAndUpgrade` — also consulted by `DelaySpecification`), `RssSyncInterval`
(see §4.6).

---

## 7. Blocklist

### 7.1 Data model

File: `NzbDrone.Core/Blocklisting/Blocklist.cs`:
```csharp
public class Blocklist : ModelBase
{
    public int SeriesId { get; set; }
    public Series Series { get; set; }
    public List<int> EpisodeIds { get; set; }
    public string SourceTitle { get; set; }
    public QualityModel Quality { get; set; }
    public DateTime Date { get; set; }
    public DateTime? PublishedDate { get; set; }
    public long? Size { get; set; }
    public DownloadProtocol Protocol { get; set; }
    public string Indexer { get; set; }
    public IndexerFlags IndexerFlags { get; set; }
    public ReleaseType ReleaseType { get; set; }
    public string Message { get; set; }
    public string Source { get; set; }
    public string TorrentInfoHash { get; set; }
    public List<Language> Languages { get; set; }
}
```
A blocklist entry is scoped to **one series** plus the specific episode ids
that release covered — it is not a global "never grab this title again," it's
"never grab *this exact release* again for *this series/episodes*."

### 7.2 Matching logic — is a candidate release already blocklisted?

File: `NzbDrone.Core/Blocklisting/BlocklistService.cs`,
`Blocklisted(int seriesId, ReleaseInfo release)`:
- **Torrents**: if the release isn't actually a `TorrentInfo`, never matches.
  If it has an info hash, look up blocklist rows for that series with a
  matching (substring-contains) `TorrentInfoHash` and confirm via
  `ReleaseComparer.SameTorrent` (exact hash match if the blocklist entry has
  one, else falls back to same-indexer-name match). If it has no hash, fall
  back to title-substring lookup filtered to `Protocol == Torrent` and the
  same `SameTorrent` check (indexer-name based, since no hash to compare).
- **Usenet**: title-substring lookup filtered to `Protocol == Usenet`, checked
  via `ReleaseComparer.SameNzb` — exact published-date match short-circuits
  true; otherwise it must be from a **different** indexer than the blocklist
  entry (or the entry has no indexer recorded) **and** published within ±2
  minutes **and** within 2 MB of the same size (`ReleaseComparer.cs`,
  `HasSameSize`/`HasSamePublishedDate`/`HasSameIndexer`). This fuzzy match
  exists because some indexers republish an identical NZB with a shifted
  timestamp/tiny size difference.
- `BlocklistedTorrentHash(seriesId, hash)` — a separate direct hash lookup used
  elsewhere in the download pipeline to reject a torrent purely by hash before
  even trying to match full release metadata.

### 7.3 What triggers a blocklist entry

1. **Automatic on download failure** — `BlocklistService.Handle(DownloadFailedEvent)`:
   builds a `Blocklist` row from the failed download's history metadata
   (indexer, quality, protocol, languages, indexer flags, release type,
   torrent hash if applicable) and inserts it. This runs for *every* detected
   failure (encrypted download, download-client-reported failure, or a
   manually-marked-failed history item) regardless of whether auto-redownload
   is enabled — blocklisting and redownloading are independent concerns (see
   §6.2's separate `RedownloadFailedDownloadService`, which explicitly runs
   **after** this handler).
2. **Manual, via the Queue UI/API** — `Sonarr.Api.V5/Queue/QueueController.cs`:
   - `DELETE /queue/{id}?blocklist=true` on a **pending** (not-yet-grabbed)
     release: blocklists it directly via `BlocklistService.Block(...)` with
     message `"Pending release manually blocklisted"`, then removes the
     pending queue item. No redownload logic applies (nothing was ever
     grabbed).
   - `DELETE /queue/{id}?blocklist=true[&skipRedownload=true]` on an
     **actively downloading/imported** tracked download: calls
     `IFailedDownloadService.MarkAsFailed(trackedDownload, message, source,
     skipRedownload)`, which synthesizes a `DownloadFailedEvent` (same event
     as the automatic path above) — so manual blocklisting **reuses the exact
     same event pipeline** as automatic failure detection, including
     `BlocklistService`'s insert and (unless `skipRedownload=true`)
     `RedownloadFailedDownloadService`'s automatic re-search. **This is the
     literal implementation of "blocklist and search again"**: it is simply
     "blocklist with `skipRedownload=false`" (the default), vs. "blocklist
     only" which is `skipRedownload=true`.
   - Bulk equivalents exist for both cases (`POST /queue/bulk` variants /
     `RemoveMany`) with the same `blocklist`/`skipRedownload` query flags
     applied per item.
3. **`FailedDownloadService.MarkAsFailed(int historyId, ...)`** — the
   history-based manual "mark as failed" action (works even when there's no
   live tracked download anymore, e.g. long after the fact), reusing the same
   grabbed-history lookup and `DownloadFailedEvent` publication.

### 7.4 How blocklisting affects future decisions

The blocklist is consulted as a rejection specification in the download
decision engine (outside this report's directory scope but load-bearing here):
any release matching `IBlocklistService.Blocklisted(seriesId, release)` is
rejected before it can be grabbed again, for both RSS sync and explicit
searches, permanently (until the blocklist entry is removed) — this is a
`RejectionType.Permanent` in the same specification pipeline that
`ReleaseRestrictionsSpecification`/`AirDateSpecification`/`DelaySpecification`
participate in (§8).

### 7.5 Manual blocklist management

File: `NzbDrone.Core/Blocklisting/ClearBlocklistCommand.cs` — a plain `Command`
(`SendUpdatesToClient => true`) executed by
`BlocklistService.Execute(ClearBlocklistCommand)` → `_blocklistRepository.Purge()`
(wipes the entire table). Individual-row management (`Delete(int id)` /
`Delete(List<int> ids)`) and a paged listing (`Paged(PagingSpec<Blocklist>)`,
joined to `Series` for display) back the Settings → Blocklist UI, where a user
can selectively remove entries to allow a previously-blocklisted release to be
considered again. Blocklist rows are also cascade-deleted when their series is
deleted (`HandleAsync(SeriesDeletedEvent)`) and swept for orphans by the
`CleanupOrphanedBlocklist` housekeeper (§4.5).

---

## 8. Delay profiles (how they gate automatic search timing)

Not explicitly one of the listed source directories, but directly answers the
prompt's "respect for delay profiles" requirement, and is tightly coupled to
Tags (§10) and search behavior (§6).

Files: `NzbDrone.Core/Profiles/Delay/DelayProfile.cs`,
`DelayProfileService.cs`, `DelayProfileRepository.cs`,
`DelayProfileTagInUseValidator.cs`; enforcement in
`NzbDrone.Core/DecisionEngine/Specifications/RssSync/DelaySpecification.cs`.

- `DelayProfile` fields: `EnableUsenet`/`EnableTorrent` (protocol gating),
  `PreferredProtocol`, `UsenetDelay`/`TorrentDelay` (minutes), `Order`
  (priority — profile with tags matching the series wins, lowest `Order`
  first; the untagged, `Order == 1`-pinned profile is the fallback default and
  cannot be deleted), `BypassIfHighestQuality` (bool), `BypassIfAboveCustomFormatScore`
  (bool) + `MinimumCustomFormatScore` (int), `Tags` (HashSet<int>).
- `DelayProfileService.BestForTags(tagIds)` picks the lowest-`Order` profile
  whose `Tags` intersects the series' tags (or has no tags at all, i.e. the
  global default), cached for 30 seconds per unique tag-set key.
- `DelaySpecification.IsSatisfiedBy` (the actual gate, evaluated per candidate
  release during both RSS sync and explicit searches):
  1. **Immediately accepts** if `information.SearchCriteria.UserInvokedSearch
     == true` — delay profiles never block a search the user explicitly
     asked for right now (interactive search, or a manually-triggered
     missing/cutoff/episode/season search command).
  2. Look up the best delay profile for the series' tags; compute
     `delay = profile.GetProtocolDelay(release.DownloadProtocol)`. If `0`,
     accept immediately (no wait configured for that protocol).
  3. If the release's protocol matches the profile's `PreferredProtocol` and
     config `DownloadPropersAndRepacks == PreferAndUpgrade`, and the release
     is a same-or-better-revision proper/repack of a file **already on disk**
     for one of the episodes, accept immediately (don't delay a proper/repack
     upgrade of an existing file).
  4. If `BypassIfHighestQuality` and the release's quality is already the
     best allowed in the series' quality profile **and** it's the preferred
     protocol, accept immediately.
  5. If `BypassIfAboveCustomFormatScore` and the release's computed custom
     format score already meets `MinimumCustomFormatScore` **and** it's the
     preferred protocol, accept immediately.
  6. Otherwise, check `IPendingReleaseService.OldestPendingRelease` for these
     episodes — if something has already been sitting pending longer than the
     configured delay, accept now (the wait already happened via an earlier
     pending release, don't wait again from scratch for a newer report of
     roughly the same thing).
  7. If the **current** release's own age is still under the delay, **reject**
     with `DownloadRejectionReason.MinimumAgeDelay` ("Waiting for better
     quality release") — this is what actually produces a **Pending Release**
     queue entry rather than an immediate grab.
  8. Otherwise accept.

This is why "missing-episode automatic search" and "RSS sync" behave
differently with respect to delay: RSS sync candidates are non-user-invoked by
construction and always pass through this full gate; a manually-triggered
`MissingEpisodeSearchCommand`/`EpisodeSearchCommand` run with
`CommandTrigger.Manual` sets `UserInvokedSearch = true` on its search
criteria and skips the delay wait entirely (step 1), while the **same**
command running off a user-added recurring scheduled task
(`CommandTrigger.Scheduled`) does **not** set `UserInvokedSearch`, so it still
respects delay profiles.

---

## 9. Health checks

Directory: `NzbDrone.Core/HealthCheck/` + `NzbDrone.Core/HealthCheck/Checks/`
(27 concrete check files — **all enumerated below**, none omitted).

### 9.1 Framework

- `HealthCheck.cs` — result model: `Type` (`HealthCheckResult`: `Ok`,
  `Notice`, `Warning`, `Error`), `Reason` (`HealthCheckReason` enum — every
  distinct failure condition across every check, ~70 values, used as a stable
  machine key independent of the (localizable) message text), `Message`
  (localized string), `WikiUrl` (auto-derived wiki anchor if not explicit).
- `HealthCheckBase.cs` — base class taking `ILocalizationService`.
- `IProvideHealthCheck.cs` — the check contract (`Check()`, plus
  `CheckOnStartup`/`CheckOnSchedule` flags read via reflection from the
  `[CheckOn(...)]` attributes on the class).
- `CheckOnAttribute.cs` — `[CheckOn(typeof(SomeEvent), condition)]`,
  repeatable; `CheckOnCondition`: `Always`, `FailedOnly` (only re-run if that
  event type's outcome was a failure), `SuccessfulOnly` (only re-run if that
  event's outcome was success — used to re-check a *different*, related
  health check when something succeeds, e.g. re-running the root-folder check
  when an episode import succeeds after previously failing).
- `EventDrivenHealthCheck.cs` / `ICheckOnCondition.cs` — wraps a check +
  its attribute-declared trigger event type + condition, and (optionally) a
  check-specific `ShouldCheckOnEvent(TEvent)` predicate for finer-grained
  gating beyond the blanket `CheckOnCondition` (used by
  `RemovedSeriesCheck`, see below).
- `HealthCheckService.cs` — orchestrates three run modes:
  1. **Startup** (`HandleAsync(ApplicationStartedEvent)`): runs every check
     flagged `CheckOnStartup` immediately, synchronously (not debounced).
  2. **Scheduled** (`Execute(CheckHealthCommand)`): if `Trigger == Manual`,
     runs **every** registered check; otherwise (the 6-hour scheduled task)
     runs only checks flagged `CheckOnSchedule`.
  3. **Event-driven**: `HandleAsync(IEvent)` (a catch-all handler for the
     entire event bus) looks up which checks declared `[CheckOn(typeof(that
     event))]`, filters by the attribute's `CheckOnCondition` /
     `ShouldCheckOnEvent`, and queues them into a debounced batch (5-second
     debounce, so a burst of related events collapses into one check pass).
  - A **15-minute startup grace period** (`_startupGracePeriodEndTime =
    StartTime + 15min`) suppresses failure *notifications* (not the checks
    themselves) until it elapses, then forces one full re-run of the startup
    checks and explicitly re-publishes `HealthCheckFailedEvent` for anything
    still failing — this avoids spamming failure notifications for
    transient conditions during app boot (e.g. a download client container
    still starting up) while still surfacing genuinely persistent problems
    once boot has clearly finished.
  - Results are cached by check-type name (`ICached<HealthCheck>`); a
    previously-failing check returning `Ok` fires `HealthCheckRestoredEvent`
    and clears the cache entry; a newly-failing check (not previously
    recorded) fires `HealthCheckFailedEvent`.

### 9.2 Every concrete check (27, alphabetical by file)

| File | Trigger(s) | What it verifies | `HealthCheckReason`(s) |
|---|---|---|---|
| `ApiKeyValidationCheck.cs` | Startup + `ConfigSavedEvent` | API key is at least 20 characters | `MinimumApiKeyLength` |
| `AppDataLocationCheck.cs` | Startup only (no attribute ⇒ startup default) | App data folder isn't inside (or equal to) the startup/install folder (would be wiped on update) | `AppDataLocation` |
| `DownloadClientCheck.cs` | Download-client provider add/update/delete/status-change | At least one download client configured; each one is reachable (`GetItems()`) | `DownloadClientCheckNoneAvailable`, `DownloadClientCheckUnableToCommunicate` |
| `DownloadClientRemovesCompletedDownloadsCheck.cs` | Download-client update/delete, root-folder change, remote-path-mapping change | Client isn't configured to auto-remove completed downloads (breaks import tracking) | `DownloadClientRemovesCompletedDownloads` |
| `DownloadClientRootFolderCheck.cs` | Same as above | Download client's output folder isn't the same as (or parent of) a library root folder | `DownloadClientRootFolder` |
| `DownloadClientSortingCheck.cs` | Same as above | Client-side post-processing/sorting isn't enabled (would move files before Sonarr imports them) | `DownloadClientSorting` |
| `DownloadClientStatusCheck.cs` | Download-client update/delete/status-change | No/some/all enabled download clients are in backoff due to repeated failures | `DownloadClientStatusSingleClient`, `DownloadClientStatusAllClients` |
| `ImportListRootFolderCheck.cs` | Import-list update/delete, root-folder change, series deleted/moved, import success/failure | Every configured import list's root folder path is valid, known, and exists on disk | `ImportListRootFolderMissing`, `ImportListRootFolderMultipleMissing` |
| `ImportListStatusCheck.cs` | Import-list update/delete/status-change | No/some/all import lists are in backoff | `ImportListStatusUnavailable`, `ImportListStatusAllUnavailable` |
| `ImportMechanismCheck.cs` | Download-client update/delete, `ConfigSavedEvent` | Completed Download Handling is enabled where it can/should be (special-cased messaging for SABnzbd/NZBGet, and a multi-computer warning when clients aren't all localhost) | `ImportMechanismEnableCompletedDownloadHandlingIfPossible(MultiComputer)`, `ImportMechanismHandlingDisabled` |
| `IndexerDownloadClientCheck.cs` | Indexer or download-client add/update/delete | Every enabled indexer's assigned "download via" client id still refers to an existing client | `IndexerDownloadClient` |
| `IndexerJackettAllCheck.cs` | Indexer add/update/delete/status-change | No enabled Torznab indexer is pointed at Jackett's "all indexers" meta-endpoint (known to misbehave) | `IndexerJackettAll` |
| `IndexerLongTermStatusCheck.cs` | Indexer update/delete/status-change | No/some/all indexers have been failing for **more than 6 hours** (a longer-horizon variant of the next check) | `IndexerLongTermStatusUnavailable`, `IndexerLongTermStatusAllUnavailable` |
| `IndexerRssCheck.cs` | Indexer add/update/delete/status-change | At least one indexer has RSS sync enabled, and at least one such indexer is currently available (not backed off) | `IndexerRssNoIndexersEnabled`, `IndexerRssNoIndexersAvailable` |
| `IndexerSearchCheck.cs` | Indexer add/update/delete/status-change | At least one indexer supports automatic search, at least one supports interactive search, and at least one automatic-capable indexer is currently available | `IndexerSearchNoAutomatic`, `IndexerSearchNoInteractive`, `IndexerSearchNoAvailableIndexers` |
| `IndexerStatusCheck.cs` | Indexer update/delete/status-change | No/some/all indexers have failed **within the last 6 hours** (short-horizon companion to `IndexerLongTermStatusCheck`) | `IndexerStatusUnavailable`, `IndexerStatusAllUnavailable` |
| `MountCheck.cs` | No attribute (checked on schedule/startup by inclusion in the default arrays) | No series' path resolves to a **read-only** mount | `MountSeries` |
| `NotificationStatusCheck.cs` | Notification update/delete/status-change | No/some/all notification connections are in backoff | `NotificationStatusSingle`, `NotificationStatusAll` |
| `PackageGlobalMessageCheck.cs` | No attribute | Surfaces an operator-injected global message from the deployment/package metadata (prefix `Error:`/`Warn:` sets severity, else `Notice`) | `Package` |
| `ProxyCheck.cs` | `ConfigSavedEvent` | If a proxy is configured: its hostname resolves via DNS, and a ping to Sonarr's cloud service through the proxy doesn't 400/error | `ProxyResolveIp`, `ProxyBadRequest`, `ProxyFailed` |
| `RecyclingBinCheck.cs` | Episode import success/failure (opposite-condition pairing) | Configured recycle bin folder (if any) is writable | `RecycleBinUnableToWrite` |
| `RemotePathMappingCheck.cs` | Download-client update/delete, remote-path-mapping change, episode import failure | The largest single check (19KB): validates remote path mappings resolve correctly across OS boundaries (Docker path translation, wrong-OS path separators, permission mismatches on both the download-client-visible and Sonarr-visible paths, files unexpectedly removed mid-import, generic/Docker folder-missing cases) | `RemotePathMappingWrongOSPath`, `RemotePathMappingBadDockerPath`, `RemotePathMappingLocalWrongOSPath`, `RemotePathMappingDockerFolderMissing`, `RemotePathMappingLocalFolderMissing`, `RemotePathMappingGenericPermissions`, `RemotePathMappingDownloadPermissionsEpisode`, `RemotePathMappingFileRemoved`, `RemotePathMappingImportEpisodeFailed`, `RemotePathMappingFilesWrongOSPath`, `RemotePathMappingFilesBadDockerPath`, `RemotePathMappingFilesLocalWrongOSPath`, `RemotePathMappingFolderPermissions`, `RemotePathMappingRemoteDownloadClient`, `RemotePathMappingFilesGenericPermissions` |
| `RemovedSeriesCheck.cs` | Series updated/deleted, series refresh complete (plus custom `ShouldCheckOnEvent` gating so it only re-checks when the relevant series is actually in `Deleted` status) | Any series marked `SeriesStatusType.Deleted` (removed from TheTVDB) still exists locally | `RemovedSeriesSingle`, `RemovedSeriesMultiple` |
| `RootFolderCheck.cs` | Series deleted/moved, episode import success/failure | Every distinct root folder derived from series paths exists on disk and isn't empty | `RootFolderMissing`, `RootFolderMultipleMissing`, `RootFolderEmpty` |
| `SystemTimeCheck.cs` | No attribute | Local system clock is within 1 day of Sonarr's cloud time service | `SystemTime` |
| `UpdateCheck.cs` | `ConfigFileSavedEvent` | If built-in auto-update is enabled and not running in Docker: startup folder isn't inside a macOS App Translocation sandbox, startup folder and its `UI` subfolder are writable; separately (independent of update mechanism), warns if a newer build is available and the current build is more than 14 days old | `UpdateStartupTranslocation`, `UpdateStartupNotWritable`, `UpdateUiNotWritable`, `UpdateAvailable` |

Every `HealthCheckReason` value is declared centrally in `HealthCheck.cs`
(the enum shown partially above/inline per row) — it is the single source of
truth Prismedia's equivalent should mirror as a closed set, one entry per
distinct failure condition, decoupled from the localized message text.

---

## 10. Import lists

Directory: `NzbDrone.Core/ImportLists/` (37 top-level entries; enumerated
fully below by concrete provider).

### 10.1 Core sync engine

- `ImportListSyncService.cs` — `Execute(ImportListSyncCommand)`: either syncs
  one definition (`DefinitionId` set) or `SyncAll()` (every list with
  `EnableAutomaticAdd`). Per item: resolves TVDB id directly, or by IMDb id,
  TMDb id, AniList id, or MyAnimeList id (in that priority order, each via a
  dedicated lookup) if the source list didn't supply a TVDB id natively;
  drops items that can't resolve to a TVDB id, are list-excluded (see below),
  or already exist in the library; builds a `Series` + `AddSeriesOptions` per
  surviving item (monitor mode `Skip` if the source list supplied explicit
  per-season data, else the list's configured `ShouldMonitor`) and bulk-adds
  via `IAddSeriesService.AddSeries(..., ignoreErrors:true)`.
  **List Sync Level** (`ListSyncLevelType`: `Disabled`, `LogOnly`,
  `KeepAndUnmonitor`, `KeepAndTag`) drives `CleanLibrary()`, which runs only
  after **every** automatic-add-enabled list has synced successfully at least
  once since the last clean (`AllListsSuccessfulWithAPendingClean`) and
  removed at least one item since then: any library series no longer present
  in **any** list's current item set (matched by TVDB/IMDb/TMDb/MAL/AniList
  id) is logged, unmonitored, or tagged with the configured `ListSyncTag`
  depending on the level.
- `ImportListExclusions` (`Exclusions/ImportListExclusion.cs`,
  `ImportListExclusionRepository.cs`, `ImportListExclusionService.cs`) — a
  standing "never auto-add this TVDB id again" list independent of any one
  provider, consulted before every add.
- `ImportListItems` (`ImportListItemRepository.cs`, `ImportListItemService.cs`)
  — persists the last-fetched raw item set per list (used by `CleanLibrary`'s
  membership check without re-fetching every list on every clean pass).
- `ImportListStatus`/`ImportListStatusService.cs`/`ImportListStatusRepository.cs`
  — per-provider backoff bookkeeping (feeds `ImportListStatusCheck`, §9).
- `ImportListFactory.cs` — the standard provider-factory pattern (enable/tag
  filtering, `AllForTag`, `AutomaticAddEnabled`, etc.).
- `FetchAndParseImportListService.cs` — fetch orchestration (parallel fetch +
  merge across all lists or a single list).
- `HttpImportListBase.cs` / `ImportListBase.cs` — shared HTTP/generic base
  classes; `ImportListPageableRequest.cs` /
  `ImportListPageableRequestChain.cs` — pagination helpers used by the
  HTTP-backed providers.
- `ImportListType.cs` — `Program`, `Plex`, `Trakt`, `Simkl`, `Other`,
  `Advanced` (a coarse UI grouping, not a behavior switch).
- `TolerantEnumConverter.cs` — JSON enum deserialization that falls back
  gracefully instead of throwing when a third-party API returns an unexpected
  enum string.

### 10.2 Every concrete import list provider (11)

| Provider class | Base | `ListType` | `MinRefreshInterval` | Notes |
|---|---|---|---|---|
| `Sonarr/SonarrImport.cs` | `ImportListBase` | `Program` | 5 min | Pulls series from **another Sonarr instance** via `SonarrV3Proxy.cs` (talks the v3 API of the remote instance); filterable by remote quality-profile ids, (deprecated) language-profile ids, tags, and root-folder paths; optional `SyncSeasonMonitoring` copies the remote instance's per-season monitored flags instead of using this list's own `ShouldMonitor` |
| `Plex/PlexImport.cs` | `HttpImportListBase` | `Plex` | 6 hours | Pulls a Plex server's watchlist/library data (`PlexListRequestGenerator.cs`, `PlexParser.cs`, settings in `PlexListSettings.cs`) |
| `Rss/RssImportBase.cs` | `HttpImportListBase` | `Advanced` | 6 hours | Generic RSS-feed-as-import-list (`RssImportBaseParser.cs`, `RssImportRequestGenerator.cs`, `RssImportBaseSettings.cs`); has a Plex-flavored variant under `Rss/Plex/` (`PlexRssImport.cs`, `PlexRssImportParser.cs`, `PlexRssImportSettings.cs`) for Plex's RSS watchlist export specifically |
| `Trakt/List/TraktListImport.cs` | `TraktImportBase<TraktListSettings>` | (Trakt-family) | — | A specific user-curated Trakt list (`TraktListRequestGenerator.cs`) |
| `Trakt/Popular/TraktPopularImport.cs` | `TraktImportBase<TraktPopularSettings>` | (Trakt-family) | — | Trakt's popular/trending/anticipated lists (`TraktPopularListType.cs` enumerates which sub-list, `TraktPopularRequestGenerator.cs`, `TraktPopularParser.cs`) |
| `Trakt/User/TraktUserImport.cs` | `TraktImportBase<TraktUserSettings>` | (Trakt-family) | — | A Trakt user's watchlist/collection/watched history (`TraktUserListType.cs`, `TraktUserWatchedListType.cs`, `TraktUserRequestGenerator.cs`, `TraktUserParser.cs`) |
| `Simkl/User/SimklUserImport.cs` | `SimklImportBase<SimklUserSettings>` | (Simkl) | — | A Simkl user's list, filterable by `SimklUserShowType.cs` / `SimklUserListType.cs`; OAuth-style flow in `SimklAPI.cs` |
| `AniList/List/AniListImport.cs` | `AniListImportBase<AniListSettings>` | (AniList) | — | An AniList (anime tracking) user list (`AniListRequestGenerator.cs`, `AniListParser.cs`, GraphQL types in `AniListTypes.cs`) |
| `MyAnimeList/MyAnimeListImport.cs` | `HttpImportListBase` | `Other` | 6 hours | A MyAnimeList user's list (`MyAnimeListRequestGenerator.cs`, `MyAnimeListParser.cs`, status filter `MyAnimeListStatus.cs`) |
| `Custom/CustomImport.cs` | `ImportListBase` | `Advanced` | 6 hours | A generic external "Advanced" custom HTTP list backend (`CustomImportProxy.cs`, resource shape `CustomAPIResource.cs`) — the escape hatch for arbitrary compatible services |

Shared Trakt plumbing: `TraktAPI.cs` (endpoint constants), `TraktImportBase.cs`
(OAuth token handling + common fetch flow), `TraktParser.cs`,
`TraktQueryHelper.cs`, `TraktSettingsBase.cs`.
Shared Simkl plumbing: `SimklImportBase.cs`, `SimklParser.cs`,
`SimklSettingsBase.cs`.
Shared AniList plumbing: `AniListImportBase.cs`, `AniListSettingsBase.cs`.

Config surface per list (on `ImportListDefinition`, `NzbDrone.Core/ImportLists/ImportListDefinition.cs`):
`EnableAutomaticAdd`, `SearchForMissingEpisodes`, `ShouldMonitor`
(`MonitorTypes`), `MonitorNewItems` (`NewItemMonitorTypes`), `QualityProfileId`,
`SeriesType`, `SeasonFolder`, `RootFolderPath`, plus each provider's own
settings class (credentials, list ids, filters). Global config:
`ListSyncLevel` (default `Disabled`), `ListSyncTag`.

---

## 11. Calendar / upcoming

File: `Sonarr.Api.V5/Calendar/CalendarController.cs`, plus
`CalendarFeedController.cs` (an iCal feed variant) and
`CalendarSubresource.cs`.

- `GET /calendar?start=&end=&includeUnmonitored=&includeSpecials=&tags=`
  defaults `start` to **today** and `end` to **today + 2 days** if not
  supplied — i.e. "what's airing in the next 2 days" is the base case the UI
  builds on for its default upcoming view.
- Backed by `EpisodeRepository.EpisodesBetweenDates(startDate, endDate,
  includeUnmonitored, includeSpecials)` (`NzbDrone.Core/Tv/EpisodeRepository.cs:153-170`):
  a straightforward `AirDateUtc BETWEEN start AND end` range query, with
  season-0 exclusion unless `includeSpecials`, and — unless
  `includeUnmonitored` — an inner join back to `Series` requiring **both**
  `Episode.Monitored` and `Series.Monitored`.
- Additional in-controller tag filtering: `tags` is a comma-separated list of
  tag ids/labels (`ITagService.GetTag` accepts either), and any episode whose
  series doesn't carry at least one of those tags is dropped after the base
  query.
- **Calendar/upcoming does not itself trigger searches.** There is no
  "search N days before air date" scheduled job in this codebase — the
  connection between "airing soon" and "get searched" is entirely indirect,
  via the mechanisms in §6: once an episode's air date has passed (`AirDateUtc
  + runtime <= now`), it becomes a **Missing** episode (§3.1) and is picked up
  by RSS sync / the next missing-search command / the immediate
  post-refresh re-search triggered by `EpisodeRefreshedService` (§1.4-4) if it
  just aired within that handler's `[-14d, +1d]` window. The Calendar view is
  purely a read/inform surface (also consumable as an external iCal feed via
  `CalendarFeedController.cs` for phone/desktop calendar apps), not a search
  scheduler.

---

## 12. Tags system

Directory: `NzbDrone.Core/Tags/` (5 files).

- `Tag.cs` — trivial `{ Id, Label }`.
- `TagDetails.cs` — the "what is this tag used by" aggregate:
  `SeriesIds`, `NotificationIds`, `RestrictionIds` (release profiles),
  `ExcludedReleaseProfileIds`, `DelayProfileIds`, `ImportListIds`,
  `IndexerIds`, `AutoTagIds`, `DownloadClientIds`. `InUse` is true if **any**
  of these lists is non-empty.
- `TagService.cs` — `Details(tagId)` / `Details()` cross-reference every one
  of those subsystems' `Tags` (or `ExcludedTags`) HashSet<int> fields to build
  the usage report; `Delete(tagId)` **hard-blocks** deletion
  (`ModelConflictException`) if `InUse` is true — a tag referenced anywhere
  cannot be deleted until every reference is removed first. `Add`/`Update`
  lower-case the label and publish `TagsUpdatedEvent`.
- `TagRepository.cs` — plain CRUD + `FindByLabel`/`GetByLabel`.
- `TagsUpdatedEvent.cs` — event fired on any tag CRUD.

### 12.1 What tags actually bind together (the "how")

Every one of these entities carries its own `HashSet<int> Tags` (or, for
release profiles, an *additional* `ExcludedTags`), and the binding is always
**"empty tag set = applies to everything," non-empty = "applies only when the
series has at least one of these tags"** (an intersection/OR match, never an
AND-all-tags match):
- **Series** (`Series.Tags`) — the tag set a series itself carries; this is
  the thing every other tag-scoped entity is matched against.
- **Indexers** (`IndexerDefinition.Tags`) — `ReleaseSearchService.Dispatch`
  (`NzbDrone.Core/IndexerSearch/ReleaseSearchService.cs:527`) filters
  candidate indexers to `indexer.Tags.Empty() || indexer.Tags.Intersect(series.Tags).Any()`
  — an indexer with tags only searches series carrying at least one matching
  tag; an untagged indexer searches everything.
- **Download clients** (via indexer→client assignment / tag matching in the
  download-client selection logic) — same pattern, surfaced through
  `TagDetails.DownloadClientIds`.
- **Delay Profiles** (`DelayProfile.Tags`) — `BestForTags` picks the
  lowest-`Order` matching-or-untagged profile (§8); this is how a household
  can, e.g., have anime download instantly (0 delay, tagged) while everything
  else waits for a Usenet-preferred window (untagged default).
- **Release Profiles** (`ReleaseProfile.Tags` + `ReleaseProfile.ExcludedTags`)
  — `EnabledForTags(tagIds, indexerId)` (§13) requires the profile to be
  `Enabled`, match-or-be-untagged on `Tags`, **and** additionally scoped by
  `IndexerIds` (empty = all indexers). `ExcludedTags` is a separate,
  independent hard-exclusion list layered on top (used by
  `AllExcludedForTag`, surfaced in `TagDetails.ExcludedReleaseProfileIds`) —
  a profile can be tag-scoped to include series A/B but explicitly excluded
  from series carrying tag C even if C also matches the include set.
- **Import Lists** (`ImportListDefinition.Tags`) — tags to stamp onto every
  series that list adds (not a filter on the list itself).
- **Notifications** (`NotificationDefinition.Tags`, same pattern) — a
  notification only fires for series carrying a matching tag, or all series
  if untagged.
- **Auto-Tagging rules** (`AutoTag.Tags`) — the tags a matching rule
  **applies to a series** (§13), distinct from a `TagSpecification` condition
  that **checks whether a series already has a given tag** as one of the
  rule's match criteria — i.e. tags can be both an auto-tagging **output**
  and an auto-tagging **input condition**, enabling chained/derived tagging.
- **Calendar filtering** (§11) also consumes `Series.Tags` directly for
  ad hoc view filtering, independent of the provider-binding mechanisms above.

---

## 13. Auto-Tagging

Directory: `NzbDrone.Core/AutoTagging/` (6 files) +
`NzbDrone.Core/AutoTagging/Specifications/` (13 files).

- `AutoTag.cs` — `{ Name, Specifications: List<IAutoTaggingSpecification>,
  RemoveTagsAutomatically: bool, Tags: HashSet<int> }`. `Tags` here are the
  tags **applied to a series** when the rule matches.
- `AutoTaggingService.cs`, `GetTagChanges(Series series)` — the evaluation
  entry point (called during series refresh/edit):
  1. Sets `series.RootFolderPath` to the best-matching configured root folder
     for the series' actual path (so `RootFolderSpecification` has something
     concrete to compare against even if the series predates a root-folder
     rename).
  2. For each `AutoTag`, groups its specifications **by concrete
     specification type** (`SpecificationMatchesGroup.cs`) and evaluates each
     individually against the series, producing a
     `Dictionary<IAutoTaggingSpecification, bool>` per type-group.
  3. `SpecificationMatchesGroup.DidMatch` = **not** (any `Required` spec in
     the group evaluated false, **or** every spec in the group evaluated
     false). Equivalently: a group matches if at least one spec in it is true
     AND no required spec in it is false — i.e. specs of the *same type*
     behave as an **OR** group (e.g. "Genre is Comedy OR Genre is Drama"),
     but any spec flagged `Required` inside that OR group becomes a hard gate
     that must independently be true.
  4. An `AutoTag` overall matches only if **every** type-group matched
     (`specificationMatches.All(x => x.DidMatch)`, i.e. **AND** across
     different specification types — "Genre matches AND Status matches").
  5. On overall match: every tag in `AutoTag.Tags` not already on the series
     is queued into `AutoTaggingChanges.TagsToAdd`.
  6. On overall non-match, **only if** `RemoveTagsAutomatically` is set: every
     tag in `AutoTag.Tags` is queued into `TagsToRemove` (so a rule can be
     "sticky" — once applied, stays applied even if conditions later stop
     matching — unless the admin explicitly opts into automatic removal).
- Results cached (`ICached<Dictionary<int, AutoTag>>`, invalidated on any
  Insert/Update/Delete, which also republish `AutoTagsUpdatedEvent`).

### 13.1 Every specification type (13, alphabetical)

Base: `AutoTagSpecificationBase.cs` (implements the shared `Negate`
inversion — every spec supports "is" vs "is not" — and `Clone()` via
`MemberwiseClone`), interface `IAutoTagSpecification.cs` (`Order`,
`ImplementationName`, `Name`, `Negate`, `Required`, `Validate()`, `Clone()`,
`IsSatisfiedBy(Series)`).

| Spec | `ImplementationName` | Match condition |
|---|---|---|
| `GenreSpecification.cs` | `"Genre"` | `series.Genres` contains any of `Value` (case-insensitive list) |
| `MonitoredSpecification.cs` | `"Monitored"` | `series.Monitored` (no `Value` — boolean presence check via the `Negate` flag) |
| `NetworkSpecification.cs` | `"Network"` | `series.Network` equals (case-insensitive) any of `Value` |
| `OriginalCountrySpecification.cs` | `"Original Country"` | `series.OriginalCountry` equals (case-insensitive) any of `Value` |
| `OriginalLanguageSpecification.cs` | `"Original Language"` | `series.OriginalLanguage.Id == Value` |
| `QualityProfileSpecification.cs` | `"Quality Profile"` | `series.QualityProfileId == Value` |
| `RootFolderSpecification.cs` | `"Root Folder"` | `series.RootFolderPath` path-equals `Value` |
| `SeriesTypeSpecification.cs` | `"Series Type"` | `(int)series.SeriesType == Value` |
| `StatusSpecification.cs` | `"Status"` | `series.Status == (SeriesStatusType)Status` |
| `TagSpecification.cs` | `"Tag"` | `series.Tags.Contains(Value)` — this is the "input condition" half of tag/auto-tag interplay noted in §12 |
| `YearSpecification.cs` | `"Year"` | `Min <= series.Year <= Max` (range) |

(`IAutoTagSpecification.cs` and `AutoTagSpecificationBase.cs` are the
interface/base, not standalone rule types — 11 concrete rule types + 1
interface + 1 base = the 13 files in the directory, all accounted for.)

---

## 14. Release Profiles (must contain / must not contain / preferred-legacy)

Directory: `NzbDrone.Core/Profiles/Releases/` (8 files) +
`TermMatchers/` (3 files).

- `ReleaseProfile.cs`:
  ```csharp
  public class ReleaseProfile : ModelBase
  {
      public string Name { get; set; }
      public bool Enabled { get; set; } = true;
      public List<string> Required { get; set; }   // "must contain"
      public List<string> Ignored { get; set; }     // "must not contain"
      public bool AirDateRestriction { get; set; }
      public int AirDateGracePeriod { get; set; }
      public List<int> IndexerIds { get; set; }     // empty = all indexers
      public HashSet<int> Tags { get; set; }        // empty = all series
      public HashSet<int> ExcludedTags { get; set; }
  }
  ```
  There is **no `Preferred` field on the current model** — `Preferred` terms
  were part of the original "Restrictions" feature (migration
  `068_add_release_restrictions.cs` created a `Preferred` column) and were
  superseded when the feature was renamed/refactored into Release Profiles
  (`127_rename_release_profiles.cs`, which renamed the `Restrictions` table to
  `ReleaseProfiles` and added `IncludePreferredWhenRenaming`) and then
  further superseded by **Custom Formats** (migration `171_add_custom_formats.cs`)
  as the mechanism for weighted/preferred scoring. `ReleaseProfilePreferredComparer`
  (still present in `ReleaseProfile.cs`, sorting a `KeyValuePair<string,int>`
  descending by score) is legacy scaffolding kept for historical/migration
  compatibility, not part of the live decision path — **preferred-word scoring
  today is Custom Formats' job, not Release Profiles'**. Prismedia should treat
  "Required"/"Ignored" as the entire current surface of this feature and route
  any preferred/weighted-term concept through its Custom-Format-equivalent
  system instead of resurrecting a `Preferred` list here.
- `ReleaseProfileService.cs` — `EnabledForTags(tagIds, indexerId)` =
  `AllForTags(tagIds)` (tag-match-or-untagged, minus anything in
  `ExcludedTags` for those tags) filtered to `Enabled == true` and
  (`IndexerIds` contains this indexer OR `IndexerIds` is empty).
- `ReleaseProfileRepository.cs` — plain CRUD, no special query logic.
- **Term matching** (`TermMatcherService.cs` + `TermMatchers/`):
  - `ITermMatcher.cs` — `IsMatch(value)`, `MatchingTerm(value)`.
  - `PerlRegexFactory.cs` — if a term is wrapped in `/pattern/modifiers` (Perl
    regex literal syntax), parses it into a .NET `Regex` (supports `m`, `s`,
    `i`, `x`, `n` modifiers; unsupported modifier chars throw
    `ArgumentException`) — this is how a Required/Ignored term can be a full
    regex instead of a plain substring.
  - `RegexTermMatcher.cs` — wraps that regex for `IsMatch`/`MatchingTerm`.
  - `CaseInsensitiveTermMatcher.cs` — the default fallback for a plain string
    term: case-insensitive substring containment.
  - `TermMatcherService.GetMatcher(term)` caches the compiled matcher per
    literal term string for 24 hours (regex compilation is not cheap, and
    terms are typically reused across many release evaluations).
- **Enforcement** — two `IDownloadDecisionEngineSpecification`s outside this
  directory but directly consuming it:
  - `ReleaseRestrictionsSpecification.cs`
    (`NzbDrone.Core/DecisionEngine/Specifications/`): for a candidate
    release's title, gathers every enabled+tag+indexer-matching profile;
    if **any** profile has `Required` terms and the title matches **none** of
    them, reject (`DownloadRejectionReason.MustContainMissing`); if **any**
    profile has `Ignored` terms and the title matches **any** of them, reject
    (`DownloadRejectionReason.MustNotContainPresent`). Note this means
    Required terms are effectively **AND-ed across profiles** (must satisfy
    every applicable profile's required-terms clause independently) while a
    single profile's own `Required` list is itself an **OR** (matching any
    one required term satisfies that profile).
  - `AirDateSpecification.cs` (same directory): picks the "best" applicable
    profile (prioritizing `AirDateRestriction == true`, then longest grace
    period) and, if that profile restricts pre-air-date grabs, rejects any
    release published before `episode.AirDateUtc + AirDateGracePeriod days`
    for any of the release's episodes, with `DownloadRejectionReason.BeforeAirDate`
    (also rejects outright if an episode has no air date at all, since there's
    nothing to compare against).

Config surface: per-profile `Enabled`, `Required[]`, `Ignored[]`,
`AirDateRestriction`, `AirDateGracePeriod` (days), `IndexerIds[]`, `Tags[]`,
`ExcludedTags[]`.

---

## 15. Cross-cutting notes for Prismedia's implementation

- **Monitoring is three independent booleans (kind-item / season-equivalent /
  episode-equivalent) reconciled by one service**, not a single inherited
  flag — Prismedia's per-kind monitoring (series→season→episode for TV; the
  analogous parent/child shape for books/music) should centralize the
  "apply a monitor preset" logic the same way `EpisodeMonitoredService` does,
  rather than scattering season/episode toggling across call sites.
- **"Wanted" is two independently-queried, independently-paged views** (missing
  = no file + aired; cutoff unmet = has file but below profile cutoff), both
  defaulting to "monitored only," both excluding nothing from the DB view but
  excluding **actively queued** items only at *search-command* time, not at
  *display* time.
- **The scheduled-task list is intentionally short** — most "wanted" discovery
  happens via a continuous RSS-equivalent poll + event-driven targeted
  re-search (new episode aired, failed-download retry, add-time flags), not a
  dedicated recurring brute-force scan; the brute-force missing/cutoff search
  commands exist as user-addable scheduled tasks layered on the same generic
  command-scheduling primitive, not as hardcoded defaults.
- **The Command queue is the single serialization point for all background
  work** — structural-equality dedup, priority + FIFO ordering, and three
  interleaving rules (disk-access mutual exclusion, long-running vs.
  exclusive gating, exclusive-runs-alone) are all generic and kind-agnostic;
  this is a strong candidate to lift wholesale as Prismedia's own
  cross-kind job/command primitive rather than re-deriving per kind.
- **Blocklist scope is (parent item, specific child ids, exact release)**, and
  "blocklist and search again" is not a separate feature — it is
  blocklist-with-redownload-not-skipped, reusing the identical failure event
  pipeline as automatic failure detection. Prismedia should keep "blocklist"
  and "redownload" as two independently toggleable booleans on the same
  remove/fail action, exactly as Sonarr does, rather than a single combined
  action.
- **Delay profiles only gate non-user-invoked searches.** Any explicit,
  human-triggered search (interactive search, manual "search now" command)
  must bypass delay/quality-preference waiting entirely; only ambient
  discovery (RSS-equivalent sync, or a *scheduled* recurring search) respects
  the configured delay.
- **Health checks are event-driven first, polling second.** The 6-hour
  scheduled sweep is a backstop; the real-time signal comes from reacting to
  provider CRUD events, status-change events, and import success/failure
  events, with a startup grace period to avoid false alarms during boot.
  Prismedia's health-check registry should adopt the same
  `[CheckOn(EventType, Condition)]` declarative pattern rather than hand-wiring
  each check's triggers.
- **Tags are a pure many-to-many binding key with one universal rule**: empty
  tag set = unscoped/applies-to-all, non-empty = OR-match against the
  series'/item's own tag set. This single rule underlies indexer scoping,
  delay-profile selection, release-profile scoping (plus a separate hard
  exclusion set), import-list tagging-on-add, and notification scoping — it
  should be implemented once and reused, not reimplemented per consumer.
- **Release Profiles today are Required/Ignored only.** Any "preferred word"
  or weighted-scoring feature request should be routed to Prismedia's
  Custom-Format-equivalent (scored) system, matching Sonarr's own migration
  path away from a `Preferred` list on this entity.
