# Sonarr Import, Parsing & Renaming System — Parity Map

> **Source.** `/Users/pauldavis/Dev/_ARCHIVE/Sonarr/src` (namespace root `NzbDrone.Core`/`Sonarr.Api.V3`). All
> paths below are absolute unless noted. This report covers the Parser, the Import Decision
> pipeline (`MediaFiles/EpisodeImport` + `Specifications/` + `Aggregation/` + `Manual/`), the
> Organizer/naming engine, Media Management configuration, Root Folders, Extras (subtitles/nfo/
> metadata), and the file lifecycle (recycle bin, upgrade-in-place, disk scan, rename-existing).
>
> **Coverage.** Every file in the eight target directories was read in full:
> `MediaFiles/` (35 top-level files + `Commands/` 7 + `Events/` 11 + `MediaInfo/` + `TorrentInfo/`),
> `MediaFiles/EpisodeImport/` (14 top-level files + `Specifications/` 14 + `Aggregation/` 4 top-level
> + `Aggregation/Aggregators/` 6 + `Aggregation/Aggregators/Augmenters/Quality/` 6 +
> `Aggregation/Aggregators/Augmenters/Language/` 6 + `Manual/` 5), `Parser/` (10 top-level files +
> `Model/` 13), `Organizer/` (13 files), `RootFolders/` (4 files), `Extras/` (5 top-level +
> `Files/` 4 + `Subtitles/` 7 + `Others/` 6 + `Metadata/` 13 incl. `Consumers/` and `Files/`),
> `Configuration/` (9 top-level files + `Events/`), plus the API surface in
> `Sonarr.Api.V3/ManualImport/` and `Sonarr.Api.V3/Episodes/RenameEpisodeController.cs`.
> Nothing below is summarized from a file name alone — every claim traces to a specific class/method.

---

## 1. The Release/File Name Parser

### 1.1 `Parser/Parser.cs` — the core title parser

**File:** `Parser/Parser.cs` (1,295 lines). Public surface: `ParseTitle(string)`, `ParsePath(string)`,
`ParseSeriesName(string)`, `CleanSeriesTitle(string)`, `NormalizeEpisodeTitle(string)`,
`NormalizeTitle(string)`, `NormalizeImdbId(string)`, `RemoveFileExtension(string)`,
`HasMultipleLanguages(string)`, `SimplifyTitle(string)`.

**`ParseTitle(title)` pipeline:**
1. `ValidateBeforeParsing`: rejects titles containing both `"password"` and `"yenc"` (spam/decoy
   NZB titles), titles with no alphanumeric character at all, titles matching
   `RejectHashedReleasesRegexes` (see below), and titles matching `SeasonFolderRegexes`
   (`^(Season[ ._-]*\d+|Specials)$` — a bare season-folder name is not a parseable release title).
2. **Reversed-title detection**: `ReversedTitleRegex` = `(?:^|[-._ ])(p027|p0801|\d{2,3}E-?\d{2}S)[-._ ]`
   — some broken indexers publish titles with the string reversed; if this pattern matches, the
   filename (sans extension) is character-reversed before continuing.
3. Extension stripped (`FileExtensions.RemoveFileExtension`), full-width brackets `【`/`】` normalized
   to `[`/`]`.
4. **Pre-substitution regexes** (`ParserCommon.PreSubstitutionRegex`, 12 patterns) rewrite known
   malformed release-title shapes into a normal form before the main regex family runs — mostly
   Chinese/Korean anime fansub conventions: Korean daily-airdate-without-season → synthesizes
   `S01Exxx`; multiple patterns strip embedded Chinese-character titles from bracket-heavy releases
   (`LoliHouse`/`ZERO`/`Lilith-Raws`/`Skymoon-Raws`/`orion origin` groups get special-cased bracket
   normalization); Chinese season-pack patterns with `第`/`话`/`集`/`完`/`Fin`/`END` markers get
   rewritten to `SxxExx`-style; a Spanish "Title (Year/Sxx...)" bracket form is rewritten too.
5. **`SimpleTitleRegex`** strips quality/codec/resolution noise so the report-title regexes don't
   have to account for it inline: resolutions (`480|540|576|720|1080|1440|2160[ip]`), codec tags
   (`x264`/`h264`/`xh.264/265/266`), `DD5.1`, illegal filename chars, and literal resolution
   strings `848x480`/`1280x720`/`1920x1080`/`3840x2160`/`4096x2160`, plus `8bit`/`10bit` tags.
6. Website prefix/postfix stripping (`ParserCommon.WebsitePrefixRegex`/`WebsitePostfixRegex` — strip
   a leading/trailing domain-name-looking token, e.g. `www.example.com - `) and torrent-site suffix
   stripping (`CleanTorrentSuffixRegex` strips `[ettv]`/`[rartv]`/`[rarbg]`/`[cttv]`/`[publichd]`).
7. **`CleanQualityBracketsRegex`** conditionally strips a trailing `[...]` bracket group only if its
   contents parse as a **known quality name** (avoids stripping a legitimate title component that
   happens to be bracketed).
8. **Six-digit air-date fixup**: `SixDigitAirDateRegex` detects a `YYMMDD`-shaped date between
   separators and rewrites it to `20YY.MM.DD` (assumes 21st century) before the main match — this
   is what lets the later air-date regexes (which expect 4-digit years) catch these releases.
9. Iterates the **`ReportTitleRegex` array (98 compiled regexes)**, trying each in declared order
   and taking the **first regex whose `Matches()` succeeds** (not the best-scoring — pure
   first-match-wins ordering, hence the very deliberate ordering from most-specific/anime-daily
   patterns down to generic season-only fallbacks at the bottom). If a match is found,
   `ParseMatchCollection` builds the `ParsedEpisodeInfo`; if it throws `InvalidDateException` the
   whole regex loop aborts (`break`) rather than falling through to a laxer pattern — a
   confidently-detected-but-invalid date is treated as a hard parse failure, not "try the next
   regex."
10. Post-match enrichment: `LanguageParser.ParseLanguages(result.ReleaseTokens)`,
    `QualityParser.ParseQuality(title)` (on the **original** title, not the simplified one),
    `ReleaseGroupParser.ParseReleaseGroup(releaseTitle)` (overridden by an anime `[SubGroup]` capture
    if present), and `GetReleaseHash(match)` (the `hash` capture group, trimmed of brackets; the
    literal value `1280x720` is explicitly excluded as a false-positive hash).
11. A `FullSeason` result whose `ReleaseTokens` contains the word `"Special"` is reclassified:
    `FullSeason = false; Special = true`.

**Regex family taxonomy (98 patterns in `ReportTitleRegex`, illustrative, not exhaustive listing of
every regex string — grouped by what they detect):**
- Daily-episode formats: year-in-title + airdate + time (Plex DVR format), bare `YYYY-MM-DD`/
  `YYYYMMDD`, `MM.DD.YYYY`, `DD.MM.YYYY`, short-month-name dates (`5th-jan-2019`), airdate+season/
  episode combined, airdate+`Part N`, Japanese variety-show leading 2-digit-date + `ep`/`#` number.
- Multi-episode without title: `S01E05.S01E06`, `1x05.1x06`, `S01E04E05`, repeated
  `S01E05 - S01E06` and `1x05 - 1x06` forms, square/paren-bracketed multi-episode
  (`[S01E11E12]`, `(S01E11-12)`, `(S1E1-3 of 12)`).
- Split episodes: `S01E05a`/`S01E05b` (`splitepisode` capture group a–d).
- Anime absolute-episode families (≈30 of the 98 patterns): `[SubGroup] Title - NN`,
  `[SubGroup] Title (SxxExx)`, `[SubGroup] Title SxxExx + absolute`, "Episode NN" word-form,
  4-digit absolute numbers, hash-suffixed releases (`[ABCD1234]`), `(Season_N)` bracket form,
  batch ranges (`01~12`, `01-12`), OVA/Special/NCOP/NCED markers.
- Mini-series (no season token at all, treated as season 1): `Part01`/`Part 01`/`Part.1`,
  `PartOne`..`PartNine` word-numbers, `E1-E2` bare-episode form, `XofY` form.
- Season-pack / partial-season: `Complete Series`, `Season|Saison|Series|Stagione N`
  (multi-language season-word support), partial-season via `seasonpart` capture
  (`Part2`/`Vol2`/`p2`), `EXTRAS`/`SUBPACK` season-extra markers.
- Regional formats: Spanish `Temporada`/`Cap` bracket form, Dutch/Flemish `Se.N-aflN`,
  Turkish `BLM`/`Bölüm`, 3-digit/4-digit season numbers (`S001E05`, `S2016E05`).
- Numeric-shorthand: `103`/`113` (season+2-digit-episode with no separator),
  `1103`/`1113` (2-digit season + 2-digit episode).
- 5-digit and 4-digit episode numbers (some sports/awards-show releases use non-standard episode
  widths).
- **Anime detection heuristic caveat**: essentially every anime pattern requires a leading
  `[SubGroup]` bracket; unbracketed absolute-numbering anime releases fall through to
  the generic "Anime - Title Absolute Episode Number" patterns near the bottom of the list, which
  match on separator+number heuristics with negative lookaheads for `p`/`i` (resolution suffix
  false-positive protection) and are intentionally last-resort (highest false-positive risk).

