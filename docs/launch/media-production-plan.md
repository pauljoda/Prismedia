# Prismedia public-launch media production plan

Status: production brief, 2026-07-24

## Purpose

This plan defines the launch-quality stills and motion footage for Prismedia's
documentation landing page, README, announcement posts, and a 45–75 second
product film. It covers the Svelte web app plus the native iPhone, iPad, and
Apple TV clients. macOS is deliberately secondary.

The launch story is:

1. **One library. Every medium.** A coherent home for video, images, books,
   audio, and files.
2. **Private and self-hosted.** The collection and its processing stay on the
   household's hardware.
3. **Made for every screen.** The web app is mobile-first, while the native
   iPhone, iPad, and tvOS clients use platform-appropriate navigation and
   playback.
4. **Calm, capable control.** Playback, reading, metadata, files, acquisition,
   and jobs are understandable without turning the interface into a control
   panel.

The visual treatment must follow `docs/design-language.md`: true black and
opaque neutral content material, neutral silver app chrome, sparse
entity-spectrum cues, artwork-reactive detail atmosphere, and glass only for
controls or layers that genuinely float. Prism color should behave like
identity paint or a brief transition of light, not a glow applied to every
object.

## Decisions at a glance

- Produce a new, cleared capture library. Do not capture the household library
  and do not rely on post-production blur as the privacy boundary.
- Use app-only pixels for masters. Browser, Simulator, and Xcode window chrome
  never belongs in the master image.
- Keep the current landing-page structure in mind: one 16:9 hero image and four
  showcase images. The first replacement batch is therefore Dashboard, Video
  Detail, Files, and mobile Video Detail.
- Add native proof to the launch narrative with at least one iPhone image and
  one tvOS image. iPad should show reading or a wide split-view library, where
  its extra canvas makes a material difference.
- Treat web Dashboard, iPhone Now Playing, and tvOS Home as the proof trio.
- Capture stills and motion from the same approved states so the landing page,
  README, and product film feel like one campaign.
- Keep macOS to one optional secondary image after the web, iOS/iPadOS, and
  tvOS set is complete.

## Verified implementation baseline

### Current documentation landing page

`documentation-site/src/pages/index.tsx` currently tells the story with:

- the headline **One library. Every medium.**;
- `dashboard.png` as both the hero and the first interface showcase;
- `video-detail.png`, `files.png`, and `mobile-video-detail.png` as the other
  interface showcases;
- feature copy for files, HLS streaming, audio, metadata, visible jobs,
  reading, Jellyfin compatibility, collections, and the single Docker image.

The landing page adds its own restrained browser frame and window dots.
Therefore replacement screenshots should contain only Prismedia UI, not a
second browser frame. The landing page is responsive, but its desktop showcase
frames strongly favor 16:9 inputs. The existing portrait showcase caps the
mobile image at 370 CSS pixels.

One copy issue should be corrected when launch-page content is next edited:
the feature introduction still says "a single trusted user," while the current
product and changelog support household accounts and per-user library state.
The media should express private household ownership without visually
reinforcing the older single-user statement.

### Implemented web surfaces

Every web shot in this plan maps to a route that exists today.

| Surface | Real route | Verified implementation and capturable state |
| --- | --- | --- |
| Dashboard | `/` | `apps/web-svelte/src/routes/+page.svelte`; loaded Continue billboard, Continue and Recent shelves, and per-medium shelves |
| Video library | `/videos` | `apps/web-svelte/src/routes/videos/+page.svelte`; shared `EntityIndexPage`, initial media-wall presentation, grid/list/feed controls |
| Movie detail | `/movies/[id]` | player, artwork-reactive detail, metadata tabs, progress, and wanted/acquisition variants |
| Video detail | `/videos/[id]` | inline player, trickplay, quality state, subtitles, and dockable transcript |
| Gallery detail and lightbox | `/galleries/[id]` | gallery detail plus the universal lightbox and linked-entity details |
| Book detail and reader | `/books/[id]`, `/books/[id]/reader` | book progress plus comic, EPUB, PDF, and combined reading/listening flows |
| Audio | `/audio`, `/audio/[id]` | albums/tracks, album detail, waveform-capable persistent player |
| Files | `/files` | watched-root browser, selected-path detail, linked entities, scan and file operations |
| Search | `/search?q=…` | cross-kind grouped search with kind controls and related results |
| Identify | `/identify`, `/identify/[entityId]` | queue dashboard, provider search, parent/child proposal review, artwork choice |
| Request/acquisition | `/request`, `/request/[kind]/[id]`; entity Acquisition tabs | discovery, review, wanted state, releases, progress, monitoring, and history |
| Jobs | `/jobs` | worker health, job catalog, queued/active/recent work, failures |
| Settings | `/settings`, `/settings/[section]` | libraries, accounts, playback, subtitles, acquisition, generation, and diagnostics |

