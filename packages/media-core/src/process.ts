import { spawn } from "node:child_process";

export async function runProcess(
  command: string,
  args: string[],
  options?: { cwd?: string },
) {
  return new Promise<{ stdout: string; stderr: string }>((resolve, reject) => {
    const child = spawn(command, args, {
      cwd: options?.cwd,
      stdio: ["ignore", "pipe", "pipe"],
    });

    let stdout = "";
    let stderr = "";

    child.stdout.on("data", (chunk) => {
      stdout += chunk.toString();
    });

    child.stderr.on("data", (chunk) => {
      stderr += chunk.toString();
    });

    child.on("error", reject);
    child.on("close", (code) => {
      if (code === 0) {
        resolve({ stdout, stderr });
        return;
      }

      reject(
        new Error(
          `${command} exited with code ${code ?? "unknown"}${
            stderr ? `: ${stderr.trim()}` : ""
          }`,
        ),
      );
    });
  });
}

/**
 * Error thrown when ffprobe/ffmpeg can't read a source file because it is
 * truncated, has no valid container header, or otherwise has no decodable
 * streams. These files are genuinely broken on disk, so retrying will not help.
 */
export class CorruptMediaError extends Error {
  filePath: string;
  cause?: Error;

  constructor(filePath: string, cause?: Error) {
    super(
      `Media file is corrupt or unreadable: ${filePath}${
        cause?.message ? ` - ${cause.message}` : ""
      }`,
    );
    this.name = "CorruptMediaError";
    this.filePath = filePath;
    this.cause = cause;
  }
}

export function isCorruptMediaError(err: unknown): boolean {
  if (!(err instanceof Error)) return false;
  const message = err.message ?? "";
  return (
    /moov atom not found/i.test(message) ||
    /Invalid data found when processing input/i.test(message) ||
    (/Invalid argument/i.test(message) && /ffprobe|ffmpeg/i.test(message)) ||
    (/End of file/i.test(message) && /ffprobe|ffmpeg/i.test(message)) ||
    /could not find codec parameters/i.test(message)
  );
}
