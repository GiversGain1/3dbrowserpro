using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BP3D
{
    /* Escritas simples a partir da malha triangulada (sem material): OBJ, STL e PLY.
       São os formatos "só geometria" do baixar do visualizador. */
    internal static class MeshOut
    {
        public static byte[] Obj(G3 mesh, string name)
        {
            StringBuilder sb = new StringBuilder(1024 + mesh.P.Count * 16);
            string obj = string.IsNullOrEmpty(name) ? "modelo" : name.Replace(" ", "_");

            sb.Append("# 3D Browser Pro — ").Append(obj).Append('\n');
            sb.Append("o ").Append(obj).Append('\n');

            for (int i = 0; i + 2 < mesh.P.Count; i += 3)
            {
                sb.Append("v ").Append(N(mesh.P[i])).Append(' ').Append(N(mesh.P[i + 1])).Append(' ')
                  .Append(N(mesh.P[i + 2])).Append('\n');
            }

            bool hasUv = mesh.T.Count == (mesh.P.Count / 3) * 2;
            if (hasUv)
            {
                for (int i = 0; i + 1 < mesh.T.Count; i += 2)
                {
                    sb.Append("vt ").Append(N(mesh.T[i])).Append(' ').Append(N(mesh.T[i + 1])).Append('\n');
                }
            }

            for (int i = 0; i + 2 < mesh.I.Count; i += 3)
            {
                int a = mesh.I[i] + 1, b = mesh.I[i + 1] + 1, c = mesh.I[i + 2] + 1;
                if (hasUv) sb.Append("f ").Append(a).Append('/').Append(a).Append(' ')
                            .Append(b).Append('/').Append(b).Append(' ')
                            .Append(c).Append('/').Append(c).Append('\n');
                else sb.Append("f ").Append(a).Append(' ').Append(b).Append(' ').Append(c).Append('\n');
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        /* STL binário: cabeçalho, contagem, e cada triângulo com normal + 3 vértices. */
        public static byte[] Stl(G3 mesh, string name)
        {
            int tris = mesh.I.Count / 3;
            byte[] bytes = new byte[84 + tris * 50];

            byte[] header = Encoding.ASCII.GetBytes("3D Browser Pro " + (name ?? "modelo"));
            Array.Copy(header, 0, bytes, 0, Math.Min(header.Length, 79));

            PutU32(bytes, 80, (uint)tris);

            int o = 84;
            for (int i = 0; i + 2 < mesh.I.Count; i += 3)
            {
                int a = mesh.I[i], b = mesh.I[i + 1], c = mesh.I[i + 2];

                float ax = mesh.P[a * 3], ay = mesh.P[a * 3 + 1], az = mesh.P[a * 3 + 2];
                float bx = mesh.P[b * 3], by = mesh.P[b * 3 + 1], bz = mesh.P[b * 3 + 2];
                float cx = mesh.P[c * 3], cy = mesh.P[c * 3 + 1], cz = mesh.P[c * 3 + 2];

                float ux = bx - ax, uy = by - ay, uz = bz - az;
                float vx = cx - ax, vy = cy - ay, vz = cz - az;

                float nx = uy * vz - uz * vy;
                float ny = uz * vx - ux * vz;
                float nz = ux * vy - uy * vx;
                float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len > 1e-9f) { nx /= len; ny /= len; nz /= len; }

                o = PutF(bytes, o, nx); o = PutF(bytes, o, ny); o = PutF(bytes, o, nz);
                o = PutF(bytes, o, ax); o = PutF(bytes, o, ay); o = PutF(bytes, o, az);
                o = PutF(bytes, o, bx); o = PutF(bytes, o, by); o = PutF(bytes, o, bz);
                o = PutF(bytes, o, cx); o = PutF(bytes, o, cy); o = PutF(bytes, o, cz);
                o = PutU16(bytes, o, 0);
            }

            return bytes;
        }

        /* PLY ascii: vértices + faces, o mais simples de importar. */
        public static byte[] Ply(G3 mesh, string name)
        {
            int verts = mesh.P.Count / 3;
            int tris = mesh.I.Count / 3;

            StringBuilder sb = new StringBuilder(512 + verts * 26 + tris * 24);
            sb.Append("ply\nformat ascii 1.0\n");
            sb.Append("comment 3D Browser Pro ").Append(name ?? "modelo").Append('\n');
            sb.Append("element vertex ").Append(verts).Append('\n');
            sb.Append("property float x\nproperty float y\nproperty float z\n");
            sb.Append("element face ").Append(tris).Append('\n');
            sb.Append("property list uchar int vertex_indices\n");
            sb.Append("end_header\n");

            for (int i = 0; i + 2 < mesh.P.Count; i += 3)
            {
                sb.Append(N(mesh.P[i])).Append(' ').Append(N(mesh.P[i + 1])).Append(' ')
                  .Append(N(mesh.P[i + 2])).Append('\n');
            }

            for (int i = 0; i + 2 < mesh.I.Count; i += 3)
            {
                sb.Append("3 ").Append(mesh.I[i]).Append(' ').Append(mesh.I[i + 1]).Append(' ')
                  .Append(mesh.I[i + 2]).Append('\n');
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        private static string N(float v)
        {
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static int PutF(byte[] b, int o, float v)
        {
            byte[] f = BitConverter.GetBytes(v);
            b[o] = f[0]; b[o + 1] = f[1]; b[o + 2] = f[2]; b[o + 3] = f[3];
            return o + 4;
        }

        private static int PutU16(byte[] b, int o, ushort v)
        {
            b[o] = (byte)(v & 0xFF);
            b[o + 1] = (byte)((v >> 8) & 0xFF);
            return o + 2;
        }

        private static void PutU32(byte[] b, int o, uint v)
        {
            b[o] = (byte)(v & 0xFF);
            b[o + 1] = (byte)((v >> 8) & 0xFF);
            b[o + 2] = (byte)((v >> 16) & 0xFF);
            b[o + 3] = (byte)((v >> 24) & 0xFF);
        }
    }
}
