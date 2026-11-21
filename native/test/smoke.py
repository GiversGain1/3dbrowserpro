"""Verifica os pacotes e executa a versão portátil com um perfil descartável."""
import argparse
import base64
from functools import partial
from hashlib import sha256
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import struct
import subprocess
from tempfile import TemporaryDirectory
from threading import Thread
from time import monotonic, sleep
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parents[2]
VERSION = (ROOT / "VERSION").read_text(encoding="utf-8").strip()
FIXTURE = ROOT / "native/test/BoxTextured.glb"
HTML = b"""<!doctype html><meta charset=utf-8><title>Teste 3D local</title>
<script>
fetch('BoxTextured.glb').then(response => response.arrayBuffer()).then(bytes => {
    const link = document.createElement('a');
    link.href = URL.createObjectURL(new Blob([bytes], {type: 'model/gltf-binary'}));
    link.download = 'download-do-site.glb';
    link.click();
});
</script>"""


def verify_archive(path, temp):
    with ZipFile(path) as package:
        assert package.testzip() is None
        names = package.namelist()
        assert len(names) == len(set(names))
        assert all(not name.startswith("/") and "\\" not in name and ".." not in name.split("/") for name in names)
        package.extractall(temp)
    return names


def run_cli(exe, stage, env):
    def convert(*args, expected=0):
        result = subprocess.run([str(exe), *map(str, args)], env=env, timeout=25)
        assert result.returncode == expected, (args, result.returncode, expected)

    source = FIXTURE.read_bytes()
    json_size, _ = struct.unpack_from("<II", source, 12)
    _, binary_type = struct.unpack_from("<II", source, 20 + json_size)
    assert binary_type == 0x004E4942
    converted = stage / "Box.gltf"
    convert("--conv", "gltf", FIXTURE, converted)
    metadata = json.loads(converted.read_text(encoding="utf-8"))
    assert metadata["asset"]["version"] == "2.0"
    assert metadata["images"][0]["bufferView"] >= 0 and metadata["textures"][0]["source"] == 0
    assert base64.b64decode(metadata["buffers"][0]["uri"].split(",", 1)[1]) == source[28 + json_size:]

    for ext in ("obj", "stl", "ply"):
        output = stage / f"Box.{ext}"
        convert("--conv", ext, FIXTURE, output)
        assert output.stat().st_size > 0
    output = stage / "Box.fbx"
    convert("--fbx", FIXTURE, output, 10)
    assert output.read_bytes().startswith(b"; FBX")
    output = stage / "Box-rig.glb"
    convert("--skin", FIXTURE, output)
    result = output.read_bytes()
    gltf = json.loads(result[20:20 + struct.unpack_from("<I", result, 12)[0]])
    assert gltf["skins"]
    convert("--conv", "obj", FIXTURE, stage / "Box.obj", expected=1)
    convert("--conv", "unsupported", FIXTURE, stage / "Box.unsupported", expected=1)
    convert("--skin", FIXTURE, FIXTURE, expected=1)
    assert FIXTURE.read_bytes() == source
    assert not (stage / "Box.unsupported").exists()

    invalid = stage / "truncated.glb"
    invalid.write_bytes(source[:-1])
    convert("--conv", "gltf", invalid, stage / "truncated.gltf", expected=1)
    assert not (stage / "truncated.gltf").exists()
    print("CLI: GLB, glTF, OBJ, STL, PLY, FBX e erros validados", flush=True)


def wait_until(check, process, profile):
    deadline = monotonic() + 25
    while monotonic() < deadline:
        if process.poll() is not None:
            break
        if check():
            return
        sleep(0.25)
    log = profile / "log.txt"
    raise AssertionError(log.read_text(encoding="utf-8", errors="replace") if log.exists() else "Sem log do WebView2")


def run_desktop(exe, stage, env):
    profile = stage / "automation"
    profile.mkdir()
    downloads = stage / "downloads"
    settings = {"autoDownload": True, "downloadRoot": str(downloads)}
    (profile / "settings.json").write_text(json.dumps(settings), encoding="utf-8")
    env = {
        **env,
        "BP3D_DATA_DIR": str(profile),
        "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS": "--host-resolver-rules=MAP * ~NOTFOUND, EXCLUDE 127.0.0.1",
    }
    web = stage / "site"
    web.mkdir()
    web.joinpath("index.html").write_bytes(HTML)
    web.joinpath("BoxTextured.glb").write_bytes(FIXTURE.read_bytes())

    def launch(argument, condition):
        proc = subprocess.Popen([str(exe), str(argument)], env=env)
        try:
            wait_until(condition, proc, profile)
        finally:
            if proc.poll() is None:
                proc.terminate()
                proc.wait(timeout=15)

    expected = sha256(FIXTURE.read_bytes()).digest()

    def same_file(name):
        return any(sha256(path.read_bytes()).digest() == expected for path in downloads.rglob(name))

    handler = partial(SimpleHTTPRequestHandler, directory=str(web))
    with ThreadingHTTPServer(("127.0.0.1", 0), handler) as server:
        thread = Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            launch(f"http://127.0.0.1:{server.server_port}/",
                   lambda: same_file("BoxTextured.glb") and same_file("download-do-site.glb"))
        finally:
            server.shutdown()
            thread.join()

    print("WebView2: captura de rede e download de site preservaram o SHA-256", flush=True)
    log = profile / "log.txt"
    launch(FIXTURE, lambda: log.exists() and "viewer: 1 malha(s), 12 triângulos" in log.read_text(encoding="utf-8"))
    print("WebView2: a aba de visualização leu a malha local com DNS externo bloqueado", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--desktop", action="store_true", help="roda também a janela do WebView2 em loopback")
    args = parser.parse_args()
    build = ROOT / "build"
    with TemporaryDirectory(prefix="smoke-", dir=build, ignore_cleanup_errors=True) as temp:
        stage = Path(temp)
        name = f"3D-Browser-Pro-{VERSION}-win64"
        verify_archive(ROOT / f"dist/{name}.zip", stage)
        app = stage / name
        ext = stage / "extension"
        names = verify_archive(ROOT / f"dist/3D-Browser-Pro-{VERSION}-extensao.zip", ext)
        manifest = json.loads((ext / "manifest.json").read_text(encoding="utf-8"))
        assert manifest["version"] == VERSION
        assert "native/Program.cs" not in names
        assert manifest["background"]["service_worker"] in names
        for script in ext.rglob("*.js"):
            subprocess.run(["node", "--input-type=module", "--check"], input=script.read_bytes(), check=True)
        print("ZIP portátil e ZIP da extensão validados. Sintaxe JavaScript aprovada.", flush=True)
        exe = app / "3D Browser Pro.exe"
        env = {**os.environ, "BP3D_DATA_DIR": str(stage / "cli")}
        run_cli(exe, stage, env)
        if args.desktop:
            run_desktop(exe, stage, env)


if __name__ == "__main__":
    main()
