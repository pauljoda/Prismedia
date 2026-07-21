import { describe, expect, it } from "vitest";
import { ACQUISITION_HISTORY_EVENT, BLOCKLIST_REASON, ENTITY_KIND } from "$lib/api/generated/codes";
import type { AcquisitionBlocklistEntry, AcquisitionHistoryView } from "$lib/api/generated/model";
import {
  blocklistGroupMatches,
  findBlocklistEntryForHistory,
  groupAcquisitionBlocklist,
} from "$lib/requests/acquisition-blocklist";

const blocked: AcquisitionBlocklistEntry = {
  id: "blocked-1",
  reason: BLOCKLIST_REASON.failed,
  title: "Hamilton.2015.FLAC",
  indexerName: "Music Indexer",
  infoHash: "ABC123",
  acquisitionId: "acquisition-1",
  entityId: "album-1",
  entityKind: ENTITY_KIND.audioLibrary,
  entityTitle: "Hamilton",
  message: "Download failed",
  createdAt: "2026-07-20T10:00:00Z",
};

const history: AcquisitionHistoryView = {
  id: "history-1",
  acquisitionId: "acquisition-1",
  entityId: "album-1",
  kind: ENTITY_KIND.audioLibrary,
  event: ACQUISITION_HISTORY_EVENT.blocklisted,
  title: "Hamilton",
  releaseTitle: "Hamilton.2015.FLAC",
  indexerName: "Music Indexer",
  downloadClientName: null,
  qualityCode: "lossless",
  formatScore: 100,
  message: "Download failed",
  createdAt: "2026-07-20T10:00:01Z",
};

describe("acquisition blocklist organization", () => {
  it("groups releases by their durable entity history", () => {
    const groups = groupAcquisitionBlocklist([blocked], [history]);

    expect(groups).toHaveLength(1);
    expect(groups[0]).toMatchObject({
      key: "entity:album-1",
      title: "Hamilton",
      entityId: "album-1",
      kind: ENTITY_KIND.audioLibrary,
    });
    expect(groups[0].entries).toEqual([blocked]);
  });

  it("matches entity, release, indexer, hash, and diagnostic text", () => {
    const group = groupAcquisitionBlocklist([blocked], [history])[0];

    expect(blocklistGroupMatches(group, "hamilton")).toBe(true);
    expect(blocklistGroupMatches(group, "music indexer")).toBe(true);
    expect(blocklistGroupMatches(group, "abc123")).toBe(true);
    expect(blocklistGroupMatches(group, "download failed")).toBe(true);
    expect(blocklistGroupMatches(group, "unrelated")).toBe(false);
  });

  it("finds the removable row for a blocklisted history event", () => {
    const otherRelease = {
      ...blocked,
      id: "blocked-2",
      title: "Different release",
    };
    expect(findBlocklistEntryForHistory(history, [otherRelease, blocked])).toEqual(blocked);
    expect(findBlocklistEntryForHistory({ ...history, acquisitionId: null }, [blocked])).toEqual(blocked);
  });
});