### Native client inventory

The sibling `/Users/pauldavis/Dev/Prismedia-SwiftUI` repository was inspected
read-only. It targets iOS 26, macOS 26, and tvOS 26 and contains three shared
Xcode schemes:

| Scheme | Platforms and launch value | Important implemented surfaces |
| --- | --- | --- |
| `PrismediaiOS` | iPhone and iPad; target device families 1 and 2 | adaptive tab/sidebar shell, Dashboard shelves, Search, entity grids and details, video playback, music Now Playing and queue, comic/EPUB/PDF readers, Files/Identify/Request/Jobs/Settings |
| `PrismediaTV` | Apple TV; target device family 3 | focus-first Home hero and shelves, Movies, Series, Collections, Search, season/episode rail, full-screen native/VLC playback controls, Settings |
| `PrismediaMac` | macOS | wide sidebar shell, entity grids/details, playback, music mini-player/Now Playing, administration; launch-secondary only |

Relevant native source anchors include:

- `PrismediaShared/App/Shell/PrismediaShellView.swift`
- `PrismediaShared/Features/Dashboard/DashboardView.swift`
- `PrismediaShared/Features/EntityGrid/EntityGridView.swift`
- `PrismediaShared/Features/EntityDetail/EntityDetailView.swift`
- `PrismediaiOS/Features/Playback/Audio/Components/MusicNowPlayingView.swift`
- `PrismediaShared/Features/Reader/ComicReaderView.swift`
- `PrismediaShared/Features/Reader/EPUBReaderView.swift`
- `PrismediaTV/Features/Television/TVHomeView.swift`
- `PrismediaTV/Features/Television/TVSeasonsDetailView.swift`
- `PrismediaTV/Features/Playback/Video/Components/TVPrismediaVideoPlayerView.swift`
- `PrismediaTV/Features/Playback/Video/Components/TVCompatibilityPlayerView.swift`

Available capture simulators at planning time include iPhone 17 Pro, iPhone 17
Pro Max, multiple current iPads, and Apple TV 4K (3rd generation). iOS 26.5 and
iOS 27 runtimes are installed. The available Apple TV runtime is tvOS 27.0,
including 4K and 1080p destinations. Use iOS 26.5 for launch iPhone/iPad
captures. Use tvOS 27.0 for the initial proof, but install and recapture on the
latest tvOS 26.x runtime before final release if exact target-OS appearance is
required.

The native repository also contains useful approved-brand candidates:

- shared `PrismediaLogo` at 1254×1254;
- shared colored and neutral prism marks at 640×590;
- the `PrismediaAppIcon.icon` bundle used by iOS and macOS;
- layered tvOS large and small app icons;
- tvOS Top Shelf images at 1920×720 and 2320×720, with 2× sources at
  3840×1440 and 4640×1440.

These assets may support end cards and store artwork after brand approval. They
are not substitutes for product screenshots. Confirm provenance and launch
approval for the icon source before publication.

## Capture standards

### Exact targets

| Target | Capture configuration | Master output |
| --- | --- | --- |
| Desktop web | Chromium, viewport 1920×1080 CSS px, device scale factor 2, dark appearance, app opened through the .NET host on port 8008 | 3840×2160 PNG |
| Mobile web | WebKit, viewport 402×874 CSS px, device scale factor 3, dark appearance, no browser UI | 1206×2622 PNG |
| iPhone native | iPhone 17 Pro, iOS 26.5, portrait, 402×874 pt at 3× | 1206×2622 PNG |
| iPad native portrait | iPad Pro 13-inch (M5), iOS 26.5, 1032×1376 pt at 2× | 2064×2752 PNG |
| iPad native landscape | iPad Pro 13-inch (M5), iOS 26.5, 1376×1032 pt at 2× | 2752×2064 PNG |
| tvOS proof/final | Apple TV 4K (3rd generation), full 4K output; proof on tvOS 27.0, final on matching stable target runtime when available | 3840×2160 PNG |
| macOS secondary | `PrismediaMac`, fixed 1512×982 pt app window at 2×, no desktop or unrelated windows | 3024×1964 PNG |
| Product film | 16:9 UHD, Rec.709, 30 fps; preserve a center-safe 4:5 and 9:16 action region for cutdowns | 3840×2160 ProRes 422 HQ master |

