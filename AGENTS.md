# Prismedia Repository Contract

## Commit & Changelog Policy

- Commit after every logical set of changes.
- Keep commits small, reviewable, and intentionally scoped.
- With every release-note-worthy change, update `CHANGELOG.md` under `## [Unreleased]`.
- Keep changelog entries user-facing and high level. Internal cleanup belongs in git history, not the changelog.
- Use Keep a Changelog sections: `What's New`, `Added`, `Changed`, `Fixed`, `Removed`, and `Docs`.
- The root `package.json` version is the build version and the single source of truth. All workspace package versions must match it exactly.
- Do not change the build version while publishing a channel image. Move the version forward when a new body of work starts, then let dev, alpha, beta, and release images publish that decided version.
- Suggested commit style:
  - `chore: bootstrap workspace`
  - `docs: define repo contract`
  - `feat(web): add media library shell`
  - `feat(api): add health and jobs routes`
  - `fix(worker): stabilize queue startup`

## Versioning

- Prismedia starts at `1.0.0`.
- Versions are plain `X.Y.Z`; do not use development suffixes.
- MAJOR: breaking API changes, schema changes that require user action, or config format changes.
- MINOR: new features, new API endpoints, or new UI views.
- PATCH: bug fixes, UI tweaks, dependency updates, and documentation.
- Docker builds run `pnpm release:check`, which verifies changelog structure and workspace package version alignment.

## Release Channels

- Every push to `main` builds only `ghcr.io/pauljoda/prismedia:dev`.
- Validation, documentation-site publishing, and channel image publishing are manual workflows.
- Manual channel publishing is handled by `.github/workflows/publish-channel.yml`.
- The manual workflow accepts `alpha`, `beta`, or `release`.
- `alpha` publishes `alpha`, `alpha-<version>`, and `alpha-<version>-<short-sha>`.
- `beta` publishes `beta`, `beta-<version>`, and `beta-<version>-<short-sha>`.
- `release` publishes `release`, `release-<version>`, `release-<version>-<short-sha>`, and `latest`.
- Publishing a channel image never edits package versions, rewrites changelog headings, creates git tags, or commits release artifacts.

## Product

Prismedia is a private media library for self-hosted collections. It is video-first, but images, galleries, books, comics, audio, performers, studios, tags, and collections are first-class library entities. It is optimized for a single trusted user or household on a private LAN and ships as a Docker image.

Stash compatibility exists for plugin and metadata workflows. Prismedia should not be framed as a replacement for another app, and third-party schemas should not define Prismedia's persistence model.

## Project Structure

```text
apps/web-svelte/       Svelte frontend only. Built as static assets and served by the .NET API.
apps/backend/          .NET API, application/domain/infrastructure layers, EF Core persistence, and .NET worker.

packages/contracts/    Frontend TypeScript constants, media helpers, and plugin protocol types.
packages/media-core/   File discovery, fingerprint, and scan primitives.
packages/plugins/      Plugin runtime helpers and contracts.
packages/stash-compat/ Stash-compatible scraper and StashBox protocol helpers.
packages/ui-svelte/    Shared Svelte design tokens and UI primitives.

infra/docker/          Dockerfiles and dev compose stack.
scripts/release/       Version and changelog validation tooling.
docs/                  Architecture and design language docs.
```

## Architecture

- Monorepo with `pnpm` workspaces and `turbo`.
- Runtime processes: .NET API/HTTP ingress, static Svelte frontend assets, and the .NET worker.
- PostgreSQL 16 is the sole stateful dependency for application data and queue/job state.
- Public HTTP contracts live in the .NET backend and are consumed by the generated Svelte client under `apps/web-svelte/src/lib/api/generated`.
- Backend work must follow `docs/backend-architecture-contract.md`: Clean Architecture, DDD-lite domain behavior, CQRS-lite use cases, EF Core as infrastructure persistence, DTO API boundaries, and generated frontend clients.

## Key Decisions

1. .NET API + static Svelte UI: the .NET API owns endpoints, persistence, and server orchestration. Svelte is a frontend client only.
2. PostgreSQL + EF Core: typed schema and EF migrations are managed from `apps/backend/src/Prismedia.Infrastructure/Persistence`.
3. .NET background worker: scan, probe, thumbnail, sprite, HLS, and metadata work runs in `apps/backend/src/Prismedia.Worker`.
4. HLS streaming: videos are transcoded to HLS on demand via ffmpeg and served by the .NET API.
5. Typed contracts: .NET contracts are the server source of truth. The frontend should prefer generated OpenAPI types.
6. No global EntityGraph: relationship links are EF persistence structures for bounded domain slices and read projections.

