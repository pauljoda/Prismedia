<script lang="ts">
  import { cn } from "@prismedia/ui-svelte";
  import type { LibraryFamily } from "./library-composition";

  interface Props {
    families: LibraryFamily[];
    /** Bar thickness in pixels. */
    height?: number;
    /** Show the labelled legend under the bar. */
    legend?: boolean;
    /** Paint with the emitted brand spectrum (a literal light moment) instead of flat material. */
    emitted?: boolean;
    class?: string;
  }

  let { families, height = 10, legend = true, emitted = false, class: className }: Props = $props();

  /** Smallest share a present family keeps, so a single album is still a visible line of light. */
  const MIN_SHARE = 0.018;

  const present = $derived(families.filter((family) => family.count > 0));
  const total = $derived(present.reduce((sum, family) => sum + family.count, 0));
  const shares = $derived.by(() => {
    if (total === 0) return [];
    const raw = present.map((family) => Math.max(MIN_SHARE, family.count / total));
    const scale = raw.reduce((sum, share) => sum + share, 0);
    return raw.map((share) => share / scale);
  });
  const numberFormat = new Intl.NumberFormat();
</script>

<div class={cn("flex flex-col gap-3", className)}>
  <div
    class="flex w-full gap-[2px] overflow-hidden rounded-[var(--radius-xs)]"
    style:height="{height}px"
    role="img"
    aria-label={present.map((family) => `${family.label} ${family.count}`).join(", ")}
  >
    {#each present as family, index (family.kind)}
      {@const pair = emitted ? family.emitted : family.accent}
      <a
        href={family.href}
        class="block h-full transition-[filter,opacity] duration-150 hover:brightness-125 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--color-accent)]"
        style:flex-basis="{(shares[index] ?? 0) * 100}%"
        style:background="linear-gradient(90deg, {pair.primary}, {pair.secondary})"
        aria-label="{family.label}: {numberFormat.format(family.count)}"
      ></a>
    {/each}
    {#if present.length === 0}
      <div class="h-full w-full bg-[var(--color-surface-3)]"></div>
    {/if}
  </div>

  {#if legend}
    <ul class="flex flex-wrap gap-x-5 gap-y-2">
      {#each families as family (family.kind)}
        {@const Icon = family.icon}
        <li>
          <a
            href={family.href}
            class={cn(
              "group flex items-center gap-2 text-caption text-text-secondary transition-colors hover:text-text-primary",
              family.count === 0 && "opacity-45",
            )}
          >
            <span class="h-2.5 w-2.5 rounded-[2px]" style:background={family.accent.primary}></span>
            <Icon class="h-3.5 w-3.5 text-text-muted group-hover:text-text-secondary" aria-hidden="true" />
            <span>{family.label}</span>
            <span class="font-mono text-text-muted">{numberFormat.format(family.count)}</span>
          </a>
        </li>
      {/each}
    </ul>
  {/if}
</div>
