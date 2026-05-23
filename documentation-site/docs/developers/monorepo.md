---
sidebar_position: 2
title: Monorepo Layout
description: Every workspace, what it does, and the dependency graph.
---

# Monorepo Layout

The repo is a pnpm + turbo monorepo plus a .NET backend solution.

## Apps

| Path | Purpose |
| --- | --- |
| `apps/backend` | .NET API, contracts, domain/application/infrastructure layers, EF Core persistence, migrations, and .NET worker. |
| `apps/web-svelte` | Svelte frontend. Builds static assets and calls the .NET API. |
| `documentation-site` | Docusaurus documentation site. |

## Packages

| Package | What lives there |
| --- | --- |
| `@prismedia/contracts` | Frontend compatibility constants and helper types while surfaces migrate fully to generated .NET OpenAPI models. |
| `@prismedia/media-core` | Frontend/shared media helpers that do not own server behavior. |
| `@prismedia/plugins` | Plugin manifest parsing, runtime helper contracts, and result normalization helpers. |
| `@prismedia/stash-compat` | Stash-compat YAML scraper adapter and StashBox GraphQL client. |
| `@prismedia/ui-svelte` | Design tokens and reusable Svelte primitives. |

## Rules

- New HTTP endpoints go in `apps/backend`.
- New database tables or columns go through EF Core migrations in `apps/backend`.
- New background jobs go in the .NET worker.
- New frontend pages go in `apps/web-svelte/src/routes`.
- Do not add SvelteKit `/api` routes.
- Do not reintroduce `@prismedia/app-core`, `@prismedia/db`, Drizzle, or a TypeScript worker.
