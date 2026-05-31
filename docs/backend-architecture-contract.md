# Backend Architecture Contract

This contract adapts "Clean .NET Domain + EF Core Infrastructure Guidelines" version 1.0, prepared May 18, 2026, for Prismedia. It is the backend direction for new work unless a later architecture decision explicitly replaces it.

## Baseline

Prismedia backend work follows:

```text
Clean Architecture
+ DDD-lite
+ CQRS-lite
+ EF Core persistence
+ DTO-based API boundaries
+ OpenAPI-generated frontend clients
```

The dependency direction is inward:

```text
Prismedia.Api --------+
Prismedia.Worker ----+
Prismedia.Infrastructure
                    v
             Prismedia.Application
                    v
               Prismedia.Domain
```

The domain model owns business truth. Application handlers own use cases. Infrastructure owns persistence and integrations. API owns HTTP contracts. The Svelte frontend owns presentation and talks to generated API clients.

Do not collapse database rows, API payloads, frontend view models, and domain objects into one shared shape. They change for different reasons.

## Layer Rules

### Domain

`Prismedia.Domain` contains entities, aggregate roots, value objects, domain services, domain events, business exceptions, and invariants. Domain objects should expose intent-bearing methods such as `Rename`, `Rate`, `Archive`, `AddChild`, or `MarkWatched` rather than requiring callers to mutate unrelated public properties in the right order.

The domain layer must not reference EF Core, SQL, HTTP, JSON serialization, API DTOs, generated frontend types, logging implementations, database transactions, or provider clients.

### Application

`Prismedia.Application` contains use cases and ports. A meaningful state-changing operation should become a command or handler with a business name, such as `ApplyEntityMetadata`, `StartLibraryScan`, `RefreshCollection`, or `UpdatePlaybackProgress`.

Handlers coordinate validation, authorization, aggregate loading, domain method calls, persistence, domain events, and application results. They should not contain EF mapping, HTTP status codes, SQL-specific logic, UI formatting, or provider implementation details.

### Infrastructure

`Prismedia.Infrastructure` implements technical details: EF Core `DbContext`, entity type configuration, repository implementations, migrations, file/media tooling, provider clients, queue storage, clocks, and other external adapters.

Infrastructure is where domain objects become database rows and where read-heavy API queries may project EF rows directly to response DTOs.

### API

`Prismedia.Api` owns HTTP: endpoint composition, request DTOs, response DTOs, status mapping, authentication/authorization wiring, OpenAPI metadata, and exception mapping. Endpoints should be thin and should not return domain entities directly.

Commands should flow through application handlers. Read-only endpoints may use EF projections when they are purely query-shaped and do not need domain behavior.

### Worker

`Prismedia.Worker` should call the same application handlers and infrastructure services as the API. One job execution should get one dependency-injection scope and one short-lived EF context/unit of work.

## EF Core Rules

- EF Core is the persistence adapter, not the domain model.
- Fluent EF configuration lives in `apps/backend/src/Prismedia.Infrastructure/Persistence`.
- Migrations live in `apps/backend/src/Prismedia.Infrastructure/Persistence/Migrations`.
- `DbContext` is the unit of work. Save once per use case whenever possible.
- Keep `DbContext` instances short-lived through DI scopes.
- Use `AsNoTracking` for read-only queries.
- Use explicit configuration for table names, keys, indexes, value conversions, relationships, delete behavior, and concurrency where needed.
- Review every generated migration before committing it.

## CQRS-Lite

Writes:

```text
API request DTO -> Command -> Application handler -> Domain aggregate -> EF SaveChanges
```

Reads:

```text
API query -> EF projection -> Response DTO -> generated TypeScript client
```

This does not require separate databases, event sourcing, or a message bus. It means write models and read models are allowed to be different because they serve different jobs.

## Repositories

Use repositories only for aggregate roots or meaningful domain slices. Do not create one repository per table. Repository methods should describe intent, for example `GetForUpdateAsync`, `FindActiveAsync`, `Add`, or `SaveAsync`.

Repositories are for loading and saving write models. Read-heavy screens can query EF directly from infrastructure-backed query services or endpoint-specific projections, returning DTOs rather than domain entities.

## API and DTO Rules

- Every endpoint uses explicit request and response contracts from `Prismedia.Contracts`.
- Do not return EF row types or domain entities from HTTP endpoints.
- DTOs are shaped for the client and may flatten, group, omit, or rename data.
- Generated OpenAPI TypeScript types are the frontend source of truth.
- `@prismedia/contracts` owns frontend-only helpers and plugin protocol types; it must not shadow API DTOs that are available through generated OpenAPI clients.

## Frontend Rules

The Svelte app must not model backend domain entities as mutable frontend business objects. It should use generated API DTOs plus route/component-local view models for presentation.

Frontend writes send explicit commands through API clients. Frontend reads consume shaped response DTOs.

## Anti-Patterns

Avoid:

- A custom global `EntityGraph` that loads and saves the whole application object network.
- Domain entities with public setters for every persisted field and no behavior.
- Returning EF rows or domain entities from API endpoints.
- Repository-per-table abstractions.
- Business rules in endpoints, Svelte components, EF configurations, or migrations.
- Blind object mapping from client DTOs into domain entities.
- Multiple `SaveChanges` calls inside one application use case unless the operation has a documented transactional boundary.

## Prismedia Entity Direction