Web screenshot masters are deliberately 16:9 because the current landing hero
and showcase frames, README, social video, and the product film can all use
that canvas without inventing pixels. Documentation derivatives may be
downsampled to 1920×1080 WebP after approval.

### File naming and derivatives

Use:

`prismedia-launch-{id}-{story}-{surface}-{target}-{variant}.{ext}`

Examples:

- `prismedia-launch-w01-dashboard-web-desktop-4k.png`
- `prismedia-launch-mw02-video-detail-web-iphone17pro-portrait.png`
- `prismedia-launch-i01-now-playing-ios-iphone17pro-portrait.png`
- `prismedia-launch-p01-comic-reader-ipados-ipadpro13-landscape.png`
- `prismedia-launch-t01-home-tvos-appletv4k-4k.png`

Keep three levels:

1. **Master:** lossless PNG still or ProRes motion, exact dimensions above.
2. **Landing/README:** color-managed WebP, generally 1920×1080 for landscape;
   retain native-resolution portrait when file size permits.
3. **Social:** explicit crops such as 1920×1080, 1080×1350, and 1080×1920.
   Never overwrite the master with a crop.

Store a sidecar manifest for every approved asset containing capture date,
source commit, deployed image digest, route/navigation state, device/runtime,
viewport, data-set revision, license references, and the hash of the master.

### Device framing and cropping

- **Desktop web:** keep the master frameless. On the landing page, use the
  existing neutral window-dot frame. The crop must include the full Prismedia
  sidebar, page identity, and the primary content moment. Do not include an
  address bar, bookmarks, extensions, desktop wallpaper, or OS menu bar.
- **Mobile web:** no Safari controls or OS status bar in the master. Use a
  minimal neutral handset silhouette only in campaign composites. Preserve the
  bottom navigation and at least 24 CSS pixels of space above it.
- **iPhone:** preserve the native status and safe areas but exclude the
  Simulator window. For the landing page, use a flat dark device mask with no
  photorealistic reflections. A 2–4 degree rotation is acceptable in a
  cross-device composite; the proof image itself remains upright.
- **iPad:** landscape is preferred for the reader and wide shell. Use a simple
  dark bezel and preserve all four edges. Do not crop away the sidebar or
  floating reader controls that prove native adaptation.
- **tvOS:** use full-bleed 16:9 for film and screenshots. A thin display edge
  is acceptable on the landing page, but a living-room stock-photo mockup is
  unnecessary. Keep text and focused controls inside a 5% title-safe region.
- **macOS:** show the app window only. It should never lead the campaign or
  displace a web/iOS/tvOS proof image.

## Prioritized web shot list

### Desktop web

| ID | Priority | Asset and real route/state | Master | Narrative use |
| --- | --- | --- | --- | --- |
| **W01** | **P0 — hero-quality web proof** | **Dashboard, `/`. Fully loaded; one cleared in-progress item drives the Continue billboard; Continue, Recent, and at least two different medium shelves are visible. Freeze lazy loading and progress.** | `prismedia-launch-w01-dashboard-web-desktop-4k.png`, 3840×2160 | Landing hero and README lead. Proves "One library. Every medium." in one glance. |
| **W02** | **P0** | **Video detail, `/videos/{id}`. Cleared video paused on a composed frame; transcript docked at right with one selected subtitle track; quality/status chips and the detail identity remain visible.** | `prismedia-launch-w02-video-transcript-web-desktop-4k.png`, 3840×2160 | Replaces the current playback showcase and carries the film's streaming/subtitle beat. |
| **W03** | P0 | Videos, `/videos`. Media-wall/grid filled with cleared landscape thumbnails; toolbar collapsed to its clean default; no selection mode or loading placeholders. | `prismedia-launch-w03-video-library-web-desktop-4k.png`, 3840×2160 | Shows collection scale and calm density. Supports the capability strip and video-first positioning. |
| **W04** | **P0** | **Files, `/files`. A fictional watched root is open; one folder is selected; file details and one linked entity are visible; no destructive confirmation, absolute host path, or real filename.** | `prismedia-launch-w04-files-web-desktop-4k.png`, 3840×2160 | Replaces the landing Files showcase and proves ownership of source media. |
| **W05** | P1 | Gallery detail, `/galleries/{id}`, with universal lightbox open on cleared original art; linked details visible but secondary. | `prismedia-launch-w05-gallery-lightbox-web-desktop-4k.png`, 3840×2160 | Visually broadens the campaign beyond video and gives the film a bright artwork-led transition. |
| **W06** | P1 | Comic or EPUB reader, `/books/{id}/reader`; mid-book, toolbar intentionally shown, progress legible, no publisher/copyrighted page. | `prismedia-launch-w06-reader-web-desktop-4k.png`, 3840×2160 | Proves first-class reading rather than a catalog-only book claim. |
| **W07** | P1 | Audio album detail, `/audio/{id}`, with the persistent player active, waveform/transport visible, and a coherent cleared album identity. | `prismedia-launch-w07-audio-player-web-desktop-4k.png`, 3840×2160 | Carries the "every medium" promise into audio and creates an easy handoff to native Now Playing. |
| **W08** | P1 | Identify review, `/identify/{entityId}`; one high-confidence proposal with field comparison and three cleared artwork options, no provider credential or private search. | `prismedia-launch-w08-identify-review-web-desktop-4k.png`, 3840×2160 | Shows metadata as reviewable, not magical or opaque. |
| **W09** | P1 | Wanted movie/book detail with Acquisition selected, `/movies/{id}` or `/books/{id}`; safe release list or active progress, monitoring state, and history. | `prismedia-launch-w09-acquisition-web-desktop-4k.png`, 3840×2160 | Proves the current first-party Request/acquisition story. |
| **W10** | P2 | Jobs, `/jobs`; one completed scan and one active harmless thumbnail job, healthy worker, no failures, UUIDs, paths, or diagnostic output. | `prismedia-launch-w10-jobs-web-desktop-4k.png`, 3840×2160 | Supports "background jobs you can see" without leading the emotional story. |

