# 3D Browser Pro — empacota assets/icon*.png em assets/icon.ico
# Uso: powershell -NoProfile -ExecutionPolicy Bypass -File tools/make-ico.ps1
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$assetsDir = Join-Path $root 'assets'
$icoPath = Join-Path $assetsDir 'icon.ico'

$entries = @()
foreach ($size in @(16, 32, 48, 128, 256)) {
    $png = Join-Path $assetsDir ("icon{0}.png" -f $size)
    if (-not (Test-Path $png)) {
        Write-Warning ("faltando {0}" -f $png)
        continue
    }
    $entries += , @($size, [System.IO.File]::ReadAllBytes($png))
}

if ($entries.Count -eq 0) { throw 'Nenhum PNG encontrado em assets/.' }

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)

# ICONDIR
$bw.Write([uint16]0)                # reservado
$bw.Write([uint16]1)                # tipo: ícone
$bw.Write([uint16]$entries.Count)   # quantidade

# ICONDIRENTRY (16 bytes cada)
$offset = 6 + (16 * $entries.Count)
foreach ($entry in $entries) {
    $size = [int]$entry[0]
    $data = [byte[]]$entry[1]
    $dim = if ($size -ge 256) { 0 } else { $size }

    $bw.Write([byte]$dim)           # largura (0 = 256)
    $bw.Write([byte]$dim)           # altura
    $bw.Write([byte]0)              # cores da paleta
    $bw.Write([byte]0)              # reservado
    $bw.Write([uint16]1)            # planos
    $bw.Write([uint16]32)           # bits por pixel
    $bw.Write([uint32]$data.Length) # tamanho da imagem
    $bw.Write([uint32]$offset)      # offset da imagem
    $offset += $data.Length
}

# dados (PNG comprimido, suportado desde o Vista)
foreach ($entry in $entries) {
    $bw.Write([byte[]]$entry[1])
}

$bw.Flush()
[System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())
$bw.Dispose()
$ms.Dispose()

$info = Get-Item $icoPath
Write-Host ("ok  assets/icon.ico  {0} bytes  ({1} tamanhos)" -f $info.Length, $entries.Count)
