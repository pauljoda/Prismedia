interface ProviderRef {
  id: string;
}

export function supportedProviderId(
  providers: readonly ProviderRef[],
  selectedProviderId: string | null | undefined,
  preferredProviderId: string | null | undefined,
): string {
  return (
    findProviderId(providers, selectedProviderId) ??
    findProviderId(providers, preferredProviderId) ??
    providers[0]?.id ??
    ""
  );
}

function findProviderId(
  providers: readonly ProviderRef[],
  providerId: string | null | undefined,
): string | null {
  if (!providerId) return null;
  return providers.some((provider) => provider.id === providerId) ? providerId : null;
}
