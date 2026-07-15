# Prismedia Design Language

## The idea

Prismedia is the place where a collection comes together. Its visual metaphor is white light entering a prism and separating into a spectrum: one library in, many kinds of media out.

The interface remains permanently dark so artwork stays dominant. Neutral silver identifies Prismedia itself. Spectrum color identifies content families in small, deliberate moments; it does not tint every surface that belongs to them. Color is semantic rather than decorative.

## Principles

- **One source, many colors.** The neutral Prismedia shell is the white-light source; entity families inherit stable spectrum pairs.
- **Black is the canvas.** The root and structural surfaces are neutral. Spectrum atmosphere belongs to artwork-reactive detail contexts and literal prism moments, not the global shell.
- **Artwork leads on detail pages.** When real entity artwork is available, derive background, primary, and secondary colors from it. Otherwise use the entity-family pair.
- **Glass belongs to chrome.** Navigation, sticky toolbars, menus, sheets, dialogs, search, players, and floating controls can be frosted. Cards, lists, grids, and content panels remain opaque.
- **Shared components own the language.** Change tokens and shared building blocks before writing route-specific styling.
- **Motion explains state.** Movement can reinforce focus, selection, loading, and hierarchy, but every state also has a non-color cue and a reduced-motion treatment. Persistent controls do not glow.
- **Mobile first, desktop first-class.** Touch interactions come first; desktop expands density and navigation without becoming a different product.

## Brand marks

Use the colored prism mark for app identity in the sidebar, mobile shell, sign-in surfaces, favicons, and product documentation. Use the neutral prism only inside the loading animation. The full light-in/light-out logo is reserved for marketing and icon contexts; it is not a routine chrome mark. The red-tinted prism is reserved for the NSFW visibility control and its active state; it is never the default brand mark or a general error icon.

Do not recolor, crop, or place the prism in a generic badge. Preserve its aspect ratio and transparent padding.

## Color system

### Neutral foundation

| Role | Value | Use |
| --- | --- | --- |
| Canvas | `#000000` | App root and dominant atmosphere |
| Surface 1 | `#111214` | Recessed wells and quiet panels |
| Surface 2 | `#191a1e` | Cards and structural content |
| Surface 3 | `#24262b` | Raised content surfaces |
| Surface 4 | `#303238` | Menus and contextual layers |
| Neutral accent | `#c7c9cc` | App-level focus and identity |
| High contrast | `#ebebeb` | Strong neutral emphasis |

### Prism spectrum

| Name | Value |
| --- | --- |
| Red | `#ff141f` |
| Orange | `#ff570a` |
| Yellow | `#ffc71f` |
| Green | `#1fc247` |
| Cyan | `#0ab3e6` |
| Blue | `#0d47ff` |
| Violet | `#7a14f5` |
| Magenta | `#d60de0` |

The spectrum values are exact brand tokens for the logo, prism beam, and other literal emitted-light moments. Persistent entity chrome uses the paired `materialSpectrum` tokens: the same hues with controlled lightness and chroma so they read as flat paint. Use opacity and `color-mix()` to create borders and material fills; broad colored glow is not part of the default UI language.

| Material token | Value |
| --- | --- |
| Red | `#b3484d` |
| Orange | `#b76337` |
| Yellow | `#9e873b` |
| Green | `#4d925d` |
| Cyan | `#3b869c` |
| Blue | `#536fb0` |
| Violet | `#775ca5` |
| Magenta | `#9a4f9d` |

### Color budget

The everyday product surface should read as roughly 90–95% neutral. Use entity color only where it adds information:

- a short taxonomy marker beside a section or page heading;
- a thin progress or active-state rail;
- a selected control that also has a border, shape, or position cue;
- artwork-derived atmosphere inside an entity detail experience;
- the colored brand mark and literal prism/loading sequence.

Every page must include one accent moment that communicates its identity without tinting the whole page. A library page can spend that moment on the top edge of its grid toolbar; Settings uses muted section icons; an entity detail page uses artwork atmosphere and its active-tab rail. Prefer one clear location over several competing accents.

