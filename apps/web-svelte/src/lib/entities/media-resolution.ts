import { MEDIA_RESOLUTION_TIERS, type MediaResolutionTierCode } from "$lib/api/generated/codes";
import { numberValue } from "$lib/utils/format";

/** Classifies source pixels with the same ordered thresholds used by Entity projections and collection SQL. */
export function resolutionBadge(
  width: number | string | null | undefined,
  height: number | string | null | undefined,
): MediaResolutionTierCode | null {
  const w = Math.round(numberValue(width) ?? 0);
  const h = Math.round(numberValue(height) ?? 0);
  return MEDIA_RESOLUTION_TIERS.find(tier => w >= tier.minimumWidth || h >= tier.minimumHeight)?.code ?? null;
}
