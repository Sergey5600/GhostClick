<#
.SYNOPSIS
    Works out the exact sizes of the setup wizard's two pictures at each display scale and,
    with -ArtDir, checks that installer/art has a picture of exactly that size for each.

.DESCRIPTION
    Inno Setup 6.3 lays the wizard out in units of its dialog font (Tahoma 8 pt at the
    screen's DPI), then enlarges the modern wizard by WizardSizePercent (120). The side
    picture's box follows that; the header picture's box is 55x58 at 96 DPI. Any mismatch
    between a picture and its box gets resampled, which is what blurred the wizard, so
    assets/build-installer-art.sh draws one picture per display scale at exactly the sizes
    printed here, and the [Code] in GhostClick.iss shows the one matching the real box.

    The arithmetic mirrors Inno's own (Projects/Src/SetupForm.pas: CalculateBaseUnitsFromFont,
    InitializeFont, SizeAndCenterIfNeeded; Wizard.pas: AnchorOuterPage; Wizard.dfm sizes),
    using Windows' own MulDiv and font metrics, so it has to run on Windows.
    CI runs it with -ArtDir; at 96 DPI the installer also logs the real box sizes, and CI
    compares the two.
#>
param(
    [string]$ArtDir
)

$ErrorActionPreference = 'Stop'

Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class WizardFont
{
    [DllImport("kernel32.dll")] public static extern int MulDiv(int number, int numerator, int denominator);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision,
        uint quality, uint pitchAndFamily, string face);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetTextExtentPoint32W(IntPtr dc, string text, int length, out Size size);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetTextMetricsW(IntPtr dc, out TextMetric metric);

    [StructLayout(LayoutKind.Sequential)] struct Size { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct TextMetric
    {
        public int tmHeight, tmAscent, tmDescent, tmInternalLeading, tmExternalLeading, tmAveCharWidth,
            tmMaxCharWidth, tmWeight, tmOverhang, tmDigitizedAspectX, tmDigitizedAspectY;
        public char tmFirstChar, tmLastChar, tmDefaultChar, tmBreakChar;
        public byte tmItalic, tmUnderlined, tmStruckOut, tmPitchAndFamily, tmCharSet;
    }

    // Inno's base units for a font of the given height: X from the average width of the
    // alphabet (Microsoft KB 145994), Y the font's full height.
    public static int[] BaseUnits(string face, int height)
    {
        IntPtr dc = GetDC(IntPtr.Zero);
        IntPtr font = CreateFontW(height, 0, 0, 0, 400, 0, 0, 0, 1 /* DEFAULT_CHARSET */, 0, 0, 0, 0, face);
        IntPtr old = SelectObject(dc, font);
        try
        {
            const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
            Size size;
            TextMetric metric;
            GetTextExtentPoint32W(dc, letters, letters.Length, out size);
            GetTextMetricsW(dc, out metric);
            return new[] { (size.cx / 26 + 1) / 2, metric.tmHeight };
        }
        finally
        {
            SelectObject(dc, old);
            DeleteObject(font);
            ReleaseDC(IntPtr.Zero, dc);
        }
    }
}
'@

function MulDiv([int]$a, [int]$b, [int]$c) { [WizardFont]::MulDiv($a, $b, $c) }

# Display scales Windows offers, as DPI.
$scales = 100, 125, 150, 175, 200, 225, 250, 300

$rows = foreach ($scale in $scales) {
    $dpi = MulDiv 96 $scale 100
    $bu = [WizardFont]::BaseUnits('Tahoma', (MulDiv -8 $dpi 72))
    $bx = $bu[0]; $by = $bu[1]

    # The form is designed at base units 6x13: client area 497x360, side picture 164x314
    # (one pixel taller than its page, which hides that row), header picture 55x58.
    # Everything is scaled by the base units, then the client height grows to 120 % and the
    # side picture's height with it; its width follows from its height at the original
    # aspect ratio.
    $clientHeight = MulDiv 360 $by 13
    $growth = (MulDiv $clientHeight 120 100) - $clientHeight
    $sideHeight = (MulDiv 314 $by 13) + $growth
    $sideWidth = MulDiv $sideHeight (MulDiv 164 $bx 6) (MulDiv 314 $by 13)

    [pscustomobject]@{
        Scale = "$scale %"
        Dpi = $dpi
        BaseUnits = "${bx}x${by}"
        Side = "${sideWidth}x${sideHeight}"
        Header = "$(MulDiv 55 $bx 6)x$(MulDiv 58 $by 13)"
    }
}

$rows | Format-Table -AutoSize | Out-String | Write-Host
Write-Host "side   = $(($rows.Side | Select-Object -Unique) -join ' ')"
Write-Host "header = $(($rows.Header | Select-Object -Unique) -join ' ')"

if ($ArtDir) {
    $missing = @()
    foreach ($row in $rows) {
        if (-not (Test-Path (Join-Path $ArtDir "WizardImage-$($row.Side).bmp"))) { $missing += "WizardImage-$($row.Side).bmp ($($row.Scale))" }
        if (-not (Test-Path (Join-Path $ArtDir "WizardSmallImage-$($row.Header).bmp"))) { $missing += "WizardSmallImage-$($row.Header).bmp ($($row.Scale))" }
    }
    if ($missing) {
        throw "installer/art has no picture for: $($missing -join ', '). Put the sizes above into assets/build-installer-art.sh and run it."
    }
    Write-Host 'installer/art has a picture of the exact size for every display scale.'
}

# For the CI's comparison with what the installer logs at the runner's 96 DPI.
$rows
