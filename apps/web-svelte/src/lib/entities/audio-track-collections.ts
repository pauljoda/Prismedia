import type { EntityCard, EntityThumbnail } from "$lib/api/generated/model";
import { fetchEntityChildren, fetchEntityThumbnails } from "$lib/api/entities";
import { assetUrl } from "$lib/api/orval-fetch";
import type { CollectionItem } from "$lib/collections/models";
import { ENTITY_KIND } from "$lib/entities/entity-codes";
import { entityThumbnailToTrackItem } from "$lib/entities/audio-track-items";
import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";

export interface AudioTrackCollectionResult {
  tracks: AudioTrackListItemDto[];
  albumCoverUrls: Record<string, string | null | undefined>;
}

interface CollectOptions {
  groupByAlbum?: boolean;
  signal?: AbortSignal;
}

/**
 * Caches thumbnail and child-batch responses while a collection is expanded. It deliberately
 * keeps a separate occurrence list during traversal: a collection may reference the same album
 * more than once, and that must still produce the same repeated queue entries in the same order.
 */
class AudioTrackHydrator {
  private readonly thumbnails = new Map<string, EntityThumbnail>();
  private readonly missingThumbnailIds = new Set<string>();
  private readonly childrenByParentId = new Map<string, EntityThumbnail[]>();

  constructor(
    private readonly signal?: AbortSignal,
    thumbnails: EntityThumbnail[] = [],
  ) {
    this.rememberThumbnails(thumbnails);
  }

  getThumbnail(id: string): EntityThumbnail | undefined {
    return this.thumbnails.get(id);
  }

  async resolveThumbnails(ids: string[], ignoreFailure = false): Promise<void> {
    const missingIds = [...new Set(ids.filter((id) => id && !this.thumbnails.has(id) && !this.missingThumbnailIds.has(id)))];
    if (missingIds.length === 0) return;

    this.signal?.throwIfAborted();
    try {
      const thumbnails = await fetchEntityThumbnails(missingIds, { signal: this.signal });
      this.rememberThumbnails(thumbnails);
      for (const id of missingIds) {
        if (!this.thumbnails.has(id)) this.missingThumbnailIds.add(id);
      }
    } catch (error) {
      this.signal?.throwIfAborted();
      if (!ignoreFailure) throw error;
      for (const id of missingIds) this.missingThumbnailIds.add(id);
    }
  }

  async resolveChildren(parentIds: string[]): Promise<void> {
    const missingParentIds = [...new Set(parentIds.filter((id) => id && !this.childrenByParentId.has(id)))];
    if (missingParentIds.length === 0) return;

    this.signal?.throwIfAborted();
    const groups = await fetchEntityChildren(missingParentIds, { signal: this.signal });
    for (const id of missingParentIds) this.childrenByParentId.set(id, []);
    for (const group of groups) {
      this.childrenByParentId.set(group.parentId, group.items);
      this.rememberThumbnails(group.items);
    }
  }

  childrenOf(id: string): EntityThumbnail[] {
    return this.childrenByParentId.get(id) ?? [];
  }

  /** Hydrates every depth of a library forest one level at a time. */
  async hydrateLibraries(roots: EntityThumbnail[]): Promise<void> {
    let level = roots;
    while (level.length > 0) {
      await this.resolveChildren(level.map((library) => library.id));
      level = level.flatMap((library) => this.childrenOf(library.id)
        .filter((child) => child.kind === ENTITY_KIND.audioLibrary));
    }
  }

  private rememberThumbnails(thumbnails: EntityThumbnail[]): void {
    for (const thumbnail of thumbnails) this.thumbnails.set(thumbnail.id, thumbnail);
  }
}

export function isAudioCollectionMemberKind(kind: string): boolean {
  return kind === ENTITY_KIND.audioTrack ||
    kind === ENTITY_KIND.audioLibrary ||
    kind === ENTITY_KIND.musicArtist;
}

