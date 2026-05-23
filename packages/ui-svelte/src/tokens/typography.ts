export const typography = {
  fonts: {
    heading: "var(--font-geist), 'Inter', sans-serif",
    body: "var(--font-inter), 'Helvetica Neue', sans-serif",
    mono: "var(--font-jetbrains-mono), 'Menlo', monospace",
  },
  scale: {
    display: { size: "2.5rem", weight: 700, lineHeight: 0.92, letterSpacing: "-0.04em" },
    h1: { size: "1.75rem", weight: 600, lineHeight: 0.95, letterSpacing: "-0.03em" },
    h2: { size: "1.25rem", weight: 600, lineHeight: 1.05, letterSpacing: "-0.02em" },
    h3: { size: "1.05rem", weight: 600, lineHeight: 1.15, letterSpacing: "-0.015em" },
    h4: { size: "0.875rem", weight: 600, lineHeight: 1.2, letterSpacing: "-0.01em" },
    bodyLg: { size: "1rem", weight: 400, lineHeight: 1.6, letterSpacing: "0" },
    body: { size: "0.875rem", weight: 400, lineHeight: 1.55, letterSpacing: "0" },
    bodySm: { size: "0.8rem", weight: 400, lineHeight: 1.5, letterSpacing: "0" },
    label: { size: "0.75rem", weight: 500, lineHeight: 1.3, letterSpacing: "0.04em" },
    kicker: { size: "0.68rem", weight: 600, lineHeight: 1.3, letterSpacing: "0.1em" },
    mono: { size: "0.8rem", weight: 400, lineHeight: 1.45, letterSpacing: "0" },
    monoSm: { size: "0.72rem", weight: 400, lineHeight: 1.4, letterSpacing: "0" },
  },
} as const;

export type Typography = typeof typography;
