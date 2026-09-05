<script lang="ts">
  import { Alert } from "@prismedia/ui-svelte";
  import FormActions from "$lib/components/forms/FormActions.svelte";

  interface Props {
    cancelLabel: string;
    errors: readonly string[];
    onCancel: () => void;
    onSave: () => void;
    saveDisabled: boolean;
    saveLabel: string;
    saving: boolean;
  }

  let {
    cancelLabel,
    errors,
    onCancel,
    onSave,
    saveDisabled,
    saveLabel,
    saving,
  }: Props = $props();
</script>

<div class="flex flex-col gap-3 border-t border-border-subtle px-6 py-4 sm:px-8">
  {#if errors.length > 0}
    <Alert.Root variant="destructive">
      <Alert.Title>Changes need attention</Alert.Title>
      <Alert.Description>
        {#each errors as error (error)}
          <p>{error}</p>
        {/each}
      </Alert.Description>
    </Alert.Root>
  {/if}
  <div class="w-full min-w-0">
    <FormActions {onSave} {onCancel} {saving} {saveDisabled} {saveLabel} {cancelLabel} />
  </div>
</div>
