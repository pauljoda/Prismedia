export interface StudioEditValues {
  name: string;
  description: string;
  aliases: string;
  url: string;
  parentId: string | null;
  favorite: boolean;
  isNsfw: boolean;
}

export interface TagEditValues {
  name: string;
  description: string;
  aliases: string;
  favorite: boolean;
  isNsfw: boolean;
  ignoreAutoTag: boolean;
}

function nullableTrim(value: string): string | null {
  const trimmed = value.trim();
  return trimmed ? trimmed : null;
}

export function buildStudioEditPatch(values: StudioEditValues) {
  return {
    name: values.name.trim(),
    description: nullableTrim(values.description),
    aliases: nullableTrim(values.aliases),
    url: nullableTrim(values.url),
    parentId: values.parentId || null,
    favorite: values.favorite,
    isNsfw: values.isNsfw,
  };
}

export function buildTagEditPatch(values: TagEditValues) {
  return {
    name: values.name.trim(),
    description: nullableTrim(values.description),
    aliases: nullableTrim(values.aliases),
    favorite: values.favorite,
    isNsfw: values.isNsfw,
    ignoreAutoTag: values.ignoreAutoTag,
  };
}
