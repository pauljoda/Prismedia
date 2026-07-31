---
sidebar_position: 3
title: Entity Definitions and Data Flow
description: How Entity kinds, capabilities, EF rows, API documents, generated clients, and shared presentation fit together.
---

# Entity Definitions and Data Flow

This is the review map for Prismedia's Entity system. Use it when adding a kind,
adding a capability, tracing an incorrect field, or deciding whether code belongs
in the shared root, a kind definition, persistence, an API projection, or one of
the frontends.

The central rule is:

> A definition describes an Entity kind. An Entity carries one instance's behavior
> and state. EF rows persist that state. An Entity document projects it for clients.
> Frontend view models decide how to present it.

Those are related objects, but they are deliberately not one object shared across
every layer.

## One-Minute Mental Model

```mermaid
flowchart LR
  Definition["EntityKindDefinition<br/>stable kind facts, defaults, policy"]
  Entity["Domain Entity<br/>identity, behavior, instance state"]
  DomainCaps["Domain capabilities<br/>optional mutable modules"]
  Rows[("EF rows<br/>storage representation")]
  Mappers["Discovered kind and capability mappers"]
  Projection["EntityCardProjector<br/>shared + kind projection"]
  Document["EntityCard<br/>one root, sparse capability array"]
  Clients["Generated codes and client models"]
  UI["Svelte and Swift presentation"]

  Definition --> Entity
  Definition --> Projection
  Entity --> DomainCaps
  Rows --> Mappers --> Entity
  Entity --> Mappers --> Rows
  Entity --> Projection --> Document --> Clients --> UI
```

The definition is a singleton description of a **type**. The Entity is a runtime
object for one library item. A database row is its **storage form**, and
`EntityCard` is its **read contract**. Do not copy a property across all four just
because it exists in one of them.

## The Data Objects

| Object | Meaning | Canonical location |
| --- | --- | --- |
| `EntityKind` | Closed typed identity. It has no independently maintained string code. | `Prismedia.Domain/Entities/Kinds/EntityKind.cs` |
| `EntityKindDefinition` | Immutable, discovered description of one kind: code, names, storage shape, presentation, navigation, behavior, workflows, defaults, and kind projection. | Beside the concrete Entity under `Prismedia.Domain/Media` or `Prismedia.Domain/Taxonomy` |
| `Entity` | Shared domain root for one item: id, title, structural placement, universal state, attached capabilities, children, relationships, files, and intent-bearing mutation methods. | `Prismedia.Domain/Entities/Entity.cs` |
| Concrete Entity | State and behavior that genuinely belongs to one kind, such as `Book.Format` or `Collection.Mode`. | The same file as its definition where practical |
| Domain capability | Optional mutable behavior/state module, such as dates, playback, progress, credits, or technical metadata. | `Prismedia.Domain/Capabilities` |
| EF row | PostgreSQL storage shape. Rows are infrastructure details, not domain objects or API DTOs. | `Prismedia.Infrastructure/Persistence/Entities` |
| Kind mapper | Constructs and persists a concrete Entity when it has kind-specific stored data. | `Prismedia.Infrastructure/Entities/Mappers/Kinds` |
| Capability mapper | Hydrates, clears, and persists one domain capability's rows. | `Prismedia.Infrastructure/Entities/Mappers/Capabilities` |
| Document capability | Immutable, discriminated API value inside `EntityCard.capabilities`. It may project a domain capability, a universal root fact, or kind-specific Entity state. | Shared values in `Prismedia.Contracts/Entities/Capabilities`; kind-owned values in `Prismedia.Domain/Entities/Documents` |
| `EntityThumbnail` | Read-optimized list/grid projection. It is intentionally not a complete Entity document. | `Prismedia.Contracts/Entities` and `EfEntityReadService` |
| `EntityCard` | Concrete shared detail document implementing the `IEntityRef` → `IEntitySummary` → `IEntityDocument` contract ladder. | `Prismedia.Contracts/Entities/EntityCards.cs` |
| Frontend model | Generated wire type plus app-local presentation state. It is not a second backend domain model. | Svelte `src/lib/entities`; Swift `PrismediaShared/Domain/Entities` and feature presentation models |

