import { fireEvent, render } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import Sidebar from "./Sidebar.svelte";

const nsfwMode = vi.hoisted(() => ({ value: "off" }));

vi.mock("$app/state", () => ({
  page: {
    url: new URL("http://localhost/videos/example"),
  },
}));

vi.mock("$lib/version", () => ({
  APP_VERSION: "0.0.0-test",
  fetchReleaseUpdateStatus: vi.fn().mockResolvedValue(null),
}));

vi.mock("$lib/nsfw/store.svelte", () => ({
  useNsfw: () => ({ mode: nsfwMode.value }),
}));

vi.mock("./LogoMark.svelte", () => ({
  default: () => "LogoMark",
}));

describe("Sidebar", () => {
  beforeEach(() => {
    nsfwMode.value = "off";
  });

  it("stacks above page media controls while expanded on hover", async () => {
    const { container } = render(Sidebar, {
      props: {
        collapsed: true,
        onToggle: vi.fn(),
      },
    });
    const sidebar = container.querySelector("aside");

    expect(sidebar).toHaveClass("z-[1200]");

    await fireEvent.mouseEnter(sidebar as HTMLElement);

    expect(sidebar).toHaveClass("w-60");
    expect(sidebar).toHaveClass("z-[1200]");
  });

  it("uses a red logo glow while NSFW mode is active", () => {
    nsfwMode.value = "show";
    const { container } = render(Sidebar, {
      props: {
        collapsed: false,
        onToggle: vi.fn(),
      },
    });

    expect(container.querySelector(".brand-mark-backdrop")).toHaveClass("brand-mark-backdrop-nsfw");
  });
});
