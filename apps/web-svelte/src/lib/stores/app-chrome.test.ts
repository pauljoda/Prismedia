import { describe, expect, it } from "vitest";
import { AppChromeStore } from "./app-chrome.svelte";

describe("AppChromeStore bottom dock inset", () => {
  it("uses the tallest registered dock and clears it when unregistered", () => {
    const chrome = new AppChromeStore(false);

    chrome.setBottomDockInset("audio", 148);
    chrome.setBottomDockInset("preview", 72);

    expect(chrome.bottomDockInsetPx).toBe(148);

    chrome.clearBottomDockInset("audio");
    expect(chrome.bottomDockInsetPx).toBe(72);

    chrome.clearBottomDockInset("preview");
    expect(chrome.bottomDockInsetPx).toBe(0);
  });
});

describe("AppChromeStore breadcrumbs", () => {
  it("sets and clears page-provided breadcrumbs", () => {
    const chrome = new AppChromeStore(false);

    const clear = chrome.setBreadcrumbs([
      { label: "Videos", href: "/videos" },
      { label: "Series", href: "/series?series=series-1" },
      { label: "Episode 1" },
    ]);

    expect(chrome.breadcrumbs).toEqual([
      { label: "Videos", href: "/videos" },
      { label: "Series", href: "/series?series=series-1" },
      { label: "Episode 1" },
    ]);

    clear();
    expect(chrome.breadcrumbs).toEqual([]);
  });
});
