<script lang="ts">
  import { goto } from "$app/navigation";
  import { ScanSearch } from "@lucide/svelte";

  interface Props {
    entityId: string;
    entityKind?: string;
    title?: string;
    label?: string;
    class?: string;
  }

  let { entityId, label = "Identify", class: className }: Props = $props();

  function navigate() {
    void goto(`/identify/${entityId}?returnId=${entityId}`);
  }
</script>

<button
  type="button"
  class={[
    "inline-flex min-h-[2.1rem] items-center justify-center gap-[0.45rem] rounded-xs border border-accent-500/55 bg-surface-2 px-[0.65rem] text-[0.76rem] text-text-primary shadow-[0_0_14px_rgba(196,154,90,0.12)] transition-all hover:border-accent-500/80 hover:shadow-[0_0_20px_rgba(196,154,90,0.22)]",
    className,
  ]}
  onclick={navigate}
>
  <ScanSearch class="h-4 w-4" />
  <span>{label}</span>
</button>