export async function collectLibraryTracks(
  libraryId: string,
  options: CollectOptions = {},
): Promise<AudioTrackCollectionResult> {
  return collectLibraryTrackGroups([libraryId], options);
}

/**
 * Expands several albums with one thumbnail lookup and breadth-first child batches. Results keep
 * the caller's album order and each album's original depth-first traversal order.
 */
export async function collectLibraryTrackGroups(
  libraryIds: string[],
  options: CollectOptions = {},
): Promise<AudioTrackCollectionResult> {
  const hydrator = new AudioTrackHydrator(options.signal);
  await hydrator.resolveThumbnails(libraryIds, true);
  const roots = libraryIds
    .map((id) => hydrator.getThumbnail(id))
    .filter((library): library is EntityThumbnail => Boolean(library));
  await hydrator.hydrateLibraries(roots);

  return collectLibraryForestTracks(roots, hydrator, options.groupByAlbum === true);
}

export async function collectArtistTracks(
  artistId: string,
  options: CollectOptions = {},
): Promise<AudioTrackCollectionResult> {
  const hydrator = new AudioTrackHydrator(options.signal);
  await hydrator.resolveThumbnails([artistId], true);
  if (!hydrator.getThumbnail(artistId)) return emptyAudioTrackCollection();

  await hydrator.resolveChildren([artistId]);
  const albums = sortedArtistAlbums(hydrator.childrenOf(artistId));
  await hydrator.hydrateLibraries(albums);
  return collectLibraryForestTracks(albums, hydrator, options.groupByAlbum === true);
}

export async function collectCollectionAudioTracks(
  items: CollectionItem[],
  options: Pick<CollectOptions, "signal"> = {},
): Promise<AudioTrackCollectionResult> {
  const entities = items.flatMap((item) => item.entity ? [item.entity] : []);
  const hydrator = new AudioTrackHydrator(options.signal, entities);
  const directTrackParentIds = items.flatMap((item) => {
    const entity = item.entity;
    return entity?.kind === ENTITY_KIND.audioTrack && entity.parentEntityId ? [entity.parentEntityId] : [];
  });
  const rootParentIds = items.flatMap((item) => {
    const entity = item.entity;
    return entity && (entity.kind === ENTITY_KIND.audioLibrary || entity.kind === ENTITY_KIND.musicArtist)
      ? [entity.id]
      : [];
  });

  // Direct tracks only need their parent thumbnail for section labels and cover artwork. Treat
  // that lookup like the previous per-track detail fetch: an unavailable parent does not remove
  // an otherwise playable track.
  await hydrator.resolveThumbnails(directTrackParentIds, true);
  await hydrator.resolveChildren(rootParentIds);

  const artistAlbumsById = new Map<string, EntityThumbnail[]>();
  const libraryRoots: EntityThumbnail[] = [];
  for (const item of items) {
    const entity = item.entity;
    if (!entity) continue;
    if (entity.kind === ENTITY_KIND.audioLibrary) {
      libraryRoots.push(entity);
    } else if (entity.kind === ENTITY_KIND.musicArtist) {
      artistAlbumsById.set(entity.id, sortedArtistAlbums(hydrator.childrenOf(entity.id)));
      libraryRoots.push(...(artistAlbumsById.get(entity.id) ?? []));
    }
  }
  await hydrator.hydrateLibraries(libraryRoots);

  const tracks: AudioTrackListItemDto[] = [];
  const albumCoverUrls: Record<string, string | null | undefined> = {};
  for (const item of items) {
    options.signal?.throwIfAborted();
    const entity = item.entity;
    if (!entity) continue;

    if (entity.kind === ENTITY_KIND.audioTrack) {
      if (entity.isWanted === true) continue;
      const album = entity.parentEntityId ? hydrator.getThumbnail(entity.parentEntityId) : undefined;
      if (album) albumCoverUrls[album.id] = audioLibraryThumbnailCoverUrl(album);
      tracks.push(entityThumbnailToTrackItem(entity, entity.parentEntityId ?? null, {
        sectionLabel: album?.title ?? null,
        sectionKey: album ? albumSectionKey(album.id) : null,
        libraryId: album?.id ?? entity.parentEntityId ?? null,
      }));
    } else if (entity.kind === ENTITY_KIND.audioLibrary) {
      appendLibraryTracks(entity, hydrator, true, tracks, albumCoverUrls);
    } else if (entity.kind === ENTITY_KIND.musicArtist) {
      for (const album of artistAlbumsById.get(entity.id) ?? []) {
        appendLibraryTracks(album, hydrator, true, tracks, albumCoverUrls);
      }
    }
  }

  return { tracks, albumCoverUrls };
}

