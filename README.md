# 3D Browser Pro

Navegador particular para **capturar e baixar modelos 3D** de qualquer site:
GLB, glTF, OBJ, FBX, STL, PLY, DAE, 3DS, USDZ — direto da **rede** e da **memória (cena WebGL/THREE.js)**.

Funciona em Tripo, Sketchfab, `<model-viewer>`, viewers de e-commerce, configuradores 3D e qualquer página que carregue um arquivo 3D.

Por dentro é um **navegador próprio**: janela, abas, barra de endereço e painel de capturas escritos em
WinForms com a engine **WebView2**. O `3D Browser Pro.exe` **é** o navegador — não abre o Brave, o
Chrome nem o Edge, e não depende de outro navegador instalado. O mesmo hook de captura também
existe como extensão Manifest V3, para quem preferir usá-lo em um Chrome/Edge/Brave comum.
Criador por Galber Bynario  facebook.com/gmtiranosconquer
Instagram: instagram.com/sansland.mmorpg
---

## Baixar pronto (ZIP com o `.exe`)

Para quem só quer usar: baixe **`3D-Browser-Pro-1.1.0-win64.zip`**, extraia a pasta inteira e dê
duplo clique em **`3D Browser Pro.exe`** — o navegador abre direto na `home.html`, sem instalar,
sem Node e sem abrir o Brave/Chrome/Edge. O ZIP traz o `.exe`, as DLLs do WebView2, `home.html`,
`assets/`, `native/ui`, `native/inject` e `src/content/hook.js` (tudo que o app lê do disco).

O pacote é gerado por:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/pack.ps1
```

Saídas:

| Arquivo | Para quê |
|---|---|
| `dist/3D-Browser-Pro-1.1.0-win64.zip` | versão pronta (extrair e executar), com `LEIA-ME.txt` |
| `3d-browser-pro-codigo-aberto.zip` | código-fonte completo, para subir no GitHub |

O `pack.ps1` compila antes de empacotar (use `-SkipBuild` para reaproveitar o `.exe`) e aceita
`-Version 1.2.0`. O ZIP de código inclui a extensão, os fontes C#, as ferramentas e a pasta
`native/vendor/` necessária para compilar, mas não o `.exe`, logs nem o pacote de distribuição.

No GitHub, publique os dois: o **código** no repositório (conteúdo do
`3d-browser-pro-codigo-aberto.zip`) e a **versão pronta** como *Release* (anexo
`3D-Browser-Pro-1.1.0-win64.zip`), que é o link de download para o usuário final.

## Abrir o navegador (jeito fácil)

Dê **duplo clique em `3D Browser Pro.exe`** (na raiz desta pasta). A janela do 3D Browser Pro abre
em `home.html`, com:

- **abas** (＋ / Ctrl+T, fechar com ✕ / Ctrl+W) e botões ◀ ▶ ⟳ ⌂ na barra superior;
- **barra de endereço** própria (Ctrl+L) — aceita `tripo3d.ai`, `https://...` ou caminho de arquivo;
- **painel de capturas** à direita (▤ / Ctrl+B) com busca, filtros por formato, **Baixar**,
  **Baixar tudo**, **.gltf**, copiar link, exportar a cena como **.glb/.obj**, escanear, limpar e ⚙;
- **visualizador 3D** embutido — **👁** ou duplo clique numa captura, **Abrir…** no painel, **Ctrl+O**
  ou um arquivo como argumento: o modelo abre numa aba própria, girando na tela;
- **zoom** da página com Ctrl+= / Ctrl+- / Ctrl+0;
- **perfil exclusivo** em `%LOCALAPPDATA%\3D Browser Pro\perfil` — histórico e cookies separados do
  seu navegador do dia a dia;
