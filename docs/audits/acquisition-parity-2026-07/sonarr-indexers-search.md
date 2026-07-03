# Sonarr Indexer & Search System — Parity Map

Source root actually mapped: `/Users/pauldavis/Dev/_ARCHIVE/Sonarr/src` (the task's
`undefined/src` placeholder resolved to this on-disk Sonarr checkout — the only Sonarr
source tree present on the machine). All paths below are relative to that root unless
stated otherwise. Sonarr version: the `Sonarr.Api.V3`/`Sonarr.Api.V5` dual-API era
(2024-2025 vintage), .NET, C#.

This document maps every capability of Sonarr's indexer framework and
request→search→decision pipeline so it can be reproduced for Prismedia's
Sonarr/Prowlarr-replacement "Wanted"/acquisition vertical, generalized across media
kinds via plugins.

---

## 1. Indexer Framework

### 1.1 Core abstraction hierarchy

```
IProvider (generic Sonarr "pluggable service" contract)
  └─ IIndexer                                    Indexers/IIndexer.cs
       └─ IndexerBase<TSettings>                  Indexers/IndexerBase.cs
            └─ HttpIndexerBase<TSettings>         Indexers/HttpIndexerBase.cs
                 ├─ Newznab                        Indexers/Newznab/Newznab.cs
                 ├─ Torznab                        Indexers/Torznab/Torznab.cs
                 ├─ BroadcastheNet                 Indexers/BroadcastheNet/BroadcastheNet.cs
                 ├─ HDBits                         Indexers/HDBits/HDBits.cs
                 ├─ IPTorrents                     Indexers/IPTorrents/IPTorrents.cs
                 ├─ FileList                       Indexers/FileList/FileList.cs
                 ├─ Torrentleech                   Indexers/Torrentleech/Torrentleech.cs
                 ├─ Nyaa                           Indexers/Nyaa/Nyaa.cs
                 ├─ Fanzub                         Indexers/Fanzub/Fanzub.cs
                 └─ TorrentRssIndexer              Indexers/TorrentRss/TorrentRssIndexer.cs
```

**`IIndexer`** (`Indexers/IIndexer.cs`) is the domain contract every indexer
implementation must satisfy:
- `bool SupportsRss`, `bool SupportsSearch` — capability flags surfaced to the UI and
  used to gate scheduling/dispatch.
- `DownloadProtocol Protocol` — `Usenet` or `Torrent` (see §8).
- One `Fetch(...)` overload **per search-criteria type** (season, single-episode,
  daily-episode, daily-season, anime-episode, anime-season, special-episode) plus
  `FetchRecent()` for RSS. This is a closed, compile-time-checked dispatch surface —
  there is no generic "search(criteria)" method; each numbering scheme gets its own
  method so each indexer implementation can opt in/out and build a scheme-specific
  query.
- `HttpRequest GetDownloadRequest(string link)` — builds the authenticated download
  request for a release's `DownloadUrl` (adds API keys/passkeys as needed at grab
  time, not just at search time).

**`IndexerBase<TSettings>`** (`Indexers/IndexerBase.cs`) supplies:
- `Priority` (int, default `25` via `IndexerDefinition.DefaultPriority`) and
  `SeasonSearchMaximumSingleEpisodeAge` (int, days) — both persisted on the
  `IndexerDefinition`, not the settings blob, so they apply uniformly regardless of
  indexer type.
- `DefaultDefinitions` — the built-in "preset" list offered when adding an indexer of
  this implementation type (disabled by default; user must fill in URL/API key and
  enable).
- `CleanupReleases(releases)` — runs after every fetch: de-dupes by `Guid`
  (`DistinctBy`), and stamps every `ReleaseInfo` with `IndexerId`, `Indexer` (name),
  `DownloadProtocol`, `IndexerPriority`, `SeasonSearchMaximumSingleEpisodeAge`. It also
  backfills `Languages` from the indexer's `MultiLanguages` setting when the release
  title itself signals a multi-language release (`Parser.HasMultipleLanguages`) and the
  parser didn't already extract languages from indexer-specific XML attributes.
- `Test()` / abstract `Test(failures)` — synchronous wrapper around the indexer's
  async connectivity self-test, used by the "Test" button in indexer settings and by
  `IndexerFactory.Test` (records success/failure into indexer status on save).

**`HttpIndexerBase<TSettings>`** (`Indexers/HttpIndexerBase.cs`) is where the generic
HTTP fetch/paging/backoff machinery lives:
- `PageSize` (virtual, default `0` = unpaged) and `SupportsPaging => PageSize > 0`.
- `RateLimit` (virtual, default `2s` between requests to the *same* indexer — keyed by
  `Definition.Id` via `HttpRequest.RateLimitKey`).
- `MaxNumResultsPerQuery = 1000` (const) — safety cap per single fetch.
- `GetRequestGenerator()` / `GetParser()` — abstract factory methods each concrete
  indexer overrides to plug in its query-building and response-parsing strategy
  (strategy pattern; this is the seam a Prismedia plugin protocol should mirror).
