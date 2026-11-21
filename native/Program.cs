// 3D Browser Pro — navegador próprio (WinForms + WebView2)
// Janela, abas, barra de endereço e painel de captura são nossos; a engine é a WebView2.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BP3D
{
    internal static partial class Program
    {
        /* Malha centralizada + esqueleto medido nela. Guarda o deslocamento para o
           visualizador poder desenhar os ossos exatamente sobre o modelo original. */
        internal class RigData
        {
            public G3 Mesh;
            public List<RigBone> Bones;
            public float OffX, OffY, OffZ;
            public string Shape;
            public string Kind = "humanoide";
            public bool ArmsOut;
        }

        /* Deslocamento usado para centralizar a malha (mesmo do RigOf), sem montar o rig. */
        internal static bool MeshOffsets(byte[] bytes, out float ox, out float oy, out float oz)
        {
            ox = 0f;
            oy = 0f;
            oz = 0f;
            try
            {
                G3 mesh = GltfRead.Load(bytes);
                if (mesh.P.Count == 0) return false;

                float[] min, max;
                mesh.Bounds(out min, out max);
                ox = (min[0] + max[0]) * 0.5f;
                oy = min[1];
                oz = (min[2] + max[2]) * 0.5f;
                return true;
            }
            catch (Exception) { return false; }
        }

        /* Esqueleto humanoide forçado (botão "T-pose" do visualizador). */
        internal static List<RigBone> HumanoidRig(byte[] bytes, out float ox, out float oy, out float oz, out string detail)
        {
            G3 mesh = GltfRead.Load(bytes);
            float[] min, max;
            mesh.Bounds(out min, out max);

            ox = (min[0] + max[0]) * 0.5f;
            oy = min[1];
            oz = (min[2] + max[2]) * 0.5f;

            for (int i = 0; i + 2 < mesh.P.Count; i += 3)
            {
                mesh.P[i] -= ox;
                mesh.P[i + 1] -= oy;
                mesh.P[i + 2] -= oz;
            }

            Shape s;
            return RigFit.BuildHumanoid(mesh, "mixamorig:", out detail, out s);
        }

        internal static RigData RigOf(byte[] bytes)
        {
            G3 mesh = GltfRead.Load(bytes);
            if (mesh.P.Count == 0 || mesh.I.Count == 0) throw new Exception("malha vazia no glb/gltf");

            float[] min, max;
            mesh.Bounds(out min, out max);

            // pé no chão (y=0) e centro no eixo x/z, para o esqueleto encaixar
            float cx = (min[0] + max[0]) * 0.5f;
            float cz = (min[2] + max[2]) * 0.5f;
            for (int i = 0; i + 2 < mesh.P.Count; i += 3)
            {
                mesh.P[i] -= cx;
                mesh.P[i + 1] -= min[1];
                mesh.P[i + 2] -= cz;
            }

            RigData d = new RigData();
            d.Mesh = mesh;
            d.OffX = cx;
            d.OffY = min[1];
            d.OffZ = cz;
            /* o esqueleto segue a geometria: humanoide, cadeia ou peça rígida */
            Shape shape = RigFit.Measure(mesh);
            string kind = RigFit.Classify(mesh, shape).Kind;
            string prefix = kind == "humanoide" ? "mixamorig:" : "";

            d.Bones = RigFit.BuildAuto(mesh, prefix, out d.Shape, out shape, out kind);
            d.Kind = kind;
            d.ArmsOut = shape.ArmsOut && kind == "humanoide";
            return d;
        }

    /* Conversão GLB/glTF -> FBX com esqueleto, também usada pela linha de comando:
           "3D Browser Pro.exe" --fbx modelo.glb modelo.fbx [maxTriangulos] */
        /* custom != null usa o esqueleto ajustado à mão no visualizador (já centralizado).
           maxTriangles > 0 reduz a malha antes do skin (FBX leve para Mixamo/Blender). */
        internal static byte[] FbxFromGlbStatic(byte[] bytes, string name, out string info, List<RigBone> custom, int maxTriangles)
        {
            RigData d = RigOf(bytes);
            bool edited = custom != null && custom.Count > 0;
            List<RigBone> bones = edited ? custom : d.Bones;

            int before = d.Mesh.Triangles;
            G3 mesh = Decimate.ToTriangles(d.Mesh, maxTriangles);

            info = bones.Count + " osso(s) [" + d.Kind + "] · " + mesh.Triangles + " triângulos" +
                   (mesh.Triangles < before ? " (de " + before + ")" : "") + " · " +
                   (edited ? "esqueleto ajustado à mão" : d.Shape);
            return Fbx.Build(mesh, name, bones);
        }

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            /* barreiras anti-engenharia-reversa: depurador, ferramentas de decompilação e
               arquivos do app fora da assinatura do build. Roda antes de qualquer janela. */
            if (!Shield.Guard()) { Environment.ExitCode = 1; return; }
            if (RunCli(args)) return;

            string start = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("--")) continue;
                start = args[i];
                break;
            }

            bool openingModel = !string.IsNullOrEmpty(start)
                && start.IndexOf("://", StringComparison.Ordinal) < 0
                && File.Exists(start);

            /* cartão de abertura: pula quando o usuário já mandou um modelo direto */
            if (!openingModel) SplashForm.ShowSplash();

            try
            {
                Application.Run(new MainForm(start));
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Falha ao iniciar o 3D Browser Pro:\n\n" + ex.Message,
                    "3D Browser Pro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal class CaptureAsset
    {
        public string key;
        public string url = "";
        public string mime = "";
        public string format = "";
        public string name = "";
        public long size;
        public byte[] bytes;
        public DateTime ts = DateTime.Now;
    }

    internal class Transfer
    {
        public string kind = "bytes";     // bytes | reply
        public string url = "";
        public string mime = "";
        public string format = "";
        public string rid = "";
        public bool ok = true;
        public string error = "";
        public string name = "";
        public int meshes;
        public int triangles;
        public Dictionary<string, object> info;
        public MemoryStream data = new MemoryStream();
        public int expectedSize;
    }

    internal class TabCtx
    {
        public WebView2 View;
        public Panel TabItem;
        public Button TabButton;
        public string Title = "nova aba";
        public string Url = "";
        public bool Ready;
        public string ExportFormat = "glb";
        public string ExportRequest;
        public readonly Dictionary<string, Transfer> Transfers = new Dictionary<string, Transfer>();
        public Timer SceneTimer;
        public Dictionary<string, object> Scene;
        public Dictionary<string, CaptureAsset> Assets = new Dictionary<string, CaptureAsset>();

        /* visualizador 3D (viewer.html) */
        public string Kind = "page";
        public bool ViewerReady;
        public bool ViewerSent;
        public string ViewerName = "";
        public string ViewerFormat = "";
        public string ViewerUrl = "";
        public byte[] ViewerBytes;

        /* esqueleto criado para o modelo aberto (coordenadas centralizadas, como no FBX) */
        public List<RigBone> Rig;
        public float RigOffX, RigOffY, RigOffZ;
        public bool RigEdited;
        public bool RigArmsOut;
        public string RigKind = "";
        public string RigShape = "";

        public bool IsViewer { get { return Kind == "viewer"; } }
    }

    internal class MainForm : Form
    {
        private readonly List<TabCtx> _tabs = new List<TabCtx>();
        private const int MaxTransfersPerTab = 8;
        private readonly JavaScriptSerializer _js = new JavaScriptSerializer();
        private int _active = -1;
        private int _seq;
        private AppSettings _settings;
        private Task<CoreWebView2Environment> _envTask;
        private string _injectJs = "";
        private bool _sideReady;

        private Panel _top;
        private FlowLayoutPanel _tabStrip;
        private Panel _browserHost;
        private Panel _side;
        private WebView2 _sideView;
        private TextBox _address;
        private Button _btnBack, _btnForward, _btnReload, _btnHome, _btnPanel, _btnSettings, _btnNewTab;
        private Label _status;

        public MainForm(string startUrl)
        {
            _js.MaxJsonLength = int.MaxValue;
            Paths.EnsureData();
            _settings = AppSettings.Load();
            LoadRigCache();
            LoadInject();
            Log.W("=== start === url=" + (startUrl == null ? "(vazia)" : startUrl));

            Text = "3D Browser Pro";
            BackColor = Color.FromArgb(11, 11, 19);
            ClientSize = new Size(1360, 860);
            MinimumSize = new Size(900, 560);
            StartPosition = FormStartPosition.CenterScreen;
            WindowState = FormWindowState.Maximized;
            KeyPreview = true;
            Application.AddMessageFilter(new HotkeyFilter(this));

            string ico = Paths.In("assets/icon.ico");
            try { if (File.Exists(ico)) Icon = new Icon(ico); }
            catch (Exception) { }

            BuildUi();

            _envTask = CoreWebView2Environment.CreateAsync(null, Paths.ProfileDir, null);

            bool startFile = IsModelFile(startUrl);
            string target = startFile
                ? Paths.ViewerUri
                : (string.IsNullOrEmpty(startUrl) ? HomeUri() : NormalizeInput(startUrl));
            TabCtx first = CreateTab(target);
            if (startFile && first != null) LoadViewerFile(first, startUrl);

            InitSidePanel();
            Shield.Watch(this);   // depurador anexado depois da abertura também fecha o app
        }

        /* ---------------------------------------------------------------- */
        /* Inicialização                                                     */
        /* ---------------------------------------------------------------- */

        private void LoadInject()
        {
            StringBuilder sb = new StringBuilder();
            try { sb.AppendLine(File.ReadAllText(Paths.In("native/inject/shim.js"))); }
            catch (Exception) { }
            try { sb.AppendLine(File.ReadAllText(Paths.In("src/content/hook.js"))); }
            catch (Exception) { }
            _injectJs = sb.ToString();
            Log.W("inject: " + _injectJs.Length + " chars");
        }

        /* O app sempre abre na home (a área com todas as IAs), nunca na última sessão. */
        private static string HomeUri()
        {
            string home = Paths.In("home.html");
            if (File.Exists(home)) return Paths.UriOf(home);
            return "https://www.bing.com";
        }

        private static string NormalizeInput(string raw)
        {
            string s = (raw ?? "").Trim();
            if (s.Length == 0) return "about:blank";
            if (s.IndexOf("://", StringComparison.Ordinal) >= 0) return s;
            if (s.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return s;
            if (File.Exists(s)) return Paths.UriOf(s);
            if (s.IndexOf(' ') >= 0 || s.IndexOf('.') < 0) return "https://www.bing.com/search?q=" + Uri.EscapeDataString(s);
            return "https://" + s;
        }

        /* ---------------------------------------------------------------- */
        /* Interface                                                          */
        /* ---------------------------------------------------------------- */

        private Panel MakeStrip(int height)
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Top;
            p.Height = height;
            p.BackColor = Color.FromArgb(16, 16, 24);
            p.Padding = new Padding(6, 6, 6, 6);
            return p;
        }

        private Button ToolButton(string glyph, string tip, EventHandler onClick)
        {
            Button b = new Button();
            b.Text = glyph;
            b.Width = 32;
            b.Dock = DockStyle.Left;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = Color.Transparent;
            b.ForeColor = Color.FromArgb(203, 213, 225);
            b.Font = new Font("Segoe UI", 11f);
            b.TabStop = false;
            b.Cursor = Cursors.Hand;
            ToolTip t = new ToolTip();
            t.SetToolTip(b, tip);
            b.Click += onClick;
            return b;
        }

        private void BuildUi()
        {
            _top = MakeStrip(46);

            _btnBack = ToolButton("◀", "Voltar (Alt+←)", delegate { NavigateBack(); });
            _btnForward = ToolButton("▶", "Avançar (Alt+→)", delegate { NavigateForward(); });
            _btnReload = ToolButton("⟳", "Recarregar (F5)", delegate { ReloadPage(); });
            _btnHome = ToolButton("⌂", "Página inicial", delegate { GoHome(); });

            Panel right = new Panel();
            right.Dock = DockStyle.Right;
            right.Width = 76;
            right.BackColor = Color.Transparent;

            _btnSettings = ToolButton("⚙", "Configurações", delegate { ShowSettings(); });
            _btnSettings.Dock = DockStyle.Right;
            _btnPanel = ToolButton("▤", "Capturas (Ctrl+B)", delegate { TogglePanel(); });
            _btnPanel.Dock = DockStyle.Right;
            right.Controls.Add(_btnPanel);
            right.Controls.Add(_btnSettings);

            Panel addrWrap = new Panel();
            addrWrap.Dock = DockStyle.Fill;
            addrWrap.Padding = new Padding(6, 5, 6, 5);
            addrWrap.BackColor = Color.Transparent;

            _address = new TextBox();
            _address.Dock = DockStyle.Fill;
            _address.BorderStyle = BorderStyle.FixedSingle;
            _address.BackColor = Color.FromArgb(9, 9, 16);
            _address.ForeColor = Color.FromArgb(238, 242, 255);
            _address.Font = new Font("Segoe UI", 10f);
            _address.KeyDown += AddressKeyDown;
            addrWrap.Controls.Add(_address);

            _top.Controls.Add(addrWrap);
            _top.Controls.Add(right);
            _top.Controls.Add(_btnHome);
            _top.Controls.Add(_btnReload);
            _top.Controls.Add(_btnForward);
            _top.Controls.Add(_btnBack);

            _tabStrip = new FlowLayoutPanel();
            _tabStrip.Dock = DockStyle.Top;
            _tabStrip.Height = 34;
            _tabStrip.WrapContents = false;
            _tabStrip.AutoScroll = true;
            _tabStrip.BackColor = Color.FromArgb(8, 8, 15);
            _tabStrip.Padding = new Padding(4, 4, 4, 4);

            _btnNewTab = ToolButton("＋", "Nova aba (Ctrl+T)", delegate { CreateTab(null); });
            _btnNewTab.Dock = DockStyle.None;
            _btnNewTab.Width = 30;
            _btnNewTab.Height = 26;
            _btnNewTab.Margin = new Padding(4, 0, 0, 0);
            _tabStrip.Controls.Add(_btnNewTab);

            _browserHost = new Panel();
            _browserHost.Dock = DockStyle.Fill;
            _browserHost.BackColor = Color.FromArgb(8, 8, 15);

            _side = new Panel();
            _side.Dock = DockStyle.Right;
            _side.Width = 350;
            _side.BackColor = Color.FromArgb(11, 11, 19);

            _status = new Label();
            _status.Dock = DockStyle.Bottom;
            _status.Height = 22;
            _status.BackColor = Color.FromArgb(13, 13, 22);
            _status.ForeColor = Color.FromArgb(139, 147, 176);
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.Padding = new Padding(10, 0, 10, 0);
            _status.Font = new Font("Segoe UI", 8.5f);
            _status.Text = "pronto";

            Controls.Add(_browserHost);
            Controls.Add(_side);
            Controls.Add(_tabStrip);
            Controls.Add(_top);
            Controls.Add(_status);

            /* O Fill precisa ser o ÚLTIMO a receber área: no WinForms o dock é
               calculado do fim da coleção para o começo, então mandá-lo para o
               fundo fazia ele ocupar a janela toda e o site vazar atrás das barras. */
            _browserHost.BringToFront();

            KeyDown += MainKeyDown;
        }

        private async void InitSidePanel()
        {
            try
            {
                CoreWebView2Environment env = await _envTask;
                _sideView = new WebView2();
                _sideView.Dock = DockStyle.Fill;
                _sideView.DefaultBackgroundColor = Color.FromArgb(11, 11, 19);
                _side.Controls.Add(_sideView);
                await _sideView.EnsureCoreWebView2Async(env);

                CoreWebView2 core = _sideView.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreHostObjectsAllowed = false;
                core.WebMessageReceived += SideMessage;
                core.NewWindowRequested += delegate (object s, CoreWebView2NewWindowRequestedEventArgs e)
                {
                    e.Handled = true;
                    CreateTab(e.Uri);
                };

                _sideReady = true;
                core.Navigate(Paths.UriOf(Paths.In("native/ui/panel.html")));
            }
            catch (Exception ex)
            {
                Log.W("ERRO painel: " + ex);
                SetStatus("painel indisponível: " + ex.Message);
            }
        }

        /* ---------------------------------------------------------------- */
        /* Abas                                                               */
        /* ---------------------------------------------------------------- */

        private TabCtx CreateTab(string url)
        {
            TabCtx tab = new TabCtx();
            if (url == Paths.ViewerUri)
            {
                tab.Kind = "viewer";
                tab.Title = "visualizador 3D";
            }

            Panel item = new Panel();
            item.Width = 194;
            item.Height = 26;
            item.Margin = new Padding(0, 0, 4, 0);
            item.BackColor = Color.Transparent;

            Button close = new Button();
            close.Text = "✕";
            close.Dock = DockStyle.Right;
            close.Width = 26;
            close.FlatStyle = FlatStyle.Flat;
            close.FlatAppearance.BorderSize = 0;
            close.BackColor = Color.Transparent;
            close.ForeColor = Color.FromArgb(139, 147, 176);
            close.TabStop = false;
            close.Cursor = Cursors.Hand;
            close.Click += delegate { CloseTab(tab); };

            Button title = new Button();
            title.Text = tab.IsViewer ? "👁 visualizador" : "nova aba";
            title.Dock = DockStyle.Fill;
            title.FlatStyle = FlatStyle.Flat;
            title.FlatAppearance.BorderSize = 0;
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.BackColor = Color.Transparent;
            title.ForeColor = Color.FromArgb(203, 213, 225);
            title.Font = new Font("Segoe UI", 9f);
            title.TabStop = false;
            title.Cursor = Cursors.Hand;
            title.Click += delegate { ActivateTab(tab); };

            item.Controls.Add(title);
            item.Controls.Add(close);
            tab.TabItem = item;
            tab.TabButton = title;

            _tabStrip.Controls.Add(item);
            _tabStrip.Controls.SetChildIndex(item, _tabStrip.Controls.Count - 1);
            _tabStrip.Controls.SetChildIndex(_btnNewTab, _tabStrip.Controls.Count - 1);

            WebView2 view = new WebView2();
            view.Dock = DockStyle.Fill;
            view.Visible = false;
            view.DefaultBackgroundColor = Color.FromArgb(8, 8, 15);
            tab.View = view;
            _browserHost.Controls.Add(view);
            _tabs.Add(tab);

            ActivateTab(tab);
            InitTab(tab, url);
            return tab;
        }

        private async void InitTab(TabCtx tab, string url)
        {
            try
            {
                CoreWebView2Environment env = await _envTask;
                await tab.View.EnsureCoreWebView2Async(env);
                CoreWebView2 core = tab.View.CoreWebView2;

                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreDevToolsEnabled = true;
                core.Settings.AreDefaultContextMenusEnabled = true;
                core.Settings.IsZoomControlEnabled = true;
                core.Settings.AreDefaultScriptDialogsEnabled = true;
                Log.W("core pronto (aba) engine=" + core.Environment.BrowserVersionString);

                core.Settings.AreHostObjectsAllowed = false;
                if (tab.IsViewer)
                    core.SetVirtualHostNameToFolderMapping(Paths.ViewerHost, Paths.In("native/ui"), CoreWebView2HostResourceAccessKind.Deny);
                else if (!string.IsNullOrEmpty(_injectJs))
                    await core.AddScriptToExecuteOnDocumentCreatedAsync(_injectJs);

                core.WebMessageReceived += delegate (object s, CoreWebView2WebMessageReceivedEventArgs e)
                {
                    HandlePageMessage(tab, e.Source, e.WebMessageAsJson);
                };

                /* atalhos do app: o teclado vai para o processo do WebView2, então só o
                   AcceleratorKeyPressed do controlador os vê antes da página. */
                HookAccelerators(tab);

                core.DownloadStarting += delegate (object s, CoreWebView2DownloadStartingEventArgs e)
                {
                    HandleDownload(tab, e);
                };

                core.NewWindowRequested += delegate (object s, CoreWebView2NewWindowRequestedEventArgs e)
                {
                    e.Handled = true;
                    CreateTab(e.Uri);
                };

                core.NavigationStarting += delegate (object sender, CoreWebView2NavigationStartingEventArgs e)
                {
                    if (tab.IsViewer && e.Uri != Paths.ViewerUri) e.Cancel = true;
                    tab.Transfers.Clear();
                    tab.ExportRequest = null;
                };
                core.SourceChanged += delegate
                {
                    tab.Url = core.Source;
                    if (tab == Active) { UpdateAddress(); UpdateWindowTitle(); }
                };

                core.DocumentTitleChanged += delegate
                {
                    string t = core.DocumentTitle;
                    if (string.IsNullOrEmpty(t)) t = HostOf(core.Source);
                    tab.Title = t.Length > 34 ? t.Substring(0, 34) + "…" : t;
                    tab.TabButton.Text = tab.Title;
                    if (tab == Active) UpdateWindowTitle();
                };

                core.NavigationCompleted += delegate (object s, CoreWebView2NavigationCompletedEventArgs e)
                {
                    Log.W("nav ok=" + e.IsSuccess + " status=" + e.WebErrorStatus + " site=" + HostOf(core.Source));
                    tab.Ready = true;
                    if (!tab.IsViewer) { SendConfig(tab); ScheduleScan(tab); }
                    SendHomeStats(tab);
                    UpdateNavButtons();
                };

                tab.Ready = true;
                if (string.IsNullOrEmpty(url)) url = HomeUri();
                core.Navigate(url);
            }
            catch (Exception ex)
            {
                Log.W("ERRO aba: " + ex);
                SetStatus("não foi possível abrir a aba: " + ex.Message);
                MessageBox.Show(
                    "Não foi possível iniciar a engine do navegador.\n\n" + ex.Message +
                    "\n\nSe o erro falar em WebView2, instale o 'Microsoft Edge WebView2 Runtime'.",
                    "3D Browser Pro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private TabCtx Active
        {
            get { return (_active >= 0 && _active < _tabs.Count) ? _tabs[_active] : null; }
        }

        private void ActivateTab(TabCtx tab)
        {
            int idx = _tabs.IndexOf(tab);
            if (idx < 0) return;
            _active = idx;

            for (int i = 0; i < _tabs.Count; i++)
            {
                TabCtx t = _tabs[i];
                bool on = (i == idx);
                t.View.Visible = on;
                t.TabItem.BackColor = on ? Color.FromArgb(26, 26, 40) : Color.Transparent;
                t.TabButton.ForeColor = on ? Color.White : Color.FromArgb(148, 163, 184);
            }

            UpdateAddress();
            UpdateWindowTitle();
            UpdateNavButtons();
            PushAssets();
            PushScene();
        }

        private void CloseTab(TabCtx tab)
        {
            int idx = _tabs.IndexOf(tab);
            if (idx < 0) return;

            _tabs.RemoveAt(idx);
            try { _tabStrip.Controls.Remove(tab.TabItem); } catch (Exception) { }
            try { _browserHost.Controls.Remove(tab.View); tab.View.Dispose(); } catch (Exception) { }
            if (tab.SceneTimer != null) tab.SceneTimer.Dispose();

            if (_tabs.Count == 0) { CreateTab(null); return; }
            if (idx <= _active) _active = Math.Min(_active, _tabs.Count - 1);
            ActivateTab(_tabs[Math.Max(0, _active)]);
        }

        private void UpdateAddress()
        {
            TabCtx t = Active;
            if (t == null) return;
            if (t.IsViewer) { if (!_address.Focused) _address.Text = "visualizador://" + (t.ViewerName ?? ""); return; }
            string url = t.Url ?? "";
            if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) url = "3d-browser://inicio";
            if (_address.Focused) return;
            _address.Text = url;
        }

        private void UpdateWindowTitle()
        {
            TabCtx t = Active;
            Text = t == null ? "3D Browser Pro" : t.Title + " — 3D Browser Pro";
        }

        private void UpdateNavButtons()
        {
            TabCtx t = Active;
            bool ok = t != null && t.Ready && t.View.CoreWebView2 != null;
            _btnBack.Enabled = ok && t.View.CoreWebView2.CanGoBack;
            _btnForward.Enabled = ok && t.View.CoreWebView2.CanGoForward;
        }

        /* ---------------------------------------------------------------- */
        /* Navegação                                                          */
        /* ---------------------------------------------------------------- */

        private void AddressKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            NavigateTo(_address.Text);
        }

        private void NavigateTo(string input)
        {
            TabCtx t = Active;
            if (t == null || t.View.CoreWebView2 == null) return;
            string url = NormalizeInput(input);
            t.View.CoreWebView2.Navigate(url);
            _browserHost.Focus();
        }

        private void NavigateBack()
        {
            TabCtx t = Active;
            if (t != null && t.View.CoreWebView2 != null && t.View.CoreWebView2.CanGoBack) t.View.CoreWebView2.GoBack();
        }

        private void NavigateForward()
        {
            TabCtx t = Active;
            if (t != null && t.View.CoreWebView2 != null && t.View.CoreWebView2.CanGoForward) t.View.CoreWebView2.GoForward();
        }

        private void ReloadPage()
        {
            TabCtx t = Active;
            if (t != null && t.View.CoreWebView2 != null) t.View.CoreWebView2.Reload();
        }

        private void GoHome()
        {
            TabCtx t = Active;
            if (t == null || t.View.CoreWebView2 == null) return;
            t.View.CoreWebView2.Navigate(HomeUri());
        }

        /* Atalhos com o foco dentro do WebView2 (o KeyDown do form não os recebe). */
        private class HotkeyFilter : IMessageFilter
        {
            private readonly MainForm _f;
            public HotkeyFilter(MainForm f) { _f = f; }

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != 0x0100) return false;                  // só WM_KEYDOWN
                if ((ModifierKeys & Keys.Control) != Keys.Control) return false;

                Keys k = (Keys)m.WParam.ToInt32();
                if (k == Keys.E) { Log.W("atalho: Ctrl+E"); _f.ExportScene((ModifierKeys & Keys.Shift) == Keys.Shift ? "obj" : "glb"); return true; }
                if (k == Keys.B) { _f.TogglePanel(); return true; }
                return false;
            }
        }

        /* O controlador do WebView2 não é público no wrapper WinForms: pegamos por
           reflexão e assinamos AcceleratorKeyPressed (o teclado não chega ao form). */
        private void HookAccelerators(TabCtx tab)
        {
            try
            {
                object ctrl = null;
                System.Reflection.PropertyInfo pi = tab.View.GetType().GetProperty(
                    "CoreWebView2Controller",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (pi != null) ctrl = pi.GetValue(tab.View, null);

                if (ctrl == null)
                {
                    System.Reflection.FieldInfo fi = tab.View.GetType().GetField(
                        "_coreWebView2Controller",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (fi != null) ctrl = fi.GetValue(tab.View);
                }
                if (ctrl == null) { Log.W("atalhos: controlador não encontrado"); return; }

                System.Reflection.EventInfo ev = ctrl.GetType().GetEvent("AcceleratorKeyPressed");
                if (ev == null) { Log.W("atalhos: evento indisponível"); return; }

                System.Reflection.MethodInfo mi = GetType().GetMethod("OnAcceleratorKey",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Delegate handler = Delegate.CreateDelegate(ev.EventHandlerType, this, mi);
                ev.AddEventHandler(ctrl, handler);
                Log.W("atalhos: Ctrl+E / Ctrl+B ligados");
            }
            catch (Exception ex) { Log.W("atalhos: " + ex.Message); }
        }

        /* assinatura tem que casar com o delegate interno do WebView2 */
        private void OnAcceleratorKey(object sender, CoreWebView2AcceleratorKeyPressedEventArgs e)
        {
            if (e.KeyEventKind != CoreWebView2KeyEventKind.KeyDown
                && e.KeyEventKind != CoreWebView2KeyEventKind.SystemKeyDown) return;
            if ((Control.ModifierKeys & Keys.Control) != Keys.Control) return;

            Keys k = (Keys)e.VirtualKey;
            if (k == Keys.E)
            {
                e.Handled = true;
                ExportScene((Control.ModifierKeys & Keys.Shift) == Keys.Shift ? "obj" : "glb");
            }
            else if (k == Keys.B) { e.Handled = true; TogglePanel(); }
            else if (k == Keys.L) { e.Handled = true; _address.Focus(); _address.SelectAll(); }
            else if (k == Keys.T) { e.Handled = true; CreateTab(null); }
            else if (k == Keys.W) { e.Handled = true; if (Active != null) CloseTab(Active); }
            else if (k == Keys.F && (Control.ModifierKeys & Keys.Shift) == Keys.Shift)
            {
                e.Handled = true;
                Log.W("atalho: Ctrl+Shift+F (FBX do visualizador)");
                if (Active != null && Active.IsViewer) ExportViewerFbx(Active, _settings.fbxTriangles, false);
                else SetStatus("abra um modelo no visualizador para exportar o FBX");
            }
        }

        private void TogglePanel()
        {
            _side.Visible = !_side.Visible;
            if (_side.Visible) PushAssets();
        }

        private void MainKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.T) { CreateTab(null); e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.W) { if (Active != null) CloseTab(Active); e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.L) { _address.Focus(); _address.SelectAll(); e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.B) { TogglePanel(); e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.E) { ExportScene(e.Shift ? "obj" : "glb"); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.F5) { ReloadPage(); e.SuppressKeyPress = true; }
            else if (e.Alt && e.KeyCode == Keys.Left) { NavigateBack(); e.SuppressKeyPress = true; }
            else if (e.Alt && e.KeyCode == Keys.Right) { NavigateForward(); e.SuppressKeyPress = true; }
            else if (e.Control && (e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Add)) { ZoomBy(0.1); e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.OemMinus) { ZoomBy(-0.1); e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.D0) { ZoomBy(0); e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.O) { OpenFileDialogForModel(); e.SuppressKeyPress = true; }
        }

        private void ZoomBy(double delta)
        {
            TabCtx t = Active;
            if (t == null || t.View.CoreWebView2 == null) return;
            double z = delta == 0 ? 1.0 : t.View.ZoomFactor + delta;
            if (z < 0.25) z = 0.25;
            if (z > 4.0) z = 4.0;
            t.View.ZoomFactor = z;
            SetStatus("zoom " + (int)Math.Round(z * 100) + "%");
        }

        private void RevealFile(string file)
        {
            try { Process.Start("explorer.exe", "/select,\"" + file + "\""); }
            catch (Exception) { }
        }

        private void SetStatus(string text)
        {
            if (_status != null) _status.Text = text;
            PushPanel(new Dictionary<string, object> {
                { "type", "toast" }, { "text", text }
            });
        }

        /* ---------------------------------------------------------------- */
        /* Downloads nativos                                                  */
        /* ---------------------------------------------------------------- */

        private void HandleDownload(TabCtx tab, CoreWebView2DownloadStartingEventArgs e)
        {
            try
            {
                CoreWebView2DownloadOperation op = e.DownloadOperation;
                string uri = op.Uri ?? "";
                string host = HostOf(uri);
                // blob:/data: não têm host; usa o site da aba para o arquivo não cair na raiz
                if (string.IsNullOrEmpty(host) && tab != null) host = HostOf(tab.Url);
                string dir = _settings.DownloadDir(host);
                Directory.CreateDirectory(dir);

                string name = FileNameFromUrl(uri);
                // o navegador já sugere o nome (Content-Disposition ou <a download>); é melhor
                // que o nome tirado da URL, que costuma vir vazia em blob:/URL assinada
                string suggested = "";
                try { suggested = Path.GetFileName(e.ResultFilePath); } catch (Exception) { }
                if (!string.IsNullOrEmpty(suggested) && suggested.IndexOf('.') > 0) name = suggested;
                if (string.IsNullOrEmpty(name)) name = "arquivo-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bin";
                string full = UniquePath(dir, name);

                // NÃO usar e.Handled = true aqui: isso avisa "o app grava o arquivo" e, como
                // ninguém gravava, todo download de botão do site era descartado em silêncio.
                e.ResultFilePath = full;
                Log.W("download iniciado: " + full);
                SetStatus("baixando: " + full);

                op.StateChanged += delegate
                {
                    try
                    {
                        if (op.State == CoreWebView2DownloadState.Completed) OnDownloadDone(tab, op, full);
                        else if (op.State == CoreWebView2DownloadState.Interrupted)
                        {
                            Log.W("download interrompido: " + op.InterruptReason);
                            SetStatus("download interrompido: " + op.InterruptReason);
                        }
                    }
                    catch (Exception ex) { Log.W("ERRO download: " + ex.Message); }
                };
            }
            catch (Exception ex)
            {
                SetStatus("download falhou: " + ex.Message);
            }
        }

        /* Depois que o WebView2 grava o arquivo: confere o formato pelo conteúdo,
           corrige a extensão e põe o item na lista (com prévia) — é assim que Meshy & cia
           entram no painel, já que nessas plataformas o modelo só existe no botão Download. */
        private void OnDownloadDone(TabCtx tab, CoreWebView2DownloadOperation op, string path)
        {
            try
            {
                if (!File.Exists(path)) { Log.W("download sem arquivo: " + path); return; }

                long max = (long)_settings.maxFileMB * 1024 * 1024;
                if (new FileInfo(path).Length > max)
                {
                    SetStatus("baixado fora do limite do painel: " + path);
                    return;
                }

                byte[] bytes = File.ReadAllBytes(path);
                string sniffed = Sniff.Identify(bytes);
                Log.W("download pronto: " + path + " (" + bytes.Length + " B, " + (string.IsNullOrEmpty(sniffed) ? "?" : sniffed) + ")");

                if (string.IsNullOrEmpty(sniffed) || sniffed == Sniff.Junk)
                {
                    SetStatus("baixado: " + path);
                    return;
                }

                string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
                if (ext != sniffed)
                {
                    string renamed = Path.ChangeExtension(path, sniffed);
                    try
                    {
                        if (File.Exists(renamed)) renamed = UniquePath(Path.GetDirectoryName(renamed), Path.GetFileName(renamed));
                        File.Move(path, renamed);
                        path = renamed;
                    }
                    catch (Exception ex) { Log.W("não renomeei: " + ex.Message); }
                }

                if (!_settings.FormatOn(sniffed) || bytes.Length > max)
                {
                    SetStatus("baixado: " + path);
                    return;
                }

                CaptureAsset a = new CaptureAsset();
                a.key = "file:" + path;
                a.url = op.Uri ?? "";
                a.mime = "";
                a.format = sniffed;
                a.size = bytes.Length;
                a.bytes = bytes;
                a.ts = DateTime.Now;
                a.name = Path.GetFileName(path);

                tab.Assets[a.key] = a;
                SetStatus("baixado: " + a.name + " (" + SizeText(a.size) + ")");
                if (_settings.openFolderAfterSave) RevealFile(path);
                if (tab == Active) AnnounceDownload(a.key);
                RefreshHome();
                if (_settings.autoFbx) FbxAuto(a);
            }
            catch (Exception ex) { Log.W("ERRO pos-download: " + ex.Message); }
        }

        /* Chegou modelo novo: abre o painel (se estava fechado) e manda o painel piscar. */
        private void AnnounceDownload(string key)
        {
            try
            {
                bool wasHidden = _side != null && !_side.Visible;
                if (wasHidden) _side.Visible = true;
                PushAssets();

                Dictionary<string, object> flash = new Dictionary<string, object>();
                flash["type"] = "flash";
                flash["key"] = key ?? "";
                PushPanel(flash);
                Log.W("painel: anuncio de download " + (key ?? "") + (wasHidden ? " (painel estava fechado, abri)" : ""));
            }
            catch (Exception ex) { Log.W("ERRO anuncio: " + ex.Message); }
        }

        /* ---------------------------------------------------------------- */
        /* Mensagens da página                                                */
        /* ---------------------------------------------------------------- */

        private void HandlePageMessage(TabCtx tab, string source, string json)
        {
            Dictionary<string, object> d = ParseMessage(json);
            if (d == null) return;

            string type = Str(d, "type");
            if (type.Length > 0)
            {
                bool homeCommand = type == "home-req" || type == "open-external" || type == "copy-text";
                string expected = homeCommand ? Paths.UriOf(Paths.In("home.html")) : Paths.ViewerUri;
                if (!string.Equals(source, expected, StringComparison.OrdinalIgnoreCase) || tab.IsViewer == homeCommand) return;

                if (type == "home-req")
                {
                    SendHomeStats(tab);
                    return;
                }
                if (type == "open-external")
                {
                    Shell.OpenUrl(Str(d, "url"));
                    return;
                }
                if (type == "copy-text")
                {
                    try { Clipboard.SetText(Str(d, "text")); SetStatus("mensagem copiada"); }
                    catch (Exception) { SetStatus("não foi possível copiar a mensagem"); }
                    return;
                }
                if (type == "viewer-ready")
                {
                    tab.ViewerReady = true;
                    Log.W("viewer: ready");
                    SendViewerPayload(tab);
                    return;
                }
                if (type == "viewer-info")
                {
                    int m = Int(d, "meshes");
                    int tri = Int(d, "triangles");
                    int skinned = Int(d, "skinned");
                    int skinBones = Int(d, "bones");
                    Log.W("viewer: " + m + " malha(s), " + tri + " triângulos, " + Int(d, "vertices") + " vértices" +
                          (skinned > 0 ? " · já tem esqueleto (" + skinBones + " ossos, " + skinned + " malha(s) com skin)" : " · sem skin"));
                    SetStatus("visualizador: " + m + " malha(s), " + tri + " triângulos" +
                              (skinned > 0 ? " · com esqueleto" : ""));
                    if (_settings.skeleton) AutoRig(tab);   // "ver esqueleto": já cria e desenha os ossos
                    return;
                }
                if (type == "viewer-fail")
                {
                    string text = Str(d, "text");
                    Log.W("viewer falhou: " + text);
                    SetStatus(string.IsNullOrEmpty(text) ? "visualizador falhou" : text);
                    return;
                }
                if (type == "viewer-rig")
                {
                    ShowRig(tab);
                    return;
                }
                if (type == "rig-done")
                {
                    Log.W("rig: visualizador desenhou " + Str(d, "bones") + " osso(s)");
                    return;
                }
                if (type == "rig-edit")
                {
                    SaveRigEdit(tab, d);
                    return;
                }
                if (type == "rig-humanoid")
                {
                    ForceHumanoid(tab);
                    return;
                }
                if (type == "rig-reset")
                {
                    /* "rig automático": descarta o desenho salvo deste modelo e recalcula */
                    string ck = CacheKeyOf(tab);
                    if (!string.IsNullOrEmpty(ck) && _rigCache.Remove(ck))
                    {
                        SaveRigCache();
                        Log.W("rig: desenho salvo descartado do cache · " + Short(ck, 40));
                    }

                    tab.Rig = null;
                    tab.RigEdited = false;
                    AutoRig(tab);
                    return;
                }
                if (type == "viewer-fbx")
                {
                    ExportViewerFbx(tab, Int(d, "tris"), Bool(d, "zip", false));
                    return;
                }
                if (type == "viewer-save")
                {
                    if (tab.ViewerBytes == null || tab.ViewerBytes.Length == 0)
                    {
                        SetStatus("esse modelo veio da web: use o download do painel ou o Ctrl+S do site");
                        return;
                    }

                    string orig = (tab.ViewerFormat ?? "").Trim().TrimStart('.').ToLowerInvariant();
                    if (string.IsNullOrEmpty(orig)) orig = "glb";

                    string want = Str(d, "format").Trim().TrimStart('.').ToLowerInvariant();

                    bool convert = orig == "glb" || orig == "gltf";
                    byte[] outBytes;
                    string info = null;

                    try
                    {
                        if (want == "glb" && convert)
                        {
                            outBytes = GlbSkin.Apply(tab.ViewerBytes, RigForDownload(tab), out info);
                        }
                        else if (want == "fbx" && convert)
                        {
                            // qualidade: a do seletor do visualizador, se ele mandou; senão a das configurações
                            int tris = d.ContainsKey("tris") ? Int(d, "tris") : _settings.fbxTriangles;
                            outBytes = Program.FbxFromGlbStatic(tab.ViewerBytes,
                                Path.GetFileNameWithoutExtension(string.IsNullOrEmpty(tab.ViewerName) ? "modelo" : tab.ViewerName),
                                out info, RigForDownload(tab), tris);
                        }
                        else if ((want == "obj" || want == "stl" || want == "ply") && convert)
                        {
                            G3 mesh = GltfRead.Load(tab.ViewerBytes);
                            string baseName = Path.GetFileNameWithoutExtension(string.IsNullOrEmpty(tab.ViewerName) ? "modelo" : tab.ViewerName);

                            if (want == "obj") outBytes = MeshOut.Obj(mesh, baseName);
                            else if (want == "stl") outBytes = MeshOut.Stl(mesh, baseName);
                            else outBytes = MeshOut.Ply(mesh, baseName);

                            info = mesh.Triangles + " triângulos (só geometria)";
                        }
                        else
                        {
                            outBytes = tab.ViewerBytes;
                            want = orig;
                            info = "arquivo original";
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.W("download " + want + " falhou: " + ex.Message);
                        SetStatus("download " + want + ": " + ex.Message);
                        return;
                    }

                    string host = HostOf(tab.ViewerUrl ?? tab.Url);
                    string dir = _settings.DownloadDir(host);
                    try { Directory.CreateDirectory(dir); } catch (Exception) { }

                    /* nome padrão pedido para a prévia: Download Asset 1, 2, 3… */
                    string suggested = NextAssetName(dir, want) + "." + want;

                    /* sem nome de arquivo? abre "Salvar como" com o nome já preenchido:
                       dá para digitar outro nome/pasta ou só apertar Enter. */
                    string file = ChooseSavePath(suggested, want, host);
                    if (string.IsNullOrEmpty(file))
                    {
                        SetStatus("download cancelado");
                        return;
                    }

                    File.WriteAllBytes(file, outBytes);
                    Log.W("visualizador: baixado " + file + " (" + want + ") · " + info);
                    SetStatus("salvo: " + file + (info == null ? "" : " · " + info));
                    if (_settings.openFolderAfterSave) RevealFile(file);
                    RefreshHome();
                    return;
                }
            }

            if (tab.IsViewer) return;

            string t = Str(d, "t");
            string id = Str(d, "id");
            if (id.Length > 128) return;
            if (t != "bytes.chunk" && t != "reply.chunk") Log.W("page: " + t + (t == "notice" ? " " + Str(d, "message") : ""));

            if (t == "bytes.begin")
            {
                Transfer x = new Transfer();
                x.kind = "bytes";
                x.url = Str(d, "url");
                x.mime = Str(d, "mime");
                x.format = Str(d, "format");
                x.expectedSize = Int(d, "size");
                long max = (long)_settings.maxFileMB * 1024 * 1024;
                if (id.Length > 0 && x.expectedSize > 0 && x.expectedSize <= max && tab.Transfers.Count < MaxTransfersPerTab)
                    tab.Transfers[id] = x;
                return;
            }

            if (t == "bytes.chunk" || t == "reply.chunk")
            {
                Transfer x;
                if (!tab.Transfers.TryGetValue(id, out x) || t != x.kind + ".chunk") return;
                if (!AppendChunk(x, Str(d, "data"))) tab.Transfers.Remove(id);
                return;
            }

            if (t == "bytes.end")
            {
                Transfer x;
                if (!tab.Transfers.TryGetValue(id, out x) || t != x.kind + ".end") return;
                tab.Transfers.Remove(id);
                if (x.data.Length != x.expectedSize) return;
                StoreCaptured(tab, x);
                return;
            }

            if (t == "reply.begin")
            {
                Transfer x = new Transfer();
                x.kind = "reply";
                x.rid = Str(d, "rid");
                x.ok = Bool(d, "ok", true);
                x.error = Str(d, "error");
                x.name = Str(d, "name");
                x.meshes = Int(d, "meshes");
                x.triangles = Int(d, "triangles");
                x.info = Obj(d, "info");
                if (x.rid != tab.ExportRequest || tab.ExportRequest == null) return;
                x.format = tab.ExportFormat;
                x.expectedSize = Int(d, "size");
                long max = (long)_settings.maxFileMB * 1024 * 1024;
                if (id.Length > 0 && x.expectedSize > 0 && x.expectedSize <= max && tab.Transfers.Count < MaxTransfersPerTab)
                    tab.Transfers[id] = x;
                return;
            }

            if (t == "reply.end")
            {
                Transfer x;
                if (!tab.Transfers.TryGetValue(id, out x) || t != x.kind + ".end") return;
                tab.Transfers.Remove(id);
                if (x.data.Length != x.expectedSize) return;
                HandleReply(tab, x);
                return;
            }

            if (t == "reply.meta")
            {
                Transfer x = new Transfer();
                x.kind = "reply";
                x.rid = Str(d, "rid");
                x.ok = Bool(d, "ok", true);
                x.error = Str(d, "error");
                x.name = Str(d, "name");
                x.meshes = Int(d, "meshes");
                x.triangles = Int(d, "triangles");
                x.info = Obj(d, "info");
                HandleReply(tab, x);
                return;
            }

            if (t == "notice")
            {
                SetStatus(Str(d, "message"));
                return;
            }
        }

        private static bool AppendChunk(Transfer transfer, string base64)
        {
            if (string.IsNullOrEmpty(base64) || base64.Length > 512 * 1024) return false;
            try
            {
                byte[] part = Convert.FromBase64String(base64);
                if (transfer.data.Length + part.Length > transfer.expectedSize) return false;
                transfer.data.Write(part, 0, part.Length);
                return true;
            }
            catch (FormatException) { return false; }
        }

        private void StoreCaptured(TabCtx tab, Transfer x)
        {
            byte[] bytes = x.data.ToArray();
            if (bytes.Length == 0) return;

            string format = (x.format ?? "").ToLowerInvariant();
            string url = x.url ?? "";

            // O conteúdo manda: corrige formato errado (URL assinada sem extensão, .bin,
            // .gltf que na verdade é GLB...) e descarta textura/áudio/zip que não é modelo.
            string sniffed = Sniff.Identify(bytes);
            if (sniffed == Sniff.Junk)
            {
                Log.W("ignorado (não é modelo)");
                return;
            }
            if (!string.IsNullOrEmpty(sniffed) && sniffed != format)
            {
                Log.W("formato por conteúdo: " + (format.Length == 0 ? "?" : format) + " -> " + sniffed);
                format = sniffed;
            }

            // palpite de URL/MIME que o conteúdo não confirma: fora da lista
            if (string.IsNullOrEmpty(sniffed) && Sniff.IsTextual(format))
            {
                Log.W("ignorado (conteúdo não confere com " + format + ", " + bytes.Length + " B): " + url);
                return;
            }

            if (format.Length == 0) { Log.W("formato não identificado, ignorado"); return; }
            if (!_settings.FormatOn(format)) return;
            if (_settings.minFileKB > 0 && bytes.Length < (long)_settings.minFileKB * 1024) return;
            string key = "mem:" + (url.Length > 0 ? url : "anon-" + DateTime.Now.Ticks);

            CaptureAsset a = new CaptureAsset();
            a.key = key;
            a.url = url;
            a.mime = x.mime ?? "";
            a.format = format;
            a.size = bytes.Length;
            a.bytes = bytes;
            a.ts = DateTime.Now;
            a.name = NameFor(url, format);

            // nome com a extensão do formato real, senão o arquivo baixado não abre
            string ext = Path.GetExtension(a.name).TrimStart('.').ToLowerInvariant();
            if (ext != format)
            {
                string baseName = Path.GetFileNameWithoutExtension(a.name);
                if (string.IsNullOrEmpty(baseName)) baseName = "modelo";
                a.name = baseName + "." + format;
            }

            tab.Assets[key] = a;
            Log.W("capturado " + a.name + " (" + a.size + " B, " + format + ")");
            PushAssets();

            if (_settings.autoDownload) WriteAsset(a, true);
            else SetStatus("capturado: " + a.name + " (" + SizeText(a.size) + ")");

            if (_settings.autoFbx) FbxAuto(a);
        }

        private void HandleReply(TabCtx tab, Transfer x)
        {
            if (x.rid != null && x.rid.StartsWith("exp", StringComparison.Ordinal))
            {
                if (x.rid != tab.ExportRequest || tab.ExportRequest == null) return;
                tab.ExportRequest = null;
                if (!x.ok)
                {
                    Log.W("exportar: falhou " + x.error);
                    SetStatus("exportar: " + (string.IsNullOrEmpty(x.error) ? "nada para exportar" : x.error));
                    return;
                }
                byte[] bytes = x.data.ToArray();
                string fmt = string.IsNullOrEmpty(x.format) ? tab.ExportFormat : x.format;
                if (bytes.Length == 0 || Sniff.Identify(bytes) != fmt)
                {
                    SetStatus("a cena não retornou um arquivo ." + fmt + " válido");
                    return;
                }
                string baseName = string.IsNullOrEmpty(x.name) ? ("cena-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")) : x.name;
                string name = baseName + "." + fmt;

                string file;
                if (_settings.askSaveLocation)
                {
                    file = ChooseSavePath(name, fmt, HostOf(tab.Url));
                    if (string.IsNullOrEmpty(file)) { SetStatus("exportação cancelada"); return; }
                }
                else
                {
                    string dir = _settings.DownloadDir(HostOf(tab.Url));
                    Directory.CreateDirectory(dir);
                    file = UniquePath(dir, name);
                }
                File.WriteAllBytes(file, bytes);
                SetStatus("exportado: " + file + " · " + x.meshes + " malha(s), " + x.triangles + " triângulos");
                if (_settings.openFolderAfterSave) RevealFile(file);
                RefreshHome();
                return;
            }

            // resposta de scene-info
            if (x.info == null)
            {
                Log.W("resposta sem info rid=" + x.rid + " ok=" + x.ok + " err=" + x.error);
                return;
            }
            if (x.info != null)
            {
                tab.Scene = x.info;
                Log.W("cena: " + Int(x.info, "meshes") + " malha(s), " + Int(x.info, "triangles") + " tris, three=" + Str(x.info, "hasThreeGlobal") + ", canvas=" + Str(x.info, "hasCanvas"));
                PushScene();
            }
        }

        /* Página inicial: quantos arquivos já foram salvos por site (uma subpasta por host). */
        private void SendHomeStats(TabCtx tab)
        {
            if (tab == null || tab.IsViewer || tab.View == null || tab.View.CoreWebView2 == null) return;
            string url = tab.View.CoreWebView2.Source ?? "";
            if (url.IndexOf("home.html", StringComparison.OrdinalIgnoreCase) < 0) return;

            Dictionary<string, object> counts = new Dictionary<string, object>();
            int totalFiles = 0;
            int todayFiles = 0;
            long totalBytes = 0;
            try
            {
                string baseDir = _settings.DownloadDir("");
                if (Directory.Exists(baseDir))
                {
                    string[] dirs = Directory.GetDirectories(baseDir);
                    for (int i = 0; i < dirs.Length; i++)
                    {
                        string host = Path.GetFileName(dirs[i]).ToLowerInvariant();
                        int n = 0;
                        try
                        {
                            string[] files = Directory.GetFiles(dirs[i]);
                            n = files.Length;
                            for (int f = 0; f < files.Length; f++)
                            {
                                try
                                {
                                    FileInfo fi = new FileInfo(files[f]);
                                    totalBytes += fi.Length;
                                    if (fi.LastWriteTime.Date == DateTime.Today) todayFiles++;
                                }
                                catch (Exception) { }
                            }
                        }
                        catch (Exception) { }

                        if (n > 0)
                        {
                            counts[host] = n;
                            totalFiles += n;
                        }
                    }
                }
            }
            catch (Exception ex) { Log.W("ERRO home-stats: " + ex.Message); }

            Dictionary<string, object> msg = new Dictionary<string, object>();
            msg["type"] = "home-stats";
            msg["counts"] = counts;
            msg["total"] = totalFiles;
            msg["bytes"] = totalBytes;
            msg["today"] = todayFiles;
            PostToTab(tab, msg);

            StringBuilder lista = new StringBuilder();
            foreach (string k in counts.Keys)
            {
                if (lista.Length > 0) lista.Append(", ");
                lista.Append(k).Append('=').Append(counts[k]);
                if (lista.Length > 200) break;
            }
            Log.W("home: estatísticas enviadas (" + counts.Count + " site(s), " + totalFiles + " arquivo(s), " +
                todayFiles + " hoje, " + SizeText(totalBytes) + ")" + (lista.Length > 0 ? " " + lista : ""));
        }

        /* Depois de salvar/baixar algo: manda as contagens de novo para as homes abertas. */
        private void RefreshHome()
        {
            for (int i = 0; i < _tabs.Count; i++) SendHomeStats(_tabs[i]);
        }

        private void SendConfig(TabCtx tab)
        {
            if (tab.View.CoreWebView2 == null) return;
            Dictionary<string, object> cfg = new Dictionary<string, object>();
            cfg["maxCapture"] = _settings.maxFileMB * 1024 * 1024;

            Dictionary<string, object> msg = new Dictionary<string, object>();
            msg["dir"] = "ext";
            msg["cmd"] = "config";
            msg["config"] = cfg;
            SendToPage(tab, msg);
        }

        private void ScheduleScan(TabCtx tab)
        {
            if (tab.SceneTimer != null) tab.SceneTimer.Dispose();
            Timer timer = new Timer();
            timer.Interval = 3000;
            timer.Tick += delegate
            {
                timer.Stop();
                timer.Dispose();
                RequestScene(tab);
            };
            tab.SceneTimer = timer;
            timer.Start();
        }

        private void RequestScene(TabCtx tab)
        {
            Dictionary<string, object> msg = new Dictionary<string, object>();
            msg["dir"] = "ext";
            msg["cmd"] = "scene-info";
            msg["id"] = "scan" + (++_seq);
            SendToPage(tab, msg);
        }

        private void ExportScene(string format)
        {
            TabCtx tab = Active;
            if (tab == null || tab.View.CoreWebView2 == null) { SetStatus("nenhuma aba ativa"); return; }

            tab.ExportFormat = format == "obj" ? "obj" : "glb";
            Log.W("exportar: pedido " + tab.ExportFormat + " url=" + tab.Url);

            Dictionary<string, object> msg = new Dictionary<string, object>();
            msg["dir"] = "ext";
            msg["cmd"] = "export";
            tab.ExportRequest = "exp" + (++_seq);
            msg["id"] = tab.ExportRequest;
            msg["format"] = tab.ExportFormat;
            SendToPage(tab, msg);
            SetStatus("exportando a cena como ." + tab.ExportFormat + "…");
        }

        private static void SendToPage(TabCtx tab, Dictionary<string, object> msg)
        {
            if (tab.View.CoreWebView2 == null) return;
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            try { tab.View.CoreWebView2.PostWebMessageAsJson(js.Serialize(msg)); }
            catch (Exception) { }
        }

        /* ---------------------------------------------------------------- */
        /* Visualizador 3D (native/ui/viewer.html)                           */
        /* ---------------------------------------------------------------- */

        internal static readonly string[] ViewerExts =
            new string[] { ".glb", ".gltf", ".obj", ".fbx", ".stl", ".ply", ".dae", ".3ds" };

        private static bool IsModelFile(string path)
        {
            if (string.IsNullOrEmpty(path) || path.IndexOf("://", StringComparison.Ordinal) >= 0) return false;
            try
            {
                if (!File.Exists(path)) return false;
                return Array.IndexOf(ViewerExts, Path.GetExtension(path).ToLowerInvariant()) >= 0;
            }
            catch (Exception) { return false; }
        }

        private TabCtx NewViewerTab()
        {
            return CreateTab(Paths.ViewerUri);
        }

        private void LoadViewerFile(TabCtx tab, string path)
        {
            if (tab == null) return;
            try
            {
                FileInfo fi = new FileInfo(path);
                long max = (long)_settings.maxFileMB * 1024 * 1024;
                if (fi.Length > max) { SetStatus("grande demais para o visualizador (" + SizeText(fi.Length) + ")"); return; }

                tab.ViewerName = fi.Name;
                tab.ViewerFormat = fi.Extension.TrimStart('.').ToLowerInvariant();
                tab.ViewerUrl = "";
                tab.ViewerBytes = File.ReadAllBytes(path);
                tab.TabButton.Text = "👁 " + Short(fi.Name, 20);
                Log.W("visualizador: arquivo " + fi.FullName + " (" + fi.Length + " B)");
                SetStatus("carregando " + fi.Name + "…");
            }
            catch (Exception ex) { SetStatus("não foi possível abrir: " + ex.Message); }
        }

        private void OpenViewerAsset(CaptureAsset a)
        {
            if (a == null) { SetStatus("arquivo não está mais na memória"); return; }
            TabCtx tab = NewViewerTab();
            if (tab == null) return;
            tab.ViewerName = a.name;
            tab.ViewerFormat = (a.format ?? "").ToLowerInvariant();
            tab.ViewerUrl = a.url ?? "";
            tab.ViewerBytes = a.bytes;
            tab.TabButton.Text = "👁 " + Short(a.name, 20);
            Log.W("visualizador: captura " + a.name + " (" + a.size + " B)");
        }

        private void OpenViewerUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            TabCtx tab = NewViewerTab();
            if (tab == null) return;
            string name = FileNameFromUrl(url);
            string format = Path.GetExtension(name ?? "").TrimStart('.').ToLowerInvariant();
            tab.ViewerName = string.IsNullOrEmpty(name) ? "modelo" : name;
            tab.ViewerFormat = string.IsNullOrEmpty(format) ? "glb" : format;
            tab.ViewerUrl = url;
            tab.ViewerBytes = null;
            tab.TabButton.Text = "👁 " + Short(tab.ViewerName, 20);
            SetStatus("carregando modelo da web…");
        }

        private void OpenFileDialogForModel()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Abrir modelo 3D no visualizador";
                dlg.Filter = "Modelos 3D|*.glb;*.gltf;*.obj;*.fbx;*.stl;*.ply;*.dae;*.3ds|Todos os arquivos|*.*";
                dlg.CheckFileExists = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                TabCtx tab = NewViewerTab();
                LoadViewerFile(tab, dlg.FileName);
            }
        }

        private void SendViewerPayload(TabCtx tab)
        {
            if (tab == null || !tab.IsViewer || !tab.ViewerReady || tab.ViewerSent) return;
            if (tab.View == null || tab.View.CoreWebView2 == null) return;
            tab.ViewerSent = true;

            Dictionary<string, object> head = new Dictionary<string, object>();
            head["type"] = "viewer-open";
            head["name"] = tab.ViewerName;
            head["format"] = tab.ViewerFormat;
            head["url"] = tab.ViewerUrl ?? "";
            head["size"] = tab.ViewerBytes == null ? 0 : tab.ViewerBytes.Length;
            PostToTab(tab, head);

            const int Block = 288 * 1024;
            if (tab.ViewerBytes != null)
            {
                for (int off = 0; off < tab.ViewerBytes.Length; off += Block)
                {
                    int len = Math.Min(Block, tab.ViewerBytes.Length - off);
                    Dictionary<string, object> chunk = new Dictionary<string, object>();
                    chunk["type"] = "viewer-chunk";
                    chunk["data"] = Convert.ToBase64String(tab.ViewerBytes, off, len);
                    PostToTab(tab, chunk);
                }
            }

            Dictionary<string, object> end = new Dictionary<string, object>();
            end["type"] = "viewer-end";
            PostToTab(tab, end);
            Log.W("visualizador: enviados " + (tab.ViewerBytes == null ? 0 : tab.ViewerBytes.Length) + " B");
        }

        private void PostToTab(TabCtx tab, Dictionary<string, object> msg)
        {
            if (tab == null || tab.View == null || tab.View.CoreWebView2 == null) return;
            try { tab.View.CoreWebView2.PostWebMessageAsJson(_js.Serialize(msg)); }
            catch (Exception ex) { Log.W("ERRO post aba: " + ex.Message); }
        }

        private static string Short(string s, int max)
        {
            s = s ?? "";
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        /* ---------------------------------------------------------------- */
        /* Painel                                                            */
        /* ---------------------------------------------------------------- */

        private void SideMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (!string.Equals(e.Source, Paths.UriOf(Paths.In("native/ui/panel.html")), StringComparison.OrdinalIgnoreCase)) return;
            Dictionary<string, object> d = ParseMessage(e.WebMessageAsJson);
            if (d == null) return;

            string cmd = Str(d, "cmd");
            Log.W("panel: " + cmd);

            if (cmd == "hello") { PushAssets(); PushScene(); return; }
            if (cmd == "download") { WriteKey(Str(d, "key")); return; }
            if (cmd == "download-all")
            {
                object raw = d.ContainsKey("keys") ? d["keys"] : null;
                object[] arr = raw as object[];
                ArrayList al = raw as ArrayList;
                int count = arr != null ? arr.Length : (al != null ? al.Count : 0);
                for (int i = 0; i < count; i++) WriteKey(Convert.ToString(arr != null ? arr[i] : al[i]), false);
                return;
            }
            if (cmd == "convert") { ConvertKey(Str(d, "key")); return; }
            if (cmd == "fbx") { FbxFromAsset(FindAsset(Str(d, "key")), null, _settings.fbxTriangles, false); return; }
            if (cmd == "copy")
            {
                try { Clipboard.SetText(Str(d, "text")); SetStatus("link copiado"); }
                catch (Exception) { SetStatus("não foi possível copiar"); }
                return;
            }
            if (cmd == "export") { ExportScene(Str(d, "format")); return; }
            if (cmd == "scan")
            {
                TabCtx t = Active;
                if (t != null) RequestScene(t);
                return;
            }
            if (cmd == "clear")
            {
                TabCtx t = Active;
                if (t != null) t.Assets.Clear();
                PushAssets();
                SetStatus("capturas da aba limpas");
                return;
            }
            if (cmd == "open-folder")
            {
                try
                {
                    string dir = _settings.DownloadDir("");
                    Directory.CreateDirectory(dir);
                    Process.Start("explorer.exe", "\"" + dir + "\"");
                }
                catch (Exception) { }
                return;
            }
            if (cmd == "settings") { ShowSettings(); return; }
            if (cmd == "hide-panel") { _side.Visible = false; return; }
            if (cmd == "preview") { OpenViewerAsset(FindAsset(Str(d, "key"))); return; }
            if (cmd == "rig-thumb") { PushRigThumb(Str(d, "key")); return; }
            if (cmd == "rig-visible")
            {
                _settings.skeleton = Bool(d, "on", true);
                _settings.Save();
                Log.W("painel: ver esqueleto = " + (_settings.skeleton ? "ligado" : "desligado"));
                SetStatus(_settings.skeleton
                    ? "ver esqueleto ligado na prévia"
                    : "ver esqueleto desligado");
                return;
            }
            if (cmd == "preview-url") { OpenViewerUrl(Str(d, "url")); return; }
            if (cmd == "open-file") { OpenFileDialogForModel(); return; }
            if (cmd == "open-url")
            {
                string u = Str(d, "url");
                if (!string.IsNullOrEmpty(u)) CreateTab(u);
                return;
            }
        }

        /* Holograma do esqueleto na prévia do painel (SVG 2D, vista frontal). */
        private readonly Dictionary<string, string> _rigThumbs = new Dictionary<string, string>();

        private void PushRigThumb(string key)
        {
            CaptureAsset a = FindAsset(key);
            if (a == null || a.bytes == null || a.bytes.Length == 0) return;

            string fmt = (a.format ?? "").ToLowerInvariant();
            if (fmt != "glb" && fmt != "gltf") return;

            string svg;
            if (!_rigThumbs.TryGetValue(key, out svg))
            {
                try
                {
                    Program.RigData d = Program.RigOf(a.bytes);
                    svg = RigThumbSvg(d);
                    _rigThumbs[key] = svg;
                    Log.W("painel: holograma do esqueleto gerado (" + d.Bones.Count + " ossos) · " + Short(a.name, 24));
                }
                catch (Exception ex)
                {
                    Log.W("holograma falhou: " + ex.Message);
                    return;
                }
            }

            Dictionary<string, object> msg = new Dictionary<string, object>();
            msg["type"] = "rig-thumb";
            msg["key"] = key;
            msg["svg"] = svg;
            PushPanel(msg);
        }

        private static string RigThumbSvg(Program.RigData d)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < d.Bones.Count; i++)
            {
                RigBone b = d.Bones[i];
                if (b.X < minX) minX = b.X;
                if (b.X > maxX) maxX = b.X;
                if (b.Y < minY) minY = b.Y;
                if (b.Y > maxY) maxY = b.Y;
            }

            float w = Math.Max(maxX - minX, 0.001f);
            float hgt = Math.Max(maxY - minY, 0.001f);
            float scale = Math.Min(84f / w, 84f / hgt);
            float ox = 8f + (84f - w * scale) * 0.5f;

            StringBuilder sb = new StringBuilder();
            sb.Append("<svg viewBox='0 0 100 100' preserveAspectRatio='xMidYMid meet'>");

            for (int i = 0; i < d.Bones.Count; i++)
            {
                RigBone b = d.Bones[i];
                if (b.Parent < 0 || b.Parent >= d.Bones.Count) continue;
                RigBone p = d.Bones[b.Parent];

                sb.Append("<line x1='").Append(Svg(ox + (p.X - minX) * scale))
                  .Append("' y1='").Append(Svg(92f - (p.Y - minY) * scale))
                  .Append("' x2='").Append(Svg(ox + (b.X - minX) * scale))
                  .Append("' y2='").Append(Svg(92f - (b.Y - minY) * scale))
                  .Append("' stroke='#22d3ee' stroke-width='3.2' stroke-linecap='round' opacity='0.9'/>");
            }

            for (int i = 0; i < d.Bones.Count; i++)
            {
                RigBone b = d.Bones[i];
                sb.Append("<circle cx='").Append(Svg(ox + (b.X - minX) * scale))
                  .Append("' cy='").Append(Svg(92f - (b.Y - minY) * scale))
                  .Append("' r='2.8' fill='#a5f3fc'/>");
            }

            sb.Append("</svg>");
            return sb.ToString();
        }

        private static string Svg(float v)
        {
            return v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }

        private void PushPanel(Dictionary<string, object> msg)
        {
            if (!_sideReady || _sideView == null || _sideView.CoreWebView2 == null) return;
            try { _sideView.CoreWebView2.PostWebMessageAsJson(_js.Serialize(msg)); }
            catch (Exception) { }
        }

        private static readonly string[] ModelFormats =
            new string[] { "glb", "gltf", "fbx", "obj", "stl", "ply", "dae", "3ds" };

        private static bool IsModelFormat(string format)
        {
            return Array.IndexOf(ModelFormats, (format ?? "").ToLowerInvariant()) >= 0;
        }

        private void PushAssets()
        {
            TabCtx tab = Active;
            List<object> items = new List<object>();
            long total = 0;

            if (tab != null)
            {
                List<CaptureAsset> list = new List<CaptureAsset>();
                foreach (KeyValuePair<string, CaptureAsset> kv in tab.Assets) list.Add(kv.Value);

                // modelos primeiro, mais recentes antes: o que você está vendo fica no topo da lista
                list.Sort(delegate(CaptureAsset x, CaptureAsset y)
                {
                    int px = IsModelFormat(x.format) ? 0 : 1;
                    int py = IsModelFormat(y.format) ? 0 : 1;
                    if (px != py) return px - py;
                    return y.ts.CompareTo(x.ts);
                });

                for (int i = 0; i < list.Count; i++)
                {
                    CaptureAsset a = list[i];
                    total += a.size;
                    Dictionary<string, object> it = new Dictionary<string, object>();
                    it["key"] = a.key;
                    it["url"] = a.url;
                    it["name"] = a.name;
                    it["format"] = a.format;
                    it["mime"] = a.mime;
                    it["size"] = a.size;
                    it["inMemory"] = true;
                    items.Add(it);
                }
            }

            Dictionary<string, object> msg = new Dictionary<string, object>();
            msg["type"] = "assets";
            msg["items"] = items;
            msg["totalBytes"] = total;
            PushPanel(msg);
        }

        private void PushScene()
        {
            TabCtx tab = Active;
            Dictionary<string, object> msg = new Dictionary<string, object>();
            msg["type"] = "scene";
            msg["info"] = tab == null ? null : tab.Scene;
            PushPanel(msg);
        }

        /* ---------------------------------------------------------------- */
        /* Gravação de arquivos                                              */
        /* ---------------------------------------------------------------- */

        private CaptureAsset FindAsset(string key)
        {
            TabCtx tab = Active;
            if (tab == null || string.IsNullOrEmpty(key)) return null;
            CaptureAsset a;
            return tab.Assets.TryGetValue(key, out a) ? a : null;
        }

        private void WriteKey(string key) { WriteKey(key, true); }

        private void WriteKey(string key, bool saveAs)
        {
            CaptureAsset a = FindAsset(key);
            if (a == null) { SetStatus("arquivo não está mais na memória"); return; }
            WriteAsset(a, false, saveAs);
        }

        private void WriteAsset(CaptureAsset a, bool silent)
        {
            WriteAsset(a, silent, false);
        }

        /* saveAs = ação explícita do usuário (baixar 1 item / salvar do visualizador):
           respeita a opção "perguntar onde salvar e o nome" e deixa escolher pasta e nome. */
        private void WriteAsset(CaptureAsset a, bool silent, bool saveAs)
        {
            try
            {
                string file;
                if (saveAs && _settings.askSaveLocation)
                {
                    file = ChooseSavePath(a.name, a.format, HostOf(a.url));
                    if (string.IsNullOrEmpty(file)) { SetStatus("salvamento cancelado"); return; }
                }
                else
                {
                    string dir = _settings.DownloadDir(HostOf(a.url));
                    Directory.CreateDirectory(dir);
                    file = UniquePath(dir, a.name);
                }

                File.WriteAllBytes(file, a.bytes);
                Log.W("original salvo: " + file);
                SetStatus((silent ? "auto-salvo: " : "salvo: ") + file);
                if (_settings.openFolderAfterSave) RevealFile(file);
                RefreshHome();
            }
            catch (Exception ex)
            {
                SetStatus("erro ao salvar: " + ex.Message);
            }
        }

        /* Próximo nome livre "Download Asset N" na pasta, para não sobrescrever nada. */
        private static string NextAssetName(string dir, string format)
        {
            int n = 1;
            try
            {
                while (n < 10000 && File.Exists(Path.Combine(dir, "Download Asset " + n + "." + format))) n++;
            }
            catch (Exception) { }
            return "Download Asset " + n;
        }

        /* Diálogo "Salvar como": pasta e nome escolhidos na hora, extensão do formato garantida. */
        private string ChooseSavePath(string suggestedName, string format, string host)
        {
            try
            {
                string fmt = (format ?? "").Trim().TrimStart('.').ToLowerInvariant();
                if (string.IsNullOrEmpty(fmt)) fmt = "bin";

                string name = Paths.SanitizeFileName(suggestedName);
                if (string.IsNullOrEmpty(name)) name = "modelo." + fmt;
                string curExt = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
                if (curExt != fmt)
                {
                    string stem = Path.GetFileNameWithoutExtension(name);
                    if (string.IsNullOrEmpty(stem)) stem = "modelo";
                    name = stem + "." + fmt;
                }

                using (SaveFileDialog dlg = new SaveFileDialog())
                {
                    dlg.Title = "Salvar asset";
                    dlg.FileName = name;
                    dlg.DefaultExt = fmt;
                    dlg.AddExtension = true;
                    dlg.OverwritePrompt = true;
                    string what = fmt == "zip" ? "Pacote" : "Modelo 3D";
                    dlg.Filter = what + " (*." + fmt + ")|*." + fmt + "|Todos os arquivos (*.*)|*.*";
                    string dir = _settings.DownloadDir(host);
                    try { Directory.CreateDirectory(dir); dlg.InitialDirectory = dir; } catch (Exception) { }
                    if (dlg.ShowDialog(this) != DialogResult.OK) return null;
                    return dlg.FileName;
                }
            }
            catch (Exception ex) { Log.W("ERRO salvar-como: " + ex.Message); return null; }
        }

        /* ------- criador de esqueleto ------- */

        /* ---------- cache dos esqueletos desenhados à mão ---------- */

        private Dictionary<string, List<RigBone>> _rigCache = new Dictionary<string, List<RigBone>>();

        private static string RigCacheFile
        {
            get { return Path.Combine(Paths.DataDir, "rigs.json"); }
        }

        private static string CacheKeyOf(TabCtx tab)
        {
            if (tab == null) return "";
            string k = tab.ViewerUrl;
            if (string.IsNullOrEmpty(k)) k = tab.Url;
            return k ?? "";
        }

        private void LoadRigCache()
        {
            _rigCache = new Dictionary<string, List<RigBone>>();
            try
            {
                if (!File.Exists(RigCacheFile)) return;

                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = int.MaxValue;
                Dictionary<string, object> root = js.DeserializeObject(File.ReadAllText(RigCacheFile)) as Dictionary<string, object>;
                if (root == null) return;

                foreach (KeyValuePair<string, object> kv in root)
                {
                    object[] arr = kv.Value as object[];
                    if (arr == null) continue;

                    List<RigBone> list = new List<RigBone>();
                    for (int i = 0; i < arr.Length; i++)
                    {
                        Dictionary<string, object> b = GltfRead.AsDict(arr[i]);
                        if (b == null) continue;

                        RigBone bone = new RigBone();
                        bone.Name = GltfRead.ToS(GltfRead.Get(b, "Name"));
                        bone.Parent = GltfRead.ToI(GltfRead.Get(b, "Parent"), -1);
                        bone.X = GltfRead.ToF(GltfRead.Get(b, "X"));
                        bone.Y = GltfRead.ToF(GltfRead.Get(b, "Y"));
                        bone.Z = GltfRead.ToF(GltfRead.Get(b, "Z"));
                        list.Add(bone);
                    }

                    if (list.Count > 0) _rigCache[kv.Key] = list;
                }

                Log.W("cache de rigs: " + _rigCache.Count + " esqueleto(s) desenhado(s)");
            }
            catch (Exception ex) { Log.W("cache de rigs não carregou: " + ex.Message); }
        }

        private void SaveRigCache()
        {
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = int.MaxValue;
                File.WriteAllText(RigCacheFile, js.Serialize(_rigCache));
            }
            catch (Exception ex) { Log.W("cache de rigs não salvou: " + ex.Message); }
        }

        private List<RigBone> DrawnRigOf(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            List<RigBone> list;
            return _rigCache.TryGetValue(key, out list) ? list : null;
        }

        private static List<RigBone> CloneBones(List<RigBone> src)
        {
            List<RigBone> list = new List<RigBone>();
            for (int i = 0; i < src.Count; i++)
            {
                RigBone b = new RigBone();
                b.Name = src[i].Name;
                b.Parent = src[i].Parent;
                b.X = src[i].X;
                b.Y = src[i].Y;
                b.Z = src[i].Z;
                list.Add(b);
            }
            return list;
        }

        /* esqueleto que vai no download: o desenhado à mão manda */
        private List<RigBone> RigForDownload(TabCtx tab)
        {
            List<RigBone> drawn = DrawnRigOf(CacheKeyOf(tab));
            if (drawn != null && drawn.Count > 0) return CloneBones(drawn);

            if (tab.Rig != null && tab.Rig.Count > 0) return tab.Rig;
            return null;
        }

        /* Cria o rig do modelo aberto (uma vez por aba) e guarda em coordenadas centralizadas. */
        private bool EnsureRig(TabCtx tab, out string note)
        {
            note = "";
            if (tab.ViewerBytes == null || tab.ViewerBytes.Length == 0)
            {
                note = "abra um arquivo glb/gltf do disco para criar o esqueleto";
                return false;
            }

            string fmt = (tab.ViewerFormat ?? "").Trim().TrimStart('.').ToLowerInvariant();
            if (fmt != "glb" && fmt != "gltf")
            {
                note = "o esqueleto é criado em glb/gltf (este é ." + fmt + ")";
                return false;
            }

                            if (tab.Rig != null && tab.Rig.Count > 0) return true;

                /* abre com o esqueleto automático mesmo que já exista desenho em cache */

            try
            {
                Program.RigData d = Program.RigOf(tab.ViewerBytes);
                tab.Rig = d.Bones;
                tab.RigOffX = d.OffX;
                tab.RigOffY = d.OffY;
                tab.RigOffZ = d.OffZ;
                tab.RigEdited = false;
                tab.RigArmsOut = d.ArmsOut;
                tab.RigKind = d.Kind;
                tab.RigShape = d.Shape;
                return true;
            }
            catch (Exception ex)
            {
                note = "não deu para criar o esqueleto: " + ex.Message;
                return false;
            }
        }

        private List<Dictionary<string, object>> RigWire(TabCtx tab)
        {
            List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
            if (tab.Rig == null) return list;

            for (int i = 0; i < tab.Rig.Count; i++)
            {
                RigBone b = tab.Rig[i];
                Dictionary<string, object> o = new Dictionary<string, object>();
                o["name"] = b.Name;
                o["parent"] = b.Parent;
                o["x"] = b.X + tab.RigOffX;
                o["y"] = b.Y + tab.RigOffY;
                o["z"] = b.Z + tab.RigOffZ;
                list.Add(o);
            }
            return list;
        }

        /* Esqueleto sobre o modelo no visualizador (conferir/editar antes de animar). */
        private void ShowRig(TabCtx tab)
        {
            string note;
            if (!EnsureRig(tab, out note))
            {
                SendRig(tab, null, note);
                return;
            }

            List<Dictionary<string, object>> list = RigWire(tab);
            SendRig(tab, list, "esqueleto (" + tab.RigKind + "): " + list.Count + " osso(s)" +
                    (tab.RigEdited ? " (ajustado)" : "") + " · " + tab.RigShape);
            Log.W("rig: " + list.Count + " osso(s) [" + tab.RigKind + "]" +
                  (tab.RigEdited ? " (ajustado à mão)" : "") + " · " + tab.RigShape);
        }

        /* Botão "T-pose": força o esqueleto humanoide de 23 ossos. */
        private void ForceHumanoid(TabCtx tab)
        {
            if (tab == null || tab.ViewerBytes == null || tab.ViewerBytes.Length == 0)
            {
                SetStatus("abra um glb/gltf para montar o esqueleto humanoide");
                return;
            }

            try
            {
                float ox, oy, oz;
                string detail;
                List<RigBone> bones = Program.HumanoidRig(tab.ViewerBytes, out ox, out oy, out oz, out detail);

                tab.Rig = bones;
                tab.RigOffX = ox;
                tab.RigOffY = oy;
                tab.RigOffZ = oz;
                tab.RigEdited = false;
                tab.RigArmsOut = true;
                tab.RigKind = "humanoide";
                tab.RigShape = detail;

                List<Dictionary<string, object>> list = RigWire(tab);
                SendRig(tab, list, "T-pose forçada: " + list.Count + " ossos · arraste as juntas para encaixar");
                Log.W("rig: humanoide forçado (T-pose) com " + list.Count + " ossos");
            }
            catch (Exception ex)
            {
                SetStatus("T-pose falhou: " + ex.Message);
                Log.W("rig humanoide falhou: " + ex.Message);
            }
        }

        /* "ver esqueleto" marcado: deduz e desenha o esqueleto assim que o modelo abre,
           em T-pose ou não (antes só aparecia quando detectava T-pose). */
        private void AutoRig(TabCtx tab)
        {
            /* esqueleto já desenhado para este modelo: volta do cache, sem recalcular */
            List<RigBone> cached = DrawnRigOf(CacheKeyOf(tab));
            if (cached != null && cached.Count > 0 && tab.ViewerBytes != null && tab.ViewerBytes.Length > 0)
            {
                float ox, oy, oz;
                if (Program.MeshOffsets(tab.ViewerBytes, out ox, out oy, out oz))
                {
                    tab.Rig = CloneBones(cached);
                    tab.RigOffX = ox;
                    tab.RigOffY = oy;
                    tab.RigOffZ = oz;
                    tab.RigEdited = true;
                    tab.RigArmsOut = false;
                    tab.RigKind = "desenhado";
                    tab.RigShape = "esqueleto desenhado à mão (cache)";

                    List<Dictionary<string, object>> drawn = RigWire(tab);
                    SendRig(tab, drawn, "esqueleto desenhado (cache): " + drawn.Count + " osso(s) · use 'rig automático' para voltar ao T-pose");
                    Log.W("rig: esqueleto desenhado do cache (" + drawn.Count + " osso(s))");
                    return;
                }
            }

            string note;
            if (!EnsureRig(tab, out note)) return;

            List<Dictionary<string, object>> list = RigWire(tab);
            string msg = "esqueleto " + tab.RigKind + ": " + list.Count + " osso(s)" +
                         (tab.RigArmsOut ? " (T-pose)" : "") + "; arraste as juntas para ajustar";
            SendRig(tab, list, msg);
            Log.W("rig: esqueleto " + tab.RigKind + " criado automaticamente (" + list.Count + " osso(s)) · " + tab.RigShape);
        }

        /* Juntas arrastadas no visualizador viram o esqueleto oficial desta aba. */
        private void SaveRigEdit(TabCtx tab, Dictionary<string, object> d)
        {
            object[] bones = GltfRead.Arr(d, "bones");
            if (bones == null || bones.Length == 0) return;

            /* desenho feito sem abrir o rig antes: precisa do deslocamento do modelo
               para guardar o esqueleto no mesmo espaço da malha do FBX/GLB */
            if (tab.RigOffX == 0f && tab.RigOffY == 0f && tab.RigOffZ == 0f &&
                tab.ViewerBytes != null && tab.ViewerBytes.Length > 0)
            {
                float ox, oy, oz;
                if (Program.MeshOffsets(tab.ViewerBytes, out ox, out oy, out oz))
                {
                    tab.RigOffX = ox;
                    tab.RigOffY = oy;
                    tab.RigOffZ = oz;
                }
            }

            List<RigBone> list = new List<RigBone>();
            for (int i = 0; i < bones.Length; i++)
            {
                Dictionary<string, object> b = GltfRead.AsDict(bones[i]);
                if (b == null) continue;

                RigBone bone = new RigBone();
                bone.Name = GltfRead.ToS(GltfRead.Get(b, "name"));
                bone.Parent = GltfRead.ToI(GltfRead.Get(b, "parent"), -1);
                bone.X = GltfRead.ToF(GltfRead.Get(b, "x")) - tab.RigOffX;
                bone.Y = GltfRead.ToF(GltfRead.Get(b, "y")) - tab.RigOffY;
                bone.Z = GltfRead.ToF(GltfRead.Get(b, "z")) - tab.RigOffZ;
                list.Add(bone);
            }

            if (list.Count == 0) return;
            tab.Rig = list;
            tab.RigEdited = true;

            if (Bool(d, "drawn", false))
            {
                string key = CacheKeyOf(tab);
                if (!string.IsNullOrEmpty(key))
                {
                    _rigCache[key] = CloneBones(list);
                    SaveRigCache();
                    tab.RigKind = "desenhado";
                }

                Log.W("rig: esqueleto desenhado à mão com " + list.Count + " osso(s) · guardado no cache (vai para o download)");
                SetStatus("esqueleto desenhado · " + list.Count + " osso(s) · já vai no próximo download");
                return;
            }

            Log.W("rig: " + list.Count + " ossos ajustados à mão (vão para o FBX)");
            SetStatus("esqueleto ajustado · " + list.Count + " ossos · salve o FBX para gravar");
        }

        private void SendRig(TabCtx tab, List<Dictionary<string, object>> bones, string note)
        {
            Dictionary<string, object> m = new Dictionary<string, object>();
            m["type"] = "rig";
            if (bones != null) m["bones"] = bones;
            if (!string.IsNullOrEmpty(note)) m["note"] = note;
            PostToTab(tab, m);
        }

        /* Exporta o FBX do modelo aberto no visualizador, usando o esqueleto ajustado.
           maxTriangles > 0 reduz a malha; zip junta o FBX e o modelo original num pacote. */
        private void ExportViewerFbx(TabCtx tab, int maxTriangles, bool zip)
        {
            Log.W("fbx: exportar do visualizador (rig " + (tab != null && tab.RigEdited ? "ajustado" : "automático") +
                  ", " + (maxTriangles > 0 ? maxTriangles + " tris" : "malha original") + (zip ? ", zip" : "") + ")");

            if (tab == null || tab.ViewerBytes == null || tab.ViewerBytes.Length == 0)
            {
                SetStatus("esse modelo veio da web: abra o arquivo local para gerar o FBX");
                return;
            }

            CaptureAsset va = new CaptureAsset();
            va.name = string.IsNullOrEmpty(tab.ViewerName) ? "modelo." + tab.ViewerFormat : tab.ViewerName;
            va.format = (tab.ViewerFormat ?? "").Trim().TrimStart('.').ToLowerInvariant();
            va.url = tab.ViewerUrl ?? "";
            va.bytes = tab.ViewerBytes;
            va.size = tab.ViewerBytes.Length;

            FbxFromAsset(va, tab.RigEdited ? tab.Rig : null, maxTriangles, zip);
        }

        /* GLB/glTF -> FBX com esqueleto humanoide em T-pose (nomes do Mixamo).
           custom = esqueleto ajustado à mão; zip = FBX + modelo original num .zip. */
        private void FbxFromAsset(CaptureAsset a, List<RigBone> custom, int maxTriangles, bool zip)
        {
            if (a == null) { SetStatus("arquivo não está mais na memória"); return; }
            string fmt = (a.format ?? "").ToLowerInvariant();
            if (fmt != "glb" && fmt != "gltf") { SetStatus("o conversor FBX aceita glb e gltf"); return; }

            try
            {
                string stem = Path.GetFileNameWithoutExtension(a.name);
                if (string.IsNullOrEmpty(stem)) stem = "modelo";

                string info;
                byte[] fbx = Program.FbxFromGlbStatic(a.bytes, stem, out info, custom, maxTriangles);

                string ext = "fbx";
                byte[] outBytes = fbx;
                if (zip)
                {
                    ext = "zip";
                    outBytes = ZipOut.Build(
                        new List<string>() { stem + ".fbx", stem + "." + fmt },
                        new List<byte[]>() { fbx, a.bytes });
                    info += " + modelo original";
                }

                info += " · " + Math.Max(1, outBytes.Length / 1024) + " KB";

                string host = HostOf(a.url);
                string suggested = stem + "." + ext;

                string file;
                if (_settings.askSaveLocation)
                {
                    file = ChooseSavePath(suggested, ext, host);
                    if (string.IsNullOrEmpty(file)) { SetStatus("conversão cancelada"); return; }
                }
                else
                {
                    string dir = _settings.DownloadDir(host);
                    Directory.CreateDirectory(dir);
                    file = UniquePath(dir, suggested);
                }

                File.WriteAllBytes(file, outBytes);
                Log.W("fbx: " + info + " -> " + file);
                SetStatus("FBX com esqueleto: " + file + " · " + info);
                if (_settings.openFolderAfterSave) RevealFile(file);
                RefreshHome();
            }
            catch (Exception ex)
            {
                Log.W("fbx falhou: " + ex.Message);
                SetStatus("conversão FBX falhou: " + ex.Message);
            }
        }

        /* Versão silenciosa, usada quando "gerar FBX automaticamente" está ligado. */
        private void FbxAuto(CaptureAsset a)
        {
            if (a == null || a.bytes == null) return;
            string fmt = (a.format ?? "").ToLowerInvariant();
            if (fmt != "glb" && fmt != "gltf") return;

            try
            {
                string info;
                byte[] fbx = Program.FbxFromGlbStatic(a.bytes, Path.GetFileNameWithoutExtension(a.name), out info, null, _settings.fbxTriangles);
                string dir = _settings.DownloadDir(HostOf(a.url));
                Directory.CreateDirectory(dir);
                string file = UniquePath(dir, Path.GetFileNameWithoutExtension(a.name) + ".fbx");
                File.WriteAllBytes(file, fbx);
                Log.W("fbx (auto): " + info + " -> " + file);
                SetStatus("FBX automático: " + Path.GetFileName(file) + " · " + info);
                RefreshHome();
            }
            catch (Exception ex) { Log.W("fbx auto falhou: " + ex.Message); }
        }

        private void ConvertKey(string key)
        {
            CaptureAsset a = FindAsset(key);
            if (a == null) { SetStatus("arquivo não está mais na memória"); return; }
            if (a.format != "glb") { SetStatus("conversão só para GLB"); return; }

            try
            {
                string json = Glb.ToGltfJson(a.bytes);
                string name = Path.GetFileNameWithoutExtension(a.name) + ".gltf";
                string file;
                if (_settings.askSaveLocation)
                {
                    file = ChooseSavePath(name, "gltf", HostOf(a.url));
                    if (string.IsNullOrEmpty(file)) { SetStatus("salvamento cancelado"); return; }
                }
                else
                {
                    string dir = _settings.DownloadDir(HostOf(a.url));
                    Directory.CreateDirectory(dir);
                    file = UniquePath(dir, name);
                }
                File.WriteAllText(file, json, new UTF8Encoding(false));
                SetStatus("glTF gerado: " + file);
                if (_settings.openFolderAfterSave) RevealFile(file);
                RefreshHome();
            }
            catch (Exception ex)
            {
                SetStatus("conversão falhou: " + ex.Message);
            }
        }

        /* ---------------------------------------------------------------- */
        /* Configurações                                                     */
        /* ---------------------------------------------------------------- */

        private void ShowSettings()
        {
            using (SettingsForm form = new SettingsForm(_settings))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;

                _settings = form.Settings;
                _settings.Normalize();
                _settings.Save();

                TabCtx t = Active;
                if (t != null) SendConfig(t);
                SetStatus("configurações salvas");
            }
        }

        /* ---------------------------------------------------------------- */
        /* Utilidades                                                        */
        /* ---------------------------------------------------------------- */

        /* A página envia JSON.stringify(...), então o host recebe uma string JSON
           embrulhada em JSON: aqui desembrulhamos os dois casos. */
        private Dictionary<string, object> ParseMessage(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 1024 * 1024) return null;
            try
            {
                object value = _js.DeserializeObject(json);
                string inner = value as string;
                if (inner != null) value = _js.DeserializeObject(inner);
                return value as Dictionary<string, object>;
            }
            catch (Exception ex)
            {
                Log.W("ERRO parse: " + ex.Message);
                return null;
            }
        }

        private static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return "";
            return Convert.ToString(v);
        }

        private static int Int(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return 0;
            try { return Convert.ToInt32(v); } catch (Exception) { return 0; }
        }

        private static bool Bool(Dictionary<string, object> d, string key, bool fallback)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return fallback;
            try { return Convert.ToBoolean(v); } catch (Exception) { return fallback; }
        }

        private static Dictionary<string, object> Obj(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v)) return null;
            return v as Dictionary<string, object>;
        }

        private static string HostOf(string url)
        {
            try
            {
                Uri u = new Uri(url);
                string h = u.Host;
                if (h.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) h = h.Substring(4);
                return Paths.SanitizeFileName(h);
            }
            catch (Exception) { return "local"; }
        }

        private static string FileNameFromUrl(string url)
        {
            try
            {
                Uri u = new Uri(url);
                string name = Uri.UnescapeDataString(Path.GetFileName(u.AbsolutePath));
                return Paths.SanitizeFileName(name);
            }
            catch (Exception) { return ""; }
        }

        private static string NameFor(string url, string format)
        {
            string name = FileNameFromUrl(url);
            string ext = format;
            if (string.IsNullOrEmpty(name) || name == "_")
            {
                string host = HostOf(url);
                name = host + "_" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            }

            string current = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
            if (current != ext)
            {
                string stem = Path.GetFileNameWithoutExtension(name);
                if (string.IsNullOrEmpty(stem)) stem = "modelo";
                name = stem + "." + ext;
            }
            return name;
        }

        private static string UniquePath(string dir, string name)
        {
            string safe = Paths.SanitizeFileName(name);
            if (string.IsNullOrEmpty(safe)) safe = "arquivo";
            string full = Path.Combine(dir, safe);
            if (!File.Exists(full) && !Directory.Exists(full)) return full;

            string stem = Path.GetFileNameWithoutExtension(safe);
            string ext = Path.GetExtension(safe);
            for (int i = 2; i < 1000; i++)
            {
                string candidate = Path.Combine(dir, stem + "-" + i + ext);
                if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
            }
            return Path.Combine(dir, stem + "-" + Guid.NewGuid().ToString("N") + ext);
        }

        private static string SizeText(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1048576) return (bytes / 1024.0).ToString("0.0") + " KB";
            return (bytes / 1048576.0).ToString("0.0") + " MB";
        }
    }

    /* ------------------------------------------------------------------ */
    /* Tela de configurações                                              */
    /* ------------------------------------------------------------------ */

    internal class SettingsForm : Form
    {
        public AppSettings Settings;

        private TextBox _root;
        private TextBox _folder;
        private readonly List<CheckBox> _formats = new List<CheckBox>();
        private CheckBox _auto, _askSave, _autoFbx, _openFolder;
        private NumericUpDown _maxFile, _minFile;
        private ComboBox _fbxTris;

        /* teto de triângulos do FBX: 0 mantém a malha original */
        private static readonly int[] FbxTriangles = new int[] { 0, 120000, 60000, 20000, 8000 };
        private static readonly string[] FbxTriangleLabels = new string[]
        {
            "original (mais pesado)", "120.000 triângulos", "60.000 triângulos",
            "20.000 triângulos (Mixamo)", "8.000 triângulos (bem leve)"
        };

        private void AddLabel(string text, int x, int y, int w, Color color)
        {
            Label l = new Label();
            l.Text = text;
            l.SetBounds(x, y, w, 20);
            l.ForeColor = color;
            Controls.Add(l);
        }

        private TextBox DarkBox(int x, int y, int w, string text)
        {
            TextBox t = new TextBox();
            t.SetBounds(x, y, w, 24);
            t.BackColor = Color.FromArgb(9, 9, 16);
            t.ForeColor = Color.White;
            t.BorderStyle = BorderStyle.FixedSingle;
            t.Text = text ?? "";
            return t;
        }

        private NumericUpDown DarkNum(int x, int y, int min, int max, int value)
        {
            NumericUpDown n = new NumericUpDown();
            n.SetBounds(x, y, 80, 24);
            n.Minimum = min;
            n.Maximum = max;
            n.Value = Math.Min(Math.Max(value, min), max);
            n.BackColor = Color.FromArgb(9, 9, 16);
            n.ForeColor = Color.White;
            n.BorderStyle = BorderStyle.FixedSingle;
            return n;
        }

        private ComboBox DarkCombo(int x, int y, int w)
        {
            ComboBox c = new ComboBox();
            c.SetBounds(x, y, w, 24);
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.FlatStyle = FlatStyle.Flat;
            c.BackColor = Color.FromArgb(9, 9, 16);
            c.ForeColor = Color.White;
            return c;
        }

        private CheckBox DarkCheck(string text, int y, bool on)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.SetBounds(18, y, 450, 22);
            c.ForeColor = Color.White;
            c.Checked = on;
            return c;
        }

        public SettingsForm(AppSettings current)
        {
            Settings = current;

            Text = "Configurações — 3D Browser Pro";
            BackColor = Color.FromArgb(14, 14, 22);
            ForeColor = Color.FromArgb(238, 242, 255);
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(494, 566);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            int y = 16;

            AddLabel("Pasta base dos downloads", 18, y, 430, Color.FromArgb(148, 163, 184)); y += 24;

            _root = DarkBox(18, y, 360, current.downloadRoot);
            Controls.Add(_root);

            Button browse = new Button();
            browse.Text = "Procurar…";
            browse.SetBounds(386, y, 90, 24);
            browse.FlatStyle = FlatStyle.Flat;
            browse.BackColor = Color.FromArgb(30, 30, 44);
            browse.ForeColor = Color.White;
            browse.FlatAppearance.BorderSize = 0;
            browse.Click += delegate
            {
                using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                {
                    fbd.Description = "Pasta base das capturas e downloads";
                    if (Directory.Exists(_root.Text)) fbd.SelectedPath = _root.Text;
                    if (fbd.ShowDialog(this) == DialogResult.OK) _root.Text = fbd.SelectedPath;
                }
            };
            Controls.Add(browse);
            y += 30;

            AddLabel("Subpasta (opcional, dentro da pasta base)", 18, y, 430, Color.FromArgb(148, 163, 184)); y += 24;

            _folder = DarkBox(18, y, 458, current.folder);
            Controls.Add(_folder);
            y += 32;

            AddLabel("Formatos capturados", 18, y, 430, Color.FromArgb(148, 163, 184)); y += 24;

            int col = 0;
            for (int i = 0; i < AppSettings.AllFormats.Length; i++)
            {
                string f = AppSettings.AllFormats[i];
                CheckBox cb = new CheckBox();
                cb.Text = f.ToUpperInvariant();
                cb.SetBounds(18 + (col % 5) * 88, y, 86, 22);
                cb.ForeColor = Color.White;
                cb.Checked = current.FormatOn(f);
                Controls.Add(cb);
                _formats.Add(cb);
                col++;
                if (col % 5 == 0) y += 24;
            }
            y += 34;

            _auto = DarkCheck("Baixar automaticamente ao capturar", y, current.autoDownload);
            Controls.Add(_auto); y += 26;

            _askSave = DarkCheck("Perguntar local e nome ao salvar (Salvar como)", y, current.askSaveLocation);
            Controls.Add(_askSave); y += 26;

            _autoFbx = DarkCheck("Gerar FBX com esqueleto automaticamente (Mixamo)", y, current.autoFbx);
            Controls.Add(_autoFbx); y += 30;

            AddLabel("Qualidade do FBX com esqueleto", 18, y + 3, 260, Color.FromArgb(148, 163, 184));
            _fbxTris = DarkCombo(300, y, 176);
            _fbxTris.Items.AddRange(FbxTriangleLabels);
            int q = Array.IndexOf(FbxTriangles, current.fbxTriangles);
            _fbxTris.SelectedIndex = q < 0 ? 0 : q;
            Controls.Add(_fbxTris);
            y += 32;

            AddLabel("Ignorar capturas menores que (KB)", 18, y + 3, 260, Color.FromArgb(148, 163, 184));
            _minFile = DarkNum(300, y, 0, 102400, current.minFileKB);
            Controls.Add(_minFile);
            y += 32;

            AddLabel("Tamanho máximo por arquivo (MB)", 18, y + 3, 260, Color.FromArgb(148, 163, 184));
            _maxFile = DarkNum(300, y, 8, 1024, current.maxFileMB);
            Controls.Add(_maxFile);
            y += 34;

            _openFolder = DarkCheck("Abrir a pasta depois de salvar", y, current.openFolderAfterSave);
            Controls.Add(_openFolder); y += 26;

            AddLabel("Abertura: sempre na home, com todas as IAs disponíveis.", 18, y + 2, 440, Color.FromArgb(110, 118, 145));
            y += 30;

            Label hint = new Label();
            hint.Text = "Destino: " + (string.IsNullOrEmpty(current.downloadRoot) ? Paths.DownloadsRoot() : current.downloadRoot) +
                (string.IsNullOrEmpty(current.folder) ? "" : "\\" + current.folder) + "\\<site>\\";
            hint.SetBounds(18, y, 440, 40);
            hint.ForeColor = Color.FromArgb(110, 118, 145);
            Controls.Add(hint);

            Button ok = new Button();
            ok.Text = "Salvar";
            ok.SetBounds(280, 492, 92, 30);
            ok.DialogResult = DialogResult.OK;
            ok.FlatStyle = FlatStyle.Flat;
            ok.BackColor = Color.FromArgb(124, 58, 237);
            ok.ForeColor = Color.White;
            ok.FlatAppearance.BorderSize = 0;
            ok.Click += delegate { Collect(); };
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "Cancelar";
            cancel.SetBounds(378, 492, 92, 30);
            cancel.DialogResult = DialogResult.Cancel;
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.BackColor = Color.FromArgb(30, 30, 44);
            cancel.ForeColor = Color.White;
            cancel.FlatAppearance.BorderSize = 0;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void Collect()
        {
            Settings.downloadRoot = _root.Text.Trim();
            Settings.folder = _folder.Text.Trim();

            List<string> on = new List<string>();
            for (int i = 0; i < _formats.Count; i++)
            {
                if (_formats[i].Checked) on.Add(AppSettings.AllFormats[i]);
            }
            Settings.enabledFormats = on;
            Settings.autoDownload = _auto.Checked;
            Settings.askSaveLocation = _askSave.Checked;
            Settings.autoFbx = _autoFbx.Checked;
            Settings.fbxTriangles = FbxTriangles[Math.Max(0, _fbxTris.SelectedIndex)];
            Settings.openFolderAfterSave = _openFolder.Checked;
            Settings.minFileKB = (int)_minFile.Value;
            Settings.maxFileMB = (int)_maxFile.Value;
        }
    }
}
