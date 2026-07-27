<script lang="ts">
  /**
   * The entity families a plugin can identify, as compact chips. Each chip carries its family's
   * muted accent on a thin rail so a plugin's coverage is scannable by colour before it is read,
   * and lists the actions the plugin supports for that family.
   */
  import { cn } from "@prismedia/ui-svelte";
  import { entityKindIcon } from "$lib/entities/entity-kind-icons";
  import type { PluginCapability } from "$lib/plugins/plugin-capabilities";

  interface Props {
    capabilities: PluginCapability[];
    /** Collapses to this many chips with a "+N" remainder; omit to show every chip. */
    limit?: number | null;
    class?: string;
  }

  let { capabilities, limit = null, class: className }: Props = $props();

  const visible = $derived(limit == null ? capabilities : capabilities.slice(0, limit));
  const hidden = $derived(capabilities.length - visible.length);
  const hiddenTitle = $derived(
    capabilities
      .slice(visible.length)
      .map((capability) => capability.label)
      .join(", "),
  );
</script>

{#if capabilities.length > 0}
  <ul class={cn("capability-chips", className)}>
    {#each visible as capability (capability.entityKind)}
      {@const Icon = entityKindIcon(capability.entityKind)}
      <li
        class="capability-chip"
        style:--family-accent={capability.accent.primary}
        title={`${capability.label}: ${capability.actionLabels.join(", ")}`}
      >
        <span class="capability-rail" aria-hidden="true"></span>
        <Icon class="h-3 w-3 shrink-0 text-text-disabled" aria-hidden="true" />
        <span class="capability-label">{capability.label}</span>
        <span class="capability-actions">{capability.actionLabels.join(" · ")}</span>
      </li>
    {/each}
    {#if hidden > 0}
      <li class="capability-chip capability-chip-more" title={hiddenTitle}>+{hidden}</li>
    {/if}
  </ul>
{/if}

<style>
  .capability-chips {
    display: flex;
    flex-wrap: wrap;
    gap: 0.25rem;
    margin: 0;
    padding: 0;
    list-style: none;
    min-width: 0;
  }

  .capability-chip {
    display: inline-flex;
    align-items: center;
    gap: 0.3rem;
    padding: 0.14rem 0.4rem 0.14rem 0;
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-xs);
    background: var(--color-surface-2);
    overflow: hidden;
  }

  /* The family's colour is a thin rail, not a fill: a row of saturated pills would drown the page. */
  .capability-rail {
    align-self: stretch;
    width: 2px;
    margin-right: 0.15rem;
    background: var(--family-accent);
  }

  .capability-label {
    font-size: 0.64rem;
    font-weight: 600;
    letter-spacing: 0.01em;
    color: var(--color-text-secondary);
    white-space: nowrap;
  }

  .capability-actions {
    font-family: var(--font-mono);
    font-size: 0.58rem;
    color: var(--color-text-disabled);
    white-space: nowrap;
  }

  .capability-chip-more {
    padding-inline: 0.4rem;
    font-family: var(--font-mono);
    font-size: 0.6rem;
    color: var(--color-text-disabled);
  }
</style>
