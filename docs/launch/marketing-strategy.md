# Prismedia Public-Launch Marketing and Information-Architecture Strategy

**Status:** Recommended launch system
**Research date:** July 24, 2026
**Primary decision:** Position Prismedia as the private, self-hosted home for the **whole media lifecycle**, not merely as a library that supports many file types.

## Executive recommendation

Prismedia's strongest product truth is not “one app with lots of media types.” It is that a collection remains one coherent system from the moment someone wants something through acquisition, identification, organization, playback or reading, and ongoing management.

The launch story should therefore lead with:

> **Your whole media life. One private home.**
>
> Prismedia is a self-hosted media library that keeps discovery, requests, downloads, metadata, files, playback, and reading connected—across web, iPhone, iPad, and Apple TV.

The enduring campaign line should be:

> **One lifecycle. Every medium.**

The prism metaphor explains this promise but should not be required to understand it. White light represents the collection and its shared state entering one system. The spectrum represents distinct, first-class experiences—watch, listen, read, browse, request, and manage—emerging without losing their connection to the source. Use the metaphor in visuals and section transitions; use plain product language in the headline and subhead.

Three changes will make the largest launch difference:

1. Replace the current “many media types” hero with the whole-lifecycle promise.
2. Make the marketing home a product narrative, with Docs remaining a clearly separate task-oriented destination.
3. Present Web, iPhone/iPad, and Apple TV as one platform family, with Docker and TestFlight as parallel, visible ways into that family.

## Evidence and guardrails

This strategy is grounded in:

- `AGENTS.md` and the repository product contract.
- `README.md`, `CHANGELOG.md`, `docs/architecture.md`, `docs/discover-request-roadmap.md`, and `docs/design-language.md`.
- The current Docusaurus home in `documentation-site/src/pages/index.tsx` and `index.module.css`.
- Current product documentation for installation, scanning, requests, playback and reading, authentication, OPDS, plugins, and Jellyfin-compatible clients.
- Product screenshots in `docs/screenshots/`, especially Dashboard, Request, Identify, Files, video detail, audio, and mobile.

The recommended copy follows these guardrails:

- Do not describe Prismedia as a replacement for any named app or product category.
- Do not promise that Prismedia eliminates every external service. Metadata, indexer, subtitle, download-client, and other configured integrations can require network access or separate services.
- Do not say “no configuration.” Prismedia has a simple container entry point, but a useful installation still needs mounts, an administrator, library roots, scans, and optional integrations.
- Do not make native iOS or tvOS feature claims until the launch owner verifies them. The repository proves a strong mobile web experience and Apple-client interoperability, but it does not contain the native app source or product documentation.
- Keep experimental interoperability explicitly labeled. The Jellyfin-compatible API is useful proof of openness, but it is not a complete Jellyfin implementation.
- Prefer inspectable proof over absolutes: one Docker image, one exposed port, mounted storage, documented accounts, public source, visible jobs, and a changelog.

## Target audiences and their tensions

### 1. The self-hosted collection curator — primary buyer and operator

**Who they are:** The person who owns the server, storage, collection structure, and integrations. They may support a household, but they carry the operational burden.

**Current tension:**

- Their media is one collection, but its lifecycle is split across request, download, metadata, playback, file, and maintenance tools.
- Every handoff creates another database, identity model, queue, failure surface, and place to troubleshoot.
- They value control, but “control” often becomes permanent tool-chain maintenance.
- They are skeptical of “all-in-one” promises that hide important tradeoffs.

**What Prismedia should promise:** A coherent source of library truth and one visible operational flow, while still allowing adapters, clients, and plugins where they add value.

**Proof that matters:** Docker installation, mounted storage, visible jobs, files and entities linked together, first-party acquisition state, provider review, public source, documented architecture, and interoperability.

### 2. The household member — primary user

**Who they are:** A partner, family member, or trusted user who wants the collection to feel like a finished product rather than a homelab.

**Current tension:**

- They do not care which service found, downloaded, identified, or transcoded an item.
- They expect personal progress, reliable sign-in, the right libraries, and an interface that works from the couch or phone.
- A system can be technically capable and still feel fragmented if each medium behaves like a separate app.

**What Prismedia should promise:** One calm, personal place to continue, browse, watch, listen, and read.

**Proof that matters:** Household accounts, per-user progress and library access, Continue surfaces, artwork-led UI, touch-first web, and television and native platform availability once verified.

### 3. The Apple-platform media user — launch growth audience

**Who they are:** An iPhone, iPad, and Apple TV user who values a native-feeling experience and continuity across screens.

**Current tension:**

- Self-hosted products often treat the server as the product and clients as an afterthought.
- Mobile may be a compressed desktop page; television support may be a protocol checkbox rather than a designed experience.
- They need to know whether TestFlight is an actual product path, not merely a development footnote.

**What Prismedia should promise:** The server, web app, native mobile experience, and living-room experience belong to one product family.

**Proof that matters:** Platform-specific screenshots or video, a persistent TestFlight CTA, an explicit feature/support matrix, and verified continuity of account and progress state.

### 4. The metadata and workflow enthusiast — secondary power user