- captura **automática** de tudo que for GLB/glTF/OBJ/FBX/STL/PLY, salvo em
  `Downloads\3D Browser Pro\<site>\` (configurável no ⚙).

Para abrir direto num site, arraste um link sobre o `.exe`, ou rode no prompt:

```bat
"3D Browser Pro.exe" tripo3d.ai
```

O navegador precisa do **Microsoft Edge WebView2 Runtime**. Ele já vem de fábrica no Windows 11 e
na maioria dos Windows 10 atualizados; se faltar, rode uma vez (pede administrador):

```bat
native\vendor\MicrosoftEdgeWebview2Setup.exe /silent /install
```

### Visualizador 3D

Clique em **👁** numa captura do painel, dê **duplo clique** nela, use **Abrir…** / **Ctrl+O** para
escolher um arquivo do disco, ou abra direto pela linha de comando:

```bat
"3D Browser Pro.exe" "D:\modelos\cadeira.glb"
```

Na aba do modelo: arraste para girar, roda do mouse para zoom, botão direito para mover. Os botões
**grade**, **aramado**, **girar** e **enquadrar** (atalhos G, W, S, R) controlam a cena, e **baixar**
grava o arquivo no disco. Lê **GLB, glTF, OBJ, FBX, STL, PLY, DAE e 3DS**, inclusive glTF comprimido
com **Draco** ou **Meshopt** — do jeito que Tripo, Meshy e Sketchfab entregam — com ambiente PBR,
então material metálico e vidro aparecem como no site.

O visualizador carrega o **three.js de uma CDN**, então precisa de internet na primeira vez. Capturar
e baixar continuam funcionando offline — sem rede, o visualizador avisa em vez de ficar em branco.

### Esqueleto e FBX para o Mixamo

Modelo em **T-pose** pode ser convertido em **FBX com esqueleto humanoide já montado**, pronto para
receber animação (Mixamo, Blender, Unreal…):

- botão **ossos** (ou tecla **O**) na aba do visualizador desenha o **esqueleto gerado sobre o
  modelo** — verde, atravessando a malha, com uma bolinha em cada junta; **passar o mouse sobre uma
  junta mostra o nome do osso** (Hips, Spine2, LeftArm…), para conferir o rig antes de animar;
- botão **esqueleto + fbx** na aba do visualizador, botão **fbx** no item do painel (glb/gltf) ou
  pela linha de comando: `"3D Browser Pro.exe" --fbx modelo.glb modelo.fbx [10000]`;
- **peso sob controle**: o seletor ao lado do botão (**fbx: original / 120k / 60k / 20k / 8k tri**)
  reduz a malha antes de gerar o arquivo — modelos de site costumam vir com 100k–300k triângulos,
  e 20k já é mais que suficiente para o Mixamo animar. A escolha fica salva e também vale para o
  FBX do painel e para o automático (⚙ → “Qualidade do FBX com esqueleto”);
- **esqueleto + fbx (.zip)** junta num só pacote o FBX e o modelo original (GLB), então dá para
  subir o FBX no Mixamo e conferir no Blender sem procurar os dois arquivos;
- em **⚙ Configurações**, “Gerar FBX com esqueleto automaticamente” cria o FBX junto com cada
  glb/gltf capturado (sem diálogo);
- o rig tem **23 ossos com os nomes do Mixamo** (`mixamorig:Hips`, `Spine`, `Spine1`, `Neck`, `Head`,
  `LeftArm`, `LeftForeArm`, `LeftHand`, `LeftUpLeg`, `LeftFoot`…), montado em **T-pose** com os ossos
  **medidos na silhueta do próprio modelo** — a linha dos braços, a largura do ombro, o vão entre as
  pernas, o afunilamento do joelho, o tornozelo e o pescoço saem de cortes horizontais da malha, não
  da caixa dela — e **pesos de skin por proximidade ao osso** (até 4 ossos por vértice);
- o FBX é **ASCII 7.4** e é lido pelo three.js do próprio visualizador — dá para conferir o arquivo
  antes de subir em qualquer lugar.

Limites: o FBX é texto (ASCII), então pesa mais que um FBX binário — 20k triângulos dá uns 2–3 MB,
100k passa de 10 MB. A redução solda vértices numa grade (bom para silhueta e para o auto-rig do
Mixamo), mas apaga detalhe fino e suaviza as UVs: para textura nítida use *original*.
O FBX leva geometria + normal + UV + skin (sem texturas embutidas). O esqueleto é genérico,
medido na silhueta (e ajuda proporções clássicas quando o modelo não está em T-pose), não um rig
anatômico junta a junta, e não tem dedos nem ossos de rosto.

### Instalar em um navegador comum (opcional)

1. Abra `chrome://extensions` (ou `edge://extensions`, `brave://extensions`).
2. Ative **Modo do desenvolvedor** (canto superior direito).
3. Clique em **Carregar sem compactação** e escolha esta pasta (a que contém o `manifest.json`).
4. Fixe o ícone **3D Browser Pro** na barra do navegador.

