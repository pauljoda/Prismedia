<script lang="ts">
  import type { Snippet } from "svelte";
  import { Collapsible, buttonVariants, cn } from "@prismedia/ui-svelte";
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

</script>

<Collapsible.Root bind:open={() => !collapsed, next => collapsed = !next}>
<section class={sectionClass}>
  <header class="review-section-header">
    <Collapsible.Trigger class={cn(buttonVariants({ variant: "ghost", size: "sm" }), "min-w-0 h-auto flex-1 justify-start whitespace-normal px-0 gap-2.5")}
    >
      {@render icon()}
      <span class="text-kicker text-text-accent">{title}</span>
      {#if meta}
        <span class="font-mono text-[0.7rem] text-text-muted">{meta}</span>
      {/if}
    </Collapsible.Trigger>

    {#if actions}
      <div class="review-section-actions">
        {@render actions()}
      </div>
    {/if}

    <Collapsible.Trigger class={buttonVariants({ variant: "ghost", size: "icon-sm" })}
      aria-label={collapsed ? "Expand section" : "Collapse section"}
      aria-controls={contentId}
      aria-expanded={!collapsed}
      title={collapsed ? `Expand ${title}` : `Collapse ${title}`}
    >
      <ChevronDown class={chevronClass} />
    </Collapsible.Trigger>
  </header>

  <Collapsible.Content id={contentId}>
    {#if !collapsed}{@render children()}{/if}
  </Collapsible.Content>
</section>
</Collapsible.Root>

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



  .review-section-actions {
    display: flex;
    flex: 0 0 auto;
    align-items: center;
    gap: 0.75rem;
  }


</style>
