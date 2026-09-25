<script module lang="ts">
  /** The single next step a tile offers; always a real page. */
  export interface TileAction {
    label: string;
    href: string;
  }

  /** One supporting fact: a value with its unit, e.g. { value: "15", label: "queued" }. */
  export interface TileFact {
    value: string;
    label: string;
    tone?: "error" | "warning";
  }
</script>

<script lang="ts">
  import type { Component, Snippet } from "svelte";
  import { ArrowRight } from "@lucide/svelte";
  import { Panel, Skeleton, StatusLed, buttonVariants, cn, type LedStatus } from "@prismedia/ui-svelte";

  interface Props {
    title: string;
    icon: Component;
    led: LedStatus;
    /** One or two status words beside the LED, e.g. "Online" or "2 failed". */
    stateLabel: string;
    /** The one figure that matters; null while loading. */
    figure: string | null;
    /** The figure's unit, e.g. "queued". */
    unit: string;
    facts?: TileFact[];
    action: TileAction;
    /** Emphasise the action when the tile needs a decision. */
    urgent?: boolean;
    pulse?: boolean;
    visual?: Snippet;
    class?: string;
  }

  let {
    title,
    icon: Icon,
    led,
    stateLabel,
    figure,
    unit,
    facts = [],
    action,
    urgent = false,
    pulse = false,
    visual,
    class: className,
  }: Props = $props();
</script>

<Panel
  class={cn(
    "flex min-w-0 flex-col border-[var(--color-border-subtle)] bg-[var(--color-surface-2)] shadow-[var(--shadow-card)]",
    className,
  )}
>
  <section class="flex h-full min-w-0 flex-col" aria-label={title}>
    <header class="flex items-center justify-between gap-3 px-4 pt-4">
      <div class="flex min-w-0 items-center gap-2.5">
        <span
          class="grid h-8 w-8 shrink-0 place-items-center rounded-[var(--radius-sm)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-1)] text-text-secondary"
        >
          <Icon class="h-4 w-4" aria-hidden="true" />
        </span>
        <h2 class="truncate font-heading text-sm font-semibold text-text-primary">{title}</h2>
      </div>
      <span class="flex shrink-0 items-center gap-1.5 font-mono text-[0.64rem] uppercase tracking-[0.12em] text-text-muted">
        <StatusLed status={led} size="sm" {pulse} />
        {stateLabel}
      </span>
    </header>

    <div class="flex flex-1 flex-col gap-3 px-4 pt-4 pb-4">
      <div class="flex min-w-0 items-baseline gap-2">
        {#if figure === null}
          <Skeleton class="h-8 w-14" />
        {:else}
          <span class="font-mono text-3xl leading-none font-medium tracking-tight text-text-primary tabular-nums">{figure}</span>
        {/if}
        <span class="min-w-0 truncate text-caption text-text-muted">{unit}</span>
      </div>
      {#if facts.length > 0}
        <ul class="flex flex-wrap gap-x-4 gap-y-1">
          {#each facts as fact (fact.label)}
            <li class="flex items-baseline gap-1.5 text-caption">
              <span
                class={cn(
                  "font-mono tabular-nums",
                  fact.tone === "error" ? "text-error-text" : fact.tone === "warning" ? "text-warning-text" : "text-text-secondary",
                )}
              >
                {fact.value}
              </span>
              <span class="text-text-muted">{fact.label}</span>
            </li>
          {/each}
        </ul>
      {/if}
      {#if visual}
        <div class="mt-auto min-w-0 pt-1">{@render visual()}</div>
      {/if}
    </div>

    <footer class="border-t border-[var(--color-border-subtle)] px-2 py-2">
      <a
        href={action.href}
        class={cn(
          buttonVariants({ variant: urgent ? "secondary" : "ghost", size: "sm" }),
          "w-full justify-between text-text-secondary hover:text-text-primary",
        )}
      >
        {action.label}
        <ArrowRight class="size-3.5" aria-hidden="true" />
      </a>
    </footer>
  </section>
</Panel>
