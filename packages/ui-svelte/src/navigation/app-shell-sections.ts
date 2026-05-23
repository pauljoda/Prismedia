export const appShellSections = [
  {
    id: "overview",
    kicker: "Overview",
    items: [
      { label: "Dashboard", href: "/", icon: "layout-dashboard" },
      { label: "Search", href: "/search", icon: "search" },
    ],
  },
  {
    id: "browse",
    kicker: "Browse",
    items: [
      { label: "Videos", href: "/videos", icon: "film" },
      { label: "Series", href: "/series", icon: "folder" },
      { label: "Images", href: "/images", icon: "image" },
      { label: "Galleries", href: "/galleries", icon: "images" },
      { label: "Books", href: "/books", icon: "book-open" },
      { label: "Audio", href: "/audio", icon: "music" },
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
      { label: "Plugins", href: "/plugins", icon: "puzzle" },
      { label: "Jobs", href: "/jobs", icon: "activity" },
      { label: "Settings", href: "/settings", icon: "settings" },
    ],
  },
] as const;

export type NavSection = (typeof appShellSections)[number];
export type NavItem = NavSection["items"][number];
