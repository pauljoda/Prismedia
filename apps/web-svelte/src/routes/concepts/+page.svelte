<script lang="ts">
  import { Sparkles } from "@lucide/svelte";
  import { onMount } from "svelte";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import PrismPageHeader from "$lib/components/concepts/PrismPageHeader.svelte";
  import SpectrumBar from "$lib/components/concepts/SpectrumBar.svelte";
  import { DESIGN_CONCEPTS } from "$lib/components/concepts/concept-catalog";
  import { loadLibraryComposition, libraryTotal, type LibraryFamily } from "$lib/components/concepts/library-composition";

  const nsfw = useNsfw();
  let families = $state<LibraryFamily[]>([]);

  onMount(() => {
    const controller = new AbortController();
    void loadLibraryComposition(nsfw.mode === "off", controller.signal).then((next) => { families = next; });
    return () => controller.abort();
  });

  const areas = [
    { key: "dashboard", label: "Dashboard" },
    { key: "management", label: "Management" },
  ] as const;
</script>

<svelte:head><title>Design concepts · Prismedia</title></svelte:head>

<div class="mx-auto flex w-full max-w-6xl flex-col gap-8 px-4 py-6 md:px-8">
  <PrismPageHeader icon={Sparkles} title="Concepts">
    {#snippet status()}
      <span class="font-mono text-caption text-text-muted">
        <span class="text-text-secondary">{new Intl.NumberFormat().format(libraryTotal(families))}</span> items
      </span>
    {/snippet}
  </PrismPageHeader>

  {#if families.length > 0}
    <SpectrumBar {families} height={8} />
  {/if}

  {#each areas as area (area.key)}
    <section class="flex flex-col gap-3">
      <h2 class="font-heading text-lg font-semibold text-text-primary">{area.label}</h2>
      <div class="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
        {#each DESIGN_CONCEPTS.filter((concept) => concept.area === area.key) as concept (concept.id)}
          <a
            href={concept.href}
            class="group flex flex-col gap-3 rounded-[var(--radius-lg)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-2)] p-4 shadow-[var(--shadow-card)] transition-colors hover:border-[var(--color-border-default)] hover:bg-[var(--color-surface-3)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--color-accent)]"
          >
            <div class="flex items-center justify-between gap-3">
              <h3 class="font-heading text-base font-semibold text-text-primary">{concept.title}</h3>
              <span class="font-mono text-[0.68rem] uppercase tracking-[0.14em] text-text-muted group-hover:text-text-secondary">Open →</span>
            </div>
            <ul class="flex flex-wrap gap-1.5">
              {#each concept.features as feature (feature)}
                <li class="rounded-[var(--radius-xs)] border border-[var(--color-border-subtle)] px-2 py-0.5 text-caption text-text-muted">{feature}</li>
              {/each}
            </ul>
          </a>
        {/each}
      </div>
    </section>
  {/each}
</div>
