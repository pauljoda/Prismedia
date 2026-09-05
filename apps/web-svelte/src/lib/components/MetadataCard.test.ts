import { render, screen } from "@testing-library/svelte";
import { describe, expect, it } from "vitest";
import MetadataCard from "./MetadataCard.svelte";

describe("MetadataCard", () => {
  it("renders repeated labels and identical rows without dropping values, including after updates", async () => {
    const repeated = { label: "SHA256", value: "same-fingerprint" };
    const { container, rerender } = render(MetadataCard, { title: "Fingerprints", rows: [repeated, repeated, { ...repeated, value: "another-fingerprint" }] });
    expect(container.querySelectorAll("dt")).toHaveLength(3);
    expect(screen.getAllByText("same-fingerprint")).toHaveLength(2);
    await rerender({ title: "Fingerprints", rows: [{ ...repeated, value: "updated-fingerprint" }, repeated] });
    expect([...container.querySelectorAll("dd")].map(item => item.textContent)).toEqual(["updated-fingerprint", "same-fingerprint"]);
  });

  it("gives long source values their own full-width row without truncating their text", () => {
    const path = "/media/music/Artist/An album with a long title/source.flac";
    const { container } = render(MetadataCard, { title: "Source", stacked: true, monospace: true, rows: [{ label: "folder", value: path }] });
    expect(screen.getByText("Folder")).toBeInTheDocument();
    expect(screen.getByText(path)).toBeInTheDocument();
    expect(container.querySelector("dl")).toHaveClass("is-stacked", "is-monospace");
  });

  it("turns machine-style field names into readable labels without rewriting deliberate codes", () => {
    render(MetadataCard, {
      title: "Stats",
      rows: [
        { label: "RuntimeMinutes", value: "90" },
        { label: "TMDB", value: "1315772" },
      ],
    });

    const heading = screen.getByRole("heading", { name: "Stats" });

    expect(heading.closest('[data-slot="card"]')).toBeInTheDocument();
    expect(screen.getByText("Runtime Minutes")).toBeInTheDocument();
    expect(screen.getByText("TMDB")).toBeInTheDocument();
  });
});