Search candidates, identify proposals, acquisition releases, and request previews
are not Entities. They can describe a possible Entity or mutation without gaining a
fake Entity id, capability array, or persistence lifecycle.

## What One Kind Definition Owns

Every value that is stable for all instances of one kind should be considered for
its `EntityKindDefinition` before creating another switch, map, or registry.

```mermaid
flowchart TD
  Kind["One EntityKindDefinition"]
  Kind --> Identity["Identity<br/>enum value, stable code, labels, category"]
  Kind --> Storage["Storage<br/>file, folder, archive, or none"]
  Kind --> Presentation["Presentation<br/>icons, aspect ratio, accents, artwork fit"]
  Kind --> Navigation["Client policy<br/>browse route, detail template, search order"]
  Kind --> Behavior["Domain policy<br/>identify, browse, engagement, deletion, pruning"]
  Kind --> Acquisition["Workflow policy<br/>requests, upload, replacement, profiles, quality"]
  Kind --> Composition["Composition<br/>default capabilities, structural placement and counts"]
  Kind --> Construction["Construction<br/>shared-root factory when no detail data exists"]
  Kind --> Projection["Projection<br/>declared kind capability types + typed projector"]
```

For example, `BookEntityKindDefinition` owns the stable `book` code, labels,
archive storage shape, thumbnail presentation, routes, identify policy, default
progress/playback capabilities, structural counts, request descriptors, allowed
root/parent placement, acquisition profile, and the projection of `BookType`,
`Format`, and cover choice.
The `Book` object owns the actual values and reading behavior for one book.

Definitions use two construction forms:

- `RootEntityKindDefinition<TEntity>` is for a type whose complete construction
  needs only `EntityRow` fields. Its definition supplies the factory, and
  infrastructure creates a convention mapper automatically.
- `EntityKindDefinition<TEntity>` is for a type with additional stored state. A
  discovered `IEntityKindMapper` reads its detail row and invokes its constructor.

`AudioEntityKindDefinition` is the deliberate protocol-only exception: it has a
kind identity and shared policy but no concrete persisted Entity CLR type.

## Discovery and Fail-Fast Validation

There is no central hand-maintained list of Entity kinds. Discovery is dynamic
within the compiled assemblies; it is not runtime plugin loading from arbitrary
external assemblies.

```mermaid
flowchart TD
  Enum["EntityKind enum members"]
  Definitions["Concrete parameterless<br/>EntityKindDefinition classes"]
  Registry["EntityKindRegistry reflection discovery"]
  Validation["Duplicate, completeness, navigation,<br/>search, acquisition, visibility validation"]
  Indexes["Indexes by enum, code,<br/>Entity CLR type, definition type"]

  Definitions --> Registry
  Enum --> Validation
  Registry --> Validation --> Indexes

  Indexes --> KindCodec["EntityKindCodec"]
  KindCodec --> CodecRegistry["CodecRegistry<br/>same API as Code enums"]
  Indexes --> Requests["RequestKindRegistry<br/>flattens definition-owned descriptors"]
  Indexes --> RootFactories["Infrastructure DI<br/>builds convention kind mappers"]
  Indexes --> Manifest["CodesManifest<br/>exports kind facts to clients"]

  SharedProjectors["Attributed shared capability projectors"] --> ProjectionRegistry["Application projection discovery"]
  InfraMappers["Kind and capability mapper classes"] --> MapperDI["Infrastructure DI discovery"]
  CapabilityTypes["CapabilityKind-attributed document types"] --> JsonResolver["JSON polymorphism discovery"]
```

At startup, the system rejects:

- an `EntityKind` with no definition;
- duplicate kind identities, stable codes, Entity CLR types, request kinds, or
  capability discriminators;
- invalid navigation topology or non-contiguous search ordering;
- inconsistent request/acquisition-profile policy;
- missing or invalid structural-placement policy;
- a definition that declares one set/order of kind capabilities but projects
  another;
