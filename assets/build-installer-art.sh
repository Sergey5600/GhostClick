#!/usr/bin/env bash
# Renders the installer wizard pictures from the SVG sources into installer/art.
#
# Inno Setup takes one bitmap per display scale and picks the one that fits, so each scale
# is rendered straight from the vector rather than stretched from one size.
#   WizardImage-*.bmp       the tall picture on the welcome and finish pages
#   WizardSmallImage-*.bmp  the app icon in the top corner of the other pages
#
# Needs: librsvg2-bin (rsvg-convert), ImageMagick (convert), and the Geist font installed
# for fontconfig (the wordmark is set in it).
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
out="$here/../installer/art"
mkdir -p "$out"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# Display scale -> size. 164x314 and 55x55 are Inno Setup's sizes at 100 %.
for scale in 100 125 150 175 200; do
    w=$(( 164 * scale / 100 )); h=$(( 314 * scale / 100 )); s=$(( 55 * scale / 100 ))

    rsvg-convert -w "$w" -h "$h" -o "$work/large.png" "$here/InstallerWizard.svg"
    convert "$work/large.png" -background '#16181F' -alpha remove -alpha off -type TrueColor \
        "BMP3:$out/WizardImage-$scale.bmp"

    # The small picture sits on the white page header, so the icon is flattened onto white.
    rsvg-convert -w "$s" -h "$s" -o "$work/small.png" "$here/AppIcon.svg"
    convert "$work/small.png" -background white -alpha remove -alpha off -type TrueColor \
        "BMP3:$out/WizardSmallImage-$scale.bmp"
done

ls -la "$out"
