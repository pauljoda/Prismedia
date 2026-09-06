import { describe, expect, it } from "vitest";
import { cn } from "@prismedia/ui-svelte";

describe("semantic control tokens", () => {
  it("preserves typography beside foreground colors", () => {
    expect(cn("text-caption", "text-foreground")).toBe("text-caption text-foreground");
    expect(cn("text-label", "text-control")).toBe("text-control");
  });

  it("replaces semantic sizes with intentional layout overrides", () => {
    expect(cn("h-control-sm px-control-pad gap-control-gap-sm", "h-auto px-2 gap-1"))
      .toBe("h-auto px-2 gap-1");
    expect(cn("size-control", "size-control-lg")).toBe("size-control-lg");
  });
});
