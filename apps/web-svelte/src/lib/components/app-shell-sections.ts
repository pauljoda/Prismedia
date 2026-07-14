import { appShellSections as baseAppShellSections } from "@prismedia/ui-svelte";
import type { AppRouteId } from "$lib/app-routes";

export interface AppShellNavItem {
  label: string;
  href: AppRouteId;
  icon: string;
}

export interface AppShellNavSection {
  id: string;
  kicker: string;
  accent: string;
  items: AppShellNavItem[];
}

const baseSections: AppShellNavSection[] = baseAppShellSections.map((section) => ({
  id: section.id,
  kicker: section.kicker,
  accent: section.accent,
  items: section.items.map((item) => ({ ...item, href: item.href as AppRouteId })),
}));

export const appShellSections: AppShellNavSection[] = baseSections;
