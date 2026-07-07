---
sidebar_position: 1
title: Architecture
description: The big picture — runtime processes, package boundaries, data flow.
---

# Architecture

Prismedia is intentionally small: a .NET API, a .NET worker, a static Svelte
frontend, and one PostgreSQL database.

## Runtime Topology

```text
Browser / LAN
    │ HTTP :8008
    ▼
.NET API
    ├─ serves /api/*
    ├─ serves built Svelte assets
    ├─ streams files and HLS assets
    └─ applies EF Core migrations
    │
    ├──────────────► PostgreSQL 16
    │                 app schema + job_runs
    │
    └──────────────► .NET worker
                      scan / probe / preview / HLS / import
```

The Svelte app is frontend-only. It calls the .NET API through generated
OpenAPI clients and small hand-written wrappers where generation has not
caught up yet.

## Packages

| Package | Responsibility |
| --- | --- |
| `apps/backend` | .NET API, domain/application/infrastructure layers, EF Core persistence, migrations, and worker. |
| `apps/web-svelte` | Static Svelte frontend and browser-side interaction. |
| `packages/ui-svelte` | Shared Svelte design primitives. |
| `packages/contracts` | Frontend-only constants, media helpers, and plugin protocol types; .NET contracts are the server source of truth. |

## Rules

- Do not add SvelteKit `/api` routes.
- Do not reintroduce Drizzle or `@prismedia/db`.
- Do not reintroduce `@prismedia/app-core` as a server package.
- Do not add a TypeScript worker.
- Server contracts belong in `apps/backend/src/Prismedia.Contracts`.
- Frontend clients should prefer generated OpenAPI models under `apps/web-svelte/src/lib/api/generated`.
