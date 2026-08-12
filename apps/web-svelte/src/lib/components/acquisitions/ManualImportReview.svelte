<script lang="ts">
  import { Ban, Check, FileQuestion, FileText, Files, ShieldAlert } from "@lucide/svelte";
  import { Button, Select, type SelectOption } from "@prismedia/ui-svelte";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import type { AcquisitionManualImportReview } from "$lib/api/generated/model";
  import { formatBytes } from "$lib/utils/format";

  let {
    review,
    assignments,
    busy = false,
    onAssignmentChange,
    onImport,
    onReject,
  }: {
    review: AcquisitionManualImportReview | null;
    assignments: Record<string, string>;
    busy?: boolean;
    onAssignmentChange: (targetEntityId: string, sourceRelativePath: string) => void;
    onImport: () => void;
    onReject: () => void;
  } = $props();

  const mappedEpisodeCount = $derived(Object.values(assignments).filter(Boolean).length);
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
    if (file.isDangerous) return "Blocked — potentially dangerous";
    return file.canMap ? "Available for mapping" : "Other downloaded file";
  }
</script>

<section class="space-y-3" aria-labelledby="manual-import-heading">
  <div class="space-y-1">
    <h2 id="manual-import-heading" class="text-kicker text-text-primary">Map expected episodes</h2>
    <p class="max-w-3xl text-sm text-text-muted">
      {review?.message ?? "Choose which downloaded file contains each expected episode."}
    </p>
  </div>

  {#if review?.warning}
    <div role="alert" class="flex items-start gap-3 rounded-sm border border-warning/30 bg-warning-muted px-3 py-2.5 text-warning-text">
      <ShieldAlert class="mt-0.5 h-4 w-4 shrink-0" />
      <div class="min-w-0">
        <p class="text-sm font-medium">Potentially unsafe download</p>
        <p class="mt-0.5 whitespace-normal text-sm [overflow-wrap:anywhere]">{review.warning}</p>
      </div>
    </div>
  {/if}

  {#if review?.available}
    <div class="overflow-visible rounded-sm border border-border-subtle bg-surface-1">
      <div class="hidden grid-cols-[minmax(0,1fr)_minmax(16rem,0.72fr)] gap-4 rounded-t-sm border-b border-border-subtle bg-surface-2 px-3 py-2 text-label text-text-muted md:grid">
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
              <p class="mt-0.5 text-[0.72rem] text-text-muted">Select the file that contains this episode.</p>
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

  {#if review && !review.available}
    <StatePlaceholder
      icon={FileQuestion}
      title="File mapping unavailable"
      description={review?.message ?? "This held import cannot be mapped from the current payload."}
    />
  {/if}

  {#if review}
    <div class="flex flex-wrap items-center justify-between gap-3 rounded-sm border border-border-subtle bg-surface-1 px-3 py-2.5">
      <p class="text-sm text-text-muted">
        {#if review.available}
          <span class="font-medium text-text-primary">{mappedEpisodeCount}</span>
          {mappedEpisodeCount === 1 ? "episode" : "episodes"} mapped · one file may satisfy several episodes
        {:else}
          Reject this payload to blocklist the release and search for another.
        {/if}
      </p>
      <div class="flex flex-wrap items-center gap-2">
        <Button type="button" variant="danger" class="gap-1.5" disabled={busy} onclick={onReject}>
          <Ban class="h-3.5 w-3.5" />
          Reject
        </Button>
        {#if review.available}
          <Button
            type="button"
            variant="primary"
            class="gap-1.5"
            disabled={busy || mappedEpisodeCount === 0}
            onclick={onImport}
          >
            <Check class="h-3.5 w-3.5" />
            Import mapped episodes
          </Button>
        {/if}
      </div>
    </div>

    <details class="group min-w-0 overflow-hidden rounded-sm border border-border-subtle bg-surface-1">
      <summary class="flex min-w-0 cursor-pointer items-center gap-2 px-3 py-2 text-kicker text-text-primary select-none">
        <Files class="h-3.5 w-3.5 text-text-muted" />
        Downloaded files
        <span class="font-mono text-[0.68rem] font-normal text-text-muted">{review.files.length}</span>
      </summary>
      <div class="min-w-0 border-t border-border-subtle">
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
    </details>
  {/if}
</section>