### Mobile web

| ID | Priority | Asset and real route/state | Master | Narrative use |
| --- | --- | --- | --- | --- |
| **MW01** | P0 | Dashboard, `/`; portrait billboard plus the beginning of Continue, bottom bar fully visible, no browser chrome. | `prismedia-launch-mw01-dashboard-web-iphone17pro-portrait.png`, 1206×2622 | Proves the web app is intentionally mobile, not a squeezed desktop. |
| **MW02** | **P0** | **Video detail, `/videos/{id}`; cleared frame, controls at rest, title/primary actions and bottom navigation visible. Do not try to show the desktop transcript dock in portrait.** | `prismedia-launch-mw02-video-detail-web-iphone17pro-portrait.png`, 1206×2622 | Direct replacement for the current landing portrait showcase. |
| **MW03** | P1 | Videos, `/videos`; two-column or media-wall layout, wanted/progress markers represented sparingly, bottom bar visible. | `prismedia-launch-mw03-video-library-web-iphone17pro-portrait.png`, 1206×2622 | Companion to W03 and a README mobile proof. |
| **MW04** | P1 | Files, `/files`; safe root list and selected folder state with touch actions available, no raw host path. | `prismedia-launch-mw04-files-web-iphone17pro-portrait.png`, 1206×2622 | Proves operation surfaces remain usable on a phone. |
| **MW05** | P2 | Search, `/search?q=aurora`; grouped cleared results across at least three entity kinds, filters closed. | `prismedia-launch-mw05-search-web-iphone17pro-portrait.png`, 1206×2622 | Shows one search across the whole collection. |

## Prioritized iPhone and iPad shot list

Native shots use the real `PrismediaiOS` scheme. "Navigation state" below
describes how to reach the implemented screen; it is not a web route.