- **`FetchReleases(pageableRequestChainSelector, isRecent)`** — the shared engine:
  1. Builds an `IndexerPageableRequestChain` (§1.4) from the generator.
  2. Walks **tiers** in order; **stops at the first tier that yields ANY releases**
     (fallback ladder — see §2.5). Within a tier, all pageable-request groups are
     fetched (not short-circuited).
  3. Within a pageable-request group, walks pages until either the page comes back
     non-full (`IsFullPage`, i.e. `page.Count < PageSize`) or `MaxNumResultsPerQuery`
     is hit.
  4. For RSS (`isRecent = true`) fetches specifically: tracks whether the fetched pages
     fully reconnect with `IndexerStatusService.GetLastRssSyncReleaseInfo` (the release
     seen at the end of the previous sync). If the oldest release on a page is older
     than the last-seen release, or the last-seen release's `DownloadUrl` appears on
     the page, the sync is considered "fully updated" and stops early. If paging runs
     out (≥ `MaxNumResultsPerQuery` and the oldest page item is >24h old) without
     reconnecting, it logs a **gap warning** ("rss sync didn't cover the period between
     X and Y UTC. Search may be required.") — this is a documented, expected
     degraded-mode behavior, not a bug.
  5. After a successful fetch, updates the indexer's last-RSS-sync release info
     (`IndexerStatusService.UpdateRssSyncStatus`) and calls
     `IndexerStatusService.RecordSuccess` (resets backoff escalation).
  6. Failure handling is by exception type (see §7) — every branch maps to either
     `RecordFailure` (escalates backoff) or `RecordConnectionFailure` (does not
     escalate, used for DNS/connect-level failures which are not necessarily the
     indexer's fault).
  7. Returns `CleanupReleases(releases)` even on partial/exception exit (whatever was
     accumulated before the exception is still returned/deduped).
- `IsValidRelease(release)` — drops releases with no `Title` or no `DownloadUrl`
  (logged at Trace, not surfaced as a rejection — this happens before the decision
  engine even sees the release).
- `FetchIndexerResponse` — applies the effective per-indexer `RateLimit`
  (`request.HttpRequest.RateLimit < RateLimit` → bump up to the indexer's minimum) and
  tags the HTTP request with `RateLimitKey = Definition.Id` so the shared HTTP client's
  rate limiter throttles per-indexer, not globally.
- `TestConnection()` — fetches the *first* request of the *first* tier of
  `GetRecentRequests()`; empty result → `"no results in configured categories"`
  validation failure; maps a long list of exception types to **localized, specific**
  validation messages (bad API key, CAPTCHA required/expired, unsupported feed,
  season/episode query not supported (HTTP 400 containing "not support the requested
  query"), 5xx / forbidden / unauthorized / timeout / DNS failure). This mapping is
  worth replicating 1:1 because indexer setup UX quality hinges on precise error
  attribution.

### 1.2 Torznab / Newznab implementations

Newznab and Torznab are **the same request generator** (`NewznabRequestGenerator`) and
**the same capability-detection code**, differentiated only by:
- `Protocol` (`Usenet` vs `Torrent`).
- Response parser (`NewznabRssParser` vs `TorznabRssParser`, both subclass a common RSS
  parsing base — Torznab's additionally subclasses `TorrentRssParser` for
  seeder/peer/magnet/infohash extraction).
- Settings type (`TorznabSettings : NewznabSettings` adds `MinimumSeeders`,
  `SeedCriteria`, `RejectBlocklistedTorrentHashesWhileGrabbing` — the torrent-only
  fields, at `FieldDefinition` indices 9-11, explicitly documented in a code comment in
  `NewznabSettings` to keep field-index numbering in sync between the two classes).

**Settings — `Indexers/Newznab/NewznabSettings.cs`** (configuration surface, shared by
Torznab):
| Field | Type | Default | Notes |
|---|---|---|---|
| `BaseUrl` | string (URL) | — | required, `ValidRootUrl` |
| `ApiPath` | string | `/api` | advanced, `ValidUrlBase("/api")` |
| `ApiKey` | string, `PrivacyLevel.ApiKey` | — | required only for a hardcoded allow-list of hostnames (`nzbs.org`, `nzb.life`, `dognzb.cr`, `nzbplanet.net`, `nzbid.org`, `nzbndx.com`, `nzbindex.in`) — otherwise optional (some newznab instances are keyless) |
| `Categories` | `int[]` | `[5030, 5040]` (SD, HD) | Newznab category numbers, see §9 |
| `AnimeCategories` | `int[]` | `[]` | separate category set used only for anime-flagged searches |
| `AnimeStandardFormatSearch` | bool | `false` | when true, anime searches ALSO try standard `SxxExx` queries in addition to absolute-number queries |
| `AdditionalParameters` | string | — | raw `&key=value` pairs appended to every query URL; validated by regex `(&.+?\=.+?)+` |
| `MultiLanguages` | `int[]` (language ids) | `[]` | advanced; see §1.1 CleanupReleases multi-language backfill |
| `FailDownloads` | `int[]` (`FailDownloads` enum) | `[]` | advanced; marks releases whose content should be treated as failed post-download (executables / "potentially dangerous" / user-defined extensions) — consumed by import-time content scanning, not by search |
| Validation: at least one of `Categories`/`AnimeCategories` must be non-empty. | | | |

**Torznab additions — `Indexers/Torznab/TorznabSettings.cs`**:
- `MinimumSeeders` (int, default `IndexerDefaults.MINIMUM_SEEDERS = 1`).
- `SeedCriteria` (`SeedCriteriaSettings`, see §7.5).
- `RejectBlocklistedTorrentHashesWhileGrabbing` (bool) — at grab time, skip a release
  if its info-hash is already in the blocklist (protects against re-grabbing a torrent
  that was previously grabbed+blocklisted under a different release title/guid).
- Torznab `Test()` additionally runs `JackettAll()` — a warning-level validation
  failure if the URL points at Jackett's aggregate `/torznab/all` endpoint (explicitly
  unsupported/discouraged because it can't be scoped to per-indexer capabilities or
  rate limits).

**Capabilities detection — `Indexers/Newznab/NewznabCapabilitiesProvider.cs`**
(`INewznabCapabilitiesProvider`):
- Calls `{BaseUrl}{ApiPath}?t=caps[&apikey=...]`, parses the `<caps>` XML response.
- Cached **7 days** per exact settings JSON (`ICached<NewznabCapabilities>` keyed by
  `indexerSettings.ToJson()` — i.e. cache invalidates automatically if the user changes
  any setting, not just on a timer).
- On any parse/connect exception, logs and **falls back to default capabilities**
  (`NewznabCapabilities` defaults: page size 100/100, `SupportedSearchParameters =
  ["q"]`, `SupportedTvSearchParameters = ["q","rid","season","ep"]`,
  `SupportsAggregateIdSearch = false`, both text search engines `"sphinx"`) rather than
  failing the whole indexer — "using the defaults instead till Sonarr restarts" is
  logged at Error. This graceful-degradation behavior is important to replicate:
  a caps-endpoint outage should not disable search entirely.
- Parsed fields: `DefaultPageSize`/`MaxPageSize` (from `<limits default max>`),
  `SupportedSearchParameters` (from `<search available supportedParams>`),
  `SupportedTvSearchParameters` + `SupportsAggregateIdSearch=true` (from `<tv-search
  available supportedParams>` — aggregate-ID search is inferred purely from the
  presence of `supportedParams`, no separate capability flag exists on the wire),
  `TextSearchEngine`/`TvTextSearchEngine` (`searchEngine` attribute, values like
  `"sphinx"` or `"raw"` — `"raw"` changes whether the request generator sends the raw
  scene title or the cleaned/sanitized title, see §2.1), and the full `<categories>`
  tree (id/name/description + `<subcat>` children) used to populate the category
  picker UI (`RequestAction("newznabCategories")` on both Newznab and Torznab).
- **`Newznab.PageSize`** (and Torznab's) is computed live from capabilities:
  `Math.Min(100, Math.Max(DefaultPageSize, MaxPageSize))`, falling back to `100` on any
  exception. This means an indexer's effective page size can silently change if its
  caps response changes.
- Indexer `Test()` additionally calls `TestCapabilities()`: passes only if
  capabilities advertise basic `q` search OR (`tvdbid`/`rid`/`q` present) AND
  (`season` AND `ep` both present) — i.e. an indexer must support at least one
  ID-based or generic-query TV search combined with season+episode parameters to be
  usable at all.

**Request generation — `Indexers/Newznab/NewznabRequestGenerator.cs`** — see §2 for the
full per-search-type breakdown; this class is shared verbatim by Torznab.

**Response parsing:**
- `NewznabRssParser` (`Indexers/Newznab/NewznabRssParser.cs`): namespace
  `http://www.newznab.com/DTD/2010/feeds/attributes/`. Extracts `tvdbid`, `rageid`
  (→ `TvRageId`), `imdb` (→ `ImdbId`, formatted `tt{7-digit}`), `usenetdate` (preferred
  over RSS `pubDate` if present), `language`/multi-`language` elements, `size`
  attribute (falls back to enclosure length), and indexer flags: `prematch`/
  `haspretime` = `1` → `Scene`; `nuked` = `1` → `Nuked`; non-empty `subs` → `Subtitles`.
  `CheckError` throws `ApiKeyException` for XML `<error code="1xx">`, or when the error
  message mentions "apikey"/"Missing parameter" and the request URL had no `apikey=`
  param; throws `RequestLimitReachedException` for "Request limit reached"; otherwise
  throws `NewznabException`. `PostProcess` warns (does not throw) if the feed's
  enclosure mime types don't match the expected `application/x-nzb` — and specifically
  detects "you configured a Torznab feed as Newznab" by checking for the torrent mime
  types and returns `false` (drops all releases from that response) in that case.
- `TorznabRssParser` (`Indexers/Torznab/TorznabRssParser.cs`, extends
  `TorrentRssParser`): namespace `http://torznab.com/schemas/2015/feed`. Same
  tvdbid/rageid/imdb extraction (via `torznab:attr`), plus torrent-specific
  `infohash`, `magneturl`, `seeders`, `peers` (falls back to `seeders+leechers` sum,
  then to generic RSS parsing), and **indexer flags derived from freeleech
  percentages**: `downloadvolumefactor` `0.5`→`Halfleech`, `0.75`→`Freeleech25`,
  `0.25`→`Freeleech75` *(note: Sonarr's naming here is "how much you pay", not "how
  much is free" — `Freeleech25` fires at `downloadvolumefactor=0.75`, i.e. you pay 75%
  cost → flag says "25% free"; verify semantics carefully when reproducing)*,
  `0.0`→`Freeleech`; `uploadvolumefactor` `2.0`→`DoubleUpload`; `tag` values
  `"internal"`→`Internal`, `"scene"`→`Scene`. Same reciprocal Newznab/Torznab
  mismatch-warning behavior in `PostProcess`.

### 1.3 Other Torznab-family / RSS-based torrent indexers

All of these reuse `HttpIndexerBase<TSettings>` + a shared `TorrentRssParser`
(`Indexers/TorrentRssParser.cs`) configured with per-site quirks, rather than writing a
bespoke parser each time — the parser exposes toggles (`CalculatePeersAsSum`,
`InfoHashElementName`, `ParseSeedersInDescription`, `PeersElementName`,
`SeedsElementName`, `SizeElementName`, `MagnetElementName`, `UseGuidInfoUrl`,
`ParseSizeInDescription`) that each indexer sets to match its RSS dialect:

| Indexer | File | Search? | Notable settings |
|---|---|---|---|
| `BroadcastheNet` | `Indexers/BroadcastheNet/*.cs` | Yes (JSON-RPC, not RSS) | Uses a completely different transport: `getTorrents` JSON-RPC call (`BroadcastheNetTorrentQuery`: `Id`, `Age`, `Category`, `Name`, `Tvdb`, `Tvrage`). `PageSize=100`, `RateLimit=5s`. Own `BroadcastheNetParser`/`BroadcastheNetTorrent(s)` models. `MinimumSeeders`, `SeedCriteria` (with elevated defaults: `SeedRatio≥1.0`, `SeedTime≥24h`, `SeasonPackSeedTime≥5×24h` — passed as validator minimums), `RejectBlocklistedTorrentHashesWhileGrabbing`. RSS uses a two-tier chain: last-seen-torrent-id window (`id >= last-100`) then a 24h age-window fallback tier. |
| `HDBits` | `Indexers/HDBits/*.cs` | Yes | `PageSize=100`. Own `HDBitsApi`/`HDBitsParser`. Extra settings: `Username`, `Categories` (`HdBitsCategory` enum: Movie/TV/Documentary/Music/Sport/Audio/XXX/MiscDemo — defaults to `[Tv, Documentary]`), `Codecs` (`HdBitsCodec`: H.264/MPEG-2/VC-1/XviD/HEVC), `Mediums` (`HdBitsMedium`: Bluray/Encode/Capture/Remux/WEB-DL). Standard `MinimumSeeders`/`SeedCriteria`/`RejectBlocklistedTorrentHashesWhileGrabbing`. |
| `IPTorrents` | `Indexers/IPTorrents/*.cs` | **No** (`SupportsSearch=false`, RSS-only) | `PageSize=0` (unpaged single feed URL). Parser: `ParseSizeInDescription=true`. Settings validate the feed URL contains `rss?` and nudge the user toward the `;download` direct-download variant. |
| `FileList` | `Indexers/FileList/*.cs` | Yes | Own `FileListRequestGenerator`/`FileListParser`. `Username`+`Passkey` auth. `Categories`/`AnimeCategories` from `FileListCategories` enum (Anime, Animation, TV_4K, TV_HD, TV_SD, Sport, RoDubbed) — a **site-specific category enum**, not Newznab numbers. |
| `Torrentleech` | `Indexers/Torrentleech/*.cs` | **No** (RSS-only) | `PageSize=0`. Parser: `UseGuidInfoUrl=true`, `ParseSeedersInDescription=true`. `ApiKey`-based feed URL. |
| `Nyaa` | `Indexers/Nyaa/*.cs` | Yes (inherited default `SupportsSearch=true`, no override) | Parser: `UseGuidInfoUrl=true`, `SizeElementName="size"`, `InfoHashElementName="infoHash"`, `PeersElementName="leechers"`, `CalculatePeersAsSum=true`, `SeedsElementName="seeders"`. `AdditionalParameters` default `"&cats=1_0&filter=1"`. `AnimeStandardFormatSearch` toggle (anime-focused tracker). Own `NyaaRequestGenerator`. |
| `Fanzub` | `Indexers/Fanzub/*.cs` | **No** (default `SupportsSearch` from base is used; RSS-only, `Protocol=Usenet`) | Uses the *generic* `RssParser` (not `TorrentRssParser`) with `UseEnclosureUrl=true, UseEnclosureLength=true` — a plain enclosure-based Usenet RSS feed, anime-only site. `AnimeStandardFormatSearch` toggle. |
| `TorrentRssIndexer` | `Indexers/TorrentRss/TorrentRssIndexer.cs` | **No** (RSS-only, generic) | The escape hatch for arbitrary/unknown torrent RSS feeds. Parser is chosen dynamically via `ITorrentRssParserFactory`/`TorrentRssParserFactory`, which in turn uses **`TorrentRssSettingsDetector`** (`Indexers/TorrentRss/TorrentRssSettingsDetector.cs`) to auto-probe the feed on save: (1) checks for the EZTV/ezrss XML namespace or DOCTYPE (`http://xmlns.ezrss.it/0.1/`) and uses `EzrssTorrentRssParser` if matched; (2) otherwise tries the generic `TorrentRssParser` and iteratively toggles `UseEnclosureUrl`/`UseEnclosureLength`/`ParseSeedersInDescription`/`SizeElementName` (`"size"` then `"Size"`)/`ParseSizeInDescription` until it finds a combination that yields valid, correctly-sized releases (validated against a `2MB` `ValidSizeThreshold` — sizes below that are treated as "probably didn't parse the size field correctly" unless `AllowZeroSize` is set). This detector logic is a strong candidate for direct reuse/adaptation: it is Sonarr's answer to "how do we support an indexer we don't have a driver for." |

`EzrssTorrentRssParser` (`Indexers/EzrssTorrentRssParser.cs`) — a `TorrentRssParser`
subclass hardcoded to the ezrss.it namespace's element names for size/seeders/infohash.

### 1.4 Query plumbing types

- **`IndexerRequest`** (`Indexers/IndexerRequest.cs`) — wraps one `HttpRequest` + its
  `HttpUri`.
- **`IIndexerRequestGenerator`** (`Indexers/IIndexerRequestGenerator.cs`) — the
  strategy interface: `GetRecentRequests()` plus one `GetSearchRequests(...)` overload
  per search-criteria type, each returning an `IndexerPageableRequestChain`.
- **`IndexerPageableRequest`** (`Indexers/IndexerPageableRequest.cs`) — one *paged
  sequence* of `IndexerRequest` (e.g., "page 0, 1, 2, ... of query X").
- **`IndexerPageableRequestChain`** (`Indexers/IndexerPageableRequestChain.cs`) — an
  ordered list of **tiers**, each tier being a list of `IndexerPageableRequest`. `Add()`
  appends to the current (last) tier; `AddTier()` starts a new tier (no-op if the
  current tier is still empty, to avoid empty leading tiers); `AddTier(request)` is
  sugar for `AddTier(); Add(request)`. `GetTier(i)` / `GetAllTiers()` / `Tiers` (count)
  are consumed by `HttpIndexerBase.FetchReleases`. **This tiering is the mechanism for
  fallback-ladder search** (§2.5): the fetch loop stops at the first tier that returns
  ≥1 release.
- **`IndexerResponse`** (`Indexers/IndexerResponse.cs`) — pairs an `IndexerRequest`
  with the raw `HttpResponse`.
- **`IProcessIndexerResponse`** / **`IParseIndexerResponse`** — the parser strategy
  interface (`ParseResponse(IndexerResponse) -> IList<ReleaseInfo>`).
- **`RssEnclosure`** (`Indexers/RssEnclosure.cs`) — `Url`/`Type`/`Length` from an RSS
  `<enclosure>`.
- **`XElementExtensions`** / **`XmlCleaner`** — low-level XML robustness helpers
  (`XmlCleaner.ReplaceEntities`/`ReplaceUnicode` strip malformed/nonstandard entities
  and unicode-escape artifacts before `XDocument.Parse`, because real-world indexer
  feeds are frequently not strictly well-formed).

---

## 2. Search Orchestration

### 2.1 `SearchCriteriaBase` hierarchy

Base class: **`IndexerSearch/Definitions/SearchCriteriaBase.cs`**
- `Series` (the target series), `SceneTitles` (list of alternate/scene titles to
  query), `Episodes` (episodes this search is for), `SearchMode` (flags:
  `Default=0`, `SearchID=1`, `SearchTitle=2` — from `DataAugmentation/Scene/SearchMode.cs`),
  `MonitoredEpisodesOnly`, `UserInvokedSearch`, `InteractiveSearch`.
- `AllSceneTitles` = `SceneTitles ∪ CleanSceneTitles` (raw + sanitized, deduped).
- `CleanSceneTitles` = each `SceneTitles` entry run through `GetCleanSceneTitle`.
- **`GetCleanSceneTitle(title)`** — the canonical query-sanitization routine: strips a
  leading `"the "` (case-insensitive), replaces `&` with `and`, strips apostrophes/
  smart-quotes/backticks (regex `['.`´‘’]`), replaces all other
  non-word characters with `+` (regex `[\W]`), collapses repeated `+`, strips
  diacritics, trims leading/trailing `+`/space. This is the exact function that
  produces newznab `q=`/`title=` query terms — replicate it precisely, since indexer
  matching quality depends on it.

Concrete criteria (`IndexerSearch/Definitions/*.cs`), each just adding the fields its
`ToString()` needs for logging:
| Class | Extra fields | Used for |
|---|---|---|
| `SingleEpisodeSearchCriteria` | `SeasonNumber`, `EpisodeNumber` | standard S01E02 |
| `SeasonSearchCriteria` | `SeasonNumber` | season-pack search |
| `DailyEpisodeSearchCriteria` | `AirDate` (DateTime) | daily/talk shows, single episode |
| `DailySeasonSearchCriteria` | `Year` | daily shows, whole-year batch |
| `AnimeEpisodeSearchCriteria` | `AbsoluteEpisodeNumber`, `EpisodeNumber`, `SeasonNumber`, `IsSeasonSearch` | anime absolute numbering |
| `AnimeSeasonSearchCriteria` | `SeasonNumber` | anime season pack |
| `SpecialEpisodeSearchCriteria` | `EpisodeQueryTitles` (string[]) | season-0 specials, searched by concatenated title strings, not numbering |

`SceneEpisodeMapping` / `SceneSeasonMapping`
(`IndexerSearch/Definitions/SceneEpisodeMapping.cs`,`SceneSeasonMapping.cs`) are
transient value objects (not persisted) produced by `ReleaseSearchService` to carry
"this specific alternate title + search mode applies to these specific
season/episode numbers" groupings — see §2.3.

### 2.2 `ISearchForReleases` / `ReleaseSearchService` — the orchestrator

**File:** `IndexerSearch/ReleaseSearchService.cs`. This is the single entry point all
search commands funnel through (`EpisodeSearch`, `SeasonSearch` — two overloads).

Dispatch by series/episode shape (`EpisodeSearch`):
1. **Daily series** (`SeriesTypes.Daily`) → requires `episode.AirDate` to be set
   (throws `SearchFailedException("Air date is missing")` otherwise, surfaced to the
   UI as a 400) → `SearchDaily`.
2. **Anime series** (`SeriesTypes.Anime`) → if the episode has no season/absolute
   number info at all (season 0, no scene/absolute numbers) it's treated as a special
   (`SearchSpecial`); otherwise `SearchAnime`.
3. **Season 0** (any non-anime series) → always `SearchSpecial`, regardless of series
   type, because season 0 = specials by convention.
4. Otherwise → `SearchSingle` (standard S/E).

`SeasonSearch` dispatches similarly by series type
(`SearchAnimeSeason`/`SearchDailySeason`/generic mapping-based season search), and for
the generic case additionally splits episodes into **scene-season groups** (via
`GetSceneSeasonMappings`) so that a single "season search" command can produce
multiple underlying per-scene-mapping season or single-episode queries when scene
numbering diverges from TVDB numbering for only part of a season.

Every `Search*` method funnels into `Dispatch(searchAction, criteriaBase)`:
1. Selects indexer pool: `InteractiveSearchEnabled()` if
   `criteriaBase.InteractiveSearch`, else `AutomaticSearchEnabled()`
   (`IndexerFactory`, §6) — both already filtered for blocked/backed-off indexers.
2. **Tag filtering**: an indexer with tags is only queried if it shares at least one
   tag with the series (`indexer.Definition.Tags.Intersect(series.Tags)`); indexers
   with no tags apply to every series. (This is duplicated as a hard reject —
   `IndexerTagSpecification`, §5 — for defense in depth against releases that slip
   through from a differently-tagged indexer via RSS/push.)
3. Fans out `searchAction(indexer)` to **every** selected indexer **concurrently**
   (`Task.WhenAll`), catching and logging per-indexer exceptions individually so one
   bad indexer never fails the whole search.
4. Stamps `Episode.LastSearchTime = now` on every episode in the criteria — this
   powers "search oldest-first" ordering in bulk search (`EpisodeSearchService`,
   §2.4) and is persisted regardless of whether any results were found, as long as at
   least one indexer was actually queried.
5. Hands all raw `ReleaseInfo` results to `IMakeDownloadDecision.GetSearchDecision`
   (the decision engine, §4) together with the originating `SearchCriteriaBase` (so
   decision specs can tell "was this release requested" vs "just seen in RSS").
6. **`DeDupeDecisions`**: groups by `Release.Guid`, keeps the decision with the fewest
   rejections, tie-broken by lowest `IndexerPriority` number... **actually by
   `.ThenBy(IndexerPriority)`**, i.e. lower priority-number wins ties (priority `1` >
   priority `25` in this ordering — verify against your priority-number convention;
   Sonarr's indexer `Priority` field is documented as "1 = highest priority" so lower
   numbers should sort first, which is what `ThenBy` (ascending) does here).

### 2.3 Scene mapping resolution (`GetSceneEpisodeMappings` / `GetSceneSeasonMappings`)

This is the mechanism that turns "one TVDB episode" into "N queries with N alternate
titles, potentially against N different season/episode numbers," and is the most
intricate piece of the whole search stack. Per episode, for every applicable
`SceneMapping` row (`DataAugmentation/Scene/SceneMapping.cs`, populated from XEM + the
Sonarr metadata service, §3):

- A mapping's `SceneOrigin` of `"tvdb"`/`"unknown:tvdb"` means **numbering should be
  taken from the TVDB episode itself**, not translated — i.e. this mapping supplies an
  *alternate title* only, not a numbering remap.
- Otherwise, mappings translate `episode.SceneSeasonNumber`/`SceneEpisodeNumber` (as
  already resolved by the XEM/scene-numbering pass, §3.3) into the season/episode
  numbers to put in the actual search query.
- A mapping only "applies" if its own season-number constraints
  (`SceneSeasonNumber`/`SeasonNumber`) match the episode's season — mappings can be
  scoped to a specific season or apply to the whole series (`-1`/null sentinel via
  `NonNegative()`).
- If an explicit mapping exists whose `SearchTerm` exactly equals the series title
  (with no filter regex), the **implicit global mapping is suppressed**
  (`includeGlobal = false`) — otherwise the series' own title is always yielded as one
  more implicit "mapping" so the primary title is never lost even if all scene
  mappings are for alternate names.
- `SearchMode` per mapping defaults to `SearchTitle` (do a literal `title=` search
  instead of a `q=` query) specifically when the mapping has an explicit
  scene-season-number remap AND the clean series title differs from the clean mapping
  search term — the rationale (per inline comment) is that when numbering diverges,
  indexers may not have properly indexed the alternate-numbered release under the
  canonical series metadata, so a literal title search is more likely to surface it
  than a metadata-id search.
- Multiple mappings that resolve to the *same* `(SeasonNumber, EpisodeNumber,
  SearchMode)` tuple are merged (their `SceneTitles` lists concatenated + deduped
  case-insensitively) rather than issuing duplicate query batches.

`GetSceneSeasonMappings` does the season-level equivalent: groups the input episodes
by `(SceneSeasonNumber ?? SeasonNumber) * 100000 + SeasonNumber` (a composite key that
disambiguates "scene season 1 mapped from TVDB season 2" from "scene season 1 mapped
from TVDB season 1"), resolves episode-level mappings for the first episode of each
group as a representative, and merges into `SceneSeasonMapping` objects keyed by
`(SeasonNumber, SearchMode)`.

### 2.4 Numbering-scheme-specific query construction

**Standard (`SearchSingle`)**: one `SingleEpisodeSearchCriteria` per resolved scene
mapping; `SeasonNumber`/`EpisodeNumber` come straight from the mapping.

**Daily (`SearchDaily`)**: parses `episode.AirDate` (format constant
`Episode.AIR_DATE_FORMAT`) into a `DateTime`, builds one `DailyEpisodeSearchCriteria`.
`SearchDailySeason` groups episodes by air-date **year**; if a year has >1 episode it
issues a `DailySeasonSearchCriteria` (whole-year batch) for that year, otherwise falls
back to a single `SearchDaily` call — i.e. daily season search is really "one query
per year, unless there's only one episode that year."

**Anime (`SearchAnime`)**: builds `AnimeEpisodeSearchCriteria` with
`SeasonNumber`/`EpisodeNumber` preferring scene numbers
(`episode.SceneSeasonNumber ?? episode.SeasonNumber`, similarly for episode number)
and `AbsoluteEpisodeNumber` preferring `SceneAbsoluteEpisodeNumber ??
AbsoluteEpisodeNumber ?? 0`. `SearchAnimeSeason` restricts to episodes that (a) pass
the monitored/interactive gate and (b) have **already aired**
(`AirDateUtc.Before(now)`), groups them into scene-season buckets via
`GetSceneSeasonMappings`, issues one season-level query per distinct scene season
**plus** a full per-episode `SearchAnime(..., isSeasonSearch: true)` call for every
episode in the set (anime season search = season query ∪ individual absolute-number
episode queries, not season query alone — because many anime releases only exist as
individual episodes even within an aired season).

**Specials (`SearchSpecial`)**: builds `SpecialEpisodeSearchCriteria` whose
`EpisodeQueryTitles` are the cross-product of `CleanSceneTitles × episode.Title`
(`"{cleanSeriesOrAltTitle} {cleanEpisodeTitle}"`), deduped case-insensitively, and
restricted to episodes with a non-blank title that also pass the monitored/interactive
gate. **In addition** to the title-based queries, it recurses into `SearchSingle` for
every one of those episodes (subject to the same monitored gate) — so specials search
by both "series + episode title" text AND by season/episode numbering
simultaneously, since specials are notoriously inconsistently numbered/labeled across
indexers.

### 2.5 Newznab/Torznab query permutations & fallback ladder

**File:** `Indexers/Newznab/NewznabRequestGenerator.cs` — this is the concrete
"permutations and fallback ladder" logic for the Torznab/Newznab family (§1.2), gated
entirely by capability booleans resolved live from `INewznabCapabilitiesProvider`
(§1.2): `SupportsSearch` (`q` in `SupportedSearchParameters`, used only for anime
`t=search`), `SupportsTvQuerySearch`/`SupportsTvTitleSearch` (`q`/`title` in
`SupportedTvSearchParameters`, combined as `SupportsTvTextSearches`),
`SupportsTvdbSearch`/`SupportsImdbSearch`/`SupportsTvRageSearch`/`SupportsTvMazeSearch`/
`SupportsTmdbSearch` (combined as `SupportsTvIdSearches`),
`SupportsSeasonSearch`/`SupportsEpisodeSearch` (`season` alone, vs `season`+`ep`
together), `SupportsAggregatedIdSearch` (server advertised `supportedParams` on
`tv-search`, meaning multiple id params can be combined in one request rather than
needing separate fallback requests per id type).

For **standard episode/season/daily searches** (`SingleEpisodeSearchCriteria`,
`SeasonSearchCriteria`, `DailyEpisodeSearchCriteria`, `DailySeasonSearchCriteria` — all
four follow the identical shape):
1. Hard gate: if the indexer doesn't support `season`(+`ep` for episode-level)
   parameters, or supports neither text nor id search at all, **no requests are
   generated** (logged at Debug, not an error).
2. **Tier 0** (built by `AddTvIdPageableRequests` then `AddTitlePageableRequests`,
   both appending into the *same* first tier):
   - If `SearchMode` includes `SearchID` (or is `Default`): id-based request(s). If
     the indexer supports aggregated ID search AND has ≥1 usable id, **one request**
     combining all available ids (`tvdbid`+`imdbid`+`rid`+`tvmazeid`+`tmdbid`, in that
     priority order, `&`-joined) is issued. Otherwise, **separate fallback requests**
     are issued in strict priority order — only the *first* available id type is used
     (tvdb → imdb → tvrage → tvmaze → tmdb), i.e. non-aggregated indexers get exactly
     one id-based request, using the highest-priority id the series has.
   - If `SearchMode` includes `SearchTitle`: **one request per scene title** using the
     literal `title=` parameter (if supported) or `q=` (if not) — see below for which
     title set is used.
3. **`AddTier()`** — start tier 1 (only materializes if tier 0 actually got any
   requests added).
4. **Tier 1**: only populated `if (SearchMode == SearchMode.Default)` — this is the
   fallback tier, a title-search pass using `AddTitlePageableRequests` (same as the
   `SearchTitle` branch above). Because `HttpIndexerBase.FetchReleases` stops at the
   first tier with results, **this tier only actually fires over the network if tier 0
   returned zero releases** — it's specifically the "id search found nothing, retry by
   title" fallback for the default (unmapped) search mode. Mapped modes
   (`SearchID`/`SearchTitle` explicitly set by scene mapping resolution) skip this
   fallback because they already issued the exact request type the mapping called
   for.
5. **`AddTitlePageableRequests`** title-set selection: if `title=` param supported,
   issues one request per **raw** `searchCriteria.SceneTitles` entry (URL-encoded,
   no cleaning). If only `q=` supported, uses either `AllSceneTitles`
   (raw+clean, deduped) or just `CleanSceneTitles` depending on whether
   `TvTextSearchEngine == "raw"` (raw engines get the uncleaned titles too, since they
   don't need `+`-joined sanitized terms).

For **anime episode search** (`AnimeEpisodeSearchCriteria`), when `SupportsSearch`:
- Always issues an id-based request combining
  `&q={absoluteEpisodeNumber:00}` (zero-padded 2-digit) with tvdb/imdb/etc IDs via
  `AddTvIdPageableRequests`.
- If `Settings.AnimeStandardFormatSearch` is enabled AND the episode has a
  season>0/episode>0 (i.e., it also has conventional numbering) AND the indexer
  supports episode search, **additionally** issues an id-based `season=`/`ep=` request
  — anime + standard numbering searched simultaneously, not either/or.
- For every scene/query title (raw if `TextSearchEngine=="raw"` else cleaned): issues
  a `t=search&q={title}+{absoluteEpisode:00}` request, and — again gated by
  `AnimeStandardFormatSearch` + episode-search support — an additional
  `t=tvsearch&q={title}&season=..&ep=..` request. All of these land in the single
  default tier (anime request generation does not use the tiered fallback pattern the
  standard path does — every permutation is issued unconditionally in one batch).

For **anime season search** (`AnimeSeasonSearchCriteria`): only fires if
`AnimeStandardFormatSearch` is on AND `SeasonNumber > 0`; issues an id-based
`season=` request plus one `t=tvsearch&q={title}&season=..` request per query title.

For **special-episode search** (`SpecialEpisodeSearchCriteria`): issues one
`t=search&q={title}` request per `EpisodeQueryTitles` entry (categories chosen via
`GetSearchCategories`, which routes to `AnimeCategories` if the series type is Anime
else `Categories`) — no id-based fallback at all, purely title/text search, since
specials rarely have reliable per-episode external ids.

**Category selection** is per-request: `GetSearchCategories` picks `AnimeCategories`
vs `Categories` based on `Series.SeriesType == Anime`; standard/daily searches always
use `Settings.Categories` directly (season/episode-level anime distinction is handled
in the anime-specific methods instead).

**Pagination**: `GetPagedRequests(maxPages=30, categories, searchType, parameters)`
builds `{BaseUrl}{ApiPath}?t={type}&cat={csv-categories}&extended=1{AdditionalParameters}[&apikey=...]{parameters}`.
If `PageSize == 0` it yields exactly one request (unpaged); otherwise yields up to
`maxPages` (30) requests with `&offset={page*PageSize}&limit={PageSize}` — actual
fetch count is bounded by `HttpIndexerBase.FetchReleases`'s "stop on non-full page or
1000-result cap" logic, not by issuing all 30 unconditionally.

**Season-number quirk**: `NewznabifySeasonNumber(n)` returns `"00"` for season 0
instead of `"0"` — a documented workaround (code comment) for NNTmux-family indexers
that treat a literal `season=0` as null/missing.

### 2.6 `SearchDefinitionBase`-equivalent commands (public search API surface)

Commands (all under `IndexerSearch/*.cs`, dispatched via the generic
`IExecute<TCommand>` command bus):
- **`EpisodeSearchCommand`** (`EpisodeSearchCommand.cs`) — search one or more specific
  `EpisodeIds`.
- **`SeasonSearchCommand`** (`SeasonSearchCommand.cs`) — search one series+season.
- **`SeriesSearchCommand`** (dispatched by `SeriesSearchService.cs`) — search a whole
  series: if no seasons are monitored, falls back to per-episode search of every
  monitored, aired, fileless episode; otherwise iterates monitored seasons and issues
  one `SeasonSearch` per season, with `missingOnly = !profile.UpgradeAllowed`
  (i.e. if the quality profile disallows upgrades, season search is restricted to only
  episodes without a file at all — no point searching for upgrades that aren't
  allowed).
- **`MissingEpisodeSearchCommand`** — bulk search across the whole library, filtered
  by `Monitored`, optional `SeriesId`/`SeriesIds`/`QualityProfileIds`/`SeriesType`,
  excluding episodes already in the download queue. Delegates to
  `EpisodeSearchService.SearchForBulkEpisodes`.
- **`CutoffUnmetEpisodeSearchCommand`** — same shape but selects episodes whose
  current file doesn't meet the quality-profile cutoff
  (`IEpisodeCutoffService.EpisodesWhereCutoffUnmet`), for automatic upgrade searching.
- **`ISearchForReleases`** (`ReleaseSearchService.cs`, §2.2) is the actual
  `SearchDefinitionBase`-equivalent service interface all four commands route through.

**`EpisodeSearchService.SearchForBulkEpisodes`** (`IndexerSearch/EpisodeSearchService.cs`)
is the batching layer for the two bulk commands: groups the input episode list by
`(SeriesId, SeasonNumber)` into `EpisodeSearchGroup`s
(`IndexerSearch/EpisodeSearchGroup.cs`), **orders groups by the earliest
`LastSearchTime` (nulls-as-`DateTime.MinValue`) across the group's episodes** — i.e.
least-recently-searched episodes/seasons are searched first, a fairness mechanism so
one series doesn't starve others of search slots — then for each group issues either a
`SeasonSearch` (if >1 episode in the group) or a single-`EpisodeSearch` (if exactly 1),
processing/grabbing decisions incrementally per group rather than batching all
searches before any grabs.

---

## 3. Scene Naming / Numbering Augmentation (`DataAugmentation`)

### 3.1 `SceneMapping` (`DataAugmentation/Scene/SceneMapping.cs`)

The persisted row shape: `MappingId` (stable external identity used for diffing
inserts/updates/deletes on refresh), `Title`, `ParseTerm` (=`Title.CleanSeriesTitle()`,
used as the reverse-lookup cache key), `SearchTerm` (the alternate title actually put
into search queries — JSON property `searchTitle`), `TvdbId`, `SeasonNumber` (JSON
`season`), `SceneSeasonNumber`, `SceneOrigin` (free-text provenance tag, e.g. `"tvdb"`,
`"unknown:tvdb"`, `"mixed"`, `"unknown:<type>"`), `SearchMode` (nullable override),
`Comment`, `FilterRegex` (optional regex that must match the *simplified* release
title for this mapping to apply — used to disambiguate multiple candidate mappings for
the same title string), `Type` (the providing service's class name, e.g. `"XemService"`).

### 3.2 `SceneMappingService` (`DataAugmentation/Scene/SceneMappingService.cs`)

- Aggregates **all registered `ISceneMappingProvider`** implementations (currently just
  `XemService`, §3.3 — the interface is explicitly pluggable, i.e. Sonarr already
  models "multiple scene-mapping sources" as a provider list, a natural seam for a
  Prismedia plugin to add its own mapping source).
- On refresh (`UpdateMappings`, triggered by `UpdateSceneMappingCommand`, a manual
  series-refresh with `ManualTrigger=true` when the in-memory cache is >1 minute
  stale, or the first `SeriesAdded`/`SeriesImported` event after startup): for each
  provider, fetches its mappings, discards any with blank `Title`/`SearchTerm`,
  computes `ParseTerm`/`Type`, and does an **id-based diff** against existing rows of
  that provider type (`MappingId` as the join key) to compute inserts/updates/deletes
  — so stale mappings from a provider are pruned, not just appended.
- Two in-memory caches, rebuilt together: `_getTvdbIdCache` (keyed by
  `ParseTerm` → mappings, used for release-title → series resolution) and
  `_findByTvdbIdCache` (keyed by `TvdbId` → mappings, used for series → alternate
  titles).
- **`FindSceneMapping(seriesTitle, releaseTitle, sceneSeasonNumber)`** — the reverse
  lookup used by the decision engine to resolve "unknown series" release titles
  against a known alias: looks up candidates by clean series title, filters by
  `FilterRegex` match against `Parser.SimplifyTitle(releaseTitle)` when any candidate
  has a regex (regex-having candidates take priority over regex-less ones if any
  regex matches), then filters by season-number applicability
  (`FilterSceneMappings(candidates, sceneSeasonNumber)` — mappings with **both**
  `SceneSeasonNumber` and `SeasonNumber` set are treated as season-scoped and must have
  `SceneSeasonNumber <= sceneSeasonNumber`, keeping only the closest match per title
  via `OrderByDescending(SceneSeasonNumber).ThenByDescending(SeasonNumber).First()`).
  If more than one distinct `TvdbId` remains after filtering, throws
  `InvalidSceneMappingException` (ambiguous mapping — surfaced as a data-quality
  problem, not silently guessed). If exactly one candidate title string has multiple
  near-duplicate spellings, `FindMappings` also does a **Levenshtein-distance
  closest-match** fallback (`LevenshteinDistance(10, 1, 10)` — max distance 10,
  substitution cost 1, insertion/deletion cost 10) among candidates when no exact
  title match exists, to tolerate minor punctuation/spacing differences between the
  release title and the mapping's `Title`.
- **`GetSceneNames(tvdbId, seasonNumbers, sceneSeasonNumbers)`** — the forward lookup
  used to build `SceneTitles` for a search: returns every mapping's `SearchTerm` where
  the mapping's `SeasonNumber` is in the requested season list, OR its
  `SceneSeasonNumber` is in the requested scene-season list, OR the mapping is
  completely unscoped (`SeasonNumber` and `SceneSeasonNumber` both absent) **and**
  `SceneOrigin != "tvdb"` (a `"tvdb"`-origin mapping with no explicit season is treated
  as global numbering info, not an alternate title, and is deliberately excluded from
  the alternate-title list).

### 3.3 XEM integration (`DataAugmentation/Xem/*.cs`)

`XemService` implements `ISceneMappingProvider` (supplies alternate-name scene
mappings, via `GetSceneTvdbNames`) **and** separately performs episode-level scene
**numbering** synchronization (a distinct concern from title mapping, both are fed by
the same upstream data source):
- `XemProxy` (`DataAugmentation/Xem/XemProxy.cs`) hits `https://thexem.info/map/`:
  `/havemap?origin=tvdb` (list of TVDB ids XEM has data for), `/all?id={tvdbid}`
  (per-episode scene↔tvdb numbering pairs), `/allNames?seasonNumbers=true` (alternate
  name → season-number map, transformed into `SceneMapping` rows with
  `MappingId = "x-{tvdbId}_S{season}_{name}"`; includes a hardcoded special-case
  exclusion for TVDB id `79151` (Fate/Zero) seasons >1, a known bad-data workaround).
  Ignores XEM's `"no single connection"`/`"no show with the tvdb_id"` failure messages
  as expected-empty rather than hard errors.
- `PerformUpdate(series)` (on `SeriesUpdatedEvent`, gated by a 3-hour-refreshed
  "does XEM have this show" id cache, or forced during a manual series refresh if that
  id cache itself is >1 minute stale): clears all episodes' `SceneAbsoluteEpisodeNumber`
  /`SceneSeasonNumber`/`SceneEpisodeNumber`/`UnverifiedSceneNumbering`, then applies
  fresh mappings; skips (per-episode) any mapping row that's all-zero
  (`Absolute==0 && Season==0 && Episode==0`, XEM's "no data" sentinel) or that
  references a TVDB episode not yet in Sonarr's DB (not-yet-updated metadata).
- **`ExtrapolateMappings`** — the most subtle piece: for episodes XEM has *no* direct
  mapping for, it infers a probable scene number rather than leaving it null, IF the
  episode is in a season XEM does have some mapped episodes for and appears to come
  *after* the last explicitly-mapped episode. It marks such episodes
  `UnverifiedSceneNumbering = true` and then computes an offset-based guess: takes the
  last mapped episode in the same TVDB season, computes
  `offset = episode.EpisodeNumber - lastMapped.Tvdb.Episode`, and projects
  `SceneSeasonNumber/EpisodeNumber/AbsoluteEpisodeNumber = lastMapped.Scene.* +
  offset`. If the season itself has zero XEM mappings but scene/tvdb season *counts*
  differ (some seasons folded/split between numbering schemes), it instead offsets
  the whole season number (`lastSceneSeason + (episode.Season - lastTvdbSeason)`) and
  leaves absolute-number extrapolation as a known gap (`// TODO:` in source).
  `series.UseSceneNumbering` is set to whether XEM returned *any* mappings at all, and
  is itself later consulted by `SceneMappingSpecification`-adjacent logic and by
  whether XEM re-checks a series that has no cached "has map" entry.

### 3.4 Daily-series augmentation (`DataAugmentation/DailySeries/*.cs`)

A much smaller, separate provider: `DailySeriesService.IsDailySeries(tvdbId)` checks a
1-hour cache of TVDB ids fetched from Sonarr's own metadata service
(`DailySeriesDataProxy`) — used to auto-classify newly added series as
`SeriesTypes.Daily` (talk shows, etc.) at add-time when TheTVDB's own type metadata is
unreliable/absent. Not part of the search-time numbering logic directly, but feeds
`Series.SeriesType`, which the whole `ReleaseSearchService` dispatch tree (§2.2)
branches on.

---

## 4. RSS Sync Loop

- **Command**: `RssSyncCommand` (`Indexers/RssSyncCommand.cs`) — `SendUpdatesToClient
  = true`, `IsLongRunning = true` (surfaced to the UI as a trackable background task,
  not fire-and-forget).
- **Scheduling** (`Jobs/TaskManager.cs`): default interval **15 minutes**
  (`ConfigService.RssSyncInterval` default `15`, `Configuration/ConfigService.cs:112`).
  `GetRssSyncInterval()` clamps: any positive value `< 10` is raised to `10` (hard
  floor to prevent hammering indexers), a negative value is treated as `0`
  (disabled — `TaskManager.GetPending` only schedules tasks with `Interval > 0`), `0`
  itself disables the task entirely. This is a **user-configurable setting**
  (Settings → Indexers → "RSS Sync Interval", minutes).
- **`RssSyncService.Sync()`** (`Indexers/RssSyncService.cs`):
  1. `IFetchAndParseRss.Fetch()` → `FetchAndParseRssService`
     (`Indexers/FetchAndParseRssService.cs`): gets `IndexerFactory.RssEnabled()`
     (already backoff-filtered), fetches every enabled indexer's `FetchRecent()`
     **concurrently** (`Task.WhenAll`), tolerating individual indexer exceptions
     (logs and contributes an empty list for that indexer) so one bad feed doesn't
     abort the whole sync. Warns (not error) if zero indexers are RSS-enabled.
  2. Concatenates fresh RSS releases with **currently-pending releases**
     (`IPendingReleaseService.GetPending()` — delayed/blocklist-retry items awaiting
     re-evaluation, see the `RssSync` decision specs in §5) into one combined report
     list.
  3. Runs `IMakeDownloadDecision.GetRssDecision(reports)` (§5 — the RSS-specific
     "not a user search" decision path, which additionally runs delay/history/pending
     specs that are explicitly skipped during interactive/automatic search).
  4. `IProcessDownloadDecisions.ProcessDecisions(decisions)` — grabs approved
     releases, re-queues temporarily-rejected ones as pending, drops permanently
     rejected ones. Logs a summary line (reports found / grabbed / pending).
  5. Publishes `RssSyncCompleteEvent(processed)` (`Indexers/RssSyncCompleteEvent.cs`)
     for downstream listeners (e.g. UI toast/notifications, activity history).
- **New-release matching to monitored episodes** happens entirely inside the shared
  decision engine (§5) — RSS doesn't have its own separate matching logic; every RSS
  item goes through the exact same `Parser.ParseTitle` → scene-mapping series
  resolution → episode identification → specification pipeline as a manual search
  result, with `searchCriteria = null` (which is what several specs, e.g.
  `MonitoredEpisodeSpecification`, `ProperSpecification`, `HistorySpecification`,
  `DeletedEpisodeFileSpecification`, key off of to know "this is an RSS/passive match,
  not a targeted search").
- **Per-indexer incremental sync state**: `IndexerStatus.LastRssSyncReleaseInfo`
  (persisted via `IndexerStatusService.UpdateRssSyncStatus`, §7) — the newest release
  seen on the *previous* successful RSS fetch for that indexer, consulted by
  `HttpIndexerBase.FetchReleases`'s "have we fully reconnected with last sync" logic
  (§1.1) to decide how far back to page and whether to log a coverage-gap warning.

---

## 5. Decision Engine (Download Decision Making) — the release-matching core

**File:** `DecisionEngine/DownloadDecisionMaker.cs` (`IMakeDownloadDecision`). Two
public entry points, both funnel into the same private pipeline:
- `GetRssDecision(reports, pushedRelease=false)` — `searchCriteria = null`.
- `GetSearchDecision(reports, searchCriteriaBase)` — `searchCriteria` set, used by all
  targeted searches (automatic + interactive).

Per-report pipeline (`GetDecisions`):
1. `Parser.ParseTitle(report.Title)`. If parsing fails or yields a "possible special"
   flag, retries via `IParsingService.ParseSpecialEpisodeTitle` (which additionally
   consults the release's `TvdbId`/`TvRageId`/`ImdbId` and, if present, the active
   `searchCriteria` to disambiguate).
2. If a series title was extracted: `IParsingService.Map(...)` resolves the actual
   `RemoteEpisode` (series + episode list), using the release's embedded ids first and
   scene-mapping/title matching as fallback.
   - `Series == null` → attempts `ISceneMappingService.FindTvdbId` on the parsed
     title; if that finds a match, rejects `MatchesAnotherSeries` (permanent) with a
     message naming the aliased TVDB id (protects against a similarly-named-but-wrong
     series absorbing a release meant for a known alias target); otherwise rejects
     `UnknownSeries`.
   - `Episodes.Empty()` → rejects `UnknownEpisode` ("Unable to identify correct
     episode(s) using release name and scene mappings").
   - Otherwise: runs `IRemoteEpisodeAggregationService.Augment` (fills in
     derived/secondary parse info), computes `CustomFormats` +
     `CustomFormatScore` via the custom-format engine (this is where §1's
     `IndexerFlagSpecification`, freeleech/scene/internal/subtitle flags, actually
     feed scoring — as **custom format conditions**, not as a first-class field in the
     built-in comparer), sets `DownloadAllowed = Episodes.Any()`, and calls
     `GetDecisionForReport` (the specification gauntlet, below).
3. If `searchCriteria != null` and parsing/series-resolution still failed, synthesizes
   a minimal `RemoteEpisode` (parsed languages+quality only) and rejects
   `UnableToParse` — this ensures **every** result of an interactive/manual search is
   represented in the API response (with a reject reason), even totally
   unparseable ones, rather than being silently dropped (RSS/automatic search DOES
   silently drop unparseable releases, since `searchCriteria == null` skips this
   branch).
4. Any uncaught exception during the whole per-report pipeline → synthesizes a bare
   `RemoteEpisode{Release=report}` and rejects `Error` ("Unexpected error processing
   release") rather than aborting the batch.

**`GetDecisionForReport`** — the specification gauntlet:
- All registered `IDownloadDecisionEngineSpecification` are grouped by `Priority`
  (`SpecificationPriority`: `Default=Parsing=Database=0`, `Disk=1` —
  `DecisionEngine/SpecificationPriority.cs`) and evaluated **priority group by
  priority group, in ascending order, stopping at the first group that produces any
  rejection(s)**. In practice this means: all Default/Database-priority specs
  (business-rule, DB-lookup-based checks) run first as one batch; only if *none* of
  them reject does it fall through to the Disk-priority batch (space/free-space/local
  filesystem checks, which are comparatively expensive I/O). This ordering is a
  meaningful performance optimization to replicate — cheap/in-memory checks gate
  before expensive disk I/O checks ever run.
- Within a priority group, **every** spec in the group is evaluated (not
  short-circuited) and **all** rejections from that group are collected —
  `DownloadDecision.Rejections` can contain multiple simultaneous reasons if multiple
  specs in the same (losing) priority group reject.
- A spec throwing an exception is caught and converted into a `DecisionError`
  rejection carrying `{SpecTypeName}: {exception.Message}` — a broken spec degrades to
  "reject this one release" rather than crashing the whole decision run, and the
  release's raw `Release`/`ParsedEpisodeInfo` JSON is attached to the logged exception
  for debugging.
- `ReleaseDecisionInformation` (`DecisionEngine/ReleaseDecisionInformation.cs`) bundles
  `pushedRelease` (bool) + `SearchCriteria` and is passed to every spec —
  this is the shared "what kind of evaluation context is this" signal specs use to
  skip themselves (e.g. "skip history check during search", "skip delay for
  user-invoked search").
- **`ReleaseSourceType`** is stamped on the `RemoteEpisode` from context:
  `ReleasePush` (manually pushed via API) / `Rss` (no search criteria, not pushed) /
  `InteractiveSearch` (`searchCriteria.InteractiveSearch`) / `UserInvokedSearch`
  (`searchCriteria.UserInvokedSearch`, i.e. a manual "Search Now" button, not
  interactive-search-and-pick) / `Search` (background automatic search). Several
  specs branch on this exact classification (delay/pending/history/proper specs all
  treat `UserInvokedSearch`/interactive differently than passive automatic/RSS).

### 5.1 Complete specification enumeration

**General specs** (`DecisionEngine/Specifications/*.cs`) — 25 files:

| Spec | Priority | Type | Reject reason(s) | Semantics |
|---|---|---|---|---|
| `AcceptableSizeSpecification` | Default | Permanent | `UnknownRuntime`, `BelowMinimumSize`, `AboveMaximumSize` | Enforces per-quality min/max size (from the quality profile item's `MinSize`/`MaxSize`, MB/minute × total episode runtime). Skips specials and zero-size (unknown) releases. If series runtime is unset, infers 45min IF all episodes aired within 24h of the season's first episode (heuristic for "this looks like a normal-cadence show whose runtime metadata just hasn't synced yet"). |
| `AlreadyImportedSpecification` | Database | Permanent | `AlreadyImportedSameHash`, `AlreadyImportedSameName` | Only runs when Completed Download Handling is enabled; checks episode history for a prior grab that was later imported with the same torrent hash or the same release name, to avoid re-grabbing something already on disk under CDH. |
| `AnimeVersionUpgradeSpecification` | (see file) | Permanent | `UnknownReleaseGroup`, `ReleaseGroupDoesNotMatch` | For anime version-number upgrades, requires both the existing file's and the new release's group to be known and matching. |
| `BlockedIndexerSpecification` | Database | Temporary | `IndexerDisabled` | Rejects if the release's `IndexerId` is currently in backoff (§7), using a 15-second cached lookup of `IndexerStatusService.GetBlockedProviders()`. |
| `CustomFormatAllowedByProfileSpecification` | (Default) | Permanent | `CustomFormatMinimumScore` | Rejects if the computed custom-format score is below the series' quality profile minimum. |
| `FreeSpaceSpecification` | Disk | Permanent | `MinimumFreeSpace` | Skippable via `SkipFreeSpaceCheckWhenImporting` setting. Computes free space at `Series.Path`, rejects if importing this release would leave less than `MinimumFreeSpaceWhenImporting` (MB, user setting) free, or go negative. |
| `FullSeasonSpecification` | Default | Permanent | `FullSeasonNotAired` | A full-season release is rejected if any of its episodes haven't aired yet (with a 24h grace window). |
| `MaximumSizeSpecification` | — | — | (rolled into `AcceptableSizeSpecification` in this version; the enum value `MaximumSizeExceeded` exists but current max-size logic lives in `AcceptableSizeSpecification`'s `AboveMaximumSize` path — verify against installed version) | |
| `MinimumAgeSpecification` | Default | Temporary | `MinimumAge` | Usenet-only. Rejects releases younger than `ConfigService.MinimumAge` minutes (global setting, default effectively 0/disabled). |
| `MultiSeasonSpecification` | Default | Permanent | `MultiSeason` | Rejects releases spanning multiple seasons in one file/pack (`ParsedEpisodeInfo.IsMultiSeason`) — unsupported. |
| `NotSampleSpecification` | — | Permanent | `Sample` | Rejects sample files. |
| `ProtocolSpecification` | Default | Permanent | `ProtocolDisabled` | Rejects Usenet or Torrent releases if that protocol is disabled in the applicable Delay Profile (`EnableUsenet`/`EnableTorrent`, tag-scoped). |
| `QualityAllowedByProfileSpecification` | — | Permanent | `QualityNotWanted` | Rejects a quality not present/allowed in the series' quality profile. |
| `QueueSpecification` | (Database) | Permanent/Temporary mix per branch | `QueueCutoffMet`, `QueueHigherPreference`, `QueueHigherRevision`, `QueueCustomFormatCutoffMet`, `QueueCustomFormatScore`, `QueueCustomFormatScoreIncrement`, `QueueUpgradesNotAllowed`, `QueuePropersDisabled` | Compares against what's *already queued/downloading* for the same episode(s), mirroring the same upgrade-decision matrix as history/disk checks but against the live queue instead of history or the file on disk. |
| `RawDiskSpecification` | — | Permanent | `Raw` | Rejects raw Bluray/DVD disk-image releases (both DVD and Bluray raw variants get distinct log messages, same reject reason). |
| `ReleaseRestrictionsSpecification` | Default | Permanent | `MustContainMissing`, `MustNotContainPresent` | Enforces per-tag/per-indexer **Release Profiles** (required/ignored term lists) via `ITermMatcherService` (supports plain substrings and presumably regex-style terms — see Release Profile settings surface). Required terms are OR'd within a profile (any one satisfies); any ignored term present anywhere is an instant reject. |
| `RepackSpecification` | Database | Permanent | `RepackDisabled`, `RepackUnknownReleaseGroup`, `RepackReleaseGroupDoesNotMatch` | Repack-specific mirror of `ProperSpecification`: gated by the same `DownloadPropersAndRepacks` setting, additionally requires the repack's release group to exactly match (case-insensitive) the existing file's release group — a repack for a *different* group isn't a real "fix," so it's rejected. |
| `RetentionSpecification` | Default | Permanent | `MaximumAge` | Usenet-only. Rejects releases older than `ConfigService.Retention` days (0 = unlimited). |
| `SameEpisodesGrabSpecification` (wraps `SameEpisodesSpecification`) | Default | Permanent | `ExistingFileHasMoreEpisodes` | Rejects a release if the existing on-disk file for any of its episodes actually spans *more* episodes than this release would replace (don't let a narrower release silently orphan wider on-disk coverage). |
| `SceneMappingSpecification` | Default | Temporary | `AmbiguousNumbering` | Rejects (temporarily — "till there's a mapping") releases whose resolved scene mapping has `SceneOrigin` prefixed `"mixed"` (multiple incompatible numbering schemes seen for this alias) or logs (without rejecting) an `"unknown"`-origin warning asking the user to report the release title upstream. |
| `SeasonPackOnlySpecification` | Default | Permanent | `NotSeasonPack` | When `Release.SeasonSearchMaximumSingleEpisodeAge` (per-indexer setting, §1.1) is set and a season search returns a non-full-season release for a season whose last known-aired episode is older than that many days, rejects it — i.e. "beyond N days into a season, only accept season packs from this indexer, not stray singles." Only applies during season searches (`searchCriteria.Episodes.Count > 1`). |
| `SeriesSpecification` (Search) | Default | Permanent | `WrongSeries` | See §5.1 Search-only table. |
| `SplitEpisodeSpecification` | Default | Permanent | `SplitEpisode` | Rejects releases flagged as a split/partial episode (`IsSplitEpisode`) — unsupported. |
| `TorrentSeedingSpecification` | Default | Permanent | `MinimumSeeders` | Torrent-only; rejects if `Seeders < indexer.MinimumSeeders` (per-indexer torrent setting, §1.2). |
| `UpgradeAllowedSpecification` | — | Permanent | `QualityUpgradesDisabled` | Rejects an upgrade attempt when the series' quality profile has upgrades disabled and a file already exists. |
| `UpgradeDiskSpecification` | (Database/Disk boundary) | Permanent | `DiskNotUpgrade`, `DiskCutoffMet`, `DiskHigherPreference`, `DiskHigherRevision`, `DiskCustomFormatCutoffMet`, `DiskCustomFormatScore`, `DiskCustomFormatScoreIncrement`, `DiskUpgradesNotAllowed` | The most complex spec: compares the release against the file(s) **currently on disk** across quality/revision/custom-format-score/cutoff, including a season-pack-specific **partial-upgrade threshold** mode (`DiskNotUpgrade` fires when a season pack would only upgrade some fraction of episodes below the configured season-pack-upgrade percentage threshold — logged with the exact `upgradedCount/totalEpisodesInPack` percentage and the configured threshold). |

**Search-only specs** (`DecisionEngine/Specifications/Search/*.cs`) — only active when
`searchCriteria != null` (no-op / accept during RSS):
| Spec | Reject reason(s) | Semantics |
|---|---|---|
| `EpisodeRequestedSpecification` | `WrongEpisode` | Rejects if the parsed release's episodes don't intersect the episodes actually requested by the search — the primary "don't grab something unrelated that merely matched query text" guard for search (not RSS, which has no notion of "requested"). |
| `SeasonMatchSpecification` | `WrongSeason` | For `SeasonSearchCriteria` specifically: parsed season number must equal the searched season. |
| `SeriesSpecification` | `WrongSeries` | Parsed series id must equal `searchCriteria.Series.Id` exactly (protects against a scene-mapping/title collision resolving to a different-but-similarly-named series than the one actually searched). |
| `SingleEpisodeSearchMatchSpecification` | `WrongSeason`, `FullSeason`, `WrongEpisode` | For `SingleEpisodeSearchCriteria`: season must match, release must not be a full season (season packs are out of scope for a single-episode search result set), episode number must be among the release's parsed episode numbers. For `AnimeEpisodeSearchCriteria`: full-season releases are rejected unless the search was itself flagged `IsSeasonSearch`. |

**RSS-sync-only specs** (`DecisionEngine/Specifications/RssSync/*.cs`) — only active
when `searchCriteria == null` (no-op / accept during search):
| Spec | Priority | Type | Reject reason(s) | Semantics |
|---|---|---|---|---|
| `DelaySpecification` | Database | Temporary | `MinimumAgeDelay` | The Delay Profile waiting-period mechanism: holds a release for N minutes (`DelayProfile.GetProtocolDelay`) before auto-grab, UNLESS (a) it's a user-invoked search, (b) the profile's preferred protocol + "prefer propers/repacks" combo already has a better revision on disk, (c) `BypassIfHighestQuality` and this quality is the profile's best on the preferred protocol, (d) `BypassIfAboveCustomFormatScore` and the score already clears the profile's minimum, or (e) the **oldest currently-pending** release for this episode has already waited longer than the delay (in which case waiting further is pointless — grab now). This is the core of "wait for a better release before grabbing the first thing seen" behavior. |
| `DeletedEpisodeFileSpecification` | Disk | Temporary | `EpisodeNotMonitored` | Only when `AutoUnmonitorPreviouslyDownloadedEpisodes` is set: if the DB says an episode has a file but it's missing from disk, treat it as "will be unmonitored on next scan" and reject for now rather than re-grabbing prematurely. |
| `HistorySpecification` | Database | Permanent | `HistoryRecentCutoffMet`, `HistoryCdhDisabledCutoffMet`, `HistoryHigherPreference`, `HistoryHigherRevision`, `HistoryCutoffMet`, `HistoryCustomFormatCutoffMet`, `HistoryCustomFormatScore`, `HistoryCustomFormatScoreIncrement`, `HistoryUpgradesNotAllowed` | Compares against the most recent `Grabbed` history event per episode (within 12h = "recent", or unconditionally if Completed Download Handling is disabled) using the shared `UpgradableSpecification`/`UpgradeableRejectReason` matrix — prevents re-grabbing something already grabbed and pending import. |
| `IndexerTagSpecification` | Default | Permanent | `NoMatchingTag` | Defense-in-depth duplicate of the tag filter already applied at dispatch time (§2.2) — needed because RSS/pushed releases don't go through `Dispatch`'s indexer selection. |
| `MonitoredEpisodeSpecification` | Default | Permanent | `SeriesNotMonitored`, `EpisodeNotMonitored` | Skipped entirely when `searchCriteria.MonitoredEpisodesOnly == false` (i.e. explicit unmonitored search). Otherwise: series must be monitored; for multi-episode releases, ALL episodes monitored → accept, ANY monitored but not all → reject "one or more episodes is not monitored" (a season pack isn't rejected just because part of it is monitored — only if none/some are unmonitored is there a partial-mismatch reject). |
| `PendingSpecification` | Database | Temporary | `MinimumAgeDelayPushed` | Skipped for RSS-sourced and user-invoked-search releases. For pushed releases: if another release covering an overlapping episode set is already pending, reject (avoid pending-queue pile-up for the same episode from multiple pushes). |
| `ProperSpecification` | Default | Permanent | `PropersDisabled`, `ProperForOldFile` | Gated by `DownloadPropersAndRepacks` setting (`DoNotPrefer` = skip check entirely). If the release is a revision-upgrade (Proper) over an existing file: reject if propers are configured `DoNotUpgrade`, or if the existing file predates a hardcoded 7-day cutoff (`DateAdded < Today - 7d`) — old files aren't proper-upgraded (assumption: an old file's "wrong" release was probably an intentional choice or no longer worth chasing). |

`SameEpisodesSpecification` (non-`I...Specification`, no `Priority`/`Type`) is a plain
helper class consumed by `SameEpisodesGrabSpecification`, not a standalone pipeline
entry — listed here for completeness of the directory enumeration.

### 5.2 Reject-reason enumeration

**`DownloadRejectionReason`** (`DecisionEngine/DownloadRejectionReason.cs`) — the
complete, closed enum (68 values) used as the typed reason code behind every
`DownloadRejection`. Every value above traces to exactly one (or a small matching
group of) spec(s); the full enum, for exhaustive parity:

```
Unknown, UnknownSeries, UnknownEpisode, MatchesAnotherSeries, UnableToParse, Error,
DecisionError, MinimumAgeDelay, MinimumAgeDelayPushed, SeriesNotMonitored,
EpisodeNotMonitored, HistoryRecentCutoffMet, HistoryCdhDisabledCutoffMet,
HistoryHigherPreference, HistoryHigherRevision, HistoryCutoffMet,
HistoryCustomFormatCutoffMet, HistoryCustomFormatScore,
HistoryCustomFormatScoreIncrement, HistoryUpgradesNotAllowed, NoMatchingTag,
PropersDisabled, ProperForOldFile, WrongEpisode, WrongSeason, WrongSeries,
FullSeason, UnknownRuntime, BelowMinimumSize, AboveMaximumSize,
AlreadyImportedSameHash, AlreadyImportedSameName, UnknownReleaseGroup,
ReleaseGroupDoesNotMatch, IndexerDisabled, Blocklisted, CustomFormatMinimumScore,
MinimumFreeSpace, FullSeasonNotAired, MaximumSizeExceeded, MinimumAge, MaximumAge,
MultiSeason, Sample, ProtocolDisabled, QualityNotWanted, QualityUpgradesDisabled,
QueueHigherPreference, QueueHigherRevision, QueueCutoffMet,
QueueCustomFormatCutoffMet, QueueCustomFormatScore,
QueueCustomFormatScoreIncrement, QueueUpgradesNotAllowed, QueuePropersDisabled,
Raw, MustContainMissing, MustNotContainPresent, RepackDisabled,
RepackUnknownReleaseGroup, RepackReleaseGroupDoesNotMatch,
ExistingFileHasMoreEpisodes, AmbiguousNumbering, NotSeasonPack, SplitEpisode,
MinimumSeeders, DiskHigherPreference, DiskHigherRevision, DiskCutoffMet,
DiskCustomFormatCutoffMet, DiskCustomFormatScore, DiskCustomFormatScoreIncrement,
DiskUpgradesNotAllowed, DiskNotUpgrade, BeforeAirDate
```

`RejectionType` (`DecisionEngine/RejectionType.cs`) is binary: `Permanent=0` (this
release will never be acceptable, e.g. wrong series) vs `Temporary=1` (may become
acceptable later, e.g. delay/backoff/pending — these are the ones that get re-queued
as **pending releases** for re-evaluation rather than discarded). `DownloadDecision`
derives `Approved` (no rejections), `Rejected` (any *Permanent* rejection present), and
`TemporarilyRejected` (rejections exist but are *all* Temporary) from the rejection
list — a decision with a mix of Permanent and Temporary rejections is `Rejected`, not
`TemporarilyRejected` (Permanent wins).

### 5.3 Prioritization / scoring (`DownloadDecisionComparer`)

**File:** `DecisionEngine/DownloadDecisionComparer.cs` — used both to rank interactive
search results for display (`DownloadDecisionPriorizationService`,
`DecisionEngine/DownloadDecisionPriorizationService.cs`, which groups by series id and
sorts each group's decisions descending by this comparer, then reunites) and (via the
same comparer) implicitly to pick which of several *approved* RSS decisions to
actually grab first. Comparison chain, in strict precedence order (first non-zero
result wins):
1. **Quality** — quality-profile index first; if `DownloadPropersAndRepacks !=
   DoNotPrefer`, tie-break by revision (`Proper`/`Repack` bump).
2. **Custom format score.**
3. **Protocol preference** — matches the Delay Profile's `PreferredProtocol`.
4. **Episode count** — full-season packs sort first (`FullSeason` boolean compare);
   among non-season-pack releases, **Anime series prefer MORE episodes per release**
   (ascending `Episodes.Count`), while all other series types prefer **FEWER**
   episodes per release (descending) — i.e. anime multi-eps-per-file batches are
   good, but for standard shows a release bundling unrelated extra episodes is
   suspicious/undesirable.
5. **Episode number** — lower (earlier) episode number preferred, as a final
   numbering-based tie-break.
6. **Indexer priority** — lower `IndexerPriority` number wins (see also
   `DeDupeDecisions`, §2.2).
7. **Peers (torrent only)** — `log10(seeders)` rounded, then `log10(peers)` rounded
   (log-scale so e.g. 1000 vs 2000 seeders doesn't dominate a genuinely better quality
   match — only order-of-magnitude differences matter).
8. **Age (usenet only)** — banded, not linear: `<1h`→1000, `<=24h`→100, `<=7d(age in
   whole days)`→10, else `-round(log10(age))` (older is worse, but the penalty grows
   only logarithmically past a week).
9. **Size** — if the quality-profile item has a `PreferredSize` (MB/min) AND the
   series has a known runtime, picks the release whose size is closest to
   `preferredSize × runtime` (rounded to 200MB buckets, closeness scored as negative
   absolute distance); otherwise falls back to "biggest wins" (rounded to 200MB
   buckets) on the theory that unlimited/unset preferred size implies "more is
   better."

Note: **indexer flags (freeleech/internal/scene/etc.) do NOT participate in this
built-in comparer at all.** They only affect ranking indirectly, through whatever
Custom Format the user has configured with an `IndexerFlagSpecification` condition
(§5.4) and that format's assigned score, which *does* feed step 2 above. This is an
important architectural point to replicate faithfully: flags are scoring **inputs a
user opts into via Custom Formats**, not a hardcoded scoring dimension.

### 5.4 Custom Format indexer-flag condition

**File:** `CustomFormats/Specifications/IndexerFlagSpecification.cs`. A Custom Format
"specification" (condition) type, selectable in the Custom Format editor, whose single
field `Value` is an `IndexerFlags` enum value (validated against `Enum.IsDefined`).
`IsSatisfiedByWithoutNegate` = `input.IndexerFlags.HasFlag((IndexerFlags)Value)` — i.e.
this is how a user builds e.g. a "+50 score if Freeleech" custom format. This is the
sole consumer of `IndexerFlags` in the scoring path (besides display in the manual
search UI, §6).

---

## 6. Interactive (Manual) Search API

**Controller:** `Sonarr.Api.V3/Indexers/ReleaseController.cs` (+ shared base
`ReleaseControllerBase.cs`, resource `ReleaseResource.cs`; a `release/push` sibling
endpoint `ReleasePushController.cs` accepts externally-sourced single releases, e.g.
from a browser extension or notification webhook).

**`GET /api/v3/release`** — three modes selected by query params:
- `?episodeId=` → `ReleaseSearchService.EpisodeSearch(episodeId, userInvokedSearch:
  true, interactiveSearch: true)`.
- `?seriesId=&seasonNumber=` → `SeasonSearch(..., userInvokedSearch: true,
  interactiveSearch: true)`.
- (neither) → falls back to whatever is currently in the RSS cache
  (`_rssFetcherAndParser.Fetch()` + `GetRssDecision`) — i.e. browsing "releases" with
  no episode/season filter just shows the current RSS pool, not a fresh targeted
  search.
Both search modes set `InteractiveSearch = true`, which (a) routes indexer selection
through `InteractiveSearchEnabled()` instead of `AutomaticSearchEnabled()` (§6.1 — a
separate enable flag per indexer!) and (b) causes several decision specs to bypass
delay/pending/history checks (§5) since a human is actively choosing right now.
Results are ranked via `IPrioritizeDownloadDecision.PrioritizeDecisions` before
mapping to resources, and every mapped `ReleaseResource` is cached (30-minute TTL,
keyed by `"{indexerId}_{guid}"`) so the subsequent grab call can retrieve the full
`RemoteEpisode` object without re-parsing.

### 6.1 Per-indexer search-type enable flags

`IndexerDefinition` (§1.1) carries **three independent booleans**:
`EnableRss`, `EnableAutomaticSearch`, `EnableInteractiveSearch` — a user can, for
example, allow an indexer for manual/interactive search only while excluding it from
RSS sync and automatic background search (or vice versa). `Enable` (computed) is `true`
if *any* of the three is set — that's what gates whether the provider is "Active" at
all (`IndexerFactory.Active()` override filters on `Enable`). `IndexerFactory` exposes
three corresponding list methods (`RssEnabled`, `AutomaticSearchEnabled`,
`InteractiveSearchEnabled`), each optionally filtering out currently-blocked indexers
(`filterBlockedIndexers = true` by default).

### 6.2 `ReleaseResource` — full field enumeration returned per release

From `Sonarr.Api.V3/Indexers/ReleaseResource.cs` (`ReleaseResourceMapper.ToResource`):

Identity/quality: `Guid`, `Quality` (`QualityModel`), `QualityWeight` (computed:
profile index×100 + revision.Real×10 + revision.Version — used purely for UI sort
stability, distinct from the comparer in §5.3), `Age`/`AgeHours`/`AgeMinutes`, `Size`.

Indexer/source: `IndexerId`, `Indexer` (name), `ReleaseGroup`, `SubGroup` (present in
the resource shape though not populated by the mapper shown — likely anime fansub
subgroup, populated elsewhere/parser-specific), `ReleaseHash`, `Title`.

Numbering: `FullSeason`, `SceneSource` (present in resource, not set by this mapper —
likely legacy/reserved), `SeasonNumber`, `EpisodeNumbers[]`, `AbsoluteEpisodeNumbers[]`,
`AirDate`, `SeriesTitle`, plus the **mapped** (post-scene-resolution) numbering:
`MappedSeasonNumber`, `MappedEpisodeNumbers[]`, `MappedAbsoluteEpisodeNumbers[]`,
`MappedSeriesId`, `MappedEpisodeInfo[]` (`ReleaseEpisodeResource`: `Id`,
`SeasonNumber`, `EpisodeNumber`, `AbsoluteEpisodeNumber`, `Title` per matched episode)
— i.e. the API deliberately surfaces BOTH what the release's filename claims AND what
Sonarr actually resolved it to, so the UI can show a manual-override affordance when
they diverge.

Language/ids: `Languages[]`, `LanguageWeight` (present in resource; not populated in
this mapper snippet — set elsewhere in language-profile scoring), `TvdbId`,
`TvRageId`, `ImdbId`.

Decision outcome: `Approved`, `TemporarilyRejected`, `Rejected`, `Rejections`
(`IEnumerable<string>` — **just the rejection messages, not the typed reason codes** —
`model.Rejections.Select(r => r.Message)`; the typed `DownloadRejectionReason` enum is
NOT sent to the frontend, only the human-readable message string, so any client-side
"reject reason" logic must pattern-match on message text or the API would need
extending to expose the enum).

URLs/dates: `PublishDate`, `CommentUrl`, `DownloadUrl`, `InfoUrl`.

Flags/eligibility: `EpisodeRequested`, `DownloadAllowed`, `ReleaseWeight` (result
ordinal position from `MapDecisions`, i.e. literally "how many-th in the already-sorted
list" — a display-order index, not a score), `CustomFormats[]`, `CustomFormatScore`,
`SceneMapping` (`AlternateTitleResource`, the specific scene-mapping alias that was
used to resolve this release, if any).

Torrent-only: `MagnetUrl`, `InfoHash`, `Seeders`, `Leechers` (derived:
`Peers - Seeders`, null if either is unknown), `Protocol`, `IndexerFlags` (raw `int`
bitmask of the `IndexerFlags` enum — the frontend renders flag badges directly from
this bitmask, no server-side flag-name translation).

Numbering-shape flags: `IsDaily`, `IsAbsoluteNumbering`, `IsPossibleSpecialEpisode`,
`Special`.

Grab-only fields (JSON-omitted when default, only meaningful on the POST body for
"queue an unknown/manual release"): `SeriesId`, `EpisodeId`, `EpisodeIds[]`,
`DownloadClientId`, `DownloadClient`, `ShouldOverride`.

### 6.3 Grab endpoint & manual override

**`POST /api/v3/release`** (`ReleaseController.DownloadRelease`): looks the release
back up in the 30-minute cache by `{indexerId}_{guid}`; **404s with "try searching
again"** if the cache has expired — i.e. a manual-search result set is only actionable
for 30 minutes before the user must re-search. Two paths:

- **`ShouldOverride == true`** (the manual-override path, used when the user
  disagrees with Sonarr's own series/episode/quality/language resolution for this
  release): requires `SeriesId`, `EpisodeIds[]` (non-empty), `Quality`, `Languages` all
  be supplied by the client; **clones** the cached `RemoteEpisode` (deliberately not
  mutating the cached original, so re-grabbing the same guid without override still
  works with the original resolution) with the override's `Series`/`Episodes`/
  `ParsedEpisodeInfo.Quality`/`Languages` substituted in, while preserving the
  original `Release`, `SceneMapping`, `MappedSeasonNumber`, `EpisodeRequested`,
  `DownloadAllowed`, `SeedConfiguration`, `CustomFormats`, `CustomFormatScore`,
  `SeriesMatchType`, `ReleaseSource` from the original decision.
- **Normal path** (`ShouldOverride` falsy or absent): if the cached `RemoteEpisode` has
  no `Series` resolved at all, falls back to `EpisodeId` (look up episode → its
  series) or `SeriesId` (look up series, then re-run
  `IParsingService.GetEpisodes(parsedInfo, series, allowSpecials: true)` against it) —
  **404 "will need to be manually provided"** if episodes still can't be resolved
  either way. If `Series` was already resolved but `Episodes` is empty, retries
  episode resolution the same way, with an `EpisodeId` fallback.
- Either path, if `Episodes` end up empty → 404.
- Calls `IDownloadService.DownloadReport(remoteEpisode, downloadClientId)`; a
  `ReleaseDownloadException` (fetching the actual file/magnet from the indexer failed)
  is converted to a **409 Conflict** ("Getting release from indexer failed") — a
  distinct HTTP status from all the 404/400 resolution failures above, so clients can
  tell "we understood the request but the indexer itself failed to serve the file"
  apart from "we couldn't figure out what you meant."

**`POST /api/v3/release/push`** (`ReleasePushController`): accepts a fully-formed
`ReleaseResource` from an external source (no indexer search involved). Validates
`Title`, (`DownloadUrl` OR `MagnetUrl`), `Protocol`, `PublishDate` all present.
Synthesizes `Guid = "PUSH-" + DownloadUrl`. Resolves `IndexerId`/`Indexer` by name/id
via `IndexerFactory.ResolveIndexer` if possible (best-effort — a completely unknown
indexer name is just logged, not an error) and a matching download client similarly
best-effort resolved by id/name. Runs the pushed release through
`GetRssDecision(..., pushedRelease: true)` under an explicit process-wide lock
(`PushLock`, a `static readonly object`) — **pushed releases are serialized against
each other** (not against normal RSS/search decisions) to avoid races when multiple
push sources fire concurrently. A decision with no resolvable `ParsedEpisodeInfo`
throws a `ValidationException` ("Unable to parse") rather than silently accepting a
completely unparseable push.

---

## 7. Failure Handling / Indexer Health

### 7.1 Backoff state machine

**Files:** `ThingiProvider/Status/ProviderStatusServiceBase.cs` (generic, reused by
indexers, download clients, notifications, etc.), specialized for indexers by
`Indexers/IndexerStatusService.cs` / `Indexers/IndexerStatus.cs`.

Persisted per-indexer state (`IndexerStatus : ProviderStatusBase`): `InitialFailure`,
`MostRecentFailure`, `EscalationLevel` (int), `DisabledTill` (nullable DateTime),
plus indexer-specific `LastRssSyncReleaseInfo` (§4).

**Escalation ladder** (`ThingiProvider/Status/EscalationBackOff.cs`) — 10 levels, in
seconds: `0, 60, 300 (5m), 900 (15m), 1800 (30m), 3600 (1h), 10800 (3h), 21600 (6h),
43200 (12h), 86400 (24h)`. `MaximumEscalationLevel` defaults to the last index (level
9 / 24h ceiling).

**`RecordFailure(providerId, minimumBackOff=0)`** (escalating):
- First failure ever (`EscalationLevel == 0`): sets `InitialFailure = now`,
  `EscalationLevel = 1`, and **does not escalate further on this same call**
  (`escalate = false` locally) — i.e. the very first observed failure always lands at
  level 1 (60s), never jumps straight to a higher level even if `minimumBackOff` is
  large (that's handled by the separate `minimumBackOff` while-loop below, which CAN
  push a fresh failure past level 1 if the caller demanded a longer minimum).
- Subsequent failures escalate by +1 level, UNLESS still within
  `MinimumTimeSinceInitialFailure` (default `TimeSpan.Zero` — effectively unused
  as configured, a hook for suppressing rapid re-escalation) or within
  `MinimumTimeSinceStartup` (default **15 minutes** after process start) — during
  the startup grace window, failures don't escalate the backoff level.
- If the caller passed a `minimumBackOff` (e.g. an indexer's `Retry-After`
  header/rate-limit response demands ≥1 hour), the escalation level is bumped up
  (capped at `MaximumEscalationLevel`) until the ladder's period at that level is
  `>= minimumBackOff` — so a hard rate-limit signal can force a longer wait than the
  organic escalation would have produced on its own.
- `DisabledTill = now + CalculateBackOffPeriod(status)` is set whenever not still in
  the initial-failure grace period, or whenever an explicit `minimumBackOff` was
  supplied (even inside the grace period — an explicit signal always applies).
- **Startup dampening**: if within the 15-minute startup grace window AND no explicit
  `minimumBackOff` was given, the computed `DisabledTill` is capped to at most
  `now + EscalationBackOff.Periods[2]` (5 minutes) — so transient failures right after
  Sonarr starts (e.g. network not fully up yet) don't accidentally disable an indexer
  for hours on the very first check.
- Publishes `ProviderStatusChangedEvent<TProvider>` on every state change (drives the
  indexer health UI badge in real time).

**`RecordConnectionFailure(providerId)`** — calls the same machinery with
`escalate: false` explicitly (DNS/connect failures don't escalate the backoff level at
all on their own, only genuine application-level failures do — though they still set
`MostRecentFailure` and can still result in a `DisabledTill` if already escalated from
prior real failures).

**`RecordSuccess(providerId)`** — de-escalates by exactly **one level** per success
(not a full reset) and clears `DisabledTill` immediately; a no-op if already at level 0.
This means recovery from a long backoff is gradual — one success only walks the level
back down by one step, so a genuinely flaky indexer can't instantly regain full trust
from a single lucky request; it has to sustain several successes to fully de-escalate
(each success also immediately un-disables it for the *current* window though, since
`DisabledTill` is cleared unconditionally on any de-escalating success).

**`GetBlockedProviders()`** — returns every persisted status where
`IsDisabled()` (`DisabledTill.HasValue && DisabledTill.Value > UtcNow`) is true; this
is what `IndexerFactory.FilterBlockedIndexers` and `BlockedIndexerSpecification`
(§5.1) both consult (the latter via a 15-second local cache to avoid hammering the
repository on every single decision evaluation).

### 7.2 Exception → status-service mapping (`HttpIndexerBase.FetchReleases`, §1.1)

| Exception | Status action | Notes |
|---|---|---|
| `WebException` (DNS/connect) | `RecordConnectionFailure` | non-escalating |
| `WebException` (other) | `RecordFailure()` (no minimum) | escalating, normal ladder |
| `TooManyRequestsException` | `RecordFailure(ex.RetryAfter or 1h default)` | escalating, minimum-forced |
| `HttpException` | `RecordFailure()` | escalating |
| `RequestLimitReachedException` | `RecordFailure(ex.RetryAfter or 1h default)` | escalating, minimum-forced |
| `ApiKeyException` | `RecordFailure()` | escalating |
| `CloudFlareCaptchaException` | `RecordFailure()` | escalating; also distinguishes expired-vs-required CAPTCHA in the log message |
| `TaskCanceledException` (timeout) | `RecordFailure()` | escalating |
| `IndexerException` | `RecordFailure()` | escalating |
| any other `Exception` | `RecordFailure()` | escalating; attaches feed URL + full exception to the log at Error |

A successful fetch (no exception) always calls `RecordSuccess` regardless of whether
any releases were actually found (zero results is not a failure).

### 7.3 Health checks

Indexer-specific health surfacing is the `IndexerStatus`/backoff system itself (no
separate scheduled "health check" job was found for indexers beyond the RSS-sync-time
implicit check and the manual "Test" button) — the presence of any blocked indexer is
what the app-wide health-check subsystem (`Health/Checks/*`, not enumerated here as
out of scope) surfaces as a dashboard warning. `IndexerFactory.Test` (§1.1, called on
Save and on the explicit Test button) is the only *deliberate* health probe;
otherwise, health is a side effect of normal fetch traffic.

### 7.4 `IIndexerRepository` / persistence

`Indexers/IndexerRepository.cs` and `Indexers/IndexerStatusRepository.cs` are thin EF
persistence layers (definitions table + status table respectively) — not further
detailed here as they're standard CRUD, but note the **split**: indexer *definitions*
(settings, enable flags, priority) and indexer *status* (backoff state, last-RSS-sync
release) are two separate persisted aggregates joined by `ProviderId`/`IndexerId`, not
one combined row. `CachedIndexerSettingsProvider`
(`Indexers/CachedIndexerSettingsProvider.cs`) is a 1-hour rolling cache in front of the
definitions repository specifically for the two hot-path fields
(`FailDownloads`, `SeedCriteriaSettings`) consumed outside the indexer's own request
pipeline (by `SeedConfigProvider`, §7.5, and presumably content-fail-download
handling), invalidated immediately on any `ProviderUpdatedEvent<IIndexer>` /
`ProviderDeletedEvent<IIndexer>`.

### 7.5 Seed criteria (torrent-only)

**Files:** `Indexers/SeedCriteriaSettings.cs`, `Indexers/SeedConfigProvider.cs`,
`Indexers/SeasonPackSeedGoal.cs`. Per-torrent-indexer settings surface (nested object
`SeedCriteria` on every `ITorrentIndexerSettings` implementation):
- `SeedRatio` (double?) — advisory minimum ratio before the download client should stop
  seeding.
- `SeedTime` (int? minutes) — advisory minimum seed duration.
- `SeasonPackSeedGoal` (`SeasonPackSeedGoal` enum: `UseStandardSeedGoal=0` or
  `UseSeasonPackSeedGoal=1`) — whether season-pack grabs should use a *separate*,
  typically higher, seed goal.
- `SeasonPackSeedRatio` / `SeasonPackSeedTime` — the season-pack-specific overrides,
  only used when `SeasonPackSeedGoal == UseSeasonPackSeedGoal`.
- Validation (`SeedCriteriaSettingsValidator`) is parameterized per-indexer-type with
  **minimum recommended values** that vary by tracker (e.g. `BroadcastheNet` passes
  `seedRatioMinimum: 1.0, seedTimeMinimum: 24h, seasonPackSeedTimeMinimum: 5×24h` —
  private-tracker-appropriate H&R-avoidance defaults — while generic Torznab uses the
  parameterless/zero-minimum validator). Values below the minimum are **warnings**, not
  hard validation failures ("Under X leads to H&R" — Hit & Run).
- `SeedConfigProvider.GetSeedConfiguration(remoteEpisode)` — resolves to `null` for
  non-torrent releases or indexer id `0`; otherwise builds a `TorrentSeedConfiguration`
  (ratio + `TimeSpan` seed time) choosing season-pack values instead of standard values
  when `remoteEpisode.ParsedEpisodeInfo.FullSeason && SeasonPackSeedGoal ==
  UseSeasonPackSeedGoal`. This configuration is handed to the download client adapter
  at grab time to configure the torrent client's per-torrent seed limits, where
  supported.

---

## 8. Download Protocol

**File:** `Indexers/DownloadProtocol.cs` — a two-value enum (plus `Unknown=0` sentinel):
`Usenet=1`, `Torrent=2`. This single flag is threaded through nearly everything in
this document: which parser base class applies (RSS/Newznab-style vs
Torrent-RSS/Torznab-style), which decision specs are protocol-gated
(`MinimumAgeSpecification`/`RetentionSpecification`=Usenet-only,
`TorrentSeedingSpecification`=Torrent-only), the Delay Profile's per-protocol
enable/delay/preference settings (§5.1 `DelaySpecification`/`ProtocolSpecification`),
and the comparer's protocol-preference tie-break (§5.3).

---

## 9. Category System (Newznab/Torznab)

Newznab/Torznab categories are a flat, provider-agnostic **integer numbering
convention** (not enumerated centrally in one Sonarr file as a full canonical list —
Sonarr instead trusts each indexer's own `caps` endpoint for the authoritative list,
falling back to a hardcoded default TV subtree when caps can't be fetched, §1.2/§9.1).

### 9.1 Default/fallback TV category tree

From `Indexers/Newznab/NewznabCategoryFieldOptionsConverter.cs` (used only when a live
capabilities fetch fails):

| Id | Name | Parent |
|---|---|---|
| 5000 | TV | — |
| 5010 | WEB-DL | 5000 |
| 5020 | Foreign | 5000 |
| 5030 | SD | 5000 |
| 5040 | HD | 5000 |
| 5045 | UHD | 5000 |
| 5050 | Other | 5000 |
| 5060 | Sport | 5000 |
| 5070 | Anime | 5000 |
| 5080 | Documentary | 5000 |

**Default `NewznabSettings.Categories = [5030, 5040]`** (SD + HD) — UHD (5045) is
notably **not** in the default set and must be opted into explicitly; a real-world
indexer preset does override this (`NZBFinder.ws` default definition passes
`categories: [5030, 5040, 5045]`, §1.2).

**Category-picker UI filtering** (`GetFieldSelectOptions`): categories `1000`
(Console/other), `3000` (Audio), `4000` (PC/software), `6000` (XXX), `7000` (Books) are
**hidden entirely** from the picker (`ignoreCategories`) as irrelevant to a TV app;
category `0` (uncategorized/"All") and `2000` (Movies) are **de-prioritized** in sort
order (`unimportantCategories` — shown, but sorted after the relevant TV
categories/subcategories) rather than hidden, since a user might legitimately want to
include them. Categories are always grouped parent-then-children, each sorted by id.

### 9.2 How categories route

- **Standard/daily searches**: always use `Settings.Categories` (the SD/HD/UHD/etc.
  bucket) via `NewznabRequestGenerator`'s title/id request builders (§2.5).
- **Anime searches**: use `Settings.AnimeCategories` (a completely separate configured
  category set — e.g. `5070` Anime, or an indexer's dedicated anime sub-category) —
  selected by `GetSearchCategories(searchCriteria)` checking
  `searchCriteria.Series?.SeriesType == SeriesTypes.Anime`.
- **RSS (`GetRecentRequests`)**: if the indexer's capabilities advertise `tv-search`
  at all, RSS fetches **both** `Categories` and `AnimeCategories` **combined** in one
  `tvsearch` request (`Settings.Categories.Concat(Settings.AnimeCategories)`) — RSS
  doesn't know in advance which upcoming release will be anime vs standard, so it
  casts the union net. If only generic `search` is supported (no `tv-search` at all),
  RSS falls back to just `AnimeCategories` under `t=search` (a narrower, degraded
  fallback for very limited indexers).
- Categories are passed on the wire as a **comma-joined, de-duplicated** list in the
  `cat=` query parameter (`GetPagedRequests`, §2.5).
- Site-specific torrent trackers that aren't Newznab/Torznab define **their own**
  category enumerations, unrelated to the Newznab numbering (e.g. `HdBitsCategory`,
  `FileListCategories`, §1.3) — category numbering is fundamentally a per-protocol
  (Newznab-wire) or per-site (bespoke API) concept, not a single global Sonarr-owned
  enum. A Prismedia implementation targeting generic Torznab/Newznab compatibility
  only needs to reproduce §9.1/§9.2; bespoke-site category enums are indexer-specific
  and out of scope for a wire-protocol-compatible layer.

---

## 10. Summary of User-Tunable Configuration Surface

Consolidated list of every setting mentioned above that a user can actually configure
(as opposed to internal constants):

**Per-indexer (all types):** Name, URL/BaseUrl, Enable RSS / Enable Automatic Search /
Enable Interactive Search (three independent toggles), Priority (int, lower = higher
priority), Tags, Season Search Maximum Single Episode Age (days).

**Per-indexer (Newznab/Torznab specifically):** API Path, API Key, Categories, Anime
Categories, Anime Standard Format Search (checkbox), Additional Parameters (raw query
string), Multi Language Release (language list), Fail Downloads (executables/
dangerous/user-defined-extensions).

**Per-indexer (torrent-capable, i.e. `ITorrentIndexerSettings`):** Minimum Seeders,
Seed Ratio, Seed Time, Season Pack Seed Goal (standard vs season-pack-specific),
Season Pack Seed Ratio, Season Pack Seed Time, Reject Blocklisted Torrent Hashes While
Grabbing.

**Site-specific extra fields:** HDBits (Username, Codecs, Mediums), FileList/BroadcastheNet
(Username/Passkey/API Key per site's own auth model), IPTorrents/Torrentleech feed
URLs, Nyaa/Fanzub Additional Parameters + Anime Standard Format Search.

**Global (Settings → Indexers):** RSS Sync Interval (minutes; 0 = disabled, values
1-9 clamp to 10, default 15), Minimum Age (usenet, minutes), Retention (usenet, days),
Skip Free Space Check When Importing (bool), Minimum Free Space When Importing (MB).

**Global (Settings → Media Management / Download Client adjacent, referenced by
specs):** Download Propers and Repacks (Do Not Prefer / Prefer-and-upgrade / Do Not
Upgrade), Auto Unmonitor Previously Downloaded Episodes, Enable Completed Download
Handling.

**Delay Profiles (tag-scoped, referenced by `DelaySpecification`/
`ProtocolSpecification`/comparer):** Enable Usenet / Enable Torrent, Preferred
Protocol, per-protocol delay (minutes), Bypass If Highest Quality, Bypass If Above
Custom Format Score + minimum score.

**Release Profiles (tag- and indexer-scoped, `ReleaseRestrictionsSpecification`):**
Required terms, Ignored terms.

**Quality Profile (per-series):** allowed qualities + ordering, cutoff, upgrades
allowed, minimum custom format score, cutoff custom format score, min/preferred/max
size per quality item, season-pack upgrade threshold percentage (`UpgradeDiskSpecification`).

**Custom Formats (global, scored per quality profile):** arbitrary condition trees
including the Indexer Flag condition (§5.4) — this is the extensibility point through
which indexer flags (freeleech/internal/scene/subtitles/nuked/etc.) actually affect
grab decisions and ranking.

---

## Directory Listings Walked (for audit trail)

- `Indexers/` — 76 files (all enumerated in §1; includes 10 concrete indexer folders:
  `BroadcastheNet`, `Fanzub`, `FileList`, `HDBits`, `IPTorrents`, `Newznab`, `Nyaa`,
  `Torrentleech`, `Torznab`, `TorrentRss`; plus `Exceptions/` (4 files) and the shared
  framework files at the `Indexers/` root).
- `IndexerSearch/` — 12 root files + `Definitions/` (10 files) = 22 files, all read
  and covered in §2.
- `DataAugmentation/` — `DailySeries/` (3 files), `Scene/` (8 files), `Xem/` (5 files,
  incl. `Model/` subfolder with 3 model classes) = 16 files, all covered in §3.
- `DecisionEngine/` (adjacent, required for search-result semantics) — 13 root files +
  `Specifications/` (25 files) + `Specifications/RssSync/` (7 files) +
  `Specifications/Search/` (4 files) = 49 files, all covered in §5.
- `Sonarr.Api.V3/Indexers/` — `ReleaseController.cs`, `ReleaseControllerBase.cs`,
  `ReleasePushController.cs`, `ReleaseResource.cs`, `IndexerFlagController.cs`,
  `IndexerFlagResource.cs` — covered in §6 (V5 has parallel
  `IndexerFlagController`/`Resource` files, not separately detailed since they mirror
  V3's shape for the flags list endpoint).
- `ThingiProvider/Status/` — 4 files, covered in §7.
- `CustomFormats/Specifications/IndexerFlagSpecification.cs` — covered in §5.4.
- `Parser/Model/{ReleaseInfo,TorrentInfo,IndexerFlags,GrabbedReleaseInfo}.cs` —
  `ReleaseInfo`/`TorrentInfo`/`IndexerFlags` covered in §1/§8; `GrabbedReleaseInfo` is a
  history-projection DTO outside this document's scope (post-grab bookkeeping, not
  search).
