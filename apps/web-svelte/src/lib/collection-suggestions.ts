import type {
  PerformerListItemDto,
  StudioListItemDto,
  TagListItemDto,
} from "@prismedia/contracts";

export interface SuggestionItem {
  name: string;
  count?: number;
}

function sortByCount(a: SuggestionItem, b: SuggestionItem) {
  return (b.count ?? 0) - (a.count ?? 0);
}

export function buildTagSuggestions(tags: TagListItemDto[]): SuggestionItem[] {
  return tags
    .map((t) => ({
      name: t.name,
      count:
        (t.videoCount ?? 0) +
        (t.galleryCount ?? 0) +
        (t.imageCount ?? 0) +
        (t.audioTrackCount ?? 0),
    }))
    .filter((s) => (s.count ?? 0) > 0)
    .sort(sortByCount);
}

export function buildPerformerSuggestions(
  performers: PerformerListItemDto[],
): SuggestionItem[] {
  return performers
    .map((p) => ({
      name: p.name,
      count:
        (p.videoCount ?? 0) +
        (p.imageAppearanceCount ?? 0) +
        (p.audioLibraryCount ?? 0),
    }))
    .filter((s) => (s.count ?? 0) > 0)
    .sort(sortByCount);
}

export function buildStudioSuggestions(
  studios: StudioListItemDto[],
): SuggestionItem[] {
  return studios
    .map((s) => ({
      name: s.name,
      count:
        (s.videoCount ?? 0) +
        (s.imageAppearanceCount ?? 0) +
        (s.audioLibraryCount ?? 0),
    }))
    .filter((s) => (s.count ?? 0) > 0)
    .sort(sortByCount);
}