| ID | Priority | Device, navigation, and state | Master | Narrative use |
| --- | --- | --- | --- | --- |
| **I01** | **P0 — iOS proof** | **iPhone 17 Pro, iOS 26.5. Audio → cleared album → play track → open mini-player. Capture `MusicNowPlayingView` in Player mode at a stable elapsed time with artwork-reactive atmosphere and transport visible.** | `prismedia-launch-i01-now-playing-ios-iphone17pro-portrait.png`, 1206×2622 | Unmistakably native proof: platform typography, safe areas, floating controls, and media ownership. |
| **I02** | P0 | iPhone 17 Pro. Video tab → cleared video detail. Inline player paused; artwork-reactive hero/details and native tabs visible; no full-screen playback. | `prismedia-launch-i02-video-detail-ios-iphone17pro-portrait.png`, 1206×2622 | Pairs with W02 to show product continuity without pretending the two clients are identical. |
| **I03** | P1 | iPhone 17 Pro. Dashboard content state with Continue Watching, Recent, and two medium shelves; account menu closed and native tab bar visible. | `prismedia-launch-i03-dashboard-ios-iphone17pro-portrait.png`, 1206×2622 | Shows the native home and system tab adaptation. The implemented iPhone dashboard is shelf-led; do not fabricate a web-style billboard. |
| **I04** | P1 | iPhone 17 Pro. Browse/Search selected, query populated with cleared mixed-kind results, keyboard dismissed. | `prismedia-launch-i04-search-ios-iphone17pro-portrait.png`, 1206×2622 | Proves native browse/search and cross-kind library structure. |
| **P01** | **P0** | **iPad Pro 13-inch (M5), landscape. Books → cleared comic → Read. Capture `ComicReaderView` in two-page spread with toolbar shown and progress/chapter control legible.** | `prismedia-launch-p01-comic-reader-ipados-ipadpro13-landscape.png`, 2752×2064 | The strongest iPad-specific image; reading uses the wide canvas instead of merely enlarging a phone grid. |
| **P02** | P1 | iPad Pro 13-inch (M5), landscape. Wide shell with sidebar and Movies or mixed collection grid; selection/filter sheets closed; several cleared posters. | `prismedia-launch-p02-library-ipados-ipadpro13-landscape.png`, 2752×2064 | Proves adaptive native navigation and collection density. |
| **P03** | P2 | iPad Pro 13-inch (M5), portrait. EPUB reader mid-chapter with reading controls visible, using original launch-demo prose. | `prismedia-launch-p03-epub-reader-ipados-ipadpro13-portrait.png`, 2064×2752 | Optional editorial/reading proof for vertical placements. |

Do not use the current preview-fixture names or artwork as launch content.
Preview infrastructure is useful for deterministic behavior, but several
existing fixtures contain recognizable or personal-looking material and were
created for engineering validation, not publication.

## Prioritized tvOS shot list

All tvOS shots map to the implemented `PrismediaTV` scheme and focus-first
screens. Keep the focused element intentional and never leave focus on an
unrelated tab or invisible player surface.

| ID | Priority | Navigation and state | Master | Narrative use |
| --- | --- | --- | --- | --- |
| **T01** | **P0 — tvOS proof** | **Home tab. `TVHomeHero` shows a cleared in-progress movie or series; first Continue Watching shelf is visible; focus rests on the primary Resume action or first shelf item.** | `prismedia-launch-t01-home-tvos-appletv4k-4k.png`, 3840×2160 | Immediate proof that Prismedia belongs on the television, with a distinct cinematic composition. |
| **T02** | P0 | Series tab → cleared series → `TVSeasonsDetailView`. One season selected, episode rail visible, episode focus drives the artwork background and description. | `prismedia-launch-t02-season-detail-tvos-appletv4k-4k.png`, 3840×2160 | Shows focus-driven browsing, hierarchy, and artwork-reactive atmosphere. |
| **T03** | P1 | Start cleared episode/movie full-screen. Reveal player chrome once; transport, timeline, Audio Tracks, Subtitles, and Playback Speed are visible with one control focused. | `prismedia-launch-t03-playback-tvos-appletv4k-4k.png`, 3840×2160 | Proves serious television playback rather than a poster browser. |
| **T04** | P1 | Movies tab. Filled poster grid, one deliberate focus lift, sort/filter/display controls visible but closed. | `prismedia-launch-t04-movies-grid-tvos-appletv4k-4k.png`, 3840×2160 | A clean browse proof for announcement posts and the film montage. |
| **T05** | P2 | Search tab. Cleared query with movie, series, and collection results; keyboard dismissed; first relevant result focused. | `prismedia-launch-t05-search-tvos-appletv4k-4k.png`, 3840×2160 | Shows living-room discovery across the collection. |

The existing layered tvOS icon and Top Shelf images can be captured separately
for store presentation, but they are brand assets, not evidence of an
implemented screen.

## macOS, intentionally secondary

After all P0 and P1 web/iOS/tvOS assets pass review, optionally capture:

| ID | Priority | State | Master |
| --- | --- | --- | --- |
| M01 | P3 | `PrismediaMac` wide sidebar plus video or audio detail, app window only, no desktop | `prismedia-launch-m01-detail-macos-window.png`, 3024×1964 |

Do not use M01 in the landing hero or the first ten launch assets. The web app
already tells the desktop story, while iPhone/iPad/tvOS provide stronger proof
of client breadth.

## Marketing concepts, clearly not product screens

These are permitted only when labeled as composites or title cards during
production:

