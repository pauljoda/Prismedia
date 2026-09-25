<script module lang="ts">
  import type { LibraryRootMediaCapabilityCode } from "$lib/api/generated/codes";

  /** A library flag the card can switch: one of the media-capability codes or a root-level switch. */
  export type LibraryFlag = LibraryRootMediaCapabilityCode | "enabled" | "isNsfw" | "autoIdentify";
</script>

<script lang="ts">
  import { ExternalLink, EyeOff, FolderOpen, Loader2, RefreshCw, Sparkles, Trash2, UsersRound } from "@lucide/svelte";
  import { Button, Toggle, cn } from "@prismedia/ui-svelte";
  import { ENTITY_KIND, LIBRARY_ROOT_MEDIA_CAPABILITY } from "$lib/api/generated/codes";
  import { getGetPluginIconUrl } from "$lib/api/generated/prismedia";
  import type { LibraryRoot } from "$lib/api/settings";
  import PluginIcon from "$lib/components/plugins/PluginIcon.svelte";
  import { bandGradient, mediaFamilyForKind } from "$lib/entities/media-families";
  import { formatRelativeTime } from "$lib/utils/format";

  interface Props {
    root: LibraryRoot;
    scanning?: boolean;
    canManageAccess?: boolean;
    /** Whether this account may see and set the NSFW flag. */
    showNsfw?: boolean;
    onToggle: (root: LibraryRoot, flag: LibraryFlag) => void;
    onScan: (root: LibraryRoot) => void;
    onAccess: (root: LibraryRoot) => void;
    onRemove: (root: LibraryRoot) => void;
  }

  let {
    root,
    scanning = false,
    canManageAccess = false,
    showNsfw = false,
    onToggle,
    onScan,
    onAccess,
    onRemove,
  }: Props = $props();

  /** What a library can scan for, each painted as the media family it feeds. */
  const SCANS = [
    { flag: LIBRARY_ROOT_MEDIA_CAPABILITY.scanVideos, label: "Video", family: mediaFamilyForKind(ENTITY_KIND.video) },
    { flag: LIBRARY_ROOT_MEDIA_CAPABILITY.scanImages, label: "Images", family: mediaFamilyForKind(ENTITY_KIND.image) },
    { flag: LIBRARY_ROOT_MEDIA_CAPABILITY.scanAudio, label: "Audio", family: mediaFamilyForKind(ENTITY_KIND.audioLibrary) },
    { flag: LIBRARY_ROOT_MEDIA_CAPABILITY.scanBooks, label: "Books & comics", family: mediaFamilyForKind(ENTITY_KIND.book) },
  ] as const satisfies ReadonlyArray<{ flag: LibraryFlag; label: string; family: unknown }>;

  const readOnly = $derived(Boolean(root.isReadOnly));
  const lit = $derived(SCANS.filter((scan) => root[scan.flag]));
  const beam = $derived.by(() => {
    const neutral = "color-mix(in oklab, var(--color-text-primary) 22%, transparent)";
    if (lit.length === 0 || !root.enabled) return `linear-gradient(90deg, ${neutral}, transparent)`;
    const stops = lit.flatMap((scan, index) => {
      const from = 40 + (60 * index) / lit.length;
      const to = 40 + (60 * (index + 1)) / lit.length;
      return [`${scan.family.accent.primary} ${from}%`, `${scan.family.accent.secondary} ${to}%`];
    });
    return `linear-gradient(90deg, ${neutral} 0%, ${stops.join(", ")})`;
  });
  const origin = $derived(root.externalOrigin ?? null);
</script>

<!-- One watched folder: where it is, which media families it feeds, and the switches that shape it. -->
<article
  class="relative flex min-w-0 flex-col overflow-hidden rounded-[var(--radius-md)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-2)] shadow-[var(--shadow-card)]"
  aria-label={root.label}
