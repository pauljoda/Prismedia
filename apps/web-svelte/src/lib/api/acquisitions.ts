import {
  cancelAcquisition as cancelAcquisitionRequest,
  createAcquisition as createAcquisitionRequest,
  deleteAcquisitionProfile as deleteAcquisitionProfileRequest,
  deleteDownloadClient,
  deleteIndexer,
  getAcquisition as getAcquisitionRequest,
  listAcquisitionProfiles,
  listAcquisitions,
  listDownloadClients,
  listIndexers,
  queueAcquisition as queueAcquisitionRequest,
  saveAcquisitionProfile as saveAcquisitionProfileRequest,
  saveDownloadClient,
  saveIndexer,
  testDownloadClient,
  testIndexer,
  updateAcquisitionProfile,
  updateDownloadClient,
  updateIndexer,
} from "$lib/api/generated/prismedia";
import type {
  AcquisitionCreateRequest,
  AcquisitionDetail,
  AcquisitionSummary,
  BookAcquisitionProfileSaveRequest,
  BookAcquisitionProfileView,
  DownloadClientSaveRequest,
  DownloadClientSummary,
  DownloadClientTestRequest,
  DownloadClientTestResponse,
  IndexerConfigSaveRequest,
  IndexerConfigSummary,
  IndexerTestRequest,
  IndexerTestResponse,
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

export async function fetchAcquisition(id: string): Promise<AcquisitionDetail> {
  return unwrapGenerated(await getAcquisitionRequest(id), "Failed to load acquisition");
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