| ID | Concept | Truth boundary |
| --- | --- | --- |
| C01 | Prism mark opening: neutral light enters, the restrained entity spectrum separates, and the wordmark resolves | Brand animation, not an implemented app screen |
| C02 | Cross-device family: W01, I01, P01, and T01 arranged over black with thin neutral frames | Composite of real approved screenshots; the arrangement itself is a marketing concept |
| C03 | "One container · port 8008" system line with `/data` and `/media` labels | Explanatory motion graphic, not a settings or deployment screen |
| C04 | End card with Prismedia mark, "One library. Every medium.", docs/GitHub CTA | Campaign title card, not an in-product view |

Never render a fake Prismedia page, a fake native feature, or a device screen
that cannot be reproduced from an implemented state. If an idea cannot be
produced from the mappings above, label it `CONCEPT` in storyboards and review
exports.

## How the assets support the landing-page narrative

| Landing or campaign beat | Primary asset | Supporting assets |
| --- | --- | --- |
| Hero: One library, every medium | W01 | MW01, I03 |
| Video-first playback with HLS, subtitles, and transcript | W02 | MW02, I02, T03 |
| Source files and catalog stay connected | W04 | MW04 |
| Mobile is first-class | MW02 | MW01, I01 |
| Images and galleries are first-class | W05 | W01 shelf crop |
| Comics, EPUBs, PDFs, and audiobooks | P01 | W06, P03, I01 |
| Audio is first-class | I01 | W07 |
| Metadata is reviewable | W08 | film-only close crops of proposal/artwork choice |
| Request and acquisition are native to the product | W09 | detail/progress motion clip from the same state |
| Background work remains visible | W10 | short Jobs film beat |
| Made for the household's screens | C02 using approved W01/I01/P01/T01 | T02 |
| One Docker image, private hardware | C03 | W04 |

## Safe content, privacy, and redaction rules

### Source-of-truth rule

The privacy boundary is the capture data set, not Photoshop.

- Build a dedicated launch-demo library containing only original, commissioned,
  properly licensed, or documented public-domain media.
- Use fictional titles, people, studios, collections, filenames, providers,
  users, and server names created for this campaign.
- Keep every entity SFW. Disable NSFW visibility for the capture account.
- Use the same coherent fictional media universe across web and native clients
  so cross-device continuity is believable.
- Keep a license manifest beside the production masters. Record creator,
  source, license, attribution requirement, and approval for every artwork,
  still, clip, audio file, font, and music cue.
- Do not use commercial posters, celebrity photography, recognizable film/TV
  frames, book covers, album covers, or provider artwork without written
  clearance.

### Never visible

- Real usernames, display names, avatars, server hostnames, LAN addresses, or
  domains.
- Real library titles, artwork, people, tags, collections, playback history,
  ratings, favorites, or progress.
- Real paths, filenames, watched-root names, mount points beyond generic
  `/media`, download locations, indexer URLs, release names, hashes, API keys,
  tokens, QR codes, provider credentials, or session identifiers.
- UUIDs, stack traces, diagnostic output, browser developer tools, terminal
  windows, notifications, menu-bar extras, or other applications.
- Failure messages copied from the real development system.

### Pre-publication checks

1. Review the capture state before recording, not only the exported frame.
2. OCR every master and search the text against a denylist of real names,
   hostnames, address fragments, roots, providers, and known private titles.
3. Visually inspect artwork and video frames at 100% and as a contact sheet.
4. Inspect Files, Jobs, Settings, Identify, and Acquisition assets twice; these
   are the highest-risk surfaces for paths, IDs, provider data, and diagnostics.
5. Strip EXIF, filesystem paths, color-profile comments, and other unnecessary
   metadata from public derivatives. Keep only the intended color profile.
6. Have a second reviewer sign the manifest before anything enters the
   repository or a public upload.
7. If a private datum is found, discard and recapture from corrected source
   data. Do not ship a blurred or painted-over version of the contaminated
   master.

## Practical capture workflow on the rich-data host

The remote rich-data host is valuable for representative performance and the
real deployment topology, but its household library is not launch material.
Use it as follows:

1. **Freeze the build.** Record the local source commit. Build the current
   checkout for `linux/amd64`, deploy only to the development environment, and
   record the container image ID and digest. Never touch the live
   `prismedia.pauljoda.com` container.
2. **Validate privately, capture nothing.** An authorized operator may use the
   existing `dev-prismedia` instance to verify that the proposed routes and
   interaction states behave well at realistic scale. Do not record, stream,
   export, OCR, or copy its screens or database. This plan does not require
   access to its media.
3. **Create an isolated capture stack on the same development host.** Use the
   exact frozen image, a separate container name/network, a new PostgreSQL
   volume, a new `/data` volume, and a dedicated read-only launch-media mount.
   Do not clone the household database or media volume.