export function tracksFromAudioLibraryDetail(
  detail: EntityCard,
  groupByAlbum: boolean,
): AudioTrackListItemDto[] {
  const trackGroup = detail.childrenByKind.find((group) => group.kind === ENTITY_KIND.audioTrack);
  return (trackGroup?.entities ?? [])
    .filter((thumb) => thumb.isWanted !== true)
    .map((thumb) => entityThumbnailToTrackItem(thumb, detail.id, {
      sectionLabel: groupByAlbum ? detail.title : undefined,
      sectionKey: groupByAlbum ? albumSectionKey(detail.id) : undefined,
    }))
    .sort((left, right) => left.sortOrder - right.sortOrder);
}

export function tracksFromAudioLibraryChildren(
  library: Pick<EntityThumbnail, "id" | "title">,
  children: EntityThumbnail[],
  groupByAlbum: boolean,
): AudioTrackListItemDto[] {
  return children
    .filter((child) => child.kind === ENTITY_KIND.audioTrack && child.isWanted !== true)
    .map((child) => entityThumbnailToTrackItem(child, library.id, {
      sectionLabel: groupByAlbum ? library.title : undefined,
      sectionKey: groupByAlbum ? albumSectionKey(library.id) : undefined,
    }))
    .sort((left, right) => left.sortOrder - right.sortOrder);
}

function collectLibraryForestTracks(
  roots: EntityThumbnail[],
  hydrator: AudioTrackHydrator,
  groupByAlbum: boolean,
): AudioTrackCollectionResult {
  const tracks: AudioTrackListItemDto[] = [];
  const albumCoverUrls: Record<string, string | null | undefined> = {};
  for (const root of roots) appendLibraryTracks(root, hydrator, groupByAlbum, tracks, albumCoverUrls);
  return { tracks, albumCoverUrls };
}

function appendLibraryTracks(
  library: EntityThumbnail,
  hydrator: AudioTrackHydrator,
  groupByAlbum: boolean,
  tracks: AudioTrackListItemDto[],
  albumCoverUrls: Record<string, string | null | undefined>,
): void {
  const children = hydrator.childrenOf(library.id);
  tracks.push(...tracksFromAudioLibraryChildren(library, children, groupByAlbum));
  albumCoverUrls[library.id] = audioLibraryThumbnailCoverUrl(library);
  for (const child of children) {
    if (child.kind === ENTITY_KIND.audioLibrary) {
      appendLibraryTracks(child, hydrator, groupByAlbum, tracks, albumCoverUrls);
    }
  }
}

function sortedArtistAlbums(children: EntityThumbnail[]): EntityThumbnail[] {
  return children
    .filter((child) => child.kind === ENTITY_KIND.audioLibrary)
    .sort((left, right) => Number(left.sortOrder ?? 0) - Number(right.sortOrder ?? 0) || left.title.localeCompare(right.title));
}

function emptyAudioTrackCollection(): AudioTrackCollectionResult {
  return { tracks: [], albumCoverUrls: {} };
}

function audioLibraryThumbnailCoverUrl(thumbnail: EntityThumbnail): string | null {
  return assetUrl(thumbnail.coverUrl ?? thumbnail.coverThumbUrl) || null;
}

function albumSectionKey(albumId: string): string {
  return `album:${albumId}`;
}
