import { spawn } from "node:child_process";
import path from "node:path";
import type {
  ScraperYamlDef,
  ScraperSceneFragment,
  ScraperPerformerFragment,
  ScraperSearchInput,
  StashScrapedScene,
  StashScrapedPerformer,
} from "./types";
import { getOwnString } from "./object";
import { resolveActionDef } from "./yaml-parser";

export class ScraperExecutionError extends Error {
  constructor(
    message: string,
    public readonly scraperName: string,
    public readonly action: string,
    public readonly stderr?: string
  ) {
    super(message);
    this.name = "ScraperExecutionError";
  }
}

export type ScraperInput =
  | ScraperSceneFragment
  | ScraperPerformerFragment
  | ScraperSearchInput;

export interface ExecutorOptions {
  timeoutMs?: number;
  scrapersRootDir?: string;
}

/**
 * Run a Stash community scraper script via the stdin/stdout JSON protocol.
 *
 * @param yamlPath    Absolute path to the scraper's .yml definition file
 * @param action      The scraper action to run (e.g. "sceneByURL", "sceneByFragment")
 * @param input       JSON payload to send to the script via stdin
 * @param options     Execution options
 * @returns           Parsed JSON output from the script, or null if no result
 */
export async function runScraperScript<T = StashScrapedScene>(
  yamlPath: string,
  definition: ScraperYamlDef,
  action: string,
  input: ScraperInput,
  options: ExecutorOptions = {}
): Promise<T | null> {
  const { timeoutMs = 30_000, scrapersRootDir } = options;
  const inputUrl = getOwnString(input, "url");

  const actionDef = resolveActionDef(definition, action, inputUrl);
  if (!actionDef || actionDef.action !== "script") {
    throw new ScraperExecutionError(
      `Scraper "${definition.name}" does not support action "${action}"`,
      definition.name,
      action
    );
  }

  const scraperDir = path.dirname(yamlPath);

  const [command, ...args] = actionDef.script;

  // Set PYTHONPATH so sibling packages (py_common, etc.) resolve
  const pythonPath = scrapersRootDir ?? path.dirname(scraperDir);
  const env = {
    ...process.env,
    PYTHONPATH: pythonPath + (process.env.PYTHONPATH ? `:${process.env.PYTHONPATH}` : ""),
  };

  return new Promise<T | null>((resolve, reject) => {
    const child = spawn(command, args, {
      cwd: scraperDir,
      stdio: ["pipe", "pipe", "pipe"],
      env,
    });

    let stdout = "";
    let stderr = "";
    let killed = false;

    const timer = setTimeout(() => {
      killed = true;
      child.kill("SIGTERM");
    }, timeoutMs);

    child.stdout.on("data", (chunk: Buffer) => {
      stdout += chunk.toString();
    });

    child.stderr.on("data", (chunk: Buffer) => {
      stderr += chunk.toString();
    });

    child.on("error", (err) => {
      clearTimeout(timer);
      reject(
        new ScraperExecutionError(
          `Failed to spawn scraper: ${err.message}`,
          definition.name,
          action,
          stderr
        )
      );
    });

    child.on("close", (code) => {
      clearTimeout(timer);

      if (killed) {
        reject(
          new ScraperExecutionError(
            `Scraper timed out after ${timeoutMs}ms`,
            definition.name,
            action,
            stderr
          )
        );
        return;
      }

      if (code !== 0 && code !== 69) {
        reject(
          new ScraperExecutionError(
            `Scraper exited with code ${code ?? "unknown"}`,
            definition.name,
            action,
            stderr
          )
        );
        return;
      }

      // Code 69 = uncaught Python exception, script outputs "null"
      const trimmed = stdout.trim();
      if (!trimmed || trimmed === "null") {
        resolve(null);
        return;
      }

      try {
        resolve(JSON.parse(trimmed) as T);
      } catch {
        reject(
          new ScraperExecutionError(
            `Failed to parse scraper output as JSON: ${trimmed.slice(0, 200)}`,
            definition.name,
            action,
            stderr
          )
        );
      }
    });

    // Write input to stdin and close
    const payload = JSON.stringify(input);
    child.stdin.write(payload);
    child.stdin.end();
  });
}

/**
 * Convenience wrapper for running scene scraper actions.
 * Automatically routes to the correct engine (script vs XPath) based on the YAML definition.
 */
export async function scrapeScene(
  yamlPath: string,
  definition: ScraperYamlDef,
  action: "sceneByURL" | "sceneByFragment" | "sceneByName" | "sceneByQueryFragment",
  input: ScraperSceneFragment | ScraperSearchInput,
  options?: ExecutorOptions
): Promise<StashScrapedScene | StashScrapedScene[] | null> {
  const inputUrl = getOwnString(input, "url");
  const actionDef = resolveActionDef(definition, action, inputUrl);

  if (!actionDef) {
    throw new ScraperExecutionError(
      `Scraper "${definition.name}" does not support action "${action}"`,
      definition.name,
      action
    );
  }

  // Route to the correct engine
  if (actionDef.action === "scrapeXPath") {
    const { runXPathScraper } = await import("./xpath-scraper");
    return runXPathScraper(definition, action, input, {
      timeoutMs: options?.timeoutMs,
    });
  }

  // Script-based scraper
  if (action === "sceneByName") {
    return runScraperScript<StashScrapedScene[]>(
      yamlPath,
      definition,
      action,
      input,
      options
    );
  }

  return runScraperScript<StashScrapedScene>(
    yamlPath,
    definition,
    action,
    input,
    options
  );
}

/**
 * Convenience wrapper for running performer scraper actions.
 */
export async function scrapePerformer(
  yamlPath: string,
  definition: ScraperYamlDef,
  action: "performerByURL" | "performerByFragment" | "performerByName",
  input: ScraperPerformerFragment | ScraperSearchInput,
  options?: ExecutorOptions
): Promise<StashScrapedPerformer | StashScrapedPerformer[] | null> {
  if (action === "performerByName") {
    return runScraperScript<StashScrapedPerformer[]>(
      yamlPath,
      definition,
      action,
      input,
      options
    );
  }

  return runScraperScript<StashScrapedPerformer>(
    yamlPath,
    definition,
    action,
    input,
    options
  );
}
