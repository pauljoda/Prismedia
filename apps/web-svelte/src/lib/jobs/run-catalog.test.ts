import { describe, expect, it } from "vitest";
import { JOB_TYPE } from "$lib/api/generated/codes";
import { RUN_CATALOG } from "./run-catalog";

describe("RUN_CATALOG", () => {
  it("offers prose-book and serialized-comic scans as distinct jobs", () => {
    const scans = RUN_CATALOG.find((group) => group.id === "scans")?.entries ?? [];

    expect(scans.map((entry) => entry.jobType)).toContain(JOB_TYPE.scanBook);
    expect(scans.map((entry) => entry.jobType)).toContain(JOB_TYPE.scanComic);
    expect(scans.find((entry) => entry.jobType === JOB_TYPE.scanBook)?.description)
      .toContain("prose books");
    expect(scans.find((entry) => entry.jobType === JOB_TYPE.scanComic)?.description)
      .toContain("serialized comic");
  });
});
