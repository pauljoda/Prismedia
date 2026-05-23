<script module lang="ts">
  export function formatSecondsInput(seconds: number): string {
    const m = Math.floor(seconds / 60);
    const s = Math.floor(seconds % 60);
    return `${m}:${String(s).padStart(2, "0")}`;
  }

  export function parseTimeInput(value: string): number | null {
    const v = value.trim();
    if (v === "") return null;
    if (v.includes(":")) {
      const parts = v.split(":").map((p) => p.trim());
      if (parts.some((p) => p !== "" && Number.isNaN(Number(p)))) return null;
      if (parts.length === 2) {
        const m = Number(parts[0]) || 0;
        const s = Number(parts[1]) || 0;
        return m * 60 + s;
      }
      if (parts.length === 3) {
        const h = Number(parts[0]) || 0;
        const m = Number(parts[1]) || 0;
        const s = Number(parts[2]) || 0;
        return h * 3600 + m * 60 + s;
      }
      return null;
    }
    const n = Number(v);
    return Number.isFinite(n) ? n : null;
  }
</script>

<script lang="ts">
  import { MapPin, X, Loader } from "@lucide/svelte";
  import { Button } from "@prismedia/ui-svelte";

  interface Props {
    title: string;
    seconds: number;
    endSeconds: number | null;
    saving: boolean;
    onTitleChange: (v: string) => void;
    onSecondsChange: (v: number) => void;
    onEndSecondsChange: (v: number | null) => void;
    onSetCurrentTime: () => void;
    onSetCurrentEndTime: () => void;
    onSave: (payload: { seconds: number; endSeconds: number | null }) => void;
    onCancel: () => void;
    saveLabel?: string;
  }

  let {
    title,
    seconds,
    endSeconds,
    saving,
    onTitleChange,
    onSecondsChange,
    onEndSecondsChange,
    onSetCurrentTime,
    onSetCurrentEndTime,
    onSave,
    onCancel,
    saveLabel = "Save Marker",
  }: Props = $props();

  let startText = $state("0:00");
  let endText = $state("");

  $effect(() => {
    startText = formatSecondsInput(seconds);
  });

  $effect(() => {
    endText = endSeconds != null ? formatSecondsInput(endSeconds) : "";
  });

  function commitStartTime() {
    const parsed = parseTimeInput(startText);
    if (parsed === null) {
      startText = formatSecondsInput(seconds);
      return;
    }
    onSecondsChange(Math.max(0, Math.floor(parsed)));
  }

  function commitEndTime() {
    const v = endText.trim();
    if (v === "") {
      onEndSecondsChange(null);
      endText = "";
      return;
    }
    const parsed = parseTimeInput(v);
    if (parsed === null) {
      endText = endSeconds != null ? formatSecondsInput(endSeconds) : "";
      return;
    }
    onEndSecondsChange(Math.max(0, Math.floor(parsed)));
  }

  function readCommittedTimes(): {
    seconds: number;
    endSeconds: number | null;
  } | null {
    const startParsed = parseTimeInput(startText);
    if (startParsed === null) {
      startText = formatSecondsInput(seconds);
      return null;
    }
    const sec = Math.max(0, Math.floor(startParsed));
    if (endText.trim() === "") return { seconds: sec, endSeconds: null };
    const endParsed = parseTimeInput(endText);
    if (endParsed === null) {
      endText = endSeconds != null ? formatSecondsInput(endSeconds) : "";
      return null;
    }
    return { seconds: sec, endSeconds: Math.max(0, Math.floor(endParsed)) };
  }

  function handleSaveClick() {
    if (!title.trim()) return;
    const payload = readCommittedTimes();
    if (!payload) return;
    onSecondsChange(payload.seconds);
    onEndSecondsChange(payload.endSeconds);
    onSave(payload);
  }
</script>

<div class="surface-card-sharp p-4 space-y-3">
  <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
    <div class="space-y-1 sm:col-span-2">
      <label class="text-xs text-text-muted" for="marker-title">Title</label>
      <input
        id="marker-title"
        class="control-input w-full"
        value={title}
        oninput={(e) => onTitleChange((e.currentTarget as HTMLInputElement).value)}
        placeholder="Marker title"
      />
    </div>

    <div class="space-y-1">
      <label class="text-xs text-text-muted" for="marker-start">Start Time</label>
      <div class="flex items-center gap-2">
        <input
          id="marker-start"
          class="control-input flex-1 min-w-0"
          bind:value={startText}
          onblur={commitStartTime}
          onkeydown={(e) => {
            if (e.key === "Enter") (e.currentTarget as HTMLInputElement).blur();
          }}
          placeholder="0:00"
        />
        <button
          type="button"
          onclick={onSetCurrentTime}
          class="flex items-center gap-1 px-2 py-1.5 text-xs text-text-muted hover:text-text-accent surface-well hover:border-border-accent transition-colors"
          title="Set to current playback time"
        >
          <MapPin class="h-3 w-3" />
          Now
        </button>
      </div>
    </div>

    <div class="space-y-1">
      <label class="text-xs text-text-muted" for="marker-end">End Time (optional)</label>
      <div class="flex items-center gap-2">
        <input
          id="marker-end"
          class="control-input flex-1 min-w-0"
          bind:value={endText}
          onblur={commitEndTime}
          onkeydown={(e) => {
            if (e.key === "Enter") (e.currentTarget as HTMLInputElement).blur();
          }}
          placeholder="—"
        />
        <button
          type="button"
          onclick={onSetCurrentEndTime}
          class="flex items-center gap-1 px-2 py-1.5 text-xs text-text-muted hover:text-text-accent surface-well hover:border-border-accent transition-colors"
          title="Set to current playback time"
        >
          <MapPin class="h-3 w-3" />
          Now
        </button>
        {#if endSeconds != null}
          <button
            type="button"
            onclick={() => {
              endText = "";
              onEndSecondsChange(null);
            }}
            class="flex items-center justify-center p-1.5 text-text-muted hover:text-error-text transition-colors"
            title="Clear end time"
            aria-label="Clear end time"
          >
            <X class="h-3 w-3" />
          </button>
        {/if}
      </div>
    </div>
  </div>

  <div class="flex items-center justify-end gap-2">
    <Button variant="ghost" size="sm" onclick={onCancel}>
      {#snippet children()}Cancel{/snippet}
    </Button>
    <Button
      variant="primary"
      size="sm"
      onclick={handleSaveClick}
      disabled={saving || !title.trim()}
    >
      {#snippet children()}
        {#if saving}<Loader class="h-3.5 w-3.5 animate-spin" />{/if}
        {saveLabel}
      {/snippet}
    </Button>
  </div>
</div>
