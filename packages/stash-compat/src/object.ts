export function hasOwnField<TKey extends PropertyKey>(
  value: object,
  key: TKey,
): value is Record<TKey, unknown> {
  return Object.hasOwn(value, key);
}

export function getOwnString(value: object, key: PropertyKey): string | undefined {
  if (!hasOwnField(value, key)) return undefined;
  return typeof value[key] === "string" ? value[key] : undefined;
}
