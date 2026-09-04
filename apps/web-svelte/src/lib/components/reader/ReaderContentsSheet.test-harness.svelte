<script lang="ts">
  import { Button } from "@prismedia/ui-svelte";
  import ReaderShell from "./ReaderShell.svelte";
  import ReaderContentsSheet from "./ReaderContentsSheet.svelte";
  let { onClose, onNext }: { onClose: () => void; onNext: () => void } = $props();
  let contentsOpen = $state(false);
</script>

<ReaderShell title="Test reader" {onClose} {onNext}>
  {#snippet controls()}
    <Button onclick={() => contentsOpen = true}>Open contents</Button>
  {/snippet}
  <p>Current page</p>
</ReaderShell>
<ReaderContentsSheet open={contentsOpen} onClose={() => contentsOpen = false}>
  <Button>Chapter one</Button>
</ReaderContentsSheet>
