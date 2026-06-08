<script lang="ts">
  import {
    FileArchive,
    FolderPlus,
    Image as ImageIcon,
    Pencil,
    RefreshCw,
    ScanLine,
    ShieldOff,
    Trash2,
    Upload,
    Undo2,
  } from "@lucide/svelte";
  import { Info } from "@lucide/svelte";
  import type { FileDetail } from "$lib/api/files";
  import { fileContentUrl } from "$lib/api/files";
  import type { FileActionId } from "$lib/files/file-actions";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import MetadataCard from "$lib/components/MetadataCard.svelte";
  import { entityReferenceToThumbnailCard } from "$lib/entities/entity-thumbnail";

  interface Props {
    detail: FileDetail | null;
    loading?: boolean;
    error?: string | null;
    mobile?: boolean;
    onBack?: () => void;
    onRefresh?: () => void;
    onAction?: (action: FileActionId) => void;
    onUploadFiles?: (files: FileList | null) => void;
    onUploadFolder?: (files: FileList | null) => void;
    onExternalDrop?: (dataTransfer: DataTransfer | null) => void;
  }

  let {
    detail,
    loading = false,
    error = null,
    mobile = false,
    onBack,
    onRefresh,
    onAction,
    onUploadFiles,
    onUploadFolder,
    onExternalDrop,
  }: Props = $props();

  let fileInput = $state<HTMLInputElement>();
  let folderInput = $state<HTMLInputElement>();
  let textPreview = $state<string | null>(null);
  let previewError = $state<string | null>(null);

  const entry = $derived(detail?.entry ?? null);
  const isDirectory = $derived(entry?.kind === "directory");
  const isRoot = $derived(isDirectory && (!entry?.path || entry.path === "."));
  const isExcluded = $derived(Boolean(entry?.excluded));
  const contentUrl = $derived(entry ? fileContentUrl(entry.rootId, entry.path) : "");
  const mime = $derived(entry?.mimeType ?? "");
  const previewKind = $derived(resolvePreviewKind(entry?.name ?? "", mime, isDirectory));

  const containerKinds = new Set(["gallery", "audio-library", "book", "movie", "video-series", "video-season", "collection"]);
  const leafKinds = new Set(["video", "image", "audio-track", "book-page", "book-chapter"]);

  const primaryLinked = $derived.by(() => {
    const linked = detail?.linkedEntities;
    if (isExcluded) return null;
    if (!linked?.length) return null;
    if (linked.length === 1) return linked[0];
    const matchKinds = isDirectory ? containerKinds : leafKinds;
    return linked.find((e) => matchKinds.has(e.kind)) ?? linked[0];
  });
  const heroCard = $derived(
    primaryLinked
      ? entityReferenceToThumbnailCard(
          { id: primaryLinked.entityId, kind: primaryLinked.kind, title: primaryLinked.title, thumbnailUrl: primaryLinked.coverUrl },
        )
      : null,
  );
  const linkedCards = $derived(
    isExcluded ? [] : (detail?.linkedEntities ?? [])
      .filter((linked) => linked.entityId !== primaryLinked?.entityId)
      .map((linked) =>
        entityReferenceToThumbnailCard(
          { id: linked.entityId, kind: linked.kind, title: linked.title, thumbnailUrl: linked.coverUrl },
        ),
      ),
  );

  function resolvePreviewKind(name: string, mimeType: string, directory: boolean): "image" | "video" | "audio" | "text" | "none" {
    if (directory || isExcluded) return "none";
    if (mimeType.startsWith("image/")) return "image";
    if (mimeType.startsWith("video/")) return "video";
    if (mimeType.startsWith("audio/")) return "audio";
    if (mimeType.startsWith("text/")) return "text";
    if (/\.(json|md|markdown|xml|srt|vtt|log|txt|csv|nfo)$/i.test(name)) return "text";
    return "none";
  }

  function formatBytes(value: number | string | null | undefined): string {
    if (value === null || value === undefined || value === "") return "—";
    const bytes = typeof value === "string" ? Number.parseInt(value, 10) : value;
    if (!Number.isFinite(bytes)) return "—";
    if (bytes < 1024) return `${bytes} B`;
    const units = ["KB", "MB", "GB", "TB"];
    let size = bytes / 1024;
    let unit = 0;
    while (size >= 1024 && unit < units.length - 1) {
      size /= 1024;
      unit += 1;
    }
    return `${size.toFixed(size >= 100 ? 0 : 1)} ${units[unit]}`;
  }

  function formatDate(value: string | null | undefined): string {
    if (!value) return "—";
    return new Intl.DateTimeFormat(undefined, {
      dateStyle: "medium",
      timeStyle: "short",
    }).format(new Date(value));
  }

  const fileMetaRows = $derived.by(() => {
    if (!entry || !detail) return [];
    const rows: { label: string; value: string }[] = [];
    if (isDirectory) {
      if (detail.directoryTotalSizeBytes != null) rows.push({ label: "Total size", value: formatBytes(detail.directoryTotalSizeBytes) });
      if (detail.directoryFileCount != null) rows.push({ label: "Files", value: detail.directoryFileCount.toLocaleString() });
    } else {
      rows.push({ label: "Size", value: formatBytes(entry.sizeBytes) });
    }
    rows.push({ label: "Kind", value: entry.kind });
    if (isExcluded) rows.push({ label: "Excluded", value: "Library scans skip this path" });
    rows.push({ label: "Modified", value: formatDate(entry.modifiedAt) });
    rows.push({ label: "Created", value: formatDate(detail.createdAt) });
    if (entry.mimeType) rows.push({ label: "MIME", value: entry.mimeType });
    return rows;
  });

  function handleDragOver(event: DragEvent): void {
    if (!event.dataTransfer?.types.includes("Files")) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = "copy";
  }

  function handleDrop(event: DragEvent): void {
    if (!event.dataTransfer?.types.includes("Files")) return;
    event.preventDefault();
    onExternalDrop?.(event.dataTransfer);
  }

  $effect(() => {
    textPreview = null;
    previewError = null;
    if (previewKind !== "text" || !contentUrl) return;

    const controller = new AbortController();
    void (async () => {
      try {
        const response = await fetch(contentUrl, {
          headers: { Range: "bytes=0-262143" },
          signal: controller.signal,
        });
        if (!response.ok) throw new Error(`Preview ${response.status}`);
        const text = await response.text();
        textPreview = text.length > 262144 ? `${text.slice(0, 262144)}\n...` : text;
      } catch (previewLoadError) {
        if (!controller.signal.aborted) {
          previewError = previewLoadError instanceof Error ? previewLoadError.message : "Preview failed";
        }
      }
    })();

    return () => controller.abort();
  });
