<script lang="ts">
  import { onMount } from "svelte";
  import { ArrowRight, ScanSearch, Send, Sparkles } from "@lucide/svelte";
  import { IDENTIFY_QUEUE_STATE } from "$lib/api/generated/codes";
  import { fetchIdentifyQueue } from "$lib/api/identify-client";
  import ManagePageHeader from "$lib/components/manage/ManagePageHeader.svelte";

  /** The first queued proposal gives the identify review concept real data to show. */
  let identifyHref = $state<string | null>(null);

  onMount(() => {
    void fetchIdentifyQueue().then(
      (queue) => {
        const item = queue.find((entry) => entry.state === IDENTIFY_QUEUE_STATE.proposal);
        identifyHref = item ? `/identify/${item.entityId}?layout=preview` : null;
      },
      () => undefined,
    );
  });

  const concepts = $derived([
    {
      href: identifyHref,
      title: "Identify review",
      icon: ScanSearch,
      features: ["Preview beside details", "Decision always in view", "Unchanged fields folded"],
    },
    {
      href: "/request?layout=preview",
      title: "Request",
      icon: Send,
      features: ["Family cards", "Kind and source bar", "Preview review"],
    },
  ]);
</script>

<svelte:head><title>Concepts · Prismedia</title></svelte:head>

<div class="mx-auto flex w-full max-w-5xl min-w-0 flex-col gap-6 pb-16">
  <ManagePageHeader icon={Sparkles} title="Concepts" />
  <ul class="grid gap-3 md:grid-cols-2">
    {#each concepts as concept (concept.title)}
      {@const Icon = concept.icon}
      <li>
        {#if concept.href}
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
        {:else}
          <div class="flex h-full flex-col gap-3 rounded-[var(--radius-md)] border border-dashed border-[var(--color-border-subtle)] p-4 opacity-60">
            <span class="flex items-center gap-2.5">
              <Icon class="size-4 text-text-muted" aria-hidden="true" />
              <span class="font-heading text-sm font-semibold text-text-secondary">{concept.title}</span>
            </span>
            <span class="text-caption text-text-muted">No proposal in the queue</span>
          </div>
        {/if}
      </li>
    {/each}
  </ul>
</div>
