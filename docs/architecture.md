# Architecture

## Runtime Topology

Prismedia is organized as a Docker-first monorepo with two primary runtime services:

- `backend` - .NET API, HTTP ingress, EF Core persistence, static frontend host
- `backend-worker` - .NET background process for scan, fingerprint, preview, and import jobs

Supporting services:

- `postgres` - application database and durable job state. No separate queue service is required.

## Responsibility Boundaries

The backend architecture contract lives in [backend-architecture-contract.md](backend-architecture-contract.md). New backend work should follow that document: Clean Architecture, DDD-lite domain objects, CQRS-lite use cases, EF Core as the infrastructure persistence adapter, DTO-based API boundaries, and OpenAPI-generated frontend clients.

### apps/web-svelte

- user interface
- responsive layout and navigation
- asset browsing, metadata workflows, settings surfaces
- static frontend build consumed by the .NET API host

### apps/backend

- same-origin `/api` transport layer
- request validation and route composition
- EF Core persistence and migrations
- local streaming endpoints
- heavy media work
- long-running or restart-safe tasks
- queue execution, retries, and progress reporting

### packages/contracts

- frontend-only constants, media helpers, and plugin protocol types
- server contracts live in `apps/backend/src/Prismedia.Contracts`

### packages/ui-svelte

- design tokens
- shared component helpers
- visual language primitives for the Svelte app

## Domain Direction

The application schema is intentionally not a direct copy of Stash and should not become a custom global entity graph.

Core library concepts:

- videos, series, seasons, and episodes
- images and galleries
- books, volumes, chapters, and pages
- audio libraries and tracks
- people, studios, tags, and collections
- fingerprints and provider source matches
- job runs and library roots

Key rules:

- Domain entities express library behavior and invariants.
- Physical files should remain modelable independently from canonical asset identity.
- EF Core persists entity records, capability/detail rows, child links, relationship links, media files, playback state, settings, and jobs.
- Child and relationship links are persistence structures, not a global `EntityGraph` runtime.
- Imported stash data is normalized into Prismedia-owned records.
- Provider provenance must be persisted for auditability and future provider expansion.

## Queue Direction

Initial queue families:

- `library-scan`
- `media-probe`
- `fingerprint`
- `preview`
- `metadata-import`

Queues must be durable, restart-safe, and visible in the UI.
