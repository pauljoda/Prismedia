// Minimal mock of $app/state for vitest. The real one is reactive via
// SvelteKit's runtime; tests don't need that machinery — they just need
// `page.url` to be readable so detail pages can read `?tab=` and the
// like.
export const page = {
  url: new URL("http://localhost/"),
  params: {} as Record<string, string>,
  state: {} as Record<string, unknown>,
  status: 200,
  error: null,
  data: {} as Record<string, unknown>,
  form: null,
};

export const navigating = null;
export const updated = { current: false, check: async () => false };