- duplicate capability types in defaults or in one projected document.

The compile-time binding `Entity<TDefinition>` also prevents each concrete Entity
from repeating a kind property or consulting a switch. Its constructor resolves
the one discovered definition of `TDefinition`.

### What discovery does not invent

Discovery removes registration sprawl, but it cannot infer new data semantics.
A new stored field can still require an EF row and migration. A new wire payload
still requires a document type. Swift still needs a concrete decoder for a new
payload shape. A genuinely new destination still needs UI. The goal is one local
implementation of each required concern, not pretending those concerns are the
same layer.

## How Entity Data Is Created

There are two intentional write lanes.

```mermaid
flowchart LR
  subgraph Materialization["Technical materialization lane"]
    Source["Filesystem scan, import,<br/>provider apply, Wanted materialization"]
    Technical["Focused infrastructure workflow"]
    Stored[("Entity root, detail, capability,<br/>file and relationship rows")]
    Source --> Technical --> Stored
  end

  subgraph Behavior["Domain behavior lane"]
    Request["HTTP command or worker use case"]
    UseCase["Application orchestration"]
    Repository["EfEntityRepository"]
    Hydrated["Hydrated concrete Entity"]
    Mutation["Rename, Rate, PatchFlags,<br/>progress or kind behavior"]
    Request --> UseCase --> Repository --> Hydrated --> Mutation --> Repository
  end

  Repository <--> Stored
```

Scans and imports are high-volume technical workflows. They often upsert
`EntityRow`, detail, file, and relationship rows directly because their job is to
materialize discovered storage truth, not execute an aggregate business behavior.
The next domain read constructs the concrete Entity from those rows. These focused
writers still pass structural assignments through `EntityStructurePlacementValidator`,
which resolves the actual parent kind and rejects cycles only when placement changes.
It is an invariant boundary, not a universal repository or global change-tracker hook.

User mutations and business operations load an Entity through
`EfEntityRepository`, call behavior, and persist it through the discovered
mappers. A focused persistence service is still appropriate when an operation is
technical, bulk-oriented, or does not need domain behavior. The review question is
whether an invariant is being bypassed, not whether every write happens through
one universal repository.

## Persistence Shape

The following is conceptual. `KIND_DETAIL_ROW` and `CAPABILITY_ROW` each represent
several concrete tables.

```mermaid
erDiagram
  ENTITY_KIND_SEED ||--o{ ENTITY : "KindCode"
  ENTITY ||--o| KIND_DETAIL_ROW : "optional kind data"
  ENTITY ||--o{ CAPABILITY_ROW : "optional shared state"
  ENTITY ||--o{ ENTITY_FILE : "owns"
  ENTITY ||--o{ ENTITY_URL : "has"
  ENTITY ||--o{ EXTERNAL_ID : "has"
  ENTITY ||--o{ USER_ENTITY_STATE : "per user"
  ENTITY ||--o{ RELATIONSHIP_LINK : "source"
  ENTITY ||--o{ RELATIONSHIP_LINK : "target"
  ENTITY ||--o{ ENTITY : "ParentEntityId"
```

Important details:

- Structural hierarchy is `EntityRow.ParentEntityId`; there is no required
  in-memory global Entity graph.
- Every kind explicitly declares whether it may be a root and which direct parent
  kinds it accepts. Parent declarations are canonical; inverse child lists are derived.
- `Entity.AddChild`, repository hydration/save, wanted materialization, provider
  structure application, and scan persistence enforce the same discovered policy.
- Non-structural relationships and credit edge metadata live in explicit link
  rows.
- Rating, favorite, playback, and reading progress are per-user facts in
  `UserEntityStateRow`, even though the hydrated Entity offers convenient behavior.
- Source ownership is a derived subtree fact based on source-role files. Clients
  must not infer it from a kind or an incidental detail row.
- Kind codes are stored as stable strings resolved by `EntityKindCodec`; changing
  one is a schema/data compatibility decision, not a display-label edit.

## Detail Read and Projection Flow

