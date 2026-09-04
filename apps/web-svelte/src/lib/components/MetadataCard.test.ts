import { render, screen } from "@testing-library/svelte";
import { describe, expect, it } from "vitest";
import MetadataCard from "./MetadataCard.svelte";

describe("MetadataCard", () => {
  it("turns machine-style field names into readable labels without rewriting deliberate codes", () => {
    render(MetadataCard, {
      title: "Stats",
      rows: [
        { label: "RuntimeMinutes", value: "90" },
        { label: "TMDB", value: "1315772" },
      ],
    });

    expect(screen.getByText("Runtime Minutes")).toBeInTheDocument();
    expect(screen.getByText("TMDB")).toBeInTheDocument();
  });
});
