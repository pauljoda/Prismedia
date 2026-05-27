import type { InstalledPlugin } from "$lib/api/plugins";

export type PluginAuthFieldSummary = { key: string; label: string };

export function authFieldName(field: PluginAuthFieldSummary): string {
  const key = field.key.toLowerCase();
  if (key.includes("client_id")) return "client ID";
  if (key.includes("client_secret")) return "client secret";
  if (key.includes("username")) return "username";
  if (key.includes("password")) return "password";
  if (key.includes("api_key") || key.includes("apikey")) return "API key";
  if (key.includes("token")) return "token";
  return field.label.toLowerCase();
}

export function authPlaceholder(plugin: InstalledPlugin, field: PluginAuthFieldSummary): string {
  const name = authFieldName(field);
  if (plugin.authStatus === "ok") {
    return `Configured - enter new ${name} to replace`;
  }
  if (name === "username" || name === "password") {
    return `Enter your ${field.label}`;
  }
  return `Paste your ${field.label}`;
}

export function authLinkLabel(field: Pick<PluginAuthFieldSummary, "key">): string {
  const key = field.key.toLowerCase();
  if (key.includes("username") || key.includes("password")) return "Open login";
  if (key.includes("client_id") || key.includes("client_secret")) return "Open settings";
  return "Get key";
}
