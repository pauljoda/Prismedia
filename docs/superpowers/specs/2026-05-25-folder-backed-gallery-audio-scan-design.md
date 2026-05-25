# Folder-Backed Gallery And Audio Scan Design

## Summary

Prismedia scans image and audio library roots so loose files at the library root remain standalone media, while every folder below the root becomes a folder-backed container entity. Image folders become galleries. Audio folders become audio libraries/playlists. Nested folders become child galleries or child audio libraries, and files inside a folder attach to that folder entity.

## Goals

- Keep root-level loose images visible as `image` entities without adding them to a gallery.
- Keep root-level loose audio files visible as `audio-track` entities without adding them to an audio library.
- Create gallery entities only for folders below an image-scanning library root.
- Create audio library entities only for folders below an audio-scanning library root.
- Preserve filesystem nesting through `ParentEntityId` and `SortOrder` so detail pages can show sub-galleries and sub-libraries.
- Keep gallery and audio detail pages using the existing `ChildrenByKind` contract.

## Non-Goals

- Do not create a visible or hidden gallery/audio-library entity for the library root itself.
- Do not infer folder hierarchy in the Svelte frontend from source paths.
- Do not introduce a new persistence schema or a third-party-compatible folder model.
- Do not change video or book scanning.

## Current Behavior

`ScanGalleryJobHandler` and `ScanAudioJobHandler` group discovered files by their direct parent directory. Each directory with matching files becomes a gallery or audio library, including the configured library root when loose files are present there.

The scanner upserts images and tracks under the directory entity that contains them, but it does not link a folder entity to its parent folder entity. Detail pages already render `gallery` children under gallery details and `audio-library` children under audio details, so the missing behavior is scan materialization rather than UI rendering.

## Desired Behavior

For an image root at `/media/images`:

```text
/media/images/root.png
/media/images/Gallery/a.png
/media/images/Gallery/A secondGallery/b.png
```

The scan should produce:

- `root.png` as a standalone `image` with no gallery parent.
- `Gallery` as a `gallery` whose source path is `/media/images/Gallery`.
- `a.png` as an `image` child of `Gallery`.
- `A secondGallery` as a `gallery` child of `Gallery`.
- `b.png` as an `image` child of `A secondGallery`.

For an audio root at `/media/audio`, the same structural rule applies:

- Root-level audio tracks are standalone `audio-track` entities.
- Each folder below the root becomes an `audio-library`.
- Nested folders become child audio libraries.
- Tracks inside a folder attach to that folder's audio library.

## Architecture

The change belongs in the scan application handlers and the EF-backed scan persistence adapter.

`ScanGalleryJobHandler` should turn discovered image directory groups into a directory tree relative to the library root. It should skip creating a gallery for the root path, upsert loose root-level images with `galleryEntityId: null`, and upsert folder galleries before their images and nested child galleries.

`ScanAudioJobHandler` should use the same shape for audio directory groups. It should skip creating an audio library for the root path, upsert loose root-level tracks without a parent audio library, and upsert folder audio libraries before tracks and nested child libraries.

`ILibraryScanPersistence` should expose parent-aware container upserts:

- Gallery upsert should accept an optional parent gallery id and sort order.
- Audio library upsert should accept an optional parent audio library id and sort order.
- Audio track upsert should accept an optional audio library id.

The existing `ParentEntityId` and `SortOrder` columns are enough to represent the hierarchy. No migration is required.

## Data Flow

1. Discover image or audio files grouped by parent directory.
2. Normalize each directory path relative to the library root.
3. Partition root-level file groups from child folder groups.
4. Upsert root-level files as loose media.
5. Walk child folders in parent-before-child order.
6. Upsert each folder as a gallery or audio library.
7. Link each folder entity to its nearest parent folder entity when that parent is below the root.
8. Upsert files in that folder as children of the folder entity.
9. Remove stale files for each folder container.
10. Remove stale root-level loose files for the root.
11. Remove stale folder containers that no longer exist under the root.

## Cleanup Semantics

Stale cleanup must distinguish loose root-level files from folder-contained files.

- `RemoveStaleImagesInGalleryAsync` continues cleaning images for a specific gallery.
- A new root-level image cleanup removes only images whose source path is directly under the image library root and whose parent is null.
- `RemoveStaleAudioTracksInLibraryAsync` continues cleaning tracks for a specific audio library.
- A new root-level audio cleanup removes only audio tracks whose source path is directly under the audio library root and whose parent is null.
- Stale gallery/audio-library container cleanup uses the valid folder path set for folders below the root only.

## API And UI Impact

No new API contract is required. `GalleryDetail` and `AudioLibraryDetail` already expose child groups through `ChildrenByKind`, and the Svelte detail pages already render sub-galleries and sub-libraries from those groups.

Index pages continue to use kind lists:

- `/images` lists image entities, including root-level loose images and images inside galleries.
- `/galleries` lists folder-backed gallery entities only.
- `/audio` lists audio library entities only.
- Audio track lists continue to list audio tracks, including root-level loose tracks and tracks inside audio libraries.

## Testing

Add application-level scan handler tests for directory classification:

- Image scan does not upsert a gallery for the root directory.
- Image scan upserts root-level images with no gallery parent.
- Image scan upserts nested galleries parent-before-child and attaches nested images to the correct gallery.
- Audio scan does not upsert an audio library for the root directory.
- Audio scan upserts root-level tracks with no audio library parent.
- Audio scan upserts nested audio libraries parent-before-child and attaches nested tracks to the correct library.

Add persistence tests for parent-aware upserts and cleanup:

- Existing gallery/audio-library rows get their parent and sort order updated when the folder moves within a scan hierarchy.
- Existing image/audio-track rows can be re-linked from loose to contained, and from one folder container to another.
- Root-level stale cleanup removes missing loose files without removing contained files.

## Rollout

This is a behavior change for rescans. Existing root gallery/audio-library entities created for library root paths should become stale because root paths are not included in the valid folder path set. Their former loose child images/tracks are then re-upserted as standalone media by source path. Existing folder-backed entities keep their ids because source paths stay stable.

The changelog should include a user-facing `Changed` entry because scan organization changes what gallery and audio detail pages show after a rescan.
