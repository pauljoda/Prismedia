<script lang="ts">
  import { StatusLed } from "@prismedia/ui-svelte";
  import type { LibraryRoot } from "$lib/api/settings";
  import BandStrip from "./BandStrip.svelte";
  import { libraryRootBands, libraryRootState } from "./library-roots";

  interface Props {
    root: LibraryRoot;
  }

  let { root }: Props = $props();

  const bands = $derived(libraryRootBands(root));
  const rootState = $derived(libraryRootState(root));
  const qualifiers = $derived(
    [root.isReadOnly ? "Read-only" : null, root.externalOrigin ? "Connected" : null, root.isNsfw ? "NSFW" : null].filter(
      (value): value is string => value !== null,
    ),
  );
</script>

<!-- One watched folder: what it feeds (bands), where it lives, and whether it is being scanned. -->
<li
  class="flex min-w-0 flex-col gap-2 rounded-[var(--radius-sm)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-1)] px-3 py-2.5"
  class:opacity-60={!root.enabled}
>
  <div class="min-w-0">
    <p class="truncate text-label font-medium text-text-primary" title={root.label}>{root.label}</p>
    <p class="hidden truncate font-mono text-[0.64rem] text-text-disabled sm:block" title={root.path}>{root.path}</p>
  </div>
  <BandStrip {bands} height={4} label="Scans" />
  <div class="flex min-w-0 flex-wrap items-center justify-between gap-x-3 gap-y-1 text-[0.68rem] text-text-muted">
    <span class="flex min-w-0 flex-wrap gap-x-2">
      <span>{bands.map((band) => band.label).join(" · ") || "—"}</span>
      {#each qualifiers as qualifier (qualifier)}
        <span class="font-mono uppercase tracking-[0.08em] text-text-disabled">{qualifier}</span>
      {/each}
    </span>
    <span class="flex shrink-0 items-center gap-1.5">
      <StatusLed status={rootState.led} size="sm" />
      {rootState.label}
    </span>
  </div>
</li>
