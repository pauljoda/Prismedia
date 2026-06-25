export const appShellSections = [
  {
    id: "overview",
    kicker: "Overview",
    items: [
      { label: "Dashboard", href: "/", icon: "layout-dashboard" },
      { label: "Search", href: "/search", icon: "search" },
      { label: "Stats", href: "/stats", icon: "chart-no-axes-combined" },
    ],
  },
  {
    id: "video",
    kicker: "Video",
    items: [
      { label: "Movies", href: "/movies", icon: "clapperboard" },
      { label: "Series", href: "/series", icon: "folder" },
      { label: "Videos", href: "/videos", icon: "film" },
    ],
  },
  {
    id: "images",
    kicker: "Images",
    items: [
      { label: "Galleries", href: "/galleries", icon: "images" },
      { label: "Images", href: "/images", icon: "image" },
    ],
  },
  {
    id: "audio",
    kicker: "Audio",
    items: [
      { label: "Artists", href: "/artists", icon: "mic-vocal" },
      { label: "Audio", href: "/audio", icon: "music" },
    ],
  },
  {
    id: "books",
    kicker: "Books",
    items: [
      { label: "Books", href: "/books", icon: "book-open" },
      { label: "Comics", href: "/comics", icon: "book-copy" },
      { label: "eBooks", href: "/ebooks", icon: "book-marked" },
    ],
  },
  {
    id: "browse",
    kicker: "Browse",
    items: [
      { label: "People", href: "/people", icon: "users" },
      { label: "Studios", href: "/studios", icon: "building" },
      { label: "Tags", href: "/tags", icon: "tags" },
      { label: "Collections", href: "/collections", icon: "folder" },
    ],
  },
  {
    id: "operate",
    kicker: "Operate",
    items: [
      { label: "Files", href: "/files", icon: "folder-tree" },
      { label: "Identify", href: "/identify", icon: "scan-search" },
      { label: "Request", href: "/request", icon: "send" },
      { label: "Plugins", href: "/plugins", icon: "puzzle" },
      { label: "Jobs", href: "/jobs", icon: "activity" },
      { label: "Settings", href: "/settings", icon: "settings" },
    ],
  },
] as const;

export type NavSection = (typeof appShellSections)[number];
export type NavItem = NavSection["items"][number];