4. **Seed cleared representative data.** Match useful density characteristics
   with synthetic counts and relationships: several Continue items, multiple
   shelves, series/seasons/episodes, a mixed collection, a gallery, comic,
   EPUB, PDF, album/tracks, subtitles, waveform, one wanted item, one active
   harmless job, and linked files. Use only approved launch media.
5. **Create a least-privilege capture account.** Give it the minimum role
   needed for each shot, a generic display name, SFW-only visibility, and no
   access to any other library. Use a separate admin capture account only for
   Files, Identify, Jobs, and Settings shots.
6. **Use the canonical app surface.** Open the web app through the .NET host on
   port 8008. Capture with a browser automation profile that has no personal
   cookies, extensions, autofill, history, bookmarks, or notifications.
7. **Connect native simulators only to the isolated capture stack.** Use
   `PrismediaiOS` and `PrismediaTV` with temporary capture credentials. Clear
   simulator session state before and after the run. Do not point a recording
   simulator at the household instance.
8. **Stabilize the state.** Wait for fonts and artwork, disable automatic hero
   advance, pause cleared video at the approved frame, freeze progress values,
   close hover-only menus, and keep one intentional focus target on tvOS.
9. **Capture to encrypted scratch space outside the repository.** Save masters,
   motion takes, manifests, and contact sheets there. Only reviewed public
   derivatives may later be copied into `docs/screenshots` or the
   documentation-site static folder.
10. **Audit and tear down.** Run the privacy and license checks, revoke capture
    credentials, remove temporary sessions, and delete the isolated capture
    database/media volumes when retention is no longer required.

If an isolated remote stack is unavailable, use the native mock-server and a
local clean web stack with newly approved launch fixtures. Do not fall back to
capturing the private library.

## Existing documentation screenshot audit

The two screenshot directories are exact duplicates today:

- `docs/screenshots`
- `documentation-site/static/img/screenshots`

Maintain one reviewed master/manifest source during production and generate
the two published copies. Do not continue two manual source sets.

All existing desktop images are 1280×720. All existing mobile images are
1206×2622 and include browser/OS chrome. The set also contains recognizable
third-party media, personal-looking filenames, provider/release information,
and diagnostic identifiers. It is useful as historical documentation but is
not a safe or sufficiently sharp public-launch source set.

### Recapture first because the landing page uses them

| Existing file | Action | Replacement |
| --- | --- | --- |
| `dashboard.png` | Recapture P0 | W01; derive 1920×1080 documentation copy |
| `video-detail.png` | Recapture P0 | W02 |
| `files.png` | Recapture P0 | W04 |
| `mobile-video-detail.png` | Recapture P0; remove browser chrome | MW02 |

### Recapture for README and documentation

| Existing files | Action |
| --- | --- |
| `videos.png`, `search.png` | Recapture from W03 and a cleared desktop Search state |
| `audio.png`, `books.png` | Recapture from W07 and approved book/reader state |
| `galleries.png`, `collections.png`, `people.png` | Recapture with cleared artwork and fictional entities |
| `identify.png`, `plugins.png` | Recapture with fictional providers/packages and no credentials |
| `jobs.png` | Recapture with harmless synthetic jobs and no diagnostic identifiers |
| `settings.png`, `settings-subtitles.png` | Recapture with generic values and no accounts, paths, tokens, or provider details |
| `transcript.png` | Recapture as a dedicated W02 variant with the transcript selected |
| `mobile-dashboard.png`, `mobile-videos.png`, `mobile-files.png` | Recapture as MW01, MW03, and MW04; no browser chrome |

### Retire rather than one-for-one recapture

| Existing file | Action and replacement |
| --- | --- |
| `requests.png` | Retire. Replace with a new `request-discovery.png` from the current first-party Request discovery flow. |
| `request-detail.png` | Retire. Replace with `acquisition-detail.png` from W09, showing the current wanted entity/acquisition model. |

Retirement means remove the old image from launch surfaces and update the
referencing documentation when the new assets land. Do not delete historical
files as part of this planning task.

## 64-second product-film storyboard

Master: 3840×2160, 30 fps, approximately 64 seconds. The narration below is a
guide, not a final voice contract. Each line is short enough to breathe beside
the UI. Use an original or fully licensed score with a restrained low pulse,
quiet glass/control foley, and one prism-like spectral transition motif.

