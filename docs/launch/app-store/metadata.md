# Prismedia App Store listing packet

Prepared: 2026-07-25  
App record: `6792944211`  
Bundle ID: `com.pauljoda.Prismedia`  
Platforms: iOS, iPadOS, and tvOS  
Release state: metadata preparation only—do not attach a build, add for review,
submit, or release.

This packet is the source of truth for the first App Store product page. Keep
credentials in the private handoff record, not in git.

## App information

| Field | Value |
| --- | --- |
| Name | `Prismedia` |
| Subtitle | `Your self-hosted media home` |
| Primary category | Entertainment |
| Secondary category | Utilities |
| Content rights | Contains or accesses third-party content; the app operator is responsible for rights to the media they add. Prismedia's review library uses the licensed sources in `review-media-sources.md`. |
| Age-rating posture | Not made for kids; no gambling, contests, social networking, general web access, or built-in mature content. Answer the questionnaire from actual app behavior rather than inferring a rating. |
| Copyright | `2026 Paul Davis` |

Character checks:

- Name: 9 of 30 characters
- Subtitle: 27 of 30 characters

## URLs

| Field | URL |
| --- | --- |
| Marketing URL | <https://pauljoda.github.io/Prismedia/> |
| Support URL | <https://pauljoda.github.io/Prismedia/support> |
| Privacy-policy URL | <https://pauljoda.github.io/Prismedia/privacy> |

## iPhone and iPad version information

### Promotional text

> One private home for movies, series, music, audiobooks, ebooks, comics,
> images, and galleries—native on iPhone, iPad, and Apple TV.

### Description

> Your media is already one collection. Prismedia gives it one private home.
>
> Connect the native app to the Prismedia server you host, then browse, watch,
> listen, and read without handing your library to an advertising platform.
> Movies, series, music, audiobooks, ebooks, comics, images, and galleries keep
> their own purpose-built experiences while sharing one identity, one account,
> and personal progress.
>
> WATCH IN FULL FIDELITY
>
> Prismedia uses a custom native player built around the codecs and playback
> capabilities of your Apple devices. Supported sources can direct-play at
> original quality, including lossless audio, while the server can remux or
> transcode when the device needs another format. Paused playback keeps title,
> timeline, stream state, resolution, audio, subtitles, and speed controls
> readable.
>
> READ YOUR WAY
>
> Open EPUB, PDF, and comic archives in a native reader. Choose typography,
> theme, margins, spacing, pagination, or continuous scrolling. A book can also
> carry an audiobook edition, keeping reading and listening progress together
> so you can move between text and narration.
>
> LISTEN NATIVELY
>
> Browse artists and albums, manage a queue, and control music or audiobooks
> with familiar native playback surfaces. Artwork, chapters, progress, shuffle,
> and repeat stay close at hand.
>
> BUILT FOR EVERY SCREEN
>
> Use touch-first browsing and reading on iPhone and iPad, then move to a
> focus-first living-room experience on Apple TV. Personal accounts keep
> playback and reading progress with the right member of the household.
>
> SELF-HOSTED BY DESIGN
>
> Prismedia connects to a server you operate. The server centralizes library
> scanning, metadata, files, requests, acquisition, streaming, and background
> work. The native app has no advertising, cross-app tracking, or developer
> analytics.
>
> Requires a reachable Prismedia server and a Prismedia account. Server
> software, installation instructions, and source are available from the
> Prismedia website.

### Keywords

`self-hosted,media server,movies,music,audiobooks,ebooks,comics,reader,Apple TV,private`

The keyword value is 86 UTF-8 bytes, below App Store Connect's 100-byte limit.

## Apple TV version information

### Promotional text

> Your self-hosted movies and series on the biggest screen, with a custom
> native player designed for full-fidelity direct playback.

### Description

Use the iPhone and iPad description above. Its platform section already
explains the focus-first Apple TV experience and the server requirement without
claiming every source will direct-play.

### Keywords

Use the same keyword value as iPhone and iPad.

## App privacy

Recommended answers, subject to a final network and dependency audit of the
exact build selected for submission:

- Tracking: **No**
- Data used for third-party advertising: **No**
- Data linked across third-party apps or websites: **No**
- Data collected by the developer through the app: **No**

