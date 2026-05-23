export async function invalidate(_resource?: string): Promise<void> {}

export async function invalidateAll(): Promise<void> {}

export async function goto(_href: string): Promise<void> {}

export function afterNavigate(_callback: unknown): void {}
