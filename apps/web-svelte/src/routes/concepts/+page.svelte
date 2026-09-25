<script lang="ts">
  import { ArrowRight, FolderOpen, ScanSearch, Settings2, Sparkles } from "@lucide/svelte";
  import ManagePageHeader from "$lib/components/manage/ManagePageHeader.svelte";

  const CONCEPTS = [
    { href: "/identify/families", title: "Identify by family", icon: ScanSearch, features: ["Queue first", "Family cards", "Unidentified counts"] },
    { href: "/concepts/libraries", title: "Library cards", icon: FolderOpen, features: ["Family scan rows", "Scan per library", "Access & remove"] },
    { href: "/concepts/settings", title: "Settings rail", icon: Settings2, features: ["Grouped rail", "Section beside it", "No index page"] },
  ];
</script>

<svelte:head><title>Concepts · Prismedia</title></svelte:head>

<div class="mx-auto flex w-full max-w-5xl min-w-0 flex-col gap-6 pb-16">
  <ManagePageHeader icon={Sparkles} title="Concepts" />
  <ul class="grid gap-3 md:grid-cols-3">
    {#each CONCEPTS as concept (concept.href)}
      {@const Icon = concept.icon}
      <li>
        <a
          href={concept.href}
          class="group flex h-full flex-col gap-3 rounded-[var(--radius-md)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-2)] p-4 shadow-[var(--shadow-card)] transition-colors hover:border-[var(--color-border-default)] hover:bg-[var(--color-surface-3)] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--color-border-accent-strong)]"
        >
          <span class="flex items-center gap-2.5">
            <Icon class="size-4 text-text-secondary" aria-hidden="true" />
            <span class="flex-1 font-heading text-sm font-semibold text-text-primary">{concept.title}</span>
            <ArrowRight class="size-4 text-text-muted transition-transform group-hover:translate-x-0.5" aria-hidden="true" />
          </span>
          <span class="flex flex-wrap gap-1.5">
            {#each concept.features as feature (feature)}
              <span class="rounded-[var(--radius-xs)] border border-[var(--color-border-subtle)] px-2 py-0.5 text-caption text-text-muted">{feature}</span>
            {/each}
          </span>
        </a>
      </li>
    {/each}
  </ul>
</div>