</script>

<section class="detail-pane" aria-label="File details" ondragover={handleDragOver} ondrop={handleDrop}>
  <div class="detail-header">
    {#if mobile}
      <button class="icon-button" type="button" onclick={onBack} aria-label="Back to folders">←</button>
    {/if}
    <div class="title-lockup">
      <h1>{entry?.name ?? "Select a file"}</h1>
      {#if entry}
        <p>{entry.path && entry.path !== "." ? entry.path : "/"}</p>
      {/if}
    </div>
    <button class="icon-button" type="button" onclick={onRefresh} aria-label="Refresh details">
      <RefreshCw class="h-3.5 w-3.5" />
    </button>
  </div>

  {#if error}
    <div class="state-panel error">{error}</div>
  {:else if loading}
    <div class="state-panel">Loading...</div>
  {:else if !detail || !entry}
    <div class="state-panel empty">
      <ImageIcon class="h-6 w-6" />
      <span>Select a file or folder to view details</span>
    </div>
  {:else}
    <div class="detail-toolbar">
      {#if isDirectory}
        <button type="button" onclick={() => fileInput?.click()}><Upload class="h-3.5 w-3.5" />Upload</button>
        <button type="button" onclick={() => onAction?.("new-folder")}><FolderPlus class="h-3.5 w-3.5" />New folder</button>
        <button type="button" onclick={() => onAction?.("rescan")}><ScanLine class="h-3.5 w-3.5" />Rescan</button>
      {/if}
      {#if !isRoot}
        <button type="button" onclick={() => onAction?.("rename")}><Pencil class="h-3.5 w-3.5" />Rename</button>
        <button type="button" onclick={() => onAction?.("move")}><FileArchive class="h-3.5 w-3.5" />Move</button>
        {#if isExcluded}
          <button type="button" onclick={() => onAction?.("remove-exclusion")}><Undo2 class="h-3.5 w-3.5" />Remove exclusion</button>
        {:else}
          <button type="button" onclick={() => onAction?.("exclude")}><ShieldOff class="h-3.5 w-3.5" />Exclude</button>
        {/if}
        <div class="toolbar-spacer"></div>
        <button class="danger" type="button" onclick={() => onAction?.("delete")}><Trash2 class="h-3.5 w-3.5" /></button>
      {/if}
    </div>

    <input
      bind:this={fileInput}
      class="hidden-input"
      type="file"
      multiple
      onchange={(event) => onUploadFiles?.(event.currentTarget.files)}
    />
    <input
      bind:this={folderInput}
      class="hidden-input"
      type="file"
      multiple
      webkitdirectory
      onchange={(event) => onUploadFolder?.(event.currentTarget.files)}
    />

    <div class="detail-body">
      {#if previewKind !== "none"}
        <div class="preview" data-kind={previewKind}>
          {#if previewKind === "image"}
            <img src={contentUrl} alt={entry.name} />
          {:else if previewKind === "video"}
            <!-- svelte-ignore a11y_media_has_caption -->
            <video src={contentUrl} controls preload="metadata"></video>
          {:else if previewKind === "audio"}
            <audio src={contentUrl} controls></audio>
          {:else if previewKind === "text"}
            {#if previewError}
              <div class="state-panel error">{previewError}</div>
            {:else}
              <pre>{textPreview ?? "Loading preview..."}</pre>
            {/if}
          {/if}
        </div>
      {/if}

      <section class="properties-card" aria-labelledby="file-properties-heading">
        {#if heroCard}
          <div class="entity-hero" aria-label="Associated entity">
            <EntityThumbnail card={heroCard} linkable selectable={false} titleSize="compact" />
          </div>
        {/if}
        <MetadataCard title="Properties" icon={Info} rows={fileMetaRows} />
      </section>

      {#if linkedCards.length > 0}
        <div class="section-label">Linked entities</div>
        <div class="linked-grid">
          <EntityGrid
            cards={linkedCards}
            selectable={false}
            scrollMaxHeight={null}
            initialPageSize={100}
            minScale={3}
            maxScale={6}
          />
        </div>
      {/if}
    </div>
  {/if}
</section>

<style>
  .detail-pane {
    display: grid;
    width: 100%;
    max-width: 100%;
    min-width: 0;
    min-height: 0;
    grid-template-rows: auto auto 1fr;
    overflow: hidden;
    background: var(--color-bg);
    height: 100%;
  }

  .detail-header {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    min-width: 0;
    border-bottom: 1px solid var(--color-border-subtle);
    padding: 0.5rem 0.75rem;
    background: var(--color-surface-1);
    min-height: 2.75rem;
  }

  .title-lockup {
    min-width: 0;
    flex: 1;
  }

  .title-lockup h1 {
    margin: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    overflow-wrap: anywhere;
    color: var(--color-text-primary);
    font-family: var(--font-body);
    font-size: 0.82rem;
    font-weight: 600;
    line-height: 1.3;
  }

  .title-lockup p {
    margin: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    overflow-wrap: anywhere;
    color: var(--color-text-disabled);
    font-family: var(--font-mono);
    font-size: 0.68rem;
  }

  .icon-button {
    display: grid;
    width: 1.75rem;
    height: 1.75rem;
    place-items: center;
    flex-shrink: 0;
    border: none;
    border-radius: var(--radius-xs);
    background: transparent;
    color: var(--color-text-muted);
    cursor: pointer;
  }

  .icon-button:hover {
    background: var(--color-surface-3);
    color: var(--color-text-primary);
  }

  .detail-toolbar {
    display: flex;
    align-items: center;
    gap: 1px;
    min-width: 0;
    overflow-x: auto;
    overflow-y: hidden;
    border-bottom: 1px solid var(--color-border-subtle);
    padding: 0.25rem 0.5rem;
    background: var(--color-surface-1);
    scrollbar-width: none;
  }

  .detail-toolbar::-webkit-scrollbar {
    display: none;
  }

  .detail-toolbar button {
    display: inline-flex;
    align-items: center;
    flex: 0 0 auto;
    gap: 0.3rem;
    min-height: 1.65rem;
    padding: 0.2rem 0.5rem;
    border: none;
    border-radius: var(--radius-xs);
    background: transparent;
    color: var(--color-text-muted);
    font-size: 0.72rem;
    cursor: pointer;
    white-space: nowrap;
  }

  .detail-toolbar button:hover {
    background: var(--color-surface-3);
    color: var(--color-text-primary);
  }

  .detail-toolbar .danger {
    color: var(--color-text-muted);
  }

  .detail-toolbar .danger:hover {
    color: var(--color-error-text);
    background: var(--color-error-muted);
  }

  .toolbar-spacer {
    flex: 1 0 0;
    min-width: 0.25rem;
  }

  .hidden-input {
    display: none;
  }

  .detail-body {
    overflow-y: auto;
    overflow-x: hidden;
    min-width: 0;
    max-width: 100%;
    padding: 0.75rem;
    display: flex;
    flex-direction: column;
    gap: 0.65rem;
  }

  .section-label {
    color: var(--color-text-disabled);
    font-family: var(--font-mono);
    font-size: 0.65rem;
    font-weight: 500;
    text-transform: uppercase;
    letter-spacing: 0.06em;
    padding-top: 0.25rem;
  }

  .properties-card {
    display: grid;
    min-width: 0;
    max-width: 100%;
    gap: 0.75rem;
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-sm);
    background: var(--color-surface-1);
    padding: 0.75rem;
    box-shadow: var(--shadow-well);
  }

  .entity-hero {
    width: min(100%, 13rem);
  }

  .entity-hero :global(.entity-thumbnail) {
    width: 100%;
  }

  .linked-grid {
    margin: 0 -0.75rem;
    min-width: 0;
    max-width: calc(100% + 1.5rem);
    padding: 0.25rem 0.5rem;
  }

  .preview {
    display: grid;
    min-width: 0;
    max-width: 100%;
    min-height: 12rem;
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-sm);
    background: var(--color-surface-1);
    box-shadow: var(--shadow-well);
    overflow: hidden;
  }

  .preview img,
  .preview video {
    width: 100%;
    height: 100%;
    max-height: 50vh;
    object-fit: contain;
  }

  .preview audio {
    align-self: center;
    width: min(36rem, calc(100% - 2rem));
    margin: 1rem auto;
  }

  .preview pre {
    margin: 0;
    min-width: 0;
    max-width: 100%;
    overflow-x: hidden;
    overflow-y: auto;
    max-height: 40vh;
    padding: 0.6rem;
    color: var(--color-text-secondary);
    font-family: var(--font-mono);
    font-size: 0.72rem;
    line-height: 1.55;
    white-space: pre-wrap;
    overflow-wrap: anywhere;
    word-break: break-word;
  }

  .state-panel {
    display: grid;
    grid-row: 2 / -1;
    place-items: center;
    align-content: center;
    color: var(--color-text-disabled);
    text-align: center;
    font-size: 0.8rem;
  }

  .state-panel.empty {
    gap: 0.5rem;
  }

  .state-panel.error {
    color: var(--color-error-text);
  }

  @media (min-width: 768px) {
    .properties-card {
      display: grid;
      grid-template-columns: minmax(10rem, 13rem) minmax(0, 1fr);
      gap: 0.65rem;
      align-items: start;
    }
  }

  @media (max-width: 767px) {
    .detail-header {
      align-items: flex-start;
    }

    .title-lockup h1 {
      display: -webkit-box;
      text-overflow: clip;
      white-space: normal;
      line-clamp: 2;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
    }
  }
</style>
