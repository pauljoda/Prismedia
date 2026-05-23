export const colors = {
  bg: "#07080b",
  surface: {
    1: "#0b0e12",
    2: "#11161d",
    3: "#202734",
    4: "#2a3038",
  },
  glass: {
    1: "rgba(11, 14, 18, 0.72)",
    2: "rgba(16, 20, 32, 0.82)",
    3: "rgba(21, 26, 40, 0.92)",
  },
  glassBlur: {
    1: "12px",
    2: "16px",
    3: "24px",
  },
  text: {
    primary: "#f0ede3",
    secondary: "#c8ccd4",
    muted: "#a4acb9",
    disabled: "#5a6070",
    accent: "#f2c26a",
    accentBright: "#f5d48a",
  },
  accent: {
    950: "#1a1408",
    900: "#2d2210",
    800: "#4a3818",
    700: "#7a5e20",
    600: "#d59a2a",
    500: "#f2c26a",
    400: "#f5d48a",
    300: "#f7dfa0",
    200: "#faecc0",
    100: "#fdf5e0",
    50: "#fefaf0",
  },
  accentGradient: {
    selection: "linear-gradient(135deg, #f2c26a 0%, #f7dfa0 100%)",
    active: "linear-gradient(135deg, #d59a2a 0%, #f2c26a 100%)",
    subtle: "linear-gradient(180deg, rgba(242,194,106,0.12) 0%, rgba(242,194,106,0) 100%)",
  },
  glow: {
    subtle: "0 0 0 1px rgba(242,194,106,0.35), 0 0 8px rgba(242,194,106,0.15)",
    full: "0 0 0 1px rgba(242,194,106,0.60), 0 0 16px rgba(242,194,106,0.30), 0 0 32px rgba(242,194,106,0.10)",
  },
  status: {
    success: { default: "#63c889", muted: "#1a3d28", text: "#8ee0aa", glow: "rgba(99, 200, 137, 0.30)" },
    warning: { default: "#f2c26a", muted: "#3d3010", text: "#f5d48a", glow: "rgba(242, 194, 106, 0.30)" },
    error:   { default: "#ff806f", muted: "#4a1c18", text: "#ff9f92", glow: "rgba(255, 128, 111, 0.30)" },
    info:    { default: "#6fa8dc", muted: "#1a2e44", text: "#92c0e8", glow: "rgba(111, 168, 220, 0.30)" },
  },
  border: {
    subtle: "rgba(164, 172, 185, 0.07)",
    default: "rgba(164, 172, 185, 0.12)",
    accent: "rgba(242, 194, 106, 0.24)",
    accentStrong: "rgba(242, 194, 106, 0.52)",
    glow: "rgba(242, 194, 106, 0.80)",
  },
  overlay: {
    scrim: "rgba(7, 8, 11, 0.75)",
    heavy: "rgba(7, 8, 11, 0.9)",
  },
} as const;

export type Colors = typeof colors;
