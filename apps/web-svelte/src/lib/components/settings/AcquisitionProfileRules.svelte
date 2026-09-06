<script lang="ts">
  import { Button, TextInput } from "@prismedia/ui-svelte";
  import { CUSTOM_FORMAT_CONDITION_TYPE } from "$lib/api/generated/codes";
  import type { AcquisitionRulePresetView, BookAcquisitionProfileSaveRequest, CustomFormatSaveRequest, CustomFormatView } from "$lib/api/generated/model";
  import { saveCustomFormat } from "$lib/api/acquisitions";
  import AcquisitionRuleFields from "./AcquisitionRuleFields.svelte";

  interface Props {
    profileForm: BookAcquisitionProfileSaveRequest;
    customFormats: CustomFormatView[];
    rulePresets: AcquisitionRulePresetView[];
    busy: boolean;
    onError: (message: string) => void;
    onMessage: (message: string) => void;
  }
  let { profileForm = $bindable(), customFormats = $bindable(), rulePresets,
    busy = $bindable(), onError, onMessage }: Props = $props();
  // Custom formats matching the profile form's current kind — the scorable set for this profile.
  const formatsForKind = $derived.by(() => {
    const kind = profileForm?.kind;
    return kind ? customFormats.filter((f) => f.kind === kind) : [];
  });
  /** Sets (or clears, when blank) the per-format score on the open profile form. */
  function setFormatScore(id: string, raw: string) {
    if (!profileForm) return;
    profileForm.formatScores ??= {};
    const next = { ...profileForm.formatScores };
    if (raw.trim() === "") {
      delete next[id];
    } else {
      next[id] = Number(raw) || 0;
    }
    profileForm.formatScores = next;
  }
  let profileRuleForm = $state<CustomFormatSaveRequest | null>(null);
  let profileRuleScore = $state(100);
  function editProfileRule(format?: CustomFormatView) {
    if (!profileForm) return;
    profileRuleForm = format
      ? { id: format.id, kind: format.kind, name: format.name, conditions: format.conditions.map((condition) => ({ ...condition })) }
      : { id: null, kind: profileForm.kind, name: "", conditions: [{ type: CUSTOM_FORMAT_CONDITION_TYPE.releaseTitle, value: "", negate: false, required: true }] };
    profileRuleScore = format ? Number(profileForm.formatScores?.[format.id] ?? 0) : 100;
  }
  async function saveProfileRule() {
    if (!profileRuleForm || !profileForm) return;
    busy = true;
    try {
      const saved = await saveCustomFormat(profileRuleForm);
      customFormats = [...customFormats.filter((format) => format.id !== saved.id), saved];
      profileForm.formatScores = { ...profileForm.formatScores, [saved.id]: profileRuleScore };
      profileRuleForm = null;
      onMessage("Rule saved. Save this profile to apply its score.");
    } catch (error) {
      onError(error instanceof Error ? error.message : "Failed to save rule");
    } finally {
      busy = false;
    }
  }

</script>

<div class="space-y-3 border-t border-border-subtle pt-3">
  <div class="flex items-center justify-between gap-2">
    <span class="text-label text-text-secondary">Release preferences</span>
    <Button size="sm" variant="secondary" onclick={() => editProfileRule()} disabled={busy}>Add rule</Button>
  </div>
  <p class="text-xs leading-relaxed text-text-muted">Start with a language, codec or source preference, or create your own title rule. Explicit preferred audio languages above take priority over weighted rules.</p>
  {#if profileRuleForm}
    <div class="space-y-3 rounded-sm border border-border-subtle bg-surface-1 p-3">
      <label class="block space-y-1"><span class="text-label text-text-secondary">Rule name</span>
        <TextInput size="sm" value={profileRuleForm.name} oninput={(event) => profileRuleForm && (profileRuleForm.name = event.currentTarget.value)} /></label>
      {#key profileRuleForm.id}
        <AcquisitionRuleFields bind:form={profileRuleForm} presets={rulePresets} editing={!!profileRuleForm.id} onpreset={(preset) => (profileRuleScore = Number(preset.suggestedScore))} />
      {/key}
      <label class="block space-y-1"><span class="text-label text-text-secondary">Score in this profile</span>
        <TextInput size="sm" type="number" value={String(profileRuleScore)} oninput={(event) => (profileRuleScore = Number(event.currentTarget.value) || 0)} /></label>
      {#if profileRuleForm.id}<p class="text-xs text-text-muted">This rule's conditions are shared with every profile using it. Its score belongs to this profile.</p>{/if}
      <div class="flex justify-end gap-2">
        <Button size="sm" variant="ghost" onclick={() => (profileRuleForm = null)}>Cancel rule</Button>
        <Button size="sm" variant="primary" disabled={busy || !profileRuleForm.name || profileRuleForm.conditions.some((condition) => !condition.value.trim())} onclick={saveProfileRule}>Save rule</Button>
      </div>
    </div>
  {/if}
</div>
{#if formatsForKind.length > 0}
  <div class="space-y-2">
    <div class="space-y-0.5">
      <span class="text-label text-text-muted">Custom format scores</span>
      <p class="text-[0.72rem] leading-relaxed text-text-muted">100 points equals one preferred term. A score below the minimum rejects the release, including negative totals at a zero minimum.</p>
    </div>
    <div class="space-y-1.5">
      {#each formatsForKind as f (f.id)}
        <div class="flex items-center justify-between gap-2 rounded-sm border border-border-subtle bg-surface-1 px-3 py-1.5">
          <Button size="sm" variant="ghost" onclick={() => editProfileRule(f)} aria-label={`Edit ${f.name}`}>{f.name}</Button>
          <TextInput size="sm" type="number" class="w-24" value={String((profileForm.formatScores ?? {})[f.id] ?? 0)} oninput={(e) => setFormatScore(f.id, e.currentTarget.value)} placeholder="0" />
        </div>
      {/each}
    </div>
    <div class="grid gap-2 sm:grid-cols-2">
      <label class="space-y-1"><span class="text-label text-text-muted">Minimum format score<span class="ml-1 text-text-muted">— reject releases scoring below this</span></span>
        <TextInput size="sm" type="number" value={String(profileForm.minFormatScore)} oninput={(e) => profileForm && (profileForm.minFormatScore = Number(e.currentTarget.value) || 0)} /></label>
      <label class="space-y-1"><span class="text-label text-text-muted">Cutoff format score<span class="ml-1 text-text-muted">— keep upgrading until a release reaches this score</span></span>
        <TextInput size="sm" type="number" value={profileForm.cutoffFormatScore == null ? "" : String(profileForm.cutoffFormatScore)} oninput={(e) => profileForm && (profileForm.cutoffFormatScore = e.currentTarget.value ? Number(e.currentTarget.value) : null)} placeholder="none" /></label>
    </div>
  </div>
{/if}
