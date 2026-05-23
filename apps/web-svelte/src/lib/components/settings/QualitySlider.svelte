<script lang="ts">
  import { cn } from "@prismedia/ui-svelte";

  interface Props {
    label: string;
    value: number;
    class?: string;
    onCommit: (value: number) => void;
  }

  let { label, value, class: className, onCommit }: Props = $props();

  const inputId = $derived(
    `quality-slider-${label.toLowerCase().replace(/[^a-z0-9]+/g, "-")}`,
  );

  let draft = $state(1);

  $effect(() => {
    draft = value;
  });

  function qualityLabel(v: number): string {
    if (v <= 1) return "Native";
    if (v <= 2) return "High";
    if (v <= 5) return "Good";
    if (v <= 10) return "Medium";
    if (v <= 15) return "Low";
    if (v <= 20) return "Very Low";
    return "Minimum";
  }

  function commit() {
    if (draft !== value) onCommit(draft);
  }
</script>

<div
  class={cn(
    "surface-card no-lift flex h-full min-h-[104px] flex-col justify-between p-3.5",
    className,
  )}
>
  <div class="mb-4 flex items-start justify-between">
    <div>
      <label class="control-label" for={inputId}>{label}</label>
      <p class="text-[0.65rem] text-text-muted mt-1">1 is native, 31 is smallest</p>
    </div>
    <span
      class="text-mono-sm px-2 py-0.5 bg-surface-1 border border-border-subtle text-text-accent shadow-well"
    >
      {qualityLabel(draft)} ({draft})
    </span>
  </div>
  <div class="relative pt-2 pb-1">
    <div
      class="absolute left-0 right-0 top-1/2 -translate-y-1/2 h-1.5 bg-surface-4 border border-border-subtle shadow-well"
    ></div>
    <div
      class="absolute left-0 top-1/2 -translate-y-1/2 h-1.5 bg-gradient-to-r from-accent-700 to-accent-500 shadow-[0_0_8px_rgba(199,155,92,0.3)]"
      style:width="{((draft - 1) / 30) * 100}%"
    ></div>
    <input
      id={inputId}
      type="range"
      min="1"
      max="31"
      step="1"
      bind:value={draft}
      onmouseup={commit}
      ontouchend={commit}
      onkeyup={commit}
      class="relative w-full h-1.5 appearance-none bg-transparent cursor-pointer [&::-webkit-slider-thumb]:appearance-none [&::-webkit-slider-thumb]:h-4 [&::-webkit-slider-thumb]:w-2.5 [&::-webkit-slider-thumb]:bg-surface-2 [&::-webkit-slider-thumb]:border [&::-webkit-slider-thumb]:border-border-accent [&::-webkit-slider-thumb]:shadow-[0_0_10px_rgba(0,0,0,0.8)] z-10"
      aria-label={label}
    />
  </div>
</div>
