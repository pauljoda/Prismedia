import { describe, expect, it } from "vitest";
import { isFatalVideoDecodeError } from "./video-player-errors";

describe("isFatalVideoDecodeError", () => {
  it.each([3, 4])("recognizes fatal media error code %s", (code) => {
    expect(isFatalVideoDecodeError({ mediaError: { code } })).toBe(true);
  });

  it.each(["decode failed", "source not supported", "buffer append error", "SRC_NOT_SUPPORTED"])(
    "recognizes fatal decoder message %s",
    (message) => {
      expect(isFatalVideoDecodeError({ message })).toBe(true);
    },
  );

  it.each([null, { code: 1 }, { code: 2 }, { message: "network timeout" }, new Error("aborted")])(
    "leaves transient errors on the current source path",
    (detail) => {
      expect(isFatalVideoDecodeError(detail)).toBe(false);
    },
  );
});
