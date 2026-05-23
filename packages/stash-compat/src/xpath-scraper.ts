import { createRequire } from "node:module";
import type {
  ScraperYamlDef,
  ScraperXPathDef,
  ScraperSceneFragment,
  StashScrapedScene,
  StashScrapedTag,
  StashScrapedPerformer,
  StashScrapedStudio,
  XPathScraperDef,
  XPathFieldDef,
  XPathPostProcess,
} from "./types";
import { resolveActionDef } from "./yaml-parser";
import { ScraperExecutionError } from "./executor";

const require = createRequire(import.meta.url);
const { JSDOM } = require("jsdom") as typeof import("jsdom");

/**
 * Run an XPath-based scraper action.
 *
 * 1. Resolves which URL to fetch (direct URL or queryURL template)
 * 2. Fetches the page HTML
 * 3. Applies XPath selectors from the xPathScrapers block
 * 4. Returns structured scene data
 */
export async function runXPathScraper(
  definition: ScraperYamlDef,
  action: string,
  input: ScraperSceneFragment | { name: string },
  options?: { timeoutMs?: number }
): Promise<StashScrapedScene | null> {
  const { timeoutMs = 15_000 } = options ?? {};

  const inputUrl =
    "url" in input && typeof input.url === "string" ? input.url : undefined;

  const actionDef = resolveActionDef(definition, action, inputUrl);
  if (!actionDef || actionDef.action !== "scrapeXPath") {
    throw new ScraperExecutionError(
      `Scraper "${definition.name}" does not have an XPath definition for "${action}"`,
      definition.name,
      action
    );
  }

  const xpathDef = actionDef as ScraperXPathDef;
  const scraperName = xpathDef.scraper;
  const xpathScraperDef = definition.xPathScrapers?.[scraperName];

  if (!xpathScraperDef?.scene) {
    throw new ScraperExecutionError(
      `XPath scraper "${scraperName}" not found or has no scene definition`,
      definition.name,
      action
    );
  }

  // Determine the URL to fetch
  let fetchUrl: string | null = null;

  if (action === "sceneByURL" || action === "performerByURL") {
    fetchUrl = inputUrl ?? null;
  } else if (xpathDef.queryURL && "file_path" in input) {
    // Build URL from queryURL template + queryURLReplace
    fetchUrl = buildQueryURL(
      xpathDef.queryURL,
      xpathDef.queryURLReplace ?? {},
      input as ScraperSceneFragment
    );
  } else if (inputUrl) {
    fetchUrl = inputUrl;
  }

  if (!fetchUrl) {
    return null;
  }

  // Build cookie header from driver.cookies that match the fetch URL
  const cookieHeader = buildCookieHeader(definition, fetchUrl);

  // Fetch the page
  let html: string;
  try {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), timeoutMs);
    const headers: Record<string, string> = {
      "User-Agent":
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
    };
    if (cookieHeader) {
      headers["Cookie"] = cookieHeader;
    }
    const res = await fetch(fetchUrl, {
      signal: controller.signal,
      headers,
    });
    clearTimeout(timer);

    if (!res.ok) {
      throw new ScraperExecutionError(
        `Failed to fetch ${fetchUrl}: HTTP ${res.status}`,
        definition.name,
        action
      );
    }

    html = await res.text();
  } catch (err) {
    if (err instanceof ScraperExecutionError) throw err;
    throw new ScraperExecutionError(
      `Network error fetching ${fetchUrl}: ${(err as Error).message}`,
      definition.name,
      action
    );
  }

  try {
    // Parse HTML and evaluate XPath selectors
    const dom = new JSDOM(html, { url: fetchUrl });
    const doc = dom.window.document;

    return evaluateSceneSelectors(doc, xpathScraperDef);
  } catch (err) {
    if (err instanceof ScraperExecutionError) throw err;
    throw new ScraperExecutionError(
      `XPath evaluation failed for ${fetchUrl}: ${(err as Error).message}`,
      definition.name,
      action
    );
  }
}

/**
 * Build a queryURL from a template and replacement rules.
 * e.g. "https://xvideos.com/video.{filename}/x" with filename extracted from file_path
 */
