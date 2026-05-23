/**
 * Plugin executor — runs Prismedia-native plugins (Python or TypeScript)
 * via the stdin/stdout JSON protocol.
 */

import { spawn } from "node:child_process";
import path from "node:path";
import type {
  PrismediaPluginManifest,
  PluginInput,
  PluginExecutionInput,
  PluginExecutionOutput,
} from "./types";

export class PluginExecutionError extends Error {
  constructor(
    message: string,
    public readonly pluginId: string,
    public readonly action: string,
    public readonly stderr?: string,
  ) {
    super(message);
    this.name = "PluginExecutionError";
  }
}

export interface PluginExecutorOptions {
  timeoutMs?: number;
  pluginsRootDir?: string;
}

/**
 * Run a native Prismedia plugin (Python runtime) via stdin/stdout.
 */
export async function runNativePythonPlugin<T = unknown>(
  manifest: PrismediaPluginManifest,
  installDir: string,
  action: string,
  input: PluginInput,
  auth: Record<string, string>,
  options: PluginExecutorOptions = {},
): Promise<T | null> {
  const { timeoutMs = 30_000, pluginsRootDir } = options;

  if (!manifest.script || manifest.script.length === 0) {
    throw new PluginExecutionError(
      `Plugin "${manifest.id}" has no script defined`,
      manifest.id,
      action,
    );
  }

  const [command, ...args] = manifest.script;

  // Set PYTHONPATH for sibling package imports
  const pythonPath = pluginsRootDir ?? path.dirname(installDir);
  const env = {
    ...process.env,
    PYTHONPATH:
      pythonPath +
      (process.env.PYTHONPATH ? `:${process.env.PYTHONPATH}` : ""),
  };

  const envelope: PluginExecutionInput = {
    prismedia_version: 1,
    action,
    auth,
    input,
  };

  return new Promise<T | null>((resolve, reject) => {
    const child = spawn(command, args, {
      cwd: installDir,
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
        new PluginExecutionError(
          `Failed to spawn plugin: ${err.message}`,
          manifest.id,
          action,
          stderr,
        ),
      );
    });

    child.on("close", (code) => {
      clearTimeout(timer);

      if (killed) {
        reject(
          new PluginExecutionError(
            `Plugin timed out after ${timeoutMs}ms`,
            manifest.id,
            action,
            stderr,
          ),
        );
        return;
      }

      if (code !== 0 && code !== 69) {
        reject(
          new PluginExecutionError(
            `Plugin exited with code ${code ?? "unknown"}`,
            manifest.id,
            action,
            stderr,
          ),
        );
        return;
      }

      const trimmed = stdout.trim();
      if (!trimmed || trimmed === "null") {
        resolve(null);
        return;
      }

      try {
        const parsed = JSON.parse(trimmed) as PluginExecutionOutput<T>;
        if (!parsed.ok && parsed.error) {
          reject(
            new PluginExecutionError(parsed.error, manifest.id, action, stderr),
          );
          return;
        }
        resolve(parsed.result ?? null);
      } catch {
        reject(
          new PluginExecutionError(
            `Failed to parse plugin output: ${trimmed.slice(0, 200)}`,
            manifest.id,
            action,
            stderr,
          ),
        );
      }
    });

    child.stdin.write(JSON.stringify(envelope));
    child.stdin.end();
  });
}

/**
 * Run a native Prismedia plugin (Python) in batch mode.
 */
export async function runNativePythonPluginBatch<T = unknown>(
  manifest: PrismediaPluginManifest,
  installDir: string,
  action: string,
  items: Array<{ id: string; input: PluginInput }>,
  auth: Record<string, string>,
  options: PluginExecutorOptions = {},
): Promise<Array<{ id: string; result: T | null }>> {
  const { timeoutMs = 60_000, pluginsRootDir } = options;

  if (!manifest.script || manifest.script.length === 0) {
    throw new PluginExecutionError(
      `Plugin "${manifest.id}" has no script defined`,
      manifest.id,
      action,
    );
  }

  const [command, ...args] = manifest.script;

  const pythonPath = pluginsRootDir ?? path.dirname(installDir);
  const env = {
    ...process.env,
    PYTHONPATH:
      pythonPath +
      (process.env.PYTHONPATH ? `:${process.env.PYTHONPATH}` : ""),
  };

  const envelope: PluginExecutionInput = {
    prismedia_version: 1,
    action,
    auth,
    batch: items,
  };

  return new Promise<Array<{ id: string; result: T | null }>>(
    (resolve, reject) => {
      const child = spawn(command, args, {
        cwd: installDir,
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
          new PluginExecutionError(
            `Failed to spawn plugin batch: ${err.message}`,
            manifest.id,
            action,
            stderr,
          ),
        );
      });

      child.on("close", (code) => {
        clearTimeout(timer);

        if (killed) {
          reject(
            new PluginExecutionError(
              `Plugin batch timed out after ${timeoutMs}ms`,
              manifest.id,
              action,
              stderr,
            ),
          );
          return;
        }

        if (code !== 0 && code !== 69) {
          reject(
            new PluginExecutionError(
              `Plugin batch exited with code ${code ?? "unknown"}`,
              manifest.id,
              action,
              stderr,
            ),
          );
          return;
        }

        const trimmed = stdout.trim();
        if (!trimmed || trimmed === "null") {
          resolve(items.map((i) => ({ id: i.id, result: null })));
          return;
        }

        try {
          const parsed = JSON.parse(trimmed) as PluginExecutionOutput<T>;
          if (!parsed.ok && parsed.error) {
            reject(
              new PluginExecutionError(
                parsed.error,
                manifest.id,
                action,
                stderr,
              ),
            );
            return;
          }
          resolve(
            parsed.results ??
              items.map((i) => ({ id: i.id, result: null })),
          );
        } catch {
          reject(
            new PluginExecutionError(
              `Failed to parse plugin batch output: ${trimmed.slice(0, 200)}`,
              manifest.id,
              action,
              stderr,
            ),
          );
        }
      });

      child.stdin.write(JSON.stringify(envelope));
      child.stdin.end();
    },
  );
}
