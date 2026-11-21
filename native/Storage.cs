using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace BP3D
{
    internal static class Paths
    {
        public static readonly string AppDir =
            AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        public static readonly string DataDir = ResolveDataDir();

        private static string ResolveDataDir()
        {
            string profile = Environment.GetEnvironmentVariable("BP3D_DATA_DIR");
            return string.IsNullOrWhiteSpace(profile)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "3D Browser Pro")
                : Path.GetFullPath(profile);
        }

        public static readonly string ProfileDir = Path.Combine(DataDir, "perfil");
        public static readonly string SettingsFile = Path.Combine(DataDir, "settings.json");

        public const string ViewerHost = "3d-browser-viewer.invalid";
        public const string ViewerUri = "https://" + ViewerHost + "/viewer.html";

        public static string In(string relative) { return Path.Combine(AppDir, relative); }

        public static string UriOf(string path) { return new Uri(path).AbsoluteUri; }

        public static string DownloadsRoot()
        {
            string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            return Directory.Exists(p) ? p : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            char[] bad = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                sb.Append(Array.IndexOf(bad, c) >= 0 ? '_' : c);
            }
            string safe = sb.ToString().Trim().TrimEnd(' ', '.');
            if (safe.Length > 120)
            {
                string extension = Path.GetExtension(safe);
                if (extension.Length > 12) extension = "";
                safe = safe.Substring(0, 120 - extension.Length).TrimEnd(' ', '.') + extension;
            }

            string device = safe.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
            if (device == "CON" || device == "PRN" || device == "AUX" || device == "NUL" ||
                device == "CONIN$" || device == "CONOUT$" ||
                (device.Length == 4 && (device.StartsWith("COM") || device.StartsWith("LPT")) &&
                 "0123456789¹²³".IndexOf(device[3]) >= 0))
                safe = "_" + safe;
            return safe;
        }

        public static void EnsureData()
        {
            if (!Directory.Exists(DataDir)) Directory.CreateDirectory(DataDir);
            if (!Directory.Exists(ProfileDir)) Directory.CreateDirectory(ProfileDir);
        }
    }

    internal static class Log
    {
        private static readonly object Gate = new object();
        private static readonly string FilePath = Path.Combine(Paths.DataDir, "log.txt");

        public static void W(string message)
        {
            try
            {
                lock (Gate)
                {
                    if (System.IO.File.Exists(FilePath) && new FileInfo(FilePath).Length > 512 * 1024) System.IO.File.Delete(FilePath);
                    System.IO.File.AppendAllText(FilePath, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + "\r\n");
                }
            }
            catch (Exception) { }
        }
    }

    internal class AppSettings
    {
        public string folder = "3D Browser Pro";
        public List<string> enabledFormats = new List<string> { "glb", "gltf", "obj", "fbx", "stl", "ply" };
        public bool autoDownload = false;
        public int maxFileMB = 96;
        public string homePage = "";

        public string downloadRoot = "";      // vazio = Downloads\3D Browser Pro
        public int minFileKB = 0;              // ignora capturas menores que isso
        public bool openFolderAfterSave = false;
        public bool askSaveLocation = false;   // perguntar onde salvar e nome ao baixar (Salvar como)
        public bool autoFbx = false;           // gerar FBX com esqueleto automaticamente ao capturar glb/gltf
        public int fbxTriangles = 0;           // teto de triângulos do FBX com esqueleto (0 = malha original)
        public bool skeleton = true;           // exibe os ossos na prévia

        public static readonly string[] AllFormats =
            new string[] { "glb", "gltf", "obj", "fbx", "stl", "ply", "dae", "3ds", "usdz" };

        private static readonly JavaScriptSerializer Js = new JavaScriptSerializer();
        static AppSettings() { Js.MaxJsonLength = int.MaxValue; }

        public static AppSettings Load()
        {
            AppSettings s = null;
            try
            {
                if (File.Exists(Paths.SettingsFile))
                {
                    s = Js.Deserialize<AppSettings>(File.ReadAllText(Paths.SettingsFile));
                }
            }
            catch (Exception) { s = null; }

            if (s == null) s = new AppSettings();
            s.Normalize();
            return s;
        }

        public void Normalize()
        {
            if (string.IsNullOrEmpty(folder)) folder = "";
            folder = Paths.SanitizeFileName(folder);
            if (folder.Length > 60) folder = folder.Substring(0, 60).TrimEnd(' ', '.');

            if (enabledFormats == null) enabledFormats = new List<string>();
            List<string> clean = new List<string>();
            for (int i = 0; i < enabledFormats.Count; i++)
            {
                string f = (enabledFormats[i] ?? "").Trim().ToLowerInvariant();
                if (Array.IndexOf(AllFormats, f) >= 0 && !clean.Contains(f)) clean.Add(f);
            }
            if (clean.Count == 0) clean.AddRange(new string[] { "glb", "gltf", "obj", "fbx" });
            enabledFormats = clean;

            if (maxFileMB < 8) maxFileMB = 8;
            if (maxFileMB > 1024) maxFileMB = 1024;
            if (fbxTriangles < 0) fbxTriangles = 0;
            if (fbxTriangles > 2000000) fbxTriangles = 2000000;
            if (homePage == null) homePage = "";

            downloadRoot = (downloadRoot ?? "").Trim();
            if (!Path.IsPathRooted(downloadRoot)) downloadRoot = "";
            if (minFileKB < 0) minFileKB = 0;
            if (minFileKB > 102400) minFileKB = 102400;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Paths.DataDir);
                File.WriteAllText(Paths.SettingsFile, Js.Serialize(this));
            }
            catch (Exception) { }
        }

        public bool FormatOn(string format)
        {
            return enabledFormats != null && format != null && enabledFormats.Contains(format);
        }

        public string DownloadDir(string host)
        {
            string baseDir = string.IsNullOrEmpty(downloadRoot) ? Paths.DownloadsRoot() : downloadRoot;
            if (!string.IsNullOrEmpty(folder)) baseDir = Path.Combine(baseDir, folder);
            if (!string.IsNullOrEmpty(host)) baseDir = Path.Combine(baseDir, host);
            return baseDir;
        }
    }

}
