---
sidebar_position: 7
title: Contributing
description: Local setup, the commit + changelog policy, and how channel images are published.
---

# Contributing

This page covers what to know before opening a PR: how to run things locally, what commits look like, and how channel images are published.

## Local development

### Prerequisites

- **Node.js 22**.
- **pnpm** 10+ (`corepack enable` is the easiest path).
- **.NET 10 SDK** for the API and worker.
- **Docker** for the Postgres container.
- **ffmpeg** and **audiowaveform** on `PATH` if you want to exercise media work outside Docker. The unified Docker image bundles these so most work happens in the container.

### Two ways to run

**The container way** (recommended for most changes):

```bash
docker compose -f infra/docker/docker-compose.yml up
```

This brings up Postgres, Vite, the .NET API, and the .NET worker with hot reload. Web is at http://localhost:8008.

**The local way** (when you're iterating fast on the Svelte app):

```bash
# 1. Start Postgres (or any Postgres you control)
docker run --rm -d --name prismedia-pg \
  -e POSTGRES_USER=prismedia -e POSTGRES_PASSWORD=prismedia -e POSTGRES_DB=prismedia \
  -p 5432:5432 postgres:16-alpine

# 2. Set the env
export DATABASE_URL=postgres://prismedia:prismedia@localhost:5432/prismedia

# 3. Run the full dev stack from VS Code or the root scripts
pnpm dev
```

Web is at http://localhost:8008. The .NET API and worker own migrations and server work.

### Useful filters

```bash
pnpm --filter @prismedia/web-svelte dev    # web only
pnpm docs:dev                            # this site
```

## The commit policy

The repository contract in `AGENTS.md` is the source of truth; the highlights:

- **Commit after every set of changes.** Don't batch; small reviewable commits are the norm.
- **Update `CHANGELOG.md` with every commit** under `## [Unreleased]`, grouped by Keep a Changelog sections (`Added` / `Changed` / `Fixed` / `Removed` / `Docs`).
- **Don't bump `package.json` versions as part of channel publishing.** Move the version forward when a new body of work starts.

Suggested commit style:

```text
chore: bootstrap workspace
docs: define repo contract
feat(web): add media library shell
feat(api): add health and jobs routes
fix(worker): stabilize queue startup
```

Use release-related commit scopes only when the change is actually about release tooling or documentation.

### Changelog rules

Every release section (including `## [Unreleased]`) **must** start with a `### What's New` block aimed at users — non-technical, 1–2 sentences per bullet, only user-visible changes (features, major fixes, behavioral changes the user would notice).

The standard sections (`### Added` / `### Changed` / `### Fixed` / `### Removed` / `### Docs`) follow with the detailed dev-facing entries. If your change has user-visible impact, add a What's New bullet **and** a detailed entry. Internal refactors only get the detailed entry.

Entries should be written for users of the app:

- **Good:** "Subtitles now auto-enable when a preferred-language track is found on play."
- **Bad:** "Refactor `subtitleAutoEnable()` to read settings via the new resolver helper."

## Versioning

Prismedia follows [SemVer](https://semver.org/) and [Keep a Changelog](https://keepachangelog.com/).

| Bump | Means |
| --- | --- |
| **MAJOR** | Breaking API, schema needs manual migration, config format changed. |
| **MINOR** | New features, new endpoints, new UI views. |
| **PATCH** | Bug fixes, UI tweaks, dep updates, docs. |

The root `package.json` carries the decided build version, starting at `1.0.0`. All workspace packages must match it exactly.

Versions are plain `X.Y.Z`. Do not use development suffixes.

The Dockerfile runs `pnpm release:check` on every build. That check verifies changelog structure and workspace package version alignment.

## How channel publishing works

Image channels are published by GitHub Actions. Publishing a channel never edits package versions, rewrites changelog headings, creates git tags, or commits release artifacts.

1. Make sure `main` is green and `## [Unreleased]` has the entries users need.
2. Open the repo on GitHub -> **Actions** -> **Publish Channel Image** -> **Run workflow**.
3. Pick `alpha`, `beta`, or `release`.
4. The workflow builds the unified Docker image from the selected commit.
5. `alpha` publishes `alpha`, `alpha-<version>`, and `alpha-<version>-<short-sha>`.
6. `beta` publishes `beta`, `beta-<version>`, and `beta-<version>-<short-sha>`.
7. `release` publishes `release`, `release-<version>`, `release-<version>-<short-sha>`, and `latest`.

When starting the next body of work, update the root and workspace package versions in a normal commit so future channel images carry the new build number.

## CI/CD

Two automation surfaces, both in `.github/workflows/`:

- **`publish-dev.yml`** — runs on every push to `main` and publishes:
  - `dev` — the latest `main` commit
  - `sha-<short>` — pinned per commit
  - `<version>-<short>` — e.g. `1.0.0-abc1234`
- **`publish-channel.yml`** — manual `workflow_dispatch` only. Publishes `alpha`, `beta`, or `release`; release also updates `latest`.
- **`validate.yml`** — runs lint, typecheck, unit tests, integration tests on PR + push to `main` + nightly. Optional smoke e2e.
- **`documentation-site.yml`** — builds and deploys this site to GitHub Pages.

`latest` resolves to the most recently promoted release image. `dev` is bleeding edge with rollback safety via `sha-` tags.

## Style and quality

- **TypeScript in the frontend/packages and C# in the server.** No JS in new code.
- **Prefer generated .NET OpenAPI contracts** over ad-hoc frontend object shapes.
- **Add tests with new logic** when behavior can regress.
- **Keep app boundaries explicit:** UI in `apps/web-svelte`, server/persistence/worker work in `apps/backend`.
- **Don't introduce abstractions beyond what the task requires.** A bug fix doesn't need surrounding cleanup. Three similar lines is better than a premature abstraction.
- **Don't add error handling for scenarios that can't happen.** Trust internal code; only validate at system boundaries.

## Git hygiene

- **Avoid destructive git commands** unless explicitly necessary.
- **Don't skip hooks** with `--no-verify`.
- **Don't amend published commits.** Add a follow-up commit instead.
- **Open a PR** for non-trivial work; let CI run before merging.

## Reading order

If you're new and want to make a small change, this order tends to work:

1. Run it locally with Docker compose. Click around. Open dev tools.
2. Read [Architecture](./architecture.md) and [Monorepo Layout](./monorepo.md).
3. Find a .NET endpoint/service or Svelte page that does something close to your task.
4. Trace the call chain: endpoint → application/infrastructure service → EF Core model.
5. Make the change. Add a test. Update CHANGELOG. Commit.
