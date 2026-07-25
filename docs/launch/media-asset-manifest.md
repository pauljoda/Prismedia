# Launch media asset manifest

Capture date: 2026-07-24  
Published derivative root: `documentation-site/static/img/showcase/`  
Local lossless master root: `~/Movies/Prismedia Launch/Masters/2026-07-24/`

Lossless native and web captures stay outside the repository so the deployed
documentation artifact contains only optimized derivatives. Every derivative
retains its source resolution and is used without browser, Simulator, or Xcode
window chrome.

## Published product proof

| Asset | Source state | Resolution | Landing-page role |
| --- | --- | ---: | --- |
| `web-dashboard.webp` | Authenticated web dashboard with mixed Continue and recent media | 1280×720 | Hero and web platform proof |
| `web-request.webp` | Request search with a movie candidate query; no request submitted | 1280×720 | Discovery and acquisition lifecycle |
| `web-detail.webp` | Artwork-reactive movie detail | 1280×720 | Video detail experience |
| `web-movies.webp` | Current movie library grid | 1280×720 | Web platform proof |
| `ios-dashboard.webp` | Native iPhone dashboard | 1320×2868 | Hero and native mobile proof |
| `ios-movies.webp` | Native iPhone movie library | 1320×2868 | iPhone and iPad platform story |
| `ios-book-combined.webp` | Book detail with reading and listening progress together | 1320×2868 | Combined ebook and audiobook story |
| `ios-reader.webp` | Reader with dark theme, Literary Serif, 120% size, 105% weight, and 1.7 line spacing | 1320×2868 | Primary reader experience |
| `ios-reader-settings.webp` | Reader settings sheet showing typography and spacing controls | 1320×2868 | Supplemental customization proof |
| `ios-audiobook.webp` | Native audiobook Now Playing, paused | 1320×2868 | Audiobook experience |
| `ios-music-player.webp` | Native music Now Playing, paused | 1320×2868 | Music experience |
| `tvos-dashboard.webp` | Native Apple TV dashboard | 3840×2160 | Living-room platform proof |
| `tvos-movies.webp` | Native Apple TV movie library | 3840×2160 | Supplemental television proof |
| `tvos-playback.webp` | Movie paused with title, direct-play state, codecs, timeline, and playback chrome visible | 3840×2160 | Primary Apple TV playback proof |

The landing page also uses the existing reviewed
`documentation-site/static/img/screenshots/galleries.png` capture because the
current rich-data account has no populated gallery root.

## Original brand atmosphere

`prism-refraction-hero.png` and its optimized WebP derivative were generated
from `docs/logo-mark.png` as an original launch asset. The prompt direction was:

> Premium 16:9 black studio composition using the exact Prismedia prism mark,
> with a thin white beam entering from the left and a controlled six-color
> spectrum exiting to the right; realistic optical glass and a restrained
> reflective floor; no text, interface, neon, or extra marks.

The unmodified generation is retained in the Codex generated-image archive for
this launch task.

## Social sharing

`documentation-site/static/img/prismedia-social-card.png` is the dedicated
1200×630 Open Graph and large-card preview. It combines the reviewed
`prism-refraction-hero.png` atmosphere with the public positioning line,
platform scope, and watch/read/listen/request experience rail. The launch site
uses it for Open Graph, X/Twitter, and `SoftwareApplication` structured data.

## Product Hunt launch kit

`docs/launch/product-hunt/assets/` contains the reviewed campaign thumbnail and
six 1270×760 launch-gallery cards. The editable source, listing copy, and
reproducible Playwright renderer live beside those exports:

- `01-one-private-home.png` introduces the one-light-in, every-medium-out story.
- `02-one-media-lifecycle.png` follows Discover → Request → Acquire → Manage →
  Enjoy.
- `03-native-playback.png` calls out Prismedia's custom Apple TV player,
  device-native codecs, original-quality video, and lossless audio.
- `04-custom-reader.png` proves the reader typography, appearance controls, and
  combined reading/listening position.
- `05-purpose-built-media.png` shows native music, audiobook, and video
  experiences beside the complete media spectrum.
- `06-self-hosted.png` explains the single-image server topology and web,
  iPhone/iPad, and Apple TV clients.

The Product Hunt thumbnail is 240×240, every gallery card is 1270×760, and all
exports are below 1 MB. `render-gallery.mjs` also fails if the output geometry
changes or a marked copy block clips.

The same launch source produces three scheduled-campaign formats:

- `social-card-product-hunt.png` is a 1200×630 link and announcement card.
- `social-square-product-hunt.png` is a 1080×1080 prism-first feed card.
- `social-story-product-hunt.png` is a 1080×1920 vertical story with a real web
  request flow, paused Apple TV playback chrome, and the customized iOS reader.

Every social export stays below 1 MB. The renderer now enforces a 5 MB hard ceiling
in addition to exact geometry and marked-copy overflow checks.

## Publication review

Before a public campaign or app-store submission, review every artwork,
book-cover, and playback frame for the applicable promotional-use rights.
Replace any unapproved third-party media with licensed launch fixtures while
preserving the same UI state and composition.
