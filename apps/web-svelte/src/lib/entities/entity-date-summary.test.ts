import { describe, expect, it } from "vitest";
import { ENTITY_DATE_TYPE as DATE } from "$lib/entities/entity-codes";
import type { EntityDetailDate } from "./entity-detail";
import { summarizeEntityDates } from "./entity-date-summary";

const date = (code: string): EntityDetailDate => ({ code, label: code, value: "2026", display: "2026", sortable: null });

describe("Entity header dates", () => {
  it("prefers a specific release milestone over announcement, general release, and distribution dates", () => {
    const dates = [DATE.digitalRelease, DATE.announcement, DATE.release, DATE.theatricalRelease, DATE.physicalRelease].map(date);
    expect(summarizeEntityDates(dates).map(item => item.code)).toEqual([DATE.theatricalRelease]);
    expect(dates).toHaveLength(5);
  });
  it.each([DATE.publication, DATE.air, DATE.release])("keeps %s ahead of distribution dates", (code) => {
    expect(summarizeEntityDates([date(DATE.digitalRelease), date(code)])[0]?.code).toBe(code);
  });
  it.each([[DATE.birth, DATE.death], [DATE.firstAir, DATE.lastAir], [DATE.careerStart, DATE.careerEnd]])(
    "keeps the %s / %s range visible and ordered",
    (start, end) => expect(summarizeEntityDates([date(end), date(start), date(DATE.announcement)]).map(item => item.code)).toEqual([start, end]),
  );
  it("preserves an unfamiliar provider date as a fallback without rewriting it", () => {
    const custom = date("provider-milestone");
    expect(summarizeEntityDates([custom])).toEqual([custom]);
    expect(summarizeEntityDates([])).toEqual([]);
  });
});
