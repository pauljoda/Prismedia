# Apple review media sources

Prepared: 2026-07-25  
Environment: `https://apple-prismedia.pauljoda.com`  
Purpose: private functional testing by Apple App Review and local simulator
validation

Only media listed here may be placed in the Apple review environment. The
environment must not mount, copy, or expose the household Prismedia library.
Source files stay on the remote review host and are not committed to git.

Checksums and the exact final paths are filled from the deployed files after
download and before App Store submission.

## License and attribution rules

- Preserve every author, project, license, and source URL below.
- Do not remove credits embedded in a source file.
- Where a Creative Commons license requires attribution, keep the credit in this
  manifest and mirror it into the media sidecar metadata shown by Prismedia.
- Mark excerpts, container conversions, thumbnails, and comic packaging as
  adaptations when applicable.
- The review server is a controlled demonstration environment, not a general
  public media service.

## Planned library

| Experience | Review fixture | Source and license | Deployment notes |
| --- | --- | --- | --- |
| Movie / video playback | **Big Buck Bunny** by Blender Foundation | [Official Blender download](https://download.blender.org/demo/movies/BBB/bbb_sunflower_1080p_30fps_normal.mp4.zip); [official CC BY 3.0 notice](https://download.blender.org/ED/poster.pdf) | Use the unmodified 1080p MP4 in a single-file movie folder. Keep embedded credits. |
| Series / episodes | **Open Movie Sampler**, using the official *Elephants Dream* and *Caminandes: Gran Dillama* teasers | [Official Blender demo directory](https://download.blender.org/demo/movies/); Blender open-movie media is distributed under Creative Commons licenses | Put each unmodified teaser in `Open Movie Sampler/Season 01/` with `S01E01` and `S01E02` filenames. Confirm the license notice for each selected file before deployment. |
| Ebook | **Alice's Adventures in Wonderland**, Lewis Carroll, illustrated by John Tenniel | [Standard Ebooks edition and rights notice](https://standardebooks.org/ebooks/lewis-carroll/alices-adventures-in-wonderland/john-tenniel); [compatible EPUB](https://standardebooks.org/ebooks/lewis-carroll/alices-adventures-in-wonderland/john-tenniel/downloads/lewis-carroll_alices-adventures-in-wonderland_john-tenniel.epub) | Carroll and Tenniel are public-domain creators. Standard Ebooks states this edition is believed free of US copyright restrictions. |
| Audiobook | **Alice's Adventures in Wonderland (version 4)**, read by Eric Leach | [LibriVox catalog and public-domain notice](https://librivox.org/alices-adventures-in-wonderland-by-lewis-carroll-5/); [M4B download](https://archive.org/download/alices_adventures_1005_librivox/AlicesAdventuresInWonderlandV5_librivox.m4b) | LibriVox recordings are dedicated to the public domain in the United States. Keep it beside the EPUB so Prismedia presents one book with reading and listening renditions. |
| Comic | **Pepper&Carrot — Episode 30: Need a Hug?** | [Episode source and attribution](https://www.peppercarrot.com/en/webcomic-sources/ep30_Need-a-Hug__files.html), Creative Commons Attribution 4.0 International | Download the English low-resolution pages `E30P00` through `E30P07`, add the attribution below and `ComicInfo.xml`, then package them without image edits as a CBZ. |
| Music | **Ascending the Vale**, **Dreamer**, and **The Entertainer**, Kevin MacLeod | Official Incompetech pages for [Ascending the Vale](https://www.incompetech.com/music/royalty-free/index.html?isrc=USUAN1600064), [Dreamer](https://www.incompetech.com/music/royalty-free/index.html?isrc=USUAN1600043), and [The Entertainer](https://www.incompetech.com/music/royalty-free/index.html?isrc=USUAN1900059), Creative Commons Attribution 4.0 International | Download the MP3 files from the official track pages. Preserve title/artist metadata and use one review-only album folder. |
| Images | Three CC0 demonstration photographs | Wikimedia Commons: [Augmented reality](https://commons.wikimedia.org/wiki/File:Augmented-reality-1957411_1920.jpg), [Photo studio](https://commons.wikimedia.org/wiki/File:Photo-studio.jpg), and [Little free library](https://commons.wikimedia.org/wiki/File:Little_free_library_stand_(Unsplash).jpg) | Verify each file page still displays CC0 before downloading the full-resolution original. |
| Gallery | **Open Creative Spaces** | The same three CC0 photographs above | Place copies in one gallery folder so both standalone image and gallery navigation can be tested. |

## Required attribution text

### Big Buck Bunny

> Big Buck Bunny © 2008 Blender Foundation — licensed under Creative Commons
> Attribution 3.0. https://peach.blender.org/

### Pepper&Carrot episode 30

> Pepper&Carrot, episode 30 “Need a Hug?”, is licensed under Creative Commons
> Attribution 4.0 International. Hereva creation: David Revoy. Lead maintainer:
> Craig Maloney. Writers: Craig Maloney, Nicolas Artance, Scribblemaniac, and
> Valvin. Correctors: Alex Gryson, CGand, Hali, Marno van der Maas, Moini, and
> Willem Sonke. Source:
> https://www.peppercarrot.com/en/webcomic-sources/ep30_Need-a-Hug__files.html

### Incompetech album

> “Ascending the Vale”, “Dreamer”, and “The Entertainer” by Kevin MacLeod
> (incompetech.com), licensed under Creative Commons Attribution 4.0.
> https://creativecommons.org/licenses/by/4.0/

## Deployed-file ledger

| Relative path | Bytes | SHA-256 | Verified source status |
| --- | ---: | --- | --- |
| Pending deployment | — | — | — |

## Pre-submission rights check

1. Open every source page again.
2. Confirm the license or public-domain statement has not changed.
3. Confirm the deployed checksum matches the file used for screenshots and
   review.
4. Confirm no household artwork, metadata, usernames, or media entered the
   review environment.
5. Confirm credits remain visible through this public manifest and sidecar
   metadata.
