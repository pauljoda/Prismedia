/**
 * Cost of a placement that leaves one row of headroom, which even the shortest tile (two rows) cannot
 * fill. Such a tile grows by that row instead of leaving a hole, so the penalty only prefers clean fits.
 */
const UNFILLABLE_PENALTY = 4;

/** A tile's footprint in grid cells: whole columns by half-column rows. */
export interface MosaicSpan {
  columns: number;
  rows: number;
}

/** Where one tile landed, zero-based. */
export interface MosaicPlacement<T> {
  item: T;
  column: number;
  row: number;
  span: MosaicSpan;
}

/**
 * Chooses a footprint from artwork shape. Rows are half a column tall, so a square is 1x2, a 2:3
 * poster 1x3, and a wide frame 2x2 (about 2:1, a light crop of 16:9).
 */
export function mosaicSpanForAspect(aspect: number): MosaicSpan {
  if (aspect >= 1.3) return { columns: 2, rows: 2 };
  if (aspect < 0.9) return { columns: 1, rows: 3 };
  return { columns: 1, rows: 2 };
}

/**
 * Packs tiles into a band exactly `rows` tall with a skyline first-fit. Items are taken in order;
 * one that cannot fit anywhere is skipped so a later, smaller item can close the gap. Positions that
 * strand empty cells under a tile are penalised, and a tile that would leave a single row of headroom
 * grows into it, which keeps the band close to gap-free and its bottom edge straight.
 */
export function packMosaic<T>(
  items: readonly T[],
  spanFor: (item: T, index: number) => MosaicSpan,
  columns: number,
  rows: number,
): MosaicPlacement<T>[] {
  const skyline = Array.from({ length: columns }, () => 0);
  const placements: MosaicPlacement<T>[] = [];

  items.forEach((item, index) => {
    if (skyline.every((height) => height >= rows)) return;
    const wanted = spanFor(item, index);
    const span = { columns: Math.min(wanted.columns, columns), rows: Math.min(wanted.rows, rows) };

    let best: { column: number; row: number; rows: number; score: number } | null = null;
    for (let column = 0; column + span.columns <= columns; column += 1) {
      const covered = skyline.slice(column, column + span.columns);
      const row = Math.max(...covered);
      if (row + span.rows > rows) continue;
      const stranded = covered.reduce((sum, height) => sum + (row - height), 0);
      const grows = rows - (row + span.rows) === 1;
      const score = row + stranded * 1.5 + (grows ? UNFILLABLE_PENALTY : 0);
      if (!best || score < best.score) best = { column, row, rows: span.rows + (grows ? 1 : 0), score };
    }
    if (!best) return;

    for (let column = best.column; column < best.column + span.columns; column += 1) {
      skyline[column] = best.row + best.rows;
    }
    placements.push({ item, column: best.column, row: best.row, span: { columns: span.columns, rows: best.rows } });
  });

  return placements;
}
