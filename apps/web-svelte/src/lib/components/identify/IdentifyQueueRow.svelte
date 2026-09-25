<script lang="ts">
  import { ChevronRight, CornerDownRight, Loader2 } from "@lucide/svelte";
  import { Button, Checkbox, cn } from "@prismedia/ui-svelte";
  import { IDENTIFY_QUEUE_STATE } from "$lib/api/generated/codes";
  import type { PluginProvider } from "$lib/api/identify-types";
  import PluginIcon from "$lib/components/plugins/PluginIcon.svelte";
  import type { IdentifyQueueItem } from "$lib/components/identify/identify-store.svelte";
  import { labelForEntityKind } from "$lib/entities/entity-codes";
  import { bandGradient, mediaFamilyForKind } from "$lib/entities/media-families";

  interface Props {
    item: IdentifyQueueItem;
    provider: PluginProvider | null;
    selected: boolean;
    onToggle: () => void;
    onReview: () => void;
  }

  let { item, provider, selected, onToggle, onReview }: Props = $props();

  const STATE_LABEL: Record<string, string> = {
    [IDENTIFY_QUEUE_STATE.proposal]: "Review",
    [IDENTIFY_QUEUE_STATE.search]: "Choose",
    [IDENTIFY_QUEUE_STATE.queued]: "Queued",
    [IDENTIFY_QUEUE_STATE.searching]: "Searching",
    [IDENTIFY_QUEUE_STATE.applying]: "Applying",
    [IDENTIFY_QUEUE_STATE.done]: "Done",
    [IDENTIFY_QUEUE_STATE.deleted]: "Deleted",
    [IDENTIFY_QUEUE_STATE.error]: "Error",
  };

  const family = $derived(mediaFamilyForKind(item.entityKind));
  const working = $derived(
    item.state === IDENTIFY_QUEUE_STATE.queued ||
      item.state === IDENTIFY_QUEUE_STATE.searching ||
      item.state === IDENTIFY_QUEUE_STATE.applying,
  );
  const confidence = $derived(
    typeof item.proposal?.confidence === "number" ? Math.round(item.proposal.confidence * 100) : null,
  );
  const proposedTitle = $derived.by(() => {
    const proposed = item.proposal?.patch?.title?.trim();
    if (!proposed) return null;
    return proposed.localeCompare(item.title.trim(), undefined, { sensitivity: "accent" }) === 0 ? null : proposed;
  });
  const cover = $derived(item.entity.coverThumbUrl ?? item.entity.coverUrl);
  const actionLabel = $derived(
    item.state === IDENTIFY_QUEUE_STATE.proposal
      ? "Review"
      : item.state === IDENTIFY_QUEUE_STATE.done
        ? "View"
        : item.state === IDENTIFY_QUEUE_STATE.error
          ? "Retry"
          : "Identify",
  );
</script>

<li
  class={cn(
    "grid min-w-0 grid-cols-[auto_auto_minmax(0,1fr)_auto] items-center gap-3 px-3 py-2.5 sm:px-4",
    "lg:grid-cols-[auto_auto_minmax(0,2fr)_minmax(0,9rem)_minmax(0,10rem)_7rem_auto]",
    selected && "bg-[var(--color-surface-3)]/60",
  )}
>
  <Checkbox size="sm" checked={selected} aria-label="Select {item.title}" onchange={onToggle} />

  <span class="relative block h-12 w-9 shrink-0 overflow-hidden rounded-[var(--radius-xs)] bg-[var(--color-surface-3)]">
    {#if cover}
      <img src={cover} alt="" loading="lazy" decoding="async" class="size-full object-cover" />
    {/if}
    <span class="absolute inset-x-0 bottom-0 h-[3px]" style:background={bandGradient(family.accent)} aria-hidden="true"></span>
  </span>

  <div class="min-w-0">
    <p class="flex items-center gap-2 font-mono text-[0.62rem] uppercase tracking-[0.12em]">
      <span
        class={cn(
          item.state === IDENTIFY_QUEUE_STATE.proposal && "text-text-primary",
          item.state === IDENTIFY_QUEUE_STATE.search && "text-warning-text",
          item.state === IDENTIFY_QUEUE_STATE.error && "text-error-text",
          working && "text-text-muted",
          item.state === IDENTIFY_QUEUE_STATE.done && "text-text-disabled",
        )}
      >
        {#if working}<Loader2 class="mr-1 inline size-3 animate-spin motion-reduce:animate-none" aria-hidden="true" />{/if}
        {STATE_LABEL[item.state] ?? item.state}
      </span>
      <span class="normal-case tracking-normal text-text-disabled lg:hidden">{labelForEntityKind(item.entityKind)}</span>
    </p>
    <p class="truncate text-label text-text-secondary" title={item.title}>{item.title}</p>
    {#if proposedTitle}
      <p class="flex min-w-0 items-center gap-1 text-text-primary" title={proposedTitle}>
        <CornerDownRight class="size-3 shrink-0 text-text-muted" aria-hidden="true" />
        <span class="truncate font-heading text-sm font-semibold">{proposedTitle}</span>
      </p>
    {/if}
    {#if item.state === IDENTIFY_QUEUE_STATE.error && item.errorMessage}
      <p class="truncate text-caption text-error-text">{item.errorMessage}</p>
    {/if}
  </div>

  <span class="hidden min-w-0 items-center gap-2 lg:flex">
    <span class="h-1.5 w-5 shrink-0 rounded-[2px]" style:background={bandGradient(family.accent)} aria-hidden="true"></span>
    <span class="truncate text-caption text-text-secondary">{labelForEntityKind(item.entityKind)}</span>
  </span>

  <span class="hidden min-w-0 items-center gap-2 lg:flex">
    {#if provider}
      <PluginIcon name={provider.name} iconUrl={provider.iconUrl} class="size-6" />
      <span class="truncate text-caption text-text-secondary">{provider.name}</span>
    {:else}
      <span class="font-mono text-caption text-text-disabled">—</span>
    {/if}
  </span>

  <span class="hidden items-center gap-2 lg:flex" title={confidence === null ? undefined : `${confidence}% match`}>
    {#if confidence !== null}
      <span class="h-1 flex-1 overflow-hidden rounded-[2px] bg-[var(--color-surface-3)]">
        <span class="block h-full bg-[var(--color-text-secondary)]" style:width="{confidence}%"></span>
      </span>
      <span class="w-9 text-right font-mono text-[0.68rem] tabular-nums text-text-secondary">{confidence}%</span>
    {:else}
      <span class="font-mono text-caption text-text-disabled">—</span>
    {/if}
  </span>

  <Button
    variant={item.state === IDENTIFY_QUEUE_STATE.proposal ? "secondary" : "ghost"}
    size="sm"
    onclick={onReview}
  >
    {actionLabel}
    <ChevronRight aria-hidden="true" />
  </Button>
</li>
