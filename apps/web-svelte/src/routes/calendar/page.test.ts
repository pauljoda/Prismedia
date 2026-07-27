import { cleanup, fireEvent, render, screen, within } from "@testing-library/svelte";
import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { DATE_PRECISION, ENTITY_DATE_TYPE, ENTITY_KIND } from "$lib/api/generated/codes";
import type { ReleaseCalendarEvent } from "$lib/api/generated/model";
import { fetchReleaseCalendar } from "$lib/api/release-calendar";
import Page from "./+page.svelte";

vi.mock("$lib/api/release-calendar", () => ({
  fetchReleaseCalendar: vi.fn(),
}));

vi.mock("$lib/nsfw/store.svelte", () => ({
  useNsfw: () => ({ mode: "off" }),
}));

const mockedFetchReleaseCalendar = vi.mocked(fetchReleaseCalendar);

function seasonEvent(index: number, date: string): ReleaseCalendarEvent {
  return {
    entityId: `season-${index}`,
    monitorId: `monitor-${index}`,
    acquisitionId: null,
    kind: ENTITY_KIND.videoSeason,
    title: `Season ${index}`,
    parentEntityId: "series-1",
    parentKind: ENTITY_KIND.videoSeries,
    parentTitle: "It's Always Sunny in Philadelphia",
    dateType: ENTITY_DATE_TYPE.air,
    value: date,
    date,
    precision: DATE_PRECISION.day,
    acquisitionStatus: null,
    isSearchGate: false,
    searchNotBefore: null,
    isSearchEligible: null,
    posterUrl: null,
  };
}

beforeAll(() => {
  HTMLDialogElement.prototype.showModal = function showModal() {
    this.open = true;
  };
  HTMLDialogElement.prototype.close = function close() {
    this.open = false;
    this.dispatchEvent(new Event("close"));
  };
});

describe("release calendar page", () => {
  afterEach(() => {
    cleanup();
    mockedFetchReleaseCalendar.mockReset();
  });

  it("links nested seasons and reveals every event from a crowded day", async () => {
    mockedFetchReleaseCalendar.mockImplementation(async (start) =>
      Array.from({ length: 6 }, (_, index) => seasonEvent(index + 1, start)));

    render(Page);

    const overflow = await screen.findByRole("button", { name: /Show all 6 events for/ });
    await fireEvent.click(overflow);

    const dialog = await screen.findByRole("dialog", { name: /Release events for/ });
    const eventLinks = within(dialog).getAllByRole("link");
    expect(eventLinks).toHaveLength(6);
    expect(eventLinks[0].querySelector("[title]")).toHaveAttribute(
      "title",
      "It's Always Sunny in Philadelphia · Season 1",
    );
    expect(eventLinks[0]).toHaveAttribute("href", "/series/series-1/seasons/season-1");
    expect(eventLinks[5]).toHaveAttribute("href", "/series/series-1/seasons/season-6");
  });
});