function buildQueryURL(
  template: string,
  replacements: Record<string, Array<{ regex: string; with?: string }>>,
  fragment: ScraperSceneFragment
): string | null {
  let url = template;

  for (const [placeholder, rules] of Object.entries(replacements)) {
    // Get the source value — for "filename", extract from file_path
    let value = "";
    if (placeholder === "filename" && fragment.file_path) {
      value = fragment.file_path.split("/").pop() ?? "";
    } else if (placeholder === "title" && fragment.title) {
      value = fragment.title;
    } else if (placeholder === "url" && fragment.url) {
      value = fragment.url;
    } else if (placeholder === "checksum" && fragment.checksum) {
      value = fragment.checksum;
    } else if (placeholder === "oshash" && fragment.oshash) {
      value = fragment.oshash;
    } else if (placeholder === "phash" && fragment.phash) {
      value = fragment.phash;
    }

    if (!value) return null;

    // Apply regex replacements in order
    for (const rule of rules) {
      const re = new RegExp(rule.regex, "s");
      if (re.test(value)) {
        value = value.replace(re, rule.with ?? "");
        break; // Stash applies first matching replacement
      }
    }

    if (!value) return null;

    url = url.replace(`{${placeholder}}`, value);
  }

  return url;
}

/**
 * Replace $variable references in a selector string using the common block.
 * Mirrors Stash's applyCommon() — simple string replacement.
 */
function applyCommon(selector: string, common: Record<string, string> | undefined): string {
  if (!common) return selector;
  let result = selector;
  for (const [key, value] of Object.entries(common)) {
    result = result.replaceAll(key, value);
  }
  return result;
}

/**
 * Apply common variable substitution to a field definition.
 */
function applyCommonToFieldDef(
  fieldDef: XPathFieldDef,
  common: Record<string, string> | undefined
): XPathFieldDef {
  if (!common) return fieldDef;
  if (typeof fieldDef === "string") {
    return applyCommon(fieldDef, common);
  }
  return { ...fieldDef, selector: applyCommon(fieldDef.selector, common) };
}

/**
 * Apply common variable substitution to all fields in a sub-object definition.
 */
function applyCommonToSubObject(
  subDef: Record<string, XPathFieldDef>,
  common: Record<string, string> | undefined
): Record<string, XPathFieldDef> {
  if (!common) return subDef;
  const result: Record<string, XPathFieldDef> = {};
  for (const [key, fieldDef] of Object.entries(subDef)) {
    result[key] = applyCommonToFieldDef(fieldDef, common);
  }
  return result;
}

/**
 * Evaluate XPath scene selectors against a parsed document.
 */
function evaluateSceneSelectors(
  doc: Document,
  scraperDef: XPathScraperDef
): StashScrapedScene | null {
  const sceneDef = scraperDef.scene;
  if (!sceneDef) return null;
  const common = scraperDef.common;

  const result: StashScrapedScene = {};

  for (const [field, selectorDef] of Object.entries(sceneDef)) {
    const fieldLower = field.toLowerCase();

    switch (fieldLower) {
      case "title":
        result.title = evaluateStringField(doc, applyCommonToFieldDef(selectorDef as XPathFieldDef, common)) ?? undefined;
        break;
      case "date":
        result.date = evaluateStringField(doc, applyCommonToFieldDef(selectorDef as XPathFieldDef, common)) ?? undefined;
        break;
      case "details":
        result.details = evaluateStringField(doc, applyCommonToFieldDef(selectorDef as XPathFieldDef, common)) ?? undefined;
        break;
      case "url":
        result.url = evaluateStringField(doc, applyCommonToFieldDef(selectorDef as XPathFieldDef, common)) ?? undefined;
        break;
      case "image":
        result.image = evaluateStringField(doc, applyCommonToFieldDef(selectorDef as XPathFieldDef, common)) ?? undefined;
        break;
      case "code":
        result.code = evaluateStringField(doc, applyCommonToFieldDef(selectorDef as XPathFieldDef, common)) ?? undefined;
        break;
      case "director":
        result.director = evaluateStringField(doc, applyCommonToFieldDef(selectorDef as XPathFieldDef, common)) ?? undefined;
        break;
      case "tags":
        result.tags = evaluateSubObjectArray(doc, applyCommonToSubObject(selectorDef as Record<string, XPathFieldDef>, common));
        break;
      case "performers":
        result.performers = evaluateSubObjectArray(doc, applyCommonToSubObject(selectorDef as Record<string, XPathFieldDef>, common));
        break;
      case "studio":
        result.studio = evaluateSubObject(doc, applyCommonToSubObject(selectorDef as Record<string, XPathFieldDef>, common));
        break;
    }
  }

  // If nothing was extracted, return null
  const hasData = result.title || result.url || result.date || result.details ||
    (result.tags && result.tags.length > 0) ||
    (result.performers && result.performers.length > 0) ||
    result.studio;

  return hasData ? result : null;
}

