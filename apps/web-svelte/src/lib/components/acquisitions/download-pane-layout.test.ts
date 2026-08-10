import { describe, expect, it } from "vitest";
import {
  MIN_DOWNLOAD_DETAIL_HEIGHT_PX,
  MIN_DOWNLOAD_LIST_HEIGHT_PX,
  clampDownloadDetailShare,
  downloadDetailShareForHeight,
} from "./download-pane-layout";

describe("download pane layout", () => {
  it("lets the inspector grow while preserving a usable transfer list", () => {
    const share = downloadDetailShareForHeight(900, 1000);
    const available = 988;
    expect(share * available).toBeCloseTo(available - MIN_DOWNLOAD_LIST_HEIGHT_PX);
  });

  it("keeps a dragged inspector above its minimum height", () => {
    const share = downloadDetailShareForHeight(20, 1000);
    expect(share * 988).toBeCloseTo(MIN_DOWNLOAD_DETAIL_HEIGHT_PX);
  });

  it("retains a safe share when the viewport is too short for both ideal minimums", () => {
    expect(clampDownloadDetailShare(0.6, 360)).toBe(0.6);
  });
});
