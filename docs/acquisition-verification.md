# Acquisition verification and recovery

Acquisition has separate decisions for finding a release, starting its transfer,
importing its files, and replacing an existing source. Passing one decision does
not bypass the later checks.

## Profiles and automatic selection

A profile controls acceptable quality, ordered audio-language preferences, file
size, protocols, custom-format scores, and upgrade behavior. Basic rules create
ordinary editable custom formats. Advanced editing changes those same rules;
weights belong to the profile.

An explicit preferred audio language ranks ahead of unspecified multiple audio
languages. `MULTI` and `Dual Audio` are fallback evidence, not proof of a particular
language. Subtitle tags do not establish audio language. Title-based preferences
rank discovery evidence; they cannot certify the contents of an unavailable file.

Auto-grab controls automatic selection for standalone acquisitions. Requested and
monitored items continue automatic acquisition. Disabling Auto-grab does not pause
those requests; use their acquisition or monitor controls.

Allow format changes is enabled by default. It permits a verified video replacement
to change its container, such as AVI to MKV. Turning it off requires individual
approval for a container change. Quality, recording identity, coverage, and
verification checks still apply when it is enabled.

## Before payload download

On compatible qBittorrent clients, automatic additions stop after torrent metadata
arrives. Prismedia inspects the advertised file list before releasing the hold.
Conflicting content and season packs with no useful coverage improvement can enter
recovery without downloading the media payload. Empty or temporarily unavailable
metadata leaves the hold closed and uses the stalled-download recovery window.

The hold is durable in the downloader. Restarting the worker does not discard it.
Starting the payload requires the same active acquisition to own the exact
transfer. An ordinary paused torrent is not an acquisition admission hold.

Other clients and older qBittorrent versions continue through their supported
transfer and monitoring paths. A usable file list may become available only after
payload transfer begins. Usenet archive and repair filenames do not establish the
names or episode coverage of files inside an archive. An archive header inventory
also cannot prove that its videos decode correctly.

Individual-song searches use advertised filenames when available to check the exact
recording before selection. Artist prefixes and track numbers are normalized;
recording variants such as instrumentals remain distinct. Album reconciliation
allows track performers to differ from a compilation album's artist.

## Before import and replacement

Newly acquired video and audio streams are fully decoded before video import.
Verification runs through background media-process admission with limited CPU
concurrency. A quick metadata probe alone cannot establish that a whole video is
undamaged.

TV mapping combines catalog identity, season and absolute numbering, episode
titles, and file evidence. A physical file may cover multiple catalog episodes.
Confident extras are imported when monitored; other extras remain available for
review. Ambiguous mappings require review instead of inventing coverage.

Verified episodes from a partially damaged pack can be imported while damaged
files remain retained. Monitored missing episodes can request replacements. A
season pack is useful only when its projected mapping improves current coverage;
previously rejected coverage can be reconsidered when library or catalog facts
change.

Video replacement retains recovery evidence until the verified new source and its
library binding agree. Interrupted replacement and library scanning coordinate to
preserve the existing Entity, source ownership, and playback history. Re-import and
rescan use current catalog facts to reconcile older mappings.

## Reviewing results

Use release rejections and custom-format scores to understand selection. Use file
review for actual import mapping and retained content. A successful search, an
advertised file inventory, a completed download, and a verified import are different
outcomes; none should be used as a substitute for the next.
