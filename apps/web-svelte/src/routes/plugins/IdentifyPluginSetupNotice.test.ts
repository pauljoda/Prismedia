import { render, screen } from "@testing-library/svelte";
import { describe, expect, it } from "vitest";
import { ENTITY_KIND } from "$lib/api/generated/codes";
import IdentifyPluginSetupNotice from "./IdentifyPluginSetupNotice.svelte";

describe("Identify plugin setup", () => {
  it("explains the required setup and returns to the same review", () => {
    render(IdentifyPluginSetupNotice, { entityId: "book-1", entityKind: ENTITY_KIND.book, ready: false });
    expect(screen.getByText("Set up metadata for Books")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Return to Identify" })).toHaveAttribute(
      "href", "/identify/book-1?returnId=book-1",
    );
  });

  it("announces when the provider is ready without automatically searching or applying metadata", () => {
    render(IdentifyPluginSetupNotice, { entityId: "book-1", entityKind: ENTITY_KIND.book, ready: true });
    expect(screen.getByText("Metadata provider ready")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Continue Identify" })).toHaveAttribute(
      "href", "/identify/book-1?returnId=book-1",
    );
  });
});
