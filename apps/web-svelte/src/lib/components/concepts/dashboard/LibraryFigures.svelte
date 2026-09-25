<script lang="ts">
  import { Skeleton } from "@prismedia/ui-svelte";
  import CompositionBeam from "./CompositionBeam.svelte";
  import { libraryTotal, type LibraryFamily } from "../library-composition";

  interface Props {
    /** Library composition in spectrum order; empty while it loads. */
    families: LibraryFamily[];
  }

  let { families }: Props = $props();

  const present = $derived(families.filter((family) => family.count > 0));
  const total = $derived(libraryTotal(families));
  const numberFormat = new Intl.NumberFormat();
</script>

<!--
  The library as figures: one total, one icon and count per family in spectrum order, and the
  hairline of light beneath them, split in the same order and proportions.
-->
<section aria-label="Library composition" class="flex flex-col gap-6 pt-2 sm:gap-8 sm:pt-8">
  {#if families.length === 0}
    <div class="flex flex-col gap-4" aria-hidden="true">
      <Skeleton class="h-14 w-48 sm:h-20" />
      <Skeleton class="h-8 w-full max-w-xl" />
    </div>
  {:else}
    <p class="flex items-baseline gap-3">
      <span class="font-heading text-6xl font-medium tabular-nums tracking-tight text-text-primary sm:text-8xl">
        {numberFormat.format(total)}
      </span>
      <span class="font-heading text-xl text-text-muted sm:text-2xl">items</span>
    </p>

    {#if present.length > 0}
      <ul class="flex flex-wrap gap-x-7 gap-y-3 sm:gap-x-10">
        {#each present as family (family.kind)}
          {@const Icon = family.icon}
          <li>
            <!-- Some families share an icon (series and comics are both folders), so each keeps a small label. -->
            <a
              href={family.href}
              class="group flex min-h-10 items-center gap-2.5 rounded-[var(--radius-xs)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-[var(--color-accent)]"
            >
              <Icon class="h-5 w-5 shrink-0 text-text-muted transition-colors group-hover:text-text-secondary sm:h-6 sm:w-6" aria-hidden="true" />
              <span class="flex flex-col">
                <span class="font-heading text-2xl leading-none tabular-nums text-text-secondary transition-colors group-hover:text-text-primary sm:text-3xl">
                  {numberFormat.format(family.count)}
                </span>
                <span class="mt-1 font-mono text-[0.6rem] uppercase tracking-[0.14em] text-text-disabled transition-colors group-hover:text-text-muted">
                  {family.label}
                </span>
              </span>
            </a>
          </li>
        {/each}
      </ul>
    {/if}
  {/if}

  <CompositionBeam {families} class="mt-1" />
</section>

<style>
  @media (prefers-reduced-motion: reduce) {
    a :global(svg),
    a span {
      transition: none;
    }
  }
</style>
