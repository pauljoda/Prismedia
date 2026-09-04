import { type ClassValue, clsx } from "clsx";
import { extendTailwindMerge } from "tailwind-merge";

// Keep semantic tokens in the same merge groups as Tailwind's numeric scale.
const twMerge = extendTailwindMerge({
  extend: {
    theme: {
      text: ["control", "label", "caption"],
      spacing: [
        "control-xs", "control-sm", "control", "control-lg",
        "control-gap", "control-gap-sm", "control-pad", "control-pad-lg",
        "icon-sm", "icon", "icon-lg", "badge",
      ],
    },
  },
});

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

/** Prop helpers used by the locally owned shadcn-svelte base components. */
export type WithoutChild<T> = Omit<T, "child">;
export type WithoutChildren<T> = Omit<T, "children">;
export type WithoutChildrenOrChild<T> = Omit<T, "children" | "child">;
export type WithElementRef<T, E extends HTMLElement = HTMLElement> = T & { ref?: E | null };
