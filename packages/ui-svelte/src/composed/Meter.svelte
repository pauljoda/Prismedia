<script lang="ts">
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

  const pct = $derived(Math.min(100, Math.max(0, (value / max) * 100)));
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
  <div class="meter-track">
    <div
      class={variant === "phosphor" ? "meter-fill-phosphor" : "meter-fill"}
      style:width="{pct}%"
    ></div>
  </div>
</div>
