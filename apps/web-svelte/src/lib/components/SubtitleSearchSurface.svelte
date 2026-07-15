<script lang="ts">
  import {
    CheckCircle2,
    ExternalLink,
    Languages,
    Loader2,
    Search,
    ShieldAlert,
  } from "@lucide/svelte";
  import { Badge, Button, Meter, TextInput } from "@prismedia/ui-svelte";
  import {
    acquireVideoSubtitle,
    searchVideoSubtitles,
    type SubtitleCandidate,
  } from "$lib/api/subtitles";

  interface Props {
    videoId: string;
    onTracksChanged: () => void | Promise<void>;
    onActiveTrackIdChange: (id: string | null) => void;
  }

  let { videoId, onTracksChanged, onActiveTrackIdChange }: Props = $props();

  let languageDraft = $state("en");
  let candidates = $state.raw<SubtitleCandidate[]>([]);
  let searched = $state(false);
  let searching = $state(false);
  let acquiringKey = $state<string | null>(null);
  let error = $state<string | null>(null);
  let message = $state<string | null>(null);

  const languages = $derived(
    languageDraft
      .split(",")
      .map((language) => language.trim())
      .filter(Boolean),
  );

  function numberValue(value: number | string | null): number {
    const parsed = Number(value ?? 0);
    return Number.isFinite(parsed) ? parsed : 0;
  }

  function languageLabel(language: string): string {
    if (!language || language === "und") return "Unknown language";
    try {
      return new Intl.DisplayNames(undefined, { type: "language" }).of(language) ?? language.toUpperCase();
    } catch {
      return language.toUpperCase();
    }
  }

  function providerLabel(provider: string): string {
    return provider
      .split(/[-_]/)
      .filter(Boolean)
      .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
      .join(" ");
  }

  function candidateKey(candidate: SubtitleCandidate): string {
    return `${candidate.provider}:${candidate.candidateId}`;
  }

  async function search() {
    if (searching || languages.length === 0) return;
    searching = true;
    searched = false;
    candidates = [];
    error = null;
    message = null;
    try {
      const response = await searchVideoSubtitles(videoId, languages);
      candidates = response.candidates;
      searched = true;
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to search subtitle providers";
    } finally {
      searching = false;
    }
  }

  async function acquire(candidate: SubtitleCandidate) {
    const key = candidateKey(candidate);
    if (acquiringKey) return;
    acquiringKey = key;
    error = null;
    message = null;
    try {
      const result = await acquireVideoSubtitle(videoId, candidate);
      await onTracksChanged();
      onActiveTrackIdChange(result.trackId);
      message = result.alreadyPresent
        ? "This subtitle was already in the library and is now active."
        : "Subtitle acquired and activated.";
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to acquire subtitle";
    } finally {
      acquiringKey = null;
    }
  }
</script>

