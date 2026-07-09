import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import type { EntitySearchCandidate } from "$lib/api/identify-types";
import PluginCandidateList from "./PluginCandidateList.svelte";

describe("PluginCandidateList", () => {
  it("activates and previews provider candidates without adapting them into Entities", async () => {
    const candidate = result();
    const onActivate = vi.fn();
    const onPreview = vi.fn();
    render(PluginCandidateList, {
      props: {
        candidates: [candidate],
        entityKind: "video-series",
        onActivate,
        onPreview,
      },
    });

    await fireEvent.click(screen.getByRole("button", { name: "Use Andor (2022)" }));
    expect(onActivate).toHaveBeenCalledWith(candidate, "tmdb:83867");

    await fireEvent.click(screen.getByRole("button", { name: "Preview Andor artwork" }));
    expect(onPreview).toHaveBeenCalledWith(candidate, "tmdb:83867");
    expect(screen.getByText("Best")).toBeInTheDocument();
  });

  it("blocks activation while disabled", async () => {
    const onActivate = vi.fn();
    render(PluginCandidateList, {
      props: {
        candidates: [result()],
        entityKind: "video-series",
        onActivate,
        disabled: true,
      },
    });

    expect(screen.getByRole("button", { name: "Use Andor (2022)" })).toHaveAttribute("aria-disabled", "true");
    await fireEvent.click(screen.getByRole("button", { name: "Use Andor (2022)" }));
    expect(onActivate).not.toHaveBeenCalled();
  });

  it("omits the artwork action when the host does not provide a preview flow", () => {
    render(PluginCandidateList, {
      props: {
        candidates: [result()],
        entityKind: "video-series",
        onActivate: vi.fn(),
      },
    });

    expect(screen.queryByRole("button", { name: "Preview Andor artwork" })).not.toBeInTheDocument();
  });
});

function result(): EntitySearchCandidate {
  return {
    externalIds: { tmdb: "83867" },
    title: "Andor",
    year: 2022,
    overview: "A rebellion begins.",
    posterUrl: "https://image.tmdb.org/t/p/w500/andor.jpg",
    popularity: 10,
    candidateId: null,
    source: null,
    confidence: null,
    matchReason: null,
  };
}
