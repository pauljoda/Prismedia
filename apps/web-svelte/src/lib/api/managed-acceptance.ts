import { ApiError } from "$lib/api/orval-fetch";
import { problemMessage, unwrapGenerated } from "$lib/api/generated-response";

const refusalStatuses = [400, 401, 403, 404, 409];

/** Distinguishes definite acceptance refusals from response loss that must retain the caller's operation ID. */
export async function acceptManagedIntent<T>(request: Promise<{ data: unknown; status: number }>,
  rejected: (message: string) => Error, uncertainMessage: string): Promise<T> {
  try {
    const response = await request;
    if (refusalStatuses.includes(response.status)) throw rejected(problemMessage(response.data) ?? "The request was not accepted");
    return unwrapGenerated(response, uncertainMessage, [202]);
  } catch (error) {
    // The generated fetch transport throws ApiError before returning a non-success response.
    if (error instanceof ApiError && refusalStatuses.includes(error.status)) throw rejected(error.message);
    throw error;
  }
}
