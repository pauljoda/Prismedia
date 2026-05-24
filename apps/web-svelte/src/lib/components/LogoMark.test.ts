import { render } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import LogoMark from "./LogoMark.svelte";

let nsfwMode = "off";

vi.mock("$lib/nsfw/store.svelte", () => ({
  useNsfw: () => ({ mode: nsfwMode }),
}));

describe("LogoMark", () => {
  it("uses the yellow brand mark in SFW mode", () => {
    nsfwMode = "off";
    const { getByAltText } = render(LogoMark, {
      props: {
        alt: "Prismedia",
      },
    });

    expect(getByAltText("Prismedia")).toHaveAttribute("src", "/brand/prismedia-logo.png");
  });

  it("uses the red brand mark in NSFW mode", () => {
    nsfwMode = "show";
    const { getByAltText } = render(LogoMark, {
      props: {
        alt: "Prismedia",
      },
    });

    expect(getByAltText("Prismedia")).toHaveAttribute("src", "/brand/prismedia-logo-nsfw.png");
  });
});
