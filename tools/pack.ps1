$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$version = (Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'VERSION deve conter major.minor.patch.' }

& (Join-Path $PSScriptRoot 'build.ps1')

Add-Type -AssemblyName System.IO.Compression.FileSystem
$dist = Join-Path $root 'dist'
$null = New-Item -ItemType Directory -Path $dist -Force
$app = Join-Path $root 'build/portable'

function Write-Package([string]$Name, [string]$Prefix, [string]$Base, [string[]]$Entries) {
    $target = Join-Path $dist ($Name + '.zip')
    $temp = Join-Path $dist ([guid]::NewGuid().ToString('N') + '.tmp')
    $backup = $temp + '.bak'
    try {
        $zip = [IO.Compression.ZipFile]::Open($temp, 'Create')
        try {
            foreach ($entryName in ($Entries | Sort-Object -Unique)) {
                $file = Get-Item -LiteralPath (Join-Path $Base $entryName)
                [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, ($Prefix + $entryName), 'Optimal')
            }
            if ($Prefix) {
                [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                    $zip, (Join-Path $root 'README.md'), ($Prefix + 'README.md'), 'Optimal')
            }
        } finally {
            $zip.Dispose()
        }

        if ([IO.File]::Exists($target)) {
            [IO.File]::Replace($temp, $target, $backup)
        } else {
            [IO.File]::Move($temp, $target)
        }
        Write-Host "Pacote: $target"
    } finally {
        if ([IO.File]::Exists($temp)) { [IO.File]::Delete($temp) }
        if ([IO.File]::Exists($backup)) { [IO.File]::Delete($backup) }
    }
}

$portableName = "3D-Browser-Pro-$version-win64"
$portable = @(
    '3D Browser Pro.exe', 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll'
    'WebView2Loader.dll', 'WebView2-LICENSE.txt', 'WebView2-NOTICE.txt', 'home.html', 'src/content/hook.js'
)
$tree = @('assets', 'native/ui', 'native/inject')
foreach ($folder in $tree) {
    $portable += @(Get-ChildItem -LiteralPath (Join-Path $root $folder) -Recurse -File | ForEach-Object {
        $_.FullName.Substring($root.Length + 1).Replace('\', '/')
    })
}
Write-Package $portableName "$portableName/" $app $portable

$extension = @('manifest.json', 'assets/icon16.png', 'assets/icon32.png', 'assets/icon48.png', 'assets/icon128.png')
foreach ($folder in @('src')) {
    $extension += @(Get-ChildItem -LiteralPath (Join-Path $root $folder) -Recurse -File | ForEach-Object {
        $_.FullName.Substring($root.Length + 1).Replace('\', '/')
    })
}
Write-Package "3D-Browser-Pro-$version-extensao" '' $root $extension
