$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$version = (Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'VERSION deve conter major.minor.patch.' }
$build = Join-Path $root 'build'
$app = Join-Path $build 'portable'
$sdk = Join-Path $root 'native/vendor'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'O compilador do .NET Framework para Windows x64 não está disponível.'
}

$dependencies = @(
    'lib/Microsoft.Web.WebView2.Core.dll'
    'lib/Microsoft.Web.WebView2.WinForms.dll'
    'WebView2Loader.dll'
)
foreach ($dependency in $dependencies) {
    if (-not (Test-Path -LiteralPath (Join-Path $sdk $dependency) -PathType Leaf)) {
        throw "Dependência WebView2 ausente: $dependency"
    }
}

$null = New-Item -ItemType Directory -Path $app -Force
$seal = Join-Path $build 'seal.g.cs'
& (Join-Path $PSScriptRoot 'seal.ps1') -OutputPath $seal

$metadata = Join-Path $build 'version.g.cs'
[IO.File]::WriteAllText($metadata, @"
[assembly: System.Reflection.AssemblyTitle("3D Browser Pro")]
[assembly: System.Reflection.AssemblyFileVersion("$version.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("$version")]
"@)

$output = Join-Path $app '3D Browser Pro.exe'
$options = @(
    '/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/codepage:65001'
    "/win32icon:$(Join-Path $root 'assets/icon.ico')"
    "/out:$output"
)
$references = @(
    'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll'
    'System.Web.Extensions.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll'
) + @($dependencies[0..1] | ForEach-Object { Join-Path $sdk $_ })
$options += @($references | ForEach-Object { "/reference:$_" })
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'native') -Filter '*.cs' -File | Sort-Object Name | ForEach-Object FullName)
& $compiler @options @sources $seal $metadata
if ($LASTEXITCODE -ne 0) { throw "Falha de compilação (código $LASTEXITCODE)." }

foreach ($dependency in $dependencies) {
    Copy-Item -LiteralPath (Join-Path $sdk $dependency) -Destination $app -Force
}
foreach ($item in @('LICENSE.txt', 'NOTICE.txt')) {
    Copy-Item -LiteralPath (Join-Path $sdk $item) -Destination (Join-Path $app ("WebView2-" + $item)) -Force
}
foreach ($item in @('home.html', 'assets', 'native/ui', 'native/inject', 'src/content/hook.js')) {
    $parent = Split-Path -Parent (Join-Path $app $item)
    $null = New-Item -ItemType Directory -Path $parent -Force
    Copy-Item -LiteralPath (Join-Path $root $item) -Destination $parent -Recurse -Force
}

Write-Host "Compilado: $output"
