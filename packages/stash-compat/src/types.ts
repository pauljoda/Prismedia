/**
 * Types for the Stash community scraper protocol.
 *
 * These types mirror the YAML definition format and the stdin/stdout
 * JSON protocol used by Python script scrapers.
 */

// ─── YAML Definition Types ──────────────────────────────────────────

export type ScraperAction = "script" | "scrapeXPath" | "scrapeJson" | "stash";

export interface ScraperScriptDef {
  action: "script";
  url?: string[];
  script: string[];
}

export interface ScraperXPathDef {
  action: "scrapeXPath";
  url?: string[];
  scraper: string;
  /** URL template for fragment-based lookups, e.g. "https://example.com/video.{filename}/x" */
  queryURL?: string;
  /** Regex replacements applied to template placeholders */
  queryURLReplace?: Record<string, Array<{ regex: string; with?: string }>>;
}

export type ScraperActionDef = ScraperScriptDef | ScraperXPathDef;

/** Post-process rule applied to a scraped value */
export interface XPathPostProcess {
  replace?: Array<{ regex: string; with: string }>;
  parseDate?: string;
}

/** A single XPath field selector — either a bare string or an object with selector + postProcess */
export type XPathFieldDef =
  | string
  | { selector: string; postProcess?: XPathPostProcess[] };

/** Sub-object selector (e.g. Performers: { Name: "//xpath" }) */
export type XPathSubObjectDef = Record<string, XPathFieldDef>;

/** A full scene/performer scraper definition from the xPathScrapers block */
export interface XPathScraperDef {
  /** Common variable definitions — keys like "$foo" are replaced in selectors before evaluation */
  common?: Record<string, string>;
  scene?: Record<string, XPathFieldDef | XPathSubObjectDef>;
  performer?: Record<string, XPathFieldDef | XPathSubObjectDef>;
}

export interface ScraperCookieDef {
  Name: string;
  Value: string;
  Domain?: string;
  Path?: string;
}

export interface ScraperDriverCookieGroup {
  CookieURL: string;
  Cookies: ScraperCookieDef[];
}

export interface ScraperYamlDef {
  name: string;
  requires?: string[];

  sceneByURL?: ScraperActionDef | ScraperActionDef[];
  sceneByFragment?: ScraperActionDef;
  sceneByName?: ScraperActionDef;
  sceneByQueryFragment?: ScraperActionDef;

  performerByURL?: ScraperActionDef | ScraperActionDef[];
  performerByFragment?: ScraperActionDef;
  performerByName?: ScraperActionDef;

  galleryByURL?: ScraperActionDef | ScraperActionDef[];
  galleryByFragment?: ScraperActionDef;

  groupByURL?: ScraperActionDef | ScraperActionDef[];

  /** Named XPath scraper definitions referenced by scrapeXPath actions */
  xPathScrapers?: Record<string, XPathScraperDef>;

  /** Driver configuration including cookies for age gates etc. */
  driver?: {
    cookies?: ScraperDriverCookieGroup[];
  };
}

// ─── Scraper Capabilities ───────────────────────────────────────────

export interface ScraperCapabilities {
  sceneByURL: boolean;
  sceneByFragment: boolean;
  sceneByName: boolean;
  sceneByQueryFragment: boolean;
  performerByURL: boolean;
  performerByFragment: boolean;
  performerByName: boolean;
  galleryByURL: boolean;
  galleryByFragment: boolean;
  groupByURL: boolean;
}

export const capabilityKeys: (keyof ScraperCapabilities)[] = [
  "sceneByURL",
  "sceneByFragment",
  "sceneByName",
  "sceneByQueryFragment",
  "performerByURL",
  "performerByFragment",
  "performerByName",
  "galleryByURL",
  "galleryByFragment",
  "groupByURL",
];

// ─── Scraper Input (stdin payload) ──────────────────────────────────

export interface ScraperSceneFragment {
  title?: string;
  url?: string;
  date?: string;
  details?: string;
  oshash?: string;
  checksum?: string;
  duration?: number;
  file_path?: string;
}

export interface ScraperPerformerFragment {
  name?: string;
  url?: string;
}

export interface ScraperSearchInput {
  name: string;
}

// ─── Scraper Output (stdout payload) ────────────────────────────────

export interface StashScrapedTag {
  name: string;
  stored_id?: string;
}

export interface StashScrapedPerformer {
  name: string;
  stored_id?: string;
  disambiguation?: string;
  gender?: string;
  urls?: string[];
  birthdate?: string;
  country?: string;
  ethnicity?: string;
  eye_color?: string;
  hair_color?: string;
  height?: string;
  weight?: string;
  measurements?: string;
  tattoos?: string;
  piercings?: string;
  aliases?: string;
  tags?: StashScrapedTag[];
  image?: string;
  images?: string[];
  details?: string;
}

export interface StashScrapedStudio {
  name: string;
  stored_id?: string;
  url?: string;
  urls?: string[];
  parent?: StashScrapedStudio;
  image?: string;
}

export interface StashScrapedScene {
  title?: string;
  code?: string;
  details?: string;
  director?: string;
  urls?: string[];
  url?: string;
  date?: string;
  image?: string;
  studio?: StashScrapedStudio;
  tags?: StashScrapedTag[];
  performers?: StashScrapedPerformer[];
  duration?: number;
}

export interface StashScrapedGroup {
  name?: string;
  date?: string;
  director?: string;
  urls?: string[];
  synopsis?: string;
  studio?: StashScrapedStudio;
  front_image?: string;
}

// ─── Normalized Output (Prismedia domain) ─────────────────────────────

export interface NormalizedScrapeResult {
  title: string | null;
  date: string | null;
  details: string | null;
  url: string | null;
  studioName: string | null;
  performerNames: string[];
  tagNames: string[];
  imageUrl: string | null;
}
