import type { AcquisitionTransferView } from "$lib/api/generated/model";
import { formatBytes, formatEta, formatSpeed } from "$lib/utils/format";
import { isTransferActive, transferStageLabel } from "./acquisition-transfer";

/** Read-only transfer display values, independent of polling and download-client operations. */
export interface AcquisitionTransferPresentation {
  stage: string;
  active: boolean;
  percent: number | null;
  speed: string;
  eta: string;
  size: string;
  peers: string;
  pieces: number[];
}

/** Normalize generated numeric scalars once; unknown progress remains indeterminate. */
export function presentAcquisitionTransfer(transfer: AcquisitionTransferView | null): AcquisitionTransferPresentation | null {
  if (!transfer) return null;
  const progress = Number(transfer.progress);
  return {
    stage: transferStageLabel(transfer.state),
    active: isTransferActive(transfer.state),
    percent: Number.isFinite(progress) ? Math.round(Math.min(1, Math.max(0, progress)) * 100) : null,
    speed: formatSpeed(Number(transfer.downloadSpeedBytesPerSecond)),
    eta: formatEta(Number(transfer.etaSeconds)),
    size: formatBytes(Number(transfer.totalSizeBytes)),
    peers: `${transfer.seeds} / ${transfer.peers}`,
    pieces: transfer.pieceStates.map(Number),
  };
}
