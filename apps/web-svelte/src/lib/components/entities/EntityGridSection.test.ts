import { fireEvent, render, screen } from "@testing-library/svelte";
import { beforeEach, describe, expect, it } from "vitest";
import EntityGridSectionHarness from "./EntityGridSection.test-harness.svelte";

describe("EntityGridSection", () => {
  beforeEach(() => {
    Object.defineProperty(window, "localStorage", {
      configurable: true,
      value: createLocalStorageStub(),
    });
  });

  it("collapses and hides its grid content from the heading chevron", async () => {
    render(EntityGridSectionHarness, { props: { prefsKey: "sub-galleries" } });

    const toggle = screen.getByRole("button", { name: /Sub Galleries/ });
    expect(toggle).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByTestId("section-grid")).toBeInTheDocument();

    await fireEvent.click(toggle);

    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByTestId("section-grid")).not.toBeInTheDocument();
    expect(window.localStorage.getItem("prismedia:entity-grid-section:sub-galleries")).toBe("collapsed");
  });

  it("restores a persisted collapsed section on mount", () => {
    window.localStorage.setItem("prismedia:entity-grid-section:sub-galleries", "collapsed");

    render(EntityGridSectionHarness, { props: { prefsKey: "sub-galleries" } });

    expect(screen.getByRole("button", { name: /Sub Galleries/ })).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByTestId("section-grid")).not.toBeInTheDocument();
  });

  it("restores each entity's preference when a detail route reuses the section", async () => {
    window.localStorage.setItem("prismedia:entity-grid-section:album-one", "collapsed");
    const { rerender } = render(EntityGridSectionHarness, { prefsKey: "album-one" });
    expect(screen.getByRole("button", { name: /Sub Galleries/ })).toHaveAttribute("aria-expanded", "false");

    await rerender({ prefsKey: "album-two" });
    expect(screen.getByRole("button", { name: /Sub Galleries/ })).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("heading", { level: 2, name: /Sub Galleries/ })).toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: /Sub Galleries/ }));
    expect(window.localStorage.getItem("prismedia:entity-grid-section:album-two")).toBe("collapsed");
  });
});

function createLocalStorageStub(): Storage {
  const values = new Map<string, string>();
  return {
    get length() {
      return values.size;
    },
    clear: () => values.clear(),
    getItem: (key: string) => values.get(key) ?? null,
    key: (index: number) => Array.from(values.keys())[index] ?? null,
    removeItem: (key: string) => {
      values.delete(key);
    },
    setItem: (key: string, value: string) => {
      values.set(key, String(value));
    },
  };
}
