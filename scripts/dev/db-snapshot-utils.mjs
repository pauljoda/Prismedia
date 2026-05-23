import { createReadStream, createWriteStream } from "node:fs";
import { mkdir } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { spawn, spawnSync } from "node:child_process";

export const DEFAULT_DATABASE_URL = "postgresql://prismedia:prismedia@localhost:5432/prismedia";
export const DEFAULT_BACKUP_DIR = ".prismedia-dev/backups";
export const DEFAULT_COMPOSE_FILE = "infra/docker/docker-compose.yml";
export const DEFAULT_POSTGRES_SERVICE = "postgres";

export function parseArgs(argv) {
  const args = { _: [] };
  for (const arg of argv) {
    if (!arg.startsWith("--")) {
      args._.push(arg);
      continue;
    }

    const [key, ...valueParts] = arg.slice(2).split("=");
    args[key] = valueParts.length > 0 ? valueParts.join("=") : true;
  }
  return args;
}

export function timestamp(now = new Date()) {
  return now.toISOString().replaceAll("-", "").replaceAll(":", "").replace(/\.\d{3}Z$/, "Z");
}

export function databaseParts(databaseUrl) {
  const url = new URL(databaseUrl);
  return {
    database: url.pathname.replace(/^\//, "") || "prismedia",
    password: decodeURIComponent(url.password || "prismedia"),
    port: url.port || "5432",
    user: decodeURIComponent(url.username || "prismedia"),
  };
}

export function commandExists(command) {
  return spawnSync(command, ["--version"], { stdio: "ignore" }).status === 0;
}

export function toolMode(args, requiredCommand) {
  if (args.docker) return "docker";
  if (args.local) return "local";
  return commandExists(requiredCommand) ? "local" : "docker";
}

export function dockerComposeArgs(args, service, command) {
  return [
    "compose",
    "-f",
    String(args["compose-file"] ?? DEFAULT_COMPOSE_FILE),
    "exec",
    "-T",
    service,
    ...command,
  ];
}

export function envForLocalPostgres(databaseUrl) {
  const url = new URL(databaseUrl);
  return {
    ...process.env,
    PGDATABASE: url.pathname.replace(/^\//, "") || "prismedia",
    PGHOST: url.hostname || "localhost",
    PGPASSWORD: decodeURIComponent(url.password || ""),
    PGPORT: url.port || "5432",
    PGUSER: decodeURIComponent(url.username || "prismedia"),
  };
}

export function describeCommand(file, args) {
  return [file, ...args].map((part) => (String(part).includes(" ") ? JSON.stringify(part) : part)).join(" ");
}

export async function run(file, args, options = {}) {
  if (options.dryRun) {
    console.log(describeCommand(file, args));
    return;
  }

  await new Promise((resolvePromise, reject) => {
    const stdio = options.stdio ?? (options.stdinText != null || options.stdinFile ? ["pipe", "inherit", "inherit"] : "inherit");
    const child = spawn(file, args, {
      env: options.env ?? process.env,
      stdio,
    });

    if (options.stdinText != null) {
      child.stdin.end(options.stdinText);
    } else if (options.stdinFile) {
      createReadStream(options.stdinFile).pipe(child.stdin);
    }

    child.on("error", reject);
    child.on("exit", (code) => {
      if (code === 0) {
        resolvePromise();
      } else {
        reject(new Error(`${file} exited with code ${code}`));
      }
    });
  });
}

export async function runToFile(file, args, outputPath, options = {}) {
  await mkdir(dirname(outputPath), { recursive: true });

  if (options.dryRun) {
    console.log(`${describeCommand(file, args)} > ${outputPath}`);
    return;
  }

  await new Promise((resolvePromise, reject) => {
    const out = createWriteStream(outputPath);
    const child = spawn(file, args, {
      env: options.env ?? process.env,
      stdio: ["ignore", "pipe", "inherit"],
    });

    child.stdout.pipe(out);
    child.on("error", reject);
    out.on("error", reject);
    child.on("exit", (code) => {
      out.end();
      if (code === 0) {
        resolvePromise();
      } else {
        reject(new Error(`${file} exited with code ${code}`));
      }
    });
  });
}

export function absolutePath(path) {
  return resolve(process.cwd(), path);
}
