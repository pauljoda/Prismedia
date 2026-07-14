import type { Component } from "svelte";
import {
  Archive,
  Captions,
  Film,
  FolderOpen,
  HardDrive,
  ScanSearch,
  Send,
  Settings2,
  Sparkles,
  UsersRound,
  Wrench,
} from "@lucide/svelte";
import { PRISM_MATERIAL_SPECTRUM } from "$lib/entities/entity-accent";

export const SETTING_SECTION = {
  libraries: "libraries",
  users: "users",
  acquisition: "acquisition",
  playback: "playback",
  subtitles: "subtitles",
  generation: "generation",
  autoIdentify: "auto-identify",
  transcodeCache: "transcode-cache",
  databaseBackups: "database-backups",
  diagnostics: "diagnostics",
} as const;

export type SettingsSectionId = (typeof SETTING_SECTION)[keyof typeof SETTING_SECTION];

export const SETTINGS_SECTION_ACCESS = {
  manager: "manager",
  admin: "admin",
} as const;

export type SettingsSectionAccess = (typeof SETTINGS_SECTION_ACCESS)[keyof typeof SETTINGS_SECTION_ACCESS];

export type SettingsSection = {
  id: SettingsSectionId;
  title: string;
  description: string;
  href: string;
  icon: Component<Record<string, unknown>>;
  accent: string;
  access: SettingsSectionAccess;
};

const icon = (component: Component<Record<string, unknown>>) => component;

export const settingsSections: readonly SettingsSection[] = [
  {
    id: SETTING_SECTION.libraries,
    title: "Watched Libraries",
    description: "Add mounted folders, choose media types, and control per-library access.",
    href: "/settings/libraries",
    icon: icon(FolderOpen as unknown as Component<Record<string, unknown>>),
    accent: PRISM_MATERIAL_SPECTRUM.cyan,
    access: SETTINGS_SECTION_ACCESS.manager,
  },
  {
    id: SETTING_SECTION.users,
    title: "Users",
    description: "Manage accounts, passwords, NSFW permissions, and library grants.",
    href: "/settings/users",
    icon: icon(UsersRound as unknown as Component<Record<string, unknown>>),
    accent: PRISM_MATERIAL_SPECTRUM.violet,
    access: SETTINGS_SECTION_ACCESS.admin,
  },
  {
    id: SETTING_SECTION.acquisition,
    title: "Acquisition",
    description: "Configure indexers, download clients, profiles, custom formats, and blocklists.",
    href: "/settings/acquisition",
    icon: icon(Send as unknown as Component<Record<string, unknown>>),
    accent: PRISM_MATERIAL_SPECTRUM.orange,
    access: SETTINGS_SECTION_ACCESS.admin,
  },
  {
    id: SETTING_SECTION.playback,
    title: "Playback",
    description: "Set player defaults and HLS behavior for video playback.",
    href: "/settings/playback",
    icon: icon(Film as unknown as Component<Record<string, unknown>>),
    accent: PRISM_MATERIAL_SPECTRUM.red,
    access: SETTINGS_SECTION_ACCESS.admin,
  },
  {
    id: SETTING_SECTION.subtitles,
    title: "Subtitles",
    description: "Tune caption behavior, style, scale, opacity, and screen position.",
    href: "/settings/subtitles",
    icon: icon(Captions as unknown as Component<Record<string, unknown>>),
    accent: PRISM_MATERIAL_SPECTRUM.yellow,
    access: SETTINGS_SECTION_ACCESS.admin,
  },
  {
    id: SETTING_SECTION.generation,
    title: "Generation Pipeline",
    description: "Control scan cadence, collections, taxonomy, previews, and background jobs.",
    href: "/settings/generation",
    icon: icon(ScanSearch as unknown as Component<Record<string, unknown>>),
    accent: PRISM_MATERIAL_SPECTRUM.green,
    access: SETTINGS_SECTION_ACCESS.admin,
  },
  {
    id: SETTING_SECTION.autoIdentify,
    title: "Auto Identify",
    description: "Choose trusted plugins and matching rules for scan-time identification.",
    href: "/settings/auto-identify",
    icon: icon(Sparkles as unknown as Component<Record<string, unknown>>),
    accent: PRISM_MATERIAL_SPECTRUM.magenta,
    access: SETTINGS_SECTION_ACCESS.admin,
  },
  {
    id: SETTING_SECTION.transcodeCache,
    title: "Transcode Cache",
    description: "Review prepared video cache usage and set the cache size limit.",
    href: "/settings/transcode-cache",
    icon: icon(HardDrive as unknown as Component<Record<string, unknown>>),
    accent: PRISM_MATERIAL_SPECTRUM.blue,
    access: SETTINGS_SECTION_ACCESS.admin,
  },
  {
    id: SETTING_SECTION.databaseBackups,
    title: "Database Backups",
    description: "Create backups, inspect retention, and restore a known-good database snapshot.",
    href: "/settings/database-backups",
    icon: icon(Archive as unknown as Component<Record<string, unknown>>),
    accent: PRISM_MATERIAL_SPECTRUM.cyan,
    access: SETTINGS_SECTION_ACCESS.admin,
  },
  {
    id: SETTING_SECTION.diagnostics,
    title: "Diagnostics",
    description: "Run focused maintenance actions for troubleshooting generated assets and fingerprints.",
    href: "/settings/diagnostics",
    icon: icon(Wrench as unknown as Component<Record<string, unknown>>),
    accent: PRISM_MATERIAL_SPECTRUM.orange,
    access: SETTINGS_SECTION_ACCESS.admin,
  },
];

export function settingsSectionById(id: string | null | undefined): SettingsSection | null {
  return settingsSections.find((section) => section.id === id) ?? null;
}

export function visibleSettingsSections(session: { canManageServer: boolean; isAdmin: boolean }): SettingsSection[] {
  if (!session.canManageServer) return [];
  return settingsSections.filter((section) => {
    if (section.access === SETTINGS_SECTION_ACCESS.admin) return session.isAdmin;
    return true;
  });
}

export const settingsDirectoryIcon = Settings2 as unknown as Component<Record<string, unknown>>;
export const settingsDirectoryAccent = PRISM_MATERIAL_SPECTRUM.cyan;
