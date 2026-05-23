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
- Startup applies pending migrations through the shared .NET runtime used by the
  API and worker.

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
