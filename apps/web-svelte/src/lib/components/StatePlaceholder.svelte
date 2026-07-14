<script lang="ts">
  import type { Component } from "svelte";
  import PrismediaLoadingMark from "./PrismediaLoadingMark.svelte";

  interface Props {
    /** Contextual icon (lucide component) shown centered in the badge. */
    icon: Component;
    title: string;
    description?: string;
    /** Renders a spinning accent ring around the icon to signal active work. */
    busy?: boolean;
  }

  let { icon, title, description, busy = false }: Props = $props();
  const Icon = $derived(icon);
</script>

<div class="state-placeholder" role={busy ? undefined : "status"} aria-busy={busy || undefined}>
  {#if busy}
    <PrismediaLoadingMark label={title} compact />
  {:else}
    <span class="badge">
      <Icon class="badge-icon" aria-hidden="true" />
    </span>
  {/if}
  <strong>{title}</strong>
  {#if description}<span class="desc">{description}</span>{/if}
</div>

<style>
  .state-placeholder {
    display: grid;
    gap: 0.4rem;
    place-content: center;
    justify-items: center;
    min-height: 11rem;
    padding: 2.25rem 1.25rem;
    background: var(--color-surface-1, #0c0f15);
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    border-radius: var(--radius-sm, 6px);
    box-shadow: inset 0 2px 8px rgba(0, 0, 0, 0.3);
    text-align: center;
  }

  .badge {
    position: relative;
    display: grid;
    place-items: center;
    width: 3rem;
    height: 3rem;
    margin-bottom: 0.35rem;
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.1));
    border-radius: var(--radius-full, 999px);
    background: var(--color-surface-2, #11151d);
    color: var(--color-text-disabled, #6b7486);
  }

  .badge :global(.badge-icon) {
    width: 1.4rem;
    height: 1.4rem;
  }

  strong {
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 0.95rem;
    font-weight: 600;
    color: var(--color-text-primary, #f4efe6);
  }

  .desc {
    max-width: 28rem;
    font-size: 0.8rem;
    line-height: 1.45;
    color: var(--color-text-muted, #8a93a6);
  }

</style>
