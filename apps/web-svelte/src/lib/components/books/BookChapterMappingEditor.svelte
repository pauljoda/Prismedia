<script lang="ts">
  import { ArrowDownToLine, Check, FileAudio, Link2Off } from "@lucide/svelte";
  import { Badge, Button, Select, type SelectOption } from "@prismedia/ui-svelte";
  import type {
    AudioChapterWindow,
    BookAlignmentResponse,
    BookChapterAudioMapping,
  } from "$lib/api/generated/model";
  import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";
  import {
    alignmentAudioWindows,
    alignmentChapterMappings,
    alignmentReadableWindows,
    audioWindowKey,
    bookSeparateProgress,
    sequentialBookChapterMappings,
  } from "$lib/entities/book-chapter-list";
  import { BOOK_CHAPTER_MAPPING_ORIGIN } from "$lib/api/generated/codes";
  import { formatDuration } from "$lib/utils/format";

  interface Props {
    resetKey: string;
    /** Server alignment: rows in display order, provenance, and coverage. */
    alignment: BookAlignmentResponse | null;
    audioTracks: readonly AudioTrackListItemDto[];
    loadError?: string | null;
    onSave: (mappings: readonly BookChapterAudioMapping[]) => Promise<BookAlignmentResponse>;
  }

  let {
    resetKey,
    alignment,
    audioTracks,
    loadError = null,
    onSave,
  }: Props = $props();

  let draft = $state.raw<BookChapterAudioMapping[]>([]);
  // An in-order fill waiting for review: nothing enters the draft until every pair has been seen.
  let proposedFill = $state.raw<BookChapterAudioMapping[] | null>(null);
  let sourceSignature = $state("");
  let firstReadableChapterKey = $state("");
  let loadedResetKey = $state<string | null>(null);
  let saving = $state(false);
  let actionError = $state<string | null>(null);
  let saved = $state(false);

  const rows = $derived(alignment?.rows ?? []);
  const coverage = $derived(alignment?.coverage ?? null);
  const separateReason = $derived(bookSeparateProgress(alignment)?.reason ?? null);
  const readableWindows = $derived(alignmentReadableWindows(alignment));
  const audioWindows = $derived(alignmentAudioWindows(alignment, audioTracks));
  const audioNumberByKey = $derived(new Map(audioWindows.map((entry, index) => [entry.key, index + 1])));
  const readableOptions = $derived<SelectOption[]>(readableWindows.map((chapter, index) => ({
    value: chapter.chapterKey,
    label: chapter.title,
    annotation: `Chapter ${index + 1}`,
  })));
  const readableTitleByKey = $derived(new Map(readableWindows.map((chapter) => [chapter.chapterKey, chapter.title])));
  const audioTitleByKey = $derived(new Map(audioWindows.map((entry) => [entry.key, entry.window.title])));
  const persistedMappings = $derived(alignmentChapterMappings(alignment));
  // Only confirmed rows (hand-picked or filled in order) are editable; automatic rows are
  // server-owned and refill after every save.
  const manualMappings = $derived(
    persistedMappings.filter((mapping) => mapping.origin !== BOOK_CHAPTER_MAPPING_ORIGIN.auto),
  );
  // Automatic matches only annotate the "no explicit mapping" option so the user can see what the
  // server's matcher already chose.
  const automaticTitleByAudioKey = $derived(new Map(persistedMappings
    .filter((mapping) => mapping.origin === BOOK_CHAPTER_MAPPING_ORIGIN.auto)
    .flatMap((mapping) => {
      const title = readableTitleByKey.get(mapping.readableChapterKey);
      return title ? [[mappingKey(mapping), title] as const] : [];
    })));
  const mappingByAudioKey = $derived(new Map(draft.map((mapping) => [mappingKey(mapping), mapping])));
  const draftSignature = $derived(mappingSignature(draft));
  const dirty = $derived(draftSignature !== sourceSignature);
  const displayedError = $derived(actionError ?? loadError);

  // The editor stays mounted while its parent route changes data. Reset only for a new Book or a
  // genuinely new persisted map; local draft changes remain untouched until save or clear.
  $effect(() => {
    const nextSignature = mappingSignature(manualMappings);
    if (loadedResetKey === resetKey && nextSignature === sourceSignature) return;
    loadedResetKey = resetKey;
    sourceSignature = nextSignature;
    draft = manualMappings.map((mapping) => ({ ...mapping }));
    proposedFill = null;
    firstReadableChapterKey = initialFirstChapterKey();
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
      .map((mapping) => `${mappingKey(mapping)}:${mapping.readableChapterKey}:${mapping.origin ?? BOOK_CHAPTER_MAPPING_ORIGIN.manual}`)
      .join("|");
  }

  function mappingKey(mapping: Pick<BookChapterAudioMapping, "audioTrackId" | "audioMarkerId">): string {
    return audioWindowKey(mapping.audioTrackId, mapping.audioMarkerId);
  }

  function windowKey(window: AudioChapterWindow): string {
    return audioWindowKey(window.trackEntityId, window.markerId);
  }

  function initialFirstChapterKey(): string {
    const firstAudioKey = audioWindows[0]?.key;
    const mapped = persistedMappings.find((mapping) => mappingKey(mapping) === firstAudioKey)?.readableChapterKey;
    if (mapped && readableTitleByKey.has(mapped)) return mapped;
    return readableWindows[0]?.chapterKey ?? "";
  }

  function selectionOptions(audioKey: string): SelectOption[] {
    const automaticTitle = automaticTitleByAudioKey.get(audioKey);
    return [
      {
        value: "",
        label: automaticTitle ? `Automatic: ${automaticTitle}` : "No explicit mapping",
      },
      ...readableOptions,
    ];
  }

  function statusFor(audioKey: string): { label: string; manual: boolean } {
    const confirmed = mappingByAudioKey.get(audioKey);
    if (confirmed) {
      return {
        label: confirmed.origin === BOOK_CHAPTER_MAPPING_ORIGIN.ordered ? "Filled in order" : "Manual",
        manual: true,
      };
    }
    if (automaticTitleByAudioKey.has(audioKey)) return { label: "Exact title", manual: false };
    return { label: "Unmatched", manual: false };
  }

  function windowRange(window: AudioChapterWindow): string {
    const start = formatDuration(Number(window.startSeconds)) ?? "0:00";
    if (window.endSeconds == null) return start;
    const end = formatDuration(Number(window.endSeconds)) ?? "0:00";
    return `${start} – ${window.endInferred ? "≈" : ""}${end}`;
  }

  function updateAudioChapterMapping(window: AudioChapterWindow, readableChapterKey: string): void {
    const key = windowKey(window);
    saved = false;
    actionError = null;
    draft = draft.filter((mapping) =>
      mappingKey(mapping) !== key &&
      (!readableChapterKey || mapping.readableChapterKey !== readableChapterKey),
    );
    if (readableChapterKey) {
      draft = [...draft, {
        audioTrackId: window.trackEntityId,
        readableChapterKey,
        origin: BOOK_CHAPTER_MAPPING_ORIGIN.manual,
        ...(window.markerId ? { audioMarkerId: window.markerId } : {}),
      }];
    }
  }

  /** Proposes pairs in playback order from the chosen chapter; they are reviewed before use. */
  function proposeFillInOrder(): void {
    if (!firstReadableChapterKey) return;
    proposedFill = sequentialBookChapterMappings(readableWindows, audioWindows, firstReadableChapterKey);
    saved = false;
    actionError = null;
  }

  /** Uses the reviewed in-order pairs as the draft; each keeps its "filled in order" origin. */
  function acceptFillInOrder(): void {
    if (!proposedFill) return;
    draft = proposedFill;
    proposedFill = null;
  }

  function clearOverrides(): void {
    draft = [];
    proposedFill = null;
    saved = false;
    actionError = null;
  }

  async function save(): Promise<void> {
    if (!dirty || saving) return;
    saving = true;
    actionError = null;
    saved = false;
    try {
      const refreshed = await onSave(draft);
      const manual = alignmentChapterMappings(refreshed)
        .filter((mapping) => mapping.origin !== BOOK_CHAPTER_MAPPING_ORIGIN.auto);
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
        Prismedia uses embedded M4B chapters when present and whole files otherwise, and pairs them
        automatically only when titles match exactly. Pick pairs yourself, or fill in order from a
        chapter and review every pair before saving. Rows follow the book, so unmatched audio
        appears where it happens.
      </p>
      {#if coverage}
        <p class="mapping-coverage">
          {coverage.pairedCount} of {coverage.readableCount} readable chapters aligned ·
          {coverage.pairedCount} of {coverage.audioWindowCount} audio chapters aligned ·
          {coverage.manualCount} confirmed · {coverage.automaticCount} exact title
        </p>
      {/if}
      {#if separateReason}
        <p class="mapping-coverage">{separateReason}</p>
      {/if}
    </div>
    <div
      class="mapping-count"
      aria-label={`${coverage?.pairedCount ?? 0} of ${coverage?.audioWindowCount ?? 0} audio chapters aligned`}
    >
      <strong>{coverage?.pairedCount ?? 0}/{coverage?.audioWindowCount ?? 0}</strong>
      <span>aligned</span>
    </div>
  </div>

  <div class="first-chapter-card">
    <div class="first-file">
      <span class="file-icon"><FileAudio class="h-5 w-5" /></span>
      <div>
        <span class="field-label">First audio chapter</span>
        <strong>{audioWindows[0]?.window.title ?? "No audio chapters"}</strong>
      </div>
    </div>
    <div class="first-chapter-control">
      <label for="first-readable-chapter">Starts at readable chapter</label>
      <Select
        value={firstReadableChapterKey}
        options={readableOptions}
        ariaLabel="Readable chapter for the first audio chapter"
        disabled={saving || readableWindows.length === 0}
        onchange={(value) => (firstReadableChapterKey = value)}
      />
    </div>
    <Button
      variant="primary"
      size="lg"
      disabled={saving || !firstReadableChapterKey || audioWindows.length === 0}
      onclick={proposeFillInOrder}
    >
      <ArrowDownToLine class="h-4 w-4" />
      Fill in order from here
    </Button>
  </div>

  {#if proposedFill}
    <div class="fill-review" role="region" aria-labelledby="fill-review-heading">
      <div class="fill-review-header">
        <div>
          <h3 id="fill-review-heading">Review {proposedFill.length} pairs filled in order</h3>
          <p>
            Each audio chapter is paired with the next readable chapter. Check every pair: saved
            pairs link reading and listening exactly as listed.
          </p>
        </div>
        <div class="fill-review-actions">
          <Button variant="ghost" onclick={() => (proposedFill = null)}>Discard</Button>
          <Button variant="primary" disabled={proposedFill.length === 0} onclick={acceptFillInOrder}>
            <Check class="h-4 w-4" />
            Use these pairs
          </Button>
        </div>
      </div>
      <ol class="fill-review-list" aria-label="Pairs filled in order">
        {#each proposedFill as pair (mappingKey(pair))}
          <li>
            <span class="pair-audio">{audioTitleByKey.get(mappingKey(pair)) ?? "Audio chapter"}</span>
            <span class="pair-arrow" aria-hidden="true">→</span>
            <span class="pair-readable">{readableTitleByKey.get(pair.readableChapterKey) ?? pair.readableChapterKey}</span>
          </li>
        {/each}
      </ol>
    </div>
  {/if}

  <div class="mapping-list" aria-label="Chapter alignment">
    {#each rows as row (row.rowId)}
      {#if row.audio}
        {@const audio = row.audio}
        {@const key = windowKey(audio)}
        {@const status = statusFor(key)}
        <div class="mapping-row">
          <span class="track-number">{String(audioNumberByKey.get(key) ?? 0).padStart(2, "0")}</span>
          <div class="track-title">
            <strong>{audio.title}</strong>
            <span>
              {windowRange(audio)}
              {#if row.readable}
                · with {row.readable.title}
              {/if}
            </span>
          </div>
          <div class="mapping-choice">
            <Badge variant={status.manual ? "accent" : "outline"}>{status.label}</Badge>
            <Select
              value={mappingByAudioKey.get(key)?.readableChapterKey ?? ""}
              options={selectionOptions(key)}
              ariaLabel={`Readable chapter for ${audio.title}`}
              disabled={saving}
              onchange={(value) => updateAudioChapterMapping(audio, value)}
            />
          </div>
        </div>
      {:else if row.readable}
        <div class="mapping-row readable-only">
          <span class="track-number">··</span>
          <div class="track-title">
            <strong>{row.readable.title}</strong>
            <span>No matching audio chapter</span>
          </div>
        </div>
      {/if}
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

  .mapping-coverage {
    margin: 0.6rem 0 0;
    color: var(--color-text-muted);
    font-size: 0.76rem;
    line-height: 1.5;
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

  .fill-review {
    display: grid;
    gap: 0.75rem;
    padding: 1rem;
    border: 1px solid var(--color-border-default);
    border-radius: var(--radius-lg);
    background: var(--color-surface-1);
  }

  .fill-review-header {
    display: flex;
    align-items: flex-start;
    justify-content: space-between;
    gap: 1rem;
  }

  .fill-review-header h3 {
    margin: 0;
    font-family: var(--font-heading);
    font-size: 0.95rem;
    font-weight: 600;
    color: var(--color-text-primary);
  }

  .fill-review-header p {
    max-width: 40rem;
    margin: 0.3rem 0 0;
    font-size: 0.78rem;
    line-height: 1.5;
    color: var(--color-text-secondary);
  }

  .fill-review-actions {
    display: flex;
    flex: 0 0 auto;
    gap: 0.5rem;
  }

  .fill-review-list {
    display: grid;
    max-height: 22rem;
    margin: 0;
    padding: 0;
    overflow-y: auto;
    list-style: none;
    border-top: 1px solid var(--color-border-subtle);
  }

  .fill-review-list li {
    display: grid;
    grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr);
    gap: 0.6rem;
    align-items: center;
    padding: 0.45rem 0.1rem;
    border-bottom: 1px solid var(--color-border-subtle);
    font-size: 0.78rem;
  }

  .pair-audio,
  .pair-readable {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    color: var(--color-text-primary);
  }

  .pair-arrow {
    font-family: var(--font-mono);
    color: var(--color-text-muted);
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

  .mapping-row.readable-only {
    min-height: 2.75rem;
    opacity: 0.72;
  }

  .mapping-choice {
    display: flex;
    align-items: center;
    gap: 0.6rem;
    min-width: 0;
  }

  .mapping-choice :global(.relative) {
    flex: 1 1 auto;
    min-width: 0;
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

    .fill-review-header {
      flex-direction: column;
    }

    .mapping-row {
      grid-template-columns: 2rem minmax(0, 1fr);
    }

    .mapping-choice {
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
