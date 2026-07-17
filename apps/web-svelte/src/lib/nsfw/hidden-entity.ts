import { goto } from "$app/navigation";
import { PROBLEM_CODE } from "$lib/api/generated/codes";
import { ApiError } from "$lib/api/orval-fetch";
import type { NsfwMode } from "./cookie";

/**
 * Whether an error is the API's entity-not-found problem, matched by the generated
 * problem code — never by message text. Non-ApiError values fall back to looking for
 * the code token inside wrapped/stringified errors.
 */
export function isHiddenEntityNotFoundError(error: unknown): boolean {
  if (error instanceof ApiError) {
    return error.problemCode === PROBLEM_CODE.entityNotFound;
  }

  const message = error instanceof Error ? error.message : String(error ?? "");
  return message.includes(PROBLEM_CODE.entityNotFound);
}

export function redirectHiddenEntityNotFound(error: unknown, mode: NsfwMode): boolean {
  if (mode !== "off" || !isHiddenEntityNotFoundError(error)) {
    return false;
  }

  void goto("/", { replaceState: true });
  return true;
}
