import { describe, expect, it } from "vitest";
import type { AcquisitionTransferView } from "$lib/api/generated/model";
import { presentAcquisitionTransfer } from "./acquisition-transfer-presentation";

const transfer: AcquisitionTransferView = {
  state: null, progress: "0.425", totalSizeBytes: "2000000",
  downloadSpeedBytesPerSecond: "1000000", etaSeconds: "60", seeds: 2, peers: 5,
  savePath: null, pieceStates: ["2", "1", "0"],
};

describe("acquisition transfer presentation", () => {
  it("normalizes generated numeric wire values for the shared view", () => {
    expect(presentAcquisitionTransfer(transfer)).toMatchObject({
      percent: 43, size: "2.0 MB", speed: "1.0 MB/s", pieces: [2, 1, 0], peers: "2 / 5",
    });
  });
  it.each([[-0.5, 0], [1.5, 100], [Number.NaN, null]])("bounds progress %s as %s", (progress, percent) => {
    expect(presentAcquisitionTransfer({ ...transfer, progress })?.percent).toBe(percent);
  });
  it("does not invent a transfer before the client responds", () => {
    expect(presentAcquisitionTransfer(null)).toBeNull();
  });
});
