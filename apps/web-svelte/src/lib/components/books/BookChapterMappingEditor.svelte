<script lang="ts">
  import { ArrowDownToLine, Check, FileAudio, Link2Off } from "@lucide/svelte";
  import { Button, Select, type SelectOption } from "@prismedia/ui-svelte";
  import type { BookAudioChapter, BookChapterAudioMapping } from "$lib/api/generated/model";
  import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";
  import {
    bookAudioChapterCandidates,
    sequentialBookChapterMappings,
    type BookAudioChapterCandidate,
    type ReadableBookChapter,
  } from "$lib/entities/book-chapter-list";
  import { BOOK_CHAPTER_MAPPING_ORIGIN } from "$lib/api/generated/codes";
  import { formatDuration } from "$lib/utils/format";

  interface Props {
    resetKey: string;
    readableChapters: readonly ReadableBookChapter[];
    audioTracks: readonly AudioTrackListItemDto[];
    audioChapters: readonly BookAudioChapter[];
    mappings: readonly BookChapterAudioMapping[];
    loadError?: string | null;
    onSave: (mappings: readonly BookChapterAudioMapping[]) => Promise<readonly BookChapterAudioMapping[]>;
  }

  let {
    resetKey,
    readableChapters,
    audioTracks,
    audioChapters,
    mappings,
    loadError = null,
    onSave,
  }: Props = $props();

  let draft = $state.raw<BookChapterAudioMapping[]>([]);
  let sourceSignature = $state("");
  let firstReadableChapterKey = $state("");
  let loadedResetKey = $state<string | null>(null);
  let saving = $state(false);
  let actionError = $state<string | null>(null);
  let saved = $state(false);

  const orderedReadable = $derived([...readableChapters].sort(
    (a, b) => a.order - b.order || a.title.localeCompare(b.title) || a.id.localeCompare(b.id),
  ));
  const orderedAudioChapters = $derived(bookAudioChapterCandidates(audioTracks, audioChapters));
  const readableOptions = $derived<SelectOption[]>(orderedReadable.map((chapter, index) => ({
    value: chapter.id,
    label: chapter.title,
    annotation: `Chapter ${index + 1}`,
  })));
  const mappingByAudioChapter = $derived(new Map(draft.map((mapping) => [mappingKey(mapping), mapping])));
  // Automatic matches are computed and persisted server-side; here they only annotate the
  // "no explicit mapping" option so the user can see what the matcher already chose.
  const automaticTitleByAudioChapter = $derived.by(() => {
    const titleByKey = new Map(readableChapters.map((chapter) => [chapter.id, chapter.title]));
    return new Map(mappings
      .filter((mapping) => mapping.origin === BOOK_CHAPTER_MAPPING_ORIGIN.auto)
      .flatMap((mapping) => {
        const title = titleByKey.get(mapping.readableChapterKey);
        return title ? [[mappingKey(mapping), title] as const] : [];
      }));
  });
  const draftSignature = $derived(mappingSignature(draft));
  const dirty = $derived(draftSignature !== sourceSignature);
  const mappedCount = $derived(draft.length);
  const displayedError = $derived(actionError ?? loadError);

  // Only manual rows are editable; automatic rows are server-owned and refill after every save.
  const manualMappings = $derived(
    mappings.filter((mapping) => mapping.origin !== BOOK_CHAPTER_MAPPING_ORIGIN.auto),
  );

  // The editor stays mounted while its parent route changes data. Reset only for a new Book or a
  // genuinely new persisted map; local draft changes remain untouched until save or clear.
  $effect(() => {
    const nextSignature = mappingSignature(manualMappings);
    if (loadedResetKey === resetKey && nextSignature === sourceSignature) return;
    loadedResetKey = resetKey;
    sourceSignature = nextSignature;
    draft = manualMappings.map((mapping) => ({ ...mapping }));
    firstReadableChapterKey = initialFirstChapterKey(
      readableChapters,
      audioTracks,
      audioChapters,
      manualMappings,
    );
    saved = false;
    actionError = null;
  });

  function mappingSignature(items: readonly BookChapterAudioMapping[]): string {
    return [...items]
      .sort((a, b) =>
        a.audioTrackId.localeCompare(b.audioTrackId)
          || (a.audioMarkerId ?? "").localeCompare(b.audioMarkerId ?? "")
          || a.readableChapterKey.localeCompare(b.readableChapterKey),
      )
      .map((mapping) => `${mappingKey(mapping)}:${mapping.readableChapterKey}`)
      .join("|");
  }

  function mappingKey(mapping: Pick<BookChapterAudioMapping, "audioTrackId" | "audioMarkerId">): string {
    return `${mapping.audioTrackId}:${mapping.audioMarkerId ?? "whole"}`;
  }

  function initialFirstChapterKey(
    chapters: readonly ReadableBookChapter[],
    tracks: readonly AudioTrackListItemDto[],
    availableAudioChapters: readonly BookAudioChapter[],
    existingMappings: readonly BookChapterAudioMapping[],
  ): string {
    const firstAudioChapter = bookAudioChapterCandidates(tracks, availableAudioChapters)[0];
    const mappedChapterKey = existingMappings.find((mapping) =>
      mappingKey(mapping) === firstAudioChapter?.key,
    )?.readableChapterKey;
    if (mappedChapterKey && chapters.some((chapter) => chapter.id === mappedChapterKey)) {
      return mappedChapterKey;
    }
    return [...chapters]
      .sort((a, b) => a.order - b.order || a.title.localeCompare(b.title) || a.id.localeCompare(b.id))[0]
      ?.id ?? "";
  }

  function selectionOptions(audioChapterKey: string): SelectOption[] {
    const automaticTitle = automaticTitleByAudioChapter.get(audioChapterKey);
    return [
      {
        value: "",
        label: automaticTitle ? `Automatic: ${automaticTitle}` : "No explicit mapping",
      },
      ...readableOptions,
    ];
  }

  function updateAudioChapterMapping(
    audioChapter: BookAudioChapterCandidate,
    readableChapterKey: string,
  ): void {
    saved = false;
    actionError = null;
    draft = draft.filter((mapping) =>
      mappingKey(mapping) !== audioChapter.key &&
      (!readableChapterKey || mapping.readableChapterKey !== readableChapterKey),
    );
    if (readableChapterKey) {
      draft = [...draft, {
        audioTrackId: audioChapter.track.id,
        readableChapterKey,
        ...(audioChapter.markerId ? { audioMarkerId: audioChapter.markerId } : {}),
      }];
    }
  }

  function markFirstChapter(): void {
    if (!firstReadableChapterKey) return;
    draft = sequentialBookChapterMappings(
      readableChapters,
      audioTracks,
      firstReadableChapterKey,
      audioChapters,
    );
    saved = false;
    actionError = null;
  }

  function clearOverrides(): void {
    draft = [];
    saved = false;
    actionError = null;
  }

  async function save(): Promise<void> {
    if (!dirty || saving) return;
    saving = true;
    actionError = null;
    saved = false;
    try {
      const persisted = await onSave(draft);
      const manual = persisted.filter((mapping) => mapping.origin !== BOOK_CHAPTER_MAPPING_ORIGIN.auto);
      draft = manual.map((mapping) => ({ ...mapping }));
      sourceSignature = mappingSignature(manual);
      saved = true;
    } catch (error) {
      actionError = error instanceof Error ? error.message : "Failed to save chapter mappings.";
    } finally {
      saving = false;
    }
  }
