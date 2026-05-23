import { fireEvent, render } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import Sidebar from "./Sidebar.svelte";

vi.mock("$app/state", () => ({
  page: {
    url: new URL("http://localhost/videos/example"),
  },
}));

vi.mock("$lib/version", () => ({
  APP_VERSION: "0.0.0-test",
  fetchReleaseUpdateStatus: vi.fn().mockResolvedValue(null),
}));

vi.mock("./LogoMark.svelte", () => ({
  default: () => "LogoMark",
}));

describe("Sidebar", () => {
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
});
