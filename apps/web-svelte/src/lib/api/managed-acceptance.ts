import { ApiError } from "$lib/api/orval-fetch";
import { problemMessage, unwrapGenerated } from "$lib/api/generated-response";
import type { ApiProblem } from "$lib/api/generated/model";

const refusalStatuses = [400, 401, 403, 404, 409];

/** Distinguishes definite acceptance refusals from response loss that must retain the caller's operation ID. */
export async function acceptManagedIntent<T>(request: Promise<{ data: unknown; status: number }>,
  rejected: (message: string, problemCode?: string) => Error, uncertainMessage: string): Promise<T> {
  try {
    const response = await request;
    if (refusalStatuses.includes(response.status)) {
      throw rejected(problemMessage(response.data) ?? "The request was not accepted", apiProblemCode(response.data));
    }
    return unwrapGenerated(response, uncertainMessage, [202]);
  } catch (error) {
    // The generated fetch transport throws ApiError before returning a non-success response.
    if (error instanceof ApiError && refusalStatuses.includes(error.status)) {
      throw rejected(error.message, error.problemCode);
    }
    throw error;
  }
}

function apiProblemCode(data: unknown): ApiProblem["code"] | undefined {
  if (!data || typeof data !== "object") return undefined;
  const code = (data as Partial<ApiProblem>).code;
  return typeof code === "string" && code.trim() ? code : undefined;
}