The current entity work should be steered away from a global graph abstraction.

The acceptable shape is:

- Domain entities represent library concepts and behavior.
- EF rows persist entity records, child links, relationship links, capabilities, media files, playback state, settings, jobs, and provider data.
- `EntityChildLinkRow` and `EntityRelationshipLinkRow` are persistence tables, not an application-wide object graph.
- `EfEntityRepository` may hydrate a bounded entity slice for a write use case, then save that slice with EF.
- Browse/detail endpoints should prefer EF projections to DTOs instead of hydrating broad domain graphs.
- Public plugin protocol names should use structural-context terminology, not graph terminology.

## Capability Model

Capabilities are optional behavior/state modules attached to a domain `Entity`. Two
deliberate patterns exist and must not be "corrected" into uniformity:

- **Domain capabilities** are concrete `EntityCapability` subclasses in
  `Prismedia.Domain/Capabilities` (description, dates, stats, technical, playback,
  markers, subtitles, fingerprints, source, classification, credits, position,
  progress, lifetime). They carry their own state and persist via per-capability EF
  mappers. A capability may legitimately be used by only one kind today (for example
  markers/subtitles on `Video`, progress on `Book`); the capability is the persistence
  and contract projection unit, so single-kind capabilities are acceptable and expected
  to be reused as new kinds appear.
- **Contract pseudo-capabilities** (`RatingCapability`, `FlagsCapability`,
  `FilesCapability`, `LinksCapability`, `ImagesCapability` in `Prismedia.Contracts`) have
  **no domain counterpart**. They project universal `Entity` properties
  (`RatingValue`, the `IsFavorite/IsNsfw/IsOrganized` flags, `EntityFiles`, `Urls` +
  `ExternalIds`) into the API as capabilities so the client sees one uniform "capability"
  surface. The contract capability set is therefore a superset of the domain set. Do not
  add domain classes for these.

Subtitle extraction state (`CapabilitySubtitles.ExtractedAt`) lives on the subtitles
capability. Its value is persisted on the `video_details.subtitles_extracted_at` column
(owned by the Video kind mapper) rather than a dedicated capability table; the scan
pipeline still queries that column directly for extraction gating.

`CapabilityCredits` is a domain capability but is **not** emitted as a contract
capability. It persists as `EntityRelationshipLinkRow` rows with relationship code
`credits`/`cast`, storing only the target `Person` id plus the `CreditRole` (and optional
character) in link metadata — never a cloned `Person`. On detail routes it is projected as
a separate `EntityCreditMetadata` list, not as a capability DTO.

## Stable Codes and Constants

Stable string identifiers are owned by exactly one source and must not be retyped inline:

- Closed sets are `[Code]`-bearing enums in `Prismedia.Domain` (entity kinds,
  relationship kinds, file roles, file source, credit roles, job types, subtitle/playback
  enums, identify states). Encode/decode through `CodecRegistry`; in EF LINQ, hoist
  `.ToCode()` to a local first so the query translates.
- Open-ended keys are constants classes (`ExternalIdProviders`, `MediaContentTypes`,
  `JellyfinProtocol`, Api-side `JellyfinRoutes`, `AppSettingKeys`).
- The frontend never hand-maintains these. `GET /api/_codegen/codes.json` (Development
  only) publishes them and `pnpm api:generate` regenerates
  `apps/web-svelte/src/lib/api/generated/codes.ts`. Frontend modules re-export the
  generated constants and add only frontend-specific concerns (labels, routes).

## Current Audit

### Already Aligned

- `Prismedia.Domain` is persistence-ignorant and has behavior-bearing entity methods.
- `Prismedia.Infrastructure.Entities.EfEntityRepository` hydrates bounded domain slices directly; there is no one-to-one Application repository interface.
- `PrismediaDbContext` is the EF persistence boundary and owns row sets, mappings, and migrations.
- API contracts live in `Prismedia.Contracts` rather than in domain classes.
- Child and relationship data is stored in explicit EF tables, which can support projections without inventing a separate graph runtime.

### Course Corrections

- Do not introduce a service named `EntityGraph` or a global load/save coordinator.
- Keep "graph" terminology out of new internal infrastructure names. Prefer "entity relationships", "structural children", "relationship links", or "entity slices".
- Do not make `EfEntityRepository.SaveAsync` the universal write path for every table. It should remain a bounded domain-slice persistence adapter. Media scanning, playback, queues, collections, and settings can use specific application handlers and EF persistence services.
- For read APIs, build DTO projections from EF with `AsNoTracking` instead of hydrating domain entities only to map them back to DTOs.
- Do not reintroduce plugin protocol fields or internal method names that say `Graph`; use `StructuralContext`, structural children, and relationship terminology.

## Definition of Done

Backend work is done when:

- Domain behavior lives in `Prismedia.Domain` and is covered by domain tests when it can regress.
- Use-case orchestration lives in `Prismedia.Application` or an intentional infrastructure adapter for technical workflows.
- EF mapping, migrations, and SQL live in `Prismedia.Infrastructure`.
- API endpoints expose DTOs from `Prismedia.Contracts`.
- Frontend code consumes generated API clients for HTTP contracts and keeps package-level helper types frontend-only.
- Tests cover domain rules, application orchestration, infrastructure persistence, or API behavior according to the risk of the change.
- The changelog includes a concise user-facing entry when the change is release-note-worthy.
