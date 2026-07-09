import { PLUGIN_SEARCH_FIELD_TYPE } from "$lib/api/generated/codes";
import type { PluginSearchField } from "$lib/api/generated/model";

/**
 * Creates a complete value map for a plugin search form. Existing values survive schema refreshes;
 * otherwise the first text field receives the caller's natural title so both legacy `title` forms
 * and richer fields such as `seriesTitle` start from useful Entity context without teaching core UI
 * any plugin-specific field names.
 */
export function seedPluginSearchFields(
  fields: PluginSearchField[],
  existing: Record<string, string>,
  title: string,
): Record<string, string> {
  const firstTextKey = fields.find((field) => field.type === PLUGIN_SEARCH_FIELD_TYPE.text)?.key;
  return Object.fromEntries(
    fields.map((field) => [
      field.key,
      existing[field.key] ?? (field.key === firstTextKey ? title.trim() : ""),
    ]),
  );
}

/** Returns only non-empty plugin-owned values, trimmed but otherwise opaque. */
export function submittedPluginSearchFields(
  fields: PluginSearchField[],
  values: Record<string, string>,
): Record<string, string> {
  const allowed = new Set(fields.map((field) => field.key));
  return Object.fromEntries(
    Object.entries(values)
      .filter(([key, value]) => allowed.has(key) && value.trim().length > 0)
      .map(([key, value]) => [key, value.trim()]),
  );
}

/** Whether every required field has a usable value. */
export function hasRequiredPluginSearchFields(
  fields: PluginSearchField[],
  values: Record<string, string>,
): boolean {
  return fields.every((field) => !field.required || Boolean(values[field.key]?.trim()));
}

/**
 * Compatibility title for plugins that still read `IdentifyQuery.Title`. A schema-aware plugin reads
 * `Fields`; legacy plugins receive the first populated text value through both representations.
 */
export function pluginSearchCompatibilityTitle(
  fields: PluginSearchField[],
  values: Record<string, string>,
  fallback: string,
): string {
  const exactTitle = values.title?.trim();
  if (exactTitle) return exactTitle;

  for (const field of fields) {
    if (field.type !== PLUGIN_SEARCH_FIELD_TYPE.text) continue;
    const value = values[field.key]?.trim();
    if (value) return value;
  }

  return fallback.trim();
}
