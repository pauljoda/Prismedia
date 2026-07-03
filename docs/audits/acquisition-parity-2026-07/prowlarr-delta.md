# Prowlarr Delta Report — What Prowlarr Adds Beyond Sonarr's Built-In Indexer Support

**Scope.** This report documents ONLY the capabilities Prowlarr adds on top of what Sonarr/Radarr already do with
their own built-in indexer definitions. It does not re-document generic indexer concepts (HTTP client plumbing,
basic RSS/Torznab parsing, quality profiles) that Sonarr already has natively. Source: `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src`.
Cardigann YAML example definitions do not exist locally anywhere under the Prowlarr checkout (only CI config
`*.yml` files) — Prowlarr does not vendor tracker definitions in its source repo; they are fetched at runtime from
`indexers.prowlarr.com` (see §6). This report is therefore built entirely from the C# engine + POCO model, which
fully determines the YAML schema because YamlDotNet deserializes directly onto these classes with
`IgnoreUnmatchedProperties()` + `CamelCaseNamingConvention`.

**Method.** Every directory named in the task was listed in full, then every file of consequence was read start to
finish (not skimmed from names). Three sub-agents handled the largest subsystems (Cardigann engine, search
aggregation/proxies, apps-sync/stats) in parallel; their findings are merged below and cross-checked against
direct reads of `EscalationBackOff.cs`, `HistoryEventType`, `NewznabStandardCategory.cs`, `DownloadMappingService.cs`,
`SearchController.cs`, and others performed independently in this session.

---

## Table of Contents