## Database

- Schema is defined by EF Core entity mappings in `apps/backend/src/Prismedia.Infrastructure/Persistence`.
- Core entities: media entities, performers, studios, tags, fingerprints, library roots, settings, sources, files, and job runs.
- EF migration files live under `apps/backend/src/Prismedia.Infrastructure/Persistence/Migrations` and are applied by the .NET runtime on startup.
- Adding a schema change:
  1. Edit the EF Core entity/model mapping.
  2. Generate an EF migration from `apps/backend`.
  3. Open the migration and review it before committing.
  4. Commit the migration, model snapshot, entity/mapping changes, tests, and changelog entry together.
  5. Apply locally by restarting the .NET API or worker and verify.
- Never introduce Drizzle, SvelteKit `/api` routes, or a TypeScript worker.
- Do not add broad compatibility bridges for abandoned schemas. If a breaking schema change needs user consent, add one explicit product flow for that exact change and remove it when it is no longer needed.

## Design System Rules

- Follow the `Prism Noir Luxe` visual direction in `docs/design-language.md`.
- Controlled radii from a unified scale (`radius-xs: 4px` through `radius-2xl: 24px`). Tight, subtle softening — never bubbly or pill-shaped containers.
- Material base plus glass overlay: solid dark surfaces as the ground layer; glass for floating and interactive elements.
- Brass accent (`#f2c26a` / `#d59a2a`) is for active and selected states and should glow rather than appear as flat color.
- Mobile first. Desktop expands the mobile layout.
- Font voices: Cinzel for display/brand, Geist for product headings, Inter for body, JetBrains Mono for utility and metadata.
- Glow and animation express state. Do not rely on color-only state changes.
- Avoid generic SaaS styling and unmodified shadcn defaults.
- Core actions must not depend on hover-only affordances.

## Data & Integration Rules

- Keep provider integrations behind stable adapter interfaces.
- Do not embed a third-party application schema as Prismedia's application schema.
- Normalize external hashes and metadata into Prismedia-owned tables and contracts.
- Plugin development discovery should include `~/Dev/Prismedia-Plugins` when it exists.

## Quality Bar

- TypeScript is required in the Svelte frontend and TypeScript packages.
- C# is required for all server, persistence, and worker logic.
- Prefer typed contracts over ad hoc object shapes.
- When making layout, interaction, or styling changes, first look for the base component that owns the pattern and prefer changing that component unless the behavior is truly route-specific.
- Public classes, records, interfaces, and non-trivial public methods should have documentation comments that explain domain meaning, parameters, return values, and important behavior.
- Add tests with new logic when behavior can regress.
- Keep app boundaries explicit: UI in `apps/web-svelte`, HTTP/persistence/worker logic in `apps/backend`, and frontend-only shared utilities in `packages/*`.

## Docker

- Development: `docker compose -f infra/docker/docker-compose.yml up` runs Vite, the .NET API, the .NET worker, and PostgreSQL with hot reload.
- Production: single unified image (`ghcr.io/pauljoda/prismedia`) bundles PostgreSQL, ffmpeg, the built Svelte frontend, the .NET API, and the .NET worker.
- The .NET API listens on port 8008, serves same-origin `/api/*` routes, and serves the built Svelte assets.
- Always open and test Prismedia through the .NET app at `http://localhost:8008`; do not browse Vite directly because Vite is only the frontend dev server and does not provide the running app surface by itself.
- Volumes: `/data` for database/cache/thumbnails and `/media` for the user's media library.

## Tooling Expectations

- Local dev stack restarts must reproduce the canonical workflow from the shell.
- When the running stack needs to be refreshed, first tell the user the dev stack is being rebooted, run `pnpm dev:kill`, then run:
  - `docker compose -f infra/docker/docker-compose.yml up -d postgres`
  - `pnpm --filter @prismedia/web-svelte dev`
  - `dotnet build apps/backend/Prismedia.slnx`
  - `dotnet run --project apps/backend/src/Prismedia.Api/Prismedia.Api.csproj`
  - `dotnet run --project apps/backend/src/Prismedia.Worker/Prismedia.Worker.csproj`
- Keep Vite, API, and Worker in long-running shell sessions when handing off a refreshed stack.
- Frontend-only Svelte changes can usually be tested through Vite/HMR. Backend, worker, database, EF migration, API contract, Docker/runtime, or other non-HMR changes require a full-stack restart before review.
- Avoid destructive git commands unless explicitly requested.
- Keep the repo runnable via Docker Compose.
- Prefer lightweight validation commands before committing.