Both frontends use `GET /api/entities/{id}` for the canonical detail document.
Kind-specific writes, child operations, and playback routes remain separate where
their commands genuinely differ.

```mermaid
sequenceDiagram
  participant Client as Svelte or Swift client
  participant Endpoint as GET /api/entities/{id}
  participant Read as EfEntityReadService
  participant Repo as EfEntityRepository
  participant Mapper as Discovered mappers
  participant DB as PostgreSQL
  participant Projector as EntityCardProjector
  participant Definition as EntityKindDefinition

  Client->>Endpoint: Request one Entity document
  Endpoint->>Read: GetAsync id and visibility
  Read->>DB: Check user, library, collection and NSFW visibility
  Read->>Repo: FindShallowAsync
  Repo->>DB: Load EntityRow
  Repo->>Mapper: Construct concrete kind
  Mapper->>DB: Load optional kind detail
  Repo->>Mapper: Hydrate every present domain capability
  Mapper->>DB: Load capability and universal rows
  Repo-->>Read: Concrete hydrated Entity
  Read->>DB: Resolve source ownership, credits, children and relationships
  Read->>Projector: Project shared root and capability state
  Projector->>Definition: Project typed kind-specific document capabilities
  Definition-->>Projector: Immutable capability values
  Read-->>Endpoint: EntityCard with thumbnail groups
  Endpoint-->>Client: Discriminated JSON document
```

`FindShallowAsync` intentionally does not recursively hydrate every child and
relationship. `EfEntityReadService` uses bounded row projections for child and
relationship thumbnails, then replaces the empty groups on the projected card.
That prevents one detail read from constructing a large object network.

Shared document capabilities are produced by discovered application projectors.
Each projector receives one context and either returns its typed capability or
`null`. Kind-specific immutable capabilities are produced by the strongly typed
method on the Entity's definition. The registry combines both sets and rejects
duplicates.

Clients must select capabilities by discriminator, never by array position. Output
ordering is deterministic for readable JSON and stable tests, but order has no
domain meaning.

## List and Batch Reads Are Deliberately Different

```mermaid
flowchart TD
  ListRequest["GET /api/entities with filters"] --> RowQuery["EF row-optimized query"]
  RowQuery --> Thumbnail["EntityThumbnail page"]
  Thumbnail --> Grid["EntityGrid / native thumbnail collection"]

  DetailRequest["GET /api/entities/{id}"] --> ShallowHydration["Shallow domain hydration"]
  ShallowHydration --> Card["EntityCard document"]
  Card --> Detail["Shared detail presentation"]

  NeedMore["Several parent or reference ids"] --> Batch["children or thumbnails batch endpoint"]
  Batch --> Grid
```

Lists do not call the detail endpoint once per result. They project
`EntityThumbnail` directly from EF rows and contributors. When a page needs several
known children or references, use `/api/entities/children` or
`/api/entities/thumbnails`. Repeated `fetchEntity` calls in a collection loop are a
review signal for a missing batch projection or an unnecessarily rich UI need.

## Capability Lifecycle

The word “capability” covers three related but distinct things:

| Form | Use it when | Example path |
| --- | --- | --- |
| Mutable domain capability | An optional state/behavior module can be attached to an Entity and has a persistence lifecycle. It may be used by one kind today and still be the right abstraction. | `CapabilityDates` → `DatesCapabilityMapper` → `DatesCapabilityProjector` |
| Kind-owned document capability | A concrete Entity has kind-specific state that clients need, but no reusable mutable module is required. | `Book.Format` → `BookEntityKindDefinition.ProjectCapabilities` → `BookMetadataCapability` |
| Universal document capability | The API needs a uniform view of root or request-scoped facts with no one-to-one domain capability. | rating, flags, images, links, file management |

Every document capability type owns its wire discriminator through
`[CapabilityKind("...")]`. `CapabilityPolymorphism` discovers all attributed
subtypes rather than maintaining a `JsonDerivedType` chain. The same discriminator
set is emitted in the code manifest.

Use this decision tree before adding a field:

