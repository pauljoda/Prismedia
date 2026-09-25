<script lang="ts">
  import type { Component, Snippet } from "svelte";
  import { cn } from "@prismedia/ui-svelte";

  interface Props {
    title: string;
    icon?: Component;
    /** Compact live facts shown beside the title (status LEDs, counts). */
    status?: Snippet;
    actions?: Snippet;
    class?: string;
  }

  let { title, icon: Icon, status, actions, class: className }: Props = $props();
</script>

<!--
  One management header for every operate/settings page. Its single accent moment is the beam
  hairline underneath: neutral white light on the left that disperses into the spectrum on the right.
-->
<header class={cn("relative flex flex-col gap-4 pb-5", className)}>
  <div class="flex flex-wrap items-start justify-between gap-4">
    <div class="flex min-w-0 items-center gap-3.5">
      {#if Icon}
        <span
          class="grid h-10 w-10 shrink-0 place-items-center rounded-[var(--radius-md)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-2)] text-text-secondary shadow-[var(--shadow-card)]"
        >
          <Icon class="h-[18px] w-[18px]" aria-hidden="true" />
        </span>
      {/if}
      <h1 class="min-w-0 self-center font-heading text-2xl font-semibold tracking-tight text-text-primary">{title}</h1>
    </div>
    {#if actions}
      <div class="flex flex-wrap items-center gap-2">{@render actions()}</div>
    {/if}
  </div>
  {#if status}
    <div class="flex flex-wrap items-center gap-x-6 gap-y-2">{@render status()}</div>
  {/if}
  <div class="prism-beam absolute inset-x-0 bottom-0 h-px" aria-hidden="true"></div>
</header>

<style>
  .prism-beam {
    background: linear-gradient(
      90deg,
      color-mix(in oklab, var(--color-text-primary) 38%, transparent) 0%,
      color-mix(in oklab, var(--color-text-primary) 22%, transparent) 42%,
      var(--color-spectrum-red) 58%,
      var(--color-spectrum-orange) 64%,
      var(--color-spectrum-yellow) 70%,
      var(--color-spectrum-green) 76%,
      var(--color-spectrum-cyan) 82%,
      var(--color-spectrum-blue) 88%,
      var(--color-spectrum-violet) 94%,
      var(--color-spectrum-magenta) 100%
    );
    opacity: 0.55;
  }
</style>
