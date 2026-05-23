<script module lang="ts">
  export type LedStatus =
    | "active"
    | "warning"
    | "error"
    | "info"
    | "idle"
    | "accent"
    | "phosphor";
  export type LedSize = "sm" | "md" | "lg";
</script>

<script lang="ts">
  import { cn } from "../lib/utils";

  interface Props {
    status: LedStatus;
    size?: LedSize;
    pulse?: boolean;
    class?: string;
  }

  const sizeClasses: Record<LedSize, string> = {
    sm: "led-sm",
    md: "",
    lg: "led-lg",
  };

  const statusClasses: Record<LedStatus, string> = {
    active: "led-active",
    warning: "led-warning",
    error: "led-error",
    info: "led-info",
    idle: "led-idle",
    accent: "led-accent",
    phosphor: "led-phosphor",
  };

  let { status, size = "md", pulse = false, class: className }: Props = $props();
</script>

<span
  class={cn(
    "led",
    sizeClasses[size],
    statusClasses[status],
    pulse && "led-pulse",
    className,
  )}
  role="status"
  aria-label={`Status: ${status}`}
></span>
