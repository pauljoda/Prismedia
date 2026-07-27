<script lang="ts">
  import { Select, TextInput } from "@prismedia/ui-svelte";
  import type { BookAcquisitionProfileSaveRequest } from "$lib/api/generated/model";
  import { releaseTimingOptionsFor } from "$lib/components/settings/acquisition-profile-release-timing";

  let { profile }: { profile: BookAcquisitionProfileSaveRequest } = $props();

  function setMilestone(value: string): void {
    profile.searchAfterDateType = (value || undefined) as typeof profile.searchAfterDateType;
    if (!value) profile.searchDelayDays = 0;
  }
</script>

<div class="space-y-2 border-l-2 border-border-accent bg-surface-2/40 px-3 py-2.5 sm:col-span-2">
  <div class="grid gap-2 sm:grid-cols-2">
    <label class="space-y-1"><span class="text-label text-text-muted">Start automatic searches</span>
      <Select
        size="sm"
        value={profile.searchAfterDateType ?? ""}
        options={releaseTimingOptionsFor(profile.kind)}
        onchange={setMilestone}
      /></label>
    {#if profile.searchAfterDateType}
      <label class="space-y-1"><span class="text-label text-text-muted">Delay after release (days)</span>
        <TextInput
          size="sm"
          type="number"
          min="0"
          max="3650"
          value={String(profile.searchDelayDays ?? 0)}
          oninput={(event) => (profile.searchDelayDays = Math.max(0, Number(event.currentTarget.value) || 0))}
        /></label>
    {/if}
  </div>
  <p class="text-[0.72rem] leading-relaxed text-text-muted">
    Automatic indexer searches wait for the selected date supplied by metadata plugins. If the date is not known yet, Prismedia keeps refreshing it. A manual Search again always starts immediately.
  </p>
</div>