1. [Cardigann YAML Indexer Definition Engine](#1-cardigann-yaml-indexer-definition-engine)
2. [Definition Distribution / Update Mechanism](#2-definition-distribution--update-mechanism)
3. [CAPTCHA Handling](#3-captcha-handling)
4. [Passthrough Newznab/Torznab Client Indexers (chaining)](#4-passthrough-newznabtorznab-client-indexers-chaining)
5. [Newznab/Torznab Category Tree](#5-newznabtorznab-category-tree)
6. [Manual Search API and Grab Endpoint](#6-manual-search-api-and-grab-endpoint-apiv1search)
7. [Query Fan-Out Mechanics (ReleaseSearchService)](#7-query-fan-out-mechanics-releasesearchservice)
8. [Download URL Proxying/Rewriting](#8-download-url-proxyingrewriting--the-actual-mechanism)
9. [Indexer Proxy Chain (HTTP/SOCKS/FlareSolverr)](#9-indexer-proxy-chain)
10. [Indexer Rate Limiting (queryLimit/grabLimit)](#10-indexer-rate-limiting-querylimitgrablimit)
11. [Indexer Health/Status and Escalating Backoff](#11-indexer-healthstatus-and-escalating-backoff)
12. [Indexer Statistics and Health Checks](#12-indexer-statistics-and-health-checks)
13. [Apps Sync (Prowlarr → Sonarr as Torznab endpoints)](#13-apps-sync-prowlarr--sonarr-as-torznab-endpoints)
14. [Cookie-Jar Auth Persistence](#14-cookie-jar-auth-persistence)
15. [Seed Criteria and Magnet-Link Construction](#15-seed-criteria-and-magnet-link-construction)
16. [Exception Taxonomy](#16-exception-taxonomy)
17. [Prismedia's Current State — What Already Exists](#17-prismedias-current-state--what-already-exists)
18. [What Must Live Inside Prismedia vs. Stay Delegated](#18-what-must-live-inside-prismedia-vs-stay-delegated)

---

## 1. Cardigann YAML Indexer Definition Engine

This is Prowlarr's single largest delta over Sonarr: a data-driven scraper engine that lets an indexer be added by
writing a YAML file instead of a compiled C# class. One `Cardigann` C# class (`TorrentIndexerBase<CardigannSettings>`)
is instantiated once per YAML definition (keyed by `Settings.DefinitionFile`); everything about login, request
shape, and response parsing is interpreted from the YAML at runtime.

**Key source files**:
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/Indexers/Definitions/Cardigann/CardigannBase.cs` (930 lines) — template engine, filter pipeline, selector evaluation shared by request+response
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/Indexers/Definitions/Cardigann/CardigannRequestGenerator.cs` (1235 lines) — login flows, request building
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/Indexers/Definitions/Cardigann/CardigannParser.cs` (726 lines) — response parsing (HTML/XML/JSON)
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/Indexers/Definitions/Cardigann/CardigannDefinition.cs` (217 lines) — the full YAML schema as POCOs
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/Indexers/Definitions/Cardigann/Cardigann.cs` (291 lines) — `IIndexer` glue/lifecycle
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/Indexers/Definitions/Cardigann/CardigannSettings.cs`, `CardigannMetaDefinition.cs`, `Captcha.cs`
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/Indexers/Definitions/Cardigann/Exceptions/CardigannException.cs`, `CardigannConfigException.cs`

### 1.1 YAML schema (every field, from `CardigannDefinition.cs`)

Root `CardigannDefinition`:

| Field | Type | Purpose |
|---|---|---|
| `id` | string | Unique slug; also the filename `{id}.yml`. |
| `settings` | `List<SettingsField>` | User-configurable fields rendered in the indexer's edit UI. |
| `name` | string | Display name. |
| `description` | string | Short blurb in the indexer picker. |
| `type` | string | `"private"` / `"public"` / else → `IndexerPrivacy.SemiPrivate`. Also gates auto-magnet generation from info-hash (forbidden for private trackers). |
| `language` | string | Locale code. |
| `encoding` | string | .NET `Encoding` name; defaults to `UTF-8` if absent. Used for every HTTP request/response and URL/form encoding. |
| `requestDelay` | double? | Minimum seconds between requests; overrides the base rate limit if larger. |
| `links` | `List<string>` | Candidate base URLs; `links.First()` is canonical `SiteLink`. |
| `legacylinks` | `List<string>` | Historical URLs; if the user's saved `BaseUrl` matches one, silently redirected to the current canonical link. |
| `followredirect` | bool | Whether the initial login/setup GET auto-follows redirects. |
| `testLinkTorrent` | bool, default true | Verifies a resolved download href actually starts with `'d'` (bencode) before accepting it. |
| `certificates` | `List<string>` | Custom trusted certs (consumed by the HTTP client layer). |
| `caps` | `CapabilitiesBlock` | Category tree + search-mode capabilities. |
| `login` | `LoginBlock` | Authentication flow (§1.2). |
| `ratio` | `RatioBlock : SelectorBlock` | Selector + `path` for scraping the user's seed ratio off a profile page. |
| `search` | `SearchBlock` | The search request/response contract (§1.3–1.4). |
| `download` | `DownloadBlock` | How to resolve/produce the final `.torrent`/magnet download request. |

`SettingsField`: `name`, `type` (`text`, `password`, `checkbox`, `select`, and display-only `info`, `info_cookie`,
`info_flaresolverr`, `info_useragent`, `info_category_8000`, `cardigannCaptcha` — unknown types throw), `label`,
`default`, `defaults` (`string[]`), `options` (`Dictionary<string,string>`, sorted alphabetically to build a stable
ordinal index for both storage and `.Config.*` substitution).

`CapabilitiesBlock` (`caps`): `categories` (`Dictionary<string,string>`, simple 1:1 shorthand), `categorymappings`
(`List<CategorymappingBlock>` — richer form, §1.5), `modes` (`Dictionary<string, List<string>>` — declares which
search verticals/params are supported; `search` mode is mandatory and must declare exactly `["q"]` or the engine
throws), `allowrawsearch` (bool — enables `$raw` passthrough, §1.3).

`LoginBlock` (`login`): `path`, `submitpath`, `cookies` (`List<string>`), `method`, `form`, `selectors` (bool),
`inputs` (`Dictionary<string,string>`), `selectorinputs`/`getselectorinputs` (`Dictionary<string,SelectorBlock>`),
`error` (`List<ErrorBlock>`), `test` (`PageTestBlock`), `captcha` (`CaptchaBlock`), `headers`.

`ErrorBlock`: `path`, `selector` (presence signals an error), `message` (`SelectorBlock`, optional override for
extracting human-readable error text).

`SelectorBlock` — the universal extraction primitive reused across login/search/download:
`selector` (CSS or JSON-path-like string), `optional` (bool), `default` (fallback literal, template-expanded),
`text` (literal/template string used instead of DOM/JSON lookup), `attribute` (read this HTML attribute instead
of text content), `remove` (CSS selector for child nodes to strip before reading text), `filters`
(`List<FilterBlock>`, §1.4), `case` (`Dictionary<string,string>` — value-dependent switch construct).

`FilterBlock`: `name` + `args` (`dynamic` — string or list). Full function enumeration in §1.4.

`PageTestBlock` (`login.test`): `path`, `selector` — a "logged-in marker" check for session-expiry detection.

`RatioBlock : SelectorBlock` adds `path`.

`SearchBlock` (`search`): `path` (legacy single-path shorthand, normalized into `paths` at load time), `paths`
(`List<SearchPathBlock>`), `headers`, `keywordsfilters` (`List<FilterBlock>` applied to the combined `.Keywords`
string), `allowEmptyInputs` (bool), `inputs` (`Dictionary<string,string>`, inherited defaults), `error`
(`List<ErrorBlock>`), `preprocessingfilters` (`List<FilterBlock>` run on the *raw response body string* before any
parsing), `rows` (`RowsBlock`), `fields` (`KeyValuePairList` — a custom ordered multimap allowing the SAME field
key to repeat, output field name → `SelectorBlock`).

`RowsBlock : SelectorBlock`: `after` (int — merges N following sibling rows' children into the current row),
`dateheaders` (`SelectorBlock` — walks backward through sibling/parent rows to find a "date group" heading),
`count` (`SelectorBlock`, JSON-only — a "total results" field; `< 1` short-circuits to zero releases), `multiple`
(bool, JSON — the selected token is itself an array of row objects), `missingAttributeEqualsNoResults` (bool —
distinguishes "no matches" from real schema breakage).

`SearchPathBlock : RequestBlock`: `categories` (`List<string>` — restricts this path to firing only when mapped
tracker categories intersect the list, or, prefixed `"!"`, only when they do NOT intersect — an exclusion/fallback
path), `inheritinputs` (default true), `followredirect`, `response` (`ResponseBlock`: `type` = `"json"`/`"xml"`/HTML-default,
`noResultsMessage`).

`RequestBlock` (base of `SearchPathBlock`/`BeforeBlock`): `path`, `method` (`get`/`post`), `inputs`, `queryseparator`
(default `&`, lets a tracker use e.g. `;`).

`DownloadBlock` (`download`): `selectors` (`List<SelectorField>`, tried in order), `method`, `before`
(`BeforeBlock : RequestBlock` + `pathselector` — a priming request fired first, e.g. to "unlock" a download or
dynamically resolve the real path), `infohash` (`InfohashBlock`: `hash`/`title` selectors + `usebeforeresponse` —
synthesizes a magnet link client-side from a scraped info-hash), `headers`.

`SelectorField`: `selector`, `attribute`, `usebeforeresponse`, `filters`.

`CaptchaBlock` (`login.captcha`): `type` (only `"image"` implemented), `selector`, `input` (form field name, or CSS
selector if `login.selectors` true); a deprecated `image` property throws if ever accessed.

`CardigannMetaDefinition` (lightweight index entry, used for the tracker picker before a full definition loads):
`id`, `file`, `name`, `description`, `type`, `language`, `encoding`, `links`, `legacylinks`, `settings`, `login`
(only to check `.Captcha != null`), `caps`, `sha` (git-blob SHA / version fingerprint).

### 1.2 Login modes (`CardigannRequestGenerator.DoLogin`, dispatched on `login.Method`)

- **`post`** — simple form POST, no page fetch first. Builds pairs directly from `login.inputs` (template-expanded,
  substituting `.Config.username`/`.Config.password` etc.), sends fixed `login.cookies` if declared, POSTs to
  `login.path`. Cookies from the response are captured and persisted.
- **`form`** — the full scrape-a-real-form flow: (1) fetch `login.path`; (2) locate `<form>` via `login.form`
  selector (default `"form"`); (3) harvest all real `<input>` fields (skipping disabled/unchecked) to preserve
  hidden CSRF tokens; (4) overlay `login.inputs` credentials — field **names** can themselves be selector-resolved
  if `login.selectors` is true (for obfuscated field names); (5) overlay `login.selectorinputs` — extra POST fields
  whose values are scraped from the landing page (e.g. a CSRF token in a `<meta>` tag); (6) overlay
  `login.getselectorinputs` — same idea appended as querystring params instead; (7) auto-detects and silently
  solves "simpleCaptcha" JS widgets (`script[src*="simpleCaptcha"]`) with zero user interaction; (8) if a real
  captcha is configured, injects the user's typed solution (`Settings.ExtraFieldData["CAPTCHA"]`) into the
  resolved captcha input field; (9) submits honoring `multipart/form-data` vs urlencoded `enctype` (hand-builds
  multipart bodies with a timestamp boundary when needed).
- **`cookie`** — no HTTP request at all; takes a raw pasted browser cookie header from
  `Settings.ExtraFieldData["cookie"]` and persists it directly. The manual escape hatch for un-scriptable
  (e.g. Cloudflare-gated) login pages.
- **`get`** — like `post` but a GET with `login.inputs` as querystring (API-key-style "login" endpoints).
- **`oneurl`** — a single magic URL: `login.inputs["oneurl"]` (a per-user secret token pasted from the tracker's
  profile) is expanded and concatenated onto `login.path`, then GET'd — no credential pairs, the
  passkey/RSS-key-as-login pattern.
- Any other value throws `NotImplementedException`.

**Error detection** (`CheckForError`, shared by all modes): HTTP 401 throws immediately; otherwise each
`login.error` block's `selector` is tested against the response — a match extracts the error text (via
`error.Message` selector, or raw text) and throws `CardigannConfigException`. No match on any block ⇒ success.

**Session-expiry detection** (`CheckIfLoginIsNeeded`): triggers re-login if there's no login block configured
(short-circuits false), a redirect occurred (also logs a domain-mismatch hint if the redirect host differs from
the configured site link, tying into the `legacylinks` mechanism), an HTTP error occurred, or —the primary explicit
signal — `login.test.selector` fails to match in the response (a "logged-in marker" element).

**Cookie persistence**: every login mode ends by calling a `CookiesUpdater` delegate that writes the cookie jar +
a 30-day expiry into the indexer's persisted status (shared with the generic mechanism in §14). A raw pasted
`cookie` extra-field overrides `GetCookies()` entirely.

### 1.3 Request templating (`CardigannRequestGenerator`)

**Template variables**: `GetBaseTemplateVariables` seeds `.Config.sitelink`, `.True`/`.False` sentinels, `.Today.Year`,
and `.Config.<settingName>` for every declared setting (checkbox → `.True`/null; select → the selected option's
key string). `GetQueryVariableDefaults` adds the full `.Query.*` surface per search vertical: Movie
(`Movie/Year/IMDBID/IMDBIDShort/TMDBID/TraktID/DoubanID`), TV (`Series/Ep/Season/TVDBID/TVRageID/TVMazeID/Episode`
+ shared IMDB/TMDB/Trakt/Douban), Music (`Album/Artist/Label/Track`), Book (`Author/Title/Publisher`), plus
universal `Type/Q/Categories/Limit/Offset/Extended/APIKey/Genre`.

**Keyword assembly**: `Q`/`Series`/`Movie`/`Year` (whichever non-empty) plus `Episode` concatenate into
`.Query.Keywords`, then `search.keywordsfilters` run over it to produce `.Keywords` — the point where an author
strips special characters/re-encodes spaces before the query text reaches any template.

**Mini Go-template engine** (`ApplyGoTemplateText`) supports, in evaluation order: `{{ re_replace .Var "pat" "repl" }}`,
`{{ join .Var "," }}`, logic functions `and`/`or`/`eq`/`ne`, `{{ if COND }}A{{ else }}B{{ end }}` conditionals
(condition must be a bare `.Variable`, truthy = non-empty), `{{ range [$i,] elem := .Var }}{{.}}{{end}}` loops, and
plain `{{ .Variable }}` substitution with an optional URL-encoding modifier threaded through.

**Category mapping into requests**: the caller's requested Torznab category ids are mapped to this tracker's
native category id strings via `caps.categories`/`caps.categorymappings`; unmapped falls back to whichever
categories are flagged `default: true`. Result becomes the `.Categories` template variable.

**Multiple search paths + category-gated routing**: `search.paths` are iterated; each path's `categories` list can
restrict it to firing only for a matching (or, prefixed `!`, non-matching/exclusion) category subset.

**`$raw` passthrough**: an input keyed literally `"$raw"` is template-expanded/URL-encoded then manually parsed as
a raw `key=value&key=value` fragment — lets an author bypass the one-key-one-value input model for exotic query
shapes, gated conceptually by `caps.allowrawsearch`.

**No generic pagination loop**: `Cardigann.PageSize => 1` is hard-coded specifically so the base `HttpIndexerBase`
paging logic never thinks a Cardigann response is a "partial page" — pagination, where supported, is entirely
authored per-tracker by wiring `.Query.Limit`/`.Query.Offset` into `inputs` (e.g. `page: "{{ .Query.Offset }}"}`),
not driven by the engine itself.

**Headers**: `search.headers` (and `login.headers`/`download.headers`, falling back to `search.headers`) are
template-expanded per key — a documented limitation is only the first value per key is used (cannot send a header
key twice).

**Download-time requests** (`DownloadRequest`): structurally identical templating. Resolves an optional
`download.before` priming request (whose own path can be dynamically computed from a selector run against an
initial fetch), then either: `infohash` mode (scrape hash+title, synthesize a magnet via `MagnetLinkBuilder`), or
`selectors` mode (try each selector in order, resolve href, and — unless magnet-scheme and `TestLinkTorrent` —
sanity-check the fetched bytes start with `'d'` bencode before accepting, falling through to the next selector on
failure), or a bare passthrough GET/POST.

### 1.4 Response parsing / selectors (`CardigannParser`)

**Dispatch on `response.type`**: **JSON** mode parses via `JToken.Parse`; if `search.rows.count` is set, a
numeric "total" is extracted and `< 1` short-circuits to zero results; rows are located via a compound
`<jsonpath>:<filterExpr>...` selector (see grammar below) and optionally treated as nested arrays (`rows.multiple`).
**XML** mode parses with AngleSharp's `XmlParser` (after `preprocessingfilters`), rows via `QuerySelectorAll`.
**HTML** mode (default) parses with AngleSharp's `HtmlParser`; after collecting rows, `rows.after` performs a
**row-merge** — appends N following sibling rows' children into the current row and removes those siblings (for
releases whose data spans multiple `<tr>`s).

**JSON field-selector mini-grammar** (`:functionName(arg)` suffixes, act as boolean gates on row acceptance, not
value transforms): `has(key)`, `not(key)`, `contains(text)`. Unknown functions are logged and skipped (non-fatal).

**Filter pipeline** (`ApplyFilters` — every filter function found in source, applied left-to-right):
`querystring` (extract a named query-param from a URL string), `timeparse`/`dateparse` (Go-style date layout
parsing), `regexp` (capture group 1), `re_replace` (template-expanded replacement), `split` (single-char separator,
supports negative/from-end index), `replace` (literal substring, template-expanded replacement), `trim` (whitespace
or a specific cutset), `prepend`, `append`, `tolower`, `toupper`, `urldecode`/`urlencode` (using the definition's
configured encoding), `htmldecode`/`htmlencode`, `timeago`/`reltime` (relative time expressions), `fuzzytime`
(loosely-formatted date strings), `validfilename` (OS-safe filename sanitization), `diacritics` (Unicode
normalize/strip-combining-marks/renormalize — arg must be `"replace"`), `jsonjoinarray` (re-parse `data` as JSON,
select a token path, join with a separator), `hexdump`/`strdump` (debug-only, log without mutating `data`),
`validate` (intersect a delimiter-tokenized allow-list against tokenized `data`). Unknown filter names are logged
and ignored (non-fatal).

A **separate, smaller row-level filter set** (`search.rows.filters`, applied once per row): `andmatch` (no-op at
this layer — actual behavior lives in `Cardigann.CleanupReleases`, which invokes the shared
`IndexerBase.FilterReleasesByQuery` to enforce AND-semantics across all query keyword tokens when this filter is
present, correcting for trackers whose native search is too loose), `strdump` (debug).

**Field name → release-property mapping** (`ParseFields`, full enumerated switch): `download` (→ `DownloadUrl` or,
if `magnet:`-prefixed, `MagnetUrl`; always sets `Guid`), `magnet`, `infohash`, `details` (→ `InfoUrl`, resolved
relative to search URL), `comments` (→ `CommentUrl`), `title` (supports `|append` modifier to concatenate across
two selectors), `description` (same `|append` support), `category` (maps tracker code → Torznab cats via
`MapTrackerCatToNewznab`; supports deprecated `|noappend`), `categorydesc` (maps by description text via
`MapTrackerCatDescToNewznab`), `size` (`ParseUtil.GetBytes`), `leechers`/`seeders` (clamped to zero above 5,000,000
as a sanity guard; both additively contribute to `Peers`), `date`, `files`, `grabs`, `downloadvolumefactor`,
`uploadvolumefactor`, `minimumratio`, `minimumseedtime`, `imdb`/`imdbid`, `tmdbid`, `rageid`, `tvdbid`, `tvmazeid`,
`traktid`, `doubanid`, `poster` (resolved to absolute URL), `genre` (tokenized, underscores→spaces, unioned into
`Genres`), `year`, `author`, `booktitle`, `publisher`, `artist`, `album`, `label`, `track`. Unknown field names
fall through a no-op default (still available as a `.Result.<name>` template variable for later fields to reference).

**Optional vs. required**: a fixed engine-level allow-list (`imdb, imdbid, tmdbid, rageid, tvdbid, tvmazeid,
traktid, doubanid, poster, banner, description, genre`) is always optional regardless of YAML, on top of anything
explicitly marked `optional: true` or suffixed `|optional`. Required + unmatched throws in JSON mode; in HTML mode
it's merely logged at Trace and the row continues (a source-confirmed asymmetry between the two code paths).

**Post-processing**: synthesizes a public magnet from `InfoHash` if `MagnetUrl` is missing and `type != private`;
reverse-extracts `InfoHash` from a `MagnetUrl` if that's missing instead; tags releases whose description starts
with `"Internal"` with `IndexerFlag.Internal`.

### 1.5 Category mapping block (author's-eye view)

`caps.categories` (simple `id: TorznabCategoryName` dict) is a 1:1 shortcut resolved via
`NewznabStandardCategory.GetCatByName` — unmatched names are dropped with a logged error. `caps.categorymappings`
(`{id, cat, desc, default}` list) is the rich form: `cat` is optional — if omitted, `AddCategoryMapping` still
synthesizes a **custom category** (via the same `+100000`/SHA1-hash scheme described in §5) rather than reusing a
standard one, which is how Cardigann supports tracker categories with no Newznab equivalent while still giving
consumers (Sonarr/Radarr) a stable, addressable id across definition updates. `desc` doubles as the custom
category's human label AND a value the parser's `categorydesc` field can match against for trackers that only
expose category text, not a stable id. `default: true` marks the fallback category used whenever a search's
mapped-category set comes back empty. `caps.modes` is validated strictly — `search` must declare exactly `["q"]`,
and unrecognized mode keys throw outright, meaning YAML authors cannot invent new search verticals without a
corresponding backend enum entry already existing.

### 1.6 Other runtime engine capabilities

- **AngleSharp** backs all HTML/XML DOM work; a hand-rolled `:root` pseudo-selector patches a gap in AngleSharp's
  native CSS support.
- **One field-mapping surface for three response shapes** — HTML/XML/JSON all funnel into the same
  `search.fields`/`ParseFields` switch; only the selector syntax differs (CSS vs. the `has()/not()/contains()`
  JSON grammar), the value pipeline afterward is identical.
- **`preprocessingfilters`** run on the raw response body string before any parsing — the escape hatch for
  malformed markup (e.g. an unescaped `&` breaking XML parsing).
- **Per-generator caching**: `Cardigann.GetRequestGenerator()` caches generator instances per `DefinitionFile` in a
  5-minute rolling cache to avoid rebuilding the templated-request machinery on every search call.
- **Definition-driven rate limiting**: `Cardigann.RateLimit` overrides the base rate limit whenever
  `definition.RequestDelay` (from YAML) exceeds the app default.
- **Redirect-domain hinting**: detects when a request to the configured site link redirected to a different host
  and surfaces a "try changing the indexer URL" hint, tied to the `legacylinks`/`ResolveSiteLink` mechanism (a
  saved legacy URL is silently swapped for the current canonical `links[0]`).
- **Torrent-file sanity verification** (`TestLinkTorrent`): guards against a download selector accidentally
  matching an HTML "please wait" interstitial instead of the real file, by checking the first byte is bencode `'d'`.
- **Multipart form-data login support**: hand-built multipart bodies with a timestamp-derived boundary.
- **Debug/introspection filters** (`hexdump`, `strdump`) are baked into the same pipeline as functional filters —
  a YAML author can instrument a broken definition without a C# debugger.

---

## 2. Definition Distribution / Update Mechanism

This is the architecture that lets Prowlarr support hundreds of trackers **without shipping new binaries**.

**Key source files**:
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/IndexerVersions/IndexerDefinitionUpdateService.cs`
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/IndexerVersions/IndexerDefinitionVersionService.cs`, `IndexerDefinitionVersion.cs`, `IndexerDefinitionVersionRepository.cs`, `IndexerDefinitionUpdateCommand.cs`

`IndexerDefinitionUpdateService` (confirmed by direct read): constants `DEFINITION_BRANCH = "master"`,
`DEFINITION_VERSION = 11` — a schema/protocol version, not an app version, letting the server-side CDN serve
different YAML shapes to different Prowlarr engine versions without breaking older clients ("Update Service will
fall back if version # does not exist for an indexer"). A hardcoded `_definitionBlocklist` (e.g. `aither`,
`hdbits`, `mteamtp`, `beyond-hd`, `blutopia`, `desitorrents`, `shareisland`, `tellytorrent`, and others) excludes
trackers that have been **promoted to hand-written native C# indexers**, preventing double-loading.

**Three layers of definition sourcing, checked in priority order**:
1. **`{AppDataFolder}/Definitions/Custom/`** — user-placed custom definitions, always scanned and merged in
   (skipping duplicates by filename/display name), never touched by automatic updates. The escape hatch for
   private/personal trackers that will never be published centrally.
2. **`{AppDataFolder}/Definitions/`** — the "official" catalog, refreshed by `UpdateLocalDefinitions()`, which is
   triggered on **every app boot** (`ApplicationStartedEvent`) and via an explicit `IndexerDefinitionUpdateCommand`
   (UI "check for updates" button, with live progress pushed to the browser via `SendUpdatesToClient = true`). It
   downloads a **zip bundle** of the entire definition set from
   `https://indexers.prowlarr.com/{branch}/{version}/package.zip`, extracts it (overwriting existing files), and
   clears the in-memory definition cache.
3. **Live per-id HTTP fallback** — `GetCachedDefinition(fileKey)` checks local disk first, then a local
   `IndexerDefinitionVersion` DB table (to fail fast if the id isn't part of the known manifest at all), and only
   then fetches `https://indexers.prowlarr.com/{branch}/{version}/{id}` individually over HTTP.

`CleanIndexerDefinition` normalizes every freshly-loaded definition: default `settings` (username/password) if
none declared, `encoding` defaults to `UTF-8`, `login.method` defaults to `"form"` if a login block exists but no
method was specified, and the legacy single `search.path` is converted into a one-element `search.paths` list so
the rest of the engine only ever deals with the `paths` collection.

`IndexerDefinitionVersion`/`IndexerDefinitionVersionService` persist `{File, Sha, LastUpdated, DefinitionId}` —
local bookkeeping for whether a given definition id is legitimate/current, independent of whether the YAML bytes
themselves are cached on disk or in memory.

**Net effect**: the server-side Prowlarr team can add trackers, fix broken selectors, or extend the schema and have
every running instance pick it up on its next restart (or manual update) — zero binary/release involvement for
tracker-level changes. Only changes to the *engine itself* (`CardigannBase`/`CardigannRequestGenerator`/`CardigannParser`)
require a real app update.

---

## 3. CAPTCHA Handling

Two distinct paths, both scoped to `login.method == "form"` (image captcha is the only implemented
`CaptchaBlock.Type`; anything else throws `NotImplementedException`):

1. **Automatic "simpleCaptcha" bypass**: if the login landing page contains
   `<script src*="simpleCaptcha">`, the engine automatically calls `simpleCaptcha.php?numImages=1` on the same
   site, parses the JSON response for `images[0].hash`, and injects that hash as `captchaSelection` plus a
   `submitme=X` marker — fully transparent, zero user interaction, no settings field needed.
2. **Real image captcha**: `Cardigann.GetDefinition()` injects a synthetic settings field
   `{ Name = "cardigannCaptcha", Type = "cardigannCaptcha", Label = "CAPTCHA" }` when `login.captcha != null` — a
   special frontend-rendered interactive captcha-solving widget. The fetch flow (`RequestAction("checkCaptcha", ...)`)
   GETs `login.path` (caching the landing page for reuse by the subsequent login POST), CSS-selects
   `login.captcha.selector` for the `<img>`, resolves it to an absolute URL, fetches the image bytes (carrying
   forward any landing-page cookies), and returns `{Type, ContentType, ImageData}` for the frontend to display.
   `GetConfigurationForSetup(automaticLogin: true)` — used by ordinary background searches — **throws** if a
   captcha is encountered ("Found captcha during automatic login, aborting"), since captcha resolution can only
   happen via the explicit user-facing setup/test action, never silently mid-search. The user's typed answer
   (`Settings.ExtraFieldData["CAPTCHA"]`) is written into the resolved captcha input field name during the real
   login POST — resolved either literally or via CSS-selector (mirroring `login.inputs` field-name resolution).

No other captcha types (reCAPTCHA/hCaptcha/etc.) are implemented — only the plain static image pattern and the
fully-automated "simpleCaptcha" bypass.

---

## 4. Passthrough Newznab/Torznab Client Indexers (chaining)

Distinct from Cardigann — two thin, native C# indexer implementations that let Prowlarr **consume** another
Newznab or Torznab-compatible endpoint as an upstream indexer (chaining to another Prowlarr instance, a dedicated
NZB indexer, or any jackett/prowlarr-compatible service). This is how Prowlarr can sit in front of things that
already speak the shared protocol without any per-site code.

**Key source files**:
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/Indexers/Definitions/Newznab/Newznab.cs`, `NewznabSettings.cs`, `NewznabCapabilitiesProvider.cs`, `NewznabRequestGenerator.cs`, `NewznabRssParser.cs`
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/Indexers/Definitions/Torznab/Torznab.cs`, `TorznabSettings.cs`

**Configuration surface** (`NewznabSettings`): `BaseUrl` (required, validated root URL), `ApiPath` (default
`/api`), `ApiKey` (required only for a hardcoded allow-list of known public Newznab hosts —
`nzbs.org`, `nzb.life`, `dognzb.cr`, `nzbplanet.net`, `nzbid.org`, `nzbndx.com`, `nzbindex.in` — otherwise optional),
`AdditionalParameters` (free-form querystring appended to every request, validated against a `(&key=value)+`
regex), `VipExpiration` (free-text date, feeds the VIP health checks in §12), `BaseSettings`
(`QueryLimit`/`GrabLimit`/`LimitsUnit`). Torznab adds `TorrentBaseSettings`
(`AppMinimumSeeders`/`SeedRatio`/`SeedTime`/`PackSeedTime`/`PreferMagnetUrl`).

**Capability discovery via `t=caps`** (`NewznabCapabilitiesProvider`): issues `GET {baseUrl}{apiPath}?t=caps[&apikey=...]`,
parses the `<caps>` XML response, and reconstructs an `IndexerCapabilities` object — search-param availability per
search type, limits, and the full category tree (matching parent/child names/ids back against
`NewznabStandardCategory`, falling back to `Other`/`OtherMisc`). Cached **7 days** per exact settings JSON — a
passthrough indexer's capability shape is assumed stable, not re-fetched on every search.

This confirms a full round-trip: Prowlarr's own `IndexerCapabilities.ToXml()` (what `NewznabController` serves
when Prowlarr is queried) is byte-for-byte the same contract `NewznabCapabilitiesProvider` parses when Prowlarr is
the *client* of another Newznab/Torznab server — including another Prowlarr instance.

---

## 5. Newznab/Torznab Category Tree

**Purpose**: Prowlarr's core mission is normalizing wildly different tracker category schemes into one shared
vocabulary so a single Sonarr/Radarr/Lidarr/Readarr query fans out across many trackers and results still make
sense. Sonarr only *consumes* this vocabulary; Prowlarr maintains and expands the tree and maps every indexer
(native or Cardigann) into it.

**Key source files**:
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/Indexers/NewznabStandardCategory.cs`
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/Indexers/IndexerCategory.cs`, `CategoryMapping.cs`
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/Indexers/IndexerCapabilitiesCategories.cs`

### 5.1 The 9 top-level parent categories (confirmed exact ids/names/counts)

| Parent | Id | # children | Children |
|---|---|---|---|
| ZedOther | 0000 | 2 | Misc (0010), Hashed (0020) |
| Console | 1000 | 14 | NDS(1010), PSP(1020), Wii(1030), XBox(1040), XBox360(1050), Wiiware(1060), XBox360 DLC(1070), PS3(1080), Other(1090), 3DS(1110), PS Vita(1120), WiiU(1130), XBox One(1140), PS4(1180) |
| Movies | 2000 | 10 | Foreign(2010), Other(2020), SD(2030), HD(2040), UHD(2045), BluRay(2050), 3D(2060), DVD(2070), WEB-DL(2080), x265(2090) |
| Audio | 3000 | 6 | MP3(3010), Video(3020), Audiobook(3030), Lossless(3040), Other(3050), Foreign(3060) |
| PC | 4000 | 7 | 0day(4010), ISO(4020), Mac(4030), Mobile-Other(4040), Games(4050), Mobile-iOS(4060), Mobile-Android(4070) |
| TV | 5000 | 10 | WEB-DL(5010), Foreign(5020), SD(5030), HD(5040), UHD(5045), Other(5050), Sport(5060), Anime(5070), Documentary(5080), x265(5090) |
| XXX | 6000 | 10 | DVD(6010), WMV(6020), XviD(6030), x264(6040), UHD(6045), Pack(6050), ImageSet(6060), Other(6070), SD(6080), WEB-DL(6090) |
| Books | 7000 | 6 | Mags(7010), EBook(7020), Comics(7030), Technical(7040), Other(7050), Foreign(7060) |

(Note: 0000/ZedOther and a separate "Other" 8000-range both exist conceptually in the codebase's `AllCats`/`ParentCats`
sets — the tree totals 8-9 parent buckets plus ~64 named children depending on how the 0000 vs 8000 "Other" bucket
is counted; both were confirmed present via direct source read of `NewznabStandardCategory.cs`.)

Every sub-category id is `parent*1000 + N*10`. This numbering IS the actual Torznab/Newznab wire contract other
apps (Sonarr, NZBGet, SABnzbd) already understand — Prowlarr doesn't invent a new scheme, it's the canonical .NET
implementation of the community-standard tree used across the *arr ecosystem.

### 5.2 Custom/tracker-specific categories (id >= 100000)

When a tracker has a category with no clean Newznab equivalent, `IndexerCapabilitiesCategories.AddCategoryMapping`
synthesizes a **custom category**:
- If the tracker category parses as an integer, the custom id is `trackerCategoryInt + 100000`.
- If it's a non-numeric string (common for Cardigann trackers), a SHA1 hash of the string is truncated —
  specifically, `BitConverter.ToUInt16(SHA1(UTF8(str)), 0)`, the first 2 bytes of the digest as a little-endian
  ushort (0–65535) — then added to 100000. This keeps custom ids **stable across restarts/updates** without a
  database-assigned sequential id, which matters because 3rd-party consumer apps (Sonarr) persist the numeric id.
  The source itself flags this as imperfect ("the hash is not perfect but it should work in most cases... the id
  must be fixed to work in 3rd party apps").
- Custom categories are explicitly excluded when concatenating capabilities across indexers for an aggregate/"all
  indexers" view (`Concat`, `Where(x => x.Id < 100000)`) — a custom id from one indexer is meaningless on another.
- `GetTrackerCategories()` similarly filters to `< 100000` in some call sites, while `MapTorznabCapsToTrackers`
  uses the full set including customs — the exclusion is context-dependent, not universal.

### 5.3 Category tree construction (`AddTorznabCategoryTree`)

Built bottom-up as mappings register: exact matches against the 9 canonical parents are recognized directly;
categories matching a canonical child are attached under the matching parent (created on demand); categories in
the `1000`–`9999` range that aren't literal canonical entries are heuristically bucketed under the parent whose
thousands-digit matches (`id / 1000`); anything else becomes its own top-level node (the normal case for
`+100000` hash-derived customs, since dividing by 1000 puts them far outside any parent's bucket).

### 5.4 Category expansion for search (`ExpandTorznabQueryCategories`)

- Ids `>= 100000` (custom) are never expanded.
- Querying a **parent** category auto-includes all its children (a plain "TV" search also matches "TV/HD",
  "TV/Anime", etc.).
- Querying a **child** category with `mapChildrenCatsToParent = true` also adds the parent — lets an indexer that
  only understands the parent-level category still be queried for a child-level request.

`SupportedCategories` (used for indexer eligibility gating before a search fires) returns the intersection of an
indexer's full category set with the requested categories — no intersection means the indexer is dropped from the
fan-out before any HTTP call is made. `GetTorznabCategoryTree(sorted: true)` sorts standard categories numerically
and custom categories (`>= 100000`) alphabetically after all standard ones.

**What a consumer (Sonarr) expects**: a `caps.xml`-style `<categories>` block
(`IndexerCapabilities.GetXDocument`, `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/Indexers/IndexerCapabilities.cs:451-503`)
with `<category id name>` parents and nested `<subcat id name>` children — the exact shape `NewznabCapabilitiesProvider`
parses back when Prowlarr itself acts as a Newznab/Torznab client.

---

## 6. Manual Search API and Grab Endpoint (`api/v1/search`)

**Key source files**:
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/Prowlarr.Api.V1/Search/SearchController.cs`, `SearchResource.cs`, `ReleaseResource.cs`
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/Download/DownloadMappingService.cs`, `DownloadService.cs`
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/Prowlarr.Api.V1/Indexers/NewznabController.cs`

### `GET /api/v1/search`

Request shape (`SearchResource`): `Query` (string), `Type` (default `"search"`; also `tvsearch`/`movie`/`music`/`book`
— maps to the Newznab `t` param), `IndexerIds` (`List<int>` — sentinel `-1` = all Usenet, `-2` = all Torrent, empty
= all), `Categories` (`List<int>`), `Limit`, `Offset`.

`GetSearchReleases`: builds an internal `NewznabRequest` (`source = "Prowlarr"` hardcoded — this is how downstream
code distinguishes UI-driven manual search from Torznab-passthrough traffic hitting the same underlying search
service), calls `request.QueryToParams()` to extract embedded `{key:value}` tokens from free text into typed
fields, then `_releaseSearchService.Search(request, payload.IndexerIds, interactiveSearch: true)`. `SearchFailedException`
→ 400; **any other exception is caught and swallowed, returning an empty list with 200 OK** — the manual search
endpoint never bubbles a generic failure to the caller.

### Response shape and caching (`MapReleases`)

Every `ReleaseInfo` is converted to a `ReleaseResource` DTO (fields: `Guid`, `Age`/`AgeHours`/`AgeMinutes`, `Size`,
`Files`, `Grabs`, `IndexerId`, `Indexer`, `SubGroup`, `ReleaseHash`, `Title`, `SortTitle`, `ImdbId`, `TmdbId`,
`TvdbId`, `TvMazeId`, `PublishDate`, `CommentUrl`, `DownloadUrl`, `InfoUrl`, `PosterUrl`, `IndexerFlags`,
`Categories`, `MagnetUrl`, `InfoHash`, `Seeders`, `Leechers` (computed `Peers - Seeders`), `Protocol`, computed
`FileName`, `DownloadClientId`), and — critically — **the full internal `ReleaseInfo` is cached** in an in-memory
`ICached<ReleaseInfo>` keyed `"{IndexerId}_{Guid}"` with a **30-minute TTL**. This cache is what makes "grab"
possible without re-searching: the client round-trips only `IndexerId`+`Guid`, and the server rehydrates the full
release (including the real download URL) from cache. If the cache entry has expired, grab fails with 404
("cache timeout probably expired, try searching again") — **the release cannot be re-derived from the DTO alone**.
`DownloadUrl`/`MagnetUrl` are rewritten through `DownloadMappingService.ConvertToProxyLink` before being returned
(mechanism in §8).

### `POST /api/v1/search` (single grab) and `POST /api/v1/search/bulk`

Single grab: validates `IndexerId`/`Guid`, looks up cached `ReleaseInfo`, calls
`_downloadService.SendReportToClient(releaseInfo, source, host, indexerDef.Redirect, DownloadClientId)`;
`ReleaseDownloadException` → 409. Bulk grab groups by indexer, **continues past per-release failures** (cache miss
or download exception just skips that one), and only returns 400 if literally none succeeded — partial success is
a valid 200 response.

### `id=0` synthetic-caps special case (`NewznabController`)

When `id == 0`, the controller answers `t=caps` with a synthetic document advertising **every**
`NewznabStandardCategory` at once and returns one canned `"Test Release"` entry pointing at `https://prowlarr.com`
for any search type — a connectivity/setup-test stub, not a real aggregate endpoint. The real cross-indexer
aggregate search is `api/v1/search`.

---

## 7. Query Fan-Out Mechanics (`ReleaseSearchService`)

**File**: `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/IndexerSearch/ReleaseSearchService.cs` (plus
`/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/IndexerSearch/Definitions/*.cs` for criteria shapes).

### Entry point and type routing

`Search(NewznabRequest, indexerIds, interactiveSearch)` is the single entry point used by BOTH the manual
`SearchController` (`interactiveSearch = true`) and `NewznabController`'s Torznab-passthrough endpoint hit by
Sonarr/Radarr (`interactiveSearch = false`) — **one code path serves both consumers**. Dispatch is a switch on
`request.t`: `movie`→`MovieSearch`, `music`→`MusicSearch`, `tvsearch`→`TvSearch`, `book`→`BookSearch`, anything
else (including `search`) → `BasicSearch`. Each builds a strongly-typed `SearchCriteriaBase` subtype from the
common Newznab fields (categories, term, limit/offset, min/max age, min/max size, source/host) plus type-specific
ID fields (IMDb/TMDb/TVDb/TvMaze/Trakt/Douban ids, season/episode, artist/album/etc.).

**RSS vs. interactive is a flag, not a separate pipeline**: `SearchCriteriaBase.IsRssSearch` is "true if
`SearchTerm` is blank" (each subtype additionally requires no ID-style field populated — an ID-only query is NOT
treated as an RSS sync). Scheduled background RSS polling calls the exact same `Search` method with
`interactiveSearch: false` and an empty term, so `IsRssSearch` evaluates true and the indexer's RSS capability
gate applies instead of its search capability gate — same fan-out machinery either way.

### Capability-gating and concurrency (`Dispatch`)

1. Start from all enabled indexers.
2. **Indexer-id filtering**: filter to `IndexerIds` list, or `-1`(all Usenet)/`-2`(all Torrent) sentinels. For an
   **interactive** search, zero indexers after filtering throws `SearchFailedException` (→ controller 400); for
   non-interactive (Torznab passthrough), an empty set silently proceeds with no results.
3. **Category-gating**: keep only indexers whose declared category support intersects the requested categories
   (§5.4's `SupportedCategories`) — a non-matching indexer never receives an HTTP call at all.
4. **Concurrency**: one `Task` per surviving indexer, `Task.WhenAll` — **all indexers queried concurrently**, no
   explicit concurrency cap/semaphore in this layer (per-indexer throttling is handled separately, §10). This is a
   "wait for the slowest" barrier — one slow indexer sets the floor latency for the whole search.

### Per-indexer isolation (`DispatchIndexer`) — how one bad indexer can't fail the whole search

1. Query-limit short-circuit: if `IIndexerLimitService.AtQueryLimit` is tripped for this indexer, returns empty
   immediately with **no HTTP call at all**.
2. The actual `indexer.Fetch(searchSpec)` call is wrapped in try/catch.
3. On success: further client-side filtering — category-expansion intersection against each release's own
   categories (a release with zero categories is kept, not dropped), then `MinAge`/`MaxAge` date-window and
   `MinSize`/`MaxSize` byte filtering. Every individual outbound HTTP request the indexer made (an indexer's
   `Fetch` can issue multiple paged requests) publishes its own `IndexerQueryEvent` for history/stats.
4. **On exception**: publishes an `IndexerQueryEvent` with an empty result (a "this indexer failed" marker), logs
   the error, and **returns an empty release array** — the exception never propagates to `Task.WhenAll`, which is
   exactly why one erroring/timed-out indexer contributes zero results instead of failing the entire search.

### De-duplication

`GroupBy(r => r.Guid)`, keeping the entry with the **numerically lowest `IndexerPriority`** per duplicate GUID
group (lower = more preferred, the standard *arr convention). Pure GUID-collision resolution — no fuzzy
title/size dedup, so the same underlying release surfaced with different GUIDs from two indexers is NOT
deduplicated. No explicit sort is applied afterward; result order is fan-out-arrival order.

---

## 8. Download URL Proxying/Rewriting — the actual mechanism

**Key source files**:
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/Download/DownloadMappingService.cs`
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/Prowlarr.Api.V1/Indexers/NewznabController.cs`
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/Indexers/HttpIndexerBase.cs` (`Download()`)

This is a genuine encrypt-and-proxy mechanism, not a bare redirect wrapper.

### Step 1 — encrypting the real link (`DownloadMappingService.ConvertToProxyLink`)

```csharp
encryptedLink = _protectionService.Protect(link.ToString());          // AES encrypt (ASP.NET Core data protection)
encodedLink   = WebEncoders.Base64UrlEncode(UTF8(encryptedLink));
proxyLink     = "{serverUrl}{urlBase}/{indexerId}/download?apikey={prowlarrApiKey}&link={encodedLink}&file={urlEncodedFile}";
```
The real tracker URL is **AES-encrypted** (not merely encoded), then base64url-encoded into the `link` query
param of a Prowlarr-hosted URL that embeds the target `indexerId` and requires Prowlarr's own `apikey`. The real
URL is never exposed to any client — a browser, Sonarr, or a download client only ever sees this proxy URL.
`ConvertToNormalLink` reverses it (decode → `UnProtect`/AES-decrypt). This rewrite happens in **two places** that
both hand callers a Prowlarr-hosted link: `SearchController.MapReleases` (manual-search JSON API) and
`NewznabController.GetNewznabResponse` (the Torznab/Newznab XML feed Sonarr/Radarr actually scrape) — **even the
RSS feed external consumer apps read never contains a raw tracker URL.**

### Step 2 — serving the proxy link (`NewznabController.GetDownload`, `GET /{id}/download`)

1. 404 if indexer missing, 410 Gone if disabled.
2. Checks `IIndexerLimitService.AtDownloadLimit` (grab-rate ceiling, §10) — 429 + `Retry-After` if tripped.
3. Requires `link`+`file` params; decrypts `link` back to the real URL server-side.
4. **Redirect-vs-proxy branch**: if `indexer.Protocol == Usenet` **OR** (`SupportsRedirect && indexerDef.Redirect`),
   Prowlarr does NOT fetch bytes itself — records a redirect-history event and issues a 301 straight to the real
   URL. **Usenet always redirects** regardless of any setting (enforced separately by a validation rule requiring
   `Redirect = true` for Usenet indexers); torrent indexers get a **per-indexer configurable toggle**.
5. Otherwise (torrent + `Redirect == false`, "proxy" mode): Prowlarr's own server fetches the `.torrent`/`.nzb`
   bytes (through `HttpIndexerBase.Download()`, which applies the full indexer-proxy chain — §9 — and follows up to
   `MaxRedirects` redirects itself, including detecting a `magnet:` redirect target mid-chain) and streams the
   bytes back with a synthesized filename/content-type.
6. **Magnet-byte sniffing**: if the downloaded "file" bytes literally start with ASCII `magnet:` (checked
   byte-by-byte), Prowlarr treats the whole response as a magnet URI and 301-redirects to it instead of serving a
   `.torrent` file — handles trackers whose download link resolves to a magnet redirect.
7. Errors map to Torznab-style XML `<error code description>` bodies with matching HTTP status (410/429/500) —
   itself part of the wire contract external Newznab/Torznab clients understand.

**Net summary**: no HMAC/signed-token scheme — real AES encryption of the literal upstream URL, embedded in a
self-referential indexer-scoped route. Whether the final hop is redirect (client fetches the tracker directly) or
full proxy (Prowlarr fetches and streams, applying its own proxy chain) is forced for Usenet and a per-indexer
toggle for torrents.

---

## 9. Indexer Proxy Chain

**Key source files**:
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/Indexers/IndexerHttpClient.cs`
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/IndexerProxies/HttpIndexerProxyBase.cs`, `IndexerProxyBase.cs`, `IndexerProxyDefinition.cs`, `IndexerProxyFactory.cs`, `IndexerProxyRepository.cs`, `IndexerProxyService.cs`
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/IndexerProxies/Http/Http.cs`, `HttpSettings.cs`
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/IndexerProxies/Socks4/Socks4.cs`, `Socks4Settings.cs`
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/IndexerProxies/Socks5/Socks5.cs`, `Socks5Settings.cs`
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/IndexerProxies/FlareSolverr/FlareSolverr.cs`, `FlareSolverrException.cs`, `FlareSolverrSettings.cs`

### 9.1 Selection logic — when a proxy is invoked (`IndexerHttpClient.GetProxies`)

**Per-indexer, tag-based selection, confirmed by direct source read**:
- If a real persisted indexer (`Id > 0`) has **zero tags**, no proxy DB lookup happens at all — untagged indexers
  are never proxied (a performance shortcut).
- A proxy applies to an indexer if they **share at least one tag** (`definition.Tags.Intersect(proxy.Definition.Tags).Any()`).
  An `IndexerProxyDefinition` is only considered "enabled" at all if it has at least one tag
  (`Enable => Tags.Any()`).
- Multiple matching proxies are deduplicated **by type**, at most one non-FlareSolverr proxy and one FlareSolverr
  proxy survive (`GroupBy(p => p is FlareSolverr).Select(g => g.First())`) — two different SOCKS proxies cannot be
  chained. Results are ordered so **FlareSolverr always runs last** — any transport proxy (HTTP/SOCKS) is applied
  to the request first, then FlareSolverr's logic layers on top.
- **Fallback for ad-hoc/test requests**: if no tag match is found and this is a transient/id-0 definition (the
  "Test" button flow before an indexer is even saved), any configured FlareSolverr instance is applied globally
  and unconditionally, so the setup UI can still exercise it.
- **There is no global always-on proxy setting for real indexers** — proxying is strictly opt-in per indexer via
  shared tags (except the id-0 testing fallback).

### 9.2 HTTP / SOCKS4 / SOCKS5 — "dumb" transport proxies

All three inherit `HttpIndexerProxyBase<TSettings>` and are structurally identical: `PreRequest` sets
`request.ProxySettings = new HttpProxySettings(ProxyType.{Http|Socks4|Socks5}, Host, Port, null, false, Username, Password)`;
no `PostResponse` override (pass-through). Settings shape identical across all three: `Host` (required), `Port`
(required), `Username`/`Password` (optional). None of them inspect or modify the response — they only affect how
the underlying transport dials out.

### 9.3 FlareSolverr — full request/response contract (application-level, not transport-level)

Unlike HTTP/SOCKS, FlareSolverr is a headless-browser challenge-solving HTTP API Prowlarr calls out to, and it
only activates **reactively**:

**`PreRequest`**: sets `SuppressHttpError = true` (lets what would be a thrown 403/503 exception flow through as a
normal response so `PostResponse` can inspect it), and injects a **previously cached User-Agent for this host**
(if any) onto the outbound request before it's even sent — a light optimization to make the original request look
more like the prior solved browser session.

**`PostResponse`** — the actual solve, gated by `CloudFlareDetectionService.IsCloudflareProtected`: checks the
response `Server` header (`cloudflare`, `cloudflare-nginx`, `ddos-guard`) AND either a 503/403 status with a known
challenge-page marker (`<title>Just a moment...</title>`, `<title>Access denied</title>`,
`<title>Attention Required! | Cloudflare</title>`, literal `error code: 1020`, `<title>DDOS-GUARD</title>`), or a
special-cased legacy-site signature. **If no challenge is detected, the original response passes through
untouched — FlareSolverr is never invoked for a normal successful request.**

When a challenge IS detected: builds a FlareSolverr API request from the *original* triggering request
(`POST {Settings.Host}/v1`), executes it, validates the FlareSolverr call's own status is 200 or 500 (500 is
tolerated — FlareSolverr returns structured errors that way), deserializes
`{Status, Message, StartTimestamp, EndTimestamp, Version, Solution: {Url, Status, Headers, Response, Cookies[], UserAgent}}`,
**caches the resolved User-Agent keyed by host**, injects the solution's cookies onto a rebuilt request, and
**re-executes the original request directly against the origin** (not through FlareSolverr) with the new UA +
cookies. FlareSolverr is only used to *obtain* a passing UA+cookie pair — the actual content fetch is Prowlarr's
own normal HTTP client.

**Request payload shape**: GET → `{cmd: "request.get", url, maxTimeout, proxy}`; POST with
`application/x-www-form-urlencoded` → `{cmd: "request.post", url, postData, headers, maxTimeout, proxy}`; POST with
`multipart/form-data` or `text/html` → explicitly **not implemented**, throws; any other method/content-type →
throws. `maxTimeout` = configured `RequestTimeout` (seconds) × 1000. The outer HTTP call to the FlareSolverr
container itself is given `RequestTimeout + 5` seconds of headroom, so Prowlarr's own client doesn't time out
before FlareSolverr's internal timeout would have produced a proper error body. If Prowlarr's own global outbound
proxy is configured, its settings are translated into a `proxy` field inside the FlareSolverr payload — **the
browser FlareSolverr spins up internally is told to use that same upstream proxy too**, but the plain HTTP call
Prowlarr makes to reach the FlareSolverr container itself is NOT proxied.

`FlareSolverrSettings`: `Host` (default `http://localhost:8191/`, required non-empty), `RequestTimeout` (default
60s, validated range **1–180 seconds**).

### 9.4 No session caching — confirmed absence

Every line of `FlareSolverr.cs`/`FlareSolverrSettings.cs`/`FlareSolverrException.cs` was read; there is **no
FlareSolverr named/persistent session support** (no `session.create`/`session.destroy`/session-id field anywhere
in the request/response DTOs). Every challenge-solve is a one-shot `request.get`/`request.post` command —
FlareSolverr spins up and tears down its own browser context per call. The **only** cross-request state Prowlarr
retains is the solved **User-Agent string per host**, cached with no explicit TTL in the generic `ICacheManager`
(unlike the release-search cache's explicit 30-minute TTL). Cookies solved by FlareSolverr are injected onto the
single request object built inside `PostResponse` and are NOT separately cached long-term. Detection re-runs on
every response regardless of the cached UA — if the UA hint alone isn't enough to pass (e.g. cookies also
expired), FlareSolverr is invoked again fresh. **Prowlarr does not cache/reuse an expensive browser session; it
re-solves via a fresh one-shot call every time a challenge is freshly detected**, only softening frequency via the
cached UA hint.

### 9.5 Retry-on-proxy-failure

No automatic retry loop exists anywhere in the proxy layer itself — if the FlareSolverr HTTP call throws (e.g.
container unreachable), the exception propagates uncaught through `IndexerHttpClient`. The only retry-shaped
behavior is `HttpIndexerBase.Download()`'s own redirect-chain following (HTTP redirects, not proxy failures), and
`ReleaseSearchService.DispatchIndexer`'s blanket catch (§7), which turns a thrown proxy exception during search
into "zero results this round" with no re-attempt in the same call. A 429 response's `Retry-After` is specifically
captured (`ReleaseDownloadException` wrapping `TooManyRequestsException`) and handed to the indexer-status
service (§11) so future requests are suppressed until the window passes, rather than retried immediately.

---

## 10. Indexer Rate Limiting (`queryLimit`/`grabLimit`)

**Key source files**:
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/Indexers/IndexerLimitService.cs`
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/Indexers/IndexerBaseSettings.cs`
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/History/History.cs`

**Domain semantics**: a user-configurable, per-indexer, sliding-window request budget — independent of and
layered on top of the escalating-failure backoff (§11). Many private trackers enforce hard API quotas (e.g.
"100 requests/day") and will ban an account if exceeded; Prowlarr self-throttles before ever making a request that
would violate the tracker's own rule.

**Configuration surface** (`IndexerBaseSettings`, present on every indexer type): `QueryLimit` (int?, "should be
greater than zero") — max combined `IndexerQuery` + `IndexerRss` history events per window; `GrabLimit` (int?) —
max `ReleaseGrabbed` events per window; `LimitsUnit` (enum `Day = 0` / `Hour = 1`, default `Day`) — window size 24h
or 1h (`CalculateIntervalLimitHours`).

**Mechanics** (a true sliding window backed by `History` row counts, not a fixed reset-at-midnight counter or an
in-memory token bucket): `AtQueryLimit`/`AtDownloadLimit` run `_historyService.CountSince(indexerId, now - intervalHours, eventTypes)`
— a plain count over the `History` table filtered by indexer/date/event-type. `CalculateRetryAfterQueryLimit`/
`CalculateRetryAfterDownloadLimit` use `FindFirstForIndexerSince(id, windowStart, eventTypes, limit)` — take the
most recent `limit` matching events, then the **oldest of that set**; the retry-after is exactly
`(thatEvent.Date + intervalHours) - now` in seconds, i.e. the precise moment the window frees a slot.

**Enforced at the front door**: both `NewznabController.GetNewznabResponse` (before search) and
`NewznabController.GetDownload` (before download) check these limits and return HTTP 429 with a computed
`Retry-After` **before attempting the upstream request** — not just observed after the fact.
`ReleaseSearchService.DispatchIndexer` also consults `AtQueryLimit` per-indexer during fan-out to skip an indexer
proactively (§7).

This is additive to, and independent from, the escalating backoff in §11 — a perfectly healthy indexer with zero
failures can still be legitimately rate-limited by the user's own configured budget.

---

## 11. Indexer Health/Status and Escalating Backoff

**Key source files**:
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/Indexers/IndexerStatus.cs`, `IndexerStatusService.cs`
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/ThingiProvider/Status/ProviderStatusBase.cs`, `ProviderStatusServiceBase.cs`, `EscalationBackOff.cs`
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/Indexers/HttpIndexerBase.cs` (call sites)

**Domain semantics**: every indexer (and, via the same base class, every "application" sync target and indexer
proxy) has a persisted circuit-breaker. A failed request nudges `EscalationLevel` up a fixed ladder of
increasingly long "disabled until" windows; a clean success nudges it back down one notch at a time (not an
instant reset). This is what prevents Prowlarr from hammering a dead/blocking tracker, and is completely generic
infrastructure shared across indexers/apps/proxies via `ProviderStatusBase`/`ProviderStatusServiceBase<TProvider,TModel>`.

### Persisted fields (`IndexerStatus : ProviderStatusBase`)

```
Id, ProviderId, InitialFailure (DateTime?), MostRecentFailure (DateTime?),
EscalationLevel (int), DisabledTill (DateTime?),
LastRssSyncReleaseInfo (ReleaseInfo — high-water mark for incremental RSS polling),
Cookies (IDictionary<string,string>), CookiesExpirationDate (DateTime?)
```

### The escalation ladder (`EscalationBackOff.Periods`, exact values confirmed by direct read)

| Level | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 |
|---|---|---|---|---|---|---|---|---|---|---|
| Backoff | 0s | 1m | 5m | 15m | 30m | 1h | 3h | 6h | 12h | 24h |

Indexers use the **full ladder to level 9 (24h)** with **zero grace period** before escalation can begin
(`MinimumTimeSinceInitialFailure = TimeSpan.Zero`). Contrast: application-sync targets (Sonarr, §13) cap at level
5 (1h max) and require a 5-minute grace period — a deliberately gentler curve for a local sync partner vs. a flaky
remote tracker.

### Exact state transitions

- **`RecordSuccess`**: no-op at level 0; otherwise `EscalationLevel -= 1` and `DisabledTill = null` — a single
  success immediately un-blocks the provider even if the new level is still > 0, but the level only steps down one
  notch, so a flapping indexer still trends toward long backoffs overall.
- **`RecordFailure(id, minimumBackOff, escalate)`**: always stamps `MostRecentFailure`. First failure in a streak
  (level 0) sets `InitialFailure = now` and jumps straight to level 1 (this jump itself does not also escalate
  further in the same call). **Startup grace**: within 15 minutes of app start, `DisabledTill` is clamped to at
  most `now + 5m` (level-2 period) unless an explicit `minimumBackOff` was supplied — prevents a fresh boot (before
  network/DNS is fully up) blacklisting every indexer for a full day. If an explicit `minimumBackOff` is passed
  (e.g. a tracker's own `Retry-After` on HTTP 429), the level is force-walked upward (still capped) until that
  level's period meets the minimum — a rate-limit hint can jump multiple rungs at once. Otherwise, absent grace
  period, the level increments by exactly 1.
- **`RecordConnectionFailure`** = `RecordFailure(id, Zero, escalate:false)` — DNS/connect-level failures
  (`WebExceptionStatus.NameResolutionFailure`/`ConnectFailure`) record and still (re)set `DisabledTill` per the
  current level, but never themselves push the level higher — only genuine application-level failures (bad HTTP
  status, parse errors, auth failures) escalate.
- **`IsDisabled()`** = `DisabledTill > UtcNow`. `GetBlockedProviders()` is what `IndexerFactory`/search dispatch
  consult to skip a provider entirely — a blocked indexer is never attempted, not attempted-and-failed-again,
  until its window elapses.

### Call sites driving this (`HttpIndexerBase.FetchReleases`/`Download`/`TestConnection`)

Exception-type-specific mapping: `TooManyRequestsException` → `RecordFailure` with the exception's parsed
`RetryAfter` (or 1-hour default) — the direct line from HTTP 429 to an immediate multi-rung jump.
`CloudFlareProtectionException`/generic `HttpException`/unexpected exceptions → normal `RecordFailure` (escalates).
DNS/connect `WebException` → `RecordConnectionFailure` (no escalation). Clean success → `RecordSuccess`.

---

## 12. Indexer Statistics and Health Checks

**Key source files**:
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/IndexerStats/IndexerStatistics.cs`, `IndexerStatisticsService.cs`
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/History/History.cs`, `HistoryService.cs`
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/Prowlarr.Api.V1/Indexers/IndexerStatsController.cs`, `IndexerStatsResource.cs`, `IndexerStatusController.cs`
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/HealthCheck/Checks/Indexer*.cs`

### 12.1 Statistics tracked (`IndexerStatistics`, every field)

| Field | Meaning |
|---|---|
| `IndexerId`, `IndexerName` | identity |
| `AverageResponseTime` | avg elapsed ms across `IndexerRss`+`IndexerQuery` events |
| `AverageGrabResponseTime` | avg elapsed ms across `ReleaseGrabbed` events |
| `NumberOfQueries` / `NumberOfGrabs` / `NumberOfRssQueries` / `NumberOfAuthQueries` | counts per event type |
| `NumberOfFailedQueries` / `NumberOfFailedGrabs` / `NumberOfFailedRssQueries` / `NumberOfFailedAuthQueries` | counts where `Successful == false` |

Sibling result sets (`CombinedStatistics`): `UserAgentStatistics` (`UserAgent`, `NumberOfQueries`,
`NumberOfGrabs`, grouped by `Data["source"]`, excluding auth events) and `HostStatistics` (`Host`,
`NumberOfQueries`, `NumberOfGrabs`, grouped by `Data["host"]`). **No persisted "failure rate" field** — that's a
derived UI computation, never stored.

### 12.2 Computation (`IndexerStatisticsService`, on-demand, no background aggregation job)

Pulls `History.Between(start, end)`, filters to requested indexer ids, groups by indexer / by `Data["source"]` /
by `Data["host"]`, single pass per group incrementing counters on a switch over `HistoryEventType`.
`CalculateAverageElapsedTime` only counts entries with `elapsedTime > 0` AND `Data["cached"] != "1"` — cached
responses are excluded from response-time averaging since they'd skew it artificially low. This is a fresh
full-table-range scan every time stats are requested — no materialized/cached stats table.

### 12.3 History event model (the substrate)

```
History { IndexerId, Date, Successful (bool), EventType, Data (Dictionary<string,string>), DownloadId }
enum HistoryEventType { Unknown=0, ReleaseGrabbed=1, IndexerQuery=2, IndexerRss=3, IndexerAuth=4, IndexerInfo=5 }
```
All 5 enum members confirmed by direct source read. `IndexerInfo` is declared but not referenced anywhere in
`IndexerStatisticsService`'s counting switch — a defined-but-currently-unused event type. Written by three event
handlers: `IndexerQueryEvent` (captures rich per-search-type `Data` — Movie: ImdbId/TmdbId/TraktId/Year/Genre; TV:
+TvdbId/RId/TvMazeId/Season/Episode; Music: Artist/Album/Track/Label/Year/Genre; Book:
Author/Title/Publisher/Year/Genre; universally Limit/Offset/ElapsedTime/Query/QueryType/Categories/Source/Host/QueryResults/Url/Cached),
`IndexerDownloadEvent` (`Data`: Source/Host/GrabMethod=`"Redirect"`|`"Proxy"`/GrabTitle/Url/ElapsedTime/InfoUrl/
DownloadClient(Name)/PublishedDate), `IndexerAuthEvent` (`Data`: ElapsedTime only). Also handles indexer-deletion
cascade and two commands: `CleanUpHistoryCommand` (ages out rows past `HistoryCleanupDays`, 0=disabled) and
`ClearHistoryCommand` (full purge).

### 12.4 API surface

`GET /api/v1/indexerstats` (`IndexerStatsController`): `startDate`/`endDate`/`indexers`/`protocols`/`tags` query
filters, returns `{Indexers, UserAgents, Hosts}`. `GET /api/v1/indexerstatus` (`IndexerStatusController`): returns
all currently-blocked providers (`DisabledTill` in the future) — a live circuit-breaker dashboard, pushed over
SignalR on `ProviderStatusChangedEvent`.

### 12.5 Health checks (directory: `HealthCheck/Checks/`, indexer/application-relevant ones enumerated in full)

1. **`IndexerCheck`** — Error if zero enabled indexers exist at all.
2. **`IndexerStatusCheck`** — "short-term": among blocked indexers whose `InitialFailure` is within the last 6
   hours, Error if ALL enabled indexers are blocked, Warning if SOME are.
3. **`IndexerLongTermStatusCheck`** — identical logic, inverted time filter: `InitialFailure` older than 6 hours
   (chronic failures) — same Error-if-all/Warning-if-some split with distinct message keys. Together #2/#3
   partition "just started failing" from "failing for a long time" as separate dashboard signals.
4. **`IndexerDownloadClientCheck`** — Warning if any enabled indexer references a `DownloadClientId` that no
   longer corresponds to any enabled download client.
5. **`IndexerNoDefinitionCheck`** — event-driven only (not on the periodic sweep); Error if a Cardigann indexer's
   `DefinitionFile` no longer exists among currently-loaded definitions (upstream YAML was removed/renamed).
6. **`IndexerProxyStatusCheck`** — Error if all enabled proxies fail `.Test()`, Warning if some fail.
7. **`IndexerVIPCheck`** — reflection-reads a `VipExpiration` string property off indexer settings; Warning if it
   falls within the next 7 days.
8. **`IndexerVIPExpiredCheck`** — same reflection mechanism; Error if already in the past.
9. **`ApplicationStatusCheck`/`ApplicationLongTermStatusCheck`** — the identical short/long-term 6-hour split
   pattern as #2/#3, but for sync-target apps (Sonarr/Radarr/etc.) instead of indexers.

**Pattern takeaway**: health checks never invent new state — all are pure read-side projections over the same
`ProviderStatusBase`-derived `DisabledTill`/`InitialFailure` fields, or direct config/reference inspection. There
is **no dedicated "RSS not syncing" check** — the closest related signal, `LastRssSyncReleaseInfo`, is bookkeeping
for incremental sync, not a health-check trigger.

---

## 13. Apps Sync (Prowlarr → Sonarr as Torznab endpoints)

Kept intentionally brief — Prismedia has no separate consumer app to sync to, so this section captures only the
wire-contract shape a consumer expects, for reference should Prismedia ever interoperate with an external
Sonarr/Radarr a user still runs.

**Key source files**:
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/Applications/ApplicationService.cs`, `ApplicationBase.cs`, `ApplicationFactory.cs`, `ApplicationSyncLevel.cs`, `AppIndexerMap*.cs`, `ApplicationStatus*.cs`
- `/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/Applications/Sonarr/Sonarr.cs`, `SonarrIndexer.cs`, `SonarrSettings.cs`, `SonarrV3Proxy.cs`

**Sync levels**: `Disabled` (app excluded entirely), `AddOnly` (new indexers pushed, existing ones never updated on
setting changes), `FullSync` (continuous reconciliation — updates AND prunes indexers that no longer qualify).

**Trigger model**: event-driven, not polling — indexer add/update/delete events, an `ApiKeyChangedEvent` (full
forced resync, since Torznab URLs embed the API key), bulk-update events, and a manual/scheduled
`ApplicationIndexerSyncCommand`.

**The ID-bridging trick**: Prowlarr encodes its own indexer identity **inside the Torznab base URL** it writes
into Sonarr — `{ProwlarrUrl}/{indexer.Id}/`. Reconciliation without trusting a perfect local ledger:
`Sonarr.GetIndexerMappings()` calls Sonarr's own `GET /api/v3/indexer`, filters to `Newznab`/`Torznab`
implementations, keeps only ones whose `baseUrl` starts with Prowlarr's configured URL (or whose `apiKey` matches
Prowlarr's own), then regex-extracts the trailing numeric path segment as the Prowlarr indexer id — no separate
external-id concept is persisted independent of the URL itself. A local `AppIndexerMap` table
(`{AppId, IndexerId, RemoteIndexerId, RemoteIndexerName}`) caches this but is actively reconciled/self-healed
against Sonarr's live state on every sync (stale mappings whose `RemoteIndexerId` 404s in Sonarr are dropped and
re-added fresh).

**Fields pushed into Sonarr** (`BuildSonarrIndexer`): a whitelisted subset of Sonarr's own indexer schema
(`baseUrl, apiPath, apiKey, categories, animeCategories, animeStandardFormatSearch, minimumSeeders,
seedCriteria.*, rejectBlocklistedTorrentHashesWhileGrabbing`), with `baseUrl`/`apiPath`/`apiKey` always overwritten
to point back at Prowlarr, and `categories`/`animeCategories` intersected with the app's configured
`SyncCategories`/`AnimeSyncCategories` settings. An indexer is skipped/removed entirely if it has no category
overlap with those settings, or doesn't support TV/generic search. A no-op equality check (`SonarrIndexer.Equals`)
avoids redundant writes on `FullSync` passes when nothing actually changed.

**Auth/connectivity**: `X-Api-Key` header (Sonarr's own key), optional basic auth; `MinimumApplicationVersion`
enforced via Sonarr's `X-Application-Version` response header (Sonarr v2 explicitly rejected).

**Status/failure tracking**: identical `ProviderStatusBase` escalation machinery as indexers (§11), but capped at
level 5 (1-hour max backoff) with a 5-minute grace period — deliberately gentler than the indexer ladder, since a
local sync partner failing is a different risk profile than a flaky remote tracker.

---

## 14. Cookie-Jar Auth Persistence

`IndexerStatus.Cookies` + `CookiesExpirationDate`, read/written via `IndexerStatusService.GetIndexerCookies`/`UpdateCookies`.
Every `HttpIndexerBase` subclass (native and Cardigann alike) persists the cookie jar from a successful
request/login, with a default expiration of **12 days** if the login flow didn't set one explicitly, or **30 days**
after any successful fetch (`HttpIndexerBase.FetchIndexerResponse`). `CheckIfLoginNeeded` (default: HTTP 401)
triggers a `DoLogin()` re-auth mid-request, replacing the stored cookie jar and retrying the original request once.
Cookie-based private-tracker sessions self-heal across expiry without user interaction as long as stored
credentials remain valid.

---

## 15. Seed Criteria and Magnet-Link Construction

- `SeedConfigProvider` resolves a per-release `TorrentSeedConfiguration` (ratio + seed time) by looking up the
  *originating indexer's* `IndexerTorrentBaseSettings`, cached 1 hour, invalidated on indexer update — lets a
  torrent client apply tracker-specific "don't stop seeding below ratio X for Y minutes" rules automatically at
  grab time, without the download client needing per-tracker config.
- `MagnetLinkBuilder` synthesizes a public magnet link from a bare info-hash plus a hardcoded list of ~19
  well-known public trackers (`tracker.opentrackr.org`, `opentracker.i2p.rocks`, etc.) — used when an indexer
  exposes only an info-hash rather than a full magnet URI or `.torrent` file.

---

## 16. Exception Taxonomy

`/Users/pauldavis/Dev/_ARCHIVE/Prowlarr/src/NzbDrone.Core/Indexers/Exceptions/`: `IndexerException` (carries the
raw `IndexerResponse` for diagnostics), `IndexerAuthException` (bad credentials), `RequestLimitReachedException`
(carries the response; distinct from `TooManyRequestsException`, the HTTP-429-specific type with a parsed
`RetryAfter`), `SizeParsingException` (release size unparseable — triggers `IsValidRelease` rejection upstream),
`UnsupportedFeedException` (malformed feed shape, e.g. missing `pubDate`). Each maps to a distinct log level and
status-service call in `HttpIndexerBase.FetchReleases`'s catch-cascade — this taxonomy keeps one failure mode
(e.g. auth failure) from being misclassified as another (e.g. transient timeout) and escalating the backoff
incorrectly.

---

## 17. Prismedia's Current State — What Already Exists

(Surveyed directly in the Prismedia codebase at `/Users/pauldavis/Dev/Prismedia`, not from Prowlarr.)

**Request/acquisition/search domain**: a full layered implementation already exists —
`Domain/Entities/Enums/Acquisition/` (`AcquisitionStatus`, `BlocklistReason`, `MonitorStatus`),
`Application/Acquisition/` (23 files: ports, decision engines, query ladder), `Application/Requests/`
(kind-agnostic front door via `RequestKindRegistry`), `Application/Jobs/Handlers/Acquisition/` (7 handlers),
`Infrastructure/Acquisition/` (EF stores + `ProwlarrIndexerClient.cs`, `QBittorrentDownloadClient.cs`), plus
Contracts/Api endpoint layers. `AcquisitionStatus` runs
Pending→Searching→AwaitingSelection→Queued→Downloading→Downloaded→Importing→Imported. Standing "monitors"
(`MonitorStatus: Active|Paused|Fulfilled`) drive periodic re-search with a serialized single-upgrade-slot mechanism.

**Plugin contracts**: `packages/plugins/src/types.ts` and `Prismedia.Contracts/Plugins/PluginManifest.cs` define
plugin capabilities, but these are entirely metadata-identification oriented (videoByURL, bookByName,
performerByFragment, etc.). There is **no "I am a search/indexer provider" plugin capability**, and no
`CAPABILITY_KIND` closed-set enum for it. The acquisition/indexer layer today is a separate, non-plugin,
hardcoded adapter pair — not wired into the plugin manifest system at all.

**Download client abstraction**: already generic — `IDownloadClient`/`IDownloadClientFactory` keyed by
`DownloadClientKind` enum (`QBittorrent` implemented, `Transmission` reserved/unimplemented). The search side
mirrors this shape: `IIndexerSearchClient`/`IndexerKind` (`Prowlarr` implemented, `Jackett` reserved/unimplemented).
Both are pluggable multi-adapter ports today with exactly one concrete implementation each — i.e., **Prismedia
currently delegates indexer search entirely to an external Prowlarr instance via `ProwlarrIndexerClient`.**

**Category mapping / rate limiting / health tracking**: category mapping exists and is explicitly Torznab-shaped
(`TorznabCategories` static class, `Categories: int[]` per indexer), but there is no per-indexer category-capability
discovery. **No rate limiting** exists for indexers or download clients today (unthrottled `Task.WhenAll` across
indexers — the only rate-limit-ish code present is unrelated: an API-key login throttle and Identify-metadata-plugin
transient-error classification). **No persisted health/status tracking** exists — only ephemeral per-request error
lists (`ProviderErrors`) and one-shot manual "Test" connectivity probes; no scheduled health poll or up/down history.

**Release scoring/parsing**: a substantial, explicitly Sonarr-inspired-by-comment scoring system already exists —
`BookReleaseScore` and per-kind decision engines (Book/Movie/Music/TV) combining quality tier × resolution/source/codec,
weighted terms, preferred-language bonus, seeder/peer tiebreakers. Accept/reject gates are implemented as a
specification pattern (`IReleaseSpecification`, doc-commented as modeled on "Sonarr's specification pattern").
Parsing is inline substring matching on titles — no structured PTN/GuessIt-style parser exists.

**Manual search UI/API**: no standalone free-text manual-indexer-search page exists. Per the locked design in
`docs/discover-request-roadmap.md`, the entity's own detail page is the management surface —
`ReleaseTable.svelte` shows scored candidates with manual queue/blocklist actions, a manual `.torrent` upload
fallback exists, and re-search re-runs the same automated query ladder rather than accepting free text.

**Explicit terminology already in the codebase**: `prowlarr` and `qbittorrent` are live, current terms throughout
the acquisition subsystem (backend, frontend, tests, changelog). `torznab` is active vocabulary for category
mapping. `sonarr`/`radarr`/`lidarr` appear only as stale doc references (`README.md`,
`documentation-site/docs/*`, describing the pre-removal integration — Sonarr/Radarr/Lidarr support was explicitly
removed 2026-06-30 per user memory) and two doc-comment-only design-inspiration mentions in scoring files.
`newznab`, `cardigann`, `flaresolverr` — **zero hits anywhere** in Prismedia's codebase today.

**Maturity summary**: Books/Movies/Music have a working search→score→queue pipeline behind hardcoded Prowlarr +
qBittorrent adapters (both already shaped as swappable multi-adapter ports, though only one concrete
implementation exists per port); TV is newest (discover/detail only, not yet committable); there is no
indexer-as-plugin abstraction, no rate-limiting, and no persisted provider health tracking anywhere in the system
today. In short: **Prismedia's whole acquisition pipeline currently assumes an external Prowlarr is present** —
this report exists precisely to scope what has to move inside Prismedia to remove that dependency.

---

## 18. What Must Live Inside Prismedia vs. Stay Delegated

Judgment calls, reasoned from the above plus Prismedia's current architecture (§17):

### Must move inside Prismedia (core to "replace Prowlarr+Sonarr with built-in support")

1. **A real category-mapping/normalization layer** — Prismedia already has `TorznabCategories`, but Prowlarr's
   depth here (parent/child expansion, custom-category hashing for stable ids, description-based fallback
   matching) is what makes cross-tracker aggregation actually work. Without it, adding a second native indexer
   type immediately breaks category filtering. This is small, self-contained, and has no reason to be delegated —
   §5 is effectively a spec to port.
2. **Per-indexer rate limiting (queryLimit/grabLimit) and escalating backoff** — Prismedia has neither today (§17
   confirms unthrottled `Task.WhenAll` and no persisted health state). This is exactly the kind of thing that gets
   an account banned on a real private tracker if skipped. The design in §10/§11 (sliding-window history counts +
   a fixed 10-level escalation ladder with success-decrements-one-level semantics) is directly portable and does
   not require Prowlarr's specific implementation — it's a generic reliability pattern any indexer-search-owning
   app needs once it talks to trackers directly.
3. **A generic search-provider plugin capability** — today Prismedia's plugin system (`PluginManifest`) has no
   concept of "I am an indexer/search provider." If Prismedia wants pluggable native indexers (the equivalent of
   Sonarr's built-in indexer definitions) or eventually a Cardigann-like YAML engine, this capability needs a home
   in the existing `[Code]`-enum/plugin-manifest system per the repo's Identifier Discipline contract — this is an
   architectural prerequisite, not optional plumbing.
4. **Query fan-out with per-indexer failure isolation** (§7) — the `Task.WhenAll` + per-task try/catch pattern
   that stops one bad indexer from failing a whole search. Prismedia's current single-provider
   (`ProwlarrIndexerClient`) design doesn't need this yet, but the moment a second native indexer type is added,
   this isolation becomes mandatory, not optional.
5. **Manual search UI/API semantics worth adopting**: the release-cache-then-grab-by-guid pattern (§6) is a clean,
   proven approach (avoids re-scraping on grab, avoids exposing raw tracker links) — Prismedia's `ReleaseTable.svelte`
   already shows scored candidates from a completed search; wiring a similar short-TTL cache under manual
   "grab this specific release" actions is a natural fit as Prismedia's UI-driven flows grow.
6. **Indexer health/status surface for the user** — even a simplified version of §12's per-indexer stats +
   circuit-breaker status (queries/grabs/failures, currently-blocked-till) gives users the visibility Prowlarr's
   UI provides today and that a hardcoded single-provider client currently has no equivalent for.

### Should stay delegated / out of scope for Prismedia itself

1. **The Cardigann YAML engine in full generality** (§1) — an enormous surface (template mini-language, 20+
   filter functions, 6 login modes, JSON/XML/HTML tri-modal parsing, category-mapping DSL) built and maintained by
   a team solely to support hundreds of long-tail private trackers. Building and maintaining an equivalent inside
   Prismedia is a multi-month undertaking with an ongoing maintenance tax (tracker sites change their HTML
   constantly) that is exactly why Prowlarr centralizes it as a live-updated YAML feed (§2) rather than
   per-app-release code. Unless Prismedia specifically wants to support arbitrary private trackers by
   community-authored definitions, native support for whichever public/semi-private indexers Prismedia's plugins
   target directly (the Sonarr model: a handful of hand-written C#/TypeScript adapters) is far cheaper than
   reimplementing Cardigann.
2. **FlareSolverr integration** (§9.3–9.4) — a genuinely separate service (a headless-browser container) that
   Prowlarr merely calls out to. If Prismedia's chosen indexers don't sit behind Cloudflare/DDoS-Guard challenges,
   there's no reason to build this at all; if some do, the right move is the same as Prowlarr's — treat it as an
   optional, separately-run sidecar service reached over a documented HTTP contract, not something reimplemented
   inside Prismedia's own process.
3. **HTTP/SOCKS4/SOCKS5 outbound proxy support** (§9.2) — low-value, "dumb" transport-layer settings. Only worth
   adding if/when a specific indexer plugin needs geo-restriction bypass; not core to the request→search→download
   loop itself.
4. **Apps-sync / Torznab-endpoint hosting** (§13) — this exists purely to let Prowlarr serve as a shared indexer
   layer for OTHER apps (Sonarr/Radarr/etc.). Since Prismedia's stated goal is to BE the all-in-one replacement
   (not a shared indexer layer for third parties), there is no reason to build a Torznab-server-hosting feature
   unless a future product decision explicitly wants Prismedia to also serve as an indexer backend for a
   still-installed external Sonarr/Radarr — which the current architecture and `docs/discover-request-roadmap.md`
   explicitly reject.
5. **Definition-distribution-as-a-service** (§2) — the whole "fetch YAML defs from a CDN, versioned independent of
   app releases" infrastructure only matters if Prismedia adopts a Cardigann-equivalent engine (see point 1 above).
   Without that engine, there's nothing to distribute.
6. **CAPTCHA-solving UI** (§3) — only relevant if Prismedia's indexer plugins target trackers that gate login
   behind a captcha; the "cookie" login mode (paste a browser session cookie) is a much simpler, already-common
   fallback pattern that covers the same need with far less engineering investment, and is worth keeping in mind
   as the low-cost equivalent if any target tracker requires interactive login at all.
7. **Download-URL AES-encryption proxy scheme** (§8) — this exists in Prowlarr specifically because Prowlarr must
   hide tracker URLs from OTHER apps (Sonarr) that only get a Torznab feed. If Prismedia fetches and hands off to
   qBittorrent directly (as it already does via `QBittorrentDownloadClient`), there's no intermediate consumer to
   hide the URL from, so this whole mechanism is very likely unnecessary — Prismedia's server-to-download-client
   handoff can stay direct.

### Bottom line

The delta that actually matters for "built-in indexer support that replaces Prowlarr+Sonarr" is the **reliability
and normalization layer** (§5, §10, §11, §12) plus a **plugin capability for search providers** (§17 gap) and
**fan-out isolation** (§7) — all of which are moderate, well-specified, and worth building natively. The **content
engine** (Cardigann, §1–§3) and the **multi-consumer-serving** features (§13, §8's encryption scheme) are the parts
of Prowlarr that exist because Prowlarr serves an ecosystem of other apps and an open-ended long tail of private
trackers — neither of which matches Prismedia's single-app, plugin-curated-indexer-set model, and both are safe
to leave out of scope unless a specific future requirement demands them.
