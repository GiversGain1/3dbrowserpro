# Histórico

## 1.2.0

- Código-fonte aberto na árvore do fork. Os ZIPs de distribuição são gerados pelo workflow de Windows. Os ZIPs antigos permanecem no histórico do Git.
- Compilação x64 com o .NET Framework, manifesto de integridade gerado durante o build e pacotes separados para o aplicativo portátil e a extensão.
- Visualização local sem CDN e download com os bytes originais, mesmo com o esqueleto da prévia ativado. Conversões exigem saída nova e sinalizam falhas no código de retorno.
- Validação de tamanho das transferências e isolamento por aba. Comandos do painel e do visualizador limitados às respectivas páginas internas.

### Verificação

O teste do pacote portátil no Windows cobre captura via `fetch`, download iniciado pela página, abertura de GLB texturizado no WebView2, reempacotamento para glTF, exportação OBJ/STL/PLY/FBX, rig em GLB e recusa de entrada truncada ou saída existente. O ZIP da extensão tem manifesto e sintaxe JavaScript verificados.

O modelo de teste [BoxTextured](https://github.com/KhronosGroup/glTF-Sample-Assets/tree/main/Models/BoxTextured) é © 2017 Cesium, sob [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). O arquivo está sem alterações. Veja a atribuição do logotipo na página original.
