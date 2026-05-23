import { describe, expect, it } from "vitest";
import {
  layoutPlayerMobileFlyout,
  playerFlyoutStyleToString,
} from "./flyout-layout";

function rect(top: number, bottom: number, right = 300): DOMRect {
  return {
    x: right - 40,
    y: top,
    top,
    bottom,
    left: right - 40,
    right,
    width: 40,
    height: bottom - top,
    toJSON: () => ({}),
  } as DOMRect;
}

describe("player flyout layout", () => {
  it("opens below the trigger when there is more room below", () => {
    const layout = layoutPlayerMobileFlyout(rect(100, 140), {
      vh: 600,
      maxHeightVh: 0.6,
      safeAreaInsets: { top: 0, bottom: 0 },
    });

    expect(layout.top).toBe("148px");
    expect(layout.bottom).toBe("auto");
    expect(layout.maxHeight).toBe("358px");
  });

  it("opens above the trigger when there is more room above", () => {
    const layout = layoutPlayerMobileFlyout(rect(520, 560), {
      vh: 600,
      maxHeightVh: 0.6,
      safeAreaInsets: { top: 0, bottom: 0 },
    });

    expect(layout.top).toBe("auto");
    expect(layout.bottom).toBe("88px");
    expect(layout.maxHeight).toBe("358px");
  });

  it("clamps trigger-anchored flyouts inside the viewport horizontally", () => {
    const layout = layoutPlayerMobileFlyout(rect(520, 560, 110), {
      vh: 600,
      vw: 640,
      maxHeightVh: 0.6,
      preferredWidth: 360,
      minWidth: 220,
      gutter: 12,
      safeAreaInsets: { top: 0, bottom: 0 },
    });

    expect(layout.left).toBe("12px");
    expect(layout.right).toBe("auto");
    expect(layout.width).toBe("360px");
    expect(layout.minWidth).toBe("220px");
    expect(layout.maxWidth).toBe("616px");
  });

  it("serializes layout objects for Svelte style attributes", () => {
    const layout = layoutPlayerMobileFlyout(rect(520, 560, 610), {
      vh: 600,
      vw: 640,
      maxHeightVh: 0.6,
      preferredWidth: 360,
      safeAreaInsets: { top: 0, bottom: 0 },
    });

    expect(playerFlyoutStyleToString(layout)).toContain("max-height:358px");
    expect(playerFlyoutStyleToString(layout)).toContain("right:auto");
  });
});
