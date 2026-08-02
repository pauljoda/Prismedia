# Consumption tracking

Prismedia models media use as consumption rather than treating every medium as video playback.
The same user-scoped capability applies to videos, images and galleries, audio tracks and albums,
books and chapters, episodes and seasons, and future consumable Entity kinds.

## Two complementary records

`CapabilityConsumption` is the cached current summary for one user and Entity:

- access count, incremented when the Entity is actually opened for watching, listening, reading,
  or viewing;
- completion and skip counts;
- total active duration;
- timed-media resume position;
- first-class last-accessed, last-active, and completed timestamps.

Discrete `accessed`, `completed`, and `skipped` events preserve timestamped history. An access can
carry a client session identifier so retries and duplicate player-start signals remain idempotent.
Active-time heartbeats are not events. Each accepted heartbeat atomically increments both the
Entity's cached total and one `(user, Entity, local day, activity kind)` bucket. Statistics can
therefore answer date-range questions from bounded daily rows without summing the complete event
history.

The activity kinds are `viewing`, `listening`, and `reading`. Clients send the viewer's UTC offset
with a heartbeat so the daily bucket follows the calendar day the user experienced.

## Structural rollups

Entity-kind definitions decide which kinds support consumption and declare progress topology.
Infrastructure discovers consumable leaf descendants from that topology instead of maintaining a
parallel list of album, season, series, or book special cases.

A container summary adds its own direct activity to the activity of its consumable leaves:

- an album sums its tracks;
- a season sums its episodes and a series sums the episodes across its seasons;
- a book sums structural chapters or parts while retaining activity reported directly by EPUB,
  PDF, comic, and audiobook sessions.

Resume position belongs to the exact playable or readable Entity. Container completion is true
only when all consumable leaves are complete, unless the container was explicitly marked complete.

## Current cursor and coverage

Progress deliberately exposes two different facts:

- `currentEntityId`, `index`, and the format-specific location identify the most recently active
  place and may move backward;
- `consumedCount`, `consumedTotal`, and `consumedPercent` describe independent coverage and do not
  fall when an earlier episode or chapter becomes current.

Clients use the current cursor for Continue or Resume actions and use consumed percentage for
season, series, and book meters. "Start over" is the explicit operation that resets both cursor and
coverage.

## HTTP compatibility

The 3.0 Entity architecture remains the public boundary. Reads use `GET /api/entities/{id}` and the
generated `consumption` and `progress` capabilities. Existing intent-specific write routes remain
stable:

- `/api/entities/{id}/playback` updates timed resume state and may carry an active-time heartbeat;
- `/api/entities/{id}/playback/events` records access, completion, or skip history;
- `/api/entities/{id}/progress` updates unit-based reading or ordered-container progress and may
  carry an active-time heartbeat;
- `/api/playback/statistics` serves the generalized consumption statistics projection.

The route names remain compatible with existing clients; the contracts and behavior are
medium-neutral. Generated web and Swift models are the client source of truth.
