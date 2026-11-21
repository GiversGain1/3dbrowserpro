using System;
using System.IO;

namespace BP3D
{
    /* Identificação do formato pelos primeiros bytes.
       Sites de IA 3D (Tripo, Meshy, Sloyd, Rodin...) servem o modelo por URLs assinadas,
       sem extensão ou com Content-Type errado — a extensão da URL mente com frequência.
       Aqui o conteúdo decide, e o que não é modelo (textura, áudio, zip, vídeo) é descartado. */
    internal static class Sniff
    {
        public const string Junk = "-";

        public static string Identify(byte[] b)
        {
            if (b == null || b.Length < 8) return null;

            /* mídia que nunca é modelo (o painel não deve listar textura como se fosse malha) */
            if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return Junk;             // PNG
            if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return Junk;                             // JPEG
            if (b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46) return Junk;                             // GIF
            if (b[0] == 0x44 && b[1] == 0x44 && b[2] == 0x53 && b[3] == 0x20) return Junk;             // DDS
            if (b[0] == 0xAB && b[1] == 0x4B && b[2] == 0x54 && b[3] == 0x58) return Junk;             // KTX
            if (b[0] == 0x1F && b[1] == 0x8B) return Junk;                                             // gzip
            if (b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46) return Junk;             // PDF
            if (b[0] == 0x42 && b[1] == 0x4D) return Junk;                                             // BMP
            if (b[0] == 0x00 && b[1] == 0x61 && b[2] == 0x73 && b[3] == 0x6D) return Junk;             // wasm
            if (b[0] == 0x49 && b[1] == 0x44 && b[2] == 0x33) return Junk;                             // MP3
            if (b[4] == 'f' && b[5] == 't' && b[6] == 'y' && b[7] == 'p') return Junk;                 // mp4/mov

            string head = Text(b, 2048);
            if (head.StartsWith("#?RADIANCE") || head.StartsWith("#?RGBE")) return Junk;                // HDR
            if (head.StartsWith("OggS") || head.StartsWith("fLaC")) return Junk;
            if (head.StartsWith("RIFF") && head.IndexOf("WEBP") == 4) return Junk;

            /* modelos */
            if (b[0] == 0x67 && b[1] == 0x6C && b[2] == 0x54 && b[3] == 0x46) return "glb";           // 'glTF'
            if (head.StartsWith("Kaydara FBX Binary")) return "fbx";
            if (head.StartsWith("; FBX") || (head.StartsWith("FBX") && head.IndexOf("Model:") > 0)) return "fbx";
            if (head.Length > 4 && (head.StartsWith("ply\r") || head.StartsWith("ply\n") || head.StartsWith("ply "))) return "ply";
            if (b[0] == 0x4D && b[1] == 0x4D) return "3ds";                                            // chunk 0x4D4D
            if (b[0] == 0x50 && b[1] == 0x4B) return HasUsd(head) ? "usdz" : Junk;                     // zip (usdz ou lixo)

            string s = head.TrimStart(' ', '\r', '\n', '\t', '\uFEFF');
            if (s.Length > 0 && s[0] == '{')
            {
                // JSON só é glTF se tiver asset.version + corpo de cena. Sem isso, JSON de
                // telemetria/config ("config", pixels, challenge) entrava na lista como glTF.
                bool asset = s.IndexOf("\"asset\"") >= 0;
                bool version = s.IndexOf("\"version\"") >= 0;
                bool body = s.IndexOf("\"meshes\"") >= 0 || s.IndexOf("\"nodes\"") >= 0
                    || s.IndexOf("\"accessors\"") >= 0 || s.IndexOf("\"buffers\"") >= 0
                    || s.IndexOf("\"scenes\"") >= 0;
                return (asset && version && body) ? "gltf" : null;
            }

            if (s.Length > 0 && s[0] == '<') return head.IndexOf("COLLADA") >= 0 ? "dae" : null;
            if (s.StartsWith("solid") && (head.IndexOf("facet") > 0 || head.IndexOf("vertex") > 0)) return "stl";
            if (LooksObj(head)) return "obj";
            if (LooksStlBinary(b)) return "stl";
            return null;
        }

        /* formatos cujo conteúdo dá para reconhecer lendo o texto: se o sniff não confirma,
           o palpite da URL/MIME estava errado e o arquivo não deve entrar na lista. */
        public static bool IsTextual(string format)
        {
            string f = (format ?? "").ToLowerInvariant();
            return f == "gltf" || f == "obj" || f == "dae" || f == "ply" || f == "stl";
        }

        private static bool HasUsd(string head)
        {
            return head.IndexOf("usdc") >= 0 || head.IndexOf("usda") >= 0 || head.IndexOf("usdz") >= 0;
        }

        private static bool LooksObj(string head)
        {
            string[] lines = head.Split('\n');
            int score = 0;
            int limit = Math.Min(lines.Length, 240);
            for (int i = 0; i < limit; i++)
            {
                string l = lines[i].Trim();
                if (l.Length < 2) continue;
                if (l[0] == 'v' && l[1] == ' ') score++;
                else if (l[0] == 'f' && l[1] == ' ') score += 2;
                else if (l.StartsWith("vt ") || l.StartsWith("vn ")) score++;
                else if (l.StartsWith("mtllib ") || l.StartsWith("usemtl ") || l.StartsWith("g ") || l.StartsWith("o ")) score++;
            }
            return score >= 3;
        }

        /* STL binário: 80 bytes de cabeçalho + contagem de triângulos + 50 bytes cada */
        private static bool LooksStlBinary(byte[] b)
        {
            if (b.Length < 84) return false;
            uint n = BitConverter.ToUInt32(b, 80);
            if (n == 0) return false;
            return 84L + (long)n * 50L == b.Length;
        }

        /* primeiros bytes como texto, trocando binário por espaço para não poluir as buscas */
        private static string Text(byte[] b, int max)
        {
            int n = Math.Min(b.Length, max);
            char[] c = new char[n];
            for (int i = 0; i < n; i++)
            {
                byte v = b[i];
                if (v == 10 || v == 13 || v == 9) c[i] = (char)v;
                else if (v < 32 || v > 126) c[i] = ' ';
                else c[i] = (char)v;
            }
            return new string(c);
        }
    }
}
