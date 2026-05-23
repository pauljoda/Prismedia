/**
 * Build a URL query string from scalar and list parameters.
 *
 * Scalar values that are `null`, `undefined`, or empty-string are omitted.
 * List values are appended (not set) so they appear as repeated keys.
 */
export function buildQueryString(
  params: Record<string, string | number | null | undefined>,
  listParams?: Record<string, string[] | undefined>,
): string {
  const sp = new URLSearchParams();

  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== "") {
      sp.set(key, String(value));
    }
  }

  if (listParams) {
    for (const [key, values] of Object.entries(listParams)) {
      values?.forEach((value) => sp.append(key, value));
    }
  }

  const qs = sp.toString();
  return qs ? `?${qs}` : "";
}
