# Release preferences

Acquisition profiles define which releases are acceptable and how acceptable releases are ranked. Quality restrictions, size limits, required/ignored terms, and the minimum custom-format score remain acceptance gates. A preference cannot rescue a rejected release.

## Audio languages

Add languages in the profile's preferred audio list and move them into preference order. A release explicitly naming a preferred audio language ranks ahead of unspecified multiple audio languages, which rank ahead of unmarked releases. This order survives candidate persistence and automatic selection. Releases naming only other audio languages are rejected; MULTI remains an uncertain fallback.

| Release evidence | Meaning |
| --- | --- |
| English, ENG, or a structured `en` attribute | Explicit English declaration |
| `Ita Eng` or `[JA+EN]` | Explicit declarations of multiple languages |
| MULTI or Dual Audio | Multiple audio languages without confirming which ones |
| ENG Subs or Multi-Sub | Subtitle evidence; does not confirm audio |
| No language label | Unknown audio language |

The language list takes priority over other ranking weights among acceptable releases. Leave it empty to control language ranking entirely through scored custom formats. An English custom format matches explicit English evidence; it does not infer English from MULTI or an unmarked title. Downloaded video audio streams are checked independently before import when language preferences are configured.

## Basic and Advanced rules

Use **Add rule** inside a profile. Basic offers common language, codec, HDR, source, and audio-format starters. Title text entered in Basic is escaped literally; spaces also match common release separators. Codec/source starters describe release-title evidence, not a guarantee about the downloaded streams.

Basic creates ordinary custom-format conditions. Advanced edits those same conditions; switching views preserves changes. Applying another starter replaces the current rule's name and conditions. Advanced supports title and release-group regex, declared audio language, quality codes, required conditions, and exclusion.

Every required condition must match, and at least one condition must match. Non-required conditions are alternatives. Exclusion inverts a valid condition. Invalid or excessively expensive patterns do not match, even when excluded; regex matching is case-insensitive and time-bounded.

Definitions are shared across profiles; weights belong to each profile. Editing an existing definition changes its conditions everywhere it is used. Saving a rule from a profile saves the definition; save the profile to apply its score. The editor explicitly identifies this boundary.

Positive weights favor a release; negative weights reduce its score. Every matching custom format contributes its profile weight. A total below **Minimum format score** rejects a release: with a zero minimum, a negative total is a rejection rather than merely a lower ranking. **Cutoff format score** allows upgrades to pursue improved custom-format scores subject to quality and existing-file replacement rules. A language preference does not authorize downgrading an owned file below the upgrade policy.