## Como usar

1. Abra o site com o modelo 3D (ex.: `tripo3d.ai`, um viewer Sketchfab, uma loja com `<model-viewer>`).
2. **Dê play / gire o modelo** — muitos sites só carregam a malha depois da interação.
3. Clique no ícone do 3D Browser Pro.
4. O painel mostra:
   - **Lista de arquivos capturados** — formato, site, tamanho e botão **Baixar**.
   - **Cena na memória** — se o site expõe a cena THREE.js, exporte a geometria viva como **.glb** ou **.obj**.
   - Para GLB capturado em memória, o botão **.gltf** gera um glTF **autocontido** (buffer embutido em base64).

Os downloads em memória vão para a pasta padrão do navegador. Os arquivos baixados por URL vão para `Downloads/3D Browser Pro/<site>/` (pasta configurável).

## Configurações

### No navegador próprio (⚙ da janela)

| Opção | Para quê serve |
|---|---|
| Pasta base dos downloads | Onde tudo é salvo. Vazio = `Downloads`. O botão **Procurar…** escolhe a pasta. |
| Subpasta | Subpasta criada dentro da pasta base; cada site vira uma pasta dentro dela. |
| Formatos capturados | Quais formatos entram no painel (glb, gltf, obj, fbx, stl, ply, dae, 3ds, usdz). |
| Baixar automaticamente ao capturar | Salva na hora, sem passar pelo painel. |
| Ignorar capturas menores que (KB) | Descarta ícones, texturas soltas e fragmentos. |
| Tamanho máximo por arquivo (MB) | É também o limite para abrir um arquivo no visualizador. |
| Abrir a pasta depois de salvar | Mostra o arquivo no Explorador assim que ele é gravado. |

O navegador **sempre abre na home** (a área com todas as IAs e atalhos), nunca na última sessão.

As configurações ficam em `%LOCALAPPDATA%\3D Browser Pro\settings.json`.

### Na extensão (legado)

Abrir em: ícone do painel → **⚙** (ou clique direito na extensão → Opções).

| Opção | Para quê serve |
|---|---|
| Pasta de destino | Subpasta em Downloads; cada site vira uma subpasta. Vazio salva direto em Downloads. |
| Formatos capturados | Liga/desliga o que aparece no painel e é guardado na memória. |
| Baixar automaticamente | Salva na hora, por formato (ou **Todos**), sem passar pelo painel. |
| Limites | Tamanho máximo por arquivo e memória por aba. |
| Contador no ícone | Mostra quantos arquivos foram capturados na aba. |
| Manter ao navegar | Mantém a lista ao trocar de página (padrão: limpa a cada navegação). |
| Manutenção | Restaurar padrões e limpar capturas de todas as abas. |

### Botões do painel

- **Baixar tudo** — baixa todos os itens do filtro/busca atual.
- **Baixar** — salva um arquivo específico (URL original ou bytes da memória).
- **.gltf** — converte um GLB em memória para glTF autocontido.
- **🔗** — copia o link direto do arquivo.
- **⚙** — configurações. **pasta** — abre a pasta de downloads. **limpar** — zera a aba.

## Recursos

