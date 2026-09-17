import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it } from "vitest";
import SourceAttribution from "./SourceAttribution.svelte";

describe("source attribution", () => {
  it("shows source statements as text and provides explicit source and license links", async () => {
    const { container } = render(SourceAttribution, { attribution: {
      sourceUrl: "https://catalog.test/source", creator: "Example creator", credit: "<img src=x onerror=alert(1)>",
      licenseName: "CC BY 4.0", licenseUrl: "https://creativecommons.org/licenses/by/4.0/", usageTerms: "Attribution", attributionRequired: true,
    } });
    await fireEvent.click(screen.getByRole("button", { name: "Source attribution" }));
    expect(screen.getByText("Example creator")).toBeInTheDocument();
    expect(screen.getByText("<img src=x onerror=alert(1)>")).toBeInTheDocument();
    expect(container.querySelector("img")).toBeNull();
    expect(screen.getByRole("link", { name: "Source page" })).toHaveAttribute("href", "https://catalog.test/source");
    expect(screen.getByRole("link", { name: "License details" })).toHaveAttribute("href", "https://creativecommons.org/licenses/by/4.0/");
    expect(screen.getByText("Required by the source")).toBeInTheDocument();
  });
});
