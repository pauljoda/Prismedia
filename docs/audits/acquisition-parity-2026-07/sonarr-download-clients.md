# Sonarr Download Client System — Feature Parity Map

Source examined: `/Users/pauldavis/Dev/_ARCHIVE/Sonarr/src/NzbDrone.Core/Download/**`,
`/Users/pauldavis/Dev/_ARCHIVE/Sonarr/src/NzbDrone.Core/RemotePathMappings/**`,
`/Users/pauldavis/Dev/_ARCHIVE/Sonarr/src/NzbDrone.Core/Queue/**`, plus the
`Blocklisting`, `Configuration`, and `IndexerSearch` slices that the download-client
loop calls into.

This document is exhaustive: every file under `Download/Clients/*` was read, every
client subdirectory is enumerated, every setting field is listed, and every state
enum is transcribed.

---

## 1. Directory inventory (what exists, file by file)

### `NzbDrone.Core/Download/` (root — orchestration, not client-specific)

| File | Responsibility |
|---|---|
| `IDownloadClient.cs` | The contract every client implements (see §2). |
| `DownloadClientBase.cs` | Abstract base: shared retry policy, `Test()` wrapper, folder validation, `DeleteItemData`. |
| `TorrentClientBase.cs` | Torrent-protocol base: magnet vs `.torrent` file download orchestration, blocklist pre-check. |
| `UsenetClientBase.cs` | Usenet-protocol base: `.nzb` download + validation orchestration. |
| `DownloadClientDefinition.cs` | Persisted provider definition: `Protocol`, `Priority`, `RemoveCompletedDownloads`, `RemoveFailedDownloads`. |
| `DownloadClientFactory.cs` | `IProviderFactory` implementation; filters to enabled + non-blocked clients; resolves by id/name. |
| `DownloadClientProvider.cs` | Client **selection**: protocol/indexer/tag filtering, priority grouping, round-robin rotation (see §9). |
| `DownloadClientItem.cs` | The universal polled-item DTO every client maps its native state into (see §4). |
| `DownloadClientInfo.cs` | Client-level status snapshot: `IsLocalhost`, `OutputRootFolders`, `SortingMode`, `RemovesCompletedDownloads`. |
| `DownloadClientStatus.cs` / `DownloadClientStatusService.cs` | Health/backoff tracking — 5 min initial suppression, escalates to 5 levels, used to temporarily skip failing clients. |
| `DownloadClientStatusRepository.cs` | Persistence for the above. |
| `DownloadClientRepository.cs` | EF-ish repository for `DownloadClientDefinition` (provider config storage). |
| `DownloadClientType.cs` | Legacy enum `{Sabnzbd, Blackhole, Pneumatic, Nzbget}` — vestigial, not the live client registry (real registry is DI-based, one class per client). |
| `DownloadItemStatus.cs` | The universal per-item status enum (see §4). |
| `DownloadService.cs` | Send-to-client orchestrator: resolves specific/any client, retries across clients on `DownloadClientException`, rate-limits, publishes `EpisodeGrabbedEvent` (see §3, §9). |
| `DownloadSeedConfigProvider.cs` | Caches "was this hash a full-season grab from indexer X" so seed settings can be re-derived after the fact (used when polling clients that don't echo back seed config, e.g. rTorrent/RQBit). |
| `DownloadProcessingService.cs` | Per-tick driver: for every tracked download, calls `Import()` if `ImportPending`, `ProcessFailed()` if `FailedPending`, then removes fully-`Imported` items whose `CanBeRemoved` is true. |
| `CompletedDownloadService.cs` | The CDH (Completed Download Handling) engine — `Check()` gatekeeping + `Import()` execution + `VerifyImport()` (see §7). |
| `FailedDownloadService.cs` | The FDH (Failed Download Handling) engine — detection + manual "mark as failed" + blocklist/redownload trigger (see §8). |
| `RedownloadFailedDownloadService.cs` | Reacts to `DownloadFailedEvent`: decides whether to re-search single episode, whole season, or a partial batch (see §8). |
| `RejectedImportService.cs` | Maps import-rejection reasons (dangerous file, executable, user-defined extension) to either a hard `Fail()` or a soft warning, based on per-indexer `FailDownloads` settings. |
| `ProvideImportItemService.cs` | Thin indirection: asks the owning `IDownloadClient.GetImportItem()` to resolve/augment the output path before import. |
| `DownloadEventHub.cs` | Central reactor for `DownloadFailedEvent` / `DownloadCompletedEvent` / `DownloadCanBeRemovedEvent` → decides whether to call `RemoveItem()` on the client and/or `MarkItemAsImported()` (see §10). |
| `IgnoredDownloadService.cs` | User-triggered "ignore this download" → publishes `DownloadIgnoredEvent`, moving `TrackedDownloadState` to `Ignored`. |
| `NzbValidationService.cs` | Validates downloaded `.nzb` bytes are well-formed XML before handing to a Usenet client; throws `InvalidNzbException`. |
| `ProcessDownloadDecisions.cs` / `ProcessedDecisions.cs` / `ProcessedDecisionResult.cs` | Bridges the decision engine's grab list into actual `DownloadService.DownloadReport()` calls, aggregating per-release success/failure/pending outcomes for the UI/API response. |
| `RefreshMonitoredDownloadsCommand.cs` / `CheckForFinishedDownloadCommand.cs` (deprecated) / `ProcessMonitoredDownloadsCommand.cs` | Command triggers for the polling/refresh loop. |
| `EpisodeGrabbedEvent.cs`, `DownloadCompletedEvent.cs`, `DownloadFailedEvent.cs`, `DownloadIgnoredEvent.cs`, `DownloadCanBeRemovedEvent.cs`, `DownloadsProcessedEvent.cs`, `UntrackedDownloadCompletedEvent.cs`, `ManualInteractionRequiredEvent.cs` | Event contracts wired through `IEventAggregator`. |
| `ResolveDownloadClientException.cs`, `InvalidNzbException.cs` | Specific exception types. |
| `Extensions/XmlExtensions.cs` | XML helpers used by NZB validation/parsing. |
| `Aggregation/Aggregators/AggregateLanguages.cs`, `Aggregation/Aggregators/IAggregateRemoteEpisode.cs`, `Aggregation/RemoteEpisodeAggregationService.cs` | Post-parse enrichment of a `RemoteEpisode` reconstructed from a tracked download (language augmentation etc.) — invoked by `TrackedDownloadService`. |

### `NzbDrone.Core/Download/Clients/` (18 client implementations — every subdirectory enumerated)

Shared, non-client-specific files in this folder:
- `DownloadClientSettingsBase.cs` — abstract settings base (`IProviderConfig` + memberwise equality).
- `DownloadClientException.cs`, `DownloadClientAuthenticationException.cs`, `DownloadClientUnavailableException.cs` — exception hierarchy every client throws through.
- `TorrentSeedConfiguration.cs` — `{ Ratio, SeedTime }` value object attached to a `RemoteEpisode` before `Download()` is called.

Client subdirectories (18 total, alphabetical):

1. `Aria2/` — `Aria2.cs`, `Aria2Containers.cs`, `Aria2Proxy.cs`, `Aria2Settings.cs`
2. `Blackhole/` — `TorrentBlackhole.cs`, `TorrentBlackholeSettings.cs`, `UsenetBlackhole.cs`, `UsenetBlackholeSettings.cs`, `ScanWatchFolder.cs`, `WatchFolderItem.cs` (two clients: torrent blackhole + usenet blackhole share the folder-scan engine)
3. `Deluge/` — `Deluge.cs`, `DelugeSettings.cs`, `DelugeProxy.cs`, `DelugeError.cs`, `DelugeException.cs`, `DelugeLabel.cs`, `DelugePriority.cs`, `DelugeTorrent.cs`, `DelugeTorrentStatus.cs`, `DelugeUpdateUIResult.cs`
4. `DownloadStation/` (Synology) — `TorrentDownloadStation.cs`, `UsenetDownloadStation.cs`, `DownloadStationSettings.cs`, `DiskStationApi.cs`, `DiskStationApiInfo.cs`, `DownloadStation2Task.cs`, `DownloadStationTask.cs`, `DownloadStationTaskAdditional.cs`, `DownloadStationTaskFile.cs`, `SerialNumberProvider.cs`, `SharedFolderMapping.cs`, `SharedFolderResolver.cs`, plus `Proxies/` (`DSMInfoProxy.cs`, `DiskStationProxyBase.cs`, `DownloadStationInfoProxy.cs`, `DownloadStationTaskProxySelector.cs`, `DownloadStationTaskProxyV1.cs`, `DownloadStationTaskProxyV2.cs`, `FileStationProxy.cs`) and `Responses/` (8 response DTOs). Two client classes (torrent + usenet) share this whole stack.
5. `Flood/` — `Flood.cs`, `FloodProxy.cs`, `FloodSettings.cs`, `Models/AdditionalTags.cs`, `Types/FloodClientSettings.cs`, `Types/Torrent.cs`, `Types/TorrentContent.cs`, `Types/TorrentListSummary.cs`
6. `FreeboxDownload/` — `TorrentFreeboxDownload.cs`, `FreeboxDownloadSettings.cs`, `FreeboxDownloadProxy.cs`, `FreeboxDownloadEncoding.cs`, `FreeboxDownloadException.cs`, `FreeboxDownloadPriority.cs`, `Responses/` (`FreeboxDownloadConfiguration.cs`, `FreeboxDownloadTask.cs`, `FreeboxLogin.cs`, `FreeboxResponse.cs`)
7. `Hadouken/` — `Hadouken.cs`, `HadoukenSettings.cs`, `HadoukenProxy.cs`, `Models/HadoukenSystemInfo.cs`, `Models/HadoukenTorrent.cs`, `Models/HadoukenTorrentResponse.cs`, `Models/HadoukenTorrentState.cs`
8. `NzbVortex/` — `NzbVortex.cs`, `NzbVortexSettings.cs`, `NzbVortexProxy.cs`, `NzbVortexAuthenticationException.cs`, `NzbVortexNotLoggedInException.cs`, `NzbVortexFile.cs`, `NzbVortexGroup.cs`, `NzbVortexJsonError.cs`, `NzbVortexLoginResultType.cs`, `NzbVortexPriority.cs`, `NzbVortexQueueItem.cs`, `NzbVortexResultType.cs`, `NzbVortexStateType.cs`, `JsonConverters/` (2 files), `Responses/` (8 DTOs)
9. `Nzbget/` — `Nzbget.cs`, `NzbgetSettings.cs`, `NzbgetProxy.cs`, `ErrorModel.cs`, `JsonError.cs`, `NzbgetCategory.cs`, `NzbgetConfigItem.cs`, `NzbgetGlobalStatus.cs`, `NzbgetHistoryItem.cs`, `NzbgetParameter.cs`, `NzbgetPostQueueItem.cs`, `NzbgetPriority.cs`, `NzbgetQueueItem.cs`, `NzbgetResponse.cs`
10. `Pneumatic/` — `Pneumatic.cs`, `PneumaticSettings.cs` (XBMC/Kodi `.strm`-file download client)
11. `QBittorrent/` — `QBittorrent.cs`, `QBittorrentSettings.cs`, `QBittorrentProxySelector.cs`, `QBittorrentProxyV1.cs`, `QBittorrentProxyV2.cs`, `QBittorrentContentLayout.cs`, `QBittorrentLabel.cs`, `QBittorrentPreferences.cs`, `QBittorrentPriority.cs`, `QBittorrentState.cs`, `QBittorrentTorrent.cs`
12. `RQBit/` — `RQBit.cs`, `RQbitSettings.cs`, `RQbitProxy.cs`, `RQBitFile.cs`, `RQBitTorrent.cs`, `ResponseModels/` (`ListTorrentsWithStatsResponse.cs`, `PostTorrentResponse.cs`, `RootResponse.cs`, `TorrentFileResponse.cs`, `TorrentResponse.cs`, `TorrentState.cs`)
13. `Sabnzbd/` — `Sabnzbd.cs`, `SabnzbdSettings.cs`, `SabnzbdProxy.cs`, `SabnzbdCategory.cs`, `SabnzbdDownloadStatus.cs`, `SabnzbdFullStatus.cs`, `SabnzbdHistory.cs`, `SabnzbdHistoryItem.cs`, `SabnzbdJsonError.cs`, `SabnzbdPriority.cs`, `SabnzbdQueue.cs`, `SabnzbdQueueItem.cs`, `JsonConverters/` (3 files), `Responses/` (6 DTOs)
14. `Transmission/` — `Transmission.cs`, `TransmissionBase.cs` (shared with Vuze), `TransmissionConfig.cs`, `TransmissionException.cs`, `TransmissionPriority.cs`, `TransmissionProxy.cs`, `TransmissionResponse.cs`, `TransmissionSettings.cs`, `TransmissionTorrent.cs`, `TransmissionTorrentStatus.cs`
15. `Tribler/` — `TriblerDownloadClient.cs`, `TriblerDownloadClientProxy.cs`, `TriblerDownloadSettings.cs`, `Models/TriblerDownloadClientApi.cs`, `Models/TriblerSettingsApi.cs`
16. `Vuze/` — `Vuze.cs` only (extends `TransmissionBase`, reuses Transmission's RPC proxy — Vuze speaks the Transmission wire protocol)
17. `rTorrent/` — `RTorrent.cs`, `RTorrentSettings.cs`, `RTorrentProxy.cs`, `RTorrentDirectoryValidator.cs`, `RTorrentFault.cs`, `RTorrentPriority.cs`, `RTorrentTorrent.cs`
18. `uTorrent/` — `UTorrent.cs`, `UTorrentSettings.cs`, `UTorrentProxy.cs`, `UTorrentPriority.cs`, `UTorrentResponse.cs`, `UTorrentTorrent.cs`, `UTorrentTorrentCache.cs`, `UTorrentTorrentStatus.cs`, `UtorrentState.cs`

**Total: 18 concrete download-client classes** (`TorrentBlackhole`, `UsenetBlackhole`, `Deluge`, `TorrentDownloadStation`, `UsenetDownloadStation`, `Flood`, `TorrentFreeboxDownload`, `Hadouken`, `NzbVortex`, `Nzbget`, `Pneumatic`, `QBittorrent`, `RQBit`, `Sabnzbd`, `Transmission`, `TriblerDownloadClient`, `Vuze`, `RTorrent`, `UTorrent`) across 5 protocol families: pure-torrent RPC clients, pure-Usenet RPC clients, blackhole (filesystem drop) clients ×2, NAS-integrated clients (Download Station) ×2, and one legacy Kodi-integration client (Pneumatic).

### `NzbDrone.Core/Download/TrackedDownloads/`

| File | Responsibility |
|---|---|
| `TrackedDownload.cs` | The lifecycle aggregate — see §6. |
| `TrackedDownloadService.cs` | In-memory cache (keyed by `DownloadId`) of every `TrackedDownload`; reconciles a fresh `DownloadClientItem` against history/parsing to rebuild `RemoteEpisode`; reacts to series add/edit/delete/refresh to re-parse cached items. |
| `DownloadMonitoringService.cs` | The polling loop driver — debounced 5s refresh, iterates all download-handling-enabled clients, calls `GetItems()`, tracks each item, runs `FailedDownloadService.Check()` + `CompletedDownloadService.Check()` inline, then fires `ProcessMonitoredDownloadsCommand`. |
| `TrackedDownloadAlreadyImported.cs` | Cross-checks per-episode history to decide if a download that looks incomplete was actually already fully imported (handles partial re-scan races). |
| `TrackedDownloadRefreshedEvent.cs`, `TrackedDownloadsRemovedEvent.cs`, `TrackedDownloadStatusMessage.cs` | Event/DTO types. |

### `NzbDrone.Core/Download/Pending/`

| File | Responsibility |
|---|---|
| `PendingRelease.cs` | A release that was matched by the decision engine but deferred (not yet sent to a client) — e.g. waiting for a delay profile, waiting for a download client to come back online, or waiting on RSS-sync grouping. |
| `PendingReleaseReason.cs` | Enum of why a release is pending (includes `DownloadClientUnavailable`, which `DownloadService.DownloadReport` reads to decide whether to filter out blocked clients). |
| `PendingReleaseRepository.cs` | Persistence. |
| `PendingReleaseService.cs` | Promotes pending releases into real grabs once conditions clear; de-duplicates competing pending releases for the same episode. |
| `PendingReleasesUpdatedEvent.cs` | Event. |

### `NzbDrone.Core/RemotePathMappings/`

| File | Responsibility |
|---|---|
| `RemotePathMapping.cs` | `{ Host, RemotePath, LocalPath }` model. |
| `RemotePathMappingRepository.cs` | Persistence. |
| `RemotePathMappingService.cs` | `RemapRemoteToLocal` / `RemapLocalToRemote` — longest-prefix path substring rewriting, keyed by client host (see §7.2). |

### `NzbDrone.Core/Queue/`

| File | Responsibility |
|---|---|
| `Queue.cs` | The user-facing queue row DTO — projection of a `TrackedDownload` (see §5). |
| `QueueService.cs` | Maintains the current in-memory queue list; rebuilds it wholesale on every `TrackedDownloadRefreshedEvent`; computes a stable per-row `Id` via `HashConverter.GetHashInt31`. |
| `QueueStatus.cs` | UI-facing status enum, separate from `DownloadItemStatus` (see §5). |
| `ObsoleteQueueService.cs`, `ObsoleteQueueUpdatedEvent.cs` | Legacy/back-compat queue shape kept for old API consumers. |
| `TimeleftComparer.cs`, `DatetimeComparer.cs` | Sort comparators for queue ordering (nulls-last time-remaining sort). |
| `QueueUpdatedEvent.cs` | Event fired after every queue rebuild. |

---

## 2. `IDownloadClient` interface contract

File: `Download/IDownloadClient.cs` (extends `IProvider`, the generic plugin-provider contract).

```csharp
public interface IDownloadClient : IProvider
{
    DownloadProtocol Protocol { get; }
    Task<string> Download(RemoteEpisode remoteEpisode, IIndexer indexer);
    IEnumerable<DownloadClientItem> GetItems();
    DownloadClientItem GetImportItem(DownloadClientItem item, DownloadClientItem previousImportAttempt);
    void RemoveItem(DownloadClientItem item, bool deleteData);
    DownloadClientInfo GetStatus();
    void MarkItemAsImported(DownloadClientItem downloadClientItem);
}
```

Every client **must** implement:
1. **`Download(remoteEpisode, indexer)`** — submit a release; returns the client-native download id (info hash for torrents, queue/nzb id for Usenet) or `null` for fire-and-forget clients (blackhole, Pneumatic) that can't report an id back.
2. **`GetItems()`** — enumerate everything currently known to the client (active + recently completed/failed), mapped into `DownloadClientItem`. This is the **sole polling surface** — there is no push/webhook model.
3. **`RemoveItem(item, deleteData)`** — remove from the client's queue/history, optionally deleting downloaded data. Clients that structurally cannot honor `deleteData=false` (blackhole) throw `NotSupportedException`.
4. **`GetStatus()`** — cheap, cacheable snapshot used for (a) validating the configured category/output folder resolves to a real path, and (b) telling the import path where files will land before the item even completes.
5. **`GetImportItem(item, previousImportAttempt)`** *(optional override, default passthrough)* — a chance to lazily resolve `OutputPath` right before import for clients whose list API doesn't return it cheaply (e.g. qBittorrent needs a second `GetTorrentFiles` call pre-2.6.1; Flood needs `GetTorrentContentPaths`).
6. **`MarkItemAsImported(item)`** *(optional override, default throws `NotSupportedException`)* — post-import hook to relabel/retag the item in the client (see §10.3 "post-import category").

`DownloadClientBase<TSettings>` supplies for free: the `Test()`/`Test(failures)` wrapper pattern, a shared Polly retry pipeline (2 retries, exponential backoff+jitter, on 5xx/408/`HttpException` with server error), `TestFolder()` validation helper, and `DeleteItemData()` (folder-or-file delete with existence checks and swallowed I/O errors logged as warnings).

`TorrentClientBase<TSettings>` and `UsenetClientBase<TSettings>` sit between `DownloadClientBase` and the concrete client, and own protocol-specific `Download()` orchestration (§3).

---

## 3. Torrent vs Usenet protocol differences

Both live in `Download/TorrentClientBase.cs` and `Download/UsenetClientBase.cs`. Concrete clients only implement the abstract `AddFrom*` primitives; the base classes own fetch/validate/error-translation.

### Torrent (`TorrentClientBase<TSettings>`)
- `Protocol => DownloadProtocol.Torrent`.
- `PreferTorrentFile` (virtual, default `false`) — when `true` (Blackhole only), the base class tries the `.torrent` URL first and falls back to magnet only on exception; when `false` (every RPC client), magnet is tried first and falls back to `.torrent` URL only if the client throws `NotSupportedException` for magnets.
- Two add primitives: `AddFromMagnetLink(remoteEpisode, hash, magnetLink)` and `AddFromTorrentFile(remoteEpisode, hash, filename, fileContent)`.
- Magnet hash is extracted client-side via `MonoTorrent.MagnetLink.Parse(...).InfoHashes.V1OrV2.ToHex()` **before** handing to the client, so Sonarr always knows the expected hash even for magnet adds.
- `.torrent` file downloads go through the shared retry pipeline with `Accept: application/x-bittorrent`, `AllowAutoRedirect = false` so 301/302/303 redirects to a `magnet:` URL are caught and redirected into the magnet path instead of being blindly followed.
- `EnsureReleaseIsNotBlocklisted()` runs **before** the actual add call: if the release didn't come from an interactive search and the indexer's `RejectBlocklistedTorrentHashesWhileGrabbing` setting is on, a hash that is already blocklisted for this series throws `ReleaseBlockedException` and the download is aborted client-side without ever touching the download client.
- After add, if the client returned an `actualHash` different from the expected parsed hash, a debug log warns that tracking may be lost (no hard failure — some clients don't echo the hash back reliably).
- 404/410 from the tracker/indexer → `ReleaseUnavailableException` (this release is gone, don't retry). 429 → logged specially ("API Grab Limit reached") and the indexer's failure counter is incremented with the http429 retry-after value.

### Usenet (`UsenetClientBase<TSettings>`)
- `Protocol => DownloadProtocol.Usenet`.
- Single add primitive: `AddFromNzbFile(remoteEpisode, filename, fileContent)` — no magnet-equivalent duality.
- `AllowAutoRedirect = true` (unlike torrent) since NZB download URLs commonly redirect through indexer proxies without special handling needed.
- After download, **`IValidateNzbs.Validate(filename, nzbData)`** runs — a full XML well-formedness / minimal-content check — before ever calling `AddFromNzbFile`. A malformed body throws `InvalidNzbException`, which prevents garbage from ever reaching SABnzbd/NZBGet/etc.
- No blocklist pre-check equivalent to the torrent hash check (NZB releases are blocklisted by title, not a content-addressed hash — see `BlocklistService.Blocklisted` §8.3).

### Shared retry policy (`DownloadClientBase.RetryStrategy`)
A `Polly.ResiliencePipeline<HttpResponse>` used by both base classes for the release-fetch HTTP call only (not for talking to the download-client API itself): retries up to 2 times on 5xx or 408, exponential backoff starting at 3s with jitter, logs each retry attempt at Info level with the client/indexer name.

---

## 4. `DownloadClientItem` — every tracked field

File: `Download/DownloadClientItem.cs`.

```csharp
public class DownloadClientItem
{
    public DownloadClientItemClientInfo DownloadClientInfo { get; set; }
    public string DownloadId { get; set; }
    public string Category { get; set; }
    public string Title { get; set; }
    public long TotalSize { get; set; }
    public long RemainingSize { get; set; }
    public TimeSpan? RemainingTime { get; set; }
    public double? SeedRatio { get; set; }
    public OsPath OutputPath { get; set; }
    public string Message { get; set; }
    public DownloadItemStatus Status { get; set; }
    public bool IsEncrypted { get; set; }
    public bool CanMoveFiles { get; set; }
    public bool CanBeRemoved { get; set; }

    public DownloadClientItem Clone(); // MemberwiseClone
}
```

Field semantics:
- **`DownloadId`** — client-native identifier (uppercased info hash for torrents by convention; numeric/opaque id for Usenet clients). This is the join key used everywhere (`TrackedDownload`, `Queue`, `History`, `Blocklist`).
- **`Category`** — the client-side label/category the item currently carries; used both to filter `GetItems()` results down to Sonarr's own downloads and, on the CDH side, to gate auto-import when there's no grab history (`CompletedDownloadService.Check`).
- **`TotalSize` / `RemainingSize`** — bytes; `RemainingSize == 0` combined with completion status is one of several "is this actually done" signals per-client.
- **`RemainingTime`** — nullable `TimeSpan`; `null` means "unknown ETA" (queued items, or a client that reports a sentinel like qBittorrent's `eta=8640000`). Feeds `Queue.EstimatedCompletionTime = UtcNow + RemainingTime`.
- **`SeedRatio`** — nullable upload/download ratio, used by seeding-rule evaluation (§11) for clients that don't expose native "reached ratio" flags Sonarr can just read directly (rTorrent, RQBit, Flood, Freebox).
- **`OutputPath`** — an `OsPath` (dual Windows/Unix-aware path value type). Left `.IsEmpty` until the client can resolve the actual on-disk output location (some clients only know this once status is `Completed`). This is the path CDH imports from, subject to remote-path-mapping rewriting (§7.2).
- **`Message`** — free-text status detail surfaced to the queue UI (error detail, "stalled", "DHT disabled", localized strings via `ILocalizationService`).
- **`Status`** — the universal `DownloadItemStatus` enum: `Queued(0)`, `Paused(1)`, `Downloading(2)`, `Completed(3)`, `Failed(4)`, `Warning(5)`. Every client's native state machine (qBittorrent's dozen string states, Deluge's status enum, NZBGet's per-field completion codes, etc.) is folded down into these six values.
- **`IsEncrypted`** — currently only set `true` by SABnzbd when a history item's title is prefixed `ENCRYPTED /` (password-protected/obfuscated release); triggers automatic-failure handling (§8.1) since Sonarr can't process it.
- **`CanMoveFiles`** — whether Sonarr is allowed to move/hardlink the output right now (import-safety gate distinct from `CanBeRemoved`: a paused-but-not-seed-limit-reached torrent should not have its files touched even though it's "done downloading," because the client still owns/needs the files on disk for seeding integrity checks).
- **`CanBeRemoved`** — whether the item has satisfied its retention/seeding policy and Sonarr may call `RemoveItem()` on it. This is the single field seeding-rule logic (§11) and completed/failed cleanup (§10) both gate on.

### `DownloadClientItemClientInfo` (nested)
```csharp
public class DownloadClientItemClientInfo
{
    public DownloadProtocol Protocol { get; set; }
    public string Type { get; set; }                 // the client's Name, e.g. "qBittorrent"
    public int Id { get; set; }
    public string Name { get; set; }                  // user-given instance name
    public bool RemoveCompletedDownloads { get; set; } // copied from DownloadClientDefinition
    public bool HasPostImportCategory { get; set; }    // true if a distinct post-import category/tag/label is configured
}
```
Built once per poll via `DownloadClientItemClientInfo.FromDownloadClient(client, hasPostImportCategory)` and stamped onto every item that client returns — this is how downstream code (`Queue`, `DownloadEventHub`) knows which client+definition an item came from without a second lookup.

---

## 5. Queue projection (`Queue.cs` / `QueueService.cs` / `QueueStatus.cs`)

`Queue` (user-facing row, distinct type from `DownloadClientItem`):
```csharp
public class Queue : ModelBase
{
    public Series Series { get; set; }
    public int? SeasonNumber { get; set; }
    [Obsolete] public Episode Episode { get; set; }
    public List<Episode> Episodes { get; set; }
    public List<Language> Languages { get; set; }
    public QualityModel Quality { get; set; }
    public decimal Size { get; set; }
    public string Title { get; set; }
    public decimal SizeLeft { get; set; }
    public TimeSpan? TimeLeft { get; set; }
    public DateTime? EstimatedCompletionTime { get; set; }
    public DateTime? Added { get; set; }
    public QueueStatus Status { get; set; }
    public TrackedDownloadStatus? TrackedDownloadStatus { get; set; }
    public TrackedDownloadState? TrackedDownloadState { get; set; }
    public List<TrackedDownloadStatusMessage> StatusMessages { get; set; }
    public string DownloadId { get; set; }
    public RemoteEpisode RemoteEpisode { get; set; }
    public DownloadProtocol Protocol { get; set; }
    public string DownloadClient { get; set; }
    public bool DownloadClientHasPostImportCategory { get; set; }
    public string Indexer { get; set; }
    public string OutputPath { get; set; }
    public string ErrorMessage { get; set; }
}
```

`QueueService` behavior:
- Rebuilds the **entire** static `_queue` list from scratch on every `TrackedDownloadRefreshedEvent` — no incremental diffing.
- Only includes `TrackedDownload`s where `IsTrackable == true` (see §6 lifecycle).
- Sorted by `RemainingTime` ascending (soonest-first; nulls sort last via natural nullable ordering — see `TimeleftComparer.cs` for the actual comparator used elsewhere).
- Row `Id` is a **deterministic hash** of `"trackedDownload-{downloadClientId}-{downloadId}"` via `HashConverter.GetHashInt31` — this means the same queue row keeps the same numeric id across polls (important for the frontend to track row identity without a real DB row).
- One `Queue` row is emitted **per download**, not per episode (multi-episode/season-pack downloads produce one row carrying `Episodes: List<Episode>`; season number is `RemoteEpisode.MappedSeasonNumber`).
- `QueueStatus` enum (distinct from `DownloadItemStatus`) has 10 members: `Unknown, Queued, Paused, Downloading, Completed, Failed, Warning, Delay, DownloadClientUnavailable, Fallback`. Only the first six are populated from `DownloadItemStatus` via `Enum.TryParse` (name-matching); the last four (`Delay`, `DownloadClientUnavailable`, `Fallback`) are reserved for **pending release** rows that never made it to a client yet (populated elsewhere, in the pending-release → queue projection, not shown in this slice but referenced by `PendingReleaseReason`).

---

## 6. `TrackedDownload` lifecycle

File: `Download/TrackedDownloads/TrackedDownload.cs`.

```csharp
public class TrackedDownload
{
    public int DownloadClient { get; set; }
    public DownloadClientItem DownloadItem { get; set; }
    public DownloadClientItem ImportItem { get; set; }
    public TrackedDownloadState State { get; set; }
    public TrackedDownloadStatus Status { get; private set; }   // Ok | Warning | Error
    public RemoteEpisode RemoteEpisode { get; set; }
    public TrackedDownloadStatusMessage[] StatusMessages { get; private set; }
    public DownloadProtocol Protocol { get; set; }
    public string Indexer { get; set; }
    public DateTime? Added { get; set; }
    public bool IsTrackable { get; set; }
    public bool HasNotifiedManualInteractionRequired { get; set; }

    public void Warn(string message, params object[] args);      // sets Status=Warning
    public void Warn(params TrackedDownloadStatusMessage[] msgs); // sets Status=Warning
    public void Fail();                                          // Status=Error, State=FailedPending, DownloadItem.CanBeRemoved=true
}
```

### `TrackedDownloadState` (8 values) — the domain lifecycle state machine
`Downloading → ImportBlocked → ImportPending → Importing → Imported`
`Downloading → FailedPending → Failed`
`(any) → Ignored` (user action)

| State | Meaning | Set by |
|---|---|---|
| `Downloading` | Default/initial; still in progress at the client, or completed but not yet evaluated for import. | `TrackedDownloadService.TrackDownload` (new item) |
| `ImportBlocked` | Completed at the client but automatic import cannot proceed (series mismatch, ID-matched-but-not-interactive, multi-episode partial failure, rejected import). | `CompletedDownloadService.SetStateToImportBlocked` |
| `ImportPending` | Completed, series/episode matched, path validated — queued for the next processing tick to actually run the importer. | `CompletedDownloadService.Check` (success path) and `CompletedDownloadService.Import` (partial-success fallback) |
| `Importing` | Import is actively running right now (very short-lived, in-memory only, exists to prevent double-import in the same tick). | `CompletedDownloadService.Import` (entry) |
| `Imported` | All expected episodes imported and verified (`VerifyImport` returned true). Terminal success state. | `CompletedDownloadService.VerifyImport` |
| `FailedPending` | Detected as failed (client reports failed status, encrypted, or manually marked) but the failure event hasn't been published/blocklisted yet. | `FailedDownloadService.Check`, `TrackedDownload.Fail()` |
| `Failed` | Failure event published, blocklist entry created, redownload decision made. Terminal failure state. | `FailedDownloadService.ProcessFailed` |
| `Ignored` | User explicitly ignored the download. Terminal state. | `IgnoredDownloadService` (via history reconstruction in `GetStateFromHistory`) |

### `TrackedDownloadStatus` (3 values) — orthogonal "health" indicator surfaced alongside `State`
`Ok` (default) → `Warning` (recoverable issue, still trackable) → `Error` (this item is done, in an error state).

### Reconstruction (`TrackedDownloadService.TrackDownload`)
Because the tracked-download cache is **purely in-memory** (`ICached<TrackedDownload>` via `ICacheManager`, no DB table), every Sonarr restart or cache eviction requires rebuilding a `TrackedDownload` from scratch out of a raw `DownloadClientItem`:
1. If an existing cached entry for this `DownloadId` exists and its `State != Downloading`, it's reused as-is (just refresh `DownloadItem` and `IsTrackable`) — **state is sticky** once past the initial downloading phase, so a completed-then-failed transition doesn't get silently reset by a stale poll.
2. Otherwise a new `TrackedDownload` is built:
   - `DownloadHistoryService.GetLatestDownloadHistoryItem(downloadId)` seeds the initial `State` via `GetStateFromHistory` (imported→`Imported`, failed→`Failed`, ignored→`Ignored`, else→`Downloading`).
   - The item's `Title` is parsed via `Parser.ParseTitle`; if that succeeds, `RemoteEpisode` is mapped either against the series recorded in download history (if imported) or with placeholder series/episode ids `(0, 0, null)`.
   - Grab-event `History` rows for this download id are consulted to recover `Indexer` and `Added` (grab timestamp).
   - **Fallback re-parse**: if the initial parse produced no series/episodes, it retries parsing the **original source title** from history (handles the case where the client renamed/folder-ized the release title beyond recognition), including a special-episode parse path.
   - `RemoteEpisodeAggregationService.Augment()` runs to fill in languages etc., then `CustomFormatCalculationService.ParseCustomFormat()` computes custom format matches against the item's actual `TotalSize`.
   - Any `MultipleSeriesFoundException` or generic parse exception results in a `Warn()` rather than a hard failure — the item still shows up in the queue, just unmatched.
3. `IsTrackable` becomes `false` (via `UpdateTrackable`) for any cached item whose `DownloadId` no longer appears in the client's live `GetItems()` result set on a given poll — this is how removed/vanished downloads get dropped from the queue without an explicit remove call.

### Event-driven re-sync
`TrackedDownloadService` also implements `IHandle<EpisodeInfoRefreshedEvent|SeriesAddedEvent|SeriesEditedEvent|SeriesBulkEditedEvent|SeriesDeletedEvent>` — any of these re-parses affected cached `TrackedDownload`s (`UpdateCachedItem`, which just re-runs title parsing + aggregation) and republishes `TrackedDownloadRefreshedEvent` so the queue reflects updated series/episode matching without waiting for the next client poll.

### Poll loop (`DownloadMonitoringService`)
- Debounced to a **5 second** window (`Debouncer(QueueRefresh, TimeSpan.FromSeconds(5))`) — triggered by `EpisodeGrabbedEvent`, `EpisodeImportedEvent`, `ManualInteractionRequiredEvent`, and also runs on the scheduled `RefreshMonitoredDownloadsCommand`.
- For each `DownloadHandlingEnabled()` client (blocked clients filtered out — see §9's status/backoff), calls `GetItems()`; a thrown exception is caught, records a client failure (`DownloadClientStatusService.RecordFailure`), and logs a warning — **that client's items are simply skipped for this poll**, not treated as "all downloads gone."
- Each returned item is fed through `TrackedDownloadService.TrackDownload`, then — **only if** the resulting state is `Downloading` or `ImportBlocked` — immediately runs `FailedDownloadService.Check()` then `CompletedDownloadService.Check()` inline, before the queue is even published. This means failed/completed detection happens at poll time, not at the later processing-command time; the processing command (`ProcessMonitoredDownloadsCommand`) only handles the heavier `Import()`/`ProcessFailed()` work for anything already flagged `ImportPending`/`FailedPending`.
- `DownloadIsTrackable()` filters what actually reaches the queue: `Imported`/`Failed`/`Ignored` states are dropped, and if `EnableCompletedDownloadHandling` is **off**, any item already `Completed` at the client is dropped too (CDH-disabled installs only show in-progress downloads in the queue).

---

## 7. Completed Download Handling (CDH)

File: `Download/CompletedDownloadService.cs`. Two phases, both driven from `DownloadProcessingService`/`DownloadMonitoringService`: `Check()` (gatekeeper, runs every poll) and `Import()` (executor, runs once state is `ImportPending`).

### 7.1 `Check(trackedDownload)` — gate logic
1. No-op unless `DownloadItem.Status == Completed`.
2. `SetImportItem()` — resolves `ImportItem` via `ProvideImportItemService` → the owning client's `GetImportItem()` override (lazy output-path resolution).
3. No-op unless current `State` is `Downloading` or `ImportBlocked` (so already-pending/importing/imported/failed items aren't reprocessed).
4. Looks up the latest `Grabbed` history row for this download id.
   - If there's no grab history **and** no client-side category, warns "Download wasn't grabbed by Sonarr and not in a category, Skipping." and returns — this is the guard against Sonarr trying to auto-import unrelated downloads sitting in the same client.
5. `ValidatePath()` — the resolved `ImportItem.OutputPath` must be non-empty and must be a path shape matching the **host OS** (`OsInfo.IsWindows` → must be `IsWindowsPath`; else must be `IsUnixPath`). A path that looks like the wrong OS's path format triggers a warning pointing at Remote Path Mappings and blocks import — this is the primary UX signal that a remote-path-mapping is needed/misconfigured.
6. Series resolution: try parsing the *client item's title* directly first; if that fails, fall back to the series recorded in the grab-history row.
   - If still unresolved: warn "Series title mismatch; automatic import is not possible..." and set state to `ImportBlocked`.
   - If the historical match was recorded as `SeriesMatchType.Id` **and** the original release source was not `InteractiveSearch`: warn "matched to series by ID... Automatic import is not possible" and block — Sonarr treats ID-based auto-matches as too risky to blind-import unless a human explicitly picked that release.
7. On success, `State = ImportPending`.

### 7.2 `Import(trackedDownload)` — executor
1. Re-resolves `ImportItem` and re-validates the path (same as `Check`).
2. If `RemoteEpisode` is still null, warns and sets `ImportBlocked` (this can happen if title parsing regressed since `Check` ran).
3. Sets `State = Importing`, then calls `IDownloadedEpisodesImportService.ProcessPath(outputPath, ImportMode.Auto, series, importItem)` — the actual file-mover/import pipeline (outside this slice).
4. If `VerifyImport()` (below) returns true, done.
5. Otherwise reverts to `ImportPending` (will retry next tick) and inspects `importResults`:
   - Empty results → warn "No files found are eligible for import..." and stop (stay `ImportPending`, will keep retrying every tick until something changes or a human intervenes).
   - Exactly one result and it was `Rejected` → delegate to `RejectedImportService.Process()` (§7.3); if that doesn't set `FailedPending`, block for manual interaction.
   - Exactly one result rejected specifically for `ImportRejectionReason.MultiSeason` → warn with the result's error text and block (multi-season packs need explicit handling, not silent partial-import).
   - Otherwise (multiple results, mixed outcome): build a warning message "One or more episodes expected in this release were not imported or missing from the release" plus one line per non-imported file (sorted by path), then set `ImportBlocked`.

### 7.3 Import verification (`VerifyImport`)
- **Primary check**: total episodes actually imported in this call ≥ `Max(1, RemoteEpisode.Episodes.Count)`. If satisfied → `State = Imported`, publish `DownloadCompletedEvent` (carrying the imported `EpisodeFile`s and a `GrabbedReleaseInfo` built from the grab-history rows), return true.
- **Fallback check** (handles decision-engine rejecting already-imported files, or partial retry runs): if not all imported *this call*, cross-reference **download history** via `TrackedDownloadAlreadyImported.IsImported()` — true only if *every* episode in `RemoteEpisode.Episodes` has a `DownloadFolderImported` history event as its most recent event. If that's true, still treat as fully imported (publishes the same `DownloadCompletedEvent`, using whatever files are currently on the episodes). A documented edge case: this assumes `EpisodeId` stability across time — if a series refresh removes then re-adds an episode with a new id, Sonarr will incorrectly treat it as not-yet-imported (explicitly called out as an accepted, rare risk in a code comment).
- If neither check passes, logs and returns false (stays `ImportPending`).

### 7.4 Manual interaction notification
`SetStateToImportBlocked()` sets `ImportBlocked` and — **only the first time** for a given `TrackedDownload` (`HasNotifiedManualInteractionRequired` latch) — publishes `ManualInteractionRequiredEvent` carrying a `GrabbedReleaseInfo` built from history. This is what drives a one-time notification (webhook/notification providers) rather than re-notifying on every poll tick while blocked.

### 7.5 Rejected-import → fail-or-warn policy (`RejectedImportService`)
Only engages when `ImportResultType.Rejected` and the tracked release info is available. Looks up **per-indexer** settings (`ICachedIndexerSettingsProvider`, keyed by `RemoteEpisode.Release.IndexerId`) for a `FailDownloads` flag set. Three specific rejection reasons can be escalated from "warn and block" to "hard fail" if the owning indexer opted in:
- `ImportRejectionReason.DangerousFile` + `FailDownloads.PotentiallyDangerous`
- `ImportRejectionReason.ExecutableFile` + `FailDownloads.Executables`
- `ImportRejectionReason.UserRejectedExtension` + `FailDownloads.UserDefinedExtensions`

Any of these calls `trackedDownload.Fail()` (→ `FailedPending`, `CanBeRemoved = true`) instead of just warning — this is Sonarr's content-safety gate (refusing to leave a download sitting around if it contains a disguised executable, for indexers configured to be strict about it). If the indexer has no settings resolvable at all, always just warns (never silently proceeds without at least surfacing the rejection).

### 7.6 Remote Path Mappings (client path → Sonarr path)
Files: `RemotePathMappings/RemotePathMapping.cs`, `RemotePathMappingService.cs`.

Model: `{ Host, RemotePath, LocalPath }` (all plain strings; `LocalPath`/`RemotePath` normalized to directory form — trailing separator — on insert).

`RemapRemoteToLocal(host, remotePath)` / `RemapLocalToRemote(host, localPath)`:
- Loaded via a 10-second cache (`ICached<List<RemotePathMapping>>`) so repeated polls don't hit the DB every time; cache is force-cleared on Add/Remove/Update.
- Matching is **case-insensitive host match** + **prefix containment** (`OsPath.Contains`) — the first mapping whose `Host` equals the given host (invariant-culture, case-insensitive) and whose `RemotePath` is a path-prefix of the given path wins; the matched prefix is stripped and replaced with `LocalPath`.
- No match → the input path is returned unchanged (i.e., mapping is opt-in per-host; if you don't configure one, Sonarr assumes client and Sonarr see the same filesystem).
- Validation on `Add`/`Update`: `Host` required; `RemotePath` cannot be empty or start with a space; `LocalPath` cannot be empty and must be rooted (absolute); `LocalPath` must exist on disk (`DirectoryNotFoundException` if not); duplicate `(Host, RemotePath)` pairs rejected.
- This service is injected into every download-client class via `IRemotePathMappingService` and is called on essentially every `GetItems()`/`GetStatus()` implementation to translate the client's own reported paths into paths Sonarr's host can actually read.

---

## 8. Failed Download Handling (FDH)

File: `Download/FailedDownloadService.cs` (detection/dispatch) + `Download/RedownloadFailedDownloadService.cs` (auto re-search) + `Blocklisting/BlocklistService.cs` (memory).

### 8.1 What counts as "failed"
`Check(trackedDownload)` (called inline during the poll, same as CDH's `Check`) only evaluates items currently `Downloading` or `ImportBlocked`. A download is flagged failed (`State = FailedPending`) if **either**:
- `DownloadItem.IsEncrypted == true` (currently only ever set by SABnzbd's `ENCRYPTED /` title prefix detection), **or**
- `DownloadItem.Status == DownloadItemStatus.Failed` (the client itself reports failure — e.g. NZBGet health-check failure, SABnzbd history status `Failed`, qBittorrent `error` state surfaced as `Warning` is explicitly *not* auto-failed this way, only `Failed` items are).

Guard: if there's no `Grabbed` history entry for this download id at all, it warns and skips — Sonarr will not auto-fail (and therefore never auto-blocklist/redownload) something it didn't grab itself.

Failure message precedence in `ProcessFailed()`: `IsEncrypted` → "Encrypted download detected"; else if `Status == Failed` and the client supplied a `Message` → use that message; else generic "Failed download detected".

### 8.2 Manual "mark as failed"
`IFailedDownloadService.MarkAsFailed(historyId, message, source, skipRedownload)` and the `TrackedDownload`-based overload are the entry points the API uses for the user-initiated "Mark as Failed" action. Both ultimately call `PublishDownloadFailedEvent`, which:
- Resolves `ReleaseSourceType` from the grab-history row's stored `Data["releaseSource"]`.
- Builds and publishes a `DownloadFailedEvent` carrying `SeriesId`, all affected `EpisodeIds` (deduped across every grabbed-history row sharing this download id — handles multi-episode grabs), `Quality`, `SourceTitle`, `DownloadClient`, `DownloadId`, the message/source, the full history `Data` dictionary, the `TrackedDownload` (if available), `Languages`, `SkipRedownload`, and `ReleaseSource`.
- `MarkAsFailed(historyId, ...)` has a special case: if the history row has no `DownloadId` at all (meaning it's not a real grab, e.g. a placeholder) but *is* itself a `Grabbed` event, it still publishes a failed event scoped to just that one episode; otherwise it throws `InvalidOperationException`.

### 8.3 Blocklisting (`BlocklistService`)
Reacts to `DownloadFailedEvent` (`IHandle<DownloadFailedEvent>`) and inserts a `Blocklist` row capturing: `SeriesId`, `EpisodeIds`, `SourceTitle`, `Quality`, `Date`, `PublishedDate` (parsed from event data), `Size`, `Indexer`, `Protocol`, `Message`, `Source`, `Languages`, `IndexerFlags`, `ReleaseType`, and — for torrents — `TorrentInfoHash` (taken directly from the tracked download's `DownloadId` when protocol is `Torrent`, else from event data).

Duplicate-grab prevention (`Blocklisted(seriesId, release)`):
- **Torrent**: matched by `TorrentInfoHash` when the release carries one (`BlocklistedByTorrentInfoHash` + `ReleaseComparer.SameTorrent`), else falls back to title-based matching filtered to `Protocol == Torrent`.
- **Usenet**: always title-based (`BlocklistedByTitle` filtered to `Protocol == Usenet`, compared via `ReleaseComparer.SameNzb`).
- `BlocklistedTorrentHash(seriesId, hash)` is the specific check `TorrentClientBase.EnsureReleaseIsNotBlocklisted` uses pre-grab (§3) — this is what stops Sonarr re-sending a hash it already knows failed, for indexers with `RejectBlocklistedTorrentHashesWhileGrabbing` enabled.
- `ClearBlocklistCommand` purges everything; per-item `Delete(id)`/`Delete(ids)` for the UI's remove action; blocklist rows for a series are hard-deleted (`DeleteForSeriesIds`) when the series itself is deleted.

### 8.4 Automatic redownload (`RedownloadFailedDownloadService`)
Reacts to `DownloadFailedEvent` with `[EventHandleOrder(EventHandleOrder.Last)]` (guaranteed to run after blocklisting and history recording). Decision tree:
1. `message.SkipRedownload == true` → do nothing (manual "mark as failed without search" path).
2. `ConfigService.AutoRedownloadFailed == false` → do nothing (global off-switch).
3. `ReleaseSource == InteractiveSearch` **and** `ConfigService.AutoRedownloadFailedFromInteractiveSearch == false` → do nothing (separate off-switch specifically for manually-triggered grabs, since a human already chose that release once).
4. Exactly 1 episode failed → push `EpisodeSearchCommand` for just that episode.
5. Episode count == the full episode count of that season (`EpisodeService.GetEpisodesBySeason`) → push `SeasonSearchCommand` for the whole season (season-pack failure re-searches as a season, not N individual episode searches).
6. Otherwise (partial multi-episode, e.g. a double-episode file) → push `EpisodeSearchCommand` for just the affected ids.

### 8.5 Cleanup after failure (`DownloadEventHub.Handle(DownloadFailedEvent)`)
Only removes the item from the download client if `DownloadItem.CanBeRemoved` is true **and** the owning `DownloadClientDefinition.RemoveFailedDownloads` is true (a distinct, separately-configurable flag from `RemoveCompletedDownloads`). Failure to remove (`NotSupportedException`, or any other exception) is logged, not rethrown — a client that can't remove failed items still keeps Sonarr's own state machine (blocklist, history, redownload) working.

---

## 9. Download client priority / rotation / selection

File: `Download/DownloadClientProvider.cs` (selection logic) + `Download/DownloadService.cs` (call-site fallback loop) + `Download/DownloadClientFactory.cs` (enable/blocked filtering) + `DownloadClientDefinition.Priority` (config surface).

### 9.1 Per-client configuration surface
`DownloadClientDefinition` (extends the generic `ProviderDefinition`):
- `Priority` (int, default `1`) — **lower number = higher priority**, grouped (not strictly ordered) — see below.
- `RemoveCompletedDownloads` (bool, default `true`).
- `RemoveFailedDownloads` (bool, default `true`).
- `Tags` (inherited from `ProviderDefinition`) — the category/tag gating mechanism (§9.3).
- Plus whatever `Enable` flag the generic provider system exposes.

### 9.2 Selection algorithm (`GetFilteredDownloadClients`)
Shared by both the "give me one client" and "give me an ordered list" entry points:
1. Filter to clients whose `Protocol` matches the release's protocol (Torrent vs Usenet — a Usenet release will never be routed to a torrent client or vice versa).
2. **Tag matching**: if the series has tags, prefer clients whose `Tags` intersect the series' tags; if none match, fall back to clients with **no tags configured at all**. If after this filter *zero* clients remain, throws `DownloadClientUnavailableException("No download client was found without tags or a matching series tag...")` — i.e., a tagged series can never fall through to an *other-tagged* client, only to an untagged one.
3. **Indexer-pinned client**: if the release's indexer has an explicit `DownloadClientId` configured, that exact client is used (bypassing priority/rotation entirely) — but still subject to the blocked-client check if `filterBlockedClients` is requested; a missing or blocked pinned client throws `DownloadClientUnavailableException` rather than silently falling back to another client.
4. **Blocked-client handling**: clients currently in backoff (`DownloadClientStatusService.GetBlockedProviders()`, keyed by recent-failure escalation, 5 min minimum suppression up to 5 escalation levels) are excluded if any non-blocked candidates remain. If **all** candidates for this protocol are blocked: when `filterBlockedClients` is true, throws `DownloadClientUnavailableException("All download clients for {protocol} are not available")`; when false, logs a trace and returns the full blocked set anyway (used by the "single client" `GetDownloadClient` path, which will simply retry the blocked one rather than have nothing).

### 9.3 Priority grouping + round robin (both selection modes group by `Priority` first, ascending — i.e. group 1 exhausted before group 2 is even considered)
- **`GetDownloadClient` (single client)**: takes only the lowest-numbered priority group, orders that group by `Definition.Id`, and round-robins **within that group only** using a cached `lastDownloadClientId` per protocol (`ICached<int>` keyed by `DownloadProtocol.ToString()`): picks the first client with `Id > lastId`, wrapping to the first client in the group if none is greater. This is a strict, deterministic round-robin — every successful use of a client (`ReportSuccessfulDownloadClient`) advances the cursor regardless of whether that call actually succeeds end-to-end.
- **`GetDownloadClients` (fallback-ordered list, used by `DownloadService.DownloadReport`)**: builds an ordered list across **all** priority groups (lowest first), and *within* each group rotates the starting point based on the same `lastUsedDownloadClient` cursor (clients after the last-used one first, then wrap to the ones before it, with the last-used one placed at the very end of its group) — this produces a full priority-then-rotation ordering so the retry loop in `DownloadService` always tries same-priority clients before ever touching a lower-priority group.

### 9.4 Fallback-on-failure at grab time (`DownloadService.DownloadReport`)
- Resolves either one **specific** client (if the release/search explicitly requested `downloadClientId`) or the full protocol-ordered fallback list above.
- `filterBlockedClients` is derived from whether the release's `PendingReleaseReason == DownloadClientUnavailable` (i.e., a release that was deferred because its client was down gets a stricter "don't even try blocked clients" pass when finally retried).
- No available clients at all → `DownloadClientUnavailableException`.
- Iterates the ordered candidate list, skipping any client id already tried in this call (`triedClients` HashSet — defends against the rotation logic somehow returning the same id twice). On success: reports back to the provider (`ReportSuccessfulDownloadClient`, advancing the rotation cursor) and returns immediately — **does not** continue trying other clients once one succeeds.
- On `DownloadClientException` (or any exception other than `ReleaseDownloadException`, which is explicitly rethrown to abort the whole loop — a hard release-level failure like "torrent file 404" should not cause Sonarr to blindly try the same broken release against every other client): logs at Trace and marks that client tried, moves to the next candidate.
- If every candidate is exhausted without success: `DownloadClientUnavailableException("All '{0}' download clients failed", protocol)`.
- Before actually calling the client, applies a **global rate limit of 1 request per 2 seconds per download-URL host** (`IRateLimitService.WaitAndPulseAsync`) for non-magnet URLs — protects indexers from being hammered by rapid-fire grabs.
- Also resolves `remoteEpisode.SeedConfiguration` from the indexer's `ISeedConfigProvider` right before dispatch (full-season vs single-episode seed rules, §11) and attaches the owning `IIndexer` instance (for indexer-specific download-request headers/auth) if the release has an `IndexerId`.
- On success, publishes `EpisodeGrabbedEvent` (carrying `DownloadClient` name, `DownloadClientId`, `DownloadClientName`, and — if the client returned one — `DownloadId`), and records success against both the download-client status service and indexer status service.
- Specific downstream exceptions (`ReleaseUnavailableException`, `ReleaseBlockedException`, `DownloadClientRejectedReleaseException`, `ReleaseDownloadException`) are all logged distinctly and rethrown — `ReleaseDownloadException` additionally increments the **indexer's** failure counter (with 429 retry-after awareness), separate from the download client's own failure counter.

### 9.5 Per-client categories/tags (the actual "category" config field)
Every RPC-based client exposes its own category/label/tag setting in its own settings class (not a shared cross-client field) — see the settings enumeration in §12. These are used two ways: (a) filtering `GetItems()` down to only items Sonarr should be looking at (most clients: qBittorrent, Deluge, NZBGet, SABnzbd, rTorrent, uTorrent, Transmission, Freebox, Download Station, RQBit, Tribler), and (b) as the destination the client is instructed to file the item under when adding it. Flood is unique in using a **set** of tags rather than a single category (`Tags: IEnumerable<string>`), with support for both fixed tags and dynamically computed "additional tags" (§12.6). Several clients (Aria2, Hadouken uses `Category` not gating on add, Vuze/Transmission-family) either have no add-time category concept or gate purely by directory instead.

---

## 10. Post-completion cleanup & "post-import category" semantics

File: `Download/DownloadEventHub.cs`.

Reacts to three events, always resolving the owning `IDownloadClient`/`DownloadClientDefinition` fresh via `IProvideDownloadClient.Get(trackedDownload.DownloadClient)`:

- **`DownloadCompletedEvent`**: first calls `MarkItemAsImported()` on the client (best-effort, catches `NotSupportedException` quietly and any other exception as an error log) — this is the "post-import category/label/tag" hook (§10.3). Then, only if `DownloadItem.CanBeRemoved` and status is not still `Downloading` and `RemoveCompletedDownloads` is true, calls `RemoveItem(item, deleteData: true)` and stops tracking.
- **`DownloadFailedEvent`**: if `CanBeRemoved` and `RemoveFailedDownloads` is true, removes with `deleteData: true` and stops tracking (§8.5).
- **`DownloadCanBeRemovedEvent`** (published by `DownloadProcessingService.RemoveCompletedDownloads()` for anything already `Imported` with `CanBeRemoved`): same removal path, gated the same way on `RemoveCompletedDownloads`.

All three removal paths always pass `deleteData: true` — Sonarr never asks a client to "remove from queue but leave the file," consistent with the fact that by this point the file has already been imported (moved/hardlinked) elsewhere.

### 10.3 "Post-import category" pattern
A recurring, near-identical pattern across many clients' `MarkItemAsImported` overrides: if a distinct `TvImportedCategory`/`PostImportCategory` setting is configured (and differs from the pre-import category), relabel the item to that category/label/tag after import completes, so a user's client UI can visually distinguish "still needing Sonarr's attention" from "done, informational only" downloads. Implemented by: QBittorrent (`SetTorrentLabel`), Transmission (only when `SupportsLabels`, i.e. client version ≥ 4.0 — adds the new label and explicitly removes the old one from the label set), Deluge (`SetTorrentLabel`, swallows `DownloadClientUnavailableException` with a warning if the label doesn't exist), rTorrent (`SetTorrentLabel` + additionally pushes the item into a synthesized `"{appname}_imported"` view via `PushTorrentUniqueView` — surfaced to the user via a `ProviderMessage` telling them what view name to expect), uTorrent (`SetTorrentLabel` + explicit `RemoveTorrentLabel` of the old category, since uTorrent doesn't overwrite), Flood (adds `PostImportTags` to the existing tag set via `SetTorrentsTags`, only if any are configured). Clients without this concept (Blackhole, Pneumatic, most Usenet clients, Aria2, Hadouken, FreeboxDownload, RQBit, Tribler, NzbVortex, NZBGet, SABnzbd, DownloadStation) simply don't override `MarkItemAsImported` and inherit the base's `NotSupportedException` throw (caught and logged, not surfaced as an error to the user).

---

## 11. Seeding rules (torrent-only)

Files: `Download/Clients/TorrentSeedConfiguration.cs`, `Download/DownloadSeedConfigProvider.cs`, `Indexers/SeedConfigProvider.cs` (indexer-level source of truth, outside this directory but the origin of the values), plus each torrent client's own `HasReachedSeedLimit`-equivalent.

`TorrentSeedConfiguration { double? Ratio; TimeSpan? SeedTime; }` — computed once per release at grab time (`DownloadService.DownloadReport` calls `ISeedConfigProvider.GetSeedConfiguration(remoteEpisode)`, which derives Ratio/SeedTime from the release's owning indexer's settings, distinguishing full-season vs single-episode). Clients that can accept this configuration **at add time** (QBittorrent ≥ API 2.8.1, Transmission, Deluge, uTorrent) set it immediately via a native "seed criteria" API call. Clients whose add-response can't set it, or that need to re-derive it later purely from a hash (rTorrent, RQBit, Flood), instead go through `DownloadSeedConfigProvider.GetSeedConfiguration(infoHash)`, which:
- 1-hour rolling cache keyed by uppercased info hash.
- Falls back to reconstructing the indexer id + "was this a full season" flag from the **latest download-history grab** for that hash, re-parsing the stored release title to recover `FullSeason`, then asks the same `ISeedConfigProvider` for the actual ratio/time.
- Returns `null` (no seed enforcement) if there's no download history for the hash at all.

### Per-client "has this torrent satisfied its seeding requirement" logic (used to gate `CanBeRemoved`/`CanMoveFiles`)
- **qBittorrent**: `HasReachedSeedLimit` checks, in order: per-torrent `RatioLimit` (if ≥0, direct compare within 0.001 tolerance); else if per-torrent limit is the "-2" sentinel (use global) and the global `MaxRatioEnabled`, compares against the global `MaxRatio`; then separately checks a **seeding-time limit** (per-torrent or global, in minutes, converted to seconds) via `HasReachedSeedingTimeLimit` — since the qBittorrent API doesn't always expose live `SeedingTime`, this maintains a **client-side extrapolation cache** (`SeedingTimeCacheEntry { LastFetched, SeedingTime }`, 5-minute TTL) that estimates elapsed seeding time between actual API refetches rather than hitting `GetTorrentProperties` every single poll; and an **inactive-seeding-time limit** (`HasReachedInactiveSeedingTimeLimit`, based on Unix `LastActivity` timestamp vs now). Additionally requires the underlying torrent to actually be in a `pausedUP`/`stoppedUP` state — even if ratio/time is satisfied, a torrent qBittorrent hasn't itself paused isn't considered removable (protects against a higher torrent-specific ratio set directly in qBittorrent's own UI that Sonarr doesn't know about).
- **Transmission/Vuze** (shared `TransmissionBase.HasReachedSeedLimit`): per-torrent `SeedRatioMode == 1` (torrent-specific override) checks stopped+ratio≥limit; `SeedRatioMode == 0` (use global) checks stopped+global `SeedRatioLimited`+ratio≥global limit. Separately, Transmission doesn't support a true seed **time** limit natively, so Sonarr "abuses" the seed **idle** limit as a proxy: per-torrent `SeedIdleMode == 1` checks (stopped or seeding) + `SecondsSeeding > SeedIdleLimit*60`; `SeedIdleMode == 0` (global) simply treats "stopped + global idle-limit enabled" as sufficient (documented in-code as an intentional approximation, not a true seed-time enforcement).
- **Deluge**: requires the torrent to be `IsAutoManaged`, have `StopAtRatio` enabled, `Ratio >= StopRatio`, and be in `DelugeTorrentStatus.Paused` — i.e., entirely delegates ratio arithmetic to Deluge's own auto-management rather than recomputing it, and only trusts the result once Deluge has actually paused the torrent itself.
- **rTorrent / RQBit** (nearly identical logic, both share the "fetch cached seed config by hash" pattern): only evaluated when the torrent `IsFinished`; checks `torrent.Ratio / 1000.0 >= seedConfig.Ratio` (ratio stored as a scaled integer, hence the /1000 conversion) **or** `(Now - FinishedTime) >= seedConfig.SeedTime`; if no seed config resolves for that hash at all, seeding-enforcement is simply skipped (torrent stays non-removable indefinitely until config resolves).
- **Flood**: only evaluated when `RemoveCompletedDownloads` is enabled and status is `Completed`; same ratio-or-time-since-`DateFinished` pattern via the cached seed-config provider.
- **uTorrent**: no ratio/time arithmetic at all — instead infers removability purely from client-reported status flags: removable once the torrent is *not* `Queued` and *not* `Started` (i.e., uTorrent's own queue/seeding-limit engine has already stopped it) — same delegation-to-native-engine pattern as Deluge.
- **Hadouken**: removable once `IsFinished` and native `State == Paused` — again delegating entirely to the client's own seed-limit engine having already paused the torrent.
- **NZBGet/SABnzbd/NzbVortex/Pneumatic/Blackhole/DownloadStation-Usenet** (Usenet clients): no seeding concept at all — Usenet has no seed ratio; `CanBeRemoved`/`CanMoveFiles` for these is generally just `true` once the item is out of the active queue (into history), since there's nothing further to protect.
- **Tribler**: `HasReachedSeedLimit` reads the **client's global** seeding-mode config (`DownloadDefaultsSeedingMode`: `Ratio | Time | Never | Forever`) rather than a per-release Sonarr-computed config — `Ratio` mode compares `AllTimeRatio` against the configured global ratio; `Time` mode compares `TimeAdded + SeedingTime` against now; `Never` always considered reached; `Forever` never considered reached. Also always requires `Status == Stopped` first.
- **Download Station (torrent)**: removable once `IsFinished` (native `Status == Finished`) — no separate ratio/time check layered on top; entirely trusts Download Station's own scheduler having finished the task.
- **Aria2**: removable once native `status == "complete"` — same trust-the-daemon pattern; Aria2 has no Sonarr-side ratio/time recomputation at all.
- **FreeboxDownload**: removable once native `Status == Done` — same pattern.

### "Remove Completed" / "Remove Failed" semantics recap
These are **not** the same as seeding satisfaction — they're the separate, per-`DownloadClientDefinition` boolean toggles (`RemoveCompletedDownloads`, `RemoveFailedDownloads`, default `true` for both) that gate whether `DownloadEventHub` is even allowed to call `RemoveItem()` at all once an item's `CanBeRemoved` becomes true through the above per-protocol logic. Turning `RemoveCompletedDownloads` off means Sonarr will happily import from and track a completed download forever without ever telling the client to delete it — useful for users who want to manage seeding/cleanup entirely inside their torrent client's own UI. qBittorrent additionally proactively **warns during Test()** (`RemovesCompletedDownloads` check against qBittorrent's *own* auto-removal-at-ratio-limit config) if qBittorrent itself is configured to delete torrents at the ratio limit — because that would race/conflict with Sonarr's own removal timing and potentially delete files Sonarr hasn't imported yet. SABnzbd has an equivalent self-check on its history-retention settings (`RemovesCompletedDownloads`, checked against `history_retention_option`/`number`/`days` combinations, warning if retention is under 14 days) for the same underlying reason: Sonarr needs the completed item to still be present in SABnzbd's history long enough for CDH to see and import it.

---

## 12. Configuration surface — every client's settings fields

All settings classes extend `DownloadClientSettingsBase<TSettings>` (adds `IProviderConfig` + memberwise-equality) except `TriblerDownloadSettings`, which implements `IProviderConfig` directly. `[FieldDefinition(order, ...)]` attributes drive auto-generated UI form order; `Advanced = true` fields are hidden behind an "Advanced" toggle; `Privacy` marks masked fields (ApiKey/UserName/Password).

### 12.1 QBittorrent (`QBittorrentSettings`)
`Host` (default `localhost`), `Port` (default `8080`), `UseSsl`, `UrlBase` (advanced), `ApiKey` (mutually exclusive with Username/Password), `Username`, `Password`, `TvCategory` (default `tv-sonarr`; regex-restricted, no `\`/`//`/leading-trailing `/`), `TvImportedCategory` (advanced, post-import category, same regex), `RecentTvPriority` / `OlderTvPriority` (select: `QBittorrentPriority {Last=0, First=1}`), `InitialState` (select: `QBittorrentState {Start=0, ForceStart=1, Stop=2}`), `SequentialOrder` (checkbox), `FirstAndLast` (checkbox, "first and last pieces first"), `ContentLayout` (select: `QBittorrentContentLayout {Default=0, Original=1, Subfolder=2}`), `AddSeriesTags` (checkbox — adds the series' Sonarr tags as qBittorrent torrent tags on add).

### 12.2 Transmission (`TransmissionSettings`)
`Host` (default `localhost`), `Port` (default `9091`), `UseSsl`, `UrlBase` (advanced, default `/transmission/`), `Username`, `Password`, `TvCategory` (default `tv-sonarr`, regex `a-z` and `-` only, mutually exclusive with `TvDirectory`), `TvImportedCategory` (advanced), `TvDirectory` (advanced — absolute output dir override, mutually exclusive with category), `RecentTvPriority` / `OlderTvPriority` (select: `TransmissionPriority {Last, First}`), `AddPaused` (checkbox). Note: has a legacy `[JsonConstructor]` overload solely to preserve backward-compat deserialization of the `tvCategory` field shape from pre-refactor configs.

### 12.3 Vuze
No dedicated settings class — reuses `TransmissionSettings` in full (Vuze speaks the Transmission RPC protocol; `Vuze : TransmissionBase` overrides only `Name`, `SupportsLabels => false`, output-path derivation quirk, protocol-version validation via `GetProtocolVersion` with `MINIMUM_SUPPORTED_PROTOCOL_VERSION = 14`).

### 12.4 Deluge (`DelugeSettings`)
`Host` (default `localhost`), `Port` (default `8112`), `UseSsl`, `UrlBase` (advanced), `Password` (default `deluge`), `TvCategory` (default `tv-sonarr`, regex `a-z0-9-`), `TvImportedCategory` (advanced, same regex), `RecentTvPriority` / `OlderTvPriority` (select: `DelugePriority {Last, First}`), `AddPaused` (checkbox), `DownloadDirectory` (advanced), `CompletedDirectory` (advanced — takes precedence over label-based completed path in `GetStatus`).

### 12.5 rTorrent (`RTorrentSettings`)
`Host` (default `localhost`), `Port` (default `8080`), `UseSsl`, `UrlBase` (default `RPC2`), `Username`, `Password`, `TvCategory` (default `tv-sonarr`; warns-as-recommendation if empty, not hard-required), `TvImportedCategory` (advanced), `TvDirectory` (advanced), `RecentTvPriority` / `OlderTvPriority` (select: `RTorrentPriority {VeryLow=0, Low=1, Normal=2, High=3}` — the only client with a 4-tier priority scale instead of binary Last/First), `AddStopped` (checkbox).

### 12.6 uTorrent (`UTorrentSettings`)
`Host` (default `localhost`), `Port` (default `8080`), `UseSsl`, `UrlBase` (advanced), `Username`, `Password`, `TvCategory` (default `tv-sonarr`, **required**, not optional), `TvImportedCategory` (advanced), `RecentTvPriority` / `OlderTvPriority` (select: `UTorrentPriority {Last, First}`), `IntialState` (select: `UTorrentState {Start=0, ForceStart=1, Pause=2, Stop=3}` — note 4 states vs qBittorrent's 3).

### 12.7 SABnzbd (`SabnzbdSettings`)
`Host` (default `localhost`), `Port` (default `8080`), `UseSsl`, `UrlBase` (advanced), `ApiKey` (required unless Username/Password set), `Username` (required unless ApiKey set), `Password` (required unless ApiKey set), `TvCategory` (default `tv`; warns-as-recommendation if empty), `RecentTvPriority` / `OlderTvPriority` (select: `SabnzbdPriority {Default=-100, Paused=-2, Low=-1, Normal=0, High=1, Force=2}`, default `Default`).

### 12.8 NZBGet (`NzbgetSettings`)
`Host` (default `localhost`), `Port` (default `6789`), `UseSsl`, `UrlBase` (advanced), `Username`/`Password` (co-required — if one is set, the other must be too), `TvCategory` (default `tv`; warns if empty), `RecentTvPriority` / `OlderTvPriority` (select: `NzbgetPriority {VeryLow=-100, Low=-50, Normal=0, High=50, VeryHigh=100, Force=900}`, default `Normal`), `AddPaused` (checkbox).

### 12.9 NZBVortex (`NzbVortexSettings`)
`Host` (default `localhost`), `Port` (default `4321`), `UrlBase` (advanced), `ApiKey` (required), `TvCategory` (labeled "Group" in UI, default `TV Shows`; warns if empty), `RecentTvPriority` / `OlderTvPriority` (select: `NzbVortexPriority {Low=-1, Normal=0, High=1}`, default `Normal`). No `UseSsl` field (unlike other Usenet clients) and no Username/Password (API-key only).

### 12.10 Pneumatic (`PneumaticSettings`)
`NzbFolder` (path, required/validated to exist), `StrmFolder` (path, required/validated to exist). No host/port/category — purely a filesystem drop mechanism producing `.strm` files for XBMC/Kodi's Pneumatic add-on to pick up.

### 12.11 Torrent Blackhole (`TorrentBlackholeSettings`)
`TorrentFolder` (path — where `.torrent`/`.magnet` files are written), `WatchFolder` (path — where completed downloads appear), `SaveMagnetFiles` (checkbox, default `false` — without it, magnet-only releases throw `NotSupportedException`), `MagnetFileExtension` (default `.magnet`), `ReadOnly` (checkbox, default `true` — controls whether Sonarr is allowed to move files out of the watch folder or must treat it as read-only/hardlink-only).

### 12.12 Usenet Blackhole (`UsenetBlackholeSettings`)
`NzbFolder` (path — where `.nzb` files are written), `WatchFolder` (path — where completed downloads appear). No read-only toggle (usenet blackhole always sets `CanMoveFiles = true`).

### 12.13 Download Station — both Torrent and Usenet variants share (`DownloadStationSettings`)
`Host` (default `127.0.0.1`), `Port` (default `5000`), `UseSsl`, `Username`, `Password`, `TvCategory` (regex `a-z-`, mutually exclusive with `TvDirectory`), `TvDirectory` (must NOT start with `/`, mutually exclusive with category). Notably has **no Priority field at all** — Download Station's task queue has no per-task priority concept exposed here.

### 12.14 Flood (`FloodSettings`)
`Host` (default `localhost`), `Port` (default `3000`), `UseSsl` (default `false`), `UrlBase`, `Username`, `Password`, `Destination` (output dir override), `Tags` (`IEnumerable<string>`, default `["sonarr"]` — a **set**, not a single category string, unique among torrent clients here), `PostImportTags` (advanced, `IEnumerable<string>` — validated to have **zero overlap** with `Tags`), `AdditionalTags` (advanced, select-multi over `AdditionalTags` enum: `TitleSlug, Quality, Languages, ReleaseGroup, Year, Indexer, Network` — dynamically computed per-release tag values appended at add time via `HandleTags()`), `StartOnAdd` (default `true`).

### 12.15 Aria2 (`Aria2Settings`)
`Host` (default `localhost`), `Port` (default `6800`), `RpcPath` (default `/rpc`), `UseSsl` (default `false`), `SecretToken` (password field, default placeholder `MySecretToken`), `Directory` (output override). No category/label concept at all — Aria2 has none; and no priority selects (Aria2 has no queue-priority feature Sonarr models).

### 12.16 Hadouken (`HadoukenSettings`)
`Host` (default `localhost`), `Port` (default `7070`), `UseSsl`, `UrlBase` (advanced), `Username` (required), `Password` (required), `Category` (default `sonarr-tv`). No priority selects.

### 12.17 Freebox Download (`FreeboxDownloadSettings`)
`Host` (default `mafreebox.freebox.fr`), `Port` (default `443`), `UseSsl` (default `true`), `ApiUrl` (advanced, default `/api/v1/`), `AppId` (required — Freebox app-auth model), `AppToken` (required, password field — Freebox app-auth model), `DestinationDirectory` (advanced, mutually exclusive with Category), `Category` (mutually exclusive with DestinationDirectory), `RecentPriority` / `OlderPriority` (select: `FreeboxDownloadPriority {Last, First}`), `AddPaused` (checkbox). Unique among all clients in using a two-legged app-id/app-token auth flow instead of username/password or a single API key.

### 12.18 RQBit (`RQbitSettings`)
`Host` (default `localhost`), `Port` (default `3030`), `UseSsl`, `UrlBase` (advanced, default `/`). The most minimal settings surface of any client — no category, no priority, no credentials (RQBit's HTTP API has none of these concepts).

### 12.19 Tribler (`TriblerDownloadSettings`)
`Host` (default `localhost`), `Port` (default `20100`), `UseSsl`, `UrlBase` (advanced), `ApiKey` (required), `TvCategory` (regex `a-z-`, mutually exclusive with `TvDirectory`), `TvDirectory` (advanced, mutually exclusive with category), `AnonymityLevel` (number, default `1`, must be ≥0 — Tor-like onion-routing hop count Tribler is famous for), `SafeSeeding` (checkbox, default `true`). Unique fields (`AnonymityLevel`, `SafeSeeding`) reflect Tribler's anonymity-network design; also unique in modeling **client-global** seeding-mode config (`Ratio | Time | Never | Forever`) rather than a per-release Sonarr-side seed config (§11).

---

## 13. Client-specific quirks worth explicitly replicating

- **qBittorrent**: `WaitForTorrent` polls up to 10× at 100ms after add before applying seed-limits/priority/force-start/tags, because the API doesn't guarantee the torrent is queryable immediately after the add call returns; if it never appears, those secondary settings are silently skipped (the torrent itself was still added). Distinguishes API-version behavior at three separate boundaries (`1.5`→ minimum supported at all, `1.6`→ label/category support, `2.0`→ labels endpoint, `2.6.1`→ reliable `ContentPath` in the list response meaning `GetImportItem`'s extra `GetTorrentFiles` round-trip can be skipped for completed torrents, `2.8.1`→ can set share-limits atomically at add time instead of a second call). Path with double leading `//` is auto-corrected to backslashes with a log warning (common Windows/category-path misconfiguration). `eta=8640000` is qBittorrent's sentinel for "unknown/queued" and is explicitly translated to `null`, not a 100-day ETA.
- **Transmission/Vuze**: category filtering has **three different strategies** depending on settings and label support — filter by native label (v4+ only, when a category is configured and the client supports labels), else filter by exact `TvDirectory` path containment, else (weakest) filter by literal category-name-as-path-segment string matching. `SupportsLabels` is a runtime version probe (`HasClientVersion(4, 0)`), not a config toggle. Vuze reuses the identical RPC proxy but reports `SupportsLabels => false` unconditionally (Vuze's Transmission-RPC implementation never added label support) and has its own output-path derivation quirk mirroring uTorrent's single-file-vs-multi-file root ambiguity.
- **Deluge**: on seeing torrents with an empty hash/name in `GetItems()`, tracks an `ignoredCount` and — the **first** time this happens — proactively calls `ReconnectToDaemon()` rather than just logging, because Deluge's web UI process can silently lose its connection to the actual daemon and start returning garbage/partial torrent entries; only warns (doesn't reconnect again) on subsequent occurrences within the same session (`_hasAttemptedReconnecting` latch).
- **rTorrent**: on `MarkItemAsImported`, in addition to the generic post-import label pattern, pushes the torrent into a dedicated computed view named `"{appname}_imported"` (e.g. `sonarr_imported`) via `PushTorrentUniqueView` — this is rTorrent-specific because rTorrent's UI/queries are organized around named views rather than labels, and the client surfaces a `ProviderMessage` at the settings-page level telling the user exactly what view name to expect in their rTorrent client. Torrents with an empty path or a path starting with `.` are explicitly skipped with a warning (guards against relative-path rTorrent configs that would otherwise resolve to nonsense inside Sonarr).
- **uTorrent**: implements a **delta/differential fetch cache** (`UTorrentTorrentCache`, 15-minute TTL, keyed by `host:port:category`) — uTorrent's list API supports a `cacheID` parameter that returns only changed/removed torrents since the last cache id, and this client merges that diff into the previous full snapshot rather than re-fetching everything every poll (the only client implementing this optimization). "Started without Queued" is explicitly called out in a comment as uTorrent's representation of "force seeding."
- **SABnzbd**: history items titled with an `ENCRYPTED /` prefix are the sole source of `IsEncrypted = true` across the entire codebase (drives automatic-failure handling, §8.1). A specific failed-history message string ("Unpacking failed, write error or disk is full?") is downgraded from `Failed` to `Warning` rather than triggering FDH — treated as a possibly-transient/disk-space issue rather than a release-quality problem. `GetCategories` has version-gated completion-dir resolution logic (pre-2.0 vs post-2.0 API) and strips trailing `*` from category directory patterns. Extensive `Test()` validation surface: warns if `enable_tv_sorting`/`enable_movie_sorting`/`enable_date_sorting` or a matching v4.1+ "sorter" is active for the configured category (SABnzbd's built-in sorting conflicts with Sonarr's own file organization), warns if job folders aren't enabled for the category (dir pattern ends in `*`), and warns if `pre_check` (check-before-download) is enabled pre-1.1 (a known-buggy combination).
- **NZBGet**: 64-bit sizes are split across two 32-bit fields (`FileSizeHi`/`FileSizeLo` etc.) because the underlying JSON-RPC API predates reliable 64-bit JSON number support; `MakeInt64` manually recombines them via bit-shift. History item status is derived from **five separate status fields** (`ParStatus`, `UnpackStatus`, `MoveStatus`, `ScriptStatus`, `DeleteStatus`) each checked against a `_successStatus` allowlist (`SUCCESS`/`NONE`) — any single field failing degrades the result to `Warning` or `Failed` depending on which field and value (`UnpackStatus == "SPACE"` specifically → `Warning`, not `Failed` — disk-space-during-unpack is treated as recoverable/informational). Manual deletions (`DeleteStatus == "MANUAL"`) are only surfaced back to Sonarr if additionally `MarkStatus == "BAD"` (an explicit user "mark bad" in NZBGet's own UI) — otherwise silently excluded from Sonarr's history view entirely (assumes the user meant to remove it without any Sonarr-side consequence).
- **NZBVortex**: `DownloadId` prefers a client-issued `AddUUID` over the numeric queue id when available, and `RemoveItem` has to fall back to a **second full queue fetch** to resolve numeric id from `AddUUID` when the stored id isn't directly numeric-parseable. `GetOutputPath` explicitly warns (sets item `Status = Warning`) when a completed download resolved to more than one file, since NZBVortex's completion path resolution assumes single-file releases by default.
- **Freebox Download**: `ReceivedPrct` is scaled by 10000 (not 100) requiring `(1 - ReceivedPrct/10000.0)` for remaining-fraction math; `StopRatio` is similarly scaled by 100. Directory/category paths must be base64-encoded before being sent to the API (`EncodeBase64()`). Error descriptions come from a dedicated `GetErrorDescription()` mapping on the task DTO rather than a raw client message field.
- **Aria2**: cannot delete downloaded files as a client feature at all (linked to an upstream Aria2 GitHub issue in a code comment) — `RemoveItem`'s `deleteData` flag is honored by Sonarr calling its own `DeleteItemData()` disk-provider deletion *after* telling Aria2 to forget the download, rather than Aria2 doing the deletion itself. Distinguishes "remove a still-active/queued download" (`RemoveTorrent`) from "remove an already complete/errored/removed download" (`RemoveCompletedTorrent`) as two different RPC calls. Skips list entries that are still resolving magnet metadata (`Files[0].Path` containing the literal string `[METADATA]`) or already in `removed` status.
- **Hadouken**: `IsFinished && State != CheckingFiles` is the completion signal (explicitly excludes the post-download integrity-check phase from counting as "done" even though byte-count-wise it may already look complete).
- **RQBit / rTorrent**: near-identical implementations (RQBit's proxy/model shapes mirror rTorrent's) — both are the two clients that lean entirely on the shared `IDownloadSeedConfigProvider` hash-based cache rather than a native per-torrent seed-limit API, and both explicitly reject torrents with empty or `.`-relative paths rather than attempting to guess a root.
- **Tribler**: the only client using a purely client-global seeding policy enum (`Ratio | Time | Never | Forever`) instead of a per-release Sonarr-computed config; explicitly documents that `AnonymityLevel`/`SafeSeeding` are passed at add-time per download (`AddDownloadRequest`), meaning anonymity is a Sonarr-configured, not Tribler-UI-configured, per-release setting. Cannot add from `.torrent` file content at all — `AddFromTorrentFile` unconditionally throws `NotSupportedException("Tribler does not support torrent files, only magnet links")`, a hard protocol-level limitation, not a missing feature. Treats `Stopped` status with `Progress < 1` as `Paused` (an override applied *after* the main switch statement) to distinguish "stopped-because-still-incomplete" from "stopped-because-finished-and-not-seeding."
- **Download Station**: constructs a composite `DownloadId` of `"{hashedSerialNumber}:{taskId}"` (`CreateDownloadId`/`ParseDownloadId`) because Download Station's own task ids are not globally unique across different NAS units/reinstalls — the serial number namespace-qualifies them so Sonarr doesn't confuse tasks from two different Download Station instances (or the same instance after a reset) that happen to reuse task id `1`. Both torrent and usenet variants share the exact same proxy/settings/validation code and only differ in the task-type filter (`BT` vs `NZB`) and per-item transfer-rate math (Usenet variant additionally computes a **global aggregate download speed** across all active NZB tasks to estimate per-item ETA proportionally, since Download Station's usenet backend doesn't report per-task speed the way its torrent backend does).
- **Blackhole (both variants)**: shares a single `ScanWatchFolder` engine that hashes folder/file mtimes+sizes to detect "still changing" vs "stable," and only reports an item as `Completed` once it has been stable for a configurable **grace period** (default 30s, `ScanGracePeriod`) — this is Sonarr's workaround for not having any real completion signal from a plain filesystem drop, and also skips OS "special folders" (e.g. `.DS_Store`/thumbs-db equivalents via `SpecialFolders.IsSpecialFolder`). Watch-folder scan results are cached 5 minutes and diffed against the previous scan by a content hash so repeat polls don't endlessly reset the grace-period timer for genuinely unchanged folders. Torrent Blackhole additionally supports writing bare `.magnet` files (text files containing just the magnet URI) when `SaveMagnetFiles` is enabled — without it, magnet-only releases are rejected outright (`NotSupportedException`) since there's no `.torrent` file to place in the watch folder. `RemoveItem` for both blackhole variants **requires** `deleteData: true` and throws otherwise — there is no concept of "remove from queue but keep the file" for a filesystem-drop client.
- **Pneumatic**: the only client whose `Download()` override completely bypasses the shared `UsenetClientBase` NZB-fetch/validate pipeline (it's a `DownloadClientBase<TSettings>` direct subclass, not `UsenetClientBase`) — it downloads the NZB itself via a raw `IHttpClient.DownloadFileAsync` straight to `NzbFolder`, then writes an XBMC/Kodi-specific `.strm` playlist-stub file (`plugin://plugin.program.pneumatic/?mode=strm&type=add_file&nzb={path}&nzbname={title}`) into `StrmFolder`, and derives its `DownloadId` from that `.strm` file's name + last-write ticks. Explicitly throws `NotSupportedException` for full-season releases ("Full season releases are not supported with Pneumatic") and for `RemoveItem` entirely (no removal capability modeled at all — Pneumatic is effectively fire-and-forget/legacy).

---

## 14. Cross-cutting settings not on a specific client

- `IConfigService.EnableCompletedDownloadHandling` (default `true`) — global CDH on/off; when off, the queue only ever shows in-progress items (§6 `DownloadIsTrackable`) and `DownloadProcessingService` never calls `Import()`.
- `IConfigService.AutoRedownloadFailed` (default `true`) / `AutoRedownloadFailedFromInteractiveSearch` (default `true`) — the two auto-redownload off-switches consumed by `RedownloadFailedDownloadService` (§8.4).
- `IConfigService.DownloadClientHistoryLimit` (default `60`) — caps how many history items are pulled per poll from clients whose history API requires an explicit page size (SABnzbd's `GetHistory`, NZBGet's `GetHistory().Take(...)`); irrelevant to clients with no separate history concept (blackhole, pure-queue clients).
- Download-client health/backoff (`DownloadClientStatusService`): 5-minute minimum suppression window before a client is first considered "blocked," escalating up to 5 levels on repeated failure — shared infrastructure with indexer status tracking, consumed by both `DownloadClientFactory.DownloadHandlingEnabled()` and `DownloadClientProvider`'s blocked-filtering (§9.2).
