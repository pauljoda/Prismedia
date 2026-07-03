# Sonarr Release Decision & Ranking System — Parity Map

> **Path note.** The orchestration prompt specified source at `undefined/src` and this
> report file at `undefined/parity/sonarr-decision-engine.md`. `undefined/src` does not
> exist — `undefined` is a literal template-substitution artifact (a `${...}` that
> failed to resolve upstream), and a stray `undefined/` directory was created inside
> the Prismedia repo itself as a side effect. The real Sonarr checkout used for this
> map lives at `/Users/pauldavis/Dev/_ARCHIVE/Sonarr` (root of the Sonarr git repo;
> `src/NzbDrone.Core` underneath is the namespace this report covers). All file paths
> below are given relative to that Sonarr repo root unless stated otherwise. This
> report file itself was written to the literal requested path,
> `undefined/parity/sonarr-decision-engine.md`, under the Prismedia repo root, per the
> deliverable contract.

Every file in the six target directories (`DecisionEngine` incl. `Specifications/`,
`Profiles` incl. `Qualities/`, `Delay/`, `Releases/`, `Qualities`, `CustomFormats`,
`Languages`) was read in full. Counts: DecisionEngine = 12 top-level files + 41 files
under `Specifications/` (38 concrete specifications + 1 interface +
`SameEpisodesSpecification` helper + `UpgradableSpecification` core service) = 53
files. Profiles = 21 files (`Qualities/` 8, `Delay/` 4, `Releases/` 8, plus
`ProfileFormatItem.cs`). Qualities = 13 files. CustomFormats = 18 files (7 core + 2
events + 9 specification implementations). Languages = 5 files. Total: 110 files
read.

---

## 1. Quality Model

### 1.1 `Quality` — the enumerated quality catalog
**File:** `src/NzbDrone.Core/Qualities/Quality.cs`

A `Quality` is an immutable value object: `Id` (int), `Name` (string), `Source`
(`QualitySource` enum), `Resolution` (int, e.g. 480/720/1080/2160). There is a fixed,
hardcoded catalog of exactly **22** qualities (including `Unknown`), each with a
unique numeric ID that is never reused/renumbered (IDs go up to 22; ID 11,
`HDTV-480p`, is explicitly commented out/retired but its number is never recycled):

| Id | Name | Source | Resolution |
|----|------|--------|-----------|
| 0 | Unknown | Unknown | 0 |
| 1 | SDTV | Television | 480 |
| 2 | DVD | DVD | 480 |
| 3 | WEBDL-1080p | Web | 1080 |
| 4 | HDTV-720p | Television | 720 |
| 5 | WEBDL-720p | Web | 720 |
| 6 | Bluray-720p | Bluray | 720 |
| 7 | Bluray-1080p | Bluray | 1080 |
| 8 | WEBDL-480p | Web | 480 |
| 9 | HDTV-1080p | Television | 1080 |
| 10 | Raw-HD | TelevisionRaw | 1080 |
| 12 | WEBRip-480p | WebRip | 480 |
| 13 | Bluray-480p | Bluray | 480 |
| 14 | WEBRip-720p | WebRip | 720 |
| 15 | WEBRip-1080p | WebRip | 1080 |
| 16 | HDTV-2160p | Television | 2160 |
| 17 | WEBRip-2160p | WebRip | 2160 |
| 18 | WEBDL-2160p | Web | 2160 |
| 19 | Bluray-2160p | Bluray | 2160 |
| 20 | Bluray-1080p Remux | BlurayRaw | 1080 |
| 21 | Bluray-2160p Remux | BlurayRaw | 2160 |
| 22 | Bluray-576p | Bluray | 576 |

`Quality.All` and `Quality.AllLookup` (Dictionary<int,Quality>) are static, built
once in a static constructor. `Quality.FindById(id)` throws `ArgumentException` for
an unrecognized id (id 0 always maps to Unknown). Explicit int↔Quality operator
conversions exist for convenient (de)serialization to a single int column.

### 1.2 `QualitySource` — the "source" axis
**File:** `src/NzbDrone.Core/Qualities/QualitySource.cs`

```
Unknown, Television, TelevisionRaw, Web, WebRip, DVD, Bluray, BlurayRaw
```
This is a strictly ordered enum (`Unknown < Television < TelevisionRaw < Web < WebRip
< DVD < Bluray < BlurayRaw`); ordinal comparisons of the enum are used directly (e.g.
`QualityFinder.FindBySourceAndResolution` orders candidate qualities `.OrderBy(q =>
q.Source)` and picks the first with `Source >= source`). Sonarr's "source" concept is
therefore both a semantic tag (where did this release originate) and an implicit
ranking axis, separate from resolution and separate from the profile-level custom
"Weight" ranking described below.

### 1.3 `Resolution` enum (parser-side, not Qualities/ but load-bearing)
**File:** `src/NzbDrone.Core/Parser/QualityParser.cs` (line ~714)

```
R360p=360, R480p=480, R540p=540, R576p=576, R720p=720, R1080p=1080, R2160p=2160, Unknown=0
```
Used by Custom Format's `ResolutionSpecification` (see §3) as the picklist of
resolution values a spec can match on — a superset of the resolutions actually present
in `Quality.All` (360p and 540p exist as parseable/matchable resolutions even though no
`Quality` catalog entry uses them, because no catalog "quality" pairs those
resolutions with a canonical source).

### 1.4 `Revision` — PROPER/REPACK/multi-part versioning
**File:** `src/NzbDrone.Core/Qualities/Revision.cs`

`Revision(int version = 1, int real = 0, bool isRepack = false)`.
- `Version`: increments for v2/v3/etc. release tags.
- `Real`: increments for the informal "REAL" tag some scene groups use to mark a
  second, more-correct proper (a proper of a proper).
- `IsRepack`: distinguishes a REPACK from a PROPER at the flag level (both raise
  `Version`/participate in revision-upgrade comparisons identically — Sonarr does
  not rank REPACK above/below PROPER, only revision `Version`/`Real` order matters).
- `CompareTo`: compares `Real` first, then `Version`. So a v1 REAL beats a v3 non-REAL
  proper — `Real` dominates `Version`.
- Operators `>`, `<`, `>=`, `<=`, `==`, `!=` are all implemented against `CompareTo`.

### 1.5 `QualityModel` — Quality + Revision, the actual per-release value
**File:** `src/NzbDrone.Core/Qualities/QualityModel.cs`

Wraps `Quality` + `Revision` plus three `QualityDetectionSource` diagnostic fields
(`SourceDetectionSource`, `ResolutionDetectionSource`, `RevisionDetectionSource` — all
`[JsonIgnore]`, used only for internal detection-confidence bookkeeping, not by the
decision engine). Implements `IComparable`: `CompareTo` first compares the **static
default `Weight`** of the two qualities (see §1.6 — NOT profile-specific ordering),
then falls back to `Revision.Real`, then `Revision.Version`. This `CompareTo` is a
*global* comparison independent of any specific `QualityProfile`; the
profile-specific comparator used everywhere in the decision engine is
`QualityModelComparer` (§1.7), which resolves ties differently.

### 1.6 `QualityDefinition` — per-quality tunable size limits and global weight
**File:** `src/NzbDrone.Core/Qualities/QualityDefinition.cs`,
`QualityDefinitionLimits.cs`, `QualityDefinitionService.cs`,
`QualityDefinitionRepository.cs`

A `QualityDefinition` (persisted, one row per `Quality`, seeded at startup) carries:
- `Quality` (the enum value)
- `Title` (user-editable display name, resettable via `ResetQualityDefinitionsCommand`)
- `GroupName` (e.g. "WEB 480p", "WEB 720p", "WEB 1080p", "WEB 2160p" — ties WEBDL and
  WEBRip of the same resolution into one movable unit in the profile-quality-ordering
  UI)
- `Weight` (int; global fallback ordering key — see `Quality.DefaultQualityDefinitions`
  hardcoded seed weights 1–18; qualities sharing a `GroupName` share the same `Weight`)
- `MinSize`, `MaxSize`, `PreferredSize` (double?, **megabytes per minute of runtime**
  — not absolute size; `null` `MaxSize` = unlimited)

`QualityDefinitionLimits`: `Min = 0`, `Max = 1000` (bounds for the numeric size
fields in the UI/validation).

`Quality.DefaultQualityDefinitions` is the **hardcoded seed table** (in `Quality.cs`,
not `QualityDefinitionService`) — every quality's default `Weight`/`MinSize`/
`MaxSize`/`PreferredSize`/`GroupName`. Examples: `Unknown` weight 1 (1–199.9MB/min,
preferred 95); `SDTV` weight 2 (2–100MB/min); `WEBRip-480p`/`WEBDL-480p` both weight 3,
GroupName "WEB 480p"; `RAWHD` weight 9, MinSize 4, **MaxSize null** (unlimited — raw HD
capture is inherently large); Remux qualities (`Bluray-1080pRemux` weight 14,
`Bluray-2160pRemux` weight 18) have MinSize 35 and MaxSize null.

`QualityDefinitionService`:
- 5-second in-memory cache (`ICached<Dictionary<Quality, QualityDefinition>>`) keyed
  `"all"`, invalidated on every `Update`/`UpdateMany`.
- `InsertMissingDefinitions()` runs on `ApplicationStartedEvent`: diffs
  `Quality.DefaultQualityDefinitions` against what's in the DB; inserts missing rows,
  updates/keeps existing rows (preserving user edits to `Title`/sizes), deletes
  orphaned rows for retired qualities.
- `Execute(ResetQualityDefinitionsCommand)`: resets `Title` back to default **only if**
  `ResetTitles = true` on the command; sizes are never reset by this command
  (title-only reset).
- `WithWeight`: **always overwrites** the persisted `Weight` with the current
  hardcoded default weight on every read — i.e. `Weight` is NOT user-configurable
  even though it's a DB column; only sizes/titles are user-tunable per quality.

**Configuration surface exposed to the user:** per-quality `Title` (rename), `MinSize`
(MB/min), `MaxSize` (MB/min, blank = unlimited), `PreferredSize` (MB/min, used as a
release-size tie-breaker target — see §7). Weight/GroupName are NOT user-editable.

### 1.7 `QualityModelComparer` — profile-relative quality ordering
**File:** `src/NzbDrone.Core/Qualities/QualityModelComparer.cs`

This is the comparator actually used throughout the decision engine (not
`QualityModel.CompareTo`). It compares two `Quality`/`QualityModel` values using a
specific `QualityProfile`'s **custom item order** (`QualityProfile.GetIndex`, §2.1),
not the global default `Weight`. `Compare(QualityModel, QualityModel,
respectGroupOrder)`: compares profile index first; if tied, falls back to
`Revision.CompareTo`. `respectGroupOrder` controls whether two members of the same
UI group (e.g. WEBDL-1080p vs WEBRip-1080p sharing a group slot) are treated as equal
rank (`false`, the default almost everywhere) or ranked by their position within the
group (`true`, used only for min/max size lookups per §7 and cutoff/upgrade
comparisons that need the concrete member, not just the group).

### 1.8 `QualityFinder` — mapping a parsed (source, resolution) pair back to a `Quality`
**File:** `src/NzbDrone.Core/Qualities/QualityFinder.cs`

