---
sidebar_position: 3
title: Database
description: Schema overview and EF Core migration workflow.
---

# Database

Prismedia stores application state in PostgreSQL 16 and manages schema changes
through EF Core migrations in the .NET backend.

## Source of Truth

- Entity mappings live under `apps/backend/src/Prismedia.Infrastructure/Persistence`.
- Migrations live under `apps/backend/src/Prismedia.Infrastructure/Persistence/Migrations`.
- The API is the single migration owner. The worker waits until the API has
  applied every known migration before it starts processing jobs.

Do not add Drizzle, `@prismedia/db`, SvelteKit database code, or TypeScript
database migrations.

## Adding a Schema Change

1. Update the EF Core entity and mapping.
2. Generate an EF Core migration from the backend project.
3. Read the generated migration before committing it.
4. Add tests for behavior that can regress.
5. Commit entity/mapping changes, migration files, tests, and changelog entry
   together.

The .NET backend is the only owner of persistence. The Svelte frontend should
consume data through `/api/*` contracts, preferably via the generated OpenAPI
client under `apps/web-svelte/src/lib/api/generated`.

## Migration History and Baselines

Applied migrations are recorded in PostgreSQL, so ordinary startup checks and
runs only pending migrations. A long history is therefore not repeated work on
every boot, and checked-in migrations must not be removed merely to shorten the
folder: older installs may still need their data transformations.

A new-install baseline is a deliberate major-version operation, not routine
cleanup. Prismedia may replace the legacy chain only after a bridge release has
established one immutable legacy head. Existing databases are eligible for a
history rebase only when both of these match:

- the complete expected legacy migration history, with no missing or unknown ids;
- a canonical fingerprint of every managed table, column, key, constraint, and index.

The package version or latest migration id alone is not proof that a database is
safe to rebase. A mismatch must stop without mutating schema or history. The
baseline transition requires a verified backup, an exclusive migration lock, a
transactional history replacement, and fresh-install versus upgraded-schema
parity tests. Installations that did not pass through the bridge must use the
retained legacy migration assembly or upgrade through the bridge first.
