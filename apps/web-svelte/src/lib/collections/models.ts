export type CollectionEntityType = "video" | "gallery" | "image" | "book" | "audio-track";
export type CollectionItemSource = "manual" | "dynamic";

export interface CollectionItem {
  id: string;
  collectionId: string;
  entityType: CollectionEntityType;
  entityId: string;
  source: CollectionItemSource;
  sortOrder: number;
  addedAt: string;
  entity: Record<string, unknown> | null;
}

export interface PlaylistSession {
  collectionId: string | null;
  collectionName: string;
  items: CollectionItem[];
  playOrder: number[];
  orderPosition: number;
  shuffle: boolean;
  loop: boolean;
  slideshowDurationSeconds: number;
  updatedAt: string;
}

export interface PlaylistSessionWrite {
  collectionId: string | null;
  collectionName: string;
  items: CollectionItem[];
  playOrder: number[];
  orderPosition: number;
  shuffle: boolean;
  loop: boolean;
  slideshowDurationSeconds: number;
}
