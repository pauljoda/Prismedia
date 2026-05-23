import type { EntityCapability } from "$lib/api/generated/model";
import type {
  EntityThumbnailAsset,
  EntityThumbnailCard,
  EntityThumbnailMetaItem,
} from "./entity-thumbnail";

/** One lab row that groups thumbnail examples by entity kind. */
export interface EntityThumbnailRow {
  kind: string;
  label: string;
  cards: EntityThumbnailCard[];
}

type ArtShape = "wide" | "video" | "square" | "portrait" | "poster";

function artDimensions(shape: ArtShape): { width: number; height: number } {
  switch (shape) {
    case "poster":
      return { width: 720, height: 1080 };
    case "portrait":
      return { width: 810, height: 1080 };
    case "square":
      return { width: 900, height: 900 };
    case "wide":
      return { width: 1260, height: 540 };
    case "video":
    default:
      return { width: 960, height: 540 };
  }
}

function svgArt(label: string, primary: string, secondary: string, accent: string, shape: ArtShape): string {
  const { width, height } = artDimensions(shape);
  const safeLabel = label.replace(/[<>&"]/g, "");
  const textWidth = Math.min(width - 96, 420);
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}"><defs><linearGradient id="g" x1="0" x2="1" y1="0" y2="1"><stop stop-color="${primary}"/><stop offset="1" stop-color="${secondary}"/></linearGradient><filter id="grain"><feTurbulence baseFrequency=".8" numOctaves="2" stitchTiles="stitch"/><feColorMatrix type="saturate" values="0"/></filter></defs><rect width="${width}" height="${height}" fill="url(#g)"/><rect width="${width}" height="${height}" opacity=".13" filter="url(#grain)"/><path d="M${width * 0.07} ${height * 0.82} C${width * 0.22} ${height * 0.55} ${width * 0.3} ${height * 0.66} ${width * 0.43} ${height * 0.4} S${width * 0.7} ${height * 0.22} ${width * 0.93} ${height * 0.48}" fill="none" stroke="${accent}" stroke-width="${Math.max(16, width * 0.018)}" opacity=".72"/><circle cx="${width * 0.78}" cy="${height * 0.25}" r="${Math.min(width, height) * 0.14}" fill="${accent}" opacity=".34"/><rect x="48" y="48" width="${textWidth}" height="70" fill="#050505" opacity=".5"/><text x="72" y="96" fill="#f4efe6" font-family="Inter,Arial,sans-serif" font-size="36" font-weight="700">${safeLabel}</text></svg>`;
  return `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`;
}

function asset(
  label: string,
  primary: string,
  secondary: string,
  accent = "#c49a5a",
  shape: ArtShape = "video",
): EntityThumbnailAsset {
  return {
    src: svgArt(label, primary, secondary, accent, shape),
    alt: label,
  };
}

function sequence(
  label: string,
  palette: [string, string, string],
  count: number,
  shape: ArtShape,
): EntityThumbnailAsset[] {
  return Array.from({ length: count }, (_, index) =>
    asset(
      `${label} ${index + 1}`,
      palette[(index + 0) % palette.length],
      palette[(index + 1) % palette.length],
      palette[(index + 2) % palette.length],
      shape,
    ),
  );
}

function rating(value: number): EntityCapability {
  return {
    kind: "rating",
    value,
  };
}

function flags(options: { isNsfw?: boolean; isFavorite?: boolean; isOrganized?: boolean } = {}): EntityCapability {
  return {
    kind: "flags",
    isFavorite: options.isFavorite ?? null,
    isNsfw: options.isNsfw ?? null,
    isOrganized: options.isOrganized ?? null,
  };
}

function images(
  supportedKinds: string[],
  cover: EntityThumbnailAsset,
  extraAssets: EntityThumbnailAsset[] = [],
  extraRole = "preview",
): EntityCapability {
  return {
    kind: "images",
    supportedKinds,
    thumbnailUrl: cover.src,
    coverUrl: cover.src,
    items: [
      { kind: "cover", path: cover.src, mimeType: "image/svg+xml" },
      ...extraAssets.map((item, index) => ({
        kind: supportedKinds.includes(extraRole) ? extraRole : (supportedKinds[index % supportedKinds.length] ?? "preview"),
        path: item.src,
        mimeType: "image/svg+xml",
      })),
    ],
  };
}

function stats(items: Array<{ code: string; value: number }>): EntityCapability {
  return {
    kind: "stats",
    items,
  };
}

function technical(options: { duration?: string; width?: number; height?: number; codec?: string; format?: string }): EntityCapability {
  return {
    kind: "technical",
    duration: options.duration ?? null,
    width: options.width ?? null,
    height: options.height ?? null,
    frameRate: null,
    bitRate: null,
    sampleRate: null,
    channels: null,
    codec: options.codec ?? null,
    container: null,
    format: options.format ?? null,
  };
}

function position(code: string, value: number, label: string): EntityCapability {
  return {
    kind: "position",
    items: [{ code, value, label }],
  };
}

function positions(items: { code: string; value: number; label: string }[]): EntityCapability {
  return {
    kind: "position",
    items,
  };
}

function card(options: {
  id: string;
  kind: string;
  title: string;
  aspectRatio: EntityThumbnailCard["aspectRatio"];
  cover: EntityThumbnailAsset;
  hover?: EntityThumbnailCard["hover"];
  supportedImageKinds?: string[];
  flagOptions?: Parameters<typeof flags>[0];
  capabilities?: EntityCapability[];
  custom?: EntityThumbnailCard["custom"];
  meta?: EntityThumbnailMetaItem[];
}): EntityThumbnailCard {
  const hover = options.hover ?? { kind: "none" };
  const hoverAssets = hover.kind === "none" || hover.kind === "sprite" ? [] : hover.assets;
  const hoverRole = hover.kind === "trickplay" ? "trickplay" : "preview";
  const supportedImageKinds =
    options.supportedImageKinds ??
    Array.from(new Set(["cover", ...(hoverAssets.length > 0 ? [hoverRole] : [])]));
  return {
    entity: {
      id: options.id,
      kind: options.kind,
      title: options.title,
      parentEntityId: null,
      sortOrder: null,
      relationships: [],
      capabilities: [
        flags(options.flagOptions),
        images(supportedImageKinds, options.cover, hoverAssets, hoverRole),
        ...(options.capabilities ?? []),
      ],
      childrenByKind: [],
    },
    aspectRatio: options.aspectRatio,
    cover: options.cover,
    custom: options.custom,
    hover,
    meta: options.meta,
  };
}

function withNsfw(card: EntityThumbnailCard): EntityThumbnailCard {
  return {
    ...card,
    entity: {
      ...card.entity,
      capabilities: card.entity.capabilities.map((capability) =>
        capability.kind === "flags"
          ? {
              ...capability,
              isNsfw: true,
            }
          : capability,
      ),
    },
  };
}

const brass = "#c49a5a";
const forest = "#293f32";
const burgundy = "#522b34";
const indigo = "#26344f";
const ember = "#7b4a24";
const graphite = "#1f2226";

/** Safe synthetic thumbnail data for exercising the shared entity-card surface without touching user media. */
const thumbnailLabSeedRows: EntityThumbnailRow[] = [
  {
    kind: "video",
    label: "Videos",
    cards: [
      card({
        id: "video-big-buck",
        kind: "video",
        title: "Big Buck Bunny Sample",
        aspectRatio: "video",
        cover: asset("Big Buck Bunny", forest, graphite, brass, "video"),
        hover: { kind: "trickplay", assets: sequence("Trickplay", [forest, ember, indigo], 6, "video") },
        capabilities: [
          rating(4),
          technical({ duration: "00:09:56", width: 1920, height: 1080, codec: "h264" }),
          positions([
            { code: "season", value: 1, label: "Season 1" },
            { code: "episode", value: 2, label: "Episode 2" },
          ]),
        ],
        custom: { bottomLeft: { label: "S1 E2", title: "Season 1, Episode 2" } },
        meta: [
          { icon: "duration", label: "09:56" },
          { icon: "video", label: "1080p" },
        ],
      }),
      card({
        id: "video-flagged",
        kind: "video",
        title: "Flagged Video Sample",
        aspectRatio: "video",
        cover: asset("Flagged Video", burgundy, graphite, brass, "video"),
        hover: { kind: "trickplay", assets: sequence("Flagged", [burgundy, ember, graphite], 6, "video") },
        flagOptions: { isNsfw: true },
        capabilities: [rating(3), technical({ duration: "00:12:18", width: 1920, height: 1080, codec: "h265" })],
        meta: [
          { icon: "duration", label: "12:18" },
          { icon: "video", label: "1080p" },
        ],
      }),
    ],
  },
  {
    kind: "video-series",
    label: "Video Series",
    cards: [
      card({
        id: "series-demo",
        kind: "video-series",
        title: "Demo Shorts",
        aspectRatio: "poster",
        cover: asset("Demo Shorts", burgundy, graphite, brass, "poster"),
        capabilities: [rating(5), stats([{ code: "videos", value: 8 }])],
        meta: [{ icon: "count", label: "8 videos" }],
      }),
    ],
  },
  {
    kind: "video-season",
    label: "Video Seasons",
    cards: [
      card({
        id: "season-01",
        kind: "video-season",
        title: "Season 01",
        aspectRatio: "poster",
        cover: asset("Season 01", indigo, graphite, brass, "poster"),
        capabilities: [position("season", 1, "Season 1"), stats([{ code: "episodes", value: 6 }])],
        custom: { bottomLeft: { label: "S1", title: "Season 1" } },
        meta: [{ icon: "count", label: "6 episodes" }],
      }),
    ],
  },
  {
    kind: "gallery",
    label: "Galleries",
    cards: [
      card({
        id: "gallery-stock",
        kind: "gallery",
        title: "Fixture Landscapes",
        aspectRatio: "square",
        cover: asset("Gallery Cover", ember, graphite, brass, "square"),
        hover: { kind: "image-sequence", assets: sequence("Gallery", [ember, forest, indigo], 5, "portrait") },
        capabilities: [stats([{ code: "images", value: 42 }])],
        meta: [{ icon: "gallery", label: "42 images" }],
      }),
      card({
        id: "gallery-flagged",
        kind: "gallery",
        title: "Flagged Gallery",
        aspectRatio: "square",
        cover: asset("Flagged Gallery", burgundy, ember, brass, "square"),
        hover: { kind: "image-sequence", assets: sequence("Flagged Gallery", [burgundy, forest, graphite], 5, "portrait") },
        flagOptions: { isNsfw: true },
        capabilities: [rating(2), stats([{ code: "images", value: 18 }])],
        meta: [{ icon: "gallery", label: "18 images" }],
      }),
    ],
  },
  {
    kind: "image",
    label: "Images",
    cards: [
      card({
        id: "image-still",
        kind: "image",
        title: "Sample Still",
        aspectRatio: { width: 4, height: 3 },
        cover: asset("Image Still", forest, indigo, brass, "wide"),
        capabilities: [rating(3), technical({ width: 1600, height: 1200, format: "jpeg" })],
        meta: [{ icon: "image", label: "1600x1200" }],
      }),
    ],
  },
  {
    kind: "book",
    label: "Books",
    cards: [
      card({
        id: "book-fixture",
        kind: "book",
        title: "Public Domain Reader",
        aspectRatio: "poster",
        cover: asset("Book Cover", burgundy, ember, brass, "poster"),
        hover: { kind: "image-sequence", assets: sequence("Pages", [burgundy, graphite, forest], 4, "poster") },
        capabilities: [stats([{ code: "pages", value: 128 }, { code: "chapters", value: 9 }])],
        meta: [
          { icon: "book", label: "128 pages" },
          { icon: "chapter", label: "9 chapters" },
        ],
      }),
    ],
  },
  {
    kind: "book-volume",
    label: "Book Volumes",
    cards: [
      card({
        id: "volume-01",
        kind: "book-volume",
        title: "Volume 01",
        aspectRatio: "poster",
        cover: asset("Volume 01", ember, burgundy, brass, "poster"),
        capabilities: [position("volume", 1, "Volume 1"), stats([{ code: "chapters", value: 4 }])],
        meta: [{ icon: "chapter", label: "4 chapters" }],
      }),
    ],
  },
  {
    kind: "book-chapter",
    label: "Book Chapters",
    cards: [
      card({
        id: "chapter-01",
        kind: "book-chapter",
        title: "Chapter 01",
        aspectRatio: "poster",
        cover: asset("Chapter 01", forest, burgundy, brass, "poster"),
        hover: { kind: "image-sequence", assets: sequence("Chapter", [forest, graphite, ember], 5, "poster") },
        capabilities: [position("chapter", 1, "Chapter 1"), stats([{ code: "pages", value: 24 }])],
        meta: [{ icon: "book", label: "24 pages" }],
      }),
    ],
  },
  {
    kind: "book-page",
    label: "Book Pages",
    cards: [
      card({
        id: "page-001",
        kind: "book-page",
        title: "Page 001",
        aspectRatio: "poster",
        cover: asset("Page 001", indigo, ember, brass, "poster"),
        capabilities: [position("page", 1, "Page 1"), technical({ width: 1200, height: 1800, format: "png" })],
      }),
    ],
  },
  {
    kind: "audio-library",
    label: "Audio Libraries",
    cards: [
      card({
        id: "audio-library",
        kind: "audio-library",
        title: "Royalty Free Album",
        aspectRatio: "square",
        cover: asset("Audio Album", graphite, indigo, brass, "square"),
        capabilities: [rating(4), stats([{ code: "tracks", value: 12 }])],
        meta: [{ icon: "audio", label: "12 tracks" }],
      }),
    ],
  },
  {
    kind: "audio-track",
    label: "Audio Tracks",
    cards: [
      card({
        id: "audio-track",
        kind: "audio-track",
        title: "Sample Track",
        aspectRatio: "square",
        cover: asset("Sample Track", indigo, forest, brass, "square"),
        capabilities: [technical({ duration: "00:03:42", codec: "aac" }), position("track", 3, "Track 3")],
        meta: [
          { icon: "duration", label: "03:42" },
          { icon: "audio", label: "track 3" },
        ],
      }),
    ],
  },
  {
    kind: "person",
    label: "People",
    cards: [
      card({
        id: "person-sample",
        kind: "person",
        title: "Sample Artist",
        aspectRatio: "portrait",
        cover: asset("Person", burgundy, indigo, brass, "portrait"),
        capabilities: [stats([{ code: "credits", value: 18 }])],
        meta: [{ icon: "person", label: "18 credits" }],
      }),
    ],
  },
  {
    kind: "studio",
    label: "Studios",
    cards: [
      card({
        id: "studio-sample",
        kind: "studio",
        title: "Sample Studio",
        aspectRatio: "wide",
        cover: asset("Studio", graphite, forest, brass, "wide"),
        capabilities: [stats([{ code: "items", value: 64 }])],
        meta: [{ icon: "studio", label: "64 items" }],
      }),
    ],
  },
  {
    kind: "tag",
    label: "Tags",
    cards: [
      card({
        id: "tag-sample",
        kind: "tag",
        title: "Animation",
        aspectRatio: "square",
        cover: asset("Tag", ember, indigo, brass, "square"),
        capabilities: [stats([{ code: "items", value: 31 }])],
        meta: [{ icon: "tag", label: "31 items" }],
      }),
    ],
  },
  {
    kind: "collection",
    label: "Collections",
    cards: [
      card({
        id: "collection-sample",
        kind: "collection",
        title: "Safe Samples",
        aspectRatio: "video",
        cover: asset("Collection", forest, ember, brass, "video"),
        hover: { kind: "image-sequence", assets: sequence("Collection", [forest, burgundy, indigo], 4, "square") },
        capabilities: [stats([{ code: "items", value: 15 }])],
        meta: [{ icon: "collection", label: "15 items" }],
      }),
      card({
        id: "collection-flagged",
        kind: "collection",
        title: "Hidden Review Queue",
        aspectRatio: "video",
        cover: asset("Flagged State", burgundy, graphite, brass, "wide"),
        flagOptions: { isNsfw: true },
        capabilities: [rating(2), stats([{ code: "items", value: 4 }])],
        meta: [{ icon: "collection", label: "4 items" }],
      }),
    ],
  },
];

function cloneCard(card: EntityThumbnailCard, index: number): EntityThumbnailCard {
  if (index === 0) return card;

  const displayIndex = index + 1;
  const title = card.entity.title.replace(/\d+$/, (value) => String(Number(value) + index).padStart(value.length, "0"));

  const cloned = {
    ...card,
    entity: {
      ...card.entity,
      id: `${card.entity.id}-${displayIndex}`,
      title: title === card.entity.title ? `${card.entity.title} ${displayIndex}` : title,
    },
  };

  return index % 4 === 2 ? withNsfw(cloned) : cloned;
}

function expandCards(cards: EntityThumbnailCard[], count: number): EntityThumbnailCard[] {
  return Array.from({ length: count }, (_, index) => cloneCard(cards[index % cards.length], index));
}

/** Safe synthetic thumbnail data for exercising the shared entity-card surface without touching user media. */
export const thumbnailLabRows: EntityThumbnailRow[] = thumbnailLabSeedRows.map((row) => ({
  ...row,
  cards: expandCards(row.cards, 300),
}));
