<script lang="ts">
  import type { Snippet } from "svelte";
  import { ChevronDown } from "@lucide/svelte";

  interface Props {
    actions?: Snippet;
    children: Snippet;
    icon: Snippet;
    lazy?: boolean;
    meta?: string | null;
    panelId: string;
    title: string;
    /** Render the section collapsed on first mount. The content stays unmounted until expanded. */
    startCollapsed?: boolean;
  }

  let {
    actions,
    children,
    icon,
    lazy = false,
    meta = null,
    panelId,
    title,
    startCollapsed = false,
  }: Props = $props();

  // svelte-ignore state_referenced_locally
  let collapsed = $state(startCollapsed);
  const contentId = $derived(`${panelId}-content`);
  const sectionClass = $derived(
    `surface-panel overflow-hidden${lazy ? " review-lazy-section" : ""}${collapsed ? " is-collapsed" : ""}`,
  );
  const chevronClass = $derived(`h-3.5 w-3.5 transition-transform${collapsed ? "" : " rotate-180"}`);

  function toggleCollapsed() {
    collapsed = !collapsed;
  }
</script>

<section class={sectionClass}>
  <header class="review-section-header">
    <button
      type="button"
      class="review-section-toggle"
      aria-controls={contentId}
      aria-expanded={!collapsed}
      onclick={toggleCollapsed}
    >
      {@render icon()}
      <span class="text-kicker text-text-accent">{title}</span>
      {#if meta}
        <span class="font-mono text-[0.7rem] text-text-muted">{meta}</span>
      {/if}
    </button>

    {#if actions}
      <div class="review-section-actions">
        {@render actions()}
      </div>
    {/if}

    <button
      type="button"
      class="review-section-collapse"
      aria-label={collapsed ? "Expand section" : "Collapse section"}
      aria-controls={contentId}
      aria-expanded={!collapsed}
      title={collapsed ? `Expand ${title}` : `Collapse ${title}`}
      onclick={toggleCollapsed}
    >
      <ChevronDown class={chevronClass} />
    </button>
  </header>

  {#if !collapsed}
    <div id={contentId}>
      {@render children()}
    </div>
  {/if}
</section>

<style>
  .review-lazy-section {
    content-visibility: auto;
    contain-intrinsic-size: auto 36rem;
  }

  .review-section-header {
    display: flex;
    align-items: center;
    gap: 0.625rem;
    border-bottom: 1px solid var(--color-border-subtle);
    background: var(--color-surface-2);
    padding: 0.625rem 0.875rem;
  }

  .is-collapsed .review-section-header {
    border-bottom: 0;
  }

  .review-section-toggle {
    display: flex;
    min-width: 0;
    min-height: 1.5rem;
    flex: 1 1 auto;
    align-items: center;
    gap: 0.625rem;
    border: 0;
    background: transparent;
    color: inherit;
    padding: 0;
    text-align: left;
  }

  .review-section-toggle:focus-visible,
  .review-section-collapse:focus-visible {
    outline: 1px solid rgb(242 194 106 / 0.72);
    outline-offset: 2px;
  }

  .review-section-actions {
    display: flex;
    flex: 0 0 auto;
    align-items: center;
    gap: 0.75rem;
  }

  .review-section-collapse {
    display: inline-flex;
    width: 1.75rem;
    height: 1.75rem;
    flex: 0 0 auto;
    align-items: center;
    justify-content: center;
    border: 1px solid transparent;
    border-radius: var(--radius-xs);
    background: transparent;
    color: var(--color-text-muted);
    transition:
      background-color 160ms ease,
      border-color 160ms ease,
      color 160ms ease;
  }

  .review-section-collapse:hover {
    border-color: var(--color-border-default);
    background: var(--color-surface-3);
    color: var(--color-text-primary);
  }
</style>
