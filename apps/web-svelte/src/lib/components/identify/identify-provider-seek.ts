export function providerSeekOrder(providerIds: readonly string[], activeProviderId: string | null | undefined): string[] {
  if (providerIds.length === 0) return [];

  const activeIndex = activeProviderId ? providerIds.indexOf(activeProviderId) : -1;
  const startIndex = activeIndex >= 0 ? activeIndex + 1 : 0;

  return startIndex >= providerIds.length ? [...providerIds] : providerIds.slice(startIndex);
}
