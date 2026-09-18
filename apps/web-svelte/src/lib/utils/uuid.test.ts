import { afterEach, describe, expect, it, vi } from "vitest";
import { createUuid } from "./uuid";

const UUID_V4 = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("createUuid", () => {
  it("produces a valid RFC 4122 v4 UUID", () => {
    expect(createUuid()).toMatch(UUID_V4);
  });

  it("stays a valid UUID on insecure origins where randomUUID is unavailable", () => {
    // Plain-HTTP LAN origins are not secure contexts, so the browser omits
    // crypto.randomUUID while still exposing crypto.getRandomValues. Ids here reach
    // Guid-typed contracts, so a non-UUID shape fails server binding with HTTP 400.
    vi.stubGlobal("crypto", {
      getRandomValues: (bytes: Uint8Array) => {
        for (let index = 0; index < bytes.length; index += 1) bytes[index] = index * 7 + 3;
        return bytes;
      },
    });

    expect(createUuid()).toMatch(UUID_V4);
  });

  it("stays a valid UUID when Web Crypto is missing entirely", () => {
    vi.stubGlobal("crypto", undefined);

    expect(createUuid()).toMatch(UUID_V4);
  });

  it("does not repeat ids across calls", () => {
    const ids = new Set(Array.from({ length: 200 }, () => createUuid()));

    expect(ids.size).toBe(200);
  });
});
