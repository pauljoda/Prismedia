<script lang="ts">
  import Progress from "../components/ui/progress/progress.svelte";
  import { cn } from "../lib/utils";

  interface Props {
    value: number;
    max?: number;
    label?: string;
    showValue?: boolean;
    variant?: "accent" | "phosphor";
    class?: string;
  }

  let {
    value,
    max = 100,
    label,
    showValue = false,
    variant = "accent",
    class: className,
  }: Props = $props();

  const pct = $derived(max > 0 && Number.isFinite(value) ? Math.min(100, Math.max(0, (value / max) * 100)) : 0);
</script>

<div class={cn("flex flex-col gap-1", className)}>
  {#if label || showValue}
    <div class="flex items-center justify-between">
      {#if label}
        <span class="text-label text-text-muted">{label}</span>
      {/if}
      {#if showValue}
        <span
          class={cn(
            "text-mono-sm",
            variant === "phosphor" ? "text-phosphor-400 text-glow-phosphor" : "text-text-muted",
          )}
        >
          {Math.round(pct)}%
        </span>
      {/if}
    </div>
  {/if}
  <Progress value={pct} aria-label={label ?? "Progress"} class="h-1.5 rounded-xs" />
</div>
