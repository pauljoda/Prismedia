/**
 * Stash adapter — wraps @prismedia/stash-compat scrapers into the PrismediaPlugin interface.
 *
 * This allows existing Stash community scrapers to participate in the Prismedia
 * plugin system without modification. The adapter maps Prismedia action names
 * to Stash action names and converts results to Prismedia normalized types.
 */

import {
  scrapeScene,
  scrapePerformer,
  normalizeSceneResult,
  normalizePerformerResult,
  parseScraperYaml,
  type ScraperYamlDef,
  type ScraperSceneFragment,
  type ScraperPerformerFragment,
  type ScraperSearchInput,
  type ExecutorOptions,
} from "@prismedia/stash-compat";
import type {
  PluginInput,
  NormalizedVideoResult,
} from "./types";
import { prismediaToStashActionMap } from "./types";

export class StashAdapterError extends Error {
  constructor(
    message: string,
    public readonly scraperId: string,
  ) {
    super(message);
    this.name = "StashAdapterError";
  }
}

/**
 * Execute a Stash community scraper as if it were a Prismedia plugin.
 *
 * @param yamlPath   Absolute path to the scraper's .yml definition
 * @param action     Prismedia action name (e.g. "videoByURL")
 * @param input      Prismedia plugin input
 * @param options    Executor options
 */
export async function executeStashScraper(
  yamlPath: string,
  action: string,
  input: PluginInput,
  options?: ExecutorOptions,
): Promise<NormalizedVideoResult | null> {
  const { definition } = await parseScraperYaml(yamlPath);
  const stashAction = prismediaToStashActionMap[action];

  if (!stashAction) {
    throw new StashAdapterError(
      `No Stash action mapping for "${action}"`,
      definition.name,
    );
  }

  // Scene actions
  if (stashAction.startsWith("scene")) {
    const fragment = pluginInputToSceneFragment(input);
    const result = await scrapeScene(
      yamlPath,
      definition,
      stashAction as "sceneByURL" | "sceneByFragment" | "sceneByName" | "sceneByQueryFragment",
      stashAction === "sceneByName"
        ? { name: input.title ?? input.name ?? "" }
        : fragment,
      options,
    );

    if (!result) return null;

    // scrapeScene may return an array for byName, take first
    const scene = Array.isArray(result) ? result[0] : result;
    if (!scene) return null;

    const normalized = normalizeSceneResult(scene);

    // Convert NormalizedScrapeResult (stash) to NormalizedVideoResult (prismedia)
    return {
      title: normalized.title,
      date: normalized.date,
      details: normalized.details,
      urls: normalized.url ? [normalized.url] : [],
      studioName: normalized.studioName,
      performerNames: normalized.performerNames,
      tagNames: normalized.tagNames,
      imageUrl: normalized.imageUrl,
      episodeNumber: null,
      series: null,
      code: null,
      director: null,
    };
  }

  // Performer actions — these return the performer result type
  if (stashAction.startsWith("performer")) {
    const fragment = pluginInputToPerformerFragment(input);
    const result = await scrapePerformer(
      yamlPath,
      definition,
      stashAction as "performerByURL" | "performerByFragment" | "performerByName",
      stashAction === "performerByName"
        ? { name: input.name ?? input.title ?? "" }
        : fragment,
      options,
    );

    // Performer results go through a different path — return null here
    // as this adapter focuses on video results. Performer results
    // are handled by the API layer that calls normalizePerformerResult directly.
    return null;
  }

  return null;
}

function pluginInputToSceneFragment(input: PluginInput): ScraperSceneFragment {
  return {
    title: input.title,
    url: input.url,
    date: input.date,
    details: input.details,
    oshash: input.oshash,
    checksum: input.checksumMd5,
    duration: input.duration,
    file_path: input.filePath,
  };
}

function pluginInputToPerformerFragment(
  input: PluginInput,
): ScraperPerformerFragment {
  return {
    name: input.name ?? input.title,
    url: input.url,
  };
}
