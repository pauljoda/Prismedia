export interface EntityCredit {
  character: string | null;
  person: {
    id: string;
    kind: string;
    title: string;
    thumbnailUrl?: string | null;
  };
  role: string | null;
}

/** Returns the user-facing subtitle for an entity credit thumbnail. */
export function creditSubtitle(credit: EntityCredit): string | undefined {
  const character = credit.character?.trim();
  if (character) return character;
  const role = labelForCreditRole(credit.role);
  return role === "Person" ? undefined : role;
}

function labelForCreditRole(role: string | null | undefined): string {
  const normalized = (role ?? "").trim();
  if (!normalized) return "Person";
  return normalized
    .replaceAll("-", " ")
    .replaceAll("_", " ")
    .replace(/\b\w/g, (value) => value.toUpperCase());
}
