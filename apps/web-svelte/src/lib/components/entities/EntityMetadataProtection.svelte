<script lang="ts">
  import { Button, DialogBase } from "@prismedia/ui-svelte";
  import { LockKeyhole, LockKeyholeOpen, Shield } from "@lucide/svelte";
  import EntityActionButton from "./EntityActionButton.svelte";
  import { getEntityMetadataFields, setEntityMetadataFieldLock } from "$lib/api/generated/prismedia";
  import type { MetadataFieldResponse } from "$lib/api/generated/model";
  import { METADATA_PATCH_FIELD, METADATA_VALUE_ORIGIN } from "$lib/api/generated/codes";
  import { unwrapGenerated } from "$lib/api/generated-response";

  let { entityId }: { entityId: string } = $props();
  let open = $state(false);
  let fields = $state<MetadataFieldResponse[]>([]);
  let loading = $state(false);
  let saving = $state(false);
  let error = $state<string | null>(null);
  const labels: Partial<Record<MetadataFieldResponse["field"], string>> = {
    [METADATA_PATCH_FIELD.title]: "Title",
    [METADATA_PATCH_FIELD.description]: "Description",
    [METADATA_PATCH_FIELD.classification]: "Classification",
  };

  async function load(id: string) {
    const response = await getEntityMetadataFields(id);
    if (id === entityId) fields = unwrapGenerated(response, "Could not load metadata protection");
  }

  async function show() {
    open = true;
    loading = true;
    error = null;
    fields = [];
    try { await load(entityId); }
    catch (cause) { error = cause instanceof Error ? cause.message : "Could not load metadata protection"; }
    finally { loading = false; }
  }

  async function toggle(field: MetadataFieldResponse) {
    const id = entityId;
    saving = true;
    error = null;
    try {
      const response = await setEntityMetadataFieldLock(id, field.field, { expectedRevision: field.revision, isLocked: !field.isLocked });
      if (id !== entityId) return;
      if (response.status === 409) {
        await load(id);
        error = "This field changed while you were viewing it. Review its current source and try again.";
        return;
      }
      const updated = unwrapGenerated<MetadataFieldResponse>(response, "Could not change metadata protection");
      fields = fields.map(current => current.field === updated.field ? updated : current);
    } catch (cause) { error = cause instanceof Error ? cause.message : "Could not change metadata protection"; }
    finally { saving = false; }
  }

  function source(field: MetadataFieldResponse): string {
    if (field.origin === METADATA_VALUE_ORIGIN.user) return "Manual edit";
    if (field.origin === METADATA_VALUE_ORIGIN.provider) return field.providerId ?? "Metadata provider";
    return "Source unknown";
  }
</script>

<EntityActionButton label="Protection" ariaLabel="Metadata protection" icon={Shield} onClick={show} />
<DialogBase.Root bind:open>
  <DialogBase.Content>
    <DialogBase.Header>
      <DialogBase.Title>Metadata protection</DialogBase.Title>
      <DialogBase.Description>Locks protect these fields from metadata providers. Manual edits lock automatically. Unlocking keeps the current value and allows future enrichment.</DialogBase.Description>
    </DialogBase.Header>
    {#if error}<p role="alert" class="text-sm text-error-text">{error}</p>{/if}
    {#if loading}<p role="status" class="text-sm text-muted-foreground">Loading field sources…</p>{/if}
    <div class="divide-y divide-border-subtle">
      {#each fields as field (field.field)}
        <div class="flex items-start justify-between gap-3 py-4">
          <div class="min-w-0 space-y-1">
            <h3 class="text-sm font-medium">{labels[field.field] ?? field.field}</h3>
            <p class="break-words text-sm text-muted-foreground">{source(field)}{field.isCleared ? " · Cleared" : ""}</p>
            {#if field.observedAt}<p class="text-xs text-muted-foreground">{new Date(field.observedAt).toLocaleString()}</p>{/if}
            {#if field.confidence != null}<p class="text-xs text-muted-foreground">Provider confidence: {Math.round(Number(field.confidence) * 100)}%</p>{/if}
          </div>
          <Button type="button" variant="outline" class="shrink-0" disabled={saving} aria-label={`${field.isLocked ? "Unlock" : "Lock"} ${labels[field.field] ?? field.field}`} onclick={() => toggle(field)}>
            {#if field.isLocked}<LockKeyhole aria-hidden="true" />{:else}<LockKeyholeOpen aria-hidden="true" />{/if}
            {field.isLocked ? "Unlock" : "Lock"}
          </Button>
        </div>
      {/each}
    </div>
    <DialogBase.Footer>
      <Button type="button" variant="outline" onclick={() => { open = false; }}>Done</Button>
    </DialogBase.Footer>
  </DialogBase.Content>
</DialogBase.Root>