</script>

<section class="mapping-editor" aria-labelledby="chapter-mapping-heading">
  <div class="mapping-header">
    <div>
      <p class="eyebrow">Audiobook alignment</p>
      <h2 id="chapter-mapping-heading">Map audio chapters to readable chapters</h2>
      <p class="mapping-intro">
        Prismedia uses embedded M4B chapters when present and whole files otherwise. Choose where
        the first audio chapter begins, then adjust any association before saving.
      </p>
    </div>
    <div class="mapping-count" aria-label={`${mappedCount} explicit mappings`}>
      <strong>{mappedCount}</strong>
      <span>mapped</span>
    </div>
  </div>

  <div class="first-chapter-card">
    <div class="first-file">
      <span class="file-icon"><FileAudio class="h-5 w-5" /></span>
      <div>
        <span class="field-label">First audio chapter</span>
        <strong>{orderedAudioChapters[0]?.title ?? "No audio chapters"}</strong>
      </div>
    </div>
    <div class="first-chapter-control">
      <label for="first-readable-chapter">Starts at readable chapter</label>
      <Select
        value={firstReadableChapterKey}
        options={readableOptions}
        ariaLabel="Readable chapter for the first audio chapter"
        disabled={saving || orderedReadable.length === 0}
        onchange={(value) => (firstReadableChapterKey = value)}
      />
    </div>
    <Button
      variant="primary"
      size="lg"
      disabled={saving || !firstReadableChapterKey || orderedAudioChapters.length === 0}
      onclick={markFirstChapter}
    >
      <ArrowDownToLine class="h-4 w-4" />
      Mark first chapter
    </Button>
  </div>

  <div class="mapping-list" aria-label="Audiobook chapter overrides">
    {#each orderedAudioChapters as audioChapter, index (audioChapter.key)}
      <div class="mapping-row">
        <span class="track-number">{String(index + 1).padStart(2, "0")}</span>
        <div class="track-title">
          <strong>{audioChapter.title}</strong>
          <span>
            {formatDuration(audioChapter.startSeconds) ?? "0:00"}
            {#if audioChapter.endSeconds !== null}
              – {formatDuration(audioChapter.endSeconds) ?? "0:00"}
            {/if}
            · {mappingByAudioChapter.has(audioChapter.key) ? "Explicit mapping" : "Automatic title matching"}
          </span>
        </div>
        <Select
          value={mappingByAudioChapter.get(audioChapter.key)?.readableChapterKey ?? ""}
          options={selectionOptions(audioChapter.key)}
          ariaLabel={`Readable chapter for ${audioChapter.title}`}
          disabled={saving}
          onchange={(value) => updateAudioChapterMapping(audioChapter, value)}
        />
      </div>
    {/each}
  </div>

  <div class="mapping-footer">
    <div class="mapping-status" aria-live="polite">
      {#if displayedError}
        <span class="error" role="alert">{displayedError}</span>
      {:else if saved}
        <span class="success"><Check class="h-3.5 w-3.5" /> Chapter mapping saved</span>
      {:else if dirty}
        <span>Unsaved mapping changes</span>
      {:else}
        <span>Mappings are up to date</span>
      {/if}
    </div>
    <div class="mapping-actions">
      <Button variant="ghost" disabled={saving || draft.length === 0} onclick={clearOverrides}>
        <Link2Off class="h-4 w-4" />
        Clear overrides
      </Button>
      <Button variant="primary" disabled={saving || !dirty} onclick={() => void save()}>
        {saving ? "Saving…" : "Save mapping"}
      </Button>
    </div>
  </div>
</section>

<style>
  .mapping-editor {
    display: grid;
    gap: 1.25rem;
    min-width: 0;
  }

  .mapping-header,
  .mapping-footer,
  .first-chapter-card,
  .mapping-row {
    display: flex;
    align-items: center;
  }

  .mapping-header {
    justify-content: space-between;
    gap: 2rem;
  }

  .eyebrow,
  .field-label,
  .first-chapter-control label {
    font-family: var(--font-mono);
    font-size: 0.66rem;
    letter-spacing: 0.12em;
    text-transform: uppercase;
    color: var(--color-text-muted);
  }

  h2 {
    margin: 0.25rem 0 0;
    font-family: var(--font-heading);
    font-size: clamp(1.15rem, 2vw, 1.55rem);
    font-weight: 600;
    color: var(--color-text-primary);
  }

  .mapping-intro {
    max-width: 48rem;
    margin: 0.45rem 0 0;
    font-size: 0.86rem;
    line-height: 1.55;
    color: var(--color-text-secondary);
  }

  .mapping-count {
    display: grid;
    place-items: center;
    flex: 0 0 auto;
    min-width: 4.5rem;
    min-height: 4.5rem;
    border: 1px solid var(--color-border-default);
    border-radius: var(--radius-md);
    background: var(--color-surface-1);
  }

  .mapping-count strong {
    font-family: var(--font-mono);
    font-size: 1.15rem;
    color: var(--color-text-primary);
  }

  .mapping-count span {
    margin-top: -0.65rem;
    font-size: 0.67rem;
    color: var(--color-text-muted);
  }

  .first-chapter-card {
    display: grid;
    grid-template-columns: minmax(12rem, 1fr) minmax(16rem, 1.25fr) auto;
    gap: 1rem;
    padding: 1rem;
    border: 1px solid var(--color-border-accent);
    border-radius: var(--radius-lg);
    background:
      linear-gradient(110deg, color-mix(in srgb, var(--color-accent-500) 8%, transparent), transparent 52%),
      var(--color-surface-1);
  }

  .first-file {
    display: flex;
    align-items: center;
    gap: 0.8rem;
    min-width: 0;
  }

  .file-icon {
    display: grid;
    place-items: center;
    flex: 0 0 auto;
    width: 2.5rem;
    height: 2.5rem;
    border: 1px solid var(--color-border-default);
    border-radius: var(--radius-sm);
    color: var(--color-text-secondary);
    background: var(--color-surface-2);
  }

  .first-file div,
  .track-title {
    display: grid;
    min-width: 0;
  }

  .first-file strong,
  .track-title strong {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    color: var(--color-text-primary);
  }

  .first-chapter-control {
    display: grid;
    gap: 0.4rem;
    min-width: 0;
  }

  .mapping-list {
    overflow: hidden;
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-md);
    background: var(--color-surface-1);
  }

  .mapping-row {
    display: grid;
    grid-template-columns: 2.5rem minmax(12rem, 1fr) minmax(15rem, 0.8fr);
    gap: 0.9rem;
    min-height: 4.25rem;
    padding: 0.65rem 0.85rem;
    border-bottom: 1px solid var(--color-border-subtle);
  }

  .mapping-row:last-child {
    border-bottom: 0;
  }

  .track-number {
    font-family: var(--font-mono);
    font-size: 0.72rem;
    color: var(--color-text-disabled);
  }

  .track-title span {
    margin-top: 0.2rem;
    font-size: 0.7rem;
    color: var(--color-text-muted);
  }

  .mapping-footer {
    justify-content: space-between;
    gap: 1rem;
  }

  .mapping-status {
    min-width: 0;
    font-size: 0.75rem;
    color: var(--color-text-muted);
  }

  .mapping-status .success {
    display: inline-flex;
    align-items: center;
    gap: 0.35rem;
    color: var(--color-success-text);
  }

  .mapping-status .error {
    color: var(--color-error-text);
  }

  .mapping-actions {
    display: flex;
    justify-content: flex-end;
    gap: 0.5rem;
  }

  @media (max-width: 800px) {
    .mapping-header {
      align-items: flex-start;
    }

    .mapping-count {
      min-width: 3.75rem;
      min-height: 3.75rem;
    }

    .first-chapter-card {
      grid-template-columns: 1fr;
    }

    .mapping-row {
      grid-template-columns: 2rem minmax(0, 1fr);
    }

    .mapping-row :global(.relative) {
      grid-column: 1 / -1;
    }

    .mapping-footer {
      align-items: stretch;
      flex-direction: column;
    }

    .mapping-actions {
      justify-content: stretch;
    }

    .mapping-actions :global(button) {
      flex: 1;
    }
  }
</style>
