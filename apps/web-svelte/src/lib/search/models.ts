export type SearchEntityKind =
  | "video"
  | "video-series"
  | "performer"
  | "studio"
  | "tag"
  | "gallery"
  | "image"
  | "book"
  | "audio-library"
  | "audio-track";

export interface SearchResultItem {
  id: string;
  kind: SearchEntityKind;
  title: string;
  subtitle: string | null;
  imagePath: string | null;
  href: string;
  rating: number | null;
  score: number;
  meta: Record<string, string | number | boolean | string[] | null>;
}

export interface SearchResultGroup {
  kind: SearchEntityKind;
  label: string;
  items: SearchResultItem[];
  total: number;
  hasMore: boolean;
}

export interface SearchResponse {
  query: string;
  groups: SearchResultGroup[];
  durationMs: number;
}
