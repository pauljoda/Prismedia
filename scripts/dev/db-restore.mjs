#!/usr/bin/env node
import { access } from "node:fs/promises";
import {
  DEFAULT_COMPOSE_FILE,
  DEFAULT_DATABASE_URL,
  DEFAULT_POSTGRES_SERVICE,
  absolutePath,
  databaseParts,
  dockerComposeArgs,
  envForLocalPostgres,
  parseArgs,
  run,
  toolMode,
} from "./db-snapshot-utils.mjs";

const args = parseArgs(process.argv.slice(2));
const backupArg = args.file ?? args._[0];
const dryRun = Boolean(args["dry-run"]);

if (args.help || !backupArg) {
  console.log(`Usage: pnpm dev:db:restore --file=path --yes [--docker|--local] [--dry-run]

Restores a local custom-format PostgreSQL dump for development.
This drops all non-system schemas before restoring.

Defaults:
  DATABASE_URL=${DEFAULT_DATABASE_URL}
  --compose-file=${DEFAULT_COMPOSE_FILE}
  --service=${DEFAULT_POSTGRES_SERVICE}`);
  process.exit(args.help ? 0 : 1);
}

if (!args.yes) {
  throw new Error("Refusing to restore without --yes. This replaces the local Prismedia database contents.");
}

const backupPath = absolutePath(String(backupArg));
if (!dryRun) {
  await access(backupPath);
}

const databaseUrl = process.env.DATABASE_URL ?? DEFAULT_DATABASE_URL;
const { database, user } = databaseParts(databaseUrl);
const mode = toolMode(args, "pg_restore");
const resetSql = `
DO $$
DECLARE schema_name text;
BEGIN
  FOR schema_name IN
    SELECT nspname
    FROM pg_namespace
    WHERE nspname <> 'information_schema'
      AND nspname NOT LIKE 'pg_%'
  LOOP
    EXECUTE format('DROP SCHEMA IF EXISTS %I CASCADE', schema_name);
  END LOOP;
END $$;

CREATE SCHEMA public;
`;

if (mode === "local") {
  const env = envForLocalPostgres(databaseUrl);
  await run("psql", ["--set", "ON_ERROR_STOP=1", "--command", resetSql], { dryRun, env });
  await run("pg_restore", ["--no-owner", "--dbname", databaseUrl, backupPath], { dryRun, env });
} else {
  const service = String(args.service ?? DEFAULT_POSTGRES_SERVICE);
  await run(
    "docker",
    dockerComposeArgs(args, service, [
      "psql",
      "-U",
      user,
      "-d",
      database,
      "--set",
      "ON_ERROR_STOP=1",
      "--command",
      resetSql,
    ]),
    { dryRun },
  );
  await run(
    "docker",
    dockerComposeArgs(args, service, [
      "pg_restore",
      "--no-owner",
      "-U",
      user,
      "-d",
      database,
    ]),
    { dryRun, stdinFile: backupPath },
  );
}

console.log(`${dryRun ? "Planned local database restore" : "Restored local database"}: ${backupPath}`);
