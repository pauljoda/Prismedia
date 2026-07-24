<script lang="ts">
  import { Plus, Save, X } from "@lucide/svelte";
  import { Button, TextInput } from "@prismedia/ui-svelte";
  import type {
    SettingDescriptor,
    SettingValue,
  } from "$lib/api/settings";
  import { valueAsSubtitlePreferenceTerms } from "$lib/settings/app-settings";
  import type { SubtitlePreferenceTerm } from "$lib/player/subtitle-types";

  interface Props {
    setting: SettingDescriptor;
    onSave: (key: string, value: SettingValue) => Promise<boolean>;
  }

  let { setting, onSave }: Props = $props();

  let terms = $derived<SubtitlePreferenceTerm[]>(
    valueAsSubtitlePreferenceTerms(setting.value, []),
  );
  let newTerm = $state("");
  let newWeight = $state("100");
  let isSaving = $state(false);
  let errorMessage = $state<string | null>(null);

  const minimumWeight = $derived(setting.constraints?.min ?? 1);
  const maximumWeight = $derived(setting.constraints?.max ?? 100);
  const maximumTerms = $derived(setting.constraints?.maxItems ?? 32);
  const normalizedNewTerm = $derived(newTerm.trim());
  const parsedNewWeight = $derived(parseWeight(newWeight));
  const canAdd = $derived(
    normalizedNewTerm.length > 0
      && terms.length < maximumTerms
      && parsedNewWeight !== null
      && !terms.some((item) =>
        item.term.localeCompare(normalizedNewTerm, undefined, { sensitivity: "accent" }) === 0),
  );
  const savedTerms = $derived(valueAsSubtitlePreferenceTerms(setting.value, []));
  const hasChanges = $derived(JSON.stringify(terms) !== JSON.stringify(savedTerms));

  function parseWeight(value: string): number | null {
    const parsed = Number(value);
    if (!Number.isInteger(parsed)) return null;
    if (parsed < minimumWeight || parsed > maximumWeight) return null;
    return parsed;
  }

  function addTerm() {
    if (!canAdd || parsedNewWeight === null) return;
    terms = [...terms, { term: normalizedNewTerm, weight: parsedNewWeight }];
    newTerm = "";
    newWeight = "100";
    errorMessage = null;
  }

  function removeTerm(term: string) {
    terms = terms.filter((item) => item.term !== term);
    errorMessage = null;
  }

  function updateWeight(term: string, value: string) {
    const weight = parseWeight(value);
    if (weight === null) return;
    terms = terms.map((item) => item.term === term ? { ...item, weight } : item);
  }

  async function save() {
    if (!hasChanges || isSaving) return;
    isSaving = true;
    errorMessage = null;
    try {
      if (!(await onSave(setting.key, terms))) {
        errorMessage = "Prismedia could not save these preference terms.";
      }
    } finally {
      isSaving = false;
    }
  }
</script>

<div class="py-3">
  <div>
    <div class="text-[0.82rem] font-medium text-text-primary">{setting.label}</div>
    <p class="mt-0.5 text-[0.68rem] leading-relaxed text-text-muted">{setting.description}</p>
    <p class="mt-2 border-l-2 border-border-accent bg-surface-2/45 px-3 py-2 text-[0.68rem] leading-relaxed text-text-secondary">
      Every term is checked separately against the track language and label, ignoring case. Matching
      weights add together, so “English Forced” can score for Forced, English, and Eng while a plain
      English track still scores for its language terms.
    </p>
  </div>

  <div class="mt-3 overflow-hidden rounded-xs border border-border-subtle bg-surface-1/60">
    {#if terms.length === 0}
      <p class="px-3 py-4 text-center text-[0.7rem] text-text-muted">
        No terms yet. With no terms configured, the first available subtitle is selected.
      </p>
    {:else}
      <div class="divide-y divide-border-subtle">
        {#each terms as item (item.term)}
          <div class="grid grid-cols-[minmax(0,1fr)_5.5rem_auto] items-center gap-2 px-3 py-2">
            <span class="truncate text-[0.76rem] font-medium text-text-primary">{item.term}</span>
            <TextInput
              type="number"
              min={minimumWeight}
              max={maximumWeight}
              step="1"
              size="sm"
              value={String(item.weight)}
              oninput={(event) => updateWeight(item.term, event.currentTarget.value)}
              aria-label={`Weight for ${item.term}`}
              class="text-center font-mono"
            />
            <Button
              type="button"
              variant="ghost"
              size="icon"
              onclick={() => removeTerm(item.term)}
              aria-label={`Remove ${item.term}`}
              title={`Remove ${item.term}`}
              class="text-text-muted hover:text-status-error-text"
            >
              <X class="h-3.5 w-3.5" />
            </Button>
          </div>
        {/each}
      </div>
    {/if}
  </div>

  <div class="mt-3 grid gap-2 sm:grid-cols-[minmax(0,1fr)_6rem_auto]">
    <TextInput
      size="sm"
      value={newTerm}
      oninput={(event) => (newTerm = event.currentTarget.value)}
      onkeydown={(event) => {
        if (event.key === "Enter") {
          event.preventDefault();
          addTerm();
        }
      }}
      placeholder="Forced"
      aria-label="New preference term"
    />
    <TextInput
      type="number"
      min={minimumWeight}
      max={maximumWeight}
      step="1"
      size="sm"
      value={newWeight}
      oninput={(event) => (newWeight = event.currentTarget.value)}
      aria-label="New term weight"
      class="text-center font-mono"
    />
    <Button
      type="button"
      variant="secondary"
      size="sm"
      disabled={!canAdd}
      onclick={addTerm}
      class="no-lift gap-1.5"
    >
      <Plus class="h-3.5 w-3.5" />
      Add term
    </Button>
  </div>

  <div class="mt-3 flex flex-wrap items-center justify-between gap-2">
    <p class="text-[0.64rem] text-text-muted">
      Weights range from {minimumWeight} to {maximumWeight}. Highest total wins; ties keep track order.
    </p>
    <Button
      type="button"
      variant="primary"
      size="sm"
      disabled={!hasChanges || isSaving}
      onclick={() => void save()}
      class="no-lift gap-1.5"
    >
      <Save class="h-3.5 w-3.5" />
      {isSaving ? "Saving…" : "Save preference terms"}
    </Button>
  </div>

  {#if errorMessage}
    <p role="alert" class="mt-2 text-[0.68rem] text-status-error-text">{errorMessage}</p>
  {/if}
</div>
