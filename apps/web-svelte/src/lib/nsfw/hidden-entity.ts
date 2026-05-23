import { goto } from "$app/navigation";
import type { NsfwMode } from "./cookie";

const ENTITY_NOT_FOUND_CODE = "entity_not_found";

export function isHiddenEntityNotFoundError(error: unknown): boolean {
  const message = error instanceof Error ? error.message : String(error ?? "");
  return message.includes(ENTITY_NOT_FOUND_CODE) ||
    /^Entity '[^']+' was not found\.$/.test(message.trim());
}

export function redirectHiddenEntityNotFound(error: unknown, mode: NsfwMode): boolean {
  if (mode !== "off" || !isHiddenEntityNotFoundError(error)) {
    return false;
  }

  void goto("/", { replaceState: true });
  return true;
}