<div class="surface-card-sharp space-y-3 p-3">
  <div class="flex items-start gap-2.5">
    <Languages class="mt-0.5 h-4 w-4 shrink-0 text-text-accent" />
    <div>
      <div class="text-[0.7rem] uppercase tracking-[0.14em] text-text-muted">
        Find subtitles
      </div>
      <p class="mt-1 text-[0.7rem] leading-relaxed text-text-disabled">
        Search OpenSubtitles for ranked matches. Results are imported as SRT for playback and
        transcripts; existing ASS and SSA tracks keep their styling.
      </p>
    </div>
  </div>

  <div class="flex flex-col gap-2 sm:flex-row">
    <TextInput
      value={languageDraft}
      oninput={(event) => (languageDraft = event.currentTarget.value)}
      onkeydown={(event) => {
        if (event.key === "Enter") void search();
      }}
      size="sm"
      placeholder="en, es, fr"
      aria-label="Subtitle search languages"
      class="sm:max-w-xs"
    />
    <Button
      variant="secondary"
      size="sm"
      disabled={searching || languages.length === 0}
      onclick={() => void search()}
      class="no-lift"
    >
      {#if searching}
        <Loader2 class="h-3.5 w-3.5 animate-spin" />
        Searching…
      {:else}
        <Search class="h-3.5 w-3.5" />
        Search providers
      {/if}
    </Button>
  </div>
  <p class="text-[0.64rem] text-text-disabled">
    Enter comma-separated language codes in priority order.
  </p>

  {#if error}
    <div class="border-l-2 border-status-error bg-error-muted/20 px-3 py-2 text-[0.72rem] text-status-error-text">
      {error}
    </div>
  {:else if message}
    <div class="flex items-center gap-2 border-l-2 border-status-success bg-success-muted/20 px-3 py-2 text-[0.72rem] text-status-success-text">
      <CheckCircle2 class="h-3.5 w-3.5 shrink-0" />
      {message}
    </div>
  {/if}

  {#if searched && candidates.length === 0}
    <div class="surface-well px-3 py-5 text-center text-[0.74rem] text-text-muted">
      No subtitle matches were found for those languages.
    </div>
  {:else if candidates.length > 0}
    <div class="space-y-2">
      <div class="flex items-center justify-between gap-3">
        <span class="text-label text-text-muted">Ranked matches</span>
        <span class="text-mono-sm text-text-disabled">{candidates.length} results</span>
      </div>

      {#each candidates as candidate (candidateKey(candidate))}
        {@const key = candidateKey(candidate)}
        {@const matchConfidence = numberValue(candidate.matchConfidence)}
        {@const qualityScore = numberValue(candidate.qualityScore)}
        <article class="surface-well space-y-3 border border-border-subtle p-3">
          <div class="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div class="min-w-0">
              <div class="flex flex-wrap items-center gap-1.5">
                <span class="text-[0.8rem] font-medium text-text-primary">
                  {languageLabel(candidate.language)}
                </span>
                <Badge variant="default" class="uppercase">{candidate.format}</Badge>
                <Badge variant="info">{providerLabel(candidate.provider)}</Badge>
                {#if candidate.hashMatched}
                  <Badge variant="success">Exact hash</Badge>
                {:else if !candidate.automaticEligible}
                  <Badge variant="warning">
                    <ShieldAlert class="mr-1 h-3 w-3" />
                    Manual review
                  </Badge>
                {/if}
              </div>
              <div class="mt-1.5 break-words text-[0.72rem] text-text-secondary">
                {candidate.releaseName ?? "Release name unavailable"}
              </div>
              <div class="mt-2 flex flex-wrap gap-1.5">
                {#if candidate.hearingImpaired}<Badge variant="default">HI / SDH</Badge>{/if}
                {#if candidate.forced}<Badge variant="accent">Forced</Badge>{/if}
                {#if candidate.aiTranslated}<Badge variant="warning">AI translated</Badge>{/if}
                {#if candidate.machineTranslated}<Badge variant="warning">Machine translated</Badge>{/if}
                {#each candidate.matchReasons as reason (reason)}
                  <Badge variant="default">{reason}</Badge>
                {/each}
              </div>
            </div>

            <Button
              size="sm"
              disabled={acquiringKey !== null}
              onclick={() => void acquire(candidate)}
              class="no-lift shrink-0"
            >
              {#if acquiringKey === key}
                <Loader2 class="h-3.5 w-3.5 animate-spin" />
                Acquiring…
              {:else}
                Acquire
              {/if}
            </Button>
          </div>

          <div class="grid gap-3 sm:grid-cols-2">
            <Meter value={matchConfidence} label="Match confidence" showValue />
            <Meter value={qualityScore} label="Subtitle quality" showValue />
          </div>

          <div class="flex flex-wrap items-center gap-x-4 gap-y-1 text-mono-sm text-text-disabled">
            <span>{numberValue(candidate.downloadCount).toLocaleString()} downloads</span>
            {#if candidate.rating !== null}
              <span>Rating {numberValue(candidate.rating).toFixed(1)}</span>
            {/if}
            {#if candidate.pageUrl}
              <a
                href={candidate.pageUrl}
                target="_blank"
                rel="noreferrer"
                class="inline-flex items-center gap-1 text-text-accent hover:text-text-accent-bright"
              >
                Provider details
                <ExternalLink class="h-3 w-3" />
              </a>
            {/if}
          </div>
        </article>
      {/each}
    </div>
  {/if}
</div>