```mermaid
flowchart TD
  Start{"What kind of fact is this?"}
  Start -->|Stable for every instance of one kind| Definition["EntityKindDefinition policy"]
  Start -->|One instance's business state| Instance{"Reusable optional behavior?"}
  Start -->|Query-only aggregate or derived fact| ReadProjection["Infrastructure read projection"]
  Start -->|Presentation only| Frontend["Shared frontend view model or component"]
  Start -->|External protocol detail| Adapter["Infrastructure adapter constants and mapping"]

  Instance -->|Yes| DomainCapability["Domain capability + discovered EF mapper"]
  Instance -->|No, truly kind-specific| ConcreteEntity["Concrete Entity field + kind mapper"]
  DomainCapability --> SharedProjector["Discovered shared document projector"]
  ConcreteEntity --> KindProjector["Definition-owned typed document projection"]
  ReadProjection --> Contract["Explicit DTO or document capability"]
```

Do not put EF queries, `DbContext`, HTTP route construction, JSON parsing, or
platform UI types on a definition. Moving stable semantic policy into the
definition reduces sprawl; moving adapter mechanics there couples the domain to
things that change for unrelated reasons.

## Contract and Code Generation

```mermaid
flowchart TD
  Definitions["EntityKindDefinitions"] --> Manifest["/api/_codegen/codes.json"]
  CodeEnums["Code enums, constants,<br/>capability discriminators"] --> Manifest
  Contracts["Contracts + API endpoint metadata"] --> OpenAPI["/openapi/v1.json"]

  OpenAPI --> Orval["Orval"]
  Orval --> TsModels["Svelte generated models"]
  Orval --> TsOperations["Svelte generated operations"]
  Manifest --> TsCodes["gen-codes.mjs → codes.ts"]

  Manifest --> SwiftCodes["ContractCodes.generated.swift"]
  Manifest --> SwiftKinds["EntityKindDefinitions.generated.swift"]
  Manifest --> SwiftRequests["RequestKindDefinition.swift"]

  TsModels --> Svelte["Svelte API helpers and presentation"]
  TsOperations --> Svelte
  TsCodes --> Svelte
  SwiftCodes --> Native["Swift Decodable models and presentation"]
  SwiftKinds --> Native
  SwiftRequests --> Native
```

Svelte receives endpoint operations and DTO types through OpenAPI/Orval, and all
closed codes and kind definitions through `codes.ts`. It should not declare a
parallel wire union.

Swift receives closed codes, complete kind definitions, and request definitions
from the same backend manifest. Its `EntityDetail` transport model is currently a
native `Decodable` type. `EntityCapability` is an explicit Swift sum type with an
`unknown` fallback, so adding a new capability payload requires either a matching
native case/decoder or accepting it as unknown until the app catches up. The
manifest parity scripts protect code values; they do not generate every payload
struct today.

## Frontend Data Flow

```mermaid
flowchart LR
  API["Canonical Entity API"]

  API --> SvelteFetch["Svelte fetchEntity / fetchEntities"]
  SvelteFetch --> SvelteController["EntityDetailPageController<br/>loading, retry, NSFW, stale requests, mutations"]
  SvelteController --> CapabilityHelpers["typed getCapability helpers"]
  CapabilityHelpers --> DetailModel["entityCardToDetailCard"]
  DetailModel --> SvelteDetail["EntityDetail and route-specific sections"]
  SvelteFetch --> SvelteGrid["EntityIndexPage → EntityGrid → EntityThumbnail"]

  API --> NativeClient["PrismediaAPIClient"]
  NativeClient --> NativeLoader["PrismediaEntityDetailLoader"]
  NativeLoader --> NativeService["EntityDetailService + EntityDetailState"]
  NativeService --> NativePresentation["EntityDetailPresentation"]
  NativePresentation --> NativeView["EntityDetailView and shared components"]
  NativeClient --> NativeGrid["EntityThumbnailCardView collections"]
```

The two apps use the same API roots and semantic document. They are not expected
to share presentation source code:

