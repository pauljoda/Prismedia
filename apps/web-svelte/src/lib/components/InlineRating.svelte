<script lang="ts">
  import StarRatingPicker from "./StarRatingPicker.svelte";

  interface Props {
    value: number | null;
    onSave: (value: number | null) => Promise<void> | void;
    ariaLabelPrefix?: string;
    /** Disable interaction (renders as a read-only display). */
    readOnly?: boolean;
  }

  let { value, onSave, ariaLabelPrefix, readOnly = false }: Props = $props();

  let pending = $state<number | null>(null);
  let pendingActive = $state(false);

  const display = $derived(pendingActive ? pending : value);

  async function handle(next: number | null) {
    if (readOnly) return;
    pending = next;
    pendingActive = true;
    try {
      await onSave(next);
    } catch {
      // swallow — pendingActive clears below; UI reverts to parent `value`
    } finally {
      pendingActive = false;
    }
  }
</script>

<StarRatingPicker
  value={display}
  onChange={readOnly ? undefined : handle}
  {readOnly}
  {ariaLabelPrefix}
/>
