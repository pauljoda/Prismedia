import { describe, expect, it } from "vitest";
import { resolveActionDef } from "./yaml-parser";
import type { ScraperActionDef, ScraperYamlDef } from "./types";

describe("resolveActionDef", () => {
  const scriptDef: ScraperActionDef = {
    action: "script",
    script: ["python", "scraper.py"],
  };
  const xpathDef: ScraperActionDef = {
    action: "scrapeXPath",
    scraper: "theXPath",
  };

  it("returns a single action definition as-is", () => {
    const def: ScraperYamlDef = {
      name: "test",
      sceneByURL: scriptDef,
    };
    expect(resolveActionDef(def, "sceneByURL")).toBe(scriptDef);
  });

  it("returns null when action is missing", () => {
    const def: ScraperYamlDef = { name: "test" };
    expect(resolveActionDef(def, "sceneByURL")).toBeNull();
  });

  it("returns null when the matched entry has no action", () => {
    const def: ScraperYamlDef = {
      name: "test",
      sceneByURL: {} as ScraperActionDef,
    };
    expect(resolveActionDef(def, "sceneByURL")).toBeNull();
  });

  it("matches by url pattern when given an array of URL-scoped definitions", () => {
    const a: ScraperActionDef = { ...scriptDef, url: ["site-a.com"] };
    const b: ScraperActionDef = { ...xpathDef, url: ["site-b.com"] };
    const def: ScraperYamlDef = {
      name: "test",
      sceneByURL: [a, b],
    };
    expect(resolveActionDef(def, "sceneByURL", "https://site-b.com/x")).toBe(b);
    expect(resolveActionDef(def, "sceneByURL", "https://site-a.com/x")).toBe(a);
  });

  it("falls back to a generic (no-url) entry when no url pattern matches", () => {
    const specific: ScraperActionDef = { ...scriptDef, url: ["site-a.com"] };
    const generic: ScraperActionDef = { ...xpathDef };
    const def: ScraperYamlDef = {
      name: "test",
      sceneByURL: [specific, generic],
    };
    expect(resolveActionDef(def, "sceneByURL", "https://unknown.com/x")).toBe(generic);
  });

  it("returns the first valid action when no inputUrl is passed for an array entry", () => {
    const a: ScraperActionDef = { ...scriptDef, url: ["site-a.com"] };
    const b: ScraperActionDef = { ...xpathDef };
    const def: ScraperYamlDef = {
      name: "test",
      sceneByURL: [a, b],
    };
    expect(resolveActionDef(def, "sceneByURL")).toBe(a);
  });

  it("returns null when no candidate has an `action` field", () => {
    const def: ScraperYamlDef = {
      name: "test",
      sceneByURL: [{} as ScraperActionDef, {} as ScraperActionDef],
    };
    expect(resolveActionDef(def, "sceneByURL", "https://site-a.com/x")).toBeNull();
  });
});
