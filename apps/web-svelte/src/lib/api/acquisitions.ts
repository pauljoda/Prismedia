import {
  blocklistAcquisitionCandidate as blocklistAcquisitionCandidateRequest,
  cancelAcquisition as cancelAcquisitionRequest,
  createAcquisition as createAcquisitionRequest,
  deleteAcquisition as deleteAcquisitionRequest,
  deleteAcquisitionBlocklistEntry,
  deleteAcquisitionProfile as deleteAcquisitionProfileRequest,
  deleteCustomFormat as deleteCustomFormatRequest,
  deleteDownloadClient,
  deleteIndexer,
  listAcquisitionBlocklist,
  listCustomFormats,
  saveCustomFormat as saveCustomFormatRequest,
  listRemotePathMappings,
  saveRemotePathMapping as saveRemotePathMappingRequest,
  deleteRemotePathMapping as deleteRemotePathMappingRequest,
  getAcquisition as getAcquisitionRequest,
  getAcquisitionFiles,
  getAcquisitionForEntity,
  getAcquisitionTransfer,
  listAcquisitionHistory,
  listAcquisitionProfiles,
  listAcquisitions,
  listDownloadQueue,
  listDownloadClients,
  listIndexers,
  queueAcquisition as queueAcquisitionRequest,
  reSearchAcquisition as reSearchAcquisitionRequest,
  saveAcquisitionProfile as saveAcquisitionProfileRequest,
  saveDownloadClient,
  saveIndexer,
  testDownloadClient,
  testIndexer,
  updateAcquisitionProfile,
  updateDownloadClient,
  updateIndexer,
  uploadAcquisitionTorrent,
} from "$lib/api/generated/prismedia";
import type {
  AcquisitionBlocklistEntry,
  CustomFormatSaveRequest,
  CustomFormatView,
  RemotePathMappingSaveRequest,
  RemotePathMappingView,
  AcquisitionCreateRequest,
  AcquisitionDetail,
  AcquisitionFilesView,
  AcquisitionHistoryView,
  AcquisitionSummary,
  AcquisitionTransferView,
  BookAcquisitionProfileSaveRequest,
  BookAcquisitionProfileView,
  DownloadClientSaveRequest,
  DownloadClientSummary,
  DownloadQueueItemView,
  DownloadClientTestRequest,
  DownloadClientTestResponse,
  IndexerConfigSaveRequest,
  IndexerConfigSummary,
  IndexerTestRequest,
  IndexerTestResponse,
  UploadAcquisitionTorrentBody,
} from "$lib/api/generated/model";
import { unwrapGenerated } from "$lib/api/generated-response";

// ── Indexers ──────────────────────────────────────────────────
export async function fetchIndexers(): Promise<IndexerConfigSummary[]> {
  return unwrapGenerated(await listIndexers(), "Failed to load indexers");
}

export async function saveIndexerConfig(payload: IndexerConfigSaveRequest): Promise<IndexerConfigSummary> {
  const request = payload.id ? updateIndexer(payload.id, payload) : saveIndexer(payload);
  return unwrapGenerated(await request, "Failed to save indexer");
}

export async function deleteIndexerConfig(id: string): Promise<void> {
  unwrapGenerated(await deleteIndexer(id), "Failed to delete indexer", [204]);
}

export async function testIndexerConnection(payload: IndexerTestRequest): Promise<IndexerTestResponse> {
  return unwrapGenerated(await testIndexer(payload), "Failed to test indexer");
}

// ── Download clients ──────────────────────────────────────────
export async function fetchDownloadClients(): Promise<DownloadClientSummary[]> {
  return unwrapGenerated(await listDownloadClients(), "Failed to load download clients");
}

export async function saveDownloadClientConfig(payload: DownloadClientSaveRequest): Promise<DownloadClientSummary> {
  const request = payload.id ? updateDownloadClient(payload.id, payload) : saveDownloadClient(payload);
  return unwrapGenerated(await request, "Failed to save download client");
}

export async function deleteDownloadClientConfig(id: string): Promise<void> {
  unwrapGenerated(await deleteDownloadClient(id), "Failed to delete download client", [204]);
}

export async function testDownloadClientConnection(payload: DownloadClientTestRequest): Promise<DownloadClientTestResponse> {
  return unwrapGenerated(await testDownloadClient(payload), "Failed to test download client");
}

// ── Profiles ──────────────────────────────────────────────────
export async function fetchAcquisitionProfiles(): Promise<BookAcquisitionProfileView[]> {
  return unwrapGenerated(await listAcquisitionProfiles(), "Failed to load acquisition profiles");
}

export async function saveAcquisitionProfile(payload: BookAcquisitionProfileSaveRequest): Promise<BookAcquisitionProfileView> {
  const request = payload.id ? updateAcquisitionProfile(payload.id, payload) : saveAcquisitionProfileRequest(payload);
  return unwrapGenerated(await request, "Failed to save acquisition profile");
}

export async function deleteAcquisitionProfileConfig(id: string): Promise<void> {
  unwrapGenerated(await deleteAcquisitionProfileRequest(id), "Failed to delete acquisition profile", [204]);
}

