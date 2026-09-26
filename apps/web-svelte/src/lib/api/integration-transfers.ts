import { acquireExecutorItem, inspectExecutorUrl, acquireCatalogOffer, requestCatalogSource, cancelIntegrationTransfer, listIntegrationTransfers, retryIntegrationTransfer } from "$lib/api/generated/prismedia";
import type { AcquireExecutorItemRequest, InspectExecutorRequest, ExecutorInspectionResponse, AcquireCatalogOfferRequest, IntegrationTransferResponse } from "$lib/api/generated/model";
import { unwrapGenerated } from "$lib/api/generated-response";

/** Accepts one explicit publication offer using an operation ID retained across uncertain retries. */
export const acquirePublication = (connectionId: string, request: AcquireCatalogOfferRequest): Promise<IntegrationTransferResponse> =>
  acquireCatalogOffer(connectionId, request).then(response => unwrapGenerated(response, "Could not accept this publication", [202]));

/** Requests one exact source item and follows its preparation before importing verified bytes. */
export const requestSourcePublication = (connectionId: string, request: AcquireCatalogOfferRequest): Promise<IntegrationTransferResponse> =>
  requestCatalogSource(connectionId, request).then(response => unwrapGenerated(response, "Could not request this publication", [202]));

/** Reads persisted transfer progress independently of queue history. */
export const fetchIntegrationTransfers = (): Promise<IntegrationTransferResponse[]> =>
  listIntegrationTransfers().then(response => unwrapGenerated(response, "Could not load publication transfers"));

/** Resumes an accepted operation without creating a new acquisition. */
export const retryPublicationTransfer = (id: string): Promise<void> =>
  retryIntegrationTransfer(id).then(response => unwrapGenerated(response, "Could not retry this publication", [202]));

/** Requests cancellation before library import; remote execution must be confirmed stopped. */
export const cancelPublicationTransfer = (id: string): Promise<IntegrationTransferResponse> =>
  cancelIntegrationTransfer(id).then(response => unwrapGenerated(response, "Could not cancel this publication"));

/** Inspects finite URL choices without creating a remote job. */
export const inspectPublicationUrl = (connectionId: string, request: InspectExecutorRequest): Promise<ExecutorInspectionResponse> =>
  inspectExecutorUrl(connectionId, request).then(response => unwrapGenerated(response, "Could not inspect this URL"));

/** Accepts a pinned executor selection with a stable operation identity. */
export const acquireExecutorPublication = (connectionId: string, request: AcquireExecutorItemRequest): Promise<IntegrationTransferResponse> =>
  acquireExecutorItem(connectionId, request).then(response => unwrapGenerated(response, "Could not accept this publication", [202]));
