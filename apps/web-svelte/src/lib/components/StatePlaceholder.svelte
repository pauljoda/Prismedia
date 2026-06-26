<script lang="ts">
  import type { Component } from "svelte";

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

<div class="state-placeholder" role="status" aria-busy={busy}>
  <span class="badge" class:is-busy={busy}>
    {#if busy}<span class="ring" aria-hidden="true"></span>{/if}
    <Icon class="badge-icon" aria-hidden="true" />
  </span>
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

  .badge.is-busy {
    color: var(--color-text-accent, #f2c26a);
    box-shadow: 0 0 18px rgba(242, 194, 106, 0.12);
  }

  .badge :global(.badge-icon) {
    width: 1.4rem;
    height: 1.4rem;
  }

  /* Spinning accent ring = the activity indicator. */
  .ring {
    position: absolute;
    inset: -3px;
    border-radius: var(--radius-full, 999px);
    border: 2px solid transparent;
    border-top-color: var(--color-text-accent, #f2c26a);
    border-right-color: color-mix(in srgb, var(--color-text-accent, #f2c26a) 45%, transparent);
    animation: state-placeholder-spin 0.9s linear infinite;
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

  @keyframes state-placeholder-spin {
    to {
      transform: rotate(360deg);
    }
  }

  @media (prefers-reduced-motion: reduce) {
    .ring {
      animation: none;
      border-right-color: var(--color-text-accent, #f2c26a);
    }
  }
</style>
