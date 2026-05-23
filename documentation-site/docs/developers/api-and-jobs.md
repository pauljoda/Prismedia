---
sidebar_position: 4
title: API & Jobs
description: HTTP route conventions and background work ownership.
---

# API & Jobs

The .NET backend owns all server behavior.

## HTTP API

API routes live in `apps/backend/src/Prismedia.Api/Endpoints` and are exposed
under `/api/*` or Jellyfin-compatible playback roots such as `/Videos` and
`/Items`.

The Svelte frontend should call the API through:

- generated OpenAPI clients in `apps/web-svelte/src/lib/api/generated`
- hand-written wrappers in `apps/web-svelte/src/lib/api/prismedia.ts` only when
  generation has not caught up yet

Do not add `apps/web-svelte/src/routes/api` handlers.

## Jobs

Long-running media work belongs in `apps/backend/src/Prismedia.Worker` and shared
.NET application/infrastructure services. Job state is represented in the
database through Prismedia-owned tables such as `job_runs`.

Do not add a TypeScript worker, pg-boss wrappers, or `@prismedia/app-core` queue
helpers.