>
  <span class="absolute inset-x-0 top-0 h-px opacity-80" style:background={beam} aria-hidden="true"></span>

  <header class="flex items-start gap-3 px-4 pt-4">
    {#if origin}
      <PluginIcon name={origin.connectionName} iconUrl={getGetPluginIconUrl(origin.pluginId)} class="size-10" />
    {:else}
      <span
        class="grid size-10 shrink-0 place-items-center rounded-md bg-surface-raised text-text-secondary ring-1 ring-inset ring-border-subtle/70"
      >
        <FolderOpen class="size-[46%]" aria-hidden="true" />
      </span>
    {/if}
    <div class={cn("min-w-0 flex-1", !root.enabled && "opacity-60")}>
      <h3 class="truncate font-heading text-sm font-semibold text-text-primary">{root.label}</h3>
      <p class="truncate font-mono text-[0.66rem] text-text-muted" title={root.path}>{root.path}</p>
      {#if origin}
        <p class="flex min-w-0 items-center gap-1.5 text-caption text-text-secondary">
          <span class="truncate">{origin.connectionName}</span>
          <span class="text-text-disabled">· read-only</span>
          <a
            href={origin.managementUrl}
            target="_blank"
            rel="noreferrer"
            class="shrink-0 text-text-muted transition-colors hover:text-text-primary"
            aria-label="Open {origin.connectionName}"
            title="Open {origin.connectionName}"
          >
            <ExternalLink class="size-3" aria-hidden="true" />
          </a>
        </p>
        <p class="truncate font-mono text-[0.62rem] text-text-disabled" title={origin.remotePath}>{origin.remotePath}</p>
      {:else if readOnly}
        <p class="text-caption text-text-muted">Read-only</p>
      {/if}
    </div>
    <Toggle
      size="sm"
      checked={root.enabled}
      ariaLabel="{root.enabled ? 'Disable' : 'Enable'} {root.label}"
      onchange={() => onToggle(root, "enabled")}
    />
  </header>

  <ul class={cn("flex flex-col gap-1 px-4 pt-4", !root.enabled && "opacity-60")} aria-label="Media {root.label} scans">
    {#each SCANS as scan (scan.flag)}
      {@const on = root[scan.flag]}
      <li class="grid min-w-0 grid-cols-[1.75rem_minmax(0,1fr)_auto] items-center gap-2.5">
        <span
          class="h-1.5 rounded-[2px]"
          style:background={on ? bandGradient(scan.family.accent) : "var(--color-surface-3)"}
          aria-hidden="true"
        ></span>
        <span class={cn("truncate text-caption", on ? "text-text-secondary" : "text-text-disabled")}>{scan.label}</span>
        <Toggle
          size="sm"
          checked={on}
          ariaLabel="Scan {scan.label} in {root.label}"
          onchange={() => onToggle(root, scan.flag)}
        />
      </li>
    {/each}
  </ul>

  <ul class="mt-3 flex flex-col gap-1 border-t border-[var(--color-border-subtle)] px-4 pt-3 pb-4">
    {#if showNsfw}
      <li class="grid grid-cols-[1.75rem_minmax(0,1fr)_auto] items-center gap-2.5">
        <EyeOff class="size-3.5 justify-self-center text-text-muted" aria-hidden="true" />
        <span class="text-caption text-text-secondary">NSFW</span>
        <Toggle size="sm" checked={root.isNsfw} ariaLabel="Mark {root.label} NSFW" onchange={() => onToggle(root, "isNsfw")} />
      </li>
    {/if}
    <li class="grid grid-cols-[1.75rem_minmax(0,1fr)_auto] items-center gap-2.5">
      <Sparkles class="size-3.5 justify-self-center text-text-muted" aria-hidden="true" />
      <span class="text-caption text-text-secondary">Auto identify</span>
      <Toggle
        size="sm"
        checked={Boolean(root.autoIdentify)}
        ariaLabel="Auto identify {root.label}"
        onchange={() => onToggle(root, "autoIdentify")}
      />
    </li>
  </ul>

  <footer class="mt-auto flex items-center justify-between gap-3 border-t border-[var(--color-border-subtle)] py-1.5 pr-1.5 pl-4">
    <span class="truncate font-mono text-[0.64rem] text-text-disabled">
      {root.lastScannedAt ? `scan ${formatRelativeTime(root.lastScannedAt, true)}` : "not scanned"}
    </span>
    <div class="flex items-center gap-1">
      <Button variant="ghost" size="sm" disabled={scanning || !root.enabled} onclick={() => onScan(root)}>
        {#if scanning}<Loader2 class="animate-spin" aria-hidden="true" />{:else}<RefreshCw aria-hidden="true" />{/if}
        Scan
      </Button>
      {#if canManageAccess}
        <Button variant="ghost" size="icon-sm" aria-label="Access to {root.label}" title="Access" onclick={() => onAccess(root)}>
          <UsersRound aria-hidden="true" />
        </Button>
      {/if}
      <Button
        variant="ghost"
        size="icon-sm"
        class="text-text-muted hover:text-error-text"
        disabled={readOnly}
        aria-label="Remove {root.label}"
        title={readOnly ? "Turn off scanning to pause an external library" : "Remove"}
        onclick={() => onRemove(root)}
      >
        <Trash2 aria-hidden="true" />
      </Button>
    </div>
  </footer>
</article>
