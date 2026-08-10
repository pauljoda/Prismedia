<script lang="ts">
  import { Check, FileQuestion, FileText } from "@lucide/svelte";
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
  }: {
    review: AcquisitionManualImportReview | null;
    assignments: Record<string, string>;
    busy?: boolean;
    onAssignmentChange: (sourceRelativePath: string, targetEntityId: string) => void;
    onImport: () => void;
  } = $props();

  const mappedFileCount = $derived(Object.values(assignments).filter(Boolean).length);
  function targetOptionsFor(sourceRelativePath: string): SelectOption[] {
    const assignedElsewhere = new Set(Object.entries(assignments)
      .filter(([source, target]) => source !== sourceRelativePath && Boolean(target))
      .map(([, target]) => target));
    return [
      { value: "", label: "Do not import" },
      ...(review?.targets ?? []).map((target) => ({
        value: target.entityId,
        label: `${target.position != null ? `Episode ${String(target.position).padStart(2, "0")} · ` : ""}${target.title}`,
        disabled: assignedElsewhere.has(target.entityId),
      })),
    ];
  }
</script>

<section class="space-y-3" aria-labelledby="manual-import-heading">
  <div class="space-y-1">
    <h2 id="manual-import-heading" class="text-kicker text-text-primary">Review downloaded files</h2>
    <p class="max-w-3xl text-sm text-text-muted">
      {review?.message ?? "Choose what each downloaded file should become before importing."}
    </p>
  </div>

  {#if review && review.files.length > 0}
    <div class="overflow-hidden rounded-sm border border-border-subtle bg-surface-1">
      <div class="hidden grid-cols-[minmax(0,1fr)_minmax(16rem,0.72fr)] gap-4 border-b border-border-subtle bg-surface-2 px-3 py-2 text-label text-text-muted md:grid">
        <span>Downloaded file</span>
        <span>Maps to</span>
      </div>
      {#each review.files as file (file.sourceRelativePath)}
        <div class="grid min-w-0 gap-3 border-b border-border-subtle px-3 py-3 last:border-b-0 md:grid-cols-[minmax(0,1fr)_minmax(16rem,0.72fr)] md:items-center md:gap-4">
          <div class="flex min-w-0 items-start gap-2.5">
            <FileText class="mt-0.5 h-4 w-4 shrink-0 text-text-muted" />
            <div class="min-w-0">
              <p class="whitespace-normal text-sm text-text-primary [overflow-wrap:anywhere]">{file.sourceRelativePath}</p>
              <p class="mt-0.5 font-mono text-[0.68rem] text-text-muted">{formatBytes(Number(file.sizeBytes))}</p>
            </div>
          </div>
          <div class="min-w-0">
            {#if file.canMap}
              <Select
                size="sm"
                value={assignments[file.sourceRelativePath] ?? ""}
                options={targetOptionsFor(file.sourceRelativePath)}
                ariaLabel={`Entity mapping for ${file.name}`}
                onchange={(targetId) => onAssignmentChange(file.sourceRelativePath, targetId)}
              />
            {:else}
              <div class="flex h-8 items-center gap-2 rounded-xs border border-border-subtle bg-surface-2 px-2.5 text-xs text-text-muted">
                <FileQuestion class="h-3.5 w-3.5" />
                Not importable
              </div>
            {/if}
          </div>
        </div>
      {/each}
    </div>

    {#if review.available}
      <div class="flex flex-wrap items-center justify-between gap-3 rounded-sm border border-border-subtle bg-surface-1 px-3 py-2.5">
        <p class="text-sm text-text-muted">
          <span class="font-medium text-text-primary">{mappedFileCount}</span>
          {mappedFileCount === 1 ? "file" : "files"} selected · unassigned files stay out of the library
        </p>
        <Button
          type="button"
          variant="primary"
          class="gap-1.5"
          disabled={busy || mappedFileCount === 0}
          onclick={onImport}
        >
          <Check class="h-3.5 w-3.5" />
          Import mapped files
        </Button>
      </div>
    {/if}
  {/if}

  {#if review && !review.available}
    <StatePlaceholder
      icon={FileQuestion}
      title="File mapping unavailable"
      description={review?.message ?? "This held import cannot be mapped from the current payload."}
    />
  {/if}
</section>
