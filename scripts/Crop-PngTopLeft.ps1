[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DirectoryPath,

    [int]$Width = 1700,

    [int]$Height = 1300
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName PresentationCore

$resolvedDirectoryPath = (Resolve-Path -LiteralPath $DirectoryPath).Path

if (-not (Test-Path -LiteralPath $resolvedDirectoryPath -PathType Container)) {
    throw "Directory was not found: $DirectoryPath"
}

$pngFiles = Get-ChildItem -LiteralPath $resolvedDirectoryPath -Filter *.png -File | Sort-Object Name

foreach ($pngFile in $pngFiles) {
    $memoryStream = $null
    $outputStream = $null
    $temporaryPath = [System.IO.Path]::Combine($pngFile.DirectoryName, "$($pngFile.BaseName).cropped$($pngFile.Extension)")

    try {
        $fileBytes = [System.IO.File]::ReadAllBytes($pngFile.FullName)
        $memoryStream = [System.IO.MemoryStream]::new($fileBytes, $false)
        $decoder = [System.Windows.Media.Imaging.PngBitmapDecoder]::new(
            $memoryStream,
            [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
            [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)

        $frame = $decoder.Frames[0]

        if ($frame.PixelWidth -lt $Width -or $frame.PixelHeight -lt $Height) {
            Write-Warning "Skipped '$($pngFile.FullName)' because it is too small ($($frame.PixelWidth)x$($frame.PixelHeight))."
            continue
        }

        $cropArea = [System.Windows.Int32Rect]::new(0, 0, $Width, $Height)
        $croppedBitmap = [System.Windows.Media.Imaging.CroppedBitmap]::new($frame, $cropArea)
        $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
        $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($croppedBitmap))

        $outputStream = [System.IO.File]::Open($temporaryPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
        $encoder.Save($outputStream)
        $outputStream.Close()
        $outputStream = $null

        Remove-Item -LiteralPath $pngFile.FullName -Force
        Move-Item -LiteralPath $temporaryPath -Destination $pngFile.FullName
        Write-Host "Cropped '$($pngFile.Name)' to ${Width}x${Height}."
    }
    finally {
        if ($outputStream -ne $null) {
            $outputStream.Dispose()
        }

        if ($memoryStream -ne $null) {
            $memoryStream.Dispose()
        }

        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}
