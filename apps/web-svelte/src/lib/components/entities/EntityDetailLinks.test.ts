import { render, screen } from "@testing-library/svelte";
import { describe, expect, it } from "vitest";
import { EXTERNAL_ID_PROVIDER } from "$lib/api/generated/codes";
import EntityDetailLinks from "./EntityDetailLinks.svelte";

describe("EntityDetailLinks", () => {
  it("shows one destination when a website duplicates the provider link, retaining its ID", () => {
    render(EntityDetailLinks, { links: [
      { label: "Website", url: "https://example.test/movie/42" },
      { label: `${EXTERNAL_ID_PROVIDER.tmdb}: 42`, provider: EXTERNAL_ID_PROVIDER.tmdb, url: "https://EXAMPLE.test:443/movie/42" },
    ] });
    expect(screen.getAllByRole("link")).toHaveLength(1);
    expect(screen.getByText("42")).toBeInTheDocument();
    expect(screen.getByRole("link")).toHaveAttribute("href", "https://EXAMPLE.test:443/movie/42");
    expect(screen.getByRole("link")).toHaveAttribute("rel", "noopener noreferrer");
  });

  it("keeps distinct pages, query values, and linkless provider IDs available", () => {
    render(EntityDetailLinks, { links: [
      { label: "Website", url: "https://example.test/movie/42?view=credits" },
      { label: "Other page", url: "https://example.test/movie/43" },
      { label: `${EXTERNAL_ID_PROVIDER.tmdb}: 42`, provider: EXTERNAL_ID_PROVIDER.tmdb, url: "https://example.test/movie/42" },
      { label: `${EXTERNAL_ID_PROVIDER.imdb}: tt123`, provider: EXTERNAL_ID_PROVIDER.imdb, url: null },
    ] });
    expect(screen.getAllByRole("link")).toHaveLength(3);
    expect(screen.getByText("tt123")).toBeInTheDocument();
    expect(screen.getByText("tt123").closest("a")).toBeNull();
    expect(screen.getByRole("region", { name: "Websites" })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Provider IDs" })).toBeInTheDocument();
  });
});
