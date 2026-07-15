<script module lang="ts">
  export type TranscriptPanelVariant = "full" | "tracks-only" | "list-only" | "compact";
</script>

<script lang="ts">
  import {
    Check,
    Layout,
    Loader2,
    PanelRightOpen,
    Pencil,
    Trash2,
    Upload,
    Wand2,
    X,
  } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import SubtitleSearchSurface from "$lib/components/SubtitleSearchSurface.svelte";
  import type { SubtitleCue, VideoSubtitleTrack } from "$lib/player/subtitle-types";
  import { fetchVideoSubtitleCues } from "$lib/player/video-subtitles";
  import { useSession } from "$lib/stores/session.svelte";

  interface Props {
    videoId: string;
    tracks: VideoSubtitleTrack[];
    activeTrackId: string | null;
    onActiveTrackIdChange: (id: string | null) => void;
    currentTime: number;
    onSeek: (time: number) => void;
    onTracksChanged: () => void | Promise<void>;
    variant?: TranscriptPanelVariant;
    isDocked?: boolean;
    onDockToggle?: () => void;
  }

  let {
    videoId,
    tracks,
    activeTrackId,
    onActiveTrackIdChange,
    currentTime,
    onSeek,
    onTracksChanged,
    variant = "full",
    isDocked = false,
    onDockToggle,
  }: Props = $props();

  const session = useSession();

  const showTrackManagement = $derived(variant !== "list-only" && variant !== "compact");
  const showTranscriptList = $derived(variant !== "tracks-only");
  const isListOnly = $derived(variant === "list-only");
  const isCompact = $derived(variant === "compact");

  let cues = $state<SubtitleCue[]>([]);
  let loadingCues = $state(false);
  let cuesError = $state<string | null>(null);
  let uploading = $state(false);
  let uploadLanguage = $state("en");
  let extractState = $state<"idle" | "queued">("idle");
  let editingTrackId = $state<string | null>(null);
  let editDraftLabel = $state("");
  let editDraftLanguage = $state("");
  let fileInput: HTMLInputElement | null = $state(null);
  let listEl: HTMLDivElement | null = $state(null);
  let lastUserScroll = 0;
  let isAutoScrolling = false;

  function formatTime(seconds: number): string {
    if (!Number.isFinite(seconds) || seconds < 0) return "0:00";
    const h = Math.floor(seconds / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    const s = Math.floor(seconds % 60);
    if (h > 0) {
      return `${h}:${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`;
    }
    return `${m}:${String(s).padStart(2, "0")}`;
  }

  function languageLabel(language: string): string {
    if (!language || language === "und") return "Unknown";
    try {
      const dn = new Intl.DisplayNames(undefined, { type: "language" });
      return dn.of(language) ?? language.toUpperCase();
    } catch {
      return language.toUpperCase();
    }
  }

  $effect(() => {
    if (tracks.length > 0 && !activeTrackId) {
      onActiveTrackIdChange(tracks[0]!.id);
    }
  });

  $effect(() => {
    if (!activeTrackId) {
      cues = [];
      cuesError = null;
      return;
    }
    const trackId = activeTrackId;
    const track = tracks.find((candidate) => candidate.id === trackId);
    if (!track) {
      cues = [];
      cuesError = "Subtitle track was not found.";
      return;
    }
    let cancelled = false;
    loadingCues = true;
    cuesError = null;
    fetchVideoSubtitleCues(track)
      .then((res) => {
        if (cancelled) return;
        cues = res.cues;
      })
      .catch((err) => {
        if (cancelled) return;
        cuesError = (err as Error).message;
      })
      .finally(() => {
        if (cancelled) return;
        loadingCues = false;
      });
    return () => {
      cancelled = true;
    };
  });

  const currentIndex = $derived.by(() => {
    if (cues.length === 0) return -1;
    for (let i = cues.length - 1; i >= 0; i--) {
      const c = cues[i]!;
      if (currentTime >= c.start && currentTime < c.end) return i;
    }
    return -1;
  });

  $effect(() => {
    const idx = currentIndex;
    if (idx < 0) return;
    const sinceLastUserScroll = Date.now() - lastUserScroll;
    if (sinceLastUserScroll < 3000) return;
    const container = listEl;
    if (!container) return;
    const el = container.querySelector<HTMLElement>(
      `[data-cue-index="${idx}"]`,
    );
    if (!el) return;
    const containerRect = container.getBoundingClientRect();
    const elRect = el.getBoundingClientRect();
    const elOffsetFromVisibleTop = elRect.top - containerRect.top;
    const desiredOffset = container.clientHeight / 2 - el.clientHeight / 2;
    const delta = elOffsetFromVisibleTop - desiredOffset;
    if (Math.abs(delta) < 2) return;
    const nextScrollTop = Math.max(
      0,
      Math.min(
        container.scrollTop + delta,
        container.scrollHeight - container.clientHeight,
      ),
    );
    isAutoScrolling = true;
    container.scrollTo({ top: nextScrollTop, behavior: "smooth" });
    window.setTimeout(() => {
      isAutoScrolling = false;
    }, 500);
  });

  function handleScroll() {
    if (isAutoScrolling) return;
    lastUserScroll = Date.now();
  }

  async function handleUpload(event: Event) {
    const input = event.currentTarget as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    cuesError = "Subtitle editing is waiting on the API.";
    if (fileInput) fileInput.value = "";
  }

  async function handleExtract() {
    if (extractState !== "idle") return;
    cuesError = "Subtitle extraction is waiting on the API.";
  }

  async function handleDelete(trackId: string) {
    void trackId;
    cuesError = "Subtitle editing is waiting on the API.";
  }

  function startEditingTrack(track: VideoSubtitleTrack) {
    editingTrackId = track.id;
    editDraftLabel = track.label ?? "";
    editDraftLanguage = track.language;
  }

  function cancelEditingTrack() {
    editingTrackId = null;
    editDraftLabel = "";
    editDraftLanguage = "";
  }

  async function saveEditingTrack() {
    if (!editingTrackId) return;
    const trackId = editingTrackId;
    const patch: { language?: string; label?: string | null } = {
      label: editDraftLabel.trim() === "" ? null : editDraftLabel.trim(),
    };
    if (editDraftLanguage.trim()) patch.language = editDraftLanguage.trim();
    void trackId;
    void patch;
    cuesError = "Subtitle editing is waiting on the API.";
  }
</script>

{#if isCompact}
  <div class="surface-card-sharp border-border-default bg-surface-1/90">
    <div class="flex items-center justify-between border-b border-border-default px-3 py-2">
      <span class="text-[0.66rem] uppercase tracking-[0.16em] text-text-muted">Transcript</span>
      {#if onDockToggle}
        <button
          type="button"
          onclick={onDockToggle}
          class="inline-flex items-center gap-1.5 border border-border-default px-2 py-0.5 text-[0.62rem] text-text-muted transition-colors duration-fast hover:border-border-accent hover:text-text-accent"
          title="Move transcript back into the tab"
        >
          <Layout class="h-3 w-3" />
          Theatre
        </button>
      {/if}
    </div>
    <div
      bind:this={listEl}
      onscroll={handleScroll}
      class="max-h-[min(32dvh,15rem)] overflow-y-auto py-1"
    >
      {#if loadingCues}
        <div class="flex items-center justify-center py-5 text-[0.76rem] text-text-muted">
          <Loader2 class="mr-2 h-4 w-4 animate-spin" />
          Loading cues...
        </div>
      {:else if cuesError}
        <div class="px-3 py-4 text-[0.76rem] text-error-text">{cuesError}</div>
      {:else if !activeTrackId}
        <div class="px-3 py-4 text-center text-[0.76rem] text-text-muted">
          Select a subtitle track to view its transcript.
        </div>
      {:else if cues.length === 0}
        <div class="px-3 py-4 text-center text-[0.76rem] text-text-muted">No cues in this track.</div>
      {:else}
        {#each cues as cue, idx (idx)}
          {@const isCurrent = idx === currentIndex}
          {@const isPast = currentIndex >= 0 ? idx < currentIndex : cue.end <= currentTime}
          <button
            type="button"
            data-cue-index={idx}
            onclick={() => onSeek(cue.start)}
            class={cn(
              "block w-full border-l-2 px-3 py-1.5 text-left text-[0.78rem] leading-snug transition-colors duration-fast",
              isCurrent
                ? "border-accent-500 bg-accent-950/60 text-text-primary text-shadow-cue"
                : isPast
                  ? "border-transparent text-text-muted opacity-65"
                  : "border-transparent text-text-secondary",
            )}
          >
            <span class="mr-2 text-mono-tabular text-[0.62rem] uppercase tracking-[0.1em] text-text-disabled">
              {formatTime(cue.start)}
            </span>
            <span class="whitespace-pre-line">{cue.text}</span>
          </button>
        {/each}
      {/if}
    </div>
  </div>
{:else}
<div class={cn("flex flex-col", isListOnly ? "h-full min-h-0 space-y-0" : "space-y-4")}>
  {#if showTrackManagement}
    <div class="surface-card-sharp p-3 space-y-3">
      <div class="flex items-center justify-between gap-2">
        <span class="text-[0.7rem] uppercase tracking-[0.14em] text-text-muted">Tracks</span>
        {#if onDockToggle}
          <button
            type="button"
            onclick={onDockToggle}
            class="inline-flex items-center gap-1.5 border border-border-default px-2 py-0.5 text-[0.65rem] text-text-muted hover:border-border-accent hover:text-text-accent transition-colors duration-fast"
            title={isDocked ? "Move transcript back into this tab" : "Dock transcript next to the video"}
          >
            {#if isDocked}
              <Layout class="h-3 w-3" />
              Theatre
            {:else}
              <PanelRightOpen class="h-3 w-3" />
              Dock next to video
            {/if}
          </button>
        {/if}
      </div>

      {#if tracks.length === 0}
        <div class="text-[0.78rem] text-text-muted">
          No subtitle tracks yet. Upload a .vtt/.srt file or extract from the video.
        </div>
      {:else}
        <div class="flex flex-col gap-1.5">
          {#each tracks as track (track.id)}
            {@const isActive = track.id === activeTrackId}
            {@const isEditing = editingTrackId === track.id}
            {@const lang = languageLabel(track.language)}
            <div
              class={cn(
                "flex items-center gap-2 border px-2.5 py-1.5 transition-colors duration-fast",
                isActive
                  ? "bg-accent-950 border-border-accent"
                  : "border-border-default hover:border-border-accent",
              )}
            >
              {#if isEditing}
                <div class="flex flex-1 items-center gap-2">
                  <input
                    type="text"
                    bind:value={editDraftLanguage}
                    maxlength={8}
                    placeholder="lang"
                    class="w-16 border border-border-default bg-surface-1 px-2 py-0.5 text-[0.72rem] text-text-primary focus:border-border-accent focus:outline-none"
                    aria-label="Track language"
                  />
                  <input
                    type="text"
                    bind:value={editDraftLabel}
                    maxlength={80}
                    placeholder="Display name (e.g. SDH, Forced)"
                    class="flex-1 border border-border-default bg-surface-1 px-2 py-0.5 text-[0.75rem] text-text-primary focus:border-border-accent focus:outline-none"
                    aria-label="Track display name"
                    onkeydown={(e) => {
                      if (e.key === "Enter") void saveEditingTrack();
                      if (e.key === "Escape") cancelEditingTrack();
                    }}
                  />
                  <button
                    type="button"
                    onclick={() => void saveEditingTrack()}
                    class="text-text-accent hover:text-text-accent-bright transition-colors"
                    title="Save"
                    aria-label="Save track"
                  >
                    <Check class="h-3.5 w-3.5" />
                  </button>
                  <button
                    type="button"
                    onclick={cancelEditingTrack}
                    class="text-text-muted hover:text-text-primary transition-colors"
                    title="Cancel"
                    aria-label="Cancel edit"
                  >
                    <X class="h-3.5 w-3.5" />
                  </button>
                </div>
              {:else}
                <button
                  type="button"
                  onclick={() => onActiveTrackIdChange(track.id)}
                  class="flex-1 flex items-center gap-2 text-left"
                >
                  <span class={cn("text-[0.78rem] font-medium", isActive ? "text-text-accent" : "text-text-primary")}>
                    {lang}
                  </span>
                  {#if track.label}
                    <span class={cn("text-[0.7rem]", isActive ? "text-text-accent/80" : "text-text-muted")}>
                      — {track.label}
                    </span>
                  {/if}
                  <span class="ml-auto flex items-center gap-1.5">
                    <span
                      class="border border-border-default px-1.5 py-px text-[0.56rem] font-semibold uppercase tracking-[0.14em] text-text-muted"
                      title={`Original format: ${track.sourceFormat}`}
                    >
                      {track.sourceFormat}
                    </span>
                    <span class="text-[0.58rem] uppercase tracking-[0.14em] text-text-muted">
                      {track.source}
                    </span>
                  </span>
                </button>
                <button
                  type="button"
                  onclick={() => startEditingTrack(track)}
                  class="text-text-muted hover:text-text-accent transition-colors"
                  title="Rename track"
                  aria-label="Rename track"
                >
                  <Pencil class="h-3.5 w-3.5" />
                </button>
                <button
                  type="button"
                  onclick={() => void handleDelete(track.id)}
                  class="text-text-muted hover:text-error-text transition-colors"
                  title="Remove track"
                  aria-label="Remove track"
                >
                  <Trash2 class="h-3.5 w-3.5" />
                </button>
              {/if}
            </div>
          {/each}
        </div>
      {/if}

      <div class="flex flex-wrap items-center gap-2">
        <input
          bind:this={fileInput}
          type="file"
          accept=".vtt,.srt,.ass,.ssa,text/vtt"
          class="hidden"
          onchange={handleUpload}
        />
        <input
          type="text"
          bind:value={uploadLanguage}
          maxlength={8}
          placeholder="lang"
          class="w-16 border border-border-default bg-surface-1 px-2 py-1 text-[0.75rem] text-text-primary focus:border-border-accent focus:outline-none"
          aria-label="Upload language"
        />
        <button
          type="button"
          onclick={() => fileInput?.click()}
          disabled={uploading}
          class="flex items-center gap-1.5 border border-border-default px-2.5 py-1 text-[0.75rem] text-text-secondary hover:border-border-accent hover:text-text-accent transition-colors duration-fast disabled:opacity-60"
        >
          {#if uploading}
            <Loader2 class="h-3.5 w-3.5 animate-spin" />
          {:else}
            <Upload class="h-3.5 w-3.5" />
          {/if}
          Upload subtitle
        </button>
        <button
          type="button"
          onclick={() => void handleExtract()}
          disabled={extractState !== "idle"}
          class="flex items-center gap-1.5 border border-border-default px-2.5 py-1 text-[0.75rem] text-text-secondary hover:border-border-accent hover:text-text-accent transition-colors duration-fast disabled:opacity-60"
        >
          <Wand2 class="h-3.5 w-3.5" />
          {extractState === "idle" ? "Extract embedded" : "Queued"}
        </button>
      </div>
    </div>

    {#if session.isAdmin}
      <SubtitleSearchSurface
        {videoId}
        {onTracksChanged}
        {onActiveTrackIdChange}
      />
    {/if}
  {/if}

  {#if showTranscriptList}
    <div class={cn("surface-card-sharp flex flex-col", isListOnly && "flex-1 min-h-0")}>
      <div class="flex items-start justify-between gap-3 border-b border-border-default px-3 py-2">
        <div class="min-w-0">
          <span class="block text-[0.7rem] uppercase tracking-[0.14em] text-text-muted">Transcript</span>
          {#if cues.length > 0}
            <span class="mt-1 block text-[0.66rem] leading-none text-text-disabled">
              {cues.length} lines in this track
            </span>
          {/if}
        </div>
        <div class="flex shrink-0 items-center gap-2">
          {#if isListOnly && onDockToggle}
            <button
              type="button"
              onclick={onDockToggle}
              class="text-text-muted hover:text-text-accent transition-colors"
              title="Move transcript back into the tab"
              aria-label="Undock transcript"
            >
              <Layout class="h-3.5 w-3.5" />
            </button>
          {/if}
        </div>
      </div>
      <div
        bind:this={listEl}
        onscroll={handleScroll}
        class={cn("surface-well overflow-y-auto", isListOnly ? "flex-1 min-h-0" : "max-h-[28rem]")}
      >
        {#if loadingCues}
          <div class="flex items-center justify-center py-10 text-text-muted text-[0.78rem]">
            <Loader2 class="h-4 w-4 animate-spin mr-2" />
            Loading cues…
          </div>
        {/if}
        {#if !loadingCues && cuesError}
          <div class="px-3 py-6 text-[0.78rem] text-error-text">{cuesError}</div>
        {/if}
        {#if !loadingCues && !cuesError && !activeTrackId}
          <div class="px-3 py-8 text-[0.78rem] text-text-muted text-center">
            Select a subtitle track to view its transcript.
          </div>
        {/if}
        {#if !loadingCues && !cuesError && activeTrackId && cues.length === 0}
          <div class="px-3 py-8 text-[0.78rem] text-text-muted text-center">No cues in this track.</div>
        {/if}
        {#if !loadingCues && !cuesError}
          {#each cues as cue, idx (idx)}
            {@const isCurrent = idx === currentIndex}
            {@const isPast = currentIndex >= 0 ? idx < currentIndex : cue.end <= currentTime}
            <button
              type="button"
              data-cue-index={idx}
              onclick={() => onSeek(cue.start)}
              class={cn(
                "block w-full text-left border-l-2 px-3 py-2 text-[0.82rem] leading-snug transition-colors duration-fast",
                isCurrent
                  ? "border-accent-500 bg-accent-950/60 text-text-primary text-shadow-cue"
                  : isPast
                    ? "border-transparent text-text-muted opacity-60 hover:opacity-100 hover:bg-surface-2"
                    : "border-transparent text-text-secondary hover:bg-surface-2 hover:text-text-primary",
              )}
            >
              <span class="text-mono-tabular text-[0.65rem] uppercase tracking-[0.1em] text-text-disabled mr-2">
                {formatTime(cue.start)}
              </span>
              <span class="whitespace-pre-line">{cue.text}</span>
            </button>
          {/each}
        {/if}
      </div>
    </div>
  {/if}
</div>
{/if}
