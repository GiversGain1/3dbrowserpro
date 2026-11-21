# 3D Browser Pro

Navegador portátil para Windows com WebView2 e uma extensão Manifest V3 para navegadores Chromium. Salva arquivos 3D carregados em páginas ou baixados pelo próprio site. O visualizador abre modelos locais sem buscar bibliotecas em uma CDN.

Projeto original de **Galber Bynario**. [Repositório original](https://github.com/sanicolaspinotti/3dbrowserpro) · [SanSland](https://www.sansland.com/)

## Usar a versão Windows

Extraia o pacote `3D-Browser-Pro-<versão>-win64.zip` inteiro e abra `3D Browser Pro.exe`. O Windows precisa ter o [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/#download). O aplicativo mantém seu próprio perfil em `%LOCALAPPDATA%/3D Browser Pro`. Defina `BP3D_DATA_DIR` como o caminho de outro diretório se precisar de um perfil separado.

- Use **Ctrl+O** para abrir um arquivo local. O painel lateral reúne os modelos detectados e permite salvar ou visualizar cada um.
- **Baixar** e **Original** preservam os bytes recebidos, incluindo texturas, materiais e animações que já estejam no arquivo.
- A exportação da **cena em exibição** salva geometria e cores em GLB ou OBJ quando o visualizador da página é compatível.
- O esqueleto automático e as conversões para OBJ, STL, PLY e FBX geram arquivos derivados. O FBX usa o formato ASCII. Para levar um derivado com esqueleto ao Blender, use a opção **GLB + esqueleto**.
- Um projeto no servidor, uma imagem de prévia e dependências externas de um glTF podem exigir exportações próprias no site de origem. Use GLB com recursos embutidos quando for trabalhar offline.

Os downloads padrão ficam em `Downloads/3D Browser Pro/<site>`. O caminho, a captura automática e os formatos podem ser ajustados no painel. Para projetos no Tripo, confirme qual modelo a página entrega e use a exportação do projeto quando precisar das partes, dos mapas ou dos clipes em arquivos separados.

## Extensão Chromium

Extraia `3D-Browser-Pro-<versão>-extensao.zip` e carregue a pasta que contém `manifest.json` como extensão sem compactação em Chrome, Edge ou Brave. A extensão usa acesso às páginas HTTP/HTTPS para detectar modelos carregados e uma permissão de downloads para gravá-los. O navegador portátil e a extensão têm perfis e configurações próprios.

## Compilar e testar

No Windows x64, com PowerShell e .NET Framework 4.8:

```powershell
pwsh -NoProfile -File tools/build.ps1
pwsh -NoProfile -File tools/pack.ps1
```

O executável fica em `build/portable/`. Os dois ZIPs ficam em `dist/`. O GitHub Actions executa os testes e publica os ZIPs como artefatos do workflow de Windows. Para verificar localmente, use Python 3.11+ e Node.js:

```powershell
pwsh -NoProfile -File native/test/sniff-check.ps1
python native/test/smoke.py
python native/test/smoke.py --desktop
```

O último comando abre temporariamente a versão extraída do ZIP em perfil próprio. O teste usa um modelo de referência texturizado servido em loopback e compara o SHA-256 do arquivo servido com o das duas cópias salvas. Também carrega o GLB no WebView2 sem resolução DNS externa. A documentação de validação está em [CHANGELOG.md](CHANGELOG.md).

```powershell
& './build/portable/3D Browser Pro.exe' --conv gltf modelo.glb modelo.gltf
& './build/portable/3D Browser Pro.exe' --skin modelo.glb modelo-com-rig.glb
& './build/portable/3D Browser Pro.exe' --fbx modelo.glb modelo.fbx 20000
```

Os comandos de conversão retornam código diferente de zero em erro e exigem um arquivo de saída novo.

## Código

| Caminho | Responsabilidade |
| --- | --- |
| `native/` | Aplicativo WinForms, integração WebView2, armazenamento e conversores |
| `native/ui/` | Painel, visualizador e bibliotecas locais |
| `src/` | Extensão Chromium e código de captura compartilhado |
| `native/test/` | Modelo de referência e testes |
| `tools/` | Compilação, geração dos pacotes e utilitários de manutenção |

Dependências vendorizadas com avisos em seus respectivos diretórios. O [SDK WebView2 1.0.2210.55](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.2210.55) fornece as DLLs de integração. O [three.js 0.160.0](native/ui/vendor/three/SOURCE.md) é a mesma versão usada anteriormente pelo visualizador.
