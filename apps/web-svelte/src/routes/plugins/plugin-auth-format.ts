import type { PluginAuthField } from "$lib/api/generated/model";

export function authLinkLabel(field: Pick<PluginAuthField, "key">): string {
  const key = field.key.toLowerCase();
  if (key.includes("username") || key.includes("password")) return "Open login";
  if (key.includes("client_id") || key.includes("client_secret")) return "Open settings";
  return "Get key";
}
