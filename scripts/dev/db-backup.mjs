#!/usr/bin/env node
import {
  DEFAULT_BACKUP_DIR,
  DEFAULT_DATABASE_URL,
  DEFAULT_POSTGRES_SERVICE,
  absolutePath,
  databaseParts,
  dockerComposeArgs,
  envForLocalPostgres,
  parseArgs,
  run,
  runToFile,
  timestamp,
  toolMode,
} from "./db-snapshot-utils.mjs";

const args = parseArgs(process.argv.slice(2));
const databaseUrl = process.env.DATABASE_URL ?? DEFAULT_DATABASE_URL;
const backupPath = absolutePath(
  String(args.file ?? `${args["out-dir"] ?? DEFAULT_BACKUP_DIR}/prismedia-dev-${timestamp()}.dump`),
);
const dryRun = Boolean(args["dry-run"]);
const mode = toolMode(args, "pg_dump");
const { database, user } = databaseParts(databaseUrl);

if (args.help) {
  console.log(`Usage: pnpm dev:db:backup [--file=path] [--out-dir=path] [--docker|--local] [--dry-run]

Creates a custom-format PostgreSQL dump for local development.
Default output: ${DEFAULT_BACKUP_DIR}/prismedia-dev-<timestamp>.dump
Default DB: ${DEFAULT_DATABASE_URL}`);
  process.exit(0);
}

if (mode === "local") {
  const command = [
    "--format=custom",
    "--no-owner",
    "--file",
    backupPath,
  ];
  await run("pg_dump", command, {
    dryRun,
    env: envForLocalPostgres(databaseUrl),
  });
} else {
  const service = String(args.service ?? DEFAULT_POSTGRES_SERVICE);
  const command = dockerComposeArgs(args, service, [
    "pg_dump",
    "--format=custom",
    "--no-owner",
    "-U",
    user,
    "-d",
    database,
  ]);
  await runToFile("docker", command, backupPath, { dryRun });
}

console.log(`${dryRun ? "Planned" : "Created"} local database backup: ${backupPath}`);
