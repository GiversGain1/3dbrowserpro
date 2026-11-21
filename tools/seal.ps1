param([Parameter(Mandatory = $true)][string]$OutputPath)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$files = @(
    'home.html'
    'native/ui/viewer.html'
    'native/ui/panel.html'
    'native/ui/panel.css'
    'native/ui/panel.js'
    'native/ui/viewer.css'
    'native/inject/shim.js'
    'src/content/hook.js'
    'assets/icon.ico'
    'assets/logos/livepix.svg'
    'assets/logos/sansland.png'
)

$files += @(Get-ChildItem -LiteralPath (Join-Path $root 'native/ui/vendor') -Recurse -File | Sort-Object FullName | ForEach-Object {
    $_.FullName.Substring($root.Length + 1).Replace('\', '/')
})
$sha = [Security.Cryptography.SHA256]::Create()
try {
    $entries = foreach ($file in $files) {
        $stream = [IO.File]::OpenRead((Join-Path $root $file))
        try {
            $digest = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant()
            "$file=$digest;"
        } finally {
            $stream.Dispose()
        }
    }
} finally {
    $sha.Dispose()
}

$source = @"
namespace BP3D
{
    internal static class SealData
    {
        internal const string Files = "$($entries -join '')";
    }
}
"@
[IO.File]::WriteAllText($OutputPath, $source, (New-Object System.Text.UTF8Encoding($false)))