Do not repeat the same entity color across a section label, icon, card border, button fill, background gradient, and shadow. Neutral typography, surfaces, and borders establish hierarchy first.

### Entity identity

| Family | Primary → secondary |
| --- | --- |
| Video | Red → orange |
| Movie | Orange → yellow |
| Series, season, episode | Yellow → green |
| Gallery | Green → cyan |
| Book, author, volume, chapter, page | Cyan → blue |
| Image | Blue → violet |
| Audio, artist, album, track | Violet → magenta |
| Collection | Magenta → red |
| People | Red → violet |
| Studios | Orange → magenta |
| Tags | Green → yellow |

Use these pairs for small heading markers, selected rails, progress, and navigation-section defaults. Keep adjacent heading text, icons, cards, and buttons neutral. A user may override a navigation section color, but that does not redefine the entity family.

## Artwork-reactive detail surfaces

Entity detail pages publish three semantic values: `background`, `primary`, and `secondary`.

1. Sample only real, already-loaded artwork. Never issue a duplicate image request for palette extraction.
2. Downsample before analysis and fail safely when the browser blocks canvas access.
3. Keep the background near black and use primary/secondary as restrained radial atmosphere and sparse interaction accents.
4. Maintain readable text contrast. Primary body text remains neutral.
5. Animate a palette change quickly, around 180ms, and remove that transition for reduced motion.
6. Only an explicit API backdrop becomes a hero image. Poster or cover art may drive the atmosphere without being enlarged into a synthetic hero.
7. If artwork is absent or extraction fails, fall back to the entity-family colors.

## Surfaces

### Content material

Cards, media, rows, grids, chips, and repeated panels use opaque neutral surfaces, a subtle neutral border, and restrained shadow. They may inherit an entity accent for state, but they do not use backdrop blur.

```css
background: var(--color-surface-2);
border: 1px solid var(--color-border-subtle);
border-radius: var(--radius-md);
box-shadow: var(--shadow-card);
```

### Frosted chrome

Glass communicates that a control floats above content. Use it for the sidebar, top bar, bottom navigation, sticky toolbars, menus, sheets, dialogs, and player controls.

```css
background: var(--color-overlay-glass);
border: 1px solid var(--color-border-default);
backdrop-filter: saturate(1.05) blur(var(--glass-blur-md));
box-shadow: var(--shadow-panel);
```

Avoid glass-on-glass stacking. When chrome contains grouped controls, use one glass parent and clear or solid child controls.

## Loading

Full blocking states use the responsive prism loader:

1. white light travels in from beyond the left edge;
2. it strikes the neutral prism;
3. the prism gains color at impact;
4. seven spectrum bands expand beyond the right edge across the available height;
5. the scene resets over a 2.8-second cycle.

The component owns the full available width so beams naturally adapt to cards, panels, or the page. The transparent prism assets are layered exactly. Under `prefers-reduced-motion`, render a static colored prism with a partially visible expanded spectrum. Use compact progress indicators for pagination, playback buffering, thumbnail loading, or refreshes where content remains visible.

## Geometry and type

Use the shared radius scale: `4`, `6`, `10`, `14`, `18`, and `24px`. Controls must not become pills unless the shape has a genuine semantic reason. Use the existing web font voices—Cinzel for rare brand display, Geist for product headings, Inter for body, and JetBrains Mono for utility metadata—while keeping hierarchy close to native system typography.

## Interaction and accessibility

- Every primary action works without hover.
- Focus is visible through shape, border, and motion as well as color.
- Text and controls meet contrast requirements over both artwork and glass.
- Motion respects `prefers-reduced-motion`.
- Blocking loaders expose one status announcement; decorative beams and prism layers are hidden from assistive technology.
- Color pickers store validated six-digit hex values and retain visible labels.

## Review checklist

1. Is the surface neutral by default, with entity color limited to an informative marker or state?
2. Is glass limited to a floating or chrome layer?
3. Does a detail page use real artwork safely and preserve the explicit-hero policy?
4. Does the loading state use the prism loader only when content is blocked?
5. Are mobile touch targets, desktop density, focus, contrast, and reduced motion all verified?
6. Was the shared component or token changed instead of duplicating the recipe in a route?