- Svelte's controller owns cancellation generations, retry/loading state, NSFW
  reloads, breadcrumbs, and optimistic shared metadata mutations. Routes should
  provide only their load function, breadcrumbs, and truly kind-specific data.
- Swift's `EntityDetailService` performs I/O while `EntityDetailState` owns request
  generations, loading/failure state, and mutation refresh. Presentation is derived
  from capabilities by `EntityDetailPresentation`.
- Entity lists should reach `EntityGrid`/`EntityThumbnail` on Svelte and the shared
  native thumbnail card surface on Swift. A new route-local Entity card is usually
  duplication.
- Playback may keep platform-specific orchestration. The shared contract and kind
  roots still apply, but video playback behavior is intentionally not being unified.

## Adding an Entity Kind

Use this order:

1. Add the typed `EntityKind` member. Do not add a code attribute; the definition
   owns the stable code.
2. Add one parameterless definition beside the concrete Entity. Fill in identity,
   storage, presentation, navigation/search, and `EntityKindBehavior`.
3. Bind the concrete type through `Entity<TDefinition>`. Use a root factory only
   if shared root fields are sufficient.
4. Declare default domain capabilities, optional facets, request descriptors,
   acquisition profile, containment, and structural thumbnail counts locally.
5. If the type has specific stored state, add its detail row, EF configuration,
   migration, and one discovered `IEntityKindMapper`. Otherwise the convention
   mapper is automatic.
6. Add any kind-owned document capability types, declare their exact order in
   `ProjectedCapabilityTypes`, and project them in the typed definition method.
7. Regenerate Svelte OpenAPI/codes and the three Swift manifest outputs.
8. Add UI only for behavior that the shared grid/detail scaffolds cannot express.
9. Test the domain invariant, mapper round trip, contract projection, and one real
   client behavior. Do not create tests for every constructor assignment.

You should **not** edit:

- `EntityKindRegistry`;
- `CodecRegistry` or `EntityKindCodec`;
- `RequestKindRegistry`;
- a central kind-to-label, kind-to-icon, kind-to-route, or kind-to-acquisition map;
- hand-written TypeScript or Swift raw-value lists covered by generation.

If adding a kind requires one of those edits, first ask whether a semantic fact is
missing from the definition or manifest.

## Adding a Capability

Choose one of these paths:

### Reusable mutable capability

1. Add the domain capability and its behavior.
2. Add one discovered `IEntityCapabilityMapper` if it persists.
3. Add the immutable document capability with `[CapabilityKind]`.
4. Add one attributed `EntityCapabilityProjector<T>` whose `Project` method returns
   a value when applicable and `null` otherwise.
5. Add the capability to kind defaults only where it should exist by default.
6. Regenerate clients and add the concrete Swift payload decoder if native uses it.

No registry list changes are required.

### Truly kind-specific document data

1. Keep the state on the concrete Entity and persist it in its kind mapper.
2. Put the immutable capability beside the definition.
3. Add its type to `ProjectedCapabilityTypes` and return it from the typed projector.
4. Regenerate clients and update native decoding when needed.

Do not create an empty mutable domain capability solely to make an API value look
like every other persistence module.

## What to Look for During Review

The following are high-signal smells:

- a growing `if`/`switch` chain over `EntityKind` that repeats stable domain facts;
- a new central registration array or dictionary;
- the same closed-set string outside its definition, `[Code]` enum, constant class,
  or generated client output;
- a per-kind detail DTO or a kind-specific GET used instead of `EntityCard`;
- a frontend list that fetches one full Entity document per item;
- children or relationships represented as full recursive documents rather than
  references/thumbnails and bounded groups;
- a route-local Entity card, raw thumbnail, retry shell, or metadata mutation that
  duplicates the shared component/controller;
- EF rows returned directly from an endpoint or domain behavior implemented in a
  Svelte/Swift view;
- a test asserting source text, CSS details, mapper assignments, or a one-off patch
  instead of an invariant or user-visible behavior.

