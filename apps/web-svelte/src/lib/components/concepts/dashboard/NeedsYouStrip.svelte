<script lang="ts">
  import { resolve } from "$app/paths";
  import { ChevronRight } from "@lucide/svelte";
  import { Badge, StatusLed, cn } from "@prismedia/ui-svelte";
  import { digestDownloads, needsYouItems, type LibraryPulse } from "./dashboard-data";

  interface Props {
    pulse: LibraryPulse;
    class?: string;
  }

  let { pulse, class: className }: Props = $props();

  const items = $derived(needsYouItems(pulse));
  const digest = $derived(digestDownloads(pulse.downloads));
  const readable = $derived(pulse.identify !== null || pulse.downloads !== null || pulse.worker !== null);
</script>

<!--
  One line for work waiting on a decision: icon, action, and title per entry, all ordinary links.
  With nothing waiting, only the live status remains.
-->
{#if readable}
  <section
    aria-label="Needs you"
    class={cn(
      "flex flex-col gap-2 rounded-[var(--radius-md)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-1)] px-3 py-2.5 sm:px-4 lg:flex-row lg:items-center lg:gap-5",
      className,
    )}
  >
    {#if items.length > 0}
      <h2 class="shrink-0 font-mono text-[0.68rem] uppercase tracking-[0.16em] text-text-muted">Needs you</h2>
      <ul class="flex min-w-0 flex-col gap-0.5 sm:flex-row sm:flex-wrap sm:gap-x-5">
        {#each items as item (item.key)}
          {@const Icon = item.icon}
          <li class="min-w-0">
            <a
              href={item.href}
              class="group flex min-h-9 min-w-0 items-center gap-2 rounded-[var(--radius-xs)] text-sm text-text-secondary transition-colors hover:text-text-primary focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--color-accent)]"
            >
              <Icon class="h-3.5 w-3.5 shrink-0 text-text-muted group-hover:text-text-secondary" aria-hidden="true" />
              <span class="shrink-0 font-medium text-text-primary">{item.action}</span>
              {#if item.count > 1}
                <Badge variant="outline" class="font-mono tabular-nums">{item.count}</Badge>
              {:else}
                <span class="min-w-0 truncate text-text-muted">{item.subject}</span>
              {/if}
              <ChevronRight class="h-3.5 w-3.5 shrink-0 text-text-disabled group-hover:text-text-muted" aria-hidden="true" />
            </a>
          </li>
        {/each}
      </ul>
    {/if}

    <p class="flex flex-wrap items-center gap-x-3 gap-y-1 text-caption text-text-muted lg:ml-auto lg:shrink-0">
      {#if pulse.worker}
        <span class="inline-flex items-center gap-1.5" title={pulse.worker.tooltip}>
          <StatusLed status={pulse.worker.led} size="sm" pulse={pulse.worker.pulse} />
          {pulse.worker.label}
        </span>
      {/if}
      {#if pulse.downloads !== null && (digest.transferring.length > 0 || digest.waiting.length > 0)}
        <a href={resolve("/downloads")} class="font-mono tabular-nums transition-colors hover:text-text-primary">
          {digest.transferring.length} moving · {digest.waiting.length} waiting
        </a>
      {/if}
    </p>
  </section>
{/if}
