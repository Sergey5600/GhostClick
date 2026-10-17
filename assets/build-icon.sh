#!/usr/bin/env bash
# Regenerates the Windows application icon from assets/AppIcon.svg.
#
# Each size is rendered straight from the vector rather than downscaled from one large
# bitmap: librsvg evaluates the drop shadow at the target resolution, and the result is
# visibly crisper at 16 and 32 px, where the icon spends most of its life.
#
# The plate's straight edges are also put on whole pixels at every size. AppIcon.svg leaves
# 24 of its 1072 units around the plate for the shadow (about 1 px at 48 px); scaled as is,
# that margin is a fraction of a pixel at 16-40 px and smears the plate's edge into a
# half-transparent ring. So each size gets its own viewBox: the margin rounded down to whole
# pixels (none up to 40 px, where the shadow would not show anyway; 1 px at 48 and 64 px;
# 5 px at 256 px), the plate filling the rest edge to edge.
#
# Needs: librsvg2-bin (rsvg-convert) and ImageMagick (convert).
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source_svg="$here/AppIcon.svg"
# Both GhostClick.App and the dev harness take the icon from here.
target_ico="$here/AppIcon.ico"

# 16/32/48/256 are what the shell asks for; the rest cover 125-200% display scaling.
sizes=(16 20 24 32 40 48 64 128 256)

# The plate and the canvas around it, in AppIcon.svg's units. Kept in step with the SVG by
# the check below.
plate='<rect x="64" y="40" width="1024" height="1024"'
plate_x=64 plate_y=40 plate_side=1024
canvas='viewBox="40 16 1072 1072"'
canvas_side=1072

for expected in "$plate" "$canvas"; do
    if ! grep -qF "$expected" "$source_svg"; then
        echo "AppIcon.svg no longer has $expected; update the plate geometry in $0" >&2
        exit 1
    fi
done

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

frames=()
for size in "${sizes[@]}"; do
    view_box=$(awk -v s="$size" -v x="$plate_x" -v y="$plate_y" -v p="$plate_side" -v c="$canvas_side" 'BEGIN {
        margin = int(s * (c - p) / 2 / c)       # whole pixels around the plate
        v = p * s / (s - 2 * margin)            # canvas side that puts the plate on them
        printf "%.4f %.4f %.4f %.4f", x + p / 2 - v / 2, y + p / 2 - v / 2, v, v
    }')
    sed "s|$canvas|viewBox=\"$view_box\"|" "$source_svg" > "$work/$size.svg"
    rsvg-convert -w "$size" -h "$size" -o "$work/$size.png" "$work/$size.svg"
    frames+=("$work/$size.png")
done

convert "${frames[@]}" -background none "$target_ico"
echo "Wrote $target_ico"
identify "$target_ico"