Some branching is legitimate. A filesystem classifier, Stash adapter,
media codec boundary, SQL projection, or SF Symbol mapping may be platform- or
protocol-specific. The test is whether the branch translates an external concern,
or secretly re-declares a fact already owned by the Entity definition.

## Current Review Pressure Points

These areas deserve scrutiny as the architecture continues to converge:

- Swift capability payload decoding is still an explicit switch. Code and kind
  parity are generated, but payload construction is not yet generated.
- Swift presentation still contains some platform-specific kind and media-role
  mappings. SF Symbols are native concerns; repeated backend semantics are not.
- Svelte detail routes still perform some related-Entity hydration for media-specific
  experiences. Prefer batch children/thumbnails or richer projections when several
  documents are loaded only to render a list.
- `EntityDetail.svelte`, `EntityGrid.svelte`, and `EntityGridToolbar.svelte` remain
  broad public façades even as concerns move into child components. Add behavior to
  the narrow owning child rather than growing the façade.
- Scan/import materializers write rows directly in several focused modules. Shared
  lifecycle rules should come from definitions or common persistence policy, while
  media-family parsing remains specialized.
- The shared `Entity` root should gain data only when the fact is truly meaningful
  across Entity kinds. Convenience alone is not enough.

## Start Here in the Code

| Question | First file |
| --- | --- |
| What defines a kind? | `apps/backend/src/Prismedia.Domain/Entities/Kinds/EntityKindDefinition.cs` |
| Where is one real example? | `apps/backend/src/Prismedia.Domain/Media/Book.cs` |
| How are definitions found and checked? | `apps/backend/src/Prismedia.Domain/Entities/Kinds/EntityKindRegistry.cs` |
| How is a domain Entity composed? | `apps/backend/src/Prismedia.Domain/Entities/Entity.cs` |
| How is it hydrated and saved? | `apps/backend/src/Prismedia.Infrastructure/Entities/EfEntityRepository.cs` |
| How do mappers join automatically? | `apps/backend/src/Prismedia.Infrastructure/Entities/Mappers/EntityMappers.cs` and `DependencyInjection.cs` |
| How does detail projection work? | `apps/backend/src/Prismedia.Application/Entities/EntityCardProjector.cs` |
| How do shared capabilities join? | `apps/backend/src/Prismedia.Application/Entities/EntityCapabilityProjectionRegistry.cs` |
| How is JSON capability polymorphism built? | `apps/backend/src/Prismedia.Contracts/Entities/Capabilities/Core/CapabilityPolymorphism.cs` |
| How is the canonical GET served? | `apps/backend/src/Prismedia.Api/Endpoints/Entities/EntityDetailEndpoint.cs` and `EfEntityReadService.GetAsync` |
| How are codes/definitions exported? | `apps/backend/src/Prismedia.Api/Codegen/CodesManifest.cs` |
| How does Svelte call the root API? | `apps/web-svelte/src/lib/api/entities.ts` |
| How does Svelte own detail page state? | `apps/web-svelte/src/lib/components/entities/entity-detail-page-controller.svelte.ts` |
| How does Svelte derive presentation? | `apps/web-svelte/src/lib/entities/entity-detail.ts` and `entity-thumbnail.ts` |
| How does Swift call the root API? | `Prismedia-SwiftUI/PrismediaShared/Networking/PrismediaAPIClient.swift` |
| How does Swift own detail state? | `Prismedia-SwiftUI/PrismediaShared/Features/EntityDetail/Services/EntityDetailService.swift` and `Models/EntityDetailState.swift` |
| What native data is generated? | `Prismedia-SwiftUI/PrismediaShared/Domain/Entities/Generated` and `Prismedia-SwiftUI/Scripts/check-contract-codes.py` |

When tracing a bug, follow the object rather than the folder name:

```text
stored row
  -> discovered mapper
  -> concrete Entity or domain capability
  -> shared projector or definition projector
  -> EntityCard capability discriminator
  -> generated/client decoder
  -> shared presentation mapper
  -> component or view
```

At each arrow, verify that the value is still owned by the same concept and has
not been renamed, defaulted, inferred, or rebuilt independently.
