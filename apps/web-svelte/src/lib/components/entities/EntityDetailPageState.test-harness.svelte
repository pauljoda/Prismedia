<script lang="ts">
  import EntityDetailPageState, { type EntityDetailPageLoadState } from "./EntityDetailPageState.svelte";

  interface Props {
    errorMessage?: string | null;
    fallbackError?: string;
    loadState: EntityDetailPageLoadState;
    onRetry?: () => void;
  }

  let {
    errorMessage = null,
    fallbackError = "Failed to load entity.",
    loadState,
    onRetry = () => undefined,
  }: Props = $props();
</script>

<EntityDetailPageState {errorMessage} {fallbackError} {loadState} {onRetry}>
  <p>Ready detail</p>
</EntityDetailPageState>
