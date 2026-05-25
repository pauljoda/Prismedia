import { appShellSections as baseAppShellSections } from "@prismedia/ui-svelte";
import type { AppRouteId } from "$lib/app-routes";
import { shouldExposeDevRoutes } from "$lib/dev-routes";

export interface AppShellNavItem {
  label: string;
  href: AppRouteId;
  icon: string;
}

export interface AppShellNavSection {
  id: string;
  kicker: string;
  items: AppShellNavItem[];
}

const baseSections: AppShellNavSection[] = baseAppShellSections.map((section) => ({
  id: section.id,
  kicker: section.kicker,
  items: section.items.map((item) => ({ ...item, href: item.href as AppRouteId })),
}));

const devSection: AppShellNavSection = {
  id: "dev",
  kicker: "Develop",
  items: [
    { label: "Dev Shim", href: "/dev", icon: "wrench" },
    { label: "Design System", href: "/design-language", icon: "palette" },
    { label: "Thumbnail Lab", href: "/dev/thumbnail-lab", icon: "grid-2x2" },
    { label: "Detail Lab", href: "/dev/detail-lab", icon: "layout-list" },
  ],
};

export const appShellSections: AppShellNavSection[] = shouldExposeDevRoutes()
  ? [...baseSections, devSection]
  : baseSections;
