<script lang="ts">
  import { Button, Select } from "@prismedia/ui-svelte";
  import type { AcquisitionRulePresetView } from "$lib/api/generated/model";
  interface Props {
    languages: string[];
    presets: AcquisitionRulePresetView[];
    onchange: (languages: string[]) => void;
  }
  let { languages, presets, onchange }: Props = $props();
  let selection = $state("");
  const options = $derived(presets.filter((preset) => preset.audioLanguage).map((preset) => ({
    value: preset.audioLanguage!, label: preset.name.replace(/ audio$/, ""),
  })));
  function move(index: number, direction: number) {
    const next = [...languages];
    [next[index], next[index + direction]] = [next[index + direction], next[index]];
    onchange(next);
  }
</script>
<div class="space-y-2">
  <span class="text-label text-text-secondary">Preferred audio languages</span>
  <p class="text-xs leading-relaxed text-text-muted">Named audio wins in this order. Unspecified MULTI is a fallback, followed by unmarked releases. Subtitle labels never confirm audio. Leave this empty to control ranking entirely with weighted rules.</p>
  {#each languages as language, index (index)}
    <div class="flex items-center gap-2 rounded-sm border border-border-subtle px-3 py-1.5">
      <span class="font-mono text-xs text-text-muted">{index + 1}</span>
      <span class="flex-1 text-sm text-text-primary">{options.find((option) => option.value.toLowerCase() === language.toLowerCase())?.label ?? language}</span>
      <Button size="sm" variant="ghost" disabled={index === 0} aria-label={`Move ${language} earlier`} onclick={() => move(index, -1)}>Earlier</Button>
      <Button size="sm" variant="ghost" disabled={index === languages.length - 1} aria-label={`Move ${language} later`} onclick={() => move(index, 1)}>Later</Button>
      <Button size="sm" variant="ghost" aria-label={`Remove ${language}`} onclick={() => onchange(languages.filter((_, i) => i !== index))}>Remove</Button>
    </div>
  {/each}
  <div class="flex items-center gap-2">
    <div class="min-w-0 flex-1"><Select size="sm" ariaLabel="Audio language to add" value={selection} options={[{ value: "", label: "Choose an audio language…" }, ...options]} onchange={(value) => (selection = value)} /></div>
    <Button size="sm" variant="secondary" disabled={!selection || languages.some((language) => language.toLowerCase() === selection.toLowerCase())} onclick={() => { onchange([...languages, selection]); selection = ""; }}>Add language</Button>
  </div>
</div>