/**
 * Evaluate a single string field from an XPath selector.
 */
function evaluateStringField(doc: Document, fieldDef: XPathFieldDef): string | null {
  const { selector, postProcessRules } = parseFieldDef(fieldDef);

  const xpathResult = doc.evaluate(
    selector,
    doc,
    null,
    9, // FIRST_ORDERED_NODE_TYPE
    null
  );

  const node = xpathResult.singleNodeValue;
  if (!node) return null;

  let value = node.nodeType === 2 // Attr node
    ? (node as Attr).value
    : node.textContent ?? "";

  value = value.trim();
  if (!value) return null;

  return applyPostProcess(value, postProcessRules);
}

/**
 * Evaluate a sub-object array (e.g. Tags, Performers) — each XPath may return multiple nodes.
 */
function evaluateSubObjectArray(
  doc: Document,
  fieldDef: Record<string, XPathFieldDef>
): Array<{ name: string }> {
  const results: Array<{ name: string }> = [];

  // Find the field that determines cardinality (usually "Name")
  const nameField = fieldDef.Name ?? fieldDef.name;
  if (!nameField) return results;

  const { selector } = parseFieldDef(nameField);

  const xpathResult = doc.evaluate(
    selector,
    doc,
    null,
    7, // ORDERED_NODE_SNAPSHOT_TYPE
    null
  );

  for (let i = 0; i < xpathResult.snapshotLength; i++) {
    const node = xpathResult.snapshotItem(i);
    if (!node) continue;

    const text = (node.textContent ?? "").trim();
    if (text) {
      results.push({ name: text });
    }
  }

  return results;
}

/**
 * Evaluate a single sub-object (e.g. Studio).
 */
function evaluateSubObject(
  doc: Document,
  fieldDef: Record<string, XPathFieldDef>
): { name: string; url?: string } | undefined {
  const obj: Record<string, string> = {};

  for (const [key, selector] of Object.entries(fieldDef)) {
    const value = evaluateStringField(doc, selector);
    if (value) {
      obj[key.toLowerCase()] = value;
    }
  }

  if (!obj.name) return undefined;
  return { name: obj.name, url: obj.url };
}

/**
 * Parse a field definition into its selector and post-process rules.
 */
function parseFieldDef(fieldDef: XPathFieldDef): {
  selector: string;
  postProcessRules: XPathPostProcess[];
} {
  if (typeof fieldDef === "string") {
    return { selector: fieldDef, postProcessRules: [] };
  }

  return {
    selector: fieldDef.selector,
    postProcessRules: fieldDef.postProcess ?? [],
  };
}

/**
 * Apply post-processing rules to a scraped string value.
 */
function applyPostProcess(value: string, rules: XPathPostProcess[]): string {
  let result = value;

  for (const rule of rules) {
    if (rule.replace) {
      for (const rep of rule.replace) {
        // Use 's' (dotall) flag so '.' matches newlines — critical for
        // extracting values from multiline script blocks via regex anchors
        const re = new RegExp(rep.regex, "s");
        if (re.test(result)) {
          result = result.replace(re, rep.with ?? "");
        }
      }
    }

    if (rule.parseDate) {
      // parseDate format is Go-style (2006-01-02)
      // The value should already be in the right format after replace rules
      result = result.trim();
    }
  }

  return result;
}

/**
 * Build a Cookie header string from the scraper definition's driver.cookies
 * that match the given fetch URL.
 */
function buildCookieHeader(definition: ScraperYamlDef, fetchUrl: string): string | null {
  const groups = definition.driver?.cookies;
  if (!groups || groups.length === 0) return null;

  const pairs: string[] = [];
  for (const group of groups) {
    try {
      const cookieOrigin = new URL(group.CookieURL).hostname.replace(/^www\./, "");
      const fetchOrigin = new URL(fetchUrl).hostname.replace(/^www\./, "");
      if (!fetchOrigin.endsWith(cookieOrigin)) continue;
    } catch {
      continue;
    }
    for (const cookie of group.Cookies) {
      pairs.push(`${cookie.Name}=${cookie.Value}`);
    }
  }

  return pairs.length > 0 ? pairs.join("; ") : null;
}
