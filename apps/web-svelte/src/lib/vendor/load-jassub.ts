export async function loadJassub() {
  const url = "/jassub/jassub-client.js";
  const mod = await import(/* @vite-ignore */ url);
  return mod.default as new (opts: Record<string, unknown>) => {
    destroy?: () => Promise<void> | void;
  };
}