| Time | Picture and motion | Narration / on-screen line | Source |
| --- | --- | --- | --- |
| 0:00–0:04 | Black. A neutral beam resolves into the prism mark; a thin spectrum separates once and settles back to silver. | On screen: **One library. Every medium.** | C01 concept using approved brand assets |
| 0:04–0:12 | W01 dashboard enters full-bleed. Slow 3% push toward the Continue billboard; shelves remain readable. | "Bring every kind of media into one private, coherent library." | W01 state, captured as a short motion take |
| 0:12–0:19 | Clean match cuts across W03 video grid, W05 gallery lightbox, and P01 comic spread. Spectrum appears only as brief edge wipes keyed to each entity family. | "Browse video, images, books, and audio without changing how the collection feels." | W03, W05, P01 |
| 0:19–0:27 | W02: begin on the cleared video frame, scrub briefly through trickplay, then dock the transcript. Keep pointer motion minimal. | "Direct play when possible. Adapt when needed. Keep subtitles and context close." | W02 |
| 0:27–0:35 | W07 player bar rises; match the album art into I01 native Now Playing. A second beat reveals P01's iPad reader controls. | "Listen and read with native controls that remember where you stopped." | W07, I01, P01 |
| 0:35–0:43 | T01 Home full-bleed, then focus moves once into T02's episode rail; the background reacts to the selected artwork. | "Move from phone to tablet to the biggest screen in the house." | T01, T02 |
| 0:43–0:50 | W04 Files selection; a linked entity opens. Quick cut to W08 proposal choice, then W10 healthy active job. | "Your files, metadata, and background work stay visible and under your control." | W04, W08, W10 |
| 0:50–0:56 | Minimal topology line: `/media` and `/data` enter one container; port 8008 connects to the device family. No pseudo-dashboard. | "One Docker image. Your hardware. No cloud library required." | C03 concept |
| 0:56–1:04 | C02 cross-device family built from W01, I01, P01, and T01. Resolve to prism mark and CTA. | On screen: **Prismedia** / **One library. Every medium.** / **Self-host it.** | C02 and C04 concepts using real approved captures |

Motion rules:

- Prefer direct spatial continuity, focus changes, scrolls, and real control
  transitions over decorative camera moves.
- One action per shot. Do not speed-run menus.
- Use straight or very lightly eased cuts between content surfaces. Reserve the
  spectral transition for the four major medium changes and the opening.
- Do not add bloom to cards, icons, or navigation. Emitted light is limited to
  the opening/closing prism motif and very brief loading energy.
- Keep captions in the lower 16.7% band and important UI above it. Preserve a
  center-safe crop for 9:16 and 4:5 derivatives.
- Record 3–5 seconds of handles before and after every action.

## First ten assets to produce

This order unlocks the current landing page first, then establishes native and
television proof, then fills the breadth story.

1. **W01** — desktop web Dashboard hero, 3840×2160.
2. **W02** — desktop web video detail with docked transcript, 3840×2160.
3. **W04** — desktop web Files with a safe linked-entity state, 3840×2160.
4. **MW02** — mobile web video detail without browser chrome, 1206×2622.
5. **I01** — iPhone native Now Playing proof, 1206×2622.
6. **T01** — tvOS Home proof, 3840×2160.
7. **P01** — iPad comic reader, 2752×2064.
8. **W03** — desktop web video library/media wall, 3840×2160.
9. **W05** — desktop web gallery lightbox, 3840×2160.
10. **T02** — tvOS season/episode detail, 3840×2160.

## Production acceptance checklist

- [ ] Every published UI image maps to a verified route or native view named in
      this document.
- [ ] C01–C04 are labeled as marketing concepts in production files and review
      boards.
- [ ] W01 passes as a hero-quality web image at full landing width and as a
      1920×1080 derivative.
- [ ] I01 unmistakably proves the implemented native iOS client.
- [ ] T01 unmistakably proves the implemented focus-first tvOS client.
- [ ] P01 uses the iPad canvas meaningfully.
- [ ] macOS remains outside the first ten assets.
- [ ] All capture masters match the exact viewport/device dimensions.
- [ ] No browser, Simulator, Xcode, terminal, notification, or desktop chrome
      appears in masters.
- [ ] All content is cleared, fictional/public-domain/commissioned, SFW, and
      covered by the license manifest.
- [ ] OCR and manual privacy review find no real names, paths, hosts, IDs,
      provider data, or private library content.
- [ ] The deployed source commit and image digest are recorded.
- [ ] UI is fully loaded, sharp, color-managed, and free of transient focus,
      hover, skeleton, error, or toast states unless that state is the subject.
- [ ] Prismedia's neutral/spectrum/glass design language remains visible without
      added generic device glow or SaaS-style gradients.
- [ ] Landing, README, native, tvOS, and film derivatives come from the same
      approved masters.
