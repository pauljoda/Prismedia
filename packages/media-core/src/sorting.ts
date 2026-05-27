const naturalPathCollator = new Intl.Collator(undefined, {
  numeric: true,
  sensitivity: "base",
});

export function naturalComparePaths(a: string, b: string): number {
  return naturalPathCollator.compare(a, b);
}

export function sortPathsNaturally<T extends string>(paths: T[]): T[] {
  return [...paths].sort(naturalComparePaths);
}
