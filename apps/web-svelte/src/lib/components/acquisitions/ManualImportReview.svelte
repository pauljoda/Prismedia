<script lang="ts">
  import { Ban, Check, FileQuestion, FileText, Files, ShieldAlert } from "@lucide/svelte";
  import { Alert, Button, Disclosure, Select, Skeleton, type SelectOption } from "@prismedia/ui-svelte";
  import type { AcquisitionManualImportReview } from "$lib/api/generated/model";
  import { formatBytes } from "$lib/utils/format";

  let {
    review,
    statusMessage,
    assignments,
    busy = false,
    onAssignmentChange,
    onImport,
    onReject,
  }: {
    review: AcquisitionManualImportReview | null;
    /** Preserve the acquisition's explanation when the review has no separate warning. */
    statusMessage?: string | null;
    assignments: Record<string, string>;
    busy?: boolean;
    onAssignmentChange: (targetEntityId: string, sourceRelativePath: string) => void;
    onImport: () => void;
    onReject: () => void;
  } = $props();

  const mappedEpisodeCount = $derived(Object.values(assignments).filter(Boolean).length);
  const warning = $derived(review?.warning || statusMessage);
  const unsafe = $derived(Boolean(review?.warning || review?.files.some(file => file.isDangerous)));
  const mappedSourcePaths = $derived(new Set(Object.values(assignments).filter(Boolean)));
  const sourceOptions = $derived<SelectOption[]>([
    { value: "", label: "No file selected" },
    ...(review?.files ?? [])
      .filter((file) => file.canMap && !file.isDangerous)
      .map((file) => ({
        value: file.sourceRelativePath,
        label: file.sourceRelativePath,
        annotation: mappedSourcePaths.has(file.sourceRelativePath) ? "Mapped" : undefined,
      })),
  ]);

  function targetLabel(position: string | number | null | undefined, title: string): string {
    const prefix = position != null ? `Episode ${String(position).padStart(2, "0")} · ` : "";
    return `${prefix}${title}`;
  }

  function fileKindLabel(file: AcquisitionManualImportReview["files"][number]): string {
    if (file.isDangerous) return "Blocked, potentially dangerous";
    return file.canMap ? "Available for mapping" : "Other downloaded file";
  }
</script>

<section
  class="flex min-w-0 flex-col gap-4"
  aria-labelledby={review?.available ? "manual-import-heading" : undefined}
  aria-label={review?.available ? undefined : "Downloaded file review"}
>
  {#if review?.available}
    <div class="flex flex-col gap-1.5">
      <h2 id="manual-import-heading" class="font-heading text-base font-semibold text-foreground">
        Map expected episodes
      </h2>
      {#if review.message && review.message !== warning}
        <p class="max-w-prose text-sm leading-relaxed text-muted-foreground">{review.message}</p>
      {/if}
    </div>
  {:else if review?.message && !warning}
    <p class="max-w-prose text-sm leading-relaxed text-muted-foreground">{review.message}</p>
  {/if}

  {#if warning}
    <Alert.Root class="border-warning/30 bg-warning-muted p-4 text-warning-text">
      <ShieldAlert />
      <Alert.Title>{unsafe ? "Unsafe file blocked" : "Import needs attention"}</Alert.Title>
      <Alert.Description class="text-warning-text [overflow-wrap:anywhere]">{warning}</Alert.Description>
    </Alert.Root>
  {/if}

  {#if !review}
    <Skeleton class="h-20 w-full" aria-label="Loading downloaded files" />
  {/if}

  {#if review?.available}
    <div class="overflow-visible rounded-sm border border-border-subtle bg-surface-1">
      <div class="hidden grid-cols-[minmax(0,1fr)_minmax(16rem,0.72fr)] gap-4 rounded-t-sm border-b border-border-subtle bg-surface-2 px-4 py-3 text-sm font-medium text-text-secondary md:grid">
        <span>Expected episode</span>
        <span>Downloaded file</span>
      </div>
      {#each review.targets as target (target.entityId)}
        <div class="grid min-w-0 gap-3 border-b border-border-subtle px-3 py-3 last:border-b-0 md:grid-cols-[minmax(0,1fr)_minmax(16rem,0.72fr)] md:items-center md:gap-4">
          <div class="flex min-w-0 items-start gap-2.5">
            <FileQuestion class="mt-0.5 h-4 w-4 shrink-0 text-text-muted" />
            <div class="min-w-0">
              <p class="whitespace-normal text-sm font-medium text-text-primary [overflow-wrap:anywhere]">
                {targetLabel(target.position, target.title)}
              </p>
            </div>
          </div>
          <div class="min-w-0">
            <Select
              size="sm"
              value={assignments[target.entityId] ?? ""}
              options={sourceOptions}
              ariaLabel={`Downloaded file for ${targetLabel(target.position, target.title)}`}
              onchange={(sourceRelativePath) => onAssignmentChange(target.entityId, sourceRelativePath)}
            />
          </div>
        </div>
      {/each}
    </div>
  {/if}

  {#if review}
    <div class="flex flex-col gap-3 rounded-md border border-border bg-muted/30 p-4 sm:flex-row sm:items-center sm:justify-between">
      <div class="min-w-0">
        <p class="text-sm font-medium text-foreground">
          {#if review.available}
            {mappedEpisodeCount} of {review.targets.length} {review.targets.length === 1 ? "episode" : "episodes"} mapped
          {:else}
            Choose a safer release
          {/if}
        </p>
        <p class="mt-1 text-xs leading-relaxed text-muted-foreground">
          {review.available
            ? "A downloaded file can satisfy more than one episode."
            : "Prismedia will remove this download, block the exact release, and search again."}
        </p>
      </div>
      <div class="flex flex-wrap items-center gap-2">
        <Button type="button" variant="danger" class="gap-1.5" disabled={busy} onclick={onReject}>
          <Ban data-icon="inline-start" />
          Reject and search again
        </Button>
        {#if review.available}
          <Button
            type="button"
            variant="primary"
            class="gap-1.5"
            disabled={busy || mappedEpisodeCount === 0}
            onclick={onImport}
          >
            <Check data-icon="inline-start" />
            Import mapped episodes
          </Button>
        {/if}
      </div>
    </div>

    <Disclosure title="Downloaded files" icon={Files} count={review.files.length}>
      <div class="min-w-0">
        {#each review.files as file (file.sourceRelativePath)}
          <div class="flex min-w-0 items-start justify-between gap-3 border-b border-border-subtle px-3 py-2.5 last:border-b-0">
            <span class="flex min-w-0 items-start gap-2.5">
              {#if file.isDangerous}
                <ShieldAlert class="mt-0.5 h-3.5 w-3.5 shrink-0 text-warning-text" />
              {:else}
                <FileText class="mt-0.5 h-3.5 w-3.5 shrink-0 text-text-muted" />
              {/if}
              <span class="min-w-0">
                <span class={file.isDangerous
                  ? "block whitespace-normal text-sm text-warning-text [overflow-wrap:anywhere]"
                  : "block whitespace-normal text-sm text-text-primary [overflow-wrap:anywhere]"}>{file.sourceRelativePath}</span>
                <span class={file.isDangerous
                  ? "mt-0.5 block text-[0.68rem] text-warning-text"
                  : "mt-0.5 block text-[0.68rem] text-text-muted"}>{fileKindLabel(file)}</span>
              </span>
            </span>
            <span class="shrink-0 font-mono text-[0.68rem] text-text-muted">{formatBytes(Number(file.sizeBytes))}</span>
          </div>
        {/each}
      </div>
    </Disclosure>
  {/if}
</section>
