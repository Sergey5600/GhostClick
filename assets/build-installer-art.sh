#!/usr/bin/env bash
# Renders the installer wizard pictures into installer/art.
#
# Inno Setup takes one bitmap per display scale and picks the one that fits.
#   WizardImage-*.bmp       the tall picture on the welcome and finish pages. Its source is
#                           InstallerWizard.png, the Figma design "WizardImage — Midnight
#                           Minimal" exported at 328x628, the 200 % size: 200 % is that picture
#                           as it is, the smaller scales are downscaled from it.
#   WizardSmallImage-*.bmp  the app icon in the top corner of the other pages, rendered
#                           straight from AppIcon.svg at each size rather than stretched.
#
# Needs: librsvg2-bin (rsvg-convert) and ImageMagick (convert, identify).
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
out="$here/../installer/art"
mkdir -p "$out"

wizard="$here/InstallerWizard.png"
if [ "$(identify -format '%wx%h' "$wizard")" != 328x628 ]; then
    echo "$wizard must be 328x628 (the 200 % size); export it from Figma at that size" >&2
    exit 1
fi

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# Display scale -> size. 164x314 and 55x55 are Inno Setup's sizes at 100 %.
for scale in 100 125 150 175 200; do
    w=$(( 164 * scale / 100 )); h=$(( 314 * scale / 100 )); s=$(( 55 * scale / 100 ))

    if [ "$scale" = 200 ]; then
        cp "$wizard" "$work/large.png"
    else
        # Two-lobe Lanczos, in sRGB: these come closest to what Figma itself draws at each
        # size. Three lobes leave a dark ring round the white ghost, and in linear light the
        # ghost and the lettering turn out visibly heavier.
        convert "$wizard" -filter Lanczos2 -resize "${w}x${h}!" "$work/large.png"
    fi
    # -density 0 drops the 25 dpi that Figma writes into its PNG; Setup goes by pixels only.
    convert "$work/large.png" -background '#16181F' -alpha remove -alpha off -density 0 \
        -type TrueColor "BMP3:$out/WizardImage-$scale.bmp"

    # The small picture sits on the white page header, so the icon is flattened onto white.
    rsvg-convert -w "$s" -h "$s" -o "$work/small.png" "$here/AppIcon.svg"
    convert "$work/small.png" -background white -alpha remove -alpha off -type TrueColor \
        "BMP3:$out/WizardSmallImage-$scale.bmp"
done

ls -la "$out"