// ── Acquisitions ──────────────────────────────────────────────
export async function fetchAcquisitions(): Promise<AcquisitionSummary[]> {
  return unwrapGenerated(await listAcquisitions(), "Failed to load acquisitions");
}

/**
 * The global Downloads view: every active acquisition across all kinds, with live download-client
 * telemetry (progress, speed, ETA, peers, client) where a transfer is in flight.
 */
export async function fetchDownloadQueue(): Promise<DownloadQueueItemView[]> {
  return unwrapGenerated(await listDownloadQueue(), "Failed to load downloads");
}

export async function fetchAcquisition(id: string): Promise<AcquisitionDetail> {
  return unwrapGenerated(await getAcquisitionRequest(id), "Failed to load acquisition");
}

/** The latest acquisition backing a library entity, or null when it has none (the common case for scanned-in items). */
export async function fetchAcquisitionForEntity(entityId: string): Promise<AcquisitionDetail | null> {
  const response = await getAcquisitionForEntity(entityId);
  if (response.status === 404) return null;
  return unwrapGenerated(response, "Failed to load the entity's acquisition");
}

export async function createAcquisition(payload: AcquisitionCreateRequest): Promise<AcquisitionSummary> {
  return unwrapGenerated(await createAcquisitionRequest(payload), "Failed to start acquisition");
}

export async function queueAcquisitionCandidate(id: string, candidateId: string): Promise<AcquisitionDetail> {
  return unwrapGenerated(await queueAcquisitionRequest(id, { candidateId }), "Failed to queue release");
}

export async function cancelAcquisition(id: string): Promise<AcquisitionDetail> {
  return unwrapGenerated(await cancelAcquisitionRequest(id), "Failed to cancel acquisition");
}

/** Re-runs the release search for an existing acquisition on demand. */
export async function reSearchAcquisition(id: string): Promise<AcquisitionDetail> {
  return unwrapGenerated(await reSearchAcquisitionRequest(id), "Failed to re-search");
}

export async function blocklistAcquisitionCandidate(id: string, candidateId: string): Promise<AcquisitionDetail> {
  return unwrapGenerated(await blocklistAcquisitionCandidateRequest(id, candidateId), "Failed to blocklist release");
}

export async function deleteAcquisition(id: string): Promise<void> {
  unwrapGenerated(await deleteAcquisitionRequest(id), "Failed to remove acquisition", [204]);
}

export async function fetchAcquisitionTransfer(id: string): Promise<AcquisitionTransferView | null> {
  // 204 means there is no live transfer (not started, or already imported/removed).
  return (unwrapGenerated(await getAcquisitionTransfer(id), "Failed to load transfer", [200, 204]) ?? null) as AcquisitionTransferView | null;
}

export async function fetchAcquisitionFiles(id: string): Promise<AcquisitionFilesView> {
  return unwrapGenerated(await getAcquisitionFiles(id), "Failed to load files");
}

export async function uploadManualTorrent(id: string, file: File): Promise<AcquisitionDetail> {
  const body = { file } as unknown as UploadAcquisitionTorrentBody;
  return unwrapGenerated(await uploadAcquisitionTorrent(id, body), "Failed to upload torrent");
}

// ── History (durable activity log) ────────────────────────────
/**
 * The durable acquisition activity log (grabbed/imported/failed/removed), newest-first. Survives the
 * acquisitions it describes. Pass `entityId` to scope it to one library entity's events.
 */
export async function fetchAcquisitionHistory(
  options: { limit?: number; entityId?: string } = {},
): Promise<AcquisitionHistoryView[]> {
  return unwrapGenerated(
    await listAcquisitionHistory({ limit: options.limit, entityId: options.entityId }),
    "Failed to load acquisition history",
  );
}

// ── Blocklist ─────────────────────────────────────────────────
export async function fetchBlocklist(): Promise<AcquisitionBlocklistEntry[]> {
  return unwrapGenerated(await listAcquisitionBlocklist(), "Failed to load blocklist");
}

export async function deleteBlocklistEntry(id: string): Promise<void> {
  unwrapGenerated(await deleteAcquisitionBlocklistEntry(id), "Failed to remove blocklist entry", [204]);
}

export async function fetchRemotePathMappings(): Promise<RemotePathMappingView[]> {
  return unwrapGenerated(await listRemotePathMappings(), "Failed to load remote path mappings");
}

export async function saveRemotePathMapping(request: RemotePathMappingSaveRequest): Promise<RemotePathMappingView> {
  return unwrapGenerated(await saveRemotePathMappingRequest(request), "Failed to save remote path mapping");
}

export async function deleteRemotePathMapping(id: string): Promise<void> {
  unwrapGenerated(await deleteRemotePathMappingRequest(id), "Failed to remove remote path mapping", [204]);
}

// ── Custom formats ────────────────────────────────────────────
export async function fetchCustomFormats(): Promise<CustomFormatView[]> {
  return unwrapGenerated(await listCustomFormats(), "Failed to load custom formats");
}

export async function saveCustomFormat(request: CustomFormatSaveRequest): Promise<CustomFormatView> {
  return unwrapGenerated(await saveCustomFormatRequest(request), "Failed to save custom format");
}

export async function deleteCustomFormat(id: string): Promise<void> {
  unwrapGenerated(await deleteCustomFormatRequest(id), "Failed to remove custom format", [204]);
}
