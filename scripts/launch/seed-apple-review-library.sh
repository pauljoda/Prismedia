#!/usr/bin/env bash
set -euo pipefail

expected_target="/home/paul/docker-data/apple-prismedia-media"

if [[ "$#" -ne 2 || "$1" != "--target" ]]; then
  printf 'Usage: %s --target %s\n' "$0" "$expected_target" >&2
  exit 64
fi

target_root="$2"
if [[ "$target_root" != "$expected_target" ]]; then
  printf 'Refusing unexpected target: %s\n' "$target_root" >&2
  exit 64
fi

for command_name in curl unzip ffmpeg python3 sha256sum find sort xargs; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    printf 'Required command is unavailable: %s\n' "$command_name" >&2
    exit 69
  fi
done

if [[ -d "$target_root" ]] && [[ -n "$(find "$target_root" -mindepth 1 -print -quit)" ]]; then
  printf 'Refusing to overwrite non-empty review library: %s\n' "$target_root" >&2
  exit 73
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
fixtures_dir="$script_dir/apple-review-fixtures"
for fixture in \
  "ATTRIBUTION.md" \
  "Big Buck Bunny (2008).nfo" \
  "Open Movie Sampler - S01E01 - Elephants Dream.nfo" \
  "Open Movie Sampler - S01E02 - Caminandes Gran Dillama.nfo" \
  "ComicInfo.xml"; do
  if [[ ! -f "$fixtures_dir/$fixture" ]]; then
    printf 'Missing fixture: %s\n' "$fixtures_dir/$fixture" >&2
    exit 66
  fi
done

stage_root="$(mktemp -d /home/paul/docker-data/apple-review-seed.XXXXXX)"
download_dir="$stage_root/downloads"
library_dir="$stage_root/library"

cleanup() {
  if [[ -n "${stage_root:-}" && -d "$stage_root" ]]; then
    find "$stage_root" -depth -delete
  fi
}
trap cleanup EXIT INT TERM HUP

mkdir -p "$download_dir" "$library_dir"

download() {
  local url="$1"
  local destination="$2"
  local partial="$destination.part"
  curl --fail --location --retry 3 --retry-delay 2 --output "$partial" "$url"
  mv "$partial" "$destination"
}

extract_single() {
  local archive="$1"
  local member_pattern="$2"
  local destination="$3"
  local member
  member="$(unzip -Z1 "$archive" | awk -v pattern="$member_pattern" '$0 ~ pattern { print; exit }')"
  if [[ -z "$member" ]]; then
    printf 'No archive member matching %s in %s\n' "$member_pattern" "$archive" >&2
    exit 65
  fi
  unzip -p "$archive" "$member" > "$destination"
}

movie_dir="$library_dir/Movies/Big Buck Bunny (2008)"
mkdir -p "$movie_dir"
download \
  "https://download.blender.org/demo/movies/BBB/bbb_sunflower_1080p_30fps_normal.mp4.zip" \
  "$download_dir/big-buck-bunny.zip"
extract_single \
  "$download_dir/big-buck-bunny.zip" \
  "\\.mp4$" \
  "$movie_dir/Big Buck Bunny (2008).mp4"
cp \
  "$fixtures_dir/Big Buck Bunny (2008).nfo" \
  "$movie_dir/Big Buck Bunny (2008).nfo"

series_dir="$library_dir/Series/Open Movie Sampler/Season 01"
mkdir -p "$series_dir"
download \
  "https://download.blender.org/demo/movies/elephantsdream_teaser.mp4.zip" \
  "$download_dir/elephants-dream.zip"
extract_single \
  "$download_dir/elephants-dream.zip" \
  "\\.mp4$" \
  "$series_dir/Open Movie Sampler - S01E01 - Elephants Dream.mp4"
cp \
  "$fixtures_dir/Open Movie Sampler - S01E01 - Elephants Dream.nfo" \
  "$series_dir/Open Movie Sampler - S01E01 - Elephants Dream.nfo"

download \
  "https://download.blender.org/demo/movies/caminandes_gran_dillama.mp4.zip" \
  "$download_dir/caminandes-gran-dillama.zip"
extract_single \
  "$download_dir/caminandes-gran-dillama.zip" \
  "\\.mp4$" \
  "$series_dir/Open Movie Sampler - S01E02 - Caminandes Gran Dillama.mp4"
cp \
  "$fixtures_dir/Open Movie Sampler - S01E02 - Caminandes Gran Dillama.nfo" \
  "$series_dir/Open Movie Sampler - S01E02 - Caminandes Gran Dillama.nfo"

alice_dir="$library_dir/Books/Lewis Carroll/Alice's Adventures in Wonderland"
mkdir -p "$alice_dir"
download \
  "https://standardebooks.org/ebooks/lewis-carroll/alices-adventures-in-wonderland/john-tenniel/downloads/lewis-carroll_alices-adventures-in-wonderland_john-tenniel.epub?source=download" \
  "$alice_dir/Alice's Adventures in Wonderland.epub"
