<script lang="ts">
  import { cn } from "@prismedia/ui-svelte";

  interface Props {
    label: string;
    value: string;
    accent?: boolean;
    gradientClass?: string;
  }

  let { label, value, accent = false, gradientClass }: Props = $props();
</script>

<div
  class={cn(
    "surface-panel relative overflow-hidden px-3 py-2.5 flex flex-col justify-between min-h-[72px]",
    accent && "border-border-accent shadow-[var(--shadow-glow-accent)]",
  )}
>
  <div
    class={cn(
      "absolute left-0 top-0 bottom-0 w-[3px] opacity-90",
      gradientClass || (accent ? "bg-accent-500" : "bg-surface-4"),
    )}
  ></div>
  <div class="flex items-center justify-between ml-1.5">
    <span class="text-[0.6rem] font-semibold tracking-[0.15em] uppercase text-text-muted">
      {label}
    </span>
  </div>
  <div
    class={cn(
      "ml-1.5 mt-1 text-lg font-mono tracking-tight",
      accent ? "text-glow-accent" : "text-text-primary",
    )}
  >
    {value}
  </div>
</div>
