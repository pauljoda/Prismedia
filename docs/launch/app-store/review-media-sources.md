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

The reproducible seed command is:

```sh
scripts/launch/seed-apple-review-library.sh \
  --target /home/paul/docker-data/apple-prismedia-media
```

The script refuses any other target and refuses to overwrite a non-empty review
library.

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
| Ebook | **Alice's Adventures in Wonderland**, Lewis Carroll, illustrated by John Tenniel | [Standard Ebooks edition and rights notice](https://standardebooks.org/ebooks/lewis-carroll/alices-adventures-in-wonderland/john-tenniel); [compatible EPUB](https://standardebooks.org/ebooks/lewis-carroll/alices-adventures-in-wonderland/john-tenniel/downloads/lewis-carroll_alices-adventures-in-wonderland_john-tenniel.epub?source=download) | Carroll and Tenniel are public-domain creators. Standard Ebooks dedicates its production work to the public domain with CC0 and states this edition is believed free of US copyright restrictions. |
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
| `ATTRIBUTION.md` | 2,275 | `fa4da143bdc07433cf1ebb47c63a29409b00310a2af9d01ca3563f8c8434c11c` | Generated from this manifest |
| `Audio/Kevin MacLeod/Open Review Album/01 - Ascending the Vale.mp3` | 7,925,486 | `c7cb11967d826336b5b24f938a56e0feb05162a8c17dd32942faacfc1f9017f2` | Incompetech CC BY 4.0; metadata-only adaptation |
| `Audio/Kevin MacLeod/Open Review Album/02 - Dreamer.mp3` | 6,531,163 | `53b7a9fb226395de60c03e32f57b1393a53d56e4a8668c116852af2a6d8f46b2` | Incompetech CC BY 4.0; metadata-only adaptation |
| `Audio/Kevin MacLeod/Open Review Album/03 - The Entertainer.mp3` | 8,247,448 | `4761cd85d1aaa9772b15e68ffff7f65d0668637fd389e3fa9a41cc4348cf8389` | Incompetech CC BY 4.0; metadata-only adaptation |
| `Audio/Kevin MacLeod/Open Review Album/cover.jpg` | 72,213 | `2f7c39054268a90629e61c0738bb5111a25ef3230e16c7fdc354eb9b6823182a` | Cropped from Prismedia original launch artwork |
| `Books/David Revoy/Pepper&Carrot/Pepper&Carrot - 030 - Need a Hug.cbz` | 4,463,912 | `e1340b626573224455594b3acbb9562f3a35a4af68427bf0b244822c7ac27747` | Pepper&Carrot CC BY 4.0; unedited pages packaged with metadata |
| `Books/Lewis Carroll/Alice's Adventures in Wonderland/Alice's Adventures in Wonderland.epub` | 10,635,901 | `ee036cd4da21ea84aa9f17cbdd75a476e1f69ee5f51136ef5469e85b008fe17d` | Standard Ebooks public-domain/CC0 production |
| `Books/Lewis Carroll/Alice's Adventures in Wonderland/Alice's Adventures in Wonderland.m4b` | 73,110,992 | `4b416d7d5cd35a2a3b7ab6f7a3c983595537fb02a4e68558a98ae89edcf0f0f6` | LibriVox public-domain recording in the US |
| `Images/Open Creative Spaces/Augmented Reality.jpg` | 1,569,247 | `ab9d4d251fa13c25daabfe46702aca9d896e221284616e45717b3536fdc124a6` | Wikimedia Commons CC0 |
| `Images/Open Creative Spaces/Little Free Library.jpg` | 6,555,247 | `a714add5e3e6cae43882ebc825b2509fbbb5f2a2277c1d83d16b296e16544dc2` | Wikimedia Commons CC0 |
| `Images/Open Creative Spaces/Photo Studio.jpg` | 2,668,559 | `fdaf23a8aeb73f380bbe9816888e5e5aab48e860663f930df3129c41f5eab210` | Wikimedia Commons CC0 |
| `Movies/Big Buck Bunny (2008)/Big Buck Bunny (2008).mp4` | 276,134,947 | `ae51005850b0ff757fe60c3dd7a12d754d3cd2397d87d939b55235e457f97658` | Blender Foundation CC BY 3.0; unmodified |
| `Movies/Big Buck Bunny (2008)/Big Buck Bunny (2008).nfo` | 449 | `87a88e2122afe8108b1ce85acb50855b83d25f47a8b49207b031ee862adf4c5b` | Prismedia review metadata and attribution |
| `Series/Open Movie Sampler/Season 01/Open Movie Sampler - S01E01 - Elephants Dream.mp4` | 30,639,218 | `5fe4e42a4c6893f40c90d841ac3e638d7b68d31b04d4a91f22b2bb5071d8c2d6` | Blender Foundation open-movie teaser; unmodified |
| `Series/Open Movie Sampler/Season 01/Open Movie Sampler - S01E01 - Elephants Dream.nfo` | 394 | `a4f1537fd9ed8107c9dfe6f60aff230982e1fd4afbcb2eb8edae7db64c64afd0` | Prismedia review metadata and attribution |
| `Series/Open Movie Sampler/Season 01/Open Movie Sampler - S01E02 - Caminandes Gran Dillama.mp4` | 125,974,946 | `468e6743c674689a728726bbe4bb4b2a65bd8702a89f021af26a8bb4d450eebd` | Blender Foundation open-movie teaser; unmodified |
| `Series/Open Movie Sampler/Season 01/Open Movie Sampler - S01E02 - Caminandes Gran Dillama.nfo` | 435 | `8003dc8f46d8712b513c1b0dd8eafdd52b28f2ca687cfcaf5840189d57cfe492` | Prismedia review metadata and attribution |

## Pre-submission rights check

1. Open every source page again.
2. Confirm the license or public-domain statement has not changed.
3. Confirm the deployed checksum matches the file used for screenshots and
   review.
4. Confirm no household artwork, metadata, usernames, or media entered the
   review environment.
5. Confirm credits remain visible through this public manifest and sidecar
   metadata.
