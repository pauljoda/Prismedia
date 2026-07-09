<script lang="ts">
  import { ChevronRight, Eye, Loader2, Star } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import type { EntitySearchCandidate } from "$lib/api/identify-types";
  import { identifyCandidateKey } from "$lib/components/identify/identify-candidate-card";
  import { entityKindIcon } from "$lib/entities/entity-kind-icons";
  import { aspectRatioForKind, toAspectRatioValue } from "$lib/entities/entity-thumbnail";

  interface Props {
    candidates: EntitySearchCandidate[];
    entityKind: string;
    onActivate: (candidate: EntitySearchCandidate, candidateKey: string) => void;
    onPreview?: (candidate: EntitySearchCandidate, candidateKey: string) => void;
    activeCandidateKey?: string | null;
    disabled?: boolean;
    markFirstAsBest?: boolean;
  }

  let {
    candidates,
    entityKind,
    onActivate,
    onPreview,
    activeCandidateKey = null,
    disabled = false,
    markFirstAsBest = true,
  }: Props = $props();

  const candidateAspect = $derived(toAspectRatioValue(aspectRatioForKind(entityKind)));
  const CandidateKindIcon = $derived(entityKindIcon(entityKind));

  function candidateTitle(candidate: EntitySearchCandidate): string {
    return candidate.title?.trim() || "Untitled match";
  }

  function candidateActionLabel(candidate: EntitySearchCandidate): string {
    return `Use ${candidateTitle(candidate)}${candidate.year ? ` (${candidate.year})` : ""}`;
  }

  function handleKeydown(
    event: KeyboardEvent,
    candidate: EntitySearchCandidate,
    candidateKey: string,
  ) {
    if (event.key !== "Enter" && event.key !== " ") return;
    event.preventDefault();
    if (!disabled) onActivate(candidate, candidateKey);
  }

  function preview(
    event: MouseEvent,
    candidate: EntitySearchCandidate,
    candidateKey: string,
  ) {
    event.preventDefault();
    event.stopPropagation();
    if (!disabled && candidate.posterUrl) onPreview?.(candidate, candidateKey);
  }
</script>

<div class="flex flex-col gap-2.5">
  {#each candidates as candidate, index (identifyCandidateKey(candidate, index))}
    {@const candidateKey = identifyCandidateKey(candidate, index)}
    {@const title = candidateTitle(candidate)}
    {@const hasCover = Boolean(candidate.posterUrl)}
    {@const active = activeCandidateKey === candidateKey}
    <div
      class={cn(
        "plugin-candidate-card relative grid items-center gap-3 rounded-sm border border-border-subtle bg-surface-1 p-2.5 text-left shadow-well transition-all hover:border-border-accent hover:bg-surface-2 hover:shadow-[0_0_20px_rgba(242,194,106,0.08)] focus-visible:border-border-accent focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-accent-500/60",
        "grid-cols-[3.5rem_minmax(0,1fr)_auto] sm:grid-cols-[4rem_minmax(0,1fr)_auto]",
        disabled ? "cursor-not-allowed opacity-60" : "cursor-pointer",
      )}
      role="button"
      tabindex={disabled ? -1 : 0}
      aria-label={candidateActionLabel(candidate)}
      aria-disabled={disabled}
      onclick={() => !disabled && onActivate(candidate, candidateKey)}
      onkeydown={(event) => handleKeydown(event, candidate, candidateKey)}
    >
      <div class="min-w-0">
        <div
          class="relative w-full overflow-hidden rounded-xs border border-border-subtle bg-surface-3"
          style={`aspect-ratio: ${candidateAspect};`}
        >
          <div class="grid h-full w-full place-items-center">
            <CandidateKindIcon class="h-6 w-6 text-text-disabled" />
          </div>
          {#if hasCover}
            <img
              src={candidate.posterUrl}
              alt=""
              loading="lazy"
              decoding="async"
              referrerpolicy="no-referrer"
              class="absolute inset-0 h-full w-full object-cover"
              onerror={(event) => ((event.currentTarget as HTMLImageElement).style.display = "none")}
            />
          {/if}
        </div>
        {#if hasCover && onPreview}
          <button
            type="button"
            class="mt-1.5 inline-flex h-7 w-full items-center justify-center rounded-xs border border-border-default bg-surface-2 text-text-muted transition-colors hover:border-border-accent hover:bg-surface-3 hover:text-text-accent focus-visible:border-border-accent focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-accent-500/60 disabled:cursor-not-allowed disabled:opacity-40"
            disabled={disabled}
            aria-label={`Preview ${title} artwork`}
            title="Preview artwork"
            onclick={(event) => preview(event, candidate, candidateKey)}
          >
            <Eye class="h-3.5 w-3.5" />
          </button>
        {/if}
      </div>

      <div class="flex min-w-0 flex-col justify-center gap-1.5 py-1">
        <div class="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
          <span class="min-w-0 break-words font-heading text-[0.88rem] font-semibold text-text-primary">
            {title}
          </span>
          {#if candidate.year}
            <span class="font-mono text-[0.7rem] text-text-muted">{candidate.year}</span>
          {/if}
          {#if markFirstAsBest && index === 0}
            <span class="inline-flex shrink-0 items-center gap-1 rounded-xs border border-border-accent bg-accent-950/60 px-1.5 py-0.5 font-mono text-[0.6rem] text-text-accent">
              <Star class="h-2.5 w-2.5" />
              Best
            </span>
          {/if}
        </div>
        {#if candidate.overview}
          <p class="text-[0.8rem] leading-relaxed text-text-secondary">{candidate.overview}</p>
        {:else}
          <p class="text-[0.78rem] leading-relaxed text-text-disabled">No provider description available.</p>
        {/if}
      </div>

      <div class="flex items-center self-stretch pl-1 text-text-accent">
        {#if active}
          <Loader2 class="h-4 w-4 animate-spin" />
        {:else}
          <ChevronRight class="h-4 w-4" />
        {/if}
      </div>
    </div>
  {/each}
</div>