Rationale: the app sends credentials, media requests, and progress to the
Prismedia server chosen by the user. A self-hosted server controlled by the user
or household is not the app developer or a developer-controlled third party.
The app has no analytics, ad, crash-reporting, attribution, or tracking SDK.
The public privacy policy separately explains the limited developer-operated
review server used during beta and platform review.

Before saving the privacy response, verify the selected archive's dependency
graph and network behavior. App Store Connect's final privacy **Publish**
confirmation is a legal attestation and requires the account holder's explicit
approval at the moment it is made.

### Apple TV privacy text

> Prismedia connects directly to a media server chosen by the user. The app has
> no advertising, tracking, or developer analytics. Server credentials are used
> to sign in, the session token is kept in the device Keychain, and library
> metadata, media, and progress are exchanged with the selected server. See the
> full policy at https://pauljoda.github.io/Prismedia/privacy.

## App Review information

### Contact

Use the account holder's real first name, last name, phone number, and
`pauldavis101@gmail.com`. Do not invent a phone number. Confirm the saved contact
details in App Store Connect.

### Sign-in required

Yes.

| Field | Value |
| --- | --- |
| Server | `https://apple-prismedia.pauljoda.com` |
| Username | Stored in the private reviewer-credential handoff |
| Password | Stored in the private reviewer-credential handoff |

The remote compose environment pins `PRISMEDIA_IMAGE` to the immutable
multi-architecture digest built from the exact `main` commit deployed for
review. It never follows the mutable `dev` tag.

### Review notes

> Prismedia is a native client for a self-hosted Prismedia media server. The
> public HTTPS demonstration server above is dedicated to App Review, requires
> no VPN or local-network access, and will remain online throughout review.
>
> On iPhone or iPad:
>
> 1. Enter the server URL, username, and password supplied in the review fields.
> 2. Dashboard shows the licensed demonstration library.
> 3. Movies → Big Buck Bunny demonstrates native video playback. Pause to reveal
>    the title, timeline, direct-play/stream state, resolution, codecs, audio,
>    subtitle, and speed controls.
> 4. Series → Open Movie Sampler demonstrates episodic browsing and progress.
> 5. Books → Alice's Adventures in Wonderland includes both an EPUB and M4B
>    audiobook. Use Read, Listen, or the combined reading/listening experience;
>    reader appearance controls are available from the reader toolbar.
> 6. Audio contains a three-track Creative Commons album.
> 7. Comics, Images, and Galleries contain Creative Commons or CC0 fixtures.
>
> On Apple TV:
>
> 1. Sign in with the same server and credentials.
> 2. Open Movies → Big Buck Bunny → Play.
> 3. Press Play/Pause to display Prismedia's custom native player chrome and
>    stream details.
>
> The review account is a non-administrator household member. It can browse and
> play every demonstration library but cannot reconfigure the server. All demo
> assets and their exact licenses are recorded at
> https://github.com/pauljoda/Prismedia/blob/main/docs/launch/app-store/review-media-sources.md.
>
> No build has been attached during metadata preparation. Select the validated
> iOS/tvOS build only when ready to submit.

## Screenshot order

The first three images should communicate the product without relying on the
description:

### iPhone 6.9-inch — 1320 × 2868 portrait

1. One private home — native dashboard and the complete media spectrum
2. Full-fidelity playback — paused player chrome and direct-play details
3. Read and listen together — combined book detail
4. A reader that is yours — typography and appearance controls
5. Music, made native — album and Now Playing
6. Every medium has an experience — comic, image, and gallery proof

### iPad 13-inch — 2752 × 2064 landscape

1. Your whole library, at a glance
2. Full-fidelity native playback
3. A spacious, customizable reader
4. Read and listen together
5. Music, albums, and queue
6. Browse every media family

### Apple TV — 3840 × 2160 landscape

1. Your media on the biggest screen
2. Full-fidelity native playback
3. Pause and see the whole playback story
4. Movies and series, focus first
5. Continue across the household
6. One private server behind every screen

Every screenshot must show the real app in use. Text and background treatment
may frame the capture, but must not obscure or simulate product controls.

## Submission boundary

Metadata completion is not authorization to:

- attach or select a build;
- add the version for review;
- submit App Privacy answers;
- accept new agreements;
- submit the app;
- schedule, manually release, or automatically release a version.

Stop with all fields and screenshots saved, then hand the account holder a list
of the remaining build-selection and submission actions.
