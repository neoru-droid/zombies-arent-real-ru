param(
    [string]$PayloadGzPath = "",
    [string]$OutputExePath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$srcDir = $PSScriptRoot
$repoRoot = Split-Path -Parent $srcDir
$csPath = Join-Path $srcDir "ZombiesArentRealRuSetup.cs"

if ([string]::IsNullOrWhiteSpace($OutputExePath)) {
    $OutputExePath = Join-Path $repoRoot "ZombiesArentReal-RU-Setup.exe"
}

$cscPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $cscPath)) {
    throw "Не найден штатный компилятор .NET Framework: $cscPath"
}
if (-not (Test-Path -LiteralPath $csPath)) {
    throw "Не найден исходный файл C#: $csPath"
}

$tempWorkDir = Join-Path ([System.IO.Path]::GetTempPath()) ("zar_ru_src_build_" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $tempWorkDir | Out-Null

try {
    $resolvedPayloadGz = $PayloadGzPath
    if ([string]::IsNullOrWhiteSpace($resolvedPayloadGz)) {
        $dataDir = Join-Path $repoRoot "data"
        $langJsonPath = Join-Path $dataDir "ru_translations.json"
        $achJsonPath  = Join-Path $dataDir "achievements_ru.json"
        $fontsDir     = Join-Path $dataDir "fonts"

        foreach ($reqPath in @($langJsonPath, $achJsonPath, $fontsDir)) {
            if (-not (Test-Path -LiteralPath $reqPath)) {
                throw "Не найден ресурс данных для сборки: $reqPath"
            }
        }

        Add-Type -AssemblyName System.Web.Extensions
        $ser = New-Object System.Web.Script.Serialization.JavaScriptSerializer
        $ser.MaxJsonLength = [int]::MaxValue

        $langMap = $ser.DeserializeObject([System.IO.File]::ReadAllText($langJsonPath, [System.Text.Encoding]::UTF8))
        $achList = $ser.DeserializeObject([System.IO.File]::ReadAllText($achJsonPath,  [System.Text.Encoding]::UTF8))

        $fontNames = @("Carlito.ttf", "Carlito-OFL.txt", "Tactical_HUD.ttf", "Tactical_HUD-OFL.txt")
        $fontsMap = @{}
        foreach ($fn in $fontNames) {
            $fp = Join-Path $fontsDir $fn
            if (-not (Test-Path -LiteralPath $fp)) {
                throw "Не найден файл шрифта: $fp"
            }
            $fontsMap[$fn] = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($fp))
        }

        $payloadObj = @{
            version      = "1.0.0"
            app_id       = 5185890
            lang         = $langMap
            achievements = $achList
            fonts        = $fontsMap
        }

        $resolvedPayloadGz = Join-Path $tempWorkDir "ru_payload.gz"
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        $jsonBytes = $utf8NoBom.GetBytes($ser.Serialize($payloadObj))
        $fs = [System.IO.File]::Create($resolvedPayloadGz)
        $gz = New-Object System.IO.Compression.GZipStream($fs, [System.IO.Compression.CompressionMode]::Compress)
        $gz.Write($jsonBytes, 0, $jsonBytes.Length)
        $gz.Dispose()
        $fs.Dispose()
    }

    if (-not (Test-Path -LiteralPath $resolvedPayloadGz)) {
        throw "Не найден файл ресурсов: $resolvedPayloadGz"
    }

    Add-Type -AssemblyName System.Drawing
    $icoPath = Join-Path $tempWorkDir "app_tactical.ico"

    function New-IconFramePng([int]$sz) {
        $bmp = New-Object System.Drawing.Bitmap($sz, $sz)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear([System.Drawing.Color]::Transparent)

        $bgBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 16, 22, 29))
        $borderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 46, 204, 113), [Math]::Max(1.5, $sz * 0.05))
        $pad = [int][Math]::Max(1, $sz * 0.06)
        $boxW = $sz - $pad * 2
        $g.FillEllipse($bgBrush, $pad, $pad, $boxW, $boxW)
        $g.DrawEllipse($borderPen, $pad, $pad, $boxW, $boxW)

        $cx = $sz * 0.5
        $cy = $sz * 0.5
        $rInner = $sz * 0.23
        $innerPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(180, 46, 204, 113), [Math]::Max(1.0, $sz * 0.04))
        $g.DrawEllipse($innerPen, [float]($cx - $rInner), [float]($cy - $rInner), [float]($rInner * 2), [float]($rInner * 2))

        $pipR = [Math]::Max(2.0, $sz * 0.09)
        $pipBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 46, 204, 113))
        $g.FillEllipse($pipBrush, [float]($cx - $pipR), [float]($cy - $pipR), [float]($pipR * 2), [float]($pipR * 2))

        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bytes = $ms.ToArray()

        $ms.Dispose()
        $pipBrush.Dispose()
        $innerPen.Dispose()
        $borderPen.Dispose()
        $bgBrush.Dispose()
        $g.Dispose()
        $bmp.Dispose()
        return ,$bytes
    }

    $sizes = @(16, 32, 48, 256)
    $pngFrames = @()
    foreach ($s in $sizes) {
        $pngFrames += ,(New-IconFramePng $s)
    }

    $icoStream = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($icoStream)
    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$sizes.Count)

    $dataOffset = 6 + (16 * $sizes.Count)
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]
        $frame = $pngFrames[$i]
        $dimByte = if ($s -ge 256) { [byte]0 } else { [byte]$s }
        $bw.Write($dimByte)
        $bw.Write($dimByte)
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]32)
        $bw.Write([uint32]$frame.Length)
        $bw.Write([uint32]$dataOffset)
        $dataOffset += $frame.Length
    }
    foreach ($frame in $pngFrames) {
        $bw.Write($frame)
    }
    $bw.Flush()
    [System.IO.File]::WriteAllBytes($icoPath, $icoStream.ToArray())
    $bw.Dispose()
    $icoStream.Dispose()

    $outDir = Split-Path -Parent $OutputExePath
    if (-not [string]::IsNullOrWhiteSpace($outDir) -and -not (Test-Path -LiteralPath $outDir)) {
        New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    }

    $cscArgs = @(
        "/nologo",
        "/optimize+",
        "/debug-",
        "/codepage:65001",
        "/platform:anycpu",
        "/target:winexe",
        "/win32icon:$icoPath",
        "/out:$OutputExePath",
        "/r:System.dll",
        "/r:System.Core.dll",
        "/r:System.Drawing.dll",
        "/r:System.Windows.Forms.dll",
        "/r:System.Web.Extensions.dll",
        "/resource:$resolvedPayloadGz,zar_ru_payload.gz",
        $csPath
    )

    & $cscPath @cscArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Сборка завершилась с кодом ошибки $LASTEXITCODE"
    }

    $len = (Get-Item -LiteralPath $OutputExePath).Length
    Write-Host "Собран файл: $OutputExePath ($len байт)"
}
finally {
    if (Test-Path -LiteralPath $tempWorkDir) {
        Remove-Item -Recurse -Force -LiteralPath $tempWorkDir
    }
}
