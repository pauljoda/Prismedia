# Prismedia Product Hunt launch

## Main information

**Name**

Prismedia

**Tagline**

One self-hosted library for your whole media lifecycle

**Description**

Prismedia turns a fragmented self-hosted media stack into one connected library.
Discover and request titles, follow acquisition, fix metadata, manage files, then
watch, listen, read, and browse without losing the item’s identity or your progress.
Video, music, audiobooks, eBooks, comics, images, and galleries get purpose-built
experiences across web, iPhone, iPad, and Apple TV—while your media and library state
stay on infrastructure you control.

**Launch tags**

1. Entertainment
2. Apple TV
3. iOS

**Links**

1. Website: <https://pauljoda.github.io/Prismedia/>
2. GitHub: <https://github.com/pauljoda/Prismedia>
3. TestFlight: <https://testflight.apple.com/join/c9bgDxr7>
4. Documentation: <https://pauljoda.github.io/Prismedia/docs/intro>

The Product Hunt **Open source** switch remains off. The public repository uses a
CC BY-NC-SA 4.0 noncommercial license, which is source-available but not an
OSI-approved open-source license.

## First maker comment

Hi Product Hunt — I built Prismedia because my media was already one collection, but
using it meant crossing a chain of unrelated apps. One handled requests, several more
handled acquisition, another metadata, another playback or reading, and every handoff
repeated identity, setup, state, and troubleshooting.

Prismedia starts from the opposite idea: an item stays the same item from the moment
someone wants it through acquisition, identification, file management, and finally
watching, listening, or reading it.

What’s in this launch:

- One Docker image for the server, web app, PostgreSQL, ffmpeg, API, and background
  worker
- A complete web workspace for browsing, requests, metadata, files, settings, and
  operations
- Native iPhone and iPad apps with video, music, audiobooks, and customizable
  EPUB/PDF/comic reading
- A focus-first Apple TV app with a custom native player for original-quality direct
  playback when the device supports the source
- First-class experiences for video, music, audiobooks, eBooks, comics, images, and
  galleries
- Household accounts, personal progress, public source, and task-oriented
  documentation

Prismedia is still early, and the native apps are currently open to a limited
TestFlight group. I’d especially value feedback on where the media lifecycle still
feels fragmented, which native experience matters most to you, and what you need to
trust an app with your household library.

## Media

Use the files in `assets/` in numeric order:

1. `01-one-private-home.png`
2. `02-one-media-lifecycle.png`
3. `03-native-playback.png`
4. `04-custom-reader.png`
5. `05-purpose-built-media.png`
6. `06-self-hosted.png`

Use `prismedia-thumbnail.png` as the square launch thumbnail.

The silent launch film remains available on the public site. Product Hunt accepts a
YouTube URL rather than a direct MP4 for its video field, so a public YouTube upload
is intentionally left as a separate publishing decision.

## Regenerating the gallery

From the repository root:

```sh
node docs/launch/product-hunt/render-gallery.mjs
```