download \
  "https://archive.org/download/alices_adventures_1005_librivox/AlicesAdventuresInWonderlandV5_librivox.m4b" \
  "$alice_dir/Alice's Adventures in Wonderland.m4b"

comic_work_dir="$stage_root/comic"
comic_target_dir="$library_dir/Books/David Revoy/Pepper&Carrot"
mkdir -p "$comic_work_dir" "$comic_target_dir"
for page_number in 00 01 02 03 04 05 06 07; do
  download \
    "https://www.peppercarrot.com/0_sources/ep30_Need-a-Hug/low-res/en_Pepper-and-Carrot_by-David-Revoy_E30P${page_number}.jpg" \
    "$comic_work_dir/${page_number}.jpg"
done
cp "$fixtures_dir/ComicInfo.xml" "$comic_work_dir/ComicInfo.xml"
(
  cd "$comic_work_dir"
  python3 -m zipfile -c \
    "$comic_target_dir/Pepper&Carrot - 030 - Need a Hug.cbz" \
    ComicInfo.xml 00.jpg 01.jpg 02.jpg 03.jpg 04.jpg 05.jpg 06.jpg 07.jpg
)

album_dir="$library_dir/Audio/Kevin MacLeod/Open Review Album"
mkdir -p "$album_dir"
download \
  "https://incompetech.com/music/royalty-free/mp3-royaltyfree/Ascending%20the%20Vale.mp3" \
  "$download_dir/ascending-the-vale.mp3"
download \
  "https://incompetech.com/music/royalty-free/mp3-royaltyfree/Dreamer.mp3" \
  "$download_dir/dreamer.mp3"
download \
  "https://incompetech.com/music/royalty-free/mp3-royaltyfree/The%20Entertainer.mp3" \
  "$download_dir/the-entertainer.mp3"

ffmpeg -hide_banner -loglevel error -y \
  -i "$download_dir/ascending-the-vale.mp3" \
  -map_metadata -1 -codec:a copy \
  -metadata title="Ascending the Vale" \
  -metadata artist="Kevin MacLeod" \
  -metadata album="Open Review Album" \
  -metadata track="1/3" \
  "$album_dir/01 - Ascending the Vale.mp3"
ffmpeg -hide_banner -loglevel error -y \
  -i "$download_dir/dreamer.mp3" \
  -map_metadata -1 -codec:a copy \
  -metadata title="Dreamer" \
  -metadata artist="Kevin MacLeod" \
  -metadata album="Open Review Album" \
  -metadata track="2/3" \
  "$album_dir/02 - Dreamer.mp3"
ffmpeg -hide_banner -loglevel error -y \
  -i "$download_dir/the-entertainer.mp3" \
  -map_metadata -1 -codec:a copy \
  -metadata title="The Entertainer" \
  -metadata artist="Kevin MacLeod" \
  -metadata album="Open Review Album" \
  -metadata track="3/3" \
  "$album_dir/03 - The Entertainer.mp3"

download \
  "https://raw.githubusercontent.com/pauljoda/Prismedia/main/documentation-site/static/img/showcase/prism-refraction-hero.png" \
  "$download_dir/prism-refraction-hero.png"
ffmpeg -hide_banner -loglevel error -y \
  -i "$download_dir/prism-refraction-hero.png" \
  -vf "scale=1400:1400:force_original_aspect_ratio=increase,crop=1400:1400" \
  -frames:v 1 \
  "$album_dir/cover.jpg"

gallery_dir="$library_dir/Images/Open Creative Spaces"
mkdir -p "$gallery_dir"
download \
  "https://upload.wikimedia.org/wikipedia/commons/5/5f/Augmented-reality-1957411_1920.jpg" \
  "$gallery_dir/Augmented Reality.jpg"
download \
  "https://upload.wikimedia.org/wikipedia/commons/f/f4/Photo-studio.jpg" \
  "$gallery_dir/Photo Studio.jpg"
download \
  "https://upload.wikimedia.org/wikipedia/commons/d/dd/Little_free_library_stand_%28Unsplash%29.jpg" \
  "$gallery_dir/Little Free Library.jpg"

cp "$fixtures_dir/ATTRIBUTION.md" "$library_dir/ATTRIBUTION.md"
(
  cd "$library_dir"
  find . -type f ! -name DEPLOYED-SHA256SUMS.txt -print0 \
    | sort -z \
    | xargs -0 sha256sum \
    > DEPLOYED-SHA256SUMS.txt
)

mkdir -p "$target_root"
cp -a "$library_dir/." "$target_root/"
chmod -R a+rX "$target_root"

printf 'Apple review library seeded at %s\n' "$target_root"
printf 'Deployed files: %s\n' "$(find "$target_root" -type f | wc -l | tr -d ' ')"