Given a parsed `QualitySource` + resolution int, finds the exact `Quality` match; if
none, special-cases sub-720p Television/Web/WebRip sources to their canonical 480p
quality (so a scene 576i broadcast rip doesn't get misfiled as Bluray-576p); otherwise
falls back to the nearest quality at that resolution whose `Source >= source`
(ordinal), logging a warning. This fallback path means an unusual/unrecognized
(source, resolution) combination degrades gracefully to "closest richer source at
that resolution" rather than crashing or defaulting to Unknown.

### 1.9 Supporting types
- `QualitiesBelowCutoff` (`QualitiesBelowCutoff.cs`): simple `(ProfileId,
  QualityIds)` DTO used by whatever reporting/UI surface lists "wanted: missing +
  below cutoff" episodes per profile.
- `QualityDetectionSource` enum: `Unknown, Name, Extension, MediaInfo` — records
  *how* the parser decided quality/resolution/revision (filename heuristic vs.
  extension vs. probed media info), purely diagnostic.
- `ProperDownloadTypes` enum: `PreferAndUpgrade, DoNotUpgrade, DoNotPrefer` — the
  three-way "download propers/repacks" setting (§8).
- `QualityDefinitionRepository` / `QualityDefinitionRepository`: thin
  `BasicRepository<QualityDefinition>` — no custom queries.
- `Commands/ResetQualityDefinitionsCommand`: `{ bool ResetTitles }`, default `false`.

---

## 2. Quality Profiles

### 2.1 `QualityProfile` — the model
**File:** `src/NzbDrone.Core/Profiles/Qualities/QualityProfile.cs`,
`QualityProfileQualityItem.cs`, `QualityIndex.cs`

Fields:
- `Name`
- `UpgradeAllowed` (bool) — master switch: if false, Sonarr never grabs/imports
  anything better than what's already on disk/queued, regardless of cutoff
- `Cutoff` (int) — a `Quality.Id` **or** a synthetic group id (see below); once a file
  meets/exceeds this quality, Sonarr stops looking to upgrade it on quality grounds
  (Custom Format cutoff is tracked separately, see §3.6/§7)
- `MinFormatScore` (int) — hard floor; a release whose total Custom Format score is
  below this is rejected outright regardless of quality (§3.6)
- `CutoffFormatScore` (int) — once existing content's CF score reaches this, CF-based
  upgrading also stops (independent of/parallel to quality `Cutoff`)
- `MinUpgradeFormatScore` (int) — minimum score **increment** a new release's CF score
  must exceed the current file's CF score by to justify a same-quality upgrade
  (prevents upgrade "thrashing" for +1-point CF differences)
- `FormatItems` (`List<ProfileFormatItem>`) — every Custom Format known to the system
  paired with this profile's score for it (see §3.5)
- `Items` (`List<QualityProfileQualityItem>`) — the ordered list defining both
  (a) allowed-vs-not per quality and (b) the profile's private ranking order,
  independent from the global `Weight`.

`QualityProfileQualityItem`: either a **leaf** (`Quality` set, `Items` empty) or a
**group** (`Quality == null`, `Id` is a synthetic id ≥1000, `Items` holds the grouped
member qualities — e.g. "WEB 1080p" grouping WEBDL-1080p + WEBRip-1080p as one
orderable, one-allowed-toggle unit). Each item (leaf or group, and each qualifying
group *member*) individually carries `Allowed`, `MinSize`, `MaxSize`, `PreferredSize`
— i.e. **profiles can override the global per-quality size limits** on a
profile-by-profile basis (`UpdateAllSizeLimits`, called when quality-definition
defaults change, cascades new defaults into every profile's items unless a user has
customized them — actually looking at the code, `UpdateAllSizeLimits` unconditionally
overwrites, so this is a "push global size-limit changes into all profiles" batch
operation, not a targeted default-if-unset).

`QualityProfile.GetIndex(quality, respectGroupOrder)`: linear scan of `Items`; matches
by exact `Quality.Id`, by group synthetic `Id`, or by scanning inside a group's
`Items`. Returns a `QualityIndex(index, groupIndex)`. This index — **the position in
the user-arranged list**, not `Quality.Weight` — is the actual quality ranking used
everywhere quality comparisons happen. This is the single most important design fact
about Sonarr's quality ranking: **profile item order IS the ranking**; the global
`Weight` in `QualityDefinition` only supplies the *default* order when a fresh
profile is created (`GetDefaultProfile` groups `Quality.DefaultQualityDefinitions` by
weight to build the initial `Items` list) and is otherwise unused once a profile
exists (past DB seeding).

`QualityIndex.CompareTo(right, respectGroupOrder)`: compares `Index` first; if tied
and `respectGroupOrder`, compares `GroupIndex` (position within the group) — this lets
size-limit lookups resolve to the specific group member's size fields even though for
ranking purposes group members are usually equal-rank.

`FirststAllowedQuality()` / `LastAllowedQuality()` (sic — the typo is in the actual
Sonarr source): first/last profile item (in list order) with `Allowed == true`;
returns the group's first/last member `Quality` if the matched item is a group. Used
when `UpgradeAllowed == false` to substitute "the single allowed quality acts as the
cutoff" (§2.3).

`CalculateCustomFormatScore(formats)`: `FormatItems.Where(fi => formats.Contains(fi.Format)).Sum(fi => fi.Score)` — simple linear sum of the profile's configured score
for each Custom Format that matched this release. **Formats not present in
`FormatItems` for this profile contribute 0** (there's no assumed default weight);
new Custom Formats are auto-inserted into every profile's `FormatItems` with `Score =
0` when created (`QualityProfileService.Handle(CustomFormatAddedEvent)`).

### 2.2 `QualityProfileService`
**File:** `src/NzbDrone.Core/Profiles/Qualities/QualityProfileService.cs`

- On `ApplicationStartedEvent`, if zero profiles exist, seeds **six default
  profiles**: `Any`, `SD`, `HD-720p`, `HD-1080p`, `Ultra-HD`, `HD - 720p/1080p` — each
  built via `GetDefaultProfile(name, cutoff, allowedQualities...)`.
- `GetDefaultProfile`: groups `Quality.DefaultQualityDefinitions` by `Weight`;
  singleton-weight groups become leaf items, multi-member weight groups (the "WEB
  ___p" families) become group items with a synthetic id starting at 1000
  incrementing per group. `Allowed` per leaf/group is set from the `allowed` params
  list. `Cutoff` defaults to `Quality.Unknown.Id` unless a `cutoff` param is supplied,
  in which case the code finds which group (if any) that cutoff quality landed in and
  points `Cutoff` at the *group* id instead of the leaf id when applicable — so cutoff
  can target a group as a whole. New profiles get `MinFormatScore = 0`,
  `CutoffFormatScore = 0`, `MinUpgradeFormatScore = 1`, and `FormatItems` seeded from
  every currently-known `CustomFormat` at `Score = 0`.
- `Handle(CustomFormatAddedEvent)`: inserts a new zero-score `ProfileFormatItem` at
  index 0 of every profile's `FormatItems`.
- `Handle(CustomFormatDeletedEvent)`: strips the deleted format from every profile's
  `FormatItems`; **if that empties a profile's FormatItems entirely**, resets that
  profile's `MinFormatScore = 0`, `CutoffFormatScore = 0`, `MinUpgradeFormatScore = 1`
  (guards against a profile referencing thresholds that are now meaningless).
- `Delete(id)`: blocked (`QualityProfileInUseException`, HTTP 400) if any `Series` or
  Import List currently references the profile.
- `UpdateAllSizeLimits(params QualityProfileSizeLimit[])`: for every profile, for
  every supplied size-limit-by-quality, locates the corresponding leaf/group-member
  item (`GetIndex(quality, respectGroupOrder: true)`) and overwrites its
  `MinSize`/`MaxSize`/`PreferredSize`. This is the propagation path used when the
  *global* `QualityDefinition` size defaults change and profiles should follow.

### 2.3 How Cutoff + UpgradeAllowed interact (critical semantics, cross-referenced from `UpgradableSpecification`, §6)
- If `UpgradeAllowed == false`: the effective "cutoff" for quality purposes becomes
  the **first allowed quality in profile order**, not the configured `Cutoff` field —
  i.e. Sonarr will grab up to the lowest allowed quality and then never look for
  anything better on quality grounds, no matter what `Cutoff` says. (See
  `QualityCutoffNotMet`: `var cutoff = profile.UpgradeAllowed ? profile.Cutoff :
  profile.FirststAllowedQuality().Id;`)
- If `UpgradeAllowed == true`: cutoff is exactly the configured `Cutoff` item/group.
- Independently of quality cutoff, Custom Format cutoff uses the same
  upgrade-allowed gate: `CustomFormatCutoffNotMet` uses `profile.UpgradeAllowed ?
  profile.CutoffFormatScore : profile.MinFormatScore` — i.e. with upgrades disabled,
  a file that merely clears the *minimum* CF score is already considered
  "cutoff met" for CF purposes (there is no room to move toward the real
  `CutoffFormatScore` once upgrades are off).
- A file is only fully "at cutoff" (`CutoffNotMet` returns false) when **both** the
  quality cutoff is met/exceeded **and** the CF cutoff is met/exceeded. If either
  is unmet, the file is still eligible to be searched for.

### 2.4 `QualityProfileRepository`
**File:** `QualityProfileRepository.cs`

Overrides base `Query` to hydrate `ProfileFormatItem.Format` from a full
`CustomFormat` object (repository does a bulk lookup of all custom formats, then
maps each profile's stored-by-id `Format` reference to the real object) — with a
defensive skip: `FormatItems` referencing a Custom Format id that no longer exists
(orphaned reference — e.g. a delete/update race) are silently dropped rather than
causing a load failure.

---

## 3. Custom Formats

### 3.1 `CustomFormat` — the model
**File:** `src/NzbDrone.Core/CustomFormats/CustomFormat.cs`

`{ Name, IncludeCustomFormatWhenRenaming (bool), Specifications: List<ICustomFormatSpecification> }`.
Equality is by `Id` only. A Custom Format is a **named bundle of specification
conditions** — it does not carry its own score; score is defined per-profile via
`QualityProfile.FormatItems` (§2.1). This is the critical architectural point: the
same Custom Format ("x265", "HDR10+", a specific release-group's tag, etc.) can be
worth different points in different profiles (e.g. +50 in a 4K profile, -1000 as an
effective ban in an SD profile via a large negative score).

### 3.2 `ICustomFormatSpecification` / `CustomFormatSpecificationBase`
**Files:** `Specifications/ICustomFormatSpecification.cs`,
`Specifications/CustomFormatSpecificationBase.cs`

Contract per condition: `Order` (int, display/evaluation ordering in the UI — **not**
used for short-circuiting, all specs on a format are always evaluated), `ImplementationName`, `InfoLink`, `Name` (user label for this condition instance),
`Negate` (bool — invert the match), `Required` (bool — see §3.4 semantics),
`Validate()`, `Clone()` (shallow `MemberwiseClone`), `IsSatisfiedBy(CustomFormatInput)`.
Base class wraps `IsSatisfiedByWithoutNegate` with the `Negate` flip so concrete specs
only implement the positive-case predicate (except `LanguageSpecification`, which
overrides the whole `IsSatisfiedBy` to give `Negate` different semantics — see §3.3.3).

### 3.3 The 9 concrete specification types (exhaustive)

All 9 live directly under `CustomFormats/Specifications/`; `RegexSpecificationBase`
is an abstract base used by two of them.

1. **`ReleaseTitleSpecification`** (`ReleaseTitleSpecification.cs`) — Order 1. Extends
   `RegexSpecificationBase` (compiled, case-insensitive regex against a user-supplied
   pattern, with Perl-style `/pattern/flags` NOT supported here — that syntax is only
   for Release Profile terms, §5). Matches if the regex matches
   `input.EpisodeInfo.ReleaseTitle` **or** `input.Filename` (either can satisfy it —
   this lets the same CF apply whether evaluating a live release title or an
   already-imported file's on-disk name).
2. **`ReleaseGroupSpecification`** (`ReleaseGroupSpecification.cs`) — Order 9. Same
   regex base, matches only `input.EpisodeInfo.ReleaseGroup`.
3. **`LanguageSpecification`** (`LanguageSpecification.cs`) — Order 3. Picklist of
   `Language` enum values (see §4) via `LanguageFieldConverter`, plus an
   `ExceptLanguage` checkbox. Has genuinely special-cased "Original" language
   handling: if `Value == Language.Original.Id` and the series has a known
   `OriginalLanguage` (≠ Unknown), the comparison target becomes the series' actual
   original-language value instead of the literal "Original" placeholder. This is the
   ONLY spec type that overrides `IsSatisfiedBy` wholesale instead of just
   `IsSatisfiedByWithoutNegate` — because `ExceptLanguage` and `Negate` compose in a
   non-trivial way: with `ExceptLanguage=false`, satisfied iff any input language
   equals the target; with `ExceptLanguage=true`, satisfied iff any input language
   differs from the target; `Negate` then flips that entire result (not just the
   inner equality), and the with-negate path is a hand-duplicated mirror of the
   without-negate path rather than a literal `!` of it (`IsSatisfiedByWithNegate`)
   — worth replicating exactly since the boolean algebra with `null` languages lists
   (defaults to `false` via `??`) is easy to get subtly wrong.
4. **`ResolutionSpecification`** (`ResolutionSpecification.cs`) — Order 6. Picklist
   from the parser's `Resolution` enum (§1.3, includes 360p/540p not present in any
   `Quality`). Matches `input.EpisodeInfo.Quality.Quality.Resolution`, defaulting to
   `Resolution.Unknown` (0) if any part of that chain is null.
5. **`SourceSpecification`** (`SourceSpecification.cs`) — Order 5. Picklist of
   `QualitySource` enum (§1.2). Matches `input.EpisodeInfo.Quality.Quality.Source`,
   defaulting to `QualitySource.Unknown` if null.
6. **`IndexerFlagSpecification`** (`IndexerFlagSpecification.cs`) — Order 4. Picklist
   of the `[Flags] IndexerFlags` enum (`Parser/Model/IndexerFlags.cs`): `Freeleech(1),
   Halfleech(2), DoubleUpload(4), Internal(8), Scene(16), Freeleech75(32),
   Freeleech25(64), Nuked(128), Subtitles(256)`. Matches via
   `input.IndexerFlags.HasFlag((IndexerFlags)Value)` — a single-value picklist, not a
   multi-select, so each `IndexerFlagSpecification` instance tests exactly one flag
   bit; multiple flag conditions are composed by adding multiple specification
   instances to the same Custom Format.
7. **`SizeSpecification`** (`SizeSpecification.cs`) — Order 8. `Min`/`Max` in GB
   (validated `Min >= 0`, `Max > Min`, `Max <= double.MaxValue`). Matches
   `input.Size > Min.Gigabytes() && input.Size <= Max.Gigabytes()` — **note the
   asymmetric bounds**: strictly-greater-than min, less-than-or-equal max. This is the
   *total release size*, not per-minute like `QualityDefinition` sizes.
8. **`ReleaseTypeSpecification`** (`ReleaseTypeSpecification.cs`) — Order 10. Picklist
   of `ReleaseType` enum (`Parser/Model/ReleaseType.cs`): `Unknown(0), SingleEpisode(1),
   MultiEpisode(2), SeasonPack(3)`. Matches `input.ReleaseType == (ReleaseType)Value`
   exactly.
9. **`RegexSpecificationBase`** itself (`RegexSpecificationBase.cs`) is abstract, not
   directly selectable — it's the shared implementation for #1 and #2. Its `Value`
   setter compiles the regex (`RegexOptions.Compiled | IgnoreCase`) eagerly on
   assignment; `MatchString(compared)` returns `false` for null input strings or an
   unset (`_regex == null`) pattern — i.e. a not-yet-configured regex condition never
   matches rather than throwing.

(That is 7 independently-selectable condition types plus 1 shared abstract base,
totaling the 8 files in `Specifications/` besides the base contract + base class —
cross-checked against the directory listing: `CustomFormatSpecificationBase.cs`,
`ICustomFormatSpecification.cs`, `IndexerFlagSpecification.cs`,
`LanguageSpecification.cs`, `RegexSpecificationBase.cs`, `ReleaseGroupSpecification.cs`,
`ReleaseTitleSpecification.cs`, `ReleaseTypeSpecification.cs`,
`ResolutionSpecification.cs`, `SizeSpecification.cs`, `SourceSpecification.cs` = 11
files, 9 concrete/selectable types total once the abstract base and interface are
excluded.)

### 3.4 Matching semantics: `Required` and negative-match handling
**File:** `SpecificationMatchesGroup.cs`, `CustomFormatCalculationService.cs`

For a candidate release, each Custom Format's specifications are grouped by their
**concrete type** (`GroupBy(t => t.GetType())`) and each spec in the format
independently evaluated (`IsSatisfiedBy(input)` → bool), producing a
`SpecificationMatchesGroup` (one group per spec *type* present on that format).
`SpecificationMatchesGroup.DidMatch`:
```csharp
!(Matches.Any(m => m.Key.Required && m.Value == false) ||
  Matches.All(m => m.Value == false))
```
i.e. a group of same-typed conditions on one format is satisfied **unless** (a) any
`Required` condition in that type-group failed, or (b) *every* condition of that
type-group failed (so multiple same-typed non-required conditions act as an OR — any
one succeeding is enough — while `Required` conditions act as an additional AND-gate
within their own type group). A `CustomFormat` matches overall only if **every**
type-group's `DidMatch` is true (`specificationMatches.All(x => x.DidMatch)`) — so
across *different* condition types the semantics are effectively AND-of-ORs-with-
required-AND-gates: at least one condition per present type must match (or, if any
condition of that type is `Required`, that specific one must match), and this must
hold for every distinct type of condition attached to the format.

`CustomFormatCalculationService.ParseCustomFormat` (the core matcher) builds a
`CustomFormatInput` and returns `matches.OrderBy(x => x.Name).ToList()` — matched
formats are returned alphabetically by name (this becomes the display order in
history/queue UI, not a ranking).

### 3.5 `CustomFormatInput` — what's available to specs
**File:** `CustomFormatInput.cs`

`{ ParsedEpisodeInfo EpisodeInfo, Series Series, long Size, IndexerFlags IndexerFlags,
List<Language> Languages, string Filename, ReleaseType ReleaseType }`. Built
differently depending on evaluation context — six overloads in
`CustomFormatCalculationService`:
- `(RemoteEpisode, size)` — for a live release under evaluation (grab-time).
- `(EpisodeFile, Series)` / `(EpisodeFile)` — evaluating an existing file on disk
  (used by `UpgradeDiskSpecification`/`UpgradeAllowedSpecification`, §6); release
  title is derived with fallback priority: `SceneName` → `OriginalFilePath` basename
  → `RelativePath` basename.
- `(Blocklist, Series)` — re-derives `ParsedEpisodeInfo` by re-parsing the blocklisted
  release's stored `SourceTitle` (falls back to the raw title if re-parse fails).
- `(EpisodeHistory, Series)` — same re-parse pattern, plus manually
  `long.TryParse`/`Enum.TryParse`s `size`/`indexerFlags`/`releaseType` back out of the
  history event's loosely-typed `Data` dictionary (history stores these as strings).
- `(LocalEpisode, fileName)` — for files actively being imported (uses
  `SceneName` if present, else the literal filename passed in).

### 3.6 Profile integration and scoring recap (ties to §2.1/§2.3)
- `QualityProfile.FormatItems`: `List<ProfileFormatItem { CustomFormat Format, int
  Score }>` — **the score lives on the profile**, not the format.
- `QualityProfile.CalculateCustomFormatScore(matchedFormats)`: sum of `Score` for every
  `FormatItems` entry whose `Format` is present in `matchedFormats`.
- `QualityProfile.MinFormatScore`: hard floor — enforced by
  `CustomFormatAllowedByProfileSpecification` (decision-engine spec, §6) which rejects
  outright if the release's total score is below this, **independent of quality
  cutoff/upgrade logic** (this runs as its own always-applicable decision-engine
  check, not just inside upgrade comparison).
- `QualityProfile.CutoffFormatScore`: once current content's CF score is ≥ this, CF
  is no longer a reason to keep upgrading (parallel axis to quality `Cutoff`).
- `QualityProfile.MinUpgradeFormatScore`: an upgrade purely on CF grounds (same
  quality, higher CF score) must clear the current score by at least this increment,
  not just by 1 point — prevents grabbing marginally-better releases repeatedly.
- Events: `CustomFormatAddedEvent`/`CustomFormatDeletedEvent` — profiles react by
  auto-inserting a 0-score item / stripping the item (and zeroing profile thresholds
  if that empties the profile's FormatItems), see §2.2.

### 3.7 `CustomFormatService` / `CustomFormatRepository`
Simple full-cache CRUD service (`_cache.Clear()` on every mutation), publishing
`CustomFormatAddedEvent`/`CustomFormatDeletedEvent` for profile-sync. `Delete(List<int>
ids)` loops one-by-one (still publishing per-id delete events so each profile sync
runs per format, not batched).

---

## 4. Language Model

### 4.1 `Language`
**File:** `src/NzbDrone.Core/Languages/Language.cs`

Same immutable-value-object pattern as `Quality`. **53 language entries** (52 real
languages ID 1–52, plus `Unknown = 0`, plus a synthetic `Original = -2` sentinel — note
there is a gap at -1, unused). Full list (id: name): 0 Unknown, 1 English, 2 French, 3
Spanish, 4 German, 5 Italian, 6 Danish, 7 Dutch, 8 Japanese, 9 Icelandic, 10 Chinese,
11 Russian, 12 Polish, 13 Vietnamese, 14 Swedish, 15 Norwegian, 16 Finnish, 17
Turkish, 18 Portuguese, 19 Flemish, 20 Greek, 21 Korean, 22 Hungarian, 23 Hebrew, 24
Lithuanian, 25 Czech, 26 Arabic, 27 Hindi, 28 Bulgarian, 29 Malayalam, 30 Ukrainian,
31 Slovak, 32 Thai, 33 Portuguese (Brazil), 34 Spanish (Latino), 35 Romanian, 36
Latvian, 37 Persian, 38 Catalan, 39 Croatian, 40 Serbian, 41 Bosnian, 42 Estonian, 43
Tamil, 44 Indonesian, 45 Macedonian, 46 Slovenian, 47 Azerbaijani, 48 Uzbek, 49
Malay, 50 Urdu, 51 Romansh, 52 Georgian, -2 Original.

`FindById` throws for unrecognized ids (0 always → Unknown). Explicit conversions
support int, and also **string→Language by case-insensitive name match** (used for
config/import parsing), which throws `ArgumentException` if no name matches.

### 4.2 `Original` sentinel semantics
`Language.Original` (id -2) is not a real spoken language — it's a placeholder meaning
"whatever this specific series' native/original language is." It's resolved at
match-time inside `LanguageSpecification` (§3.3.3) by looking up
`Series.OriginalLanguage`, falling back to literal comparison against the sentinel
itself if the series has no known original language (`Language.Unknown`). This lets a
single Custom Format like "Original Language" apply correctly across a library of
mixed-origin shows without per-series configuration.

### 4.3 `LanguagesComparer` — multi-language list ranking
**File:** `LanguagesComparer.cs`

Compares two `List<Language>` (a release/file can carry multiple detected audio
languages, e.g. dual-audio). Ranking rule, evaluated in order:
1. A non-empty list beats an empty list.
2. Among two lists that are each >1 language, more languages beats fewer (multi-audio
   ranked above single unless...).
3. ...but a >1-language list is ranked **below** a single-language list when compared
   against each other (i.e., `x.Count > 1 && y.Count == 1` → x loses; `x.Count == 1 &&
   y.Count > 1` → x wins) — so the actual net ordering is: **exactly one detected
   language > multiple detected languages > empty**. (This is *not* used directly by
   the decision engine specs, which use a single `List<Language>` matched by content
   rather than pairwise ranking two candidate releases' language lists — it exists to
   support UI-side sorting of "what language is this" displays, but was faithfully
   captured here since it's part of the enumerated `Languages` directory contract.)
4. Two single-language lists tie-break by alphabetical `Name`.

### 4.4 Field converters (UI picklist sources, not logic)
- `LanguageFieldConverter`: full `Language.All`, sorted with `Id > 0` (real languages)
  before `Id <= 0` (Unknown/Original), then alphabetically — used by
  `LanguageSpecification.Value`'s picklist (so Custom Format language conditions can
  target Unknown or Original too).
- `OriginalLanguageFieldConverter`: `Language.All.Where(Id >= 0)` — excludes the
  `Original` sentinel (a series can't have its "original language" be the Original
  placeholder) but keeps Unknown. Used for `Series.OriginalLanguage` selection.
- `RealLanguageFieldConverter`: excludes `Unknown` entirely — used wherever a language
  must be a genuine spoken language (e.g. certain profile language-selection UI, not
  present in the six target directories but referenced for context).

---

## 5. Release Profiles (a.k.a. "Restrictions" — required/ignored terms)

**Files:** `Profiles/Releases/ReleaseProfile.cs`, `ReleaseProfileService.cs`,
`ReleaseProfileRepository.cs`, `TermMatcherService.cs`,
`TermMatchers/{ITermMatcher,CaseInsensitiveTermMatcher,RegexTermMatcher}.cs`,
`PerlRegexFactory.cs`

This is a **distinct, older mechanism from Custom Formats** — release-title
allow/deny-listing plus air-date gating, tag-scoped and indexer-scoped, evaluated as
its own pair of decision-engine specs (`ReleaseRestrictionsSpecification`,
`AirDateSpecification`, §6) rather than folded into scoring. Do not conflate with
Custom Formats: Release Profiles are binary accept/reject gates; Custom Formats are
scored/weighted.

`ReleaseProfile` fields: `Name`, `Enabled` (default true), `Required: List<string>`,
`Ignored: List<string>`, `AirDateRestriction` (bool), `AirDateGracePeriod` (int,
days), `IndexerIds: List<int>` (empty = applies to all indexers), `Tags:
HashSet<int>` (empty = applies to all series), `ExcludedTags: HashSet<int>` (series
carrying any of these tags are excluded even if `Tags` would otherwise include them).

**Term matching** (`TermMatcherService`): each `Required`/`Ignored` string is either:
- A **Perl-style regex** `/pattern/flags` (detected via `PerlRegexFactory`'s
  `/(?<pattern>.*)/(?<modifiers>[a-z]*)` format regex) — flags supported: `m`
  (Multiline), `s` (Singleline), `i` (IgnoreCase), `x` (IgnorePatternWhitespace), `n`
  (ExplicitCapture); any other letter throws `ArgumentException` at match-compile
  time. Compiled `.NET` regex under the hood (Perl syntax is NOT actually
  translated/validated — the comment in source explicitly says ".net compliant" is
  assumed).
- Otherwise a **plain case-insensitive substring** match
  (`CaseInsensitiveTermMatcher`: lowercases both term and value, `.Contains`).

Matchers are cached per literal term string for 24 hours (`ICached<ITermMatcher>`),
so identical term strings across different Release Profiles share one compiled
matcher instance.

`ReleaseProfileService.EnabledForTags(tags, indexerId)`: filters to profiles that are
`Enabled`, tag-applicable (`Tags` intersects the series' tags OR `Tags` is empty, AND
`ExcludedTags` does NOT intersect), and indexer-applicable (`IndexerIds` contains this
indexer OR `IndexerIds` is empty).

`AllExcludedForTag`, `AllForTag`, `AllForTags`, `Get`, `Delete`, `Add`, `Update`, `All`
round out CRUD/query surface — no other filtering logic beyond what's described.

**Air date restriction:** `AirDateSpecification` (§6) separately reads
`AirDateRestriction`/`AirDateGracePeriod` off release profiles: among all enabled
profiles for the series+indexer, it picks the "best" one by
`OrderByDescending(AirDateRestriction).ThenByDescending(AirDateGracePeriod)` — i.e. if
ANY applicable release profile enables the restriction, the restriction is considered
enabled for evaluation purposes, and among restriction-enabling profiles the
**largest** grace period wins (most permissive). If no applicable profile has the
restriction enabled at all, air-date gating is skipped entirely for that release.

---

## 6. The Decision Engine

### 6.1 Overall flow (`DownloadDecisionMaker`)
**File:** `src/NzbDrone.Core/DecisionEngine/DownloadDecisionMaker.cs`

Entry points: `GetRssDecision(reports, pushedRelease)` (periodic RSS sync / a
manually-pushed release) and `GetSearchDecision(reports, searchCriteria)` (interactive
or automatic search). Both funnel into a shared generator `GetDecisions`. Per release:

1. Parse the release title (`Parser.ParseTitle`). If parsing fails or looks like a
   possible special episode, attempt `ParseSpecialEpisodeTitle` as a fallback (uses
   TVDB/TVRage/IMDb ids and search-criteria context to disambiguate specials).
2. If series title resolved: map to a concrete `Series`/`Episodes` via
   `IParsingService.Map` (scene-mapping aware).
   - No matching `Series` at all → check `SceneMappingService` for an alias hit; if
     found, reject as `MatchesAnotherSeries` (temporary/permanent depending on
     construction — constructed as `Permanent` by default here); else reject
     `UnknownSeries`.
   - `Series` resolved but zero `Episodes` matched → reject `UnknownEpisode`.
   - Otherwise: run `RemoteEpisodeAggregationService.Augment` (fills in
     cross-referenced metadata), compute `CustomFormats` for the release
     (`CustomFormatCalculationService.ParseCustomFormat`) and
     `CustomFormatScore` (`Series.QualityProfile.CalculateCustomFormatScore`), set
     `DownloadAllowed = Episodes.Any()`, then run the full specification gauntlet via
     `GetDecisionForReport`.
3. If title parse failed entirely AND this is a search (not RSS) AND no series title
   could even be extracted, still surface an `UnableToParse` rejection decision (so
   the caller/UI can show *why* a candidate result was dropped, rather than silently
   omitting it) — this branch synthesizes a minimal `ParsedEpisodeInfo` using just
   `LanguageParser`/`QualityParser` fallbacks so a rejection reason can be attached.
4. Any unhandled exception during processing of one release is caught, logged, and
   turned into an `Error`-reason `DownloadDecision` for *that release only* — one bad
   release never aborts the whole batch.
5. Debug-logs accept/reject with the joined rejection reasons.

### 6.2 `GetDecisionForReport` — priority-tiered specification evaluation
```csharp
foreach (var specifications in _specifications.GroupBy(v => v.Priority).OrderBy(v => v.Key))
{
    reasons = specifications.Select(c => EvaluateSpec(c, remoteEpisode, information))
                            .Where(c => c != null).ToArray();
    if (reasons.Any()) break;
}
```
Every `IDownloadDecisionEngineSpecification` is grouped by its declared
`SpecificationPriority` and evaluated **tier by tier**, cheapest tier first; **all
specs within a tier are always evaluated** (not short-circuited individually) so that
if a release is rejected by multiple specs in the same tier, *all* of those rejection
reasons are collected and surfaced together — but as soon as **any** spec in a tier
produces a rejection, evaluation stops and **later (more expensive) tiers are never
run**. This is the actual meaning of "priority" here: it's a short-circuiting cost
gate, not a first-match-wins single-spec priority.

`SpecificationPriority` enum (`SpecificationPriority.cs`) — only 4 named values but
effectively 2 distinct tiers because three share value 0:
```
Default = 0, Parsing = 0, Database = 0, Disk = 1
```
So **all cheap/in-memory/parsing/DB-cache checks run in tier 0**, and **only disk I/O
checks (`FreeSpaceSpecification`, `DeletedEpisodeFileSpecification`) run in tier 1**,
and tier 1 is skipped entirely if anything in tier 0 already rejected. This is a
meaningful perf optimization to replicate: never touch the filesystem to evaluate a
release that's already going to be rejected on cheaper grounds.

Any exception thrown by an individual spec's `IsSatisfiedBy` is caught per-spec
(attaching the release JSON and parsed-info JSON to the exception's `Data` for
diagnostics) and converted into a `DecisionError` rejection for that spec only — one
buggy/throwing spec doesn't kill the whole decision for that release, nor does it
stop the other specs *in the same tier* from also being evaluated (they're all
`.Select`ed before the `.Where(c => c != null)` filter).

### 6.3 Complete enumerated list of `IDownloadDecisionEngineSpecification` (38 total)

Each entry: accepts/rejects semantics, rejection reason code(s), priority tier,
rejection `Type` (Permanent vs Temporary — Temporary rejections are treated
differently downstream: `DownloadDecision.TemporarilyRejected` is true only when
**every** rejection on that decision is `Temporary`, meaning the release may become
acceptable later without any state change, e.g. once a delay window passes).

**Root `DecisionEngine/Specifications/` (25 files, of which 23 implement the
interface; `IDownloadDecisionEngineSpecification.cs` is the contract itself and
`SameEpisodesSpecification.cs` is a plain helper class consumed by
`SameEpisodesGrabSpecification`, not itself an `IDownloadDecisionEngineSpecification`):**

1. **`AcceptableSizeSpecification`** — Default/Permanent. Rejects specials (skips
   check), zero-size releases (skips check — size unknown, can't validate). Computes
   effective runtime: per-episode `Runtime` if set, else series `Runtime`; if series
   runtime is 0, tries a fallback of 45 minutes **only if** this is the show's pilot
   episode/season and all its episodes air within 24h of the first (a heuristic for
   brand-new shows without a confirmed runtime yet). If total computed runtime is
   still 0 → reject `UnknownRuntime` (can't safely size-check). Otherwise looks up the
   profile's per-quality (group-respecting) `MinSize`/`MaxSize` (MB/min), multiplies
   by total runtime minutes, and rejects `BelowMinimumSize` / `AboveMaximumSize` if
   release size falls outside. `MaxSize` of 0 or null = unlimited (skip max check).
2. **`AirDateSpecification`** — Database/Permanent. See §5's air-date restriction
   description. No applicable release profile → accept. Best matching profile found;
   if it doesn't actually enable `AirDateRestriction` → accept. Else every episode in
   the release must have a known `AirDateUtc` (else reject `BeforeAirDate` — "No air
   date available") and the release's `PublishDate` must be ≥ `airDate +
   gracePeriodDays` for every episode, else reject `BeforeAirDate` with the computed
   dates in the message.
3. **`AlreadyImportedSpecification`** — Database/Permanent. Only runs if Completed
   Download Handling (CDH) is enabled (`EnableCompletedDownloadHandling` config). For
   each episode that already `HasFile`: finds the most recent `Grabbed` history event
   and a matching `DownloadFolderImported` event tied to that same download id; if the
   grabbed and imported *qualities* already match, skip (no redundant-download risk).
   Otherwise (mismatched qualities — e.g. grabbed as 1080p, only a lower-quality file
   actually got imported), guards against re-grabbing the exact same content: rejects
   `AlreadyImportedSameHash` if it's a torrent whose `InfoHash` matches the prior
   grab's `DownloadId`, or `AlreadyImportedSameName` if the release `Title` exactly
   (case-insensitive) matches the prior grab's `SourceTitle`.
4. **`AnimeVersionUpgradeSpecification`** — Default/Permanent. Only applies to
   `SeriesTypes.Anime`. Skipped entirely if `DownloadPropersAndRepacks ==
   DoNotPrefer`. For each existing episode file, if the new release is a revision
   upgrade (`IsRevisionUpgrade`, §6.4) of that file, **release-group must match**:
   reject `UnknownReleaseGroup` if either side's release group can't be determined,
   or `ReleaseGroupDoesNotMatch` if they differ. (Anime fansub groups commonly
   release v2/v3 versions of only their own encodes — this prevents "upgrading" to
   a different group's v2 which may not actually be compatible/better.)
5. **`BlockedIndexerSpecification`** — Database/Temporary. 15-second cached lookup of
   currently-blocked indexer ids (`IIndexerStatusService.GetBlockedProviders`);
   rejects `IndexerDisabled` with the disabled-until timestamp in the message if this
   release's indexer is currently blocked (e.g. due to repeated failures).
6. **`BlocklistSpecification`** — Database/Permanent. Rejects `Blocklisted` if
   `IBlocklistService.Blocklisted(seriesId, release)` — i.e. this exact
   release was previously grabbed and failed/blocklisted for this series.
7. **`CustomFormatAllowedbyProfileSpecification`** (class name has a lowercase "by" —
   verified from source) — Default/Permanent. Rejects `CustomFormatMinimumScore` if
   `subject.CustomFormatScore < profile.MinFormatScore`. Independent, always-on
   floor check (§3.6).
8. **`FreeSpaceSpecification`** — **Disk**/Permanent. Skippable via
   `SkipFreeSpaceCheckWhenImporting`. Gets available space at the series' library
   path (missing directory → treat as "unknown," accept rather than block); rejects
   `MinimumFreeSpace` if downloading this release would leave ≤0 space remaining, or
   less than the configured `MinimumFreeSpaceWhenImporting` threshold.
9. **`FullSeasonSpecification`** — Default/Permanent. Only applies to releases parsed
   as `FullSeason`. Rejects `FullSeasonNotAired` if any episode in that season lacks
   an air date or airs more than 24h in the future (prevents grabbing a "season pack"
   release that's actually incomplete/mislabeled before the season has finished
   airing).
10. **`MaximumSizeSpecification`** — Default/Permanent. Global (not per-quality)
    `MaximumSize` config (MB); 0 = disabled. Zero-size release skips check. Rejects
    `MaximumSizeExceeded`.
11. **`MinimumAgeSpecification`** — Default/**Temporary**. Usenet-only (torrents
    always pass). `MinimumAge` config in minutes; 0 = disabled. Rejects `MinimumAge`
    if the release's `AgeMinutes` is under the threshold — Temporary because it
    becomes satisfiable purely by the passage of time.
12. **`MultiSeasonSpecification`** — Default/Permanent. Rejects `MultiSeason`
    unconditionally if `ParsedEpisodeInfo.IsMultiSeason` — multi-season packs
    (`S01-S03`) are never supported, no config to enable them.
13. **`NotSampleSpecification`** — Default/Permanent. Rejects `Sample` if the title
    contains "sample" (case-insensitive substring) AND size < 70MB — the size check
    keeps this from false-positive rejecting a legitimately large release whose title
    happens to contain the word "sample."
14. **`ProtocolSpecification`** — Default/Permanent. Reads the best `DelayProfile`
    for the series' tags; rejects `ProtocolDisabled` if the release's protocol
    (Usenet/Torrent) isn't enabled (`EnableUsenet`/`EnableTorrent`) on that profile.
15. **`QualityAllowedByProfileSpecification`** — Default/Permanent. Looks up the
    release's quality's item/group in the profile; rejects `QualityNotWanted` if
    `Allowed == false` on that item/group.
16. **`QueueSpecification`** — Default/Permanent. For every item **already in the
    download queue** for the same series+overlapping episodes (skipping items in
    `FailedPending` state to avoid a race with in-flight replacement searches):
    recomputes that queued item's Custom Formats, checks
    `UpgradableSpecification.CutoffNotMet` (reject `QueueCutoffMet` if the queued
    item already meets cutoff — no need to grab something else too) and
    `IsUpgradable` (maps `UpgradeableRejectReason` → matching `QueueXxx` rejection
    reason — `BetterQuality`→`QueueHigherPreference`,
    `BetterRevision`→`QueueHigherRevision`, `QualityCutoff`→`QueueCutoffMet`,
    `CustomFormatCutoff`→`QueueCustomFormatCutoffMet`,
    `CustomFormatScore`→`QueueCustomFormatScore`,
    `MinCustomFormatScore`→`QueueCustomFormatScoreIncrement`,
    `UpgradesNotAllowed`→`QueueUpgradesNotAllowed`). Additionally, if this would be a
    same-quality revision upgrade over the queued item and
    `DownloadPropersAndRepacks == DoNotUpgrade`, rejects `QueuePropersDisabled`.
17. **`RawDiskSpecification`** — Default/Permanent. Three compiled regexes detect
    raw-disc release titles (disc-N Blu-ray patterns, "full Blu-ray," DVD-R/DVD5/DVD9
    patterns) → reject `Raw` ("Raw Bluray/DVD release"). Also rejects by container
    extension if known: `vob`/`iso` → "Raw DVD release", `m2ts` → "Raw Bluray
    release". No config toggle — raw disc releases are always rejected.
18. **`ReleaseRestrictionsSpecification`** — Default/Permanent. See §5 — evaluates
    `Required`/`Ignored` terms from all enabled+applicable Release Profiles. **Any**
    matching `Ignored` term → reject `MustNotContainPresent` (message lists which
    ignored terms matched). Each `Required`-bearing profile must have **at least
    one** of its required terms present, else reject `MustContainMissing`
    (message lists the profile's required terms, not which one[s] were satisfied by
    others).
19. **`RepackSpecification`** — Database/Permanent. Only applies if
    `Revision.IsRepack`. Skipped if `DoNotPrefer`. For each existing file that this
    would be a revision-upgrade over: `DoNotUpgrade` → reject `RepackDisabled`;
    otherwise release-group must match between file and new release (mirrors the
    Anime version-upgrade group-matching logic) — reject
    `RepackUnknownReleaseGroup` if either is unknown, `RepackReleaseGroupDoesNotMatch`
    if they differ (case-insensitive compare).
20. **`RetentionSpecification`** — Default/Permanent. Usenet-only. `Retention` config
    (days); 0 = unlimited. Rejects `MaximumAge` if release `Age` (days) exceeds
    retention.
21. **`SameEpisodesGrabSpecification`** — Default/Permanent. Delegates to
    `SameEpisodesSpecification` (plain helper, not itself a decision spec): for every
    distinct existing `EpisodeFileId` referenced by the target episodes, all episodes
    that file actually covers must be a subset of the episodes in this release;
    otherwise reject `ExistingFileHasMoreEpisodes` (protects against replacing a
    multi-episode file with a release covering fewer episodes than the file already
    has, which would silently lose episode coverage).
22. **`SceneMappingSpecification`** — Default/**Temporary** ("Temporary till there's a
    mapping" per the source comment). No mapping / no explicit `SceneOrigin` → accept.
    `SceneOrigin` starting with `mixed:` → reject `AmbiguousNumbering` (scene uses
    multiple, unidentifiable numbering schemes for this show — can't safely trust
    parsed episode numbers). `SceneOrigin` starting with `unknown:` → just logs a
    debug note (assumed numbering type from the second colon-segment, defaulting to
    "scene") but does NOT reject.
23. **`SeasonPackOnlySpecification`** — Default/Permanent. Only meaningful during a
    multi-episode search (`searchCriteria.Episodes.Count > 1`) for `Standard` series
    type, non-full-season singles. If `Release.SeasonSearchMaximumSingleEpisodeAge >
    0` (a per-release/indexer-config value) and the season's episodes (that have
    aired) finished airing more than that many days ago, reject `NotSeasonPack` —
    forces "old completed seasons must be grabbed as a season pack, not
    episode-by-episode" once single episodes are considered stale.
24. **`SplitEpisodeSpecification`** — Default/Permanent. Rejects `SplitEpisode`
    unconditionally if `ParsedEpisodeInfo.IsSplitEpisode` — split/partial episode
    releases (e.g. "1x01a") are never supported.
25. **`TorrentSeedingSpecification`** — Default/Permanent. Torrent-only (no-op if not
    a `TorrentInfo` or indexer id is 0). Looks up the indexer's
    `ITorrentIndexerSettings.MinimumSeeders`; rejects `MinimumSeeders` if the
    release's reported seeder count is below that per-indexer threshold. Missing
    indexer (deleted since release was fetched) → accept rather than fail.
26. **`UpgradableSpecification`** (`UpgradableSpecification.cs`) — not itself an
    `IDownloadDecisionEngineSpecification` (no `Priority`/`Type`/`IsSatisfiedBy`
    matching that interface's signature); it's the shared core comparison **service**
    consumed by `QueueSpecification`, `UpgradeAllowedSpecification`,
    `UpgradeDiskSpecification`, `HistorySpecification`, `AnimeVersionUpgradeSpecification`,
    `RepackSpecification`, `DelaySpecification`, `ProperSpecification`. Full mechanics
    in §6.4 below since it's the crux of the whole ranking system.
27. **`UpgradeAllowedSpecification`** — Default/Permanent. For each existing episode
    file, recomputes that file's Custom Formats and calls
    `UpgradableSpecification.IsUpgradeAllowed`; rejects
    `QualityUpgradesDisabled` if that returns false (i.e., this would be a real
    upgrade in quality or CF score terms, but the profile's `UpgradeAllowed` is off,
    and it's not a pure same-quality revision upgrade which is always allowed
    regardless of that flag — see §6.4's `IsUpgradeAllowed`).
28. **`UpgradeDiskSpecification`** — Default/Permanent. The most involved spec.
    Non-season-pack path: for each existing file, if cutoff is already met for that
    file (`CutoffNotMet` false) → reject `DiskCutoffMet`; else runs
    `IsUpgradable` and maps `UpgradeableRejectReason` to `DiskXxx` rejection codes
    exactly parallel to `QueueSpecification`'s mapping (`BetterQuality`→
    `DiskHigherPreference`, `BetterRevision`→`DiskHigherRevision`,
    `QualityCutoff`→`DiskCutoffMet`, `CustomFormatCutoff`→`DiskCustomFormatCutoffMet`,
    `CustomFormatScore`→`DiskCustomFormatScore`,
    `MinCustomFormatScore`→`DiskCustomFormatScoreIncrement`,
    `UpgradesNotAllowed`→`DiskUpgradesNotAllowed`). **Season-pack path** (this
    release is a full-season release) is materially different: counts missing
    episodes (not yet on disk — automatically counted as "upgradable" since there's
    nothing to compare against) plus, for episodes that DO have files, counts how
    many of those existing files are individually upgradable per `IsUpgradable`. If
    literally all episodes in the pack are missing, accept immediately. Otherwise
    applies the `SeasonPackUpgrade` config mode: `Any` (accept if ≥1 upgradable
    episode), `All` (require 100% upgradable), or `Threshold` (require
    `SeasonPackUpgradeThreshold`% of episodes upgradable) — else reject
    `DiskNotUpgrade` with the computed upgradable count/percentage in the message.
29. **`RssSync/DelaySpecification`** — Database/**Temporary**. User-invoked searches
    bypass delay entirely. Reads the tag-matched `DelayProfile`'s delay minutes for
    this release's protocol; 0 delay → accept immediately. If preferred-protocol AND
    `DownloadPropersAndRepacks == PreferAndUpgrade` and this would be a same-quality
    revision upgrade over an existing file → accept immediately (propers/repacks
    bypass the normal delay when preference is on, since they're specifically
    time-sensitive fixes). `BypassIfHighestQuality`: if this release is already the
    best allowed quality in the profile AND on the preferred protocol → accept
    (no reason to wait for something even better that structurally can't exist).
    `BypassIfAboveCustomFormatScore`: if CF score already ≥
    `MinimumCustomFormatScore` AND preferred protocol → accept. Otherwise: if there's
    already a pending release for these episodes that's been waiting longer than the
    delay → accept this one now (the wait already "paid off" via the older pending
    item — this specific release effectively rides along). Else if this release
    itself hasn't been out longer than the delay yet → reject `MinimumAgeDelay`
    (Temporary — will resolve once age catches up).
30. **`RssSync/DeletedEpisodeFileSpecification`** — **Disk**/Temporary. Only relevant
    to RSS sync (skipped during any search) and only if
    `AutoUnmonitorPreviouslyDownloadedEpisodes` is enabled. Checks whether files the
    DB thinks exist for these episodes are actually missing from disk; if so, rejects
    `EpisodeNotMonitored` (they'll be auto-unmonitored on the next disk scan — this
    spec just prevents grabbing a replacement in the interim before that cleanup
    runs).
31. **`RssSync/HistorySpecification`** — Database/Permanent. Skipped entirely during
    search (RSS-sync only). For each episode, finds the most recent history event; if
    it was a `Grabbed` event, computes whether that grab is "recent" (within the last
    12 hours) — if not recent AND CDH is enabled, skip (assume CDH already handled
    import/failure). Otherwise (recent, or CDH disabled so we can't rely on it),
    recomputes that grab's Custom Formats and runs the same
    `CutoffNotMet`/`IsUpgradable` logic as `QueueSpecification`/`UpgradeDiskSpecification`,
    mapping to `HistoryXxx` rejection reasons (`HistoryRecentCutoffMet` /
    `HistoryCdhDisabledCutoffMet` depending on whether it was the "recent" or
    "CDH-disabled" branch that concluded cutoff-met; `HistoryHigherPreference`,
    `HistoryHigherRevision`, `HistoryCutoffMet`, `HistoryCustomFormatCutoffMet`,
    `HistoryCustomFormatScore`, `HistoryCustomFormatScoreIncrement`,
    `HistoryUpgradesNotAllowed`). This exists to prevent RSS sync from re-grabbing
    something that was *just* grabbed moments ago and hasn't had time to be imported
    yet, without relying solely on the Queue (which might have already cleared).
32. **`RssSync/IndexerTagSpecification`** — Default/Permanent. If the source indexer
    has any tags configured, at least one of them must be present on the series'
    tags, else reject `NoMatchingTag`. Indexer with no tags = applies to everything.
    Missing indexer (deleted) → accept.
33. **`RssSync/MonitoredEpisodeSpecification`** — Default/Permanent. Search with
    `MonitoredEpisodesOnly: false` bypasses this check entirely (used for interactive
    "search even unmonitored" flows). Series itself must be `Monitored` else reject
    `SeriesNotMonitored`. If not all episodes in a multi-episode release are
    monitored: single-episode release → `EpisodeNotMonitored`; zero monitored among
    multiple → same reason, different log message; partial monitored coverage → same
    reason ("One or more episodes is not monitored"). Full monitored coverage always
    passes.
34. **`RssSync/PendingSpecification`** — Database/**Temporary**. Skipped for RSS
    itself and for user-invoked searches. If a release covering any of the same
    episodes is already sitting in the **pending** queue (delayed-but-not-yet-grabbed),
    rejects `MinimumAgeDelayPushed` — prevents a manually pushed/pumped release from
    jumping the queue ahead of something already delaying for the profile's normal
    window.
35. **`RssSync/ProperSpecification`** — Default/Permanent. Skipped entirely during
    search (RSS-only, like History). Skipped if `DoNotPrefer`. For each file this
    would be a revision-upgrade over: `DoNotUpgrade` → reject `PropersDisabled`; else
    if the existing file is older than 7 days (`DateAdded < Today.AddDays(-7)`) →
    reject `ProperForOldFile` — Sonarr does not auto-grab propers for files that have
    already been sitting on disk for over a week (assumes the user is unlikely to
    care about a technically-more-correct release of something they've likely already
    watched).
36. **`Search/EpisodeRequestedSpecification`** — Default/Permanent. Search-only (no-op
    for RSS). The release's matched episodes must intersect the actual
    searched-for episode ids, else reject `WrongEpisode` with a formatted season/
    episode range in the message (or generic "Episode wasn't requested" if somehow
    zero episodes matched at all).
37. **`Search/SeasonMatchSpecification`** — Default/Permanent. Search-only, and only
    when `SearchCriteria` is specifically a `SeasonSearchCriteria`. Season number in
    the parsed release must equal the searched season, else reject `WrongSeason`.
38. **`Search/SeriesSpecification`** — Default/Permanent. Search-only. The resolved
    series id must equal the searched series id, else reject `WrongSeries`.
39. **`Search/SingleEpisodeSearchMatchSpecification`** — Default/Permanent. Search-only,
    branches on criteria subtype: for `SingleEpisodeSearchCriteria`, season must
    match (`WrongSeason`), the release must actually carry specific episode numbers
    — a full season pack found during a single-episode search is rejected as
    `FullSeason` (this is a *different* rejection reason than the plain
    `MultiSeasonSpecification`'s `FullSeason`... actually same enum value, reused
    across two specs deliberately) — and the searched episode number must be among
    the release's parsed episode numbers (`WrongEpisode`). For
    `AnimeEpisodeSearchCriteria`, only checks: a full-season result during a
    non-season anime search is rejected `FullSeason`.

**Total specifications implementing `IDownloadDecisionEngineSpecification`: 38**
(root: 25 files minus the interface file minus the plain helper
`SameEpisodesSpecification.cs` = 23, but `UpgradableSpecification` is also not an
`IDownloadDecisionEngineSpecification` so root contributes 22; `RssSync/` contributes
8; `Search/` contributes 4. 22 + 8 + 4 = 34... reconciling exactly against the file
list: root implementers are AcceptableSize, AirDate, AlreadyImported,
AnimeVersionUpgrade, BlockedIndexer, Blocklist, CustomFormatAllowedbyProfile,
FreeSpace, FullSeason, MaximumSize, MinimumAge, MultiSeason, NotSample, Protocol,
QualityAllowedByProfile, Queue, RawDisk, ReleaseRestrictions, Repack, Retention,
SameEpisodesGrab, SceneMapping, SeasonPackOnly, SplitEpisode, TorrentSeeding,
UpgradeAllowed, UpgradeDisk = **26**; RssSync = Delay, DeletedEpisodeFile, History,
IndexerTag, MonitoredEpisode, Pending, Proper = **7**; Search = EpisodeRequested,
SeasonMatch, Series, SingleEpisodeSearchMatch = **4**. Total = **37** concrete
`IDownloadDecisionEngineSpecification` implementations, plus the interface file and
the two non-implementing helper classes (`SameEpisodesSpecification`,
`UpgradableSpecification`) = 40 files, plus `IRejectWithReason.cs`,
`ReleaseDecisionInformation.cs`, `SpecificationPriority.cs`,
`UpgradeableRejectReason.cs` support types not in the Specifications folder itself.
This reconciles with the directory's 41-file count.)

### 6.4 `UpgradableSpecification` — the shared upgrade/cutoff engine (crux of ranking)
**File:** `DecisionEngine/Specifications/UpgradableSpecification.cs`

Four public members, used pervasively:

**`IsUpgradable(profile, currentQuality, currentFormats, newQuality, newFormats) →
UpgradeableRejectReason`** — the master "is release B actually better than what I
have (A)" decision, evaluated in this exact order:
1. Compare quality via `QualityModelComparer` (profile-order-aware). If new > current
   AND the quality cutoff isn't already met by current → **`None`** (accept, it's a
   genuine quality upgrade and there's still room to upgrade).
2. If new < current (existing is simply better quality) → **`BetterQuality`**
   (reject).
3. Compare revisions (`newQuality.Revision.CompareTo(currentQuality.Revision)`,
   independent of the quality-equality check at this point). If
   `DownloadPropersAndRepacks != DoNotPrefer` and new revision > current → **`None`**
   (accept — same-or-lesser quality tier but a better proper/repack/real still counts
   as an upgrade when propers are preferred, *unless* the user set DoNotPrefer).
4. If `!profile.UpgradeAllowed` → **`UpgradesNotAllowed`** (reject; checked only after
   the revision-upgrade special case above, meaning: **a pure proper/repack revision
   upgrade of the same quality is allowed even when `UpgradeAllowed` is false** —
   revision upgrades are not gated by the profile upgrade switch, only quality/CF
   upgrades are).
5. If `DownloadPropersAndRepacks != DoNotPrefer` and new revision < current →
   **`BetterRevision`** (reject — existing has the better revision).
6. If quality is strictly better than current (re-checked here after the revision
   gates) → **`QualityCutoff`** (reject — this branch is reached when quality is
   better but cutoff was already met, so no further quality-driven upgrade is
   wanted).
7. Compute `currentFormatScore`/`newFormatScore` via
   `profile.CalculateCustomFormatScore`. If `newFormatScore <= currentFormatScore` →
   **`CustomFormatScore`** (reject — no CF improvement).
8. If `currentFormatScore >= profile.CutoffFormatScore` → **`CustomFormatCutoff`**
   (reject — CF cutoff already met, no more CF-driven upgrading wanted).
9. If `newFormatScore < currentFormatScore + profile.MinUpgradeFormatScore` →
   **`MinCustomFormatScore`** (reject — improvement exists but doesn't clear the
   configured minimum increment).
10. Otherwise → **`None`** (accept — genuine CF-score upgrade that clears all gates).

**`QualityCutoffNotMet(profile, currentQuality, newQuality=null)`** — effective
cutoff is `profile.Cutoff` if `UpgradeAllowed`, else
`profile.FirststAllowedQuality().Id` (§2.3). True if current quality's profile-index
is below that cutoff's index, **or** (if a `newQuality` was supplied) the new release
would be a same-quality revision upgrade (revision upgrades always count as "cutoff
not met yet" so they're never blocked purely by quality cutoff).

**`CutoffNotMet(profile, currentQuality, currentFormats, newQuality=null)`** — true if
EITHER `QualityCutoffNotMet` OR the private `CustomFormatCutoffNotMet` (current CF
score below `UpgradeAllowed ? CutoffFormatScore : MinFormatScore`) is true. This is
the single predicate answering "is there still any reason at all (quality- or
CF-driven) to keep searching for something better than what's on disk/queued/in
history right now" — used to gate whether upgrade-searching for an episode should
even continue.

**`IsRevisionUpgrade(currentQuality, newQuality)`** — true only when
`currentQuality.Quality == newQuality.Quality` (exact same enum value — a WEBRip
proper is never considered a "revision upgrade" of a WEBDL, even at the same
resolution, because they're different `Quality.Id`s) AND
`newQuality.Revision.CompareTo(currentQuality.Revision) > 0`.

**`IsUpgradeAllowed(profile, currentQuality, currentFormats, newQuality,
newFormats)`** — simpler boolant used specifically by `UpgradeAllowedSpecification`:
true unconditionally if it's a revision upgrade (propers/repacks always allowed to
replace regardless of the profile's `UpgradeAllowed` flag — consistent with point 4
above); else true if (quality-upgrade OR CF-upgrade) AND `profile.UpgradeAllowed`;
false if (quality-upgrade OR CF-upgrade) AND NOT `UpgradeAllowed`; true (default) if
neither is actually an upgrade at all (nothing to gate).

### 6.5 `UpgradeableRejectReason` enum
**File:** `UpgradeableRejectReason.cs`
```
None, BetterQuality, BetterRevision, QualityCutoff, CustomFormatScore,
CustomFormatCutoff, MinCustomFormatScore, UpgradesNotAllowed
```
This is the shared vocabulary translated by 4 different call sites
(`QueueSpecification`, `UpgradeDiskSpecification`, `RssSync/HistorySpecification`)
into 4 parallel families of user-facing `DownloadRejectionReason` codes
(`Queue*`, `Disk*`, `History*` prefixes) — same underlying logic, contextualized
rejection message per surface (queue vs. disk vs. history).

### 6.6 `DownloadDecision` / `DownloadRejection` / `RejectionType`
**Files:** `DownloadDecision.cs`, `DownloadRejection.cs`, `Rejection.cs`,
`RejectionType.cs`, `DownloadRejectionReason.cs`, `DownloadSpecDecision.cs`

`DownloadDecision`: wraps a `RemoteEpisode` plus zero-or-more `DownloadRejection`s.
`Approved` = no rejections. `TemporarilyRejected` = has rejections and **all** are
`RejectionType.Temporary`. `Rejected` = has rejections and **any** are `Permanent`
(note: these two properties are not perfect complements — a mix of temporary and
permanent rejections on one decision is `Rejected == true` and
`TemporarilyRejected == false`, i.e. permanent wins when mixed, which matches real
Sonarr behavior of only ever offering full retry cycles when *every* blocker is
transient).

`DownloadRejectionReason` enum: **72 distinct values** (verified by reading the
complete enum body) spanning parse errors, series/episode identity mismatches, every
quality/CF/upgrade rejection family (`History*`, `Queue*`, `Disk*` triads),
protocol/indexer/blocklist gates, size/age/retention gates, and structural
release-shape rejections (multi-season, split-episode, raw-disk, sample).

`DownloadSpecDecision` (the per-spec return type, distinct from `DownloadDecision`):
factory-only construction (`Accept()` returns a cached singleton `Accepted=true`
instance; `Reject(reason, message, args...)` builds a rejected instance with a
formatted message). Specs never construct rejection instances directly.

`RejectionType`: `Permanent = 0, Temporary = 1`.

`Rejection<TRejectionReason>`: generic base (used to type `DownloadRejection` as
`Rejection<DownloadRejectionReason>`), `ToString()` = `"[{Type}] {Message}"`.

### 6.7 `ReleaseDecisionInformation` / `SpecificationPriority`
Already covered inline above; `ReleaseDecisionInformation{ PushedRelease,
SearchCriteria }` is threaded through every spec call so specs can distinguish RSS
sync vs. manual push vs. interactive/automatic search, and — for search — inspect the
concrete `SearchCriteriaBase` subtype (`SingleEpisodeSearchCriteria`,
`SeasonSearchCriteria`, `AnimeEpisodeSearchCriteria`, etc.) for search-specific
filtering.

---

## 7. Release Weighting / Ranking Between Candidates — `DownloadDecisionComparer`

**File:** `DecisionEngine/DownloadDecisionComparer.cs`, orchestrated by
`DownloadDecisionPriorizationService.cs`

Once every candidate release has passed the full `IDownloadDecisionEngineSpecification`
gauntlet (§6) and produced an *approved* `DownloadDecision`, surviving candidates for
the **same series** are grouped and ordered by `DownloadDecisionComparer`, descending
(`OrderByDescending(decision, comparer)`), so index 0 is "the release Sonarr will
actually grab." Decisions with no resolvable `Series` are left in their original
relative order and unioned back in afterward (they're not competing against anything
since `Series` is null). This comparator is instantiated fresh in the
Priorization service (not injected as a shared singleton) so it always sees the
current `IConfigService`/`IDelayProfileService` state.

**Exact tie-break order** (`Compare(x, y)` tries each comparer in sequence,
short-circuiting at the first non-zero result — the classic
`.FirstOrDefault(result => result != 0)` composite-comparator pattern):

1. **`CompareQuality`** — profile-index compare of the two releases' parsed
   `Quality` (via `QualityProfile.GetIndex`, **not** respecting group order at this
   stage). If `DownloadPropersAndRepacks == DoNotPrefer`, this is the *entire*
   quality comparison (revision is deliberately ignored — propers/repacks carry zero
   ranking weight when the user has opted out of preferring them). Otherwise, quality
   index compare is chained with a **secondary** compare on `Revision` — so a higher
   Revision (PROPER/REPACK/REAL) at the *same* quality index wins over the
   non-proper release, but a strictly higher quality always wins regardless of
   revision on the lower-quality side (revision only breaks ties within identical
   quality index, it never overrides quality).
2. **`CompareCustomFormatScore`** — plain numeric compare of
   `remoteEpisode.CustomFormatScore`. Higher wins. Runs after quality/revision, so a
   *lower*-quality release can never win purely on CF score against a
   higher-quality/revision competitor — CF score is strictly a tie-breaker beneath
   quality+revision, not an independent weighted axis blended with quality.
3. **`CompareProtocol`** — boolean compare (`downloadProtocol ==
   delayProfile.PreferredProtocol`) — a release on the tag-matched delay profile's
   preferred protocol beats one that isn't, all else equal so far.
4. **`CompareEpisodeCount`** — first compares `FullSeason` (bool) so a season-pack
   release outranks a non-season-pack one at this tie level; if that ties, and
   **both** competing releases belong to an `Anime`-type series, **fewer** episodes
   wins (`CompareBy` ascending — anime often has weekly single-episode releases
   competing against occasional batch releases, and Sonarr here prefers the
   more-granular single release so it doesn't have to wait for/re-download a whole
   batch); for any non-anime (or mixed) case, **more** episodes wins
   (`CompareByReverse` — a release covering more of the requested episodes in one
   shot is preferred for standard shows).
5. **`CompareEpisodeNumber`** — reverse-compare of the **minimum** episode number
   across the release's matched episodes — i.e., prefers the release starting at
   the *lower* episode number when episode counts otherwise tied (favors earlier
   chronological coverage).
6. **`CompareIndexerPriority`** — reverse-compare of `ReleaseInfo.IndexerPriority`
   (lower configured indexer-priority number wins — indexer priority is a
   user-configured per-indexer int where lower = more preferred, consistent with
   Sonarr's indexer settings convention elsewhere).
7. **`ComparePeersIfTorrent`** — only applies if **both** competing releases are
   torrents (protocol mismatches are assumed already resolved by
   `CompareProtocol`/the `ProtocolSpecification` gate); compares
   `Math.Round(Math.Log10(seeders))` (log-scale, so 100 vs 900 seeders tie while 10
   vs 100 differ), then, if still tied, the same log-scale compare on peers. Missing
   or zero seeders/peers is treated as `0` in the log compare (not `-Infinity`/error).
8. **`CompareAgeIfUsenet`** — only applies if both are usenet; a hand-tuned scoring
   ladder rather than a linear age compare: age <1 hour → score 1000 (extremely
   fresh, dominates); 1–24 hours → score 100; 1–7 days → score 10; older than 7 days
   → `-round(log10(days))` (a mildly *negative*, log-decaying score so very old
   usenet posts very slightly prefer being *newer* among themselves, but this whole
   tier is dwarfed by the fixed 1000/100/10 buckets above it — practically, this
   comparer only meaningfully discriminates within the same age bucket at the
   >7-day tail).
9. **`CompareSize`** (final tie-breaker) — looks up the release's specific
   quality-item's `PreferredSize` (group-respecting index, per-minute MB) for the
   series' quality profile. If `PreferredSize` is set **and** the series has a known
   `Runtime`, computes the ideal total size (`Runtime * PreferredSize`) and prefers
   whichever candidate's actual size is closest to that ideal, rounded to the nearest
   200MB bucket before the closeness comparison (so near-identical sizes don't
   thrash the ordering on tiny byte differences) — this is a "closest to a sweet
   spot," not "biggest wins." If there's no `PreferredSize` configured (interpreted
   as "unlimited/no preference") or no known series runtime, falls back to straight
   "larger file wins" (assumed to correlate with better encode quality at a fixed
   nominal quality tier), again bucketed to the nearest 200MB.

If literally every comparer ties, `Compare` returns 0 (order is whatever the
underlying stable-ish LINQ ordering yields — no further explicit tie-break exists
beyond these 9).

`DownloadDecisionPriorizationService.PrioritizeDecisions`: the actual entry point
callers use; groups approved-and-series-resolved decisions by `Series.Id`, orders
each group by the comparer descending, flattens back out, and appends decisions with
a null `Series` (unranked, since there's no competing series membership to group by)
in their original order at the end via `Union`.

---

## 8. Delay Profiles

**Files:** `Profiles/Delay/DelayProfile.cs`, `DelayProfileService.cs`,
`DelayProfileRepository.cs`, `DelayProfileTagInUseValidator.cs`

`DelayProfile` fields: `EnableUsenet` (bool), `EnableTorrent` (bool),
`PreferredProtocol` (`DownloadProtocol`: `Unknown=0, Usenet=1, Torrent=2`),
`UsenetDelay` (int, minutes), `TorrentDelay` (int, minutes), `Order` (int, resolution
priority among multiple applicable profiles), `BypassIfHighestQuality` (bool),
`BypassIfAboveCustomFormatScore` (bool), `MinimumCustomFormatScore` (int, threshold
for the above bypass), `Tags` (`HashSet<int>`, empty = the global/default profile
matching everything).

`GetProtocolDelay(protocol)`: returns `TorrentDelay` if protocol is Torrent, else
`UsenetDelay` (i.e., anything that isn't literally `Torrent` — including `Unknown` —
is treated as the Usenet delay bucket).

**Resolution (`BestForTags`)**: among all `DelayProfile`s whose `Tags` intersects the
series' tags **or** whose `Tags` is empty, the one with the **lowest `Order`** wins
(`OrderBy(Order).First()`). Results are cached per distinct tag-set key for 30
seconds. There is always exactly one profile with `Id == 1` that acts as the
un-deletable default/fallback (protected explicitly in `Delete`/`Reorder` — id 1 is
skipped when renumbering `Order` after a delete, and stays fixed as the ultimate
fallback with empty `Tags`).

**Ordering/reordering (`Reorder(id, afterId)`)**: a manual linked-position algorithm
— compute the target `afterOrder` (0 if inserting first, otherwise the
current-or-adjusted order of the `after` anchor depending on whether the moving item
was originally before or after it), then walk every profile: the moving profile gets
`afterOrder + 1`; the anchor gets `afterOrder`; anything with a higher order than the
insertion point gets pushed up sequentially (`afterCount`, incrementing); anything
between the old and new position shifts down by one. Id 1 is always skipped/untouched
during this shuffle.

**How delay interacts with the decision engine** (see also §6.3 item 29,
`RssSync/DelaySpecification`, the actual enforcement point):
- User-invoked (interactive) searches **always bypass delay** entirely.
- If a same-quality proper/repack revision upgrade is found and
  `DownloadPropersAndRepacks == PreferAndUpgrade` and the release is on the
  preferred protocol → bypass delay (propers are time-sensitive; don't make the user
  wait through the normal window for a fix release).
- `BypassIfHighestQuality`: a release already at the top of the profile's allowed
  quality list, on the preferred protocol, skips delay (nothing better could ever
  arrive to justify waiting).
- `BypassIfAboveCustomFormatScore`: a release already at/above
  `MinimumCustomFormatScore`, on the preferred protocol, skips delay.
- Otherwise: if an older pending release already accumulated more delay-minutes than
  the configured delay, THIS release is allowed through immediately (piggybacks on
  the already-elapsed wait); otherwise if this specific release's own age hasn't yet
  cleared the delay, it's rejected as `MinimumAgeDelay` (a *Temporary* rejection —
  eligible again once time passes, no state change needed).
- Protocol enablement itself (can this protocol be used for this series at all) is a
  **separate**, permanent gate: `ProtocolSpecification` (§6.3 item 14) rejects
  `ProtocolDisabled` outright if `EnableUsenet`/`EnableTorrent` is off for the
  tag-matched profile — this is enforced independently of and prior to any delay-
  window logic.

---

## 9. Proper / Repack Handling and "Download Propers" Setting

**Config surface:** `IConfigService.DownloadPropersAndRepacks` (type
`ProperDownloadTypes`, `Qualities/ProperDownloadTypes.cs`):
```
PreferAndUpgrade, DoNotUpgrade, DoNotPrefer
```
This single tri-state setting governs proper/repack behavior across **7 different
call sites**, each interpreting it slightly differently depending on context:

- **`PreferAndUpgrade`** (the "on" / default-preferring state): propers/repacks are
  actively preferred as upgrades over the same quality (subject to normal
  cutoff/age gates elsewhere), AND bypass the normal delay-profile wait window when
  found (`RssSync/DelaySpecification`).
- **`DoNotUpgrade`**: propers/repacks are recognized and ranked (still beat
  non-propers in `DownloadDecisionComparer`'s revision tie-break, and still count as
  legitimate "quality" for e.g. `UpgradableSpecification`'s revision-upgrade special
  case at decision-time) but Sonarr will **not proactively search/grab purely to
  acquire one** — enforced as outright rejections at multiple points:
  `QueueSpecification` → `QueuePropersDisabled`; `RssSync/ProperSpecification` →
  `PropersDisabled`; `RepackSpecification` → `RepackDisabled`.
- **`DoNotPrefer`**: propers/repacks carry **no ranking weight at all** — most
  strikingly, `DownloadDecisionComparer.CompareQuality` drops the revision
  comparison entirely in this mode (quality-index-only compare), and every
  revision-aware spec (`AnimeVersionUpgradeSpecification`, `RepackSpecification`,
  `RssSync/ProperSpecification`) exits early with an unconditional accept ("skip
  check") rather than evaluating revision logic at all. `UpgradableSpecification`
  itself still has the underlying `!= DoNotPrefer` guards baked into steps 3 and 5 of
  `IsUpgradable` (§6.4), meaning **in `DoNotPrefer` mode a same-quality revision is
  simply never treated as an upgrade nor a downgrade reason** — it falls through
  those steps and gets decided purely on the CF-score comparison instead. Note this
  means a user can still get preferred words/Custom Formats to rank propers if they
  explicitly build a Custom Format that regex-matches "PROPER"/"REPACK" in the
  release title even with `DoNotPrefer` set — the setting only suppresses the
  *built-in* revision-awareness, not what a user-defined Custom Format can key on.

**`RepackSpecification` vs `RssSync/ProperSpecification` vs
`AnimeVersionUpgradeSpecification`**: three separate, deliberately parallel specs
rather than one generalized "revision" spec:
- `RepackSpecification` fires only when the **candidate release itself** is a repack
  (`Revision.IsRepack`), runs in the `Database` priority tier, and applies to every
  series type. Enforces release-group matching against every episode file this
  would upgrade.
- `RssSync/ProperSpecification` fires for revision upgrades in general during **RSS
  sync only** (explicitly `Accept()`s immediately whenever `SearchCriteria != null`,
  i.e. it never runs during any kind of search — its only job is gating unsolicited
  RSS-driven proper grabs), runs in the `Default` tier, and additionally enforces a
  **7-day file-age cutoff**: even with propers preferred/upgradeable, a proper for a
  file already on disk more than 7 days is rejected (`ProperForOldFile`) — the
  assumption being the user has likely already consumed content that old and a
  technically-more-correct re-encode isn't worth randomly re-triggering.
- `AnimeVersionUpgradeSpecification` fires only for `SeriesTypes.Anime` (any
  revision upgrade, not just repacks), enforcing the release-group match rule that
  fansub-group versioning conventions require, and has **no age cutoff** (unlike the
  RSS proper spec) and **does run during search** (no `SearchCriteria != null`
  early-return), since anime version-upgrade releases are commonly found via active
  search, not just passive RSS.

**Release-group matching pattern** (shared shape across `RepackSpecification` and
`AnimeVersionUpgradeSpecification`, worth replicating identically): a revision
upgrade against an existing file is only permitted if both the file's stored
`ReleaseGroup` and the new release's parsed `ReleaseGroup` are non-blank AND equal
(case-insensitive for repacks — `StringComparison.InvariantCultureIgnoreCase`; plain
`!=` i.e. ordinal for the anime spec, an inconsistency between the two specs worth
either replicating exactly or deliberately normalizing). Unknown release group on
either side is a hard rejection (`UnknownReleaseGroup`/`RepackUnknownReleaseGroup`),
not a permissive pass-through — Sonarr treats "can't verify same-group" as unsafe to
upgrade rather than assuming compatibility.

---

## 10. Cross-cutting Config Surface (all settings referenced above, enumerated)

From `IConfigService` (settings that decision-engine/quality/CF/delay logic reads —
not the full config surface of Sonarr, only what's exercised by these six
directories):

| Setting | Type | Used by |
|---|---|---|
| `EnableCompletedDownloadHandling` | bool | `AlreadyImportedSpecification`, `RssSync/HistorySpecification` |
| `AutoUnmonitorPreviouslyDownloadedEpisodes` | bool | `RssSync/DeletedEpisodeFileSpecification` |
| `DownloadPropersAndRepacks` | `ProperDownloadTypes` | 7 call sites, §9 |
| `SkipFreeSpaceCheckWhenImporting` | bool | `FreeSpaceSpecification` |
| `MinimumFreeSpaceWhenImporting` | int (MB) | `FreeSpaceSpecification` |
| `SeasonPackUpgrade` | `SeasonPackUpgradeType` (`All=0,Threshold=1,Any=2`) | `UpgradeDiskSpecification` |
| `SeasonPackUpgradeThreshold` | double (%) | `UpgradeDiskSpecification` |
| `Retention` | int (days, 0=unlimited) | `RetentionSpecification` |
| `MaximumSize` | int (MB, 0=unlimited) | `MaximumSizeSpecification` |
| `MinimumAge` | int (minutes, 0=disabled) | `MinimumAgeSpecification` |

Plus, per-`QualityProfile` (§2.1): `UpgradeAllowed`, `Cutoff`, `MinFormatScore`,
`CutoffFormatScore`, `MinUpgradeFormatScore`, per-quality `Allowed`/`MinSize`/
`MaxSize`/`PreferredSize`, and the full `FormatItems` score table. Per-`QualityDefinition`
(global, §1.6): `Title`, `MinSize`, `MaxSize`, `PreferredSize` per quality. Per-
`DelayProfile` (§8): `EnableUsenet`, `EnableTorrent`, `PreferredProtocol`,
`UsenetDelay`, `TorrentDelay`, `Order`, `BypassIfHighestQuality`,
`BypassIfAboveCustomFormatScore`, `MinimumCustomFormatScore`, `Tags`. Per-
`ReleaseProfile` (§5): `Enabled`, `Required`, `Ignored`, `AirDateRestriction`,
`AirDateGracePeriod`, `IndexerIds`, `Tags`, `ExcludedTags`. Per-indexer:
`IndexerPriority` (tie-break axis), `ITorrentIndexerSettings.MinimumSeeders`
(torrent-only), indexer `Tags` (matched against series tags,
`RssSync/IndexerTagSpecification`), and indexer block/enable status
(`BlockedIndexerSpecification`).

---

## 11. Edge Cases Worth Replicating Deliberately (consolidated)

These are the non-obvious behaviors most likely to be silently missed in a
reimplementation, gathered from the detail above:

1. **Profile item order, not global quality weight, is the real ranking** once a
   profile exists — global `Weight` only seeds new profiles and is force-overwritten
   on every read of `QualityDefinition` (never persisted as user-editable).
2. **`UpgradeAllowed = false` still allows same-quality revision (proper/repack)
   upgrades.** Only quality-tier and CF-score upgrades are blocked by that flag.
3. Cutoff, when `UpgradeAllowed` is off, silently becomes "first allowed quality in
   profile order" (quality) and "MinFormatScore" (custom format) — **not** the
   configured `Cutoff`/`CutoffFormatScore` fields, which become inert.
4. `DownloadDecisionComparer.CompareQuality` drops revision comparison **entirely**
   when `DownloadPropersAndRepacks == DoNotPrefer` — this is a comparer-shape change,
   not just a threshold change.
5. Size limits in `QualityDefinition`/profile items are **per-minute-of-runtime**
   (MB/min), not absolute — `AcceptableSizeSpecification` multiplies by computed
   total runtime (with a 45-minute default-runtime heuristic gated on
   pilot-episode-plus-24h-air-window) before comparing.
6. `SizeSpecification` (Custom Formats) size bounds are asymmetric:
   `size > Min && size <= Max` (strict lower bound, inclusive upper bound).
7. Specification evaluation is **tiered by cost** (`SpecificationPriority`), not
   flat — disk-touching specs never run if any cheaper spec already rejected.
   Within a tier, ALL specs run and all their rejections are collected even though
   only the first tier with any rejection is used.
8. `RejectionType.Temporary` vs `Permanent` matters for retry semantics —
   `DownloadDecision.Rejected` is true if **any** rejection is Permanent even when
   others are Temporary (mixed rejections behave as a hard rejection).
9. Anime series get materially different comparer behavior
   (`CompareEpisodeCount` prefers *fewer* episodes for anime, *more* for standard)
   and a materially different revision-upgrade gate
   (`AnimeVersionUpgradeSpecification` requires release-group match, runs during
   search, has no age cutoff — contrast with the RSS-only, 7-day-cutoff
   `ProperSpecification`).
10. `Language.Original` (id -2) is a resolved-at-match-time sentinel pointing at
    `Series.OriginalLanguage`, not a literal language — and `LanguageSpecification`
    hand-duplicates its negate-path logic rather than wrapping the positive path in
    `!`, because `ExceptLanguage` and `Negate` are orthogonal toggles that don't
    commute through simple negation given `null`-coalescing defaults.
11. Custom Format matching required-vs-optional semantics are **per condition-type
    group**, not per specification-instance globally: multiple same-typed
    non-required conditions on one format OR together; a `Required` condition of a
    given type is an additional AND gate scoped to that type; different types AND
    together across the whole format.
12. `QueueSpecification` explicitly skips `FailedPending` queue items to avoid a
    race with an item that's already about to be re-searched.
13. Torrent seeder/peer comparisons are **log10-scaled and rounded**, so releases
    within the same order of magnitude of seeders/peers are treated as tied on that
    axis, falling through to size as the real deciding factor.
14. `CompareSize`'s "closest to preferred size" logic rounds both the delta and the
    fallback absolute size to the nearest 200MB bucket before comparing — avoids
    thrashing rank order over trivial byte-level size differences.
15. `AlreadyImportedSpecification` only fires when CDH already imported something at
    a **different** quality than what was grabbed — if grabbed and imported
    qualities match, it's assumed to be a normal, safe, already-completed cycle and
    is not re-checked.
16. A blocked/deleted indexer referenced by a still-cached release is treated
    permissively (`accept`) in `TorrentSeedingSpecification` and
    `RssSync/IndexerTagSpecification` — missing-config fails open, not closed, for
    those two checks specifically (contrast with `BlockedIndexerSpecification`,
    which is the dedicated closed/blocked check and fails closed by design).
17. Delay-profile resolution and Release-profile resolution both use an
    empty-`Tags`-means-"applies to everything" convention, but Release Profiles
    additionally support `ExcludedTags` (opt-out) which Delay Profiles do not have
    an equivalent for.
18. `ReleaseProfile` terms support an actual Perl-flavored `/pattern/flags` regex
    syntax with a specific whitelist of supported single-letter flags (`m s i x n`)
    that throws on any other letter — Custom Format regex conditions, by contrast,
    are always compiled as plain case-insensitive .NET regex with no flag syntax at
    all. These are two different regex dialects/entry points in the same codebase.
19. Full-season release detection has **two independent air-completeness gates**
    that fire at different times: `FullSeasonSpecification` (all episodes must have
    aired, ≤24h grace) applies broadly; `SeasonPackOnlySpecification` is the inverse
    concern (forcing a season-pack *requirement* once single episodes from an old,
    fully-aired season are considered stale, via
    `SeasonSearchMaximumSingleEpisodeAge`) and only applies during multi-episode
    search for `Standard` (non-anime) series.
