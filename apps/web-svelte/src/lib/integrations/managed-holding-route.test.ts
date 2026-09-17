import { describe, expect, it } from "vitest";
import { ENTITY_KIND } from "$lib/api/generated/codes";
import {
  managedHoldingHref,
  managedHoldingSourceHref,
  parseManagedHoldingIdentities,
} from "./managed-holding-route";

describe("managed holding route", () => {
  it("round-trips opaque route segments and an exact sorted identity pin", () => {
    const href = managedHoldingHref("source/id", {
      remoteId: "movie:part/1",
      entityKind: ENTITY_KIND.movie,
      title: "Arrival",
      year: 2016,
      externalIds: { zeta: "value/2", alpha: "Value:01" },
      monitored: true,
      profileId: null,
      remoteFileCount: 1,
    });
    const url = new URL(href, "http://localhost");

    expect(url.pathname).toBe("/request/source/source%2Fid/movie/movie%3Apart%2F1");
    expect(url.searchParams.get("identities")).toBe('{"alpha":"Value:01","zeta":"value/2"}');
    expect(parseManagedHoldingIdentities(url.searchParams.get("identities"))).toEqual({
      alpha: "Value:01",
      zeta: "value/2",
    });
  });

  it.each([
    null,
    "",
    "null",
    "[]",
    "{}",
    '{"provider":""}',
    '{"provider":"   "}',
    '{"":"identity"}',
    '{"provider":42}',
    '{"provider":"line\\nbreak"}',
  ])("rejects an invalid or unpinned identity payload: %s", (value) => {
    expect(parseManagedHoldingIdentities(value)).toBeNull();
  });

  it("preserves opaque identity case and surrounding non-blank whitespace", () => {
    expect(parseManagedHoldingIdentities('{"Provider":"  Value  "}')).toEqual({
      Provider: "  Value  ",
    });
  });

  it("restores the connected source and its Request media kind", () => {
    expect(managedHoldingSourceHref("source/id", ENTITY_KIND.movie)).toBe(
      "/request?connection=source%2Fid&kind=movie",
    );
  });
});