**`RejectHashedReleasesRegexes`** (used only as a pre-parse rejection gate, 11 patterns): generic
32/30/26/24/39-char mixed-case hash strings, a stricter lowercase 24-char pattern, two very
specific NZBGeek-era 11-letter+3-digit / 12-letter+3-digit patterns (deliberately strict "coz
they are very close to the valid 101 ep numbering"), a `Backup_NNNNN Sxx-xx` pattern, and three
literal known-junk-string releases (`123`, `abc`, `abc-xyz`, `b00bs`, `170424_26`) that appeared on
specific dates historically (comments annotate exactly when each pattern started appearing, e.g.
"Started appearing December 2014").

**Special-episode title extraction** (`SpecialEpisodeTitleRegex`, 2 patterns): matches
`.SxxE00.<title>.` or `.Sxx.Special.<title>.` immediately before a quality-tag boundary
(`720p|1080p|2160p|HDTV|WEB|WEBRip|WEB-DL`) or end-of-string.

**`ParsePath(path)`** — folder+file combination logic (distinct from `ParseTitle`):
1. Parses the bare filename first.
2. **Season-folder + bare-episode-number fallback**: if the filename alone matches
   `SimpleEpisodeNumberRegex` (`^[ex]?(?<episode>\d{1,3})(?:[ex-](?<episode>\d{1,3}))?...`) AND the
   *parent directory* name matches `SeasonFolderRegex` (`^(?:S|Season|Saison|Series|Stagione)[-_. ]*
   (?<season>\d{1,4})...`), AND the filename-only parse either failed, is a mini-series, or is
   absolute-numbered — synthesizes a combined string `S{season}E{first}[-E{last}]
   {remaining text}` and re-parses *that* as the canonical result. This is what lets a library laid
   out as `Series/Season 03/05 - Title.mkv` parse correctly even though the bare filename `05 -
   Title.mkv` has no season number of its own.
3. If the filename alone is a bare integer (no parse result) and the **directory name** parses with
   that integer present in its `AbsoluteEpisodeNumbers` or `EpisodeNumbers`, narrows the directory's
   result down to just that one number (handles a directory-per-episode layout where files are
   literally named `5.mkv`).
4. Falls back to parsing `directoryName + " " + fileName` combined, then `directoryName + extension`
   alone, in that order, if all else fails.

**`CleanSeriesTitle`**: numeric-only titles pass through unchanged; `%` is replaced with the word
`percent` (handles literal show titles like "3%"); otherwise `NormalizeRegex` strips a curated set
of stopwords (`a`, `à`, `an`, `and`, `or`, `of` — matched only mid-string, never touching the very
first/last word boundary token) plus all non-word characters, lowercases, and strips diacritics.
This is the canonical **series-matching key**, distinct from the display title.

**`NormalizeTitle`** (different from `CleanSeriesTitle`): strips punctuation, strips a **leading**
`a`/`an`/`the` article only, collapses whitespace, lowercases.

**`NormalizeImdbId`**: validates `^(\d{1,10}|(tt)\d{1,10})$`, left-pads the numeric portion to 7
digits with `tt` prefix; returns `null` for anything that doesn't fit this shape or is ≤2 chars
after stripping `tt`.

### 1.2 Quality parsing — `Parser/QualityParser.cs`

**`ParseQuality(name)`** delegates to `ParseQualityName(name)`, then — only if still `Unknown` and
the name has no invalid path chars — falls back to `MediaFileExtensions.GetQualityForExtension`
(seeds a coarse quality guess purely from file extension, e.g. `.m2ts` → `Bluray720p`, `.mkv`/`.ts`/
`.wtv` → `HDTV720p`, most SD container extensions → `SDTV`, `.img`/`.iso`/`.vob` → `DVD`).

**`ParseQualityName(name)` decision tree** (each branch sets both a resolution and a source, and a
`QualityDetectionSource` per-field: `Unknown`/`Name`/`MediaInfo`/`Extension`):
1. `RawHDRegex` (`RawHD`/`Raw-HD`/`Raw.HD`) → `Quality.RAWHD` immediately, short-circuits everything
   else.
2. Parses **modifiers first** (`ParseQualityModifiers`): `VersionRegex` extracts an explicit
   `vN`/`repackN`/`reripN` version digit; `ProperRegex` (`\bproper\b`) bumps revision to
   `(parsedVersion ?? 1) + 1` if not already set by version; `RepackRegex`
   (`repack\d?|rerip\d?`) does the same **and** sets `Revision.IsRepack = true`; `RealRegex`
   (`\bREAL\b`, case-sensitive, can match multiple times) sets `Revision.Real` to the match count.
3. Detects `SourceRegex` (bluray/webdl/webrip/hdtv/bdrip/brrip/dvd/dsr/pdtv/sdtv/tvrip — a single
   compiled alternation with `IgnorePatternWhitespace`) and `ResolutionRegex`
   (360/480/540/576/720/1080/2160, plus alias forms `1280x720`, `1920x1080`, `1440p`→1080p,
   `4kto1080p`→1080p, `FHD`→1080p) and `RemuxRegex` independently, then cross-references them:
   - `bluray` + `xvid`/`divx` codec → `Bluray480p` (codec overrides an otherwise-higher resolution
     signal — old xvid rips mislabeled with a resolution tag are still bucketed SD).
   - `bluray` + 2160p → `Bluray2160pRemux` if remux matched else `Bluray2160p`; same pattern for
     1080p; 576p → `Bluray576p`; 360/480/540p → `Bluray480p`; a remux tag with **no** resolution (or
     720p specifically) still resolves to `Bluray1080pRemux` unless resolution is exactly 720p, in
     which case plain `Bluray720p` wins (a documented deliberate exception: "720p remux should
     fallback as 720p BluRay" since a genuine 720p remux is vanishingly rare and more likely a
     mislabel).
   - `webdl` → resolution-mapped `WEBDL{2160|1080|720}p`, with a literal-string fallback check for
     `[WEBDL]` → `WEBDL720p`, else `WEBDL480p`.
   - `webrip` → same resolution ladder to `WEBRip{2160|1080|720|480}p`.
   - `hdtv` → checks `MPEG2Regex` first (MPEG-2 HDTV is `RAWHD`, not standard HDTV), else resolution
     ladder to `HDTV{2160|1080|720}p`, literal `[HDTV]` fallback → `HDTV720p`, else `SDTV`.
   - `bdrip`/`brrip` → resolution-only switch (bluray-tier qualities), default `Bluray480p`.
   - `dvd` → flat `Quality.DVD`.
   - `pdtv`/`sdtv`/`dsr`/`tvrip` → resolution ladder to HDTV1080p/720p (yes, these SD-source tags
     can still be promoted to HDTV720p/1080p if a resolution tag or `HighDefPdtvRegex`
     (`hr[-_. ]ws`, "high-res widescreen") is also present), else `SDTV`.
4. If no source matched at all but a **remux** tag and a resolution did: maps resolution directly to
   a Bluray-tier quality (480p→Bluray480p, 720p→Bluray720p, 2160p→Bluray2160pRemux,
   1080p→Bluray1080pRemux) with `SourceDetectionSource = Unknown` (the remux implies Bluray-class
   without an explicit "Bluray" token being present).
5. **Anime-specific bluray/webdl detection** (`AnimeBlurayRegex` = `bd(?:720|1080|2160)|(?&lt;=[-_.
   (\[])bd(?=[-_. )\]])`; `AnimeWebDlRegex` = `\[WEB\]|[\[\(]WEB[ .]`) — separate from the main
   source regex because anime releases often use a bare `BD` token instead of `BluRay`.
6. Generic resolution-only fallback (no source token matched at all): tries to get a quality
   *source* from the file extension via `MediaFileExtensions.GetQualityForExtension`, then maps
   resolution via `QualityFinder.FindBySourceAndResolution(source, resolution)`; if source is still
   `Unknown`, defaults source-less 2160p/1080p/720p to `HDTV{res}` and 360/480/540/576p to `SDTV`.
7. Final literal-string fallbacks in strict order: `x264`+no other signal → `SDTV`; literal
   `848x480` (→ DVD if "dvd" in name, Bluray480p if "bluray", else SDTV); literal `1280x720` (→
   Bluray720p if "bluray" else HDTV720p); literal `1920x1080` (analogous); literal
   `bluray720p`/`bluray1080p`/`bluray2160p` compound strings; and finally `OtherSourceRegex`
   (`HD[-_. ]TV` / `SD[-_. ]TV` with a space/separator between HD and TV, a **different** pattern
   from the main `hdtv` alternative which requires no separator) as the last-resort match.

`Resolution` enum: `R360p=360, R480p=480, R540p=540, R576p=576, R720p=720, R1080p=1080, R2160p=2160,
Unknown=0` — the numeric values are the literal pixel-height convention, not sequential IDs.

### 1.3 Language parsing — `Parser/LanguageParser.cs`

`ParseLanguages(title)` first applies `CleanSeriesTitleRegex` (strips everything before a
`SxxExx`-style marker, so a series title that happens to contain a language-looking word — e.g. a
title with "German" in it — doesn't get misdetected) then runs two passes:
1. **Plain-substring checks** (`lowerTitle.Contains("spanish")` etc.) for ~25 languages where a
   full English language *name* appearing anywhere in the title is a strong enough signal on its
   own: Spanish, Danish, Dutch, Japanese, Icelandic, Chinese (`mandarin`/`cantonese`/`chinese`),
   Korean, Russian, Polish, Vietnamese, Swedish, Norwegian, Finnish, Turkish, Portuguese,
   Hungarian, Hebrew, Arabic, Hindi, Malayalam, Ukrainian, Bulgarian, Slovak,
   Portuguese-Brazil (`brazilian`/`dublado`), Spanish-Latino (`latino`), Latvian, Azerbaijani,
   Uzbek. `English` is checked **last**, after the regex pass, so it never masks a more specific
   detected language when both appear (dual-audio releases commonly list "English" alongside the
   primary language).
2. **`RegexLanguage(title)`** — two compiled regex passes:
   - `CaseSensitiveLanguageRegex`: `LT`/`CZ`/`PL`/`BG`/`SK`/`DE` as bare 2-letter **uppercase**
     codes, explicitly excluding matches immediately preceded/followed by `SUB` (so `ENSUB`-style
     subtitle-language tags aren't misread as an audio-language code) — Lithuanian, Czech, Polish,
     Bulgarian, Slovak, German.
   - `LanguagesOnlyRegex` (case-insensitive, ~25 named groups): English (`ing`/`eng`), Italian,
     German (incl. `swissgerman`, `ger.dub`, bare `ger`), Flemish, Greek, French (strict token set
     `FR|VF|VF2|VFF|VFI|VFQ|TRUEFRENCH|FRENCH|FRE|FRA` bounded by `_`/word-boundary), Russian
     (`rus`/`ru`), Hungarian (`HUNDUB`/`HUN`), Hebrew (`HebDub`), Polish (`PL DUB`/`DUB PL`/`LEK
     PL`/`PL LEK`), Chinese (`[CHS]`/`[CHT]`/`[BIG5]`/`[GB]`/CJK ideographs/`简`/`繁`/`字幕`/`国语音轨`),
     Bulgarian (`bgaudio`), Spanish (`español`/`castellano`/`esp`/`spa`, explicitly **not** matching
     `spa(Latino)`), Ukrainian, Thai, Romanian (`RoDubbed`/`ROMANIAN`), Catalan, Latvian, Turkish,
     Urdu, Romansh, Georgian, Japanese (`(JA)`/`JAP`/`JPN`), Portuguese (`.por.`), and a literal
     `original`/`orig` marker (maps to `Language.Original`, used for "keep original audio track"
     dual-language tagging).
   - The **outer** `LanguageRegex` wraps `LanguagesOnlyRegex` with a negative lookahead
     `(?!(?:[-_. ]{LanguagesOnlyRegex})*[-_. ]subs?)` — a detected language token immediately
     followed by (optionally more language tokens then) the literal word `sub`/`subs` is excluded
     entirely, since that's a *subtitle* language list, not an audio language.
3. **German dual/multi-language special case**: if the *only* detected language ends up being
   German, checks `GermanDualLanguageRegex` (`(?&lt;!WEB[-_. ]?)\bDL\b` — a bare `DL` token not
   preceded by `WEB`, since `WEB-DL` is a source tag, not a language marker) → adds
   `Language.Original` (German-DL releases are dual-audio, original + German dub);
   `GermanMultiLanguageRegex` (`\bML\b`) → adds **both** `Original` and `English`.
4. Result is de-duplicated by underlying enum int value (`DistinctBy((int)l)`); if nothing matched
   at all, defaults to `[Language.Unknown]`.

**Subtitle-specific language parsing** (also in this file, used by the Extras/Subtitles pipeline):
- `ParseSubtitleLanguage(fileName)`: matches `SubtitleLanguageRegex` (ISO 2-3 letter code
  surrounded by optional `forced|foreign|default|cc|psdh|sdh` tags, separated by `-`/`_`/`.`/space,
  anchored to end-of-filename) and resolves the code via `IsoLanguages.Find`; falls back to
  checking whether the filename ends with a full language *name* string; defaults to `Unknown`.
- `ParseLanguageTags(fileName)`: extracts just the `tags` capture group values (lowercased) from the
  same regex — `forced`/`foreign`/`default`/`cc`/`psdh`/`sdh` — as a flat string list, **not**
  modeled as individual booleans.
- `ParseSubtitleLanguageInformation(fileName)` / `SubtitleLanguageTitleRegex`: additionally attempts
  to recover an embedded **title** segment between the base filename and the language code (e.g. a
  release-group tag), and a numeric "copy" index via `SubtitleTitleRegex` (`^((?&lt;title&gt;.+) -
  )?(?&lt;copy&gt;\d{1,3})$`) — falls back to `ParseBasicSubtitle` (no title recovery) if the title
  group didn't match or more than one ISO code was captured (ambiguous).

**`IsoLanguages.cs`**: a static `HashSet&lt;IsoLanguage&gt;` mapping (2-letter code, region code,
3-letter code, `Language` enum) for ~50 languages — includes region-specific variants sharing a base
code: `pt`/`pt-pt`→Portuguese vs `pt`/`br`→PortugueseBrazil; `es`/(none)→Spanish vs
`es`/`mx`→SpanishLatino; `no`/`nb`→Norwegian (both map to the same `Language.Norwegian`, i.e. Bokmål
is not distinguished); `nl`→both Dutch and Flemish (same code, disambiguated only by which
`Language` enum value the release-title regex, not the ISO table, actually selected upstream).

### 1.4 Release group parsing — `Parser/ReleaseGroupParser.cs`

`ParseReleaseGroup(title)` order of operations:
1. Strip extension, apply the same `PreSubstitutionRegex`/`WebsitePrefixRegex`/
   `CleanTorrentSuffixRegex` normalization as the main parser.
2. **Anime path**: `AnimeReleaseGroupRegex` (`^(?:\[(?&lt;subgroup&gt;...)\](?:_|-|\s|\.)?)`) — a
   leading `[Group]` bracket, if present, is authoritative and short-circuits everything else.
3. Otherwise applies `CleanReleaseGroupRegex` (strips a leading `SxxExx` block plus known
   junk-suffix tokens like `-RP`, `-NZBGeek`, `-Obfuscated`, `-Scrambled`, `-sample`, `-Pre`,
   `-postbot`, `-xpost`, several named uploader "junk" tags, `-RePACKPOST`).
4. **Named-exception lists** (two separate hard-coded regexes) for release groups whose naming
   convention doesn't follow the standard trailing `-GroupName` pattern: an "exact anywhere" list
   (`Fight-BB`, `VARYG`, `E.N.D`, `KRaLiMaRKo`, `BluDragon`, `DarQ`, `KCRT`, `BEN_THE_MEN`, `TAoE`,
   `QxR`, `Vialle`) and a "must end with `)`/`]`" list (`Joy`, `ImE`, `UTR`, `t3nzin`, `Anime Time`,
   `Project Angel`, `Hakata Ramen`, `HONE`, `Vyndros`, `SEV`, `Garshasp`, `Kappa`, `Natty`, `RCVR`,
   `SAMPA`, `YOGI`, `r00t`, `EDGE2020`, `Celdra`) — both checked (in that order) before falling
   through to the generic pattern, and both take the **last** match in the title if multiple occur.
5. **Generic pattern** (`ReleaseGroupRegex`): a trailing `-GroupName` (allowing one embedded hyphen,
   e.g. `-Xtra-Ordinary`) with negative lookaheads excluding common quality/source/audio-codec/
   language-code tokens from being misread as a group name (`HDTV`,`SDTV`,`WEB-DL`,`Blu-Ray`,
   `480p`..`2160p`, `DTS-HD`,`DTS-X`,`DTS-MA`,`DTS-ES`, `-ES`/`-EN`/`-CAT`/`-GER`/`-FRA`/`-FRE`/
   `-ITA`, `N-bit`, a trailing `-NN` 2-digit token, or a `YYYY-NN` date-like token) — plus an
   alternate form for a trailing `[GroupName]` bracket.
6. Purely numeric matches or matches equal to `InvalidReleaseGroupRegex` (`^([se]\d+|[0-9a-f]{8})$`
   — an `sNN`/`eNN` fragment or an 8-hex-digit hash) are discarded, returning `null`.

### 1.5 Other Parser support files

- **`ParserCommon.cs`**: internal-only home for `PreSubstitutionRegex`, `WebsitePrefixRegex`,
  `WebsitePostfixRegex`, `CleanTorrentSuffixRegex` — shared by `Parser`, `QualityParser` (indirectly
  via title cleanup), and `ReleaseGroupParser`.
- **`RegexReplace.cs`**: a tiny wrapper pairing a compiled `Regex` with either a literal replacement
  string or a `MatchEvaluator` delegate, exposing `Replace`/`TryReplace` (the latter returns whether
  a substitution actually happened, used to `break` out of the pre-substitution loop after the
  first hit).
- **`ReleaseComparer.cs`**: `SameNzb`/`SameTorrent` — de-duplication logic for the *download
  queue/history* (not import), comparing indexer name, published-date (±2 minute tolerance), and
  size (±2 MB tolerance) for Usenet, or torrent info-hash (falling back to indexer-name match) for
  torrents. Not part of the import pipeline itself but shares the `Parser` namespace.
- **`SceneChecker.cs`**: `IsSceneTitle(title)` — a conservative "is this string plausibly a scene
  release name" check (must contain a dot, must **not** contain a space, must fully parse with a
  non-null release group, known quality, and non-blank series title) — deliberately biased toward
  false negatives ("It's better not to use a title that might be scene than to use one that isn't
  scene"). Used by `SceneNameCalculator` and `AggregateEpisodes`.
- **`ValidateParsedEpisodeInfo.cs`**: single check — a `Daily`-style parse result against a
  `SeriesTypes.Standard` series is invalid (logged as a warning or debug depending on caller
  context) — this is what prevents a coincidentally date-shaped release title from being accepted
  for a normal season/episode series.
- **`Parser/Model/`** — the 13 DTOs consumed throughout the pipeline: `ParsedEpisodeInfo` (the
  parser's direct output — see field list below), `SeriesTitleInfo` (`Title`, `TitleWithoutYear`,
  `Year`, `AllTitles[]` for multi-title "Title1 / Title2" releases), `LocalEpisode` (the pipeline's
  working aggregate — every field a decision spec or aggregator reads/writes, listed in full in
  §2.7), `RemoteEpisode` (the grab-time equivalent of `LocalEpisode`, carries `ReleaseInfo` +
  `SceneMapping` + `MappedSeasonNumber` + `SeriesMatchType`), `ReleaseInfo` (indexer-side release
  metadata: `Guid`,`Title`,`Size`,`DownloadUrl`,`InfoUrl`,`CommentUrl`,`IndexerId`,`Indexer`,
  `IndexerPriority`,`DownloadProtocol`,`TvdbId`,`TvRageId`,`ImdbId`,`PublishDate`,`Origin`,`Source`,
  `Container`,`Codec`,`Resolution`,`Languages`,`IndexerFlags`, computed `Age`/`AgeHours`/
  `AgeMinutes`), `ReleaseType` enum (`Unknown=0, SingleEpisode=1, MultiEpisode=2, SeasonPack=3`),
  `IndexerFlags` `[Flags]` enum (`Freeleech=1, Halfleech=2, DoubleUpload=4, Internal=8, Scene=16,
  Freeleech75=32, Freeleech25=64, Nuked=128, Subtitles=256`), `GrabbedReleaseInfo` (reconstructed
  from `EpisodeHistory` rows: `Title`,`Indexer`,`Size`,`IndexerFlags`,`ReleaseType`,`EpisodeIds`),
  `SubtitleTitleInfo` (`LanguageTags`,`Language`,`RawTitle`,`Title`,`Copy`,`TitleFirst`),
  `FindSeriesResult`, `ImportListItemInfo`, `ReleaseComparerModel`, `TorrentInfo`.

**`ParsedEpisodeInfo` field list** (the parser's canonical output shape): `ReleaseTitle`,
`SeriesTitle`, `SeriesTitleInfo`, `Quality` (`QualityModel`), `SeasonNumber` (int),
`EpisodeNumbers` (int[]), `AbsoluteEpisodeNumbers` (int[]), `SpecialAbsoluteEpisodeNumbers`
(decimal[] — fractional absolute numbers like `12.5` for OVA/special episodes), `AirDate` (string,
`Episode.AIR_DATE_FORMAT`), `Languages`, `FullSeason`/`IsPartialSeason`/`IsMultiSeason`/
`IsSeasonExtra`/`IsSplitEpisode`/`IsMiniSeries`/`Special` (bools), `ReleaseGroup`, `ReleaseHash`,
`SeasonPart` (int), `ReleaseTokens` (the un-consumed remainder of the title after
season/episode/airdate matching — this is what language/proper/repack detection runs against), and
`DailyPart` (int? — a `Part N` suffix on a daily episode, e.g. two-part news specials). Computed
properties: `IsDaily`, `IsAbsoluteNumbering`, `IsPossibleSpecialEpisode`,
`IsPossibleSceneSeasonSpecial`, `ReleaseType`.

---

## 2. Import Decision Pipeline

### 2.1 Entry points — turning a folder/file into import attempts

**`MediaFiles/DownloadedEpisodesCommandService.cs`** (`IExecute<DownloadedEpisodesScanCommand>`):
requires a non-blank `Path` (throws `ArgumentException` otherwise). If a `DownloadClientId` is
given and resolves to a known `TrackedDownload`, calls
`DownloadedEpisodesImportService.ProcessPath(path, importMode, trackedDownload.RemoteEpisode.Series,
trackedDownload.DownloadItem)` then `CompletedDownloadService.VerifyImport(trackedDownload,
importResults)` (the tracked-download state-machine hook). An unknown download ID logs a warning
and falls back to an **untracked** `ProcessPath(path, importMode)` (series = null, resolved later
per item). If **no** result has `ImportResultType.Imported`, the command still completes
successfully but reports `CommandResult.Unsuccessful` (surfaces a failure banner without hard-
failing the job queue).

**`MediaFiles/DownloadedEpisodesImportService.cs`** — three public methods:
`ProcessRootFolder(DirectoryInfo)`, `ProcessPath(path, importMode, series?, downloadClientItem?)`,
`ShouldDeleteFolder(DirectoryInfo, Series)`.

*Folder processing* (`ProcessFolder`, series-unknown overload → series-known overload):
1. Cleans the folder name of literal `_UNPACK_`/`_FAILED_` substrings, resolves series via
   `_parsingService.GetSeries(cleanedName)`; unresolvable → single result rejected
   `UnknownSeries`.
2. **Series-folder guard**: if the "download" folder path IS itself an existing series root
   (`_seriesService.SeriesPathExists`), rejects the whole folder `SeriesFolder` — refuses to
   treat a real library folder as a completed-download drop.
3. Parses folder name and download-item title into `ParsedEpisodeInfo`; enumerates video files
   (`DiskScanService.GetVideoFiles` + `FilterPaths`).
4. **File-lock guard** — only when **no** `downloadClientItem` is present (manual/root-folder
   scans, not tracked downloads): any locked file aborts the *entire folder* with one `FileLocked`
   rejection.
5. **Multi-season guard**: `downloadClientItemInfo.IsMultiSeason == true` rejects the whole folder
   up front with `MultiSeason` — cross-season releases are never auto-imported, full stop.
6. Runs `ImportDecisionMaker.GetImportDecisions(..., sceneSource: true)` then
   `ImportApprovedEpisodes.Import(decisions, newDownload: true, downloadClientItem, importMode)`.
7. **Folder cleanup**: `Auto` import mode resolves to `Move` (no client item, or client
   `CanMoveFiles`) or `Copy` otherwise. On `Move` with ≥1 successful import and
   `ShouldDeleteFolder(...)` true, recursively deletes the source folder (non-fatal on `IOException`).
   If **zero** results came back at all, `CheckEmptyResultForIssue` scans for dangerous/executable/
   archive files and returns a diagnostic-only rejection explaining why nothing happened.

*`ShouldDeleteFolder(dir, series)`* — the "is it safe to delete this now-processed download
folder" gate: `false` if **any** video file fails to parse; `false` if **any** video file is
determined **not** to be a sample (i.e., every remaining video must be a confirmed sample — the
real episode(s) must have already been moved out); `false` if any `.rar` file over 10 MB exists
anywhere recursively (unextracted archive); `false` on `DirectoryNotFoundException` (already gone)
or any other exception (fail-safe).

*File processing* (`ProcessFile`) runs an **ordered gauntlet before ever calling
`ImportDecisionMaker`**: filename starts with `._` → `InvalidFilePath`; extension in
`FileExtensions.DangerousExtensions` (`.arj .lnk .lzh .ps1 .scr .vbs .zipx`) → `DangerousFile`;
extension in `ExecutableExtensions` (`.bat .cmd .exe .sh`) → `ExecutableFile`; extension in the
user-configured `ConfigService.UserRejectedExtensions` (comma-list, each token trimmed and
re-prefixed with `.`) → `UserRejectedExtension`; extension blank or not in
`MediaFileExtensions.Extensions` → `UnsupportedExtension`; locked file (only checked absent a
download-client item) → `FileLocked`. Only after all six checks pass does it reach the decision
maker.

### 2.2 `ImportDecisionMaker` — the orchestrator

**File:** `MediaFiles/EpisodeImport/ImportDecisionMaker.cs`. Interface `IMakeImportDecision` exposes
four `GetImportDecisions` overloads (progressively richer parameters) plus `GetDecision(LocalEpisode,
DownloadClientItem)` for pre-built episodes (used by Manual Import's reprocess step).

**Canonical flow** (`GetImportDecisions(videoFiles, series, downloadClientItem,
downloadClientItemInfo, folderInfo, sceneSource, filterExistingFiles)`):
1. If `filterExistingFiles`, drops files already tracked for this series
   (`MediaFileService.FilterExistingFiles`, path-based).
2. **Sample pre-count optimization**: if `sceneSource` is true, actually runs `DetectSample.IsSample`
   (ffprobe-backed) against every candidate to count non-sample files; if `sceneSource` is false
   (a rescan of an already-organized folder), just uses the raw file count — this is a deliberate
   ffprobe-avoidance optimization for already-curated library folders.
3. Builds one `LocalEpisode` per file: `Series`, `DownloadClientEpisodeInfo`, `DownloadItem`,
   `FolderEpisodeInfo`, `Path`, `SceneSource`, `ExistingFile` (`series.Path.IsParentPath(file)`),
   `OtherVideoFiles` (`nonSampleVideoFileCount > 1` — "this release has more than one real video
   file", used downstream by `AggregateEpisodes`/`SceneNameCalculator`).
4. Per-file private `GetDecision(localEpisode, downloadClientItem, otherFiles)`:
   - Parses the file path (`Parser.ParsePath`), sets `Size`, resolves `ReleaseType` via a fallback
     chain (DownloadClientEpisodeInfo → FolderEpisodeInfo → FileEpisodeInfo → `Unknown`).
   - Calls `AggregationService.Augment` (runs every registered aggregator — see §2.6).
   - **If zero episodes resolved**: rejects with the first true of, in order,
     `IsPartialSeason` (checked across DownloadClientEpisodeInfo → FolderEpisodeInfo →
     FileEpisodeInfo) → `PartialSeason`; else `IsSeasonExtra` (same fallback chain) →
     `SeasonExtra`; else → `InvalidSeasonOrEpisode`.
   - **If episodes resolved**: propagates `IndexerFlags` from tracked-download release info if
     present, calls `LocalEpisodeCustomFormatCalculationService.UpdateEpisodeCustomFormats`
     (must happen *before* specs run — `UpgradeSpecification` depends on the computed score), then
     runs the full spec gauntlet via the public `GetDecision`.
   - Exception isolation: `AugmentingFailedException` → `UnableToParse`; any other exception →
     logged + `Error`, `"Unexpected error processing file"`.
5. Public `GetDecision(localEpisode, downloadClientItem)` runs **every** registered
   `IImportDecisionEngineSpecification.IsSatisfiedBy` and unions all non-null rejections into one
   `ImportDecision` — every spec always runs (no short-circuit on first rejection), so a rejected
   file's `Rejections` list can carry multiple simultaneous reasons.
6. `EvaluateSpec` wraps each spec call: any exception thrown *by a spec itself* becomes an
   `ImportRejection(DecisionError, "{SpecClassName}: {exceptionMessage}")` rather than crashing the
   batch.

**Season-pack / multi-episode-per-file / multi-series note**: `ImportDecisionMaker` produces
exactly one `LocalEpisode`/`ImportDecision` per input **file**, which may carry many `Episodes`
(a season-pack-in-one-file scenario). Genuinely **multi-series** releases are never handled here —
`DownloadedEpisodesImportService` resolves exactly one `Series` per folder/file *before*
`ImportDecisionMaker` ever runs, so cross-series content either fails series resolution
(`UnknownSeries`) or must be split by the caller into separate manual-import calls. Multi-**season**
content is blocked even earlier by the `IsMultiSeason` guard in `DownloadedEpisodesImportService`.

### 2.3 The 14 `Specifications/` — every rejection rule, verbatim

All implement `IImportDecisionEngineSpecification.IsSatisfiedBy(LocalEpisode, DownloadClientItem) →
ImportSpecDecision`. `ImportSpecDecision.Accept()` is a cached singleton; `.Reject(reason, message,
args)` builds a templated rejection.

1. **`AbsoluteEpisodeNumberSpecification`** — skipped for non-Anime series or when the naming
   format doesn't require an absolute number (`IBuildFileNames.RequiresAbsoluteEpisodeNumber()`).
   For each episode: skip if it aired &gt;1 day ago; else reject `MissingAbsoluteEpisodeNumber` if it
   has no `AbsoluteEpisodeNumber` — guards against importing a very-freshly-aired anime episode
   before metadata has back-filled its absolute number.
2. **`AlreadyImportedSpecification`** (`Priority => Database`) — skipped with no
   `downloadClientItem`. Per episode with `HasFile`, finds the latest `DownloadFolderImported` and
   `Grabbed` history rows for this exact `DownloadId`. No prior import → fine. Re-grabbed *after*
   the prior import → fine (explicit user re-grab). Imported *after* the last grab (normal case, or
   no grab record at all) → reject `EpisodeAlreadyImported` with the prior import timestamp.
   Prevents duplicate scans of the same completed-download folder from re-importing repeatedly.
3. **`EpisodeTitleSpecification`** — skipped for `ExistingFile`, `EpisodeTitleRequired == Never`, or
   a naming format that doesn't reference `{Episode Title}`. **Bulk-season exemption**:
   `EpisodeTitleRequired == BulkSeasonReleases` and all episodes share one air-date and fewer than 4
   episodes in the season share that date → skip (small same-day drops are exempt; once ≥4 episodes
   share a date it's treated as a bulk/full-season drop and the requirement applies). Per episode:
   aired &gt;48h ago → skip; blank/whitespace title → reject `TitleMissing`; title literally `"TBA"`
   → reject `TitleTba`.
4. **`FreeSpaceSpecification`** — skipped by `SkipFreeSpaceCheckWhenImporting` or `ExistingFile`.
   Checks free space on `Directory.GetParent(series.Path)` (the volume/root **above** the series
   folder). Formula: reject `MinimumFreeSpace` if
   `freeSpace < localEpisode.Size + MinimumFreeSpaceWhenImporting.Megabytes()`. Any I/O exception or
   an indeterminate free-space read is fail-open (accept).
5. **`FullSeasonSpecification`** — if `FileEpisodeInfo.FullSeason` is true (a *single* file's own
   name parsed as representing an entire season) → reject `FullSeason`, "Review file name or
   manually import."
6. **`HasAudioTrackSpecification`** — fail-open if MediaInfo is unavailable; else reject `NoAudio`
   if `MediaInfo.AudioStreams` is null/empty.
7. **`MatchesFolderSpecification`** — skipped for `ExistingFile`. Re-parses possible-scene-season-
   specials on both file and folder info. Skips if either folder or file info is null, or if the
   folder info yielded zero episodes (season-only folder name). Computes `unexpected` = file
   episodes not present in the folder's own resolved episode set; rejects `EpisodeUnexpected`
   (singular/plural message) — catches a file dropped in the wrong season/show folder.
8. **`MatchesGrabSpecification`** — skipped for `ExistingFile` or when there's no grab-history-
   derived `Release`/`EpisodeIds`. Computes `unexpected` = episodes not present in
   `Release.EpisodeIds`; rejects `EpisodeNotFoundInRelease` — catches a file that, once extracted,
   turns out to be a different episode than what was actually grabbed.
9. **`NotSampleSpecification`** — skipped for `ExistingFile`. `DetectSample.IsSample` result
   `Sample` → reject `Sample`; **`Indeterminate` is also a rejection** (`SampleIndeterminate`) — a
   conservative default, not an accept. `InvalidSeasonException` (ambiguous season count) is
   swallowed as a warning, falling through to accept.
10. **`NotUnpackingSpecification`** — skipped for `ExistingFile`. For each configured working-folder
    prefix (`ConfigService.DownloadClientWorkingFolders`, pipe-delimited, e.g. `_UNPACK_|_FAILED_`),
    walks every ancestor directory of the file. Non-Windows: any ancestor **starting with** that
    prefix is an immediate, unconditional reject `Unpacking`. Windows: same match additionally
    requires the file's last-write-time to be within the past 5 minutes (grace-period/staleness
    check instead of a hard block).
11. **`SameEpisodesImportSpecification`** (`Type => Permanent`) — wraps the general
    `SameEpisodesSpecification`: gathers every distinct existing `EpisodeFileId` referenced by the
    candidate episodes, and for each, requires *every* episode currently mapped to that existing
    file to also be present in the incoming candidate's episode set. Failing that → reject
    `ExistingFileHasMoreEpisodes` — blocks a new single-episode file from silently orphaning
    episodes that were part of a combined/multi-episode file on disk.
12. **`SplitEpisodeSpecification`** — `FileEpisodeInfo.IsSplitEpisode` (e.g. `S01E05a`/`S01E05b`
    style) → reject `SplitEpisode`, "Single episode split into multiple files" (not supported for
    auto-import).
13. **`UnverifiedSceneNumberingSpecification`** — skipped for `ExistingFile`. Any episode with
    `UnverifiedSceneNumbering == true` → reject `UnverifiedSceneMapping` (explains TheXEM's mapping
    for this specific episode hasn't been confirmed by administrators yet and needs manual input).
14. **`UpgradeSpecification`** — the critical upgrade-in-place decision, see §4 below for full
    detail. Rejections: `NotQualityUpgrade`, `NotRevisionUpgrade`, `NotCustomFormatUpgrade`,
    `NotCustomFormatUpgradeAfterRename`.

**`ImportRejectionReason` — the complete closed set (35 values, `MediaFiles/EpisodeImport/
ImportRejectionReason.cs`)**: `Unknown, FileLocked, UnknownSeries, DangerousFile, ExecutableFile,
UserRejectedExtension, ArchiveFile, SeriesFolder, InvalidFilePath, UnsupportedExtension,
PartialSeason, SeasonExtra, InvalidSeasonOrEpisode, UnableToParse, Error, DecisionError, NoEpisodes,
MissingAbsoluteEpisodeNumber, EpisodeAlreadyImported, TitleMissing, TitleTba, MinimumFreeSpace,
FullSeason, NoAudio, EpisodeUnexpected, EpisodeNotFoundInRelease, Sample, SampleIndeterminate,
Unpacking, ExistingFileHasMoreEpisodes, SplitEpisode, UnverifiedSceneMapping, NotQualityUpgrade,
NotRevisionUpgrade, NotCustomFormatUpgrade, NotCustomFormatUpgradeAfterRename, MultiSeason`.

**Supporting types**: `ImportDecision` (`LocalEpisode` + `Rejections`; `Approved =>
Rejections.Empty()`), `ImportRejection : Rejection<ImportRejectionReason>` (reason + message +
`RejectionType`, default `Permanent`), `ImportSpecDecision` (the accept/reject factory each spec
returns), `ImportResult` (wraps a decision plus either an `EpisodeFile` or error strings; its
`Result` property computes `Imported`/`Rejected`/`Skipped` — `Skipped` specifically means "the
decision was Approved but something still went wrong during the physical import"), `ImportMode`
(`Auto=0, Move=1, Copy=2` — `Auto` resolves based on `downloadClientItem.CanMoveFiles`).

### 2.4 Manual Import — full flow and API surface

**HTTP API** (`Sonarr.Api.V3/ManualImport/`):
- `GET /manualimport?folder=&downloadId=&seriesId=&seasonNumber=&filterExistingFiles=true` →
  `ManualImportController.GetMediaFiles`. If `seriesId` is given **without** `downloadId`, lists
  already-tracked files for that series/season (`ManualImportService.GetMediaFiles(seriesId,
  seasonNumber)`); otherwise browses a folder/download (`GetMediaFiles(folder, downloadId, seriesId,
  filterExistingFiles)`). Response items get `QualityWeight` computed server-side
  (`AddQualityWeight`: base weight from `Quality.DefaultQualityDefinitions` + `Revision.Real * 10` +
  `Revision.Version`) for UI sorting.
- `POST /manualimport` with `List<ManualImportReprocessResource>` →
  `ManualImportController.ReprocessItems`. For each item, calls
  `ManualImportService.ReprocessItem(path, downloadId, seriesId, seasonNumber, episodeIds,
  releaseGroup, quality, languages, indexerFlags, releaseType)` and copies the recomputed
  `SeasonNumber`/`Episodes`/`ReleaseType`/`IndexerFlags`/`Rejections`/`CustomFormats`/
  `CustomFormatScore` back onto the request item (round-trip re-decision after a user edit).
  Language/Quality/ReleaseGroup are only overwritten from the recompute if the user had left them
  unset — the user's explicit choice always wins once made.
- `GET /rename?seriesId=&seasonNumber=` and `GET /rename/bulk?seriesIds=` →
  `RenameEpisodeController` — preview-only, described in §7.

**`ManualImportResource`** (API DTO) fields: `Path`, `RelativePath`, `FolderName`, `Name`, `Size`,
`Series`, `SeasonNumber?`, `Episodes`, `EpisodeFileId?`, `ReleaseGroup`, `Quality`, `Languages`,
`QualityWeight`, `DownloadId`, `CustomFormats`, `CustomFormatScore`, `IndexerFlags`, `ReleaseType`,
`Rejections` (`{Reason, Type}` — so the UI can show *why* the automatic pipeline would have
rejected a file, letting the user override anyway).

**`ManualImportService`** (`MediaFiles/EpisodeImport/Manual/ManualImportService.cs`):

*List-building* (`GetMediaFiles`, two overloads):
- By series/season: existing DB-tracked files mapped directly (`MapItem(EpisodeFile, ...)`); when no
  specific season is requested, also disk-scans for **unmapped** video files in the series folder
  and adds bare placeholder items (`Quality = Unknown`, `Languages = [Unknown]`, no episodes, no
  rejections yet).
- By folder/download path: resolves the path from a `TrackedDownload`'s output path if a
  `downloadId` is given; single-file path → `ProcessFile`; folder path → `ProcessFolder`
  (recursive). `ProcessFolder`, if series cannot be resolved and the folder contains **more than
  100 files**, short-circuits to bare unparsed placeholder items as a performance guard against
  pathological huge folders. Otherwise it runs the **exact same** `ImportDecisionMaker
  .GetImportDecisions` pipeline as automatic import (all specs, aggregation, sample detection,
  custom-format scoring) — the only difference from automatic import is that rejections are
  *surfaced for user override* rather than enforced.

*`ReprocessItem`* — recomputes a decision after the user edits fields in the UI, without a full
disk re-scan:
- With `episodeIds` selected: builds a fresh `LocalEpisode`, applies user overrides (falling back to
  a fresh parse only for fields the user left blank/unknown), runs
  `UpdateEpisodeCustomFormats` + `AggregationService.Augment` (to backfill MediaInfo/release-history/
  subtitle info "so imported files have all additional information an automatic import would"),
  **then re-applies every user-chosen field a second time** (since `Augment`'s aggregators could
  have overwritten them), and finally calls `ImportDecisionMaker.GetDecision` — i.e. re-runs **only
  the specifications**, not aggregation, against the user-overridden episode.
- With a `seasonNumber` but **no** episodes selected: deliberately does **not** guess — returns an
  item pre-rejected `NoEpisodes`, "Episodes not selected," preserving the user's partial season
  selection in the UI rather than reverting it.
- Neither: falls through to a full `ProcessFile` re-scan.

*`Execute(ManualImportCommand)`* — the actual commit, per submitted `ManualImportFile`:
1. Builds a `LocalEpisode` directly from the **user's submitted values** (`ReleaseGroup`, `Quality`,
   `Languages`, `IndexerFlags`, `ReleaseType`) — no fallback parsing at this stage.
2. Runs `AggregationService.Augment` (backfill), then **re-applies every user override again**
   (same double-apply pattern as `ReprocessItem`).
3. Runs `UpdateEpisodeCustomFormats`.
4. **Builds a bare `ImportDecision` with zero rejections** — `new ImportDecision(localEpisode)`. This
   is the force-import bypass: the commit step never re-runs any
   `IImportDecisionEngineSpecification`; the user's explicit choice is trusted unconditionally.
5. Calls `ImportApprovedEpisodes.Import` — routed through the exact same move/copy/DB-write/
   extras/notification logic as automatic import.
6. **Post-commit bookkeeping**: untracked-download imports are grouped by `(SeriesId,
   SeasonNumber)` and fire `UntrackedDownloadCompletedEvent` (computing a common source path via
   longest-common-path) so downstream consumers treat a pile of manually-imported files like a
   completed download. Tracked-download imports are grouped by `DownloadId`; the source folder is
   deleted if `ShouldDeleteFolder` + `CanMoveFiles`; the download is marked
   `TrackedDownloadState.Imported` and fires `DownloadCompletedEvent` **only if** the count of
   actually-imported episodes is `≥ Math.Max(1, expectedEpisodeCount)` — a **partial** manual
   import leaves the tracked download not-fully-imported so it stays actionable in the queue.

**Command**: `ManualImportCommand` — `List<ManualImportFile> Files`, `ImportMode`;
`SendUpdatesToClient = true`, `RequiresDiskAccess = true`. `ManualImportFile` (the per-file override
payload): `Path`, `FolderName`, `SeriesId`, `EpisodeIds`, `EpisodeFileId?`, `Quality`, `Languages`,
`ReleaseGroup`, `IndexerFlags`, `ReleaseType`, `DownloadId` (equality is by `Path` only).

### 2.5 Sample detection — `DetectSample.cs`

`DetectSampleResult`: `Indeterminate | Sample | NotSample`.

**Stage 1 — extension/special short-circuit**: `isSpecial == true` → `NotSample` immediately;
extension `.flv` or `.strm` → `NotSample` without further checks (pointer/legacy files where
runtime is meaningless); otherwise → `Indeterminate`, proceed to stage 2.

**Stage 2 — runtime heuristic**: gets file runtime via ffprobe (`IVideoFileInfoReader.GetRunTime`
or cached `MediaInfo.RunTime`); returns `Indeterminate` if ffprobe is unavailable. Expected runtime
is the episode's own `Runtime`, falling back to the series' `Runtime`, defaulting to **45 minutes**
if that sums to zero. Minimum-allowed-runtime formula (`GetMinimumAllowedRuntime`):

| Expected runtime | Minimum allowed (sample threshold) |
|---|---|
| ≤ 3 min (anime shorts) | 15 seconds |
| ≤ 10 min (webisodes) | 90 seconds |
| ≤ 30 min | 300 seconds (5 min) |
| &gt; 30 min | 600 seconds (10 min) |

A file with exactly zero runtime is always `Sample` (treated as broken/invalid, logged as an
error). This is a **pure duration heuristic scaled to expected episode length** — there is no
fixed byte-size threshold anywhere in the sample-detection logic.

### 2.6 Aggregation — multi-source, confidence-weighted field resolution

**File:** `MediaFiles/EpisodeImport/Aggregation/AggregationService.cs`. `Augment(LocalEpisode,
DownloadClientItem)`:
1. Throws `AugmentingFailedException` if the file is a recognized media extension but **none** of
   `DownloadClientEpisodeInfo`/`FolderEpisodeInfo`/`FileEpisodeInfo` parsed at all.
2. Sets `Size`; sets `SceneName` via `SceneNameCalculator.GetSceneName` only if `SceneSource`.
3. **ffprobe gate**: only runs MediaInfo probing if the file is a media file AND (it's not an
   `ExistingFile` OR `EnableMediaInfo` is on) — new imports are always probed; files already sitting
   in the library are only re-probed if "Analyze Media Files" is enabled.
4. Runs every registered `IAggregateLocalEpisode` in ascending `Order`; each augmenter's exceptions
   are caught and logged individually — one failing augmenter never aborts the whole augment call.

**The six aggregators** (all `Order = 1` except subtitle info at `Order = 2`, guaranteeing episode
resolution happens before subtitle-title cleanup runs):

- **`AggregateEpisodes`**: decides which `ParsedEpisodeInfo` source is authoritative. If there's
  only one video file in the release **and** the file's own name is not itself a scene-style title,
  prefers the download-client-item's parse (if present, non-full-season, and
  `PreferOtherEpisodeInfo` agrees) or else the folder's parse over the raw filename parse.
  `PreferOtherEpisodeInfo` specifically declines to prefer an absolute-numbered "other" source when
  the file's own info is non-absolute (protects anime season/episode-vs-absolute-number ambiguity).
  Falls back to `ParseSpecialEpisodeTitle` if no parse exists at all, and again if the chosen source
  resolved to zero episodes but was flagged `IsPossibleSpecialEpisode`.
- **`AggregateQuality`**: delegates to `IAugmentQuality` strategies (see below), keeping the
  highest-confidence non-Unknown source, highest-confidence non-zero resolution, and — for
  revision — the higher-confidence signal, or on a confidence *tie* the numerically higher revision
  (so a detected proper/repack signal never gets clobbered by an equally-confident but
  lower-revision source).
- **`AggregateLanguage`**: seeds from `series.OriginalLanguage` (or `Unknown`), then lets
  `IAugmentLanguage` strategies override — but **only** if a strategy returns a non-empty language
  list that isn't just `[Unknown]` (a weak/absent signal never blanks out a stronger prior guess).
- **`AggregateReleaseGroup`**: fallback chain — DownloadClientEpisodeInfo (non-full-season) →
  FolderEpisodeInfo (non-full-season) → FileEpisodeInfo (full-season allowed) →
  DownloadClientEpisodeInfo (full-season allowed) → FolderEpisodeInfo (full-season allowed). Full
  seasons skip the download/folder title first because a season-pack title is less likely to
  correctly encode any *one* episode's actual release-group tag.
- **`AggregateReleaseHash`**: fallback chain FileEpisodeInfo → DownloadClientEpisodeInfo →
  FolderEpisodeInfo, each gated by `!FullSeason` (a hash is meaningless for a season pack).
- **`AggregateReleaseInfo`**: no-ops without a `downloadClientItem`; otherwise looks up `Grabbed`
  history rows for that `DownloadId` and, if any exist, sets `localEpisode.Release =
  GrabbedReleaseInfo(...)` — this feeds `MatchesGrabSpecification`.
- **`AggregateSubtitleInfo`** (`Order = 2`): only applies when the *current file itself* is a
  subtitle. No-ops if no episodes resolved. Parses subtitle language/title info via
  `LanguageParser.ParseSubtitleLanguageInformation`, then `CleanSubtitleTitleInfo`: if the parsed
  subtitle title is a substring of the episode's own file title (before or after rename), re-parses
  with the simpler `ParseBasicSubtitle` (avoids the episode's own title being misread as language
  metadata); separately strips any language tags that already appear verbatim in the episode
  file's title (avoids double-counting a language name that coincidentally appears in the show
  title).

**Quality augmenters** (`Aggregators/Augmenters/Quality/`, `Confidence` enum `Fallback < Default <
Tag < MediaInfo`, ordered ascending by `Order`):
1. `AugmentQualityFromFileName` (`Order 1`) — reads `FileEpisodeInfo.Quality`; confidence is `Tag`
   if the quality's own `SourceDetectionSource`/`ResolutionDetectionSource` was `Name`, else
   `Fallback`.
2. `AugmentQualityFromFolder` (`Order 2`) — same logic against `FolderEpisodeInfo.Quality`.
3. `AugmentQualityFromDownloadClientItem` (`Order 3`) — same logic against
   `DownloadClientEpisodeInfo.Quality`.
4. `AugmentQualityFromMediaInfo` (`Order 4`) — parses the MediaInfo container **title** field (if
   present) as a quality-name string for the *source* signal (confidence `MediaInfo` only if the
   parsed source isn't Unknown and came from a name-style match); resolution comes purely from
   pixel dimensions: `≥3200×` or `≥2100y` → 2160p, `≥1800×`/`≥1000y` → 1080p, `≥1200×`/`≥700y` →
   720p, `≥1000×`/`≥560y` → 576p, else (any positive dimensions) → 480p — all at `Confidence
   .MediaInfo`.
5. `AugmentQualityFromReleaseName` (`Order 5`) — re-parses quality from the **grab history's**
   stored `SourceTitle` (looked up by `DownloadId`) — a last-resort source distinct from the
   current filename, useful when the file itself was renamed by the download client before Sonarr
   saw it.

**Language augmenters** (`Aggregators/Augmenters/Language/`, `Confidence` enum `Fallback < Default <
Filename < MediaInfo`): `AugmentLanguageFromFileName` (`Order 1`, confidence `Filename`, subtracts
any language incidentally detected in the episode's own *title* text to avoid false positives),
`AugmentLanguageFromDownloadClientItem`, `AugmentLanguageFromFolder`, and
`AugmentLanguageFromMediaInfo` (`Order 4`, confidence `MediaInfo` — maps each distinct MediaInfo
audio-stream language via `IsoLanguages.Find`).

### 2.7 `LocalEpisode` — the working aggregate (full field list)

`Path`, `Size`, `FileEpisodeInfo`, `DownloadClientEpisodeInfo`, `DownloadItem`, `FolderEpisodeInfo`,
`Series`, `Episodes`, `OldFiles` (replaced-file bookkeeping for upgrades), `Quality`, `Languages`,
`IndexerFlags`, `ReleaseType`, `MediaInfo`, `ExistingFile` (bool), `SceneSource` (bool),
`ReleaseGroup`, `ReleaseHash`, `SceneName`, `OtherVideoFiles` (bool), `CustomFormats`,
`CustomFormatScore`, `OriginalFileNameCustomFormats`, `OriginalFileNameCustomFormatScore`,
`Release` (`GrabbedReleaseInfo`), `ScriptImported` (bool), `FileNameBeforeRename`,
`FileNameUsedForCustomFormatCalculation`, `ShouldImportExtras` (bool), `PossibleExtraFiles`,
`SubtitleInfo`. Computed: `SeasonNumber` (throws `InvalidSeasonException` if episodes span 0 or
&gt;1 distinct seasons), `IsSpecial` (`SeasonNumber == 0`), `GetSceneOrFileName()`,
`ToEpisodeFile()` (builds the persisted `EpisodeFile` entity — sets `RelativePath` only if the
current path is actually inside `Series.Path`, resolves `ReleaseType` via the same fallback chain
if still `Unknown`), `GetOriginalFilePath()` (best-effort relative path used for Custom Format
"before rename" comparisons).

---

## 3. File Organization / Naming Engine

### 3.1 `Organizer/FileNameBuilder.cs` — the token-substitution engine

Everything renders through **one regex-driven token engine**, not hardcoded format branches.
Central regex `TitleRegex` captures an optional **prefix** (`[`, `(`, space, `-`, `.`, `_`) and
**suffix** (`]`, `)`, space, `-`, `.`) around each `{token[:customFormat]}` — these decorations are
only emitted if the token's resolved value is non-empty, so `[{Release Group}]` disappears
entirely (not `[]`) when there's no release group. An internal `separator` capture lets hyphenated
token variants (`{Series-Title}`) replace spaces in the *value* with that character. `{{`/`}}`
escapes to a literal brace.

**Complete enumerated token list** (50 tokens across 8 families, every one confirmed against the
switch/dictionary in source):

*Series* (all support `:N` truncation, positive = truncate-from-end-with-ellipsis, negative =
truncate-from-start): `{Series Title}`, `{Series CleanTitle}` (punctuation-stripped, diacritics
removed), `{Series TitleYear}`, `{Series CleanTitleYear}`, `{Series TitleWithoutYear}`, `{Series
CleanTitleWithoutYear}`, `{Series TitleThe}` (moves a leading "The"/"A"/"An" to `, The` suffix),
`{Series CleanTitleThe}`, `{Series TitleTheYear}`, `{Series CleanTitleTheYear}`, `{Series
TitleTheWithoutYear}`, `{Series CleanTitleTheWithoutYear}`, `{Series TitleFirstCharacter}` (first
alphanumeric of the The-shifted title, uppercased, diacritics stripped, falls back to `_`),
`{Series Year}` (no truncation support).

*IDs*: `{ImdbId}`, `{TvdbId}`, `{TvMazeId}` (empty if ≤0), `{TmdbId}` (empty if ≤0).

*Numbering* (see §3.2): `{Season}`, `{Episode}`, internally synthesized `{Season Episode1..N}`
(one per distinct season/episode block in the pattern), `{absolute}`, internally synthesized
`{Absolute Pattern1..N}`.

*Air date*: `{Air Date}` — dashes replaced with spaces, or literal `"Unknown"` if absent.

*Episode title*: `{Episode Title}` (multi-episode joiner `+`), `{Episode CleanTitle}` (joiner
`and`, per-title multi-part suffix stripping like `(1)`/`Part 2` before joining).

*Episode file*: `{Original Title}` (scene name, or current filename if fallback allowed),
`{Original Filename}` (current on-disk filename, fallback-gated), `{Release Group}` (renders the
literal string `"Sonarr"` if empty **and** undecorated by prefix/suffix, else empty so decorated
forms vanish cleanly), `{Release Hash}`.

*Quality*: `{Quality Full}` (= Title + Proper/vN + REAL combined), `{Quality Title}`, `{Quality
Proper}` (`"Proper"` for non-anime revision &gt;1, or `"vN"` for anime), `{Quality Real}` (`"REAL"` if
`Revision.Real > 0`).

*MediaInfo* (only emitted if `MediaInfo != null`): `{MediaInfo Video}` (alias of VideoCodec),
`{MediaInfo VideoCodec}`, `{MediaInfo VideoBitDepth}` (defaults `"8"`), `{MediaInfo Audio}` (alias
of AudioCodec), `{MediaInfo AudioCodec}`, `{MediaInfo AudioChannels}` (e.g. `5.1`),
`{MediaInfo AudioLanguages}` (bracketed `[XX+YY]` ISO list, skips rendering if English-only),
`{MediaInfo AudioLanguagesAll}`, `{MediaInfo SubtitleLanguages}`, `{MediaInfo
SubtitleLanguagesAll}`, `{MediaInfo Simple}` (`"{VideoCodec} {AudioCodec}"`), `{MediaInfo Full}`
(`"{VideoCodec} {AudioCodec}{AudioLanguages} {SubtitleLanguages}"`), `{MediaInfo
VideoDynamicRange}` (requires MediaInfo schema ≥5), `{MediaInfo VideoDynamicRangeType}` (requires
schema ≥11).

*Custom formats*: `{Custom Formats}` (space-joined list of matched, rename-eligible formats;
optional `:filter` — leading `-` = exclusion list, else inclusion list, comma-separated format
names), `{Custom Format}` (singular, **requires** a `:Name` argument, returns that one format's
string or empty).

**Token mechanics**: case of the *token name itself* controls output casing (`{series title}` →
force-lowercase value, `{SERIES TITLE}` → force-uppercase, `{Series Title}` → natural casing) —
token lookup is case- and separator-insensitive via `FileNameBuilderTokenEqualityComparer`
(strips whitespace/underscores/non-word chars before hashing, so `{series-title}` ==
`{SERIES_TITLE}` == `{Series Title}` as dictionary keys).

**Illegal-character handling**: `BadCharacters = \ / < > ? * | "` map to `GoodCharacters = + + "" ""
! - "" ""` respectively (positional substitution table) when `ReplaceIllegalCharacters` is true;
when false, all of them are simply deleted. **Colon is handled separately** via
`ColonReplacementFormat`: `Delete` (removed), `Dash` (`:`→`-`), `SpaceDash` (`:`→` -`),
`SpaceDashSpace` (`:`→` - `), `Smart` (default — `": "` becomes `" - "`, any remaining bare `:`
becomes `-`), `Custom` (user-supplied free-text string). Windows reserved device names
(`aux|com[1-9]|con|lpt[1-9]|nul|prn` at the start of a folder name) are rewritten post-cleanup.

**Multi-episode style** (`MultiEpisodeStyle` enum, runtime default `PrefixedRange`):
`Extend=0` (`S01E01-02-03`, bare-hyphen episode extension — fallback default), `Duplicate=1`
(`S01E01S01E02S01E03`, full block repeated per episode), `Repeat=2` (`S01E01E02E03`, episode marker
repeated), `Scene=3` (`S01E01-E02-E03`, forces a literal hyphen before repeating), `Range=4`
(`S01E01-03`, collapsed first-last, bare hyphen, no repeated `E`), `PrefixedRange=5`
(`S01E01-E03`, collapsed first-last, keeps the episode-separator letter — **this is the actual
runtime default** per `NamingConfig.Default`). An analogous but distinct switch exists for anime
absolute numbering, where `Range` and `PrefixedRange` are functionally identical.

**Numbering-format auto-selection**: Standard (`{season}`/`{episode}` tokens) is used unless
`series.SeriesType == Daily` and the episode's season &gt; 0 (season 0/specials of a daily series
still use Standard), in which case `DailyEpisodeFormat` (built around `{Air Date}`) is used; or
unless `SeriesType == Anime` and every episode has an absolute number (or the pattern doesn't
require one), in which case `AnimeEpisodeFormat` (built around `{absolute}`) is used.
`SeriesTypes` enum: `Standard=0, Daily=1, Anime=2`.

**Truncation**: positive `:N` truncates from the end + appends an ellipsis marker (trimming
trailing space/dot first); **negative** `:N` truncates from the **start** instead (reverse, cut,
reverse, prepend ellipsis). Episode-title truncation specifically computes the max allowed length
as the remaining path-segment budget (`LongPathSupport.MaxFileNameLength`, further capped by any
per-token numeric suffix) minus the byte-length of the already-resolved rest of the pattern segment
— falls through several strategies (full joined titles → `first...last` → `first...` → single
title verbatim → hard-truncate-with-ellipsis). Length math uses **byte count**, not character
count. `BuildFilePath` additionally subtracts the season-folder path's byte length from
`LongPathSupport.MaxFilePathLength` before generating the filename, so the combined full path also
respects a max total length.

**"Don't rename" mode**: if `NamingConfig.RenameEpisodes == false`, the entire builder is bypassed
— it returns the scene name / current filename verbatim (illegal-character-cleaned only). This is
a top-level boolean, not a naming pattern choice.

### 3.2 Folder structure — series/season/specials

- **Series folder**: `SeriesFolderFormat` (default `"{Series Title}"`) — a **separate, simpler**
  token pass restricted to series/ID tokens only (no episode/quality/mediainfo/custom-format
  tokens available at all in folder patterns).
- **Season folder**: `SeasonFolderFormat` (default `"Season {season}"`) for season ≥ 1;
  `SpecialsFolderFormat` (default `"Specials"`) is used instead whenever `seasonNumber == 0`.
- Folder-format validators require the season-folder pattern to contain
  `\{season(\:\d+)?\}` and the series-folder pattern to reference some series-title token variant;
  the specials-folder pattern only needs to not collide with the disk scanner's own filtered-
  subfolder name list (`DiskScanService.FilteredSubFolderMatches`) — so a specials-folder pattern
  that happens to resolve to a scanner-ignored name (e.g. one of the extras-subfolder exclusion
  names) is rejected, since imported files there would become invisible to future scans.
- `RootFolderService.GetUnmappedFolders` derives its scan depth from how many path separators
  appear in the **series** folder format — a two-level series-folder pattern makes unmapped-folder
  detection look two levels deep for candidate series directories.

### 3.3 `NamingConfig` — persisted settings (complete field list, `Organizer/NamingConfig.cs`)

| Field | Type | Default |
|---|---|---|
| `RenameEpisodes` | bool | `false` |
| `ReplaceIllegalCharacters` | bool | `true` |
| `ColonReplacementFormat` | enum | `Smart` |
| `CustomColonReplacementFormat` | string | `""` |
| `MultiEpisodeStyle` | enum | `PrefixedRange` |
| `StandardEpisodeFormat` | string | `"{Series Title} - S{season:00}E{episode:00} - {Episode Title} {Quality Full}"` |
| `DailyEpisodeFormat` | string | `"{Series Title} - {Air-Date} - {Episode Title} {Quality Full}"` |
| `AnimeEpisodeFormat` | string | `"{Series Title} - S{season:00}E{episode:00} - {Episode Title} {Quality Full}"` |
| `SeriesFolderFormat` | string | `"{Series Title}"` |
| `SeasonFolderFormat` | string | `"Season {season}"` |
| `SpecialsFolderFormat` | string | `"Specials"` |

`NamingConfigService.GetConfig()` lazily seeds this single-row settings table with
`NamingConfig.Default` on first access (double-checked-locked insert) rather than via a migration
seed row; `Save` is a plain upsert with no versioning/history. Legacy fields like
`IncludeSeriesTitle`/`Separator`/`NumberStyle` do **not** exist in the current codebase — fully
superseded by the freeform token-pattern strings with inline formatting semantics.

### 3.4 Validation — two independent layers

**Structural** (`FileNameValidation.cs`, FluentValidation rule-builders): each of Standard/Daily/
Anime episode formats must contain a season+episode combo, **or** its own special signal (Air Date
for Daily, Absolute Episode for Anime), **or** an `{Original Title}`/`{Original Filename}` token
(escape hatch — original-name preservation guarantees uniqueness a different way). Series-folder
format must reference a series-title token; season-folder format must reference `{season}`;
specials-folder format must not collide with scanner-excluded names; the custom colon-replacement
string itself is checked against `IllegalColonCharactersValidator` (rejects the standard bad
characters **plus** a literal colon, since the replacement string could otherwise reintroduce the
very character it's meant to replace).

**Semantic round-trip** (`FileNameValidationService.cs`): generates a **sample** filename from the
pattern, then feeds it back through Sonarr's own `Parser.ParsePath`/`ParseTitle` and confirms the
parser recovers the *same* season/episode numbers (or air date, or absolute number) that were used
to generate it. This catches patterns that are token-syntactically valid but produce filenames
Sonarr's own parser can't correctly re-identify later (e.g. ambiguous separator choices).

### 3.5 Naming preview (`FileNameSampleService.cs`)

Eight `Get*Sample` methods (`GetStandardSample`, `GetMultiEpisodeSample`, `GetDailySample`,
`GetAnimeSample`, `GetAnimeMultiEpisodeSample`, `GetSeriesFolderSample`, `GetSeasonFolderSample`,
`GetSpecialsFolderSample`) power the live settings-page preview. Uses fixed fake fixtures (a series
titled `"The Series Title's!"` to exercise apostrophe/"The"-prefix/punctuation handling; episode
titles like `"Episode Title (1)"` to exercise multi-part-suffix stripping; representative
MediaInfo/CustomFormat fixtures) — swallows `NamingFormatException` and returns an empty string so
an in-progress/invalid pattern shows a blank preview instead of crashing the settings page.

---

## 4. Upgrade-in-Place — what happens to the existing file

**`UpgradeSpecification`** (import decision spec — full logic, `Specifications/
UpgradeSpecification.cs`) is what *decides* whether a new file is an upgrade, evaluated per
episode that already has a file (`EpisodeFileId > 0`):

1. **Quality tier compare** via `QualityModelComparer` bound to the series' quality profile (a
   profile-relative rank, not a flat numeric compare). Strictly lower → immediate reject
   `NotQualityUpgrade`; this short-circuits every other check for that episode.
2. **Same-tier revision check** (only when quality tier is equal): if `DownloadPropersAndRepacks !=
   DoNotPrefer` and the new file's `Revision` compares lower than the existing file's → reject
   `NotRevisionUpgrade`. Disabling proper/repack preference makes revision differences irrelevant
   at this stage entirely.
3. **Same-tier Custom Format score check**: compares `localEpisode.CustomFormatScore` (computed
   against the **final destination/renamed filename**) against the existing file's freshly
   recomputed score. If the new score is lower:
   - If the score computed against the **original, pre-rename** filename (`localEpisode
     .OriginalFileNameCustomFormatScore`) *would* have beaten the existing file, but the
     post-rename score doesn't → the more specific `NotCustomFormatUpgradeAfterRename` rejection
     fires, with a message explicitly distinguishing "AfterRename... do not improve... even though
     BeforeRename... did." This is a deliberate diagnostic callout for the case where Sonarr's own
     renaming is what caused a Custom Format signal to be lost (e.g. a format that matches on
     release-group text present in the original name but not in the library naming template).
   - Otherwise → the generic `NotCustomFormatUpgrade`.
4. If every already-linked episode clears all three checks (or has no resolvable existing file
   row), the spec accepts.

**Net rule**: a strictly higher quality tier always wins outright regardless of revision/custom
format; an equal tier requires a non-decreasing revision (when proper/repack preference is on) AND
a non-decreasing Custom Format score computed against the eventual renamed filename.

**Physical file replacement** (`MediaFiles/UpgradeMediaFileService.cs`, `IUpgradeMediaFiles
.UpgradeEpisodeFile`), invoked from `ImportApprovedEpisodes` only when `newDownload == true`:
1. Collects every **distinct existing** episode file referenced by the incoming episodes (handles
   both "one new file replaces one old file" and "one new season-pack file replaces several old
   per-episode files" shapes).
2. **Data-loss guard**: if there are existing files to replace but the series' root folder is
   missing from disk, throws `RootFolderNotFoundException` **before touching anything** — protects
   against deleting the old file when the mount is simply unmounted/disconnected.
3. For each existing file still present on disk: sends it to the **recycle bin**
   (`IRecycleBinProvider.DeleteFile`, subfolder computed relative to the root so season-subfolder
   structure is preserved inside the bin). If already missing from disk, just logs a warning (no
   recycle-bin call — nothing to move).
4. Records each replacement as `DeletedEpisodeFile(file, recycleBinPath)` on
   `moveFileResult.OldFiles` (and mirrors it onto `localEpisode.OldFiles`) —
   `recycleBinPath` is `null` when the recycle bin is disabled (hard-deleted) or the file was
   already absent.
5. Deletes the old file's DB row via `MediaFileService.Delete(file, DeleteMediaFileReason.Upgrade)`
   — this fires `EpisodeFileDeletedEvent(reason: Upgrade)`, which cascades to the extras pipeline
   (associated subtitle/other/metadata extras for that old file are *also* recycle-binned) and to
   the empty-folder cleanup handler.
6. Places the new file (`CopyEpisodeFile` if `copyOnly`, i.e. the source is still being seeded/
   read-only; otherwise `MoveEpisodeFile`).

**There is no separate "upgrade retention policy"** — the old file always goes through the exact
same recycle-bin-or-permanent-delete logic as any other delete, gated purely by whether
`ConfigService.RecycleBin` is configured.

**Season-pack upgrade type** (`MediaFiles/SeasonPackUpgradeType.cs`, config-only enum surfaced via
`ConfigService.SeasonPackUpgrade`/`SeasonPackUpgradeThreshold`): `All=0`, `Threshold=1`, `Any=2` —
governs season-pack-vs-individual-episode-file upgrade eligibility at the release-decision layer
(before download), with `SeasonPackUpgradeThreshold` (default `100.0`, a percentage) used when the
mode is `Threshold`. This enum exists in `MediaFiles` but the threshold/percentage comparison logic
itself lives in the grab-time decision engine, not in the import-time specs read for this report.

---

## 5. MediaManagement Settings — every property

Complete enumeration of `Configuration/IConfigService.cs` media-management-relevant properties (all
backed by a key/value table, cached, with hard defaults baked into `ConfigService.cs`):

| Setting | Type | Default | Behavior |
|---|---|---|---|
| `AutoUnmonitorPreviouslyDownloadedEpisodes` | bool | `false` | Auto-unmonitor an episode instead of re-grabbing forever once a download for it has already completed/superseded. |
| `RecycleBin` | string | `""` | Destination path for soft-deletes. Empty = disabled (hard delete everywhere). |
| `RecycleBinCleanupDays` | int | `7` | Age (days) after which recycle-bin contents auto-purge. `0` disables automatic cleanup (manual "empty" still works). |
| `DownloadPropersAndRepacks` | enum `ProperDownloadTypes` | `PreferAndUpgrade` | Whether Proper/Repack releases are preferred+auto-upgraded, preferred-only, or ignored (`DoNotPrefer`). |
| `CreateEmptySeriesFolders` | bool | `false` | Create the series folder on disk immediately when a series is added, even with zero episodes downloaded. |
| `DeleteEmptyFolders` | bool | `false` | After import/scan/delete, prune now-empty season/series subfolders (and the series folder itself if fully empty). |
| `FileDate` | enum `FileDateType` | `None` | Stamp imported file mtimes from `LocalAirDate`, `UtcAirDate`, or leave untouched. |
| `SkipFreeSpaceCheckWhenImporting` | bool | `false` | Bypasses `FreeSpaceSpecification` entirely. |
| `MinimumFreeSpaceWhenImporting` | int (MB) | `100` | Minimum free space required on the destination volume, on top of the incoming file's own size. |
| `CopyUsingHardlinks` | bool | `true` | When the source file is read-only (e.g. still seeding), use hardlink-or-copy instead of move; also governs extras copy-vs-hardlink when source is read-only. |
| `EnableMediaInfo` | bool | `true` | Run ffprobe on imported files for codec/resolution/audio metadata; gates re-probing of already-library files. |
| `UseScriptImport` / `ScriptImportPath` | bool / string | `false` / `""` | Delegate the actual move/rename to an external script instead of Sonarr's built-in mover. |
| `ImportExtraFiles` | bool | `false` | Master switch for importing sidecar files (subtitles/nfo/images/etc.) alongside a video during manual/completed-download import. |
| `ExtraFileExtensions` | string | `"srt"` | Comma-separated extension allowlist for extras eligible for import. |
| `RescanAfterRefresh` | enum `RescanAfterRefreshType` | `Always` | Whether a disk rescan auto-runs after a series metadata refresh (`Always` / `AfterManual` / `Never`). |
| `EpisodeTitleRequired` | enum `EpisodeTitleRequiredType` | `Always` | Whether a release must carry an episode title to import (`Always` / `BulkSeasonReleases` / `Never`). |
| `UserRejectedExtensions` | string | `""` | User-defined always-reject extension blocklist, beyond the built-in dangerous/executable sets. |
| `SeasonPackUpgrade` | enum `SeasonPackUpgradeType` | `All` | Season-pack-vs-individual-episode upgrade eligibility mode. |
| `SeasonPackUpgradeThreshold` | double (%) | `100.0` | Threshold used when `SeasonPackUpgrade == Threshold`. |
| `SetPermissionsLinux` | bool | `false` | Master chmod/chown toggle on Linux/macOS (Mono path); no effect on Windows. |
| `ChmodFolder` | string (octal) | `"755"` | Permission string applied uniformly to **both** files and folders on non-Windows (despite the "Folder" name). |
| `ChownGroup` | string | `""` | Group name/GID to chown imported files/folders to; empty = don't change group ownership. |

Also present but not media-management-specific: download-client working-folder markers
(`DownloadClientWorkingFolders`, default `"_UNPACK_|_FAILED_"`, pipe-delimited, drives
`NotUnpackingSpecification`), `DownloadClientHistoryLimit`, `EnableCompletedDownloadHandling`,
`AutoRedownloadFailed`, `AutoRedownloadFailedFromInteractiveSearch`.

**`FileDateType`** (`MediaFiles/FileDateType.cs`): `None=0, LocalAirDate=1, UtcAirDate=2`.
**`EpisodeTitleRequiredType`**: `Always=0, BulkSeasonReleases=1, Never=2`.
**`RescanAfterRefreshType`**: `Always, AfterManual, Never`.

`ConfigFileProvider.cs` is a **separate**, lower-level provider for bootstrap/host `config.xml`
(port, auth, logging, Postgres connection, update channel) — architecturally distinct from the
media-management settings above, though it uses the same reflect-over-properties/string-diff/
persist-on-change pattern for its own `SaveConfigDictionary`.

---

## 6. Root Folders & Per-Series Paths

**`RootFolder`** (`RootFolders/RootFolder.cs`): `Path`, `Accessible` (bool, runtime-computed),
`IsEmpty`, `FreeSpace`/`TotalSpace` (long?, bytes), `UnmappedFolders`.

**`RootFolderService`**:
- `Add(RootFolder)` validates, in order: path is non-empty and rooted (else `ArgumentException`);
  the folder physically exists (else `DirectoryNotFoundException`); it isn't already registered
  (else `InvalidOperationException`); it is writable by the current process user (else
  `UnauthorizedAccessException` naming `Environment.UserName`).
- `Remove(id)` deletes the DB row and clears the internal cache — **never touches disk**.
- Detail population (`GetDetails`) runs in a `Task.Run(...).Wait(timeout ? 5000 : -1)` — bounded to
  5 seconds in list-view contexts, unbounded when fetching a single root folder by ID.
- **Unmapped-folder detection** (`GetUnmappedFolders`): lists subdirectories at the depth implied by
  the configured `SeriesFolderFormat`'s path-separator count, subtracts every path matching an
  existing series' `Path`, and filters out a hard-coded set of OS/NAS housekeeping folder names:
  `$recycle.bin`, `system volume information`, `recycler`, `lost+found`, `.appledb`,
  `.appledesktop`, `.appledouble`, `@eadir`, `.grab`. Results sort case-insensitively by name.
- `GetBestRootFolderPath(path)` (cached 1 day per input path): finds the longest registered root
  folder path that is a parent of the given path; falls back to the OS-derived parent directory if
  no root folder matches at all.
- `AllWithUnmappedFolders()` wraps each folder's detail computation in try/catch so one unreachable
  root folder (e.g. a disconnected NAS mount) doesn't block the whole list from loading.

---

## 7. Extras (Subtitles / NFO / Metadata) on Import

A clean plugin-style pipeline: every extra-file "manager" implements `IManageExtraFiles`
(`Extras/Files/ExtraFileManager.cs`), each declaring an `Order`, and `ExtraService`
(`Extras/ExtraService.cs`) tries them **in order** for both "new import" and "existing file
discovery" scans — the **first** manager whose `CanImportFile` returns true claims each candidate
file.

**Manager order**: `MetadataService` (`Order 0`) → `SubtitleService`/`ExistingSubtitleImporter`
(`Order 1`) → `OtherExtraService`/`ExistingOtherExtraImporter` (`Order 2`, unconditional catch-all
— `CanImportFile` always returns `true`).

**`ExtraFileManager<T>` base** (`Extras/Files/ExtraFileManager.cs`) provides:
- `ImportFile`: transfers a candidate file to sit next to the episode file, named
  `<episodeFileBaseName><suffix><extension>`. Uses a plain `Move` normally, or
  `HardLinkOrCopy`/`Copy` (per `CopyUsingHardlinks`) when the source is read-only.
- `MoveFile`: renames/relocates an already-tracked extra file to follow an episode-file rename.

**Discovery vs. import**: `ExistingSubtitleImporter`/`ExistingOtherExtraImporter`/
`ExistingMetadataImporter` run on `SeriesScannedEvent` against files already sitting on disk
(collected as "possible extra files" — every non-video file under the series path, filtered by
`DiskScanService.FilterPaths`); `SubtitleService`/`OtherExtraService`/`MetadataService`'s
`ImportFiles` run at actual new-download-import time via `ExtraService.ImportEpisode`, scanning the
**source download folder** (not the library folder) for files with a wanted extension
(`ConfigService.ExtraFileExtensions`) next to the video, gated entirely by
`ConfigService.ImportExtraFiles`.

### 7.1 Subtitles (`Extras/Subtitles/`)

**Extensions** (`SubtitleFileExtensions.cs`): `.aqt .ass .idx .jss .psb .rt .smi .srt .ssa .sub
.txt .utf .utf8 .utf-8 .vtt`.

**`SubtitleFile`** fields: `Language` (enum), `Copy` (int, disambiguates multiple tracks of the
same language), `LanguageTags` (`List<string>` — free-form tag vocabulary, **not** individual
booleans: `forced`, `foreign`, `default`, `cc`, `psdh`, `sdh`), `Title` (optional embedded
uploader/release-group label), computed `AggregateString = Language + Title + LanguageTags +
Extension` (groups "the same logical subtitle track" across renames/copies).

**Matching algorithm** (`SubtitleService.ImportFiles`), applied to candidate subtitle files found
next to the source video:
1. **Filename-prefix match**: subtitle filename starts with the video's own base filename.
2. **Season/episode-number match**: `Parser.ParsePath` the subtitle file itself; if its season and
   episode numbers exactly match the video's parsed info, it's a match.
3. **Sole-video fallback**: if *no* subtitle matched by name/number but the source folder contains
   exactly one non-sample video file (samples filtered via `DetectSample`), **any** subtitle file
   present is imported and a warning is logged ("Imported any available subtitle file for
   episode"). If there are more than 2 video files total in the folder, this fallback is skipped
   entirely (ambiguous — too many candidates).
4. Matched files are grouped by `AggregateString`; within a group, sequential copies are numbered.
5. Suffix construction (`GetSuffix`): `.{title}[- {copy}]` if a title was recovered (copy count
   appended only when there are multiple copies of that title), else `.{copy}` if multiple
   untitled copies exist; then `.{isoCode}` (two-letter, via `IsoLanguages`) if the language is
   known; then `.{tag1}.{tag2}...` for any recovered language tags — producing filenames like
   `Episode.en.forced.srt` or `Episode.GroupName - 2.fr.srt`.
6. Language/tags are extracted via `LanguageParser.ParseSubtitleLanguage`/`ParseLanguageTags`
   (§1.3).

`MoveFilesAfterRename` re-derives the identical grouping/suffix logic to keep subtitle filenames in
sync when the parent episode file itself gets renamed (naming-scheme change or series rename).

### 7.2 Other extras (`Extras/Others/`)

`OtherExtraFile` is a bare `ExtraFile` subclass (no extra properties) — the catch-all bucket for
sidecar files that aren't subtitles and aren't Sonarr-recognized metadata (arbitrary `.nfo`,
images, etc. that a user's own tooling placed alongside the release).

`OtherExtraService.ImportFiles` matches candidates the same way as subtitles (filename-prefix or
season/episode-number match), with two extra rules: **only the first `.nfo` per import batch is
kept** (`hasNfo` flag — arbitrary custom NFO content isn't safely mergeable across multiple
candidates), and non-nfo collisions get a numeric copy-suffix (`.1`, `.2`, ...).

`OtherExtraFileRenamer` is a **collision-avoidance** utility invoked by `MetadataService` before it
writes any *generated* metadata/image file: if a pre-existing `OtherExtraFile` DB row already
claims the exact path Sonarr wants to write to (a user's own sidecar file happens to occupy that
filename), the existing file is renamed to `<path>-orig` on disk (recycling any prior `-orig`
first) rather than being silently overwritten.

### 7.3 Metadata (`Extras/Metadata/`)

`MetadataType` enum: `Unknown=0, SeriesMetadata=1, EpisodeMetadata=2, SeriesImage=3,
SeasonImage=4, EpisodeImage=5`. `MetadataFile` extends `ExtraFile`, adding `Hash` (SHA-256 of
contents, used to detect no-op rewrites) and `Consumer` (which metadata plugin produced it).

Four built-in consumers under `Consumers/`: **Plex**, **Xbmc/Kodi** (incl. `KodiEpisodeGuide`,
`XbmcNfoDetector`), **Wdtv**, **Roksbox** — each implementing `IMetadata` with its own NFO/image
file-shape conventions. `MetadataService` (`Order 0`) drives generation on every lifecycle hook:
after media-cover update (series images), after series scan (full metadata + season images +
per-episode-file metadata/images), after episodes-imported-batch (series-level metadata refresh),
after a single episode import (episode metadata + image), after episode-folder creation
(series/season metadata for freshly-created folders). Duplicate metadata rows are detected and the
extras are recycle-binned + DB-deleted. `MetadataService.CanImportFile` always returns `false` and
`ImportFiles` always returns empty — metadata is **never** "imported" from arbitrary disk files the
way subtitles/others are; it is purely consumer-generated. Discovery of pre-existing (non-Sonarr-
generated) on-disk metadata is a separate concern (`ExistingMetadataImporter`).

### 7.4 Deletion cascade

`Extras/Files/ExtraFileService.cs` (generic, shared by all three managers): on
`EpisodeFileDeletedEvent`, **unless** the reason is `NoLinkedEpisodes` (a DB-only orphan cleanup,
no disk action), sends the associated extra files to the **recycle bin** before deleting their DB
rows — so subtitle/nfo/metadata sidecars follow the exact same soft-delete lifecycle as the video
file itself whenever it's replaced or manually deleted.

---

## 8. Disk Scan & Manual File-Change Detection

**`MediaFiles/DiskScanService.cs`** — `Scan(Series)` (also `IExecute<RescanSeriesCommand>` for the
manual "Rescan" UI action, single series or all):

1. If the series folder doesn't exist on disk: publishes `SeriesScanSkippedEvent
   (RootFolderDoesNotExist)` if the root folder itself is gone, or `(RootFolderIsEmpty)` if the
   root exists but is empty — both protect against treating a disconnected mount as "everything
   deleted." If the root is fine and just this series' folder is missing, optionally creates it
   (`CreateEmptySeriesFolders`, skipped if `DeleteEmptyFolders` is *also* true since it would be
   immediately re-deleted), runs `MediaFileTableCleanupService.Clean` with an empty on-disk file
   list (orphans any DB records), and returns early.
2. Normal path: lists video files (filtered through extras-subfolder/hidden-file exclusion
   regexes), cleans the DB table against the live on-disk set, computes newly-unmapped files,
   **runs them through the exact same `ImportDecisionMaker`/`ImportApprovedEpisodes` pipeline as
   a completed download** — this is how Sonarr detects and imports a file a user dropped into the
   library folder by hand.
3. **Re-probes existing tracked files whose size changed** on disk (compares live file size against
   the DB-stored `Size`; triggers a MediaInfo refresh on mismatch, or a plain size update if the
   MediaInfo refresh itself didn't already persist a change) — this is the mechanism for detecting
   a manually-replaced file in place without going through the import pipeline again.
4. Prunes empty folders (`DeleteEmptyFolders`-gated).
5. Publishes `SeriesScannedEvent(series, possibleExtraFiles)` — the trigger for all three
   existing-extras importers (§7).

**Exclusion regexes** (all compiled, case-insensitive): extras-subfolder names (`extras`,
`extrafanart`, `behind the scenes`, `deleted scenes`, `featurettes`, `interviews`, `other`,
`scenes`, `samples`, `shorts`, `trailers`, `theme[-_. ]music`, `backdrops`); general excluded
subfolders (`@eadir`, `.@__thumb`, `plex versions`, any dot-hidden folder); excluded extra-file
name suffixes (Kodi/Plex convention: `-trailer`, `-other`, `-behindthescenes`, `-deleted`,
`-featurette`, `-interview`, `-scene`, `-short`); excluded files (dotfiles, `_`-prefixed,
`.unmanic`, `.DS_Store`, `Thumbs.db`).

**`MediaFileTableCleanupService.Clean`** — the DB-orphan reconciliation logic: a tracked file whose
resolved path is no longer in the on-disk set is deleted with reason `MissingFromDisk`; a tracked
file that exists on disk but has **no episode** referencing its ID is deleted with reason
`NoLinkedEpisodes`; a reverse pass then zeroes out `EpisodeFileId` on any episode still pointing at
a now-deleted file row. Per-file exceptions are caught and logged without aborting the whole
series' cleanup pass.

---

## 9. Recycle Bin, Delete Reasons, and Permissions

**`RecycleBinProvider`** (`MediaFiles/RecycleBinProvider.cs`) — `DeleteFolder`, `DeleteFile`,
`Empty()`, `Cleanup()` (also `IExecute<CleanUpRecycleBinCommand>`):
- Enabled/disabled purely by whether `ConfigService.RecycleBin` is non-blank. Disabled → every
  delete call falls through to a permanent `_diskProvider.DeleteFolder`/`DeleteFile`.
- `DeleteFolder`: moves the whole folder to `<RecycleBin>/<original name>`, then stamps the folder
  and every contained file's last-write-time to `UtcNow` (this timestamp is what `Cleanup()` later
  ages against).
- `DeleteFile`: computes `<RecycleBin>/<subfolder>/<fileName>`, creates the destination folder
  (wrapping I/O failures as `RecycleBinException`), resolves name collisions by appending `_2`,
  `_3`, ... before the extension, moves the file, stamps its last-write-time to now, returns the
  final path (or `null` if hard-deleted because the bin is disabled).
- `Empty()`: manual, explicit "empty trash" — deletes every top-level item under the bin root. Not
  called automatically anywhere.
- `Cleanup()`: no-op if disabled or `RecycleBinCleanupDays == 0`; otherwise recursively deletes any
  file whose last-write-time + `RecycleBinCleanupDays` has passed, then prunes newly-empty
  subfolders.

**`DeleteMediaFileReason`** enum (`MediaFiles/DeleteMediaFileReason.cs`): `MissingFromDisk`
(scan-detected removal — DB-only, no recycle-bin call, explicitly excluded from the
delete-empty-folders cascade since Sonarr didn't initiate the delete), `Manual` (user-initiated
delete via UI/API), `Upgrade` (superseded by a better import), `NoLinkedEpisodes` (orphaned DB row
cleanup — DB-only, never touches disk), `ManualOverride` (used by the manual-import/rescan
DB-record-replacement path in `ImportApprovedEpisodes`).

**`MediaFileDeletionService`**: `DeleteEpisodeFile` validates the root folder exists/is non-empty
before touching anything (defensive against deleting into an unmounted volume), recycle-bins the
file, deletes the DB row with reason `Manual`. `Execute(DeleteSeriesFilesCommand)` is the bulk
equivalent across multiple series, skipping (not aborting) any series whose root-folder checks
fail. `HandleAsync(SeriesDeletedEvent)` (only when `DeleteFiles` was requested) defensively checks
the series' path isn't a parent-of/identical-to any *other* still-existing series' path before
recycle-binning the whole folder — protects against misconfigured/overlapping series paths.
`Handle(EpisodeFileDeletedEvent)` (ordered `Last`) implements `DeleteEmptyFolders`: walks upward
from the deleted file's folder to the series root pruning empty subfolders, then hard-deletes
(**not** recycle-bins) the series folder itself if it's now completely empty — empty directory
shells are always unlinked directly, never recycled.

**`MediaFileAttributeService`** — Windows: `SetFilePermissions` calls `InheritFolderPermissions`
(resets ACLs to inherit from parent), swallowing `UnauthorizedAccessException`/
`InvalidOperationException`/`FileNotFoundException` as debug noise (common on NAS/SMB mounts);
`SetFolderPermissions` is a no-op. Non-Windows: both file and folder permission calls funnel
through the same `SetMonoPermissions`, a no-op unless `SetPermissionsLinux` is enabled, otherwise
applying the **same** `ChmodFolder` string and `ChownGroup` to both files and folders uniformly (no
separate file-vs-folder chmod value despite the property name). All exceptions here are caught and
logged as warnings, never fatal.

---

## 10. Post-Import: Notifications, Webhooks, and Commands

**`INotification`** interface (`Notifications/INotification.cs`) — every notification provider
implements: `OnGrab`, `OnDownload`, `OnRename`, `OnImportComplete`, `OnEpisodeFileDelete`,
`OnSeriesAdd`, `OnSeriesDelete`, `OnHealthIssue`, `OnHealthRestored`, `OnApplicationUpdate`,
`OnManualInteractionRequired`, plus matching `SupportsOn*` capability flags (including a distinct
`SupportsOnEpisodeFileDeleteForUpgrade` flag — upgrade-triggered deletes can be independently
enabled/disabled from manual deletes in each notification's settings).

**`ImportCompleteMessage`** (`Notifications/ImportCompleteMessage.cs`) payload: `Message`,
`Series`, `Episodes`, `EpisodeFiles`, `SourcePath`, `SourceTitle`, `DownloadClientInfo`,
`DownloadId`, `Release` (`GrabbedReleaseInfo`), `DestinationPath`, `ReleaseGroup`, `ReleaseQuality`.

**Webhook** (`Notifications/Webhook/`) is the most complete integration surface — 29 files,
including per-event payload builders (`WebhookGrabPayload`, `WebhookImportCompletePayload`,
`WebhookRenamePayload`, `WebhookEpisodeDeletePayload`, `WebhookHealthPayload`,
`WebhookSeriesAddPayload`, `WebhookSeriesDeletePayload`, `WebhookApplicationUpdatePayload`,
`WebhookManualInteractionPayload`) and DTOs mirroring the domain shape
(`WebhookEpisode`/`WebhookEpisodeFile`/`WebhookEpisodeFileMediaInfo`/`WebhookSeries`/
`WebhookRelease`/`WebhookGrabbedRelease`/`WebhookCustomFormat`/`WebhookCustomFormatInfo`/
`WebhookRenamedEpisodeFile`/`WebhookDownloadClientItem`/`WebhookImage`). `WebhookEventType` is the
closed set of event names dispatched; `WebhookMethod` (`POST`/`PUT`) and per-webhook settings
(`WebhookSettings`) configure the endpoint.

**Rename command surface** (also see §2.4 for the preview API):
- `RenameFilesCommand` (`SeriesId`, `List<int> Files`) — renames a **user-selected subset** of
  files for one series.
- `RenameSeriesCommand` (`List<int> SeriesIds`) — renames **all** files for one or more entire
  series.
- Both flagged `RequiresDiskAccess = true` (gated/refused by the command queue when the library
  disk isn't currently reachable) and `SendUpdatesToClient = true` (live SignalR progress during a
  potentially long batch).
- `RenameEpisodeFileService.Execute` for either command calls the shared `RenameFiles` helper:
  per-file, captures the previous path, calls `EpisodeFileMovingService.MoveEpisodeFile`
  (rebuilds the destination path via the naming engine, same machinery as import), persists the
  updated `EpisodeFile`, records a `RenamedEpisodeFile` (`EpisodeFile`, `PreviousPath`,
  `PreviousRelativePath`), and publishes `EpisodeFileRenamedEvent` per file. Per-file error
  isolation: `FileAlreadyExistsException` → warn and skip; `SameFilenameException` → debug-log and
  skip (already correctly named, a true no-op); any other exception → error-log and skip — **one
  bad file never aborts the rest of the batch**. After the loop, prunes empty subfolders and
  publishes a single `SeriesRenamedEvent` (all renamed files) and a terminal `RenameCompletedEvent`.
- Rename preview (`GetRenamePreviews`, backing `GET /rename` and `GET /rename/bulk`) is
  **diff-based and non-destructive**: it computes what the naming engine *would* produce for every
  tracked file and only returns entries where the computed path actually differs from the current
  path — files already correctly named are silently omitted, never touched.

**Disk-scan-based manual-change detection** is covered in full in §8 — Sonarr does not watch the
filesystem continuously; it relies on the explicit `RescanSeriesCommand` (manual or triggered by
`RescanAfterRefresh` after a metadata refresh) plus the on-import/on-scan
`ImportDecisionMaker`/`ImportApprovedEpisodes` pipeline to reconcile manually-added files, and the
file-size-comparison re-probe in `DiskScanService.Scan` to detect manually-replaced files in place.

---

## Cross-Cutting Notes for Parity Implementation

1. **One recycle-bin choke point.** Every disk-delete path that touches user media (episode file
   delete, extra file delete, series folder delete, duplicate-metadata delete, the "-orig"
   collision rename-then-recycle) funnels through the single `IRecycleBinProvider`. Disabling it
   (blank path) uniformly converts every one of these into a permanent delete — there is no
   per-subsystem soft-delete concept. The only exception is empty-folder-shell removal, which is
   always a direct hard unlink.
2. **Aggregation is confidence-weighted, not first-match-wins.** Quality and Language both resolve
   via an ordered list of independent "augmenter" strategies (filename, folder, download-client
   title, MediaInfo, grab-history release name), each tagged with a `Confidence` tier
   (`Fallback/Default/Tag-or-Filename/MediaInfo`), and the aggregator keeps the highest-confidence
   non-empty signal per field — with an explicit tie-break rule (Quality: prefer higher revision on
   a confidence tie, to protect a detected proper/repack signal).
3. **Manual Import reuses the *exact* automatic pipeline for listing/preview** (same
   `ImportDecisionMaker`, same specs, same aggregation) but **bypasses specs entirely at commit
   time** — the final `Execute(ManualImportCommand)` builds a zero-rejection `ImportDecision`
   manually. Rejections are shown to the user during review, never enforced once they submit.
4. **Every spec always runs; failures are isolated per-spec.** `ImportDecisionMaker` doesn't
   short-circuit on the first rejection (a file can be rejected for several simultaneous reasons),
   and a spec that throws becomes a `DecisionError` rejection naming the offending class rather
   than crashing the batch.
5. **The naming engine is a single generalized token-substitution pass**, not per-scheme code
   branches — season/episode/absolute-number blocks are themselves detected via regex *within* the
   pattern string and swapped for synthesized placeholder tokens before the final substitution
   pass runs. Folder naming is a deliberately restricted subset of the same engine (no
   episode/quality/mediainfo/custom-format tokens).
6. **Validation is two-layered**: structural (pattern must contain required tokens) and semantic
   round-trip (generate a sample, re-parse it with Sonarr's own parser, confirm the identity
   survives) — this second layer is easy to skip in a from-scratch implementation but is what
   catches naming schemes that are syntactically valid yet practically unparseable later.
7. **Upgrade-in-place is quality-tier-first, custom-format-second, with a rename-aware edge case**:
   a strictly better quality tier always wins; an equal tier falls to revision (gated by a global
   proper/repack preference setting) then to Custom Format score computed against the *final
   renamed filename* — with a distinct rejection reason specifically for the case where renaming
   itself caused the format score to regress.
8. **The extras subsystem is a three-manager ordered pipeline** (Metadata → Subtitles → Others) with
   first-claim-wins semantics, sharing one generic `ExtraFile`/`ExtraFileManager<T>` base for
   transfer/rename/recycle-bin-on-delete behavior, and reusing the **same** matching heuristics
   (filename-prefix, season/episode-number, sole-video-in-folder fallback) between the "new
   download" import path and the "existing files on disk" discovery path.
