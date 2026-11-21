// 3D Browser Pro — barreiras anti-engenharia-reversa.
// Ideia: nada aqui "tranca" um binário .NET (isso é impossível), mas cada camada encarece
// a decompilação/patche: depurador, ferramentas de decompilação e arquivos alterados.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace BP3D
{
    internal static class Shield
    {
        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        private static extern bool CheckRemoteDebuggerPresent(IntPtr process, ref bool present);

        /* nomes das ferramentas ficam como códigos embaralhados (XOR 0x5A): procurar
           "dnspy" no binário com strings/hex não acha nada. */
        private static string X(params byte[] codes)
        {
            char[] c = new char[codes.Length];
            for (int i = 0; i < codes.Length; i++) c[i] = (char)(codes[i] ^ 0x5A);
            return new string(c);
        }

        private static readonly string[] Tools = new string[]
        {
            X(0x3E,0x34,0x29,0x2A,0x23),
            X(0x3E,0x34,0x29,0x2A,0x23,0x77,0x22,0x62,0x6C),
            X(0x3E,0x34,0x29,0x2A,0x23,0x74,0x39,0x35,0x34,0x29,0x35,0x36,0x3F),
            X(0x33,0x36,0x29,0x2A,0x23),
            X(0x33,0x36,0x29,0x2A,0x23,0x39,0x37,0x3E),
            X(0x3E,0x35,0x2E,0x2A,0x3F,0x3F,0x31),
            X(0x30,0x2F,0x29,0x2E,0x3E,0x3F,0x39,0x35,0x37,0x2A,0x33,0x36,0x3F),
            X(0x28,0x3F,0x3C,0x36,0x3F,0x39,0x2E,0x35,0x28),
            X(0x3E,0x3F,0x6E,0x3E,0x35,0x2E),
            X(0x3D,0x32,0x33,0x3E,0x28,0x3B),
            X(0x33,0x3E,0x3B),
            X(0x33,0x3E,0x3B,0x6C,0x6E),
            X(0x33,0x3E,0x3B,0x2B),
            X(0x33,0x3E,0x3B,0x2B,0x6C,0x6E),
            X(0x22,0x6C,0x6E,0x3E,0x38,0x3D),
            X(0x22,0x69,0x68,0x3E,0x38,0x3D),
            X(0x35,0x36,0x36,0x23,0x3E,0x38,0x3D),
            X(0x39,0x32,0x3F,0x3B,0x2E,0x3F,0x34,0x3D,0x33,0x34,0x3F),
            X(0x39,0x32,0x3F,0x3B,0x2E,0x3F,0x34,0x3D,0x33,0x34,0x3F,0x77,0x22,0x62,0x6C,0x05,0x6C,0x6E),
            X(0x37,0x3F,0x3D,0x3B,0x3E,0x2F,0x37,0x2A,0x3F,0x28),
            X(0x29,0x39,0x23,0x36,0x36,0x3B),
            X(0x2A,0x3F,0x77,0x38,0x3F,0x3B,0x28),
            X(0x32,0x22,0x3E),
            X(0x2A,0x28,0x35,0x39,0x3F,0x29,0x29,0x32,0x3B,0x39,0x31,0x3F,0x28),
        };

        private static bool _alarm;

        /* ---------------------------------------------------------------- */
        /* Camadas                                                            */
        /* ---------------------------------------------------------------- */

        /* depurador anexado (o passo 1 de quem quer quebrar o binário) */
        private static string DebuggerAlarm()
        {
            bool remote = false;
            try { CheckRemoteDebuggerPresent(Process.GetCurrentProcess().Handle, ref remote); }
            catch (Exception) { }

            bool attached = false;
            try { attached = Debugger.IsAttached || Debugger.IsLogging() || remote; }
            catch (Exception) { attached = remote; }

            return attached ? "Há um depurador gerenciado/nativo anexado a este processo." : null;
        }

        /* ferramentas de decompilação, despejo de IL e instrumentação abertas */
        private static string ToolAlarm()
        {
            List<string> found = new List<string>();
            Process[] all;
            try { all = Process.GetProcesses(); }
            catch (Exception) { return null; }

            for (int i = 0; i < all.Length; i++)
            {
                string name;
                try { name = (all[i].ProcessName ?? "").ToLowerInvariant(); }
                catch (Exception) { continue; }
                if (name.Length == 0) continue;

                for (int t = 0; t < Tools.Length; t++)
                {
                    if (name == Tools[t] || name.StartsWith(Tools[t] + "-", StringComparison.Ordinal))
                    {
                        if (!found.Contains(name)) found.Add(name);
                        break;
                    }
                }
            }

            return found.Count == 0
                ? null
                : "Ferramenta de engenharia reversa aberta: " + string.Join(", ", found.ToArray()) + ".";
        }

        /* os arquivos que o app carrega do disco (html/js/css/ícone) batem com os do build?
           o SHA-256 esperado é gerado por tools/seal.ps1 e compilado dentro do exe. */
        private static string TamperAlarm()
        {
            try
            {
                string all = SealData.Files ?? "";
                string[] items = all.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (items.Length == 0) return "Assinatura interna ausente — o build está incompleto.";

                for (int i = 0; i < items.Length; i++)
                {
                    int eq = items[i].IndexOf('=');
                    if (eq <= 0) continue;

                    string rel = items[i].Substring(0, eq);
                    string want = items[i].Substring(eq + 1).Trim().ToLowerInvariant();
                    string path = Path.Combine(Paths.AppDir,
                        rel.Replace('/', Path.DirectorySeparatorChar));

                    if (!File.Exists(path))
                        return "Arquivo do app removido: " + rel + ".";

                    string got;
                    using (SHA256 sha = SHA256.Create())
                    using (FileStream fs = File.OpenRead(path))
                    {
                        byte[] h = sha.ComputeHash(fs);
                        StringBuilder sb = new StringBuilder(h.Length * 2);
                        for (int b = 0; b < h.Length; b++) sb.Append(h[b].ToString("x2"));
                        got = sb.ToString();
                    }

                    if (got != want)
                        return "Arquivo do app modificado: " + rel + ".";
                }
            }
            catch (Exception ex) { return "Falha ao conferir os arquivos do app: " + ex.Message; }

            return null;
        }

        /* ---------------------------------------------------------------- */
        /* API                                                                */
        /* ---------------------------------------------------------------- */

        /* null = liberado; texto = motivo do bloqueio */
        internal static string Why()
        {
            string why = DebuggerAlarm();
            if (why != null) return why;
            why = ToolAlarm();
            if (why != null) return why;
            return TamperAlarm();
        }

        internal static bool Guard()
        {
            string why = Why();
            if (why == null) { Log.W("shield: ok"); return true; }

            Log.W("shield: bloqueado — " + why);
            Mensagem(why, false);
            return false;
        }

        /* rechecagem enquanto o app roda: anexar depurador depois de abrir não adianta */
        internal static void Watch(Form owner)
        {
            Timer t = new Timer();
            t.Interval = 15000;
            t.Tick += delegate
            {
                if (_alarm) return;
                string why = DebuggerAlarm() ?? ToolAlarm();
                if (why == null) return;

                _alarm = true;
                t.Stop();
                Log.W("shield: fechando — " + why);
                Mensagem(why, true);
                try { Environment.Exit(4); }
                catch (Exception) { Application.Exit(); }
            };
            t.Start();
        }

        private static void Mensagem(string why, bool fechando)
        {
            try
            {
                MessageBox.Show(
                    (fechando ? "Esta sessão será fechada.\n\n" : "") +
                    why + "\n\n" +
                    "O 3D Browser Pro não roda com depurador, decompilador ou despejador de IL ativos, " +
                    "e não roda com os arquivos do programa alterados.\n\n" +
                    "Feche o programa citado (ou pare a depuração) e abra o 3D Browser Pro de novo.\n" +
                    "Quer ler o código? O fonte está em 3d-browser-pro-codigo-aberto.zip — estude, " +
                    "aprenda e contribua em vez de quebrar.",
                    "3D Browser Pro — proteção",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch (Exception) { }
        }
    }
}
