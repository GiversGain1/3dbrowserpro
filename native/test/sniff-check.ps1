# Testa BP3D.Sniff (identificação de formato pelos bytes) no app compilado.
# Uso: powershell -NoProfile -ExecutionPolicy Bypass -File native\test\sniff-check.ps1
$ErrorActionPreference = 'Stop'

$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$exe = Join-Path $root 'build/portable/3D Browser Pro.exe'
if (-not (Test-Path $exe)) { Write-Output "FALHOU: $exe nao existe (compile primeiro)"; exit 1 }

$dir = Join-Path $root ('build/test-sniff-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null

function Save($name, $text) {
  [IO.File]::WriteAllText((Join-Path $dir $name), $text, (New-Object System.Text.UTF8Encoding($false)))
}

# amostras de texto
Save 'a.obj' "# export`nv 0 0 0`nv 1 0 0`nv 0 1 0`nvt 0 0`nvn 0 0 1`nf 1 2 3`n"
Save 'a.ply' "ply`nformat ascii 1.0`nelement vertex 1`nproperty float x`nend_header`n0 0 0`n"
Save 'a.gltf' '{"asset":{"version":"2.0"},"scenes":[{"nodes":[0]}],"nodes":[{"mesh":0}],"meshes":[{"primitives":[]}]}'
Save 'a.dae' '<?xml version="1.0"?><COLLADA xmlns="http://www.collada.org/2005/11/COLLADASchema" version="1.4.1"><asset/></COLLADA>'
Save 'a_ascii.stl' "solid test`nfacet normal 0 0 1`nouter loop`nvertex 0 0 0`nvertex 1 0 0`nvertex 0 1 0`nendloop`nendfacet`nendsolid test`n"
Save 'a_fbx.fbx' "; FBX 7.4.0 project file`nFBXHeaderExtension:  {`n}`n"

# JSON que NÃO é glTF (foi o que entrou como falso positivo no painel)
Save 'config' '{"asset":"pixel","version":2,"pixel":"a2_h6m1wlpdnplj"}'
Save 'precursor' '{"asset":"x"}'

# binários
$b = New-Object byte[] 200
[Text.Encoding]::ASCII.GetBytes('glTF').CopyTo($b, 0)
[BitConverter]::GetBytes([uint32]2).CopyTo($b, 4)
[BitConverter]::GetBytes([uint32]200).CopyTo($b, 8)
[IO.File]::WriteAllBytes((Join-Path $dir 'a.glb'), $b)

$stl = New-Object byte[] (84 + 50)
[Text.Encoding]::ASCII.GetBytes('solid binary').CopyTo($stl, 0)
[BitConverter]::GetBytes([uint32]1).CopyTo($stl, 80)
[IO.File]::WriteAllBytes((Join-Path $dir 'a_bin.stl'), $stl)

$ds = New-Object byte[] 64
[Text.Encoding]::ASCII.GetBytes('MM').CopyTo($ds, 0)
[IO.File]::WriteAllBytes((Join-Path $dir 'a.3ds'), $ds)

# lixo que NÃO pode entrar no painel
Copy-Item (Join-Path $root 'assets\icon32.png') (Join-Path $dir 'tex.png')
$zip = New-Object byte[] 64
[Text.Encoding]::ASCII.GetBytes('PK').CopyTo($zip, 0)
[IO.File]::WriteAllBytes((Join-Path $dir 'pack.zip'), $zip)
$junk = New-Object byte[] 32
[Text.Encoding]::ASCII.GetBytes('hello world, nothing 3d here').CopyTo($junk, 0)
[IO.File]::WriteAllBytes((Join-Path $dir 'lixo.txt'), $junk)

$asm = [Reflection.Assembly]::LoadFrom($exe)
$t = $asm.GetType('BP3D.Sniff')
$flags = [Reflection.BindingFlags]'Static,Public,NonPublic'
$m = $t.GetMethod('Identify', $flags)

$expect = @{
  'a.obj' = 'obj'; 'a.ply' = 'ply'; 'a.gltf' = 'gltf'; 'a.dae' = 'dae'
  'a_ascii.stl' = 'stl'; 'a_fbx.fbx' = 'fbx'; 'a.glb' = 'glb'; 'a_bin.stl' = 'stl'
  'a.3ds' = '3ds'; 'tex.png' = '-'; 'pack.zip' = '-'
  'config' = '(nulo)'; 'precursor' = '(nulo)'
}

function Identify($path) {
  $bytes = [IO.File]::ReadAllBytes($path)
  $callArgs = New-Object object[] 1
  $callArgs[0] = $bytes
  return $m.Invoke($null, $callArgs)
}

$bad = 0
foreach ($name in ($expect.Keys | Sort-Object)) {
  $got = Identify (Join-Path $dir $name)
  if ($null -eq $got) { $got = '(nulo)' }
  $ok = ($got -eq $expect[$name])
  $label = 'ok   '
  if (-not $ok) { $bad++; $label = 'ERRO ' }
  Write-Output ($label + $name.PadRight(12) + ' esperado=' + $expect[$name] + '  obtido=' + $got)
}

$got = Identify (Join-Path $dir 'lixo.txt')
$shown = '(nulo)'
if ($null -ne $got) { $shown = [string]$got }
Write-Output ('lixo.txt     esperado=(nulo)  obtido=' + $shown)

if ($bad -gt 0) { Write-Output "FALHOU: $bad caso(s)"; exit 1 }
Write-Output 'OK - identificacao por bytes'
exit 0
