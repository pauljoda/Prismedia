<script lang="ts">
  import type { Component } from "svelte";

  interface Props {
    icon: Component<Record<string, unknown>>;
    label: string;
    value: string;
  }

  let { icon: Icon, label, value }: Props = $props();
</script>

<div class="flex items-center justify-between">
  <span class="flex items-center gap-2 text-text-muted text-xs">
    <Icon class="h-3.5 w-3.5" />
    {label}
  </span>
  <span class="text-mono-sm">{value}</span>
</div>