| Recurso | Descrição |
|---|---|
| Captura de rede | `webRequest` detecta `.glb`, `.gltf`, `.obj`, `.fbx`, `.stl`, `.ply`, `.dae`, `.3ds`, `.usdz` e respostas com MIME de modelo. |
| Captura em memória | Intercepta `fetch`/`XMLHttpRequest` e guarda os **bytes exatos** (funciona com URLs assinadas, POST e `blob:`). |
| Detecção por assinatura | Arquivos sem extensão e servidos como `octet-stream` são identificados pelo cabeçalho (`glTF`, `Kaydara FBX`, `ply`, JSON glTF, OBJ). |
| Cena ao vivo | Envolve `THREE.WebGLRenderer.prototype.render` e lê `window.scene`, `model-viewer.model` etc. |
| Exportar GLB | Escritor de glTF 2.0 próprio: hierarquia, TRS, normais, UV, índices e materiais PBR. |
| Exportar OBJ | Geometria tesselada com matrizes de mundo aplicadas e matriz normal correta. |
| GLB → glTF | Repack do container: chunk JSON + BIN em data URI. |
| Painel | Busca, filtros por formato, “Baixar tudo”, copiar link e atalho para as configurações. |
| Visualizador 3D | Aba própria com three.js + OrbitControls: grade, aramado, rotação automática, enquadrar e salvar. |
| Cena JavaScript (three.js) | O visualizador de qualquer site pode ser exportado: **Ctrl+E** grava a cena atual em GLB e **Ctrl+Shift+E** em OBJ, além dos botões **GLB**/**OBJ** no painel. Funciona também em sites que usam three.js como módulo (sem `window.THREE`), via o canal `__THREE_DEVTOOLS__`. |
| Download do site | O botão **Download** de qualquer site (Meshy, Tripo, Sketchfab) é gravado na pasta configurada, tem a extensão corrigida pelo conteúdo e entra no painel com prévia. Quando chega, o painel **abre sozinho se estava fechado**, pisca e destaca o item novo. |
| Formato pelo conteúdo | O navegador identifica o modelo pelos **primeiros bytes** (glTF, FBX, OBJ, PLY, STL, DAE, 3DS, USDZ) e descarta textura, áudio e zip — mesmo quando a URL é assinada, não tem extensão ou mente sobre ela. |
| 100% local | Sem servidores, sem telemetria, sem dependências externas. Nada sai do seu computador. |

## Limitações honestas

- **Texturas não são exportadas** pelo exportador de cena (só geometria + cor do material). O arquivo original capturado mantém tudo.
- **FBX só é baixado** no formato original — não há escritor FBX (formato proprietário e binário).
- Sites que usam THREE.js como **módulo** (sem `window.THREE`) ou engines como Babylon/Unity/WebGL puro **não expõem a cena**; nesses casos use os arquivos capturados na lista (o `.glb` de origem normalmente aparece).
- Iframes e canvas em outra origem não têm a cena lida em memória, mas o download por URL continua funcionando.
- **Visualizador:** lê glb, gltf, obj, fbx, stl, ply, dae e 3ds; **usdz não** (e glTF com texturas **KTX2** avisa em vez de abrir). Usa o three.js da CDN — sem internet ele avisa em vez de mostrar o modelo. Abrir um modelo **por URL** só funciona se o site liberar CORS; o que passou pelo painel sempre abre.
- Respeite os direitos autorais e os termos de uso dos sites que você visita.

## Doação

O botão **💜 doe e apoie o desenvolvedor** da página inicial abre um cartão com o recado:

> Você conseguiu baixar seus assets — e vai conseguir muito mais. Então nos ajude a ficar felizes
também: doe para o desenvolvedor e ajude o 3D Browser Pro a receber mais atualizações.

A doação é para **Galber Bynario**, desenvolvedor da ferramenta, pelo LivePix:
<https://livepix.gg/sansland> (o **SanSland** é só a referência da conta/link — não é a doação do
jogo). A logo oficial do LivePix fica em destaque no cartão dos créditos e nos botões de doação.

O cartão **não tem campo de texto**: ele traz um tutorial de 3 passos — o doador escreve o próprio
canal de comunicação (Telegram, WhatsApp, e-mail, Discord…) **no campo de mensagem do LivePix**,
e o desenvolvedor entra em contato para enviar as novas ferramentas. O botão **copiar mensagem
pronta** coloca um texto-modelo na área de transferência para colar nesse campo:

```
Doei para o 3D Browser Pro 💜 meu canal de comunicação: (escreva seu contato no campo de mensagem do LivePix)
```

Abaixo da doação há o bloco do **SanSland** (logo + convite), com o Discord
<https://discord.com/invite/kb7g3WqmHT>: entre no grupo e procure em **ferramentas usadas pelo
criador** — o link também fica no LivePix na hora de doar.

A página de doação e os links abrem no **navegador padrão do PC**, fora do app — como os créditos.

## Proteção (anti-engenharia-reversa)

O `3D Browser Pro.exe` não abre quando as condições abaixo não batem. Todas as checagens estão em
`native/shield.cs` e rodam **antes** de qualquer janela (inclusive nos modos de linha de comando):

| Camada | O que faz |
|---|---|
| Anti-depurador | Recusa abrir com depurador gerenciado/nativo anexado (`Debugger.IsAttached`, `Debugger.IsLogging`, `CheckRemoteDebuggerPresent`). Um timer recheca a cada 15 s e fecha a sessão se alguém anexar a depuração depois. |
| Anti-ferramenta | Recusa abrir com decompilador/despejador de IL aberto: dnSpy, ILSpy, dotPeek, JustDecompile, .NET Reflector, de4dot, Ghidra, IDA, x64dbg/x32dbg, OllyDbg, Cheat Engine, MegaDumper, Scylla, PE-bear, HxD, Process Hacker. Os nomes ficam embaralhados (XOR) no binário, não aparecem como texto. |
| Assinatura dos arquivos | `tools/seal.ps1` grava o SHA-256 de `home.html`, `native/ui/*`, `native/inject/shim.js`, `src/content/hook.js` e `assets/icon.ico` em `native/seal.g.cs`, compilado dentro do exe. Trocar qualquer um desses arquivos (tirar o botão de doação, injetar script) faz o app avisar e não abrir. |

O build já roda o `tools/seal.ps1` sozinho, então editar HTML/JS e recompilar funciona normalmente.

**O que isso não é:** um binário .NET pode ser decompilado por definição — nenhuma dessas camadas
impede isso, elas só encarecem o caminho (patching, despejo de IL, redistribuição modificada).
Para uma proteção realmente forte seria preciso ofuscador comercial (.NET Reactor / ConfuserEx) e
assinatura de código com certificado — nada disso está aqui. Quem quiser o código tem o fonte
aberto em `3d-browser-pro-codigo-aberto.zip`: a ideia é estudar e contribuir.

## Estrutura

```
manifest.json              MV3, permissões e scripts (versão extensão)
3D Browser Pro.exe         O NAVEGADOR (WinForms + WebView2, C# 5, ~125 KB)
Abrir 3D Browser Pro.bat   atalho que abre o navegador, aceita URL como argumento
home.html                  página inicial do navegador
native/Program.cs          fonte do navegador: abas, barra, painel, captura, downloads
native/sniff.cs            identifica o formato pelos bytes (glTF, FBX, OBJ, PLY, STL, DAE, 3DS, USDZ)
native/inject/shim.js      ponte página -> host (blocos base64 via chrome.webview)
native/ui/panel.*          painel de capturas (HTML/CSS/JS) dentro do navegador
native/ui/viewer.*         visualizador 3D em aba própria (three.js via CDN)
native/shield.cs          barreiras anti-depuração/decompilação e conferência dos arquivos
native/seal.g.cs          assinatura SHA-256 dos arquivos, gerada por tools/seal.ps1 (não editar)
native/test/capture.html   página de teste: baixa GLBs e confere a captura
native/test/gltf-check.ps1 testa a conversão GLB -> glTF do app compilado
native/test/sniff-check.ps1 testa a identificação de formato pelos bytes
native/test/download.html   testa o download disparado por um site (blob: + nome próprio)
native/vendor/             SDK do WebView2 (net45) + instalador do runtime
Microsoft.Web.WebView2.*.dll, WebView2Loader.dll   runtime do app (ao lado do .exe)
legacy/                    launcher antigo do Brave e a extensão como estavam antes
launcher/Launcher.cs       fonte do launcher legado (C# 5)
assets/icon*.png           ícone gerado (16/32/48/128/256)
assets/icon.ico            ícone do executável (5 tamanhos)
assets/icon.svg            versão vetorial / favicon
assets/icon-preview.html   página para ver o ícone em todos os tamanhos
src/background.js          service worker: webRequest, downloads, roteamento
src/content/hook.js        mundo da página: fetch/XHR, cena THREE, escritores GLB/OBJ
src/content/bridge.js      mundo isolado: bytes em memória, conversões, salvar arquivo
src/lib/formats.js         tabela de formatos e utilitários
src/lib/settings.js        padrões, validação e persistência das configurações
src/popup/*                painel de captura
src/options/*              página de configurações
tools/seal.ps1             gera native/seal.g.cs com o SHA-256 dos arquivos do app
tools/make-icons.ps1       gerador de PNGs (PowerShell + GDI+)
tools/make-ico.ps1         empacota os PNGs em assets/icon.ico
tools/build-exe.bat        compila o "3D Browser Pro.exe" (csc do .NET Framework)
tools/pack.ps1             gera o ZIP pronto (dist/) e o ZIP de código-fonte para o GitHub
assets/logos/              logos dos sites, LivePix e SanSland usados na home e na doação
dist/                      saída do pack.ps1 (ignorada pelo git)
tools/selftest.js          autoteste de GLB/OBJ/glTF (roda no navegador)
tools/build-selftest.sh    monta tools/_selfcheck.html para o autoteste
```

## Gerar os ícones novamente

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/make-icons.ps1
```

O script não tem dependências: usa o GDI+ do Windows, desenha em 4x e reduz com bicúbica
preservando o canal alfa.

## Recompilar o navegador

```bat
tools\build-browser.bat
```

Compila `native/Program.cs` com o `csc.exe` que já vem no Windows (`.NET Framework 4`, C# 5) e copia
as DLLs do WebView2 para a raiz, ao lado do `.exe`. Sem SDK, Node, NuGet nem download — o SDK já
está em `native/vendor/` (versão em `native/vendor/sdk-version.txt`).
O ícone do `.exe` vem de `assets/icon.ico`.

Se o app não abrir, veja `%LOCALAPPDATA%\3D Browser Pro\log.txt` — ele registra engine,
navegação e cada captura. Erros de compilação ficam em `native/build-log.txt`.

### Versão extensão (legado)

O launcher antigo, que abria o Brave com a extensão carregada, continua em
`legacy\3D Browser Pro (atalho Brave).exe` (fonte em `launcher/Launcher.cs`, build em
`tools\build-exe.bat`).

## Autoteste (sem Node)

```bash
bash tools/build-selftest.sh
```

Abra `tools/_selfcheck.html` no navegador: ele embute os fontes da extensão, valida a sintaxe,
monta uma cena 3D falsa e confere o GLB byte a byte (cabeçalho, chunks, acessores, hierarquia),
a exportação OBJ e a conversão GLB → glTF.

## Ajustes rápidos

- Mais formatos → `src/lib/formats.js` e `EXT_3D` em `src/content/hook.js`.
- Teto de memória por aba → `MAX_TOTAL` em `src/content/bridge.js` (padrão 160 MB).
- Tamanho máximo por arquivo → `MAX_CAPTURE` em `src/content/hook.js` (padrão 96 MB).