**Who they are:** Someone who cares about correct identity, artwork, relationships, subtitles, acquisition decisions, and file placement as much as playback.

**Current tension:**

- Automation can be opaque, while manual workflows do not scale.
- Metadata changes can erase intentional edits or make provenance hard to understand.
- Failed background work is often buried in logs.

**What Prismedia should promise:** Automation with review, provenance, and visible operational state.

**Proof that matters:** Identify proposals, Auto Identify, durable queues and history, explicit Jobs, field-level review, release review, and preserved user edits.

### 5. The contributor and integration builder — credibility audience

**Who they are:** A developer who may inspect, extend, package, or contribute to Prismedia.

**Current tension:**

- “Open” products sometimes expose source but not architecture, contracts, or extension points.
- Compatibility layers can quietly dictate the product's internal model.
- A large application can be difficult to trust if its boundaries are unclear.

**What Prismedia should promise:** A Prismedia-owned model, documented boundaries, stable adapter points, generated contracts, and an inspectable development process.

**Proof that matters:** GitHub, license clarity, architecture docs, plugin docs, changelog, release channels, and explicit status labels.

## Category and positioning

### Recommended category

**External category:** A self-hosted media library for the whole lifecycle.

**Internal strategic category:** A private media lifecycle platform.

“Media server” is too narrow because it foregrounds serving files. “Media manager” understates playback and reading. “All-in-one” sounds generic and implies unsupported replacement claims. “Media lifecycle” captures the distinctive breadth, while “self-hosted media library” keeps the public description familiar.

### Positioning statement

For people and households who keep their own media, Prismedia is a private, self-hosted media library that connects discovery, acquisition, metadata, files, playback, reading, and operations in one coherent system. Unlike a chain of isolated dashboards, Prismedia keeps the item, its identity, its state, and its history connected throughout the lifecycle—while remaining extensible through clients, providers, and plugins.

### The contrast to dramatize

Do not build a competitor comparison table. Show the structural problem instead:

```text
Fragmented workflow
Want → request database → download database → metadata database → playback database → file tools

Prismedia
One entity → wanted → acquiring → identified → available → enjoyed → maintained
```

This is a systems contrast, not a claim that every integration disappears. It explains why one source of truth matters without naming or diminishing another product.

### Brand idea

**White light in:** Files, wants, metadata, and household state enter one trusted library.

**The prism:** Prismedia normalizes identity, relationships, files, progress, jobs, and access.

**Spectrum out:** Distinct experiences for watching, listening, reading, browsing, requesting, and managing remain recognizable but coherent.

The design must stay within the existing language: true black and opaque neutral material as the ground; neutral silver for Prismedia; spectrum color used sparsely; artwork-driven atmosphere on details; frosted glass only for floating or persistent chrome. A marketing page may use the literal white-light/prism/spectrum sequence more prominently than the product UI, but should not become a wall of neon gradients.

## Message hierarchy

The launch page should communicate these ideas in order:

1. **Outcome:** Your whole media life has one private home.
2. **Category:** Prismedia is a self-hosted media library.
3. **Differentiator:** It keeps the whole lifecycle connected, not only playback.
4. **Breadth:** Video-first, with books, comics, images, galleries, and audio as first-class experiences.
5. **Every screen:** Web, iPhone/iPad, and Apple TV are designed as one product family.
6. **Control:** Your files and library state live on infrastructure you control.
7. **Operational coherence:** Requests, acquisition, metadata, files, jobs, and user state refer to the same library entities.
8. **Extensibility:** Plugins, provider adapters, OPDS, and experimental Jellyfin-compatible access meet existing workflows without defining Prismedia's model.
9. **Proof:** One Docker image, one port, public source, documentation, changelog, screenshots, and status labels.

Do not open with a comprehensive list of media types. Lists prove breadth after the visitor understands the outcome; they do not create the category.

## The first viewport

### Recommended exact hero

**Eyebrow**

> Private · self-hosted · made for the household

**H1**

> Your whole media life. One private home.

**Subhead**

> Prismedia is a self-hosted media library that keeps discovery, requests, downloads, metadata, files, playback, and reading connected—across web, iPhone, iPad, and Apple TV.

**Primary CTA**

> Install with Docker

Destination: `/docs/getting-started/install`

**Secondary CTA**

> Join TestFlight

Destination: <https://testflight.apple.com/join/c9bgDxr7>

**Tertiary text link**

> View the source

Destination: `https://github.com/pauljoda/Prismedia`

**Proof rail**

> One Docker image · One exposed port · Household accounts · Source available

