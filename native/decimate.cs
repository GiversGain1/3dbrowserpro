// 3D Browser Pro — redução de triângulos para o FBX com esqueleto.
// Solda os vértices que caem na mesma célula de uma grade; é barato, sem
// dependências, e mantém normais e UVs (médias por célula) válidos.
using System;
using System.Collections.Generic;

namespace BP3D
{
    internal static class Decimate
    {
        /* No máximo maxTriangles; <= 0 ou malha já pequena devolve a mesma malha. */
        public static G3 ToTriangles(G3 mesh, int maxTriangles)
        {
            if (mesh == null || maxTriangles <= 0 || mesh.Triangles <= maxTriangles) return mesh;

            float[] min, max;
            mesh.Bounds(out min, out max);
            float size = Math.Max(max[0] - min[0], Math.Max(max[1] - min[1], max[2] - min[2]));
            if (size <= 1e-6f) return mesh;

            /* numa superfície fechada os triângulos ficam perto de 2·res², então a primeira
               grade já cai quase no alvo; as passadas seguintes corrigem pelo erro medido. */
            int res = (int)Math.Sqrt(maxTriangles / 2.0);
            if (res < 4) res = 4;
            if (res > 2048) res = 2048;

            /* faixa [lo, hi): hi é a primeira grade que já estourou o alvo */
            int lo = 4, hi = 2049;
            G3 best = null;

            for (int pass = 0; pass < 7 && lo < hi; pass++)
            {
                G3 cand = Weld(mesh, min, size, res);
                int tris = cand.Triangles;

                if (tris <= maxTriangles)
                {
                    best = cand;
                    lo = res + 1;
                    if (tris * 100 >= maxTriangles * 97) break;   // já encostou no alvo
                }
                else hi = res;

                /* próximo chute pela razão entre o alvo e o que saiu; senão, bisseção */
                int next = tris <= maxTriangles
                    ? (int)Math.Ceiling(res * Math.Sqrt((double)maxTriangles / tris))
                    : (int)(res * Math.Sqrt((double)maxTriangles / tris) * 0.97);

                if (next < lo) next = lo;
                if (next >= hi) next = lo + (hi - lo) / 2;
                if (next < lo || next >= hi || next == res) break;
                res = next;
            }

            return best ?? Weld(mesh, min, size, 4);
        }

        private static G3 Weld(G3 mesh, float[] min, float size, int res)
        {
            G3 out3 = new G3();

            int verts = mesh.P.Count / 3;
            bool hasUv = mesh.T.Count == verts * 2;
            bool hasN = mesh.N.Count == verts * 3;
            int[] remap = new int[verts];
            List<int> used = new List<int>(verts);
            Dictionary<long, int> cells = new Dictionary<long, int>(verts);

            for (int v = 0; v < verts; v++)
            {
                long key = ((long)Cell(mesh.P[v * 3], min[0], size, res) * (res + 1) +
                            Cell(mesh.P[v * 3 + 1], min[1], size, res)) * (res + 1) +
                           Cell(mesh.P[v * 3 + 2], min[2], size, res);

                int id;
                if (!cells.TryGetValue(key, out id))
                {
                    id = used.Count;
                    cells[key] = id;
                    used.Add(0);
                    out3.P.Add(0); out3.P.Add(0); out3.P.Add(0);
                    out3.N.Add(0); out3.N.Add(0); out3.N.Add(0);
                    if (hasUv) { out3.T.Add(0); out3.T.Add(0); }
                }
                remap[v] = id;
                used[id]++;

                out3.P[id * 3] += mesh.P[v * 3];
                out3.P[id * 3 + 1] += mesh.P[v * 3 + 1];
                out3.P[id * 3 + 2] += mesh.P[v * 3 + 2];
                if (hasN)
                {
                    out3.N[id * 3] += mesh.N[v * 3];
                    out3.N[id * 3 + 1] += mesh.N[v * 3 + 1];
                    out3.N[id * 3 + 2] += mesh.N[v * 3 + 2];
                }
                if (hasUv)
                {
                    out3.T[id * 2] += mesh.T[v * 2];
                    out3.T[id * 2 + 1] += mesh.T[v * 2 + 1];
                }
            }

            /* cada célula vira a média dos vértices dela (posição, normal e UV) */
            for (int id = 0; id < used.Count; id++)
            {
                float n = Math.Max(1, used[id]);
                out3.P[id * 3] /= n; out3.P[id * 3 + 1] /= n; out3.P[id * 3 + 2] /= n;
                if (hasUv) { out3.T[id * 2] /= n; out3.T[id * 2 + 1] /= n; }

                float nx = out3.N[id * 3], ny = out3.N[id * 3 + 1], nz = out3.N[id * 3 + 2];
                float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len < 1e-6f) { nx = 0; ny = 1; nz = 0; len = 1; }
                out3.N[id * 3] = nx / len;
                out3.N[id * 3 + 1] = ny / len;
                out3.N[id * 3 + 2] = nz / len;
            }

            /* faces: fora as que viraram linha/ponto e as repetidas depois da solda */
            bool dedupe = used.Count < (1 << 21);
            HashSet<long> seen = dedupe ? new HashSet<long>() : null;

            for (int i = 0; i + 2 < mesh.I.Count; i += 3)
            {
                int a = remap[mesh.I[i]], b = remap[mesh.I[i + 1]], c = remap[mesh.I[i + 2]];
                if (a == b || b == c || a == c) continue;

                if (dedupe && !seen.Add(FaceKey(a, b, c))) continue;

                out3.I.Add(a); out3.I.Add(b); out3.I.Add(c);
            }

            return out3;
        }

        private static int Cell(float value, float min, float size, int res)
        {
            int c = (int)((value - min) / size * res);
            if (c < 0) return 0;
            return c > res ? res : c;
        }

        private static long FaceKey(int a, int b, int c)
        {
            if (a > b) { int t = a; a = b; b = t; }
            if (b > c) { int t = b; b = c; c = t; }
            if (a > b) { int t = a; a = b; b = t; }
            return ((long)(uint)a << 42) | ((long)(uint)b << 21) | (uint)c;
        }
    }
}
