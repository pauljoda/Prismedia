<script lang="ts">
  import { ChevronRight } from "@lucide/svelte";
  import { Progress, Skeleton, StatusLed } from "@prismedia/ui-svelte";
  import type { SettingsSection } from "$lib/settings/settings-section-catalog";
  import type { SectionStatus } from "./settings-families";

  interface Props {
    section: SettingsSection;
    /** Live state; undefined while loading, null when the section has none to show. */
    status: SectionStatus | null | undefined;
    /** Names of individual settings inside this section that match the current filter. */
    matches?: string[];
  }

  let { section, status, matches = [] }: Props = $props();
  const Icon = $derived(section.icon);
</script>

<!-- A whole-card link to one settings section, with what is configured now on its bottom edge. -->
<a
  href={section.href}
  class="group flex h-full min-w-0 flex-col rounded-[var(--radius-md)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-2)] shadow-[var(--shadow-card)] transition-colors duration-150 hover:border-[var(--color-border-default)] hover:bg-[var(--color-surface-3)] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--color-border-accent-strong)] motion-reduce:transition-none"
>
  <div class="flex flex-1 items-center gap-3 p-4">
    <span
      class="grid h-9 w-9 shrink-0 place-items-center rounded-[var(--radius-sm)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-1)] text-text-secondary transition-colors group-hover:text-text-primary"
    >
      <Icon class="h-4 w-4" aria-hidden="true" />
    </span>
    <div class="min-w-0 flex-1">
      <h3 class="font-heading text-sm font-semibold text-text-primary">{section.title}</h3>
      {#if matches.length > 0}
        <p class="mt-1.5 text-caption text-text-secondary">
          <span class="mr-1 font-mono text-[0.6rem] uppercase tracking-[0.12em] text-text-disabled">Matches</span>
          {matches.slice(0, 4).join(", ")}{matches.length > 4 ? ` +${matches.length - 4}` : ""}
        </p>
      {/if}
    </div>
    <ChevronRight
      class="h-4 w-4 shrink-0 text-text-disabled transition-colors group-hover:text-text-secondary"
      aria-hidden="true"
    />
  </div>
  <div class="flex min-h-10 items-center gap-2.5 border-t border-[var(--color-border-subtle)] px-4 py-2.5">
    {#if status === undefined}
      <Skeleton class="h-3 w-40" />
    {:else if status}
      <StatusLed status={status.led} size="sm" />
      <span class="min-w-0 flex-1 truncate font-mono text-[0.7rem] text-text-secondary" title={status.text}>{status.text}</span>
      {#if status.fill !== undefined}
        <Progress value={status.fill} aria-label="Used" class="h-1.5 w-14 shrink-0 rounded-xs" />
      {/if}
    {:else}
      <StatusLed status="error" size="sm" />
      <span class="font-mono text-[0.7rem] text-text-disabled">unavailable</span>
    {/if}
  </div>
</a>
