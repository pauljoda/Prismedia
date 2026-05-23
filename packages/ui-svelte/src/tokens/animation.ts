export const animation = {
  easing: {
    default: "cubic-bezier(0.4, 0, 0.2, 1)",
    enter: "cubic-bezier(0, 0, 0.2, 1)",
    exit: "cubic-bezier(0.4, 0, 1, 1)",
    mechanical: "cubic-bezier(0.25, 0, 0.25, 1)",
  },
  duration: {
    fast: "80ms",
    normal: "160ms",
    moderate: "240ms",
    slow: "380ms",
  },
  keyframes: {
    /**
     * glowPulse — selected items, active indicators, processing state.
     * Apply: animation: glow-pulse 2.4s ease-in-out infinite
     */
    glowPulse: {
      "0%, 100%": {
        boxShadow:
          "0 0 0 1px rgba(242,194,106,0.50), 0 0 10px rgba(242,194,106,0.20)",
      },
      "50%": {
        boxShadow:
          "0 0 0 1px rgba(242,194,106,0.80), 0 0 20px rgba(242,194,106,0.40), 0 0 40px rgba(242,194,106,0.15)",
      },
    },
    /**
     * fadeIn — panel and overlay entrance.
     * Apply: animation: fade-in 240ms ease-enter
     */
    fadeIn: {
      from: { opacity: "0", transform: "scale(0.97)" },
      to: { opacity: "1", transform: "scale(1)" },
    },
    /**
     * slideUp — mobile sheet and bottom-drawer entrance.
     * Apply: animation: slide-up 280ms ease-mechanical
     */
    slideUp: {
      from: { transform: "translateY(100%)" },
      to: { transform: "translateY(0)" },
    },
    /**
     * ledPulse — status indicator in processing/loading state.
     * Apply: animation: led-pulse 1.6s ease-in-out infinite
     */
    ledPulse: {
      "0%, 100%": { opacity: "1" },
      "50%": { opacity: "0.35" },
    },
  },
} as const;

export type Animation = typeof animation;