“Source available” is the safe launch wording under the current license; see [Trust, source, and self-hosting](#trust-source-and-self-hosting).

### Hero visual

Show the lifecycle, not a floating dashboard alone:

- At left, one restrained white beam labeled `Your collection`.
- At center, the actual Prismedia mark.
- At right, four or five quiet spectrum lanes: `Discover`, `Organize`, `Watch`, `Listen`, `Read`.
- Place real product crops inside or immediately below the lanes: Request, Identify, video detail, audio, and reader.
- Keep the Dashboard as the dominant product frame behind or below the conceptual sequence.
- On mobile, collapse the beam into a vertical sequence rather than shrinking desktop labels.
- Under reduced motion, render the full sequence statically.

### Ten-second comprehension test

After one glance, a new visitor should be able to answer:

1. **What is it?** A self-hosted media library.
2. **Why is it different?** It connects the whole workflow, from request and acquisition through playback and management.
3. **Where does it work?** Web and Apple platforms.
4. **What do I do next?** Install the server or join TestFlight.

If any visual treatment obscures those four answers, simplify it.

## Marketing-site information architecture

### Separate the marketing home from Docs

The current Docusaurus home moves from the hero directly into “Documentation / Where to start.” That makes the public home feel like a decorated docs index and interrupts the product story before the differentiator is established.

Recommended structure:

| Surface | Job | Recommended URL |
| --- | --- | --- |
| Marketing home | Explain the product, build desire, establish proof, route people to install or TestFlight | `/` |
| Product overview | Deeper lifecycle and media-experience story | `/product` |
| Platforms | Web, iPhone/iPad, and Apple TV support and screenshots | `/platforms` |
| Self-hosting | Deployment model, data control, architecture summary, requirements | `/self-hosting` |
| Docs home | Task-oriented installation, use, deployment, plugin, and developer documentation | `/docs/` |
| Changelog | Current product status and release evidence | Existing repository or docs changelog route |

The marketing home may live in Docusaurus technically, but its navigation and narrative should behave like a product site. Docs keeps its own sidebar and task hierarchy after the visitor enters `/docs/`.

### Recommended global navigation

**Left**

- Prismedia mark and wordmark → `/`
- Product → `/#lifecycle` or `/product`
- Platforms → `/#platforms` or `/platforms`
- Self-hosting → `/#self-hosting` or `/self-hosting`
- GitHub → repository

**Right**

- Docs → `/docs/`
- Join TestFlight → provided TestFlight URL
- Install → `/docs/getting-started/install`

On mobile, keep **Install** and **Join TestFlight** visible before the full menu. Do not make Docs the logo destination or the first primary nav item.

## Landing-page narrative, section by section

### 1. Hero: the category and promise

**Visitor question:** What is this, and why should I care?

Use the recommended hero verbatim. Pair the lifecycle prism with real product UI. Do not put a media-type ticker above the fold; it competes with the more important lifecycle idea.

### 2. The problem: one collection, too many handoffs

**Visitor question:** What problem is Prismedia actually solving?

**Section headline**

> Your collection is one thing. Managing it should feel that way.

**Body**

> Finding something, bringing it home, fixing its metadata, organizing its files, and finally enjoying it often means crossing a chain of disconnected tools. Prismedia keeps the item and its history intact from the first request to the next play.

**Visual**

A simple before/after lifecycle diagram. The fragmented side uses neutral outlined boxes, not competitor logos. The Prismedia side is one continuous entity timeline.

### 3. The lifecycle: from discovery to enjoyment

**Visitor question:** What does “whole lifecycle” mean in the product?

**Section headline**

> One lifecycle. Every medium.

**Five chapters**

1. **Discover** — Search provider-backed proposals for something not yet in the library.
2. **Acquire** — Review releases, send work to configured download clients, follow progress, and import into the intended library.
3. **Identify** — Compare metadata proposals, select artwork, preserve intentional edits, and keep provenance visible.
4. **Enjoy** — Watch video, listen to audio, read books and comics, or explore images and galleries with personal progress.
5. **Maintain** — Manage files, scans, subtitles, jobs, users, storage, and failures without losing the connection to the item.

Each chapter should use a real screenshot and one concrete proof line. The animation, if any, should follow the lifecycle once rather than loop every card independently.

### 4. Product proof: the item remains the item

**Visitor question:** Is this integration cosmetic, or is the data actually connected?

**Section headline**

> Wanted, downloaded, identified, and available. Still the same item.

**Body**

> Prismedia creates wanted media inside the library, carries its provider identity and acquisition state forward, then attaches imported files to that same entity. Metadata, progress, files, artwork, and history stay connected instead of being reconstructed at every handoff.

**Proof trio**

- Wanted items appear in their normal library views.
- Acquisition progress and history stay with the item.
- Files and catalog entities link back to each other.

This is the most defensible technical differentiator and should appear before the broad feature grid.

### 5. Media experiences: distinct, not bolted on

**Visitor question:** Does supporting many media types mean shallow support?

**Section headline**

> One library does not mean one generic experience.

**Body**

> Video, books, comics, audio, images, and galleries share identity, search, relationships, access, and progress. Each medium still gets the controls and presentation it needs.

**Experience groups**

- **Watch:** Movies, series, videos, direct play, stream copy, on-demand HLS, subtitles, transcripts, trickplay, and resume.
- **Read:** EPUB, PDF, CBZ, ZIP, and CBR experiences; paged and scrolling modes; personal progress; OPDS access for supported readers.
- **Listen:** Artists, albums, tracks, audiobooks, queue, shuffle, waveform scrubbing, persistent playback, and OS media controls.
- **Explore:** Images, galleries, people, studios, tags, collections, search, related entities, and artwork-led details.

Use one leading image per experience, not a grid of tiny feature cards.

### 6. Platforms: one product family

**Visitor question:** Where will I actually use it?

**Section headline**

> Your library belongs on every screen that matters.

Give Web, iPhone/iPad, and Apple TV equal card size and their own authentic image. Do not depict the native apps with browser chrome. Do not use one generic “mobile” card to cover both native and responsive web.

The platform copy is in [Platform storytelling](#platform-storytelling). Native claims remain launch-gated until verified.

### 7. Household and privacy

**Visitor question:** Is this a server dashboard or a household product?

**Section headline**

> Self-hosted for you. Personal for everyone at home.

**Body**

> Every household member signs in with their own account. Progress, favorites, ratings, library access, and content visibility stay personal, while administrators keep control of the server and shared collection.

**Proof**

- Administrator and member roles.
- Per-user library access.
- Per-user progress, favorites, ratings, and visibility.
- The same credentials across the web app and supported client surfaces.

Avoid “single-user” language; it is now materially out of date.

### 8. Self-hosting and operations

**Visitor question:** How much infrastructure does this add?

**Section headline**

> One image in. A complete library boots.

**Body**

> Prismedia packages PostgreSQL, ffmpeg, the web app, the .NET API, and the background worker into one Docker image. Mount `/data` and `/media`, expose port `8008`, and complete setup in the browser.

**Command**

```bash
docker pull ghcr.io/pauljoda/prismedia:latest
```

Do not publish this command as the main launch path until the `latest` tag is confirmed available. If launch uses `alpha` or another channel, make the visible command match the supported installation docs.

**Operations proof**

> Scans, probes, thumbnails, previews, subtitles, HLS, identify, acquisition, imports, and maintenance stay visible in Jobs.

### 9. Extensibility and interoperability

**Visitor question:** Will Prismedia fit an existing setup?

**Section headline**

> One library model. Open edges.

**Body**

> Prismedia owns its library model and keeps integrations behind adapters. Extend metadata workflows with native plugins, adapt compatible community scrapers, browse supported books through OPDS, or connect tested media clients through the experimental Jellyfin-compatible API.

**Status labels**

- Plugins — supported.
- OPDS 1.2 — supported for documented formats and readers.
- Jellyfin-compatible clients — experimental, with a link to the tested-client matrix.

Do not imply schema or implementation equivalence with a third-party server.

### 10. Trust and project proof

**Visitor question:** Why should I trust a young self-hosted project with my collection?

**Section headline**

> Inspect the product. Inspect the work behind it.

Use a compact proof board:

- Public source repository.
- Exact license label and link.
- Architecture and plugin documentation.
- Changelog and release channels.
- Current version and published image identity.
- Issue tracker or community destination.
- “Early,” “alpha,” “beta,” or “release” status stated plainly.

Do not use GitHub-star, install, or user counts until they are measured and current.

### 11. Final CTA: two first-class ways in

**Headline**

> Bring the whole collection into focus.

**Body**

> Run Prismedia on your own hardware, then take your library to the browser and the Apple screens you use every day.

**CTAs**

- **Install with Docker**
- **Join TestFlight**
- **Read the docs**
- **View on GitHub**

Docs belongs here as a support path, not as the marketing narrative itself.

## Feature grouping

The current home presents nine similarly weighted capability cards. That makes deployment, audio, metadata, HLS, jobs, files, Jellyfin compatibility, collections, and reading feel unrelated. Group features around the user's lifecycle instead:

| Group | User promise | Capabilities that support it |
| --- | --- | --- |
| Find what belongs | Discover beyond files already on disk | Provider-driven search, Request, canonical proposals, wanted entities, monitored items |
| Bring it home | Follow acquisition without losing identity | Indexers, configured download clients, release review, live progress, import, durable history, quality and monitoring controls |
| Make it yours | Turn files and provider data into a curated library | Scans, sidecars, Identify, Auto Identify, field review, artwork, people, studios, tags, collections |
| Enjoy every medium | Give each medium a purpose-built experience | Video playback, subtitles, transcripts, trickplay, audio player, readers, lightbox, personal resume |
| Keep it healthy | Make operations understandable | Files, exclusions, uploads and moves, Jobs, storage, settings, backups, accounts, diagnostics |
| Meet the rest of the ecosystem | Extend without surrendering the product model | Plugins, Stash-compatible scraper adapters, OPDS, experimental Jellyfin-compatible API, generated HTTP contracts |

On the marketing page, lead each group with an outcome and no more than three supporting proofs. Keep the exhaustive compatibility and format lists in Docs.

## Platform storytelling

Platform parity does not mean identical feature lists. Each platform should have a clear role inside one product family.

### Web — the complete library workspace

**Safe exact copy**

> **The whole library, in your browser.**
> Browse every medium, request and identify new items, manage files, tune settings, and watch background work from one responsive interface.

This is supported by repository documentation and current screenshots. Show both desktop and touch-first mobile web so “web” does not read as “desktop only.”

### iPhone and iPad — the personal companion

**Launch-gated exact copy**

> **Your library, made for the screen in your hand.**
> Browse, continue, watch, listen, and read in a native Prismedia experience for iPhone and iPad.

**CTA**

> Join TestFlight

Destination: <https://testflight.apple.com/join/c9bgDxr7>

Before publishing, verify each verb, supported device class, minimum OS, server requirement, authentication flow, background/audio behavior, and whether one TestFlight invitation covers both iPhone and iPad.

### Apple TV — the living-room experience

**Launch-gated exact copy**

> **The collection comes home to the biggest screen.**
> Prismedia for Apple TV turns the shared library into a focused living-room experience, with household accounts and progress connected to the rest of Prismedia.

Before publishing, verify app availability through the provided TestFlight, account switching, supported media kinds, playback modes, progress sync, and the exact term Apple requires for the platform (`Apple TV`, `Apple TV app`, or `tvOS app` in each context).

### Interoperable clients — useful, explicitly secondary

Infuse, Manet, Finamp, Symfonium, Swiftfin, and other compatible clients should not occupy the same platform row as Prismedia's own web and native experiences. Present them lower on the page under “Works with your setup,” with the experimental label and link to the tested-client matrix.

## Trust, source, and self-hosting

### Trust proposition

The strongest trust claim is not “privacy” in the abstract. It is **inspectable control**:

- Media is mounted from storage the operator controls.
- Application data and generated assets live under the documented `/data` mount.
- The server exposes one documented application port.
- Background work is visible as jobs instead of hidden daemon activity.
- Users and per-library permissions are first-class.
- Provider credentials are documented as encrypted at rest with an installation secret.
- Source, architecture, changelog, and release process are available for inspection.

### Safe exact trust copy

> **Your storage. Your server. One visible system.**
> Prismedia runs on hardware you control, keeps library state in its own database, and makes background work visible. Connect external providers and clients when they help; the collection remains yours.

This is more accurate than “no cloud” or “no external services.” Prismedia can call configured metadata, subtitle, indexer, and download services; the marketing should celebrate user choice rather than deny those boundaries.

### Open-source wording requires a decision

The current home and README say “open source,” while the root `LICENSE` is Creative Commons Attribution-NonCommercial-ShareAlike 4.0. The noncommercial restriction conflicts with the [Open Source Definition's requirement](https://opensource.org/osd) that a license not restrict a field of endeavor; the [Open Source Initiative specifically identifies noncommercial clauses](https://opensource.org/licenses/common-reasons-for-rejection-of-licenses) as a reason a license will not be approved. Creative Commons also [does not recommend CC licenses for software](https://creativecommons.org/faq/).

Until the project adopts an appropriate software license or receives a deliberate legal/product determination, use:

> **Source available**

and:

> **View the source**

Do not use “fork it,” “open source,” or an “open-source” badge as launch proof under the current license. If the licensing decision changes, replace the trust rail with the exact license name (for example, “MIT licensed”) rather than the generic phrase alone.

### Proof assets to prepare

- Current Docker image/channel badge that resolves to a published artifact.
- Current version badge sourced from `package.json`.
- License badge using the exact legal label.
- Last release date and changelog link.
- A current architecture diagram.
- Platform support matrix with status and last-tested version.
- Three current screenshots or short clips showing Request → acquisition, Identify, and playback/reading.
- A small “Your data paths” diagram for `/media`, `/data`, and port `8008`.

## Voice and tone

### Voice principles

**Clear before clever.** “Self-hosted media library” must appear before the prism poetry.

**Confident, not absolute.** Use concrete verbs and evidence. Avoid “everything,” “effortless,” “zero configuration,” “never,” and “no dependencies” unless the scoped claim is literally true.

**Calm, capable, and technically honest.** Prismedia should sound like a product a careful operator can trust, not like a breathless growth launch.

**Household-aware.** Speak to the person enjoying the collection as well as the person maintaining it.

**Medium-inclusive.** “Watch, listen, and read” is better than using “watch” as shorthand for every experience.

**Interoperable without being derivative.** Say that Prismedia works with clients, providers, and plugins. Do not define it through another product's name or schema.

**Specific status language.** Mark TestFlight, experimental compatibility, alpha/beta channels, and early-development limitations where they appear.

### Preferred vocabulary

| Prefer | Avoid |
| --- | --- |
| private, self-hosted media library | ultimate media server |
| whole media lifecycle | all-in-one replacement |
| one connected system | one app to replace them all |
| on hardware you control | no cloud, ever |
| configured providers and clients | no external dependencies |
| source available / exact license | open source, until licensing is resolved |
| tested clients | works with every Jellyfin app |
| one Docker image | zero configuration |
| household accounts | single-user app |
| first-class video, books, audio, images | supports every format |
| experimental Jellyfin-compatible API | built-in Jellyfin server |

### Sentence style

- Headlines: short, human, outcome-led, usually 3–9 words.
- Body: one idea per sentence; concrete nouns; active voice.
- Technical terms: use only after the product outcome is clear.
- Lists: no more than five items in marketing sections.
- Prism language: one strong metaphor per major page, not in every heading.

## Exact landing-page draft copy

The following is a coherent first draft, ready to adapt into a design. Bracketed notes are editorial gates, not public copy.

### Navigation

> Product · Platforms · Self-hosting · GitHub
> Docs · Join TestFlight · Install

### Hero

> **Private · self-hosted · made for the household**
>
> # Your whole media life. One private home.
>
> Prismedia is a self-hosted media library that keeps discovery, requests, downloads, metadata, files, playback, and reading connected—across web, iPhone, iPad, and Apple TV.
>
> **Install with Docker**
> **Join TestFlight**
> View the source
>
> One Docker image · One exposed port · Household accounts · Source available

[Verify native platform availability and the current Docker channel before publication.]

### Problem

> ## Your collection is one thing. Managing it should feel that way.
>
> Finding something, bringing it home, fixing its metadata, organizing its files, and finally enjoying it often means crossing a chain of disconnected tools. Prismedia keeps the item and its history intact from the first request to the next play.

### Lifecycle

> **ONE LIFECYCLE**
>
> ## From discovery to enjoyment, every handoff stays connected.
>
> **Discover**
> Search the providers you choose and review what belongs in your library.
>
> **Acquire**
> Find a release, follow the transfer, and import it into the right place.
>
> **Identify**
> Compare proposals, choose artwork, and keep the metadata you meant to keep.
>
> **Enjoy**
> Watch, listen, read, and browse with personal progress across the collection.
>
> **Maintain**
> See files, scans, subtitles, jobs, users, and failures in the same system.

### Entity continuity

> ## Wanted today. Available tomorrow. Still the same item.
>
> A request becomes a real library entity before the download begins. Provider identity, acquisition state, files, artwork, history, and progress stay connected as the item moves through Prismedia.
>
> **See Request in action**

### Experiences

> **EVERY MEDIUM**
>
> ## One library. Purpose-built ways to enjoy it.
>
> Video, books, comics, audio, images, and galleries share one foundation without being flattened into one generic interface.
>
> **Watch**
> Direct play when the client can handle it, stream copy when possible, and on-demand HLS when needed—plus subtitles, transcripts, trickplay, and resume.
>
> **Read**
> Open comics, EPUBs, and PDFs in focused readers with the controls and personal progress each format needs.
>
> **Listen**
> Move through artists, albums, tracks, and audiobooks with a persistent queue, waveform scrubbing, and system media controls.
>
> **Explore**
> Bring images, galleries, people, studios, tags, and collections into the same search and relationship model.

### Platforms

> **PRISMEDIA EVERYWHERE**
>
> ## Your library belongs on every screen that matters.
>
> **Web**
> The complete library workspace. Browse every medium, request and identify new items, manage files, tune settings, and watch background work from one responsive interface.
>
> **iPhone & iPad**
> Your library, made for the screen in your hand. Browse, continue, watch, listen, and read in a native Prismedia experience.
>
> **Apple TV**
> The collection comes home to the biggest screen, with household accounts and progress connected to the rest of Prismedia.
>
> **Join TestFlight**

[Verify every native-platform claim before publication.]

### Household

> ## Self-hosted for you. Personal for everyone at home.
>
> Every household member signs in with their own account. Progress, favorites, ratings, library access, and content visibility stay personal, while administrators keep control of the server and shared collection.

### Self-hosting

> **YOUR HARDWARE**
>
> ## One image in. A complete library boots.
>
> Prismedia packages PostgreSQL, ffmpeg, the web app, the .NET API, and the background worker into one Docker image. Mount your data and media, expose port `8008`, and complete setup in the browser.
>
> `docker pull ghcr.io/pauljoda/prismedia:latest`
>
> **Install with Docker**
> Read the deployment docs

[Confirm the `latest` tag before publication.]

### Operations

> ## The background should never be a black box.
>
> Scans, probes, thumbnails, previews, waveforms, subtitles, HLS, identify, acquisition, imports, and maintenance stay visible in Jobs, with progress and failures you can act on.

### Extensibility

> ## One library model. Open edges.
>
> Add metadata providers with native plugins, adapt compatible community scrapers, connect supported book readers through OPDS, or use tested media clients through Prismedia's experimental Jellyfin-compatible API.
>
> **Explore plugins**
> **See tested clients**

### Trust

> **BUILT IN PUBLIC**
>
> ## Your storage. Your server. One visible system.
>
> Prismedia runs on hardware you control, keeps library state in its own database, and makes background work visible. Connect external providers and clients when they help; the collection remains yours.
>
> **View the source**
> **Read the architecture**
> **See the changelog**

### Final CTA

> # Bring the whole collection into focus.
>
> Run Prismedia on your own hardware, then take your library to the browser and the Apple screens you use every day.
>
> **Install with Docker**
> **Join TestFlight**
> Read the docs

## Social and launch-copy seeds

These are seeds, not claims to publish without the verification gates below.

### Short launch post

> Your media is one collection. Why does managing it feel like five different systems?
>
> Prismedia is a private, self-hosted media library that keeps requests, downloads, metadata, files, playback, and reading connected.
>
> One lifecycle. Every medium.
>
> [Install] [TestFlight] [GitHub]

### Apple-platform post

> Prismedia is not only a server.
>
> The web is the complete library workspace. iPhone and iPad put the collection in your hand. Apple TV brings it to the room.
>
> Join the TestFlight: <https://testflight.apple.com/join/c9bgDxr7>

[Publish only after the platform support matrix is verified.]

### Self-hosted community post

> I built Prismedia because my collection was one system, but managing it was not.
>
> Prismedia connects discovery, acquisition, metadata, files, playback, reading, and background operations around the same library entities. It is video-first, with books, comics, audio, images, galleries, and household accounts treated as first-class parts of the product.
>
> It runs as one Docker image with mounted `/data` and `/media`, and the work is visible on GitHub. It is still young, so the launch page states experimental surfaces and release channels plainly.
>
> I would especially value feedback on [specific launch question].

### Hacker News title and opening

> **Show HN: Prismedia – a self-hosted media library for the whole lifecycle**
>
> Prismedia grew out of a simple frustration: media can belong to one collection while requests, downloads, metadata, files, playback, and operational state live in separate systems. Prismedia keeps those stages attached to the same library entities, then provides purpose-built experiences for video, books, comics, audio, images, and galleries.

The body should link architecture, installation, source, and a 60–90 second unedited product walkthrough. State the current release status and license exactly.

### TestFlight invitation

> **Help shape Prismedia on Apple platforms.**
>
> Connect to your Prismedia library, use it on the screens you already own, and tell us where the experience still breaks the sense of one connected product.
>
> **Join TestFlight**

### Release-note lead

> Prismedia [version] brings the whole media lifecycle into one self-hosted system: from provider-backed discovery and acquisition to identification, files, playback, reading, and household progress.

Follow with only the capabilities actually introduced or verified in that release.

### Thirty-second demo script

> “This is one item moving through Prismedia. I find it in Request, review its identity, and add it to the library. Prismedia follows the acquisition, imports the file, and keeps the same item, artwork, history, and metadata. Now it is ready to watch, listen to, or read—without rebuilding the story at every handoff. That is Prismedia: your whole media life, in one private home.”

Use a real captured flow. Do not simulate acquisition status or use prototype native footage as though it were shipped.

## Inspiration lessons translated into Prismedia ideas

These references were reviewed for communication and information architecture, not for copy or visual imitation.

| Reference | Useful lesson | Original Prismedia translation | What not to copy |
| --- | --- | --- | --- |
| [T3 Code](https://t3.codes/) | A specific category line and a concrete “from one surface” explanation establish the product in seconds. Product UI and source access appear as proof, not footnotes. | Say “self-hosted media library for the whole lifecycle,” immediately enumerate the lifecycle, then show Request → item → playback as one real flow. Give GitHub a visible trust role. | Its irreverent voice, testimonial wall, agent-control-plane language, or “steal our code” phrasing. Prismedia should be calmer and household-aware. |
| [Apple Mac](https://www.apple.com/mac/) | Platform/product lineup, short benefit-led cards, and an ecosystem chapter let distinct products feel like one family. | Give Web, iPhone/iPad, and Apple TV equal platform cards, then show how account and progress continuity connect them. Use short platform promises and authentic device imagery. | Apple's scale claims, retail structure, glossy hardware framing, or typography. Prismedia should show the actual self-hosted system and status. |
| [Immich](https://immich.app/) | The category, privacy benefit, server, and mobile app are understandable immediately; download and demo paths are direct. | Keep “self-hosted media library” in the hero and pair Docker with TestFlight. Show server and client as two parts of one product, not separate audiences. | A photo-backup framing or generic claim that privacy alone differentiates Prismedia. The lifecycle is the stronger story. |
| [Home Assistant](https://www.home-assistant.io/) | Local control, community, integrations, companion apps, and concrete proof coexist without hiding that cloud fallbacks can exist. | Say what runs locally, name optional integrations honestly, make native apps first-class, and prove trust through docs, source, status, and community. | Large integration counts or “no cloud” absolutes without measured evidence. |
| [Supabase](https://supabase.com/) | A broad platform becomes understandable by grouping products around one foundation and explaining that capabilities work independently but coherently. | Group Prismedia by lifecycle outcomes and use “one entity, many experiences” as the connective tissue. Let exhaustive technical details live one level deeper. | Developer-platform jargon, scale claims, or a dense grid of equally weighted modules. |
| [Tailscale](https://tailscale.com/) | Outcome-led navigation and persistent Docs/Download paths support both evaluation and action. Its broad homepage also demonstrates the risk of addressing too many segments at once. | Keep Product, Platforms, Self-hosting, Docs, TestFlight, and Install visible. Make the household self-hoster the clear primary audience, with contributors and integrations secondary. | Enterprise segment sprawl or a navigation taxonomy larger than the early product needs. |
| [Paperless-ngx](https://docs.paperless-ngx.com/) | A precise transformation story (“documents become a searchable archive”), clear local-data language, screenshots, and deep docs give a community project credibility. | State the transformation from fragmented media workflow to one connected lifecycle; show real screenshots; make limitations and data paths easy to inspect. | Turning the marketing home into a long feature inventory or using Docs as the only product narrative. |

## Claims that require verification before launch

| Claim or asset | Why it needs verification | Launch-safe fallback |
| --- | --- | --- |
| “Across web, iPhone, iPad, and Apple TV” | Native app source and support docs are not in this repository. | “Across the responsive web app, with Apple-platform apps in TestFlight.” |
| The TestFlight link is open and includes the intended platforms | Links can be full, expired, paused, or scoped to different builds/devices. | “Apple-platform TestFlight coming soon,” with email/community CTA. |
| Native iOS verbs: browse, continue, watch, listen, read | Exact implemented features are not documented here. | “A native Prismedia companion for iPhone and iPad,” followed only by verified features. |
| Native tvOS accounts, playback, and progress sync | These are central platform claims but not evidenced here. | “Prismedia for Apple TV is in TestFlight,” with no feature list. |
| Whether iPad is a distinct supported target | An iPhone app running in compatibility mode is not the same as an iPad experience. | Mention iPhone only until verified. |
| The minimum supported iOS, iPadOS, and tvOS versions | Required for support expectations. | Keep requirements in the TestFlight/App FAQ once confirmed. |
| “Source available” versus “open source” | The current CC BY-NC-SA 4.0 license is not OSI-approved for software because it restricts commercial use. | Use the exact license name and “View the source.” |
| `ghcr.io/pauljoda/prismedia:latest` exists and is the recommended install | The README warns that `latest`, `release`, and other manual channels may not be published yet. | Show the verified `alpha` or `dev` command and label its stability. |
| Launch maturity: early, alpha, beta, or release | README and release-channel docs allow multiple states. | Put the exact current channel beside every install CTA. |
| Screenshots represent the launch build | Current screenshots visibly show an older `v2.1.0 DEV` build while `package.json` is `2.4.3`. | Recapture on the release candidate and identify the build in the asset manifest. |
| Exact Request media-kind coverage | Current README, changelog, docs intro, and older request docs do not all describe the same integration model or breadth. | Use only the kinds verified in the release candidate and current Request UI. |
| “No external request app required” | First-party acquisition is shipped, but metadata providers, indexers, and download clients may still be configured externally. | “Request and acquisition state live in Prismedia; connect the providers, indexers, and download clients you choose.” |
| Prowlarr, direct Torznab/Newznab, qBittorrent, Transmission, SABnzbd, and slskd support | Changelog and README differ in which integrations they enumerate. | Move the full current matrix to Docs and link it. |
| Supported reader formats and features | README, playback docs, and OPDS support have different scopes. | Separate “built-in reader formats” from “OPDS acquisition formats.” |
| Jellyfin-compatible client list and behaviors | The compatibility layer is explicitly experimental and client versions change. | Publish a dated, versioned tested-client matrix. |
| “One Docker image” on every supported architecture | Packaging is documented, but launch architecture availability and image manifests should be checked. | State the architectures present in the published manifest. |
| Hardware-transcoding availability | Host devices, drivers, and codec support vary. | Say “hardware acceleration where configured and supported,” with Docs link. |
| “Your files stay on your server” | Configured providers can receive queries, hashes, or metadata; downloads come from external systems. | “Your media storage and library state remain on infrastructure you control.” |
| No telemetry or tracking | No explicit telemetry policy was found in the reviewed launch docs. | Do not claim it until code and network behavior are audited and documented. |
| Credentials “encrypted at rest” in every deployment mode | The unified image documents AES-256-GCM and secret handling, but custom deployments may differ. | Scope the claim to the documented unified image. |
| Free, forever, or pricing language | The current license permits noncommercial use but no launch pricing/support model is documented. | Use the exact license and omit pricing claims. |
| User, install, star, performance, or library-scale numbers | No launch-approved measurements were provided. | Use qualitative proof and real unedited captures. |
| “Complete,” “every format,” or “works with any client” | These are broader than the documented compatibility boundaries. | Use concrete supported types and dated matrices. |
| Public demo availability | No supported public demo URL was found. | Use a short recorded walkthrough until a privacy-safe demo exists. |

## Launch content checklist

Before implementing the marketing home:

1. Resolve or explicitly label the source-license wording.
2. Verify TestFlight scope, supported devices, native features, and server requirements.
3. Choose and publish the supported Docker launch channel.
4. Recapture screenshots and platform footage from the same release candidate.
5. Reconcile Request/acquisition documentation with the current first-party model.
6. Publish a dated platform and compatible-client matrix.
7. Prepare one real Request → acquisition → available-item demo.
8. Keep marketing analytics, if added, documented and consistent with the privacy story.
9. Run the ten-second comprehension test with at least five people who do not know the project.
10. Keep Docs task-oriented and link into it at points of intent rather than inserting docs navigation into the product story.

## Success criteria for the launch home

The marketing work is successful when:

- A new visitor can identify the category, lifecycle difference, platform family, and next action in under ten seconds.
- The first three sections explain the product without relying on a media-format inventory.
- The page shows at least one real connected lifecycle, not only static library grids.
- Web, iPhone/iPad, and Apple TV each receive explicit, authentic storytelling.
- Install, TestFlight, GitHub, and Docs are distinct actions with clear destinations.
- Trust claims are backed by links, status, exact versions, and exact license language.
- No copy frames Prismedia as a replacement for a named product.
- No experimental or native capability appears as a shipped fact without verification.
- The visual expression follows the existing neutral, sparse-spectrum, artwork-led design language.
