#!/usr/bin/env bash
# Regenerates the Windows application icon from assets/AppIcon.svg.
#
# Each size is rendered straight from the vector rather than downscaled from one large
# bitmap: librsvg evaluates the drop shadow at the target resolution, and the result is
# visibly crisper at 16 and 32 px, where the icon spends most of its life.
#
# Needs: librsvg2-bin (rsvg-convert) and ImageMagick (convert).
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source_svg="$here/AppIcon.svg"
# Both GhostClick.App and the dev harness take the icon from here.
target_ico="$here/AppIcon.ico"

# 16/32/48/256 are what the shell asks for; the rest cover 125-200% display scaling.
sizes=(16 20 24 32 40 48 64 128 256)

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

frames=()
for size in "${sizes[@]}"; do
    rsvg-convert -w "$size" -h "$size" -o "$work/$size.png" "$source_svg"
    frames+=("$work/$size.png")
done

convert "${frames[@]}" -background none "$target_ico"
echo "Wrote $target_ico"
identify "$target_ico"
