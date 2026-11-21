// 3D Browser Pro — esqueleto medido na silhueta do modelo (não na caixa).
// Fatia a malha em bandas horizontais e tira daí: linha dos braços (T-pose),
// largura do ombro/quadril, vão entre as pernas, altura do quadril, joelho,
// tornozelo, pescoço e topo da cabeça. Cai nas proporções clássicas se o
// modelo não parecer um humanoide em T-pose.
using System;
using System.Collections.Generic;

namespace BP3D
{
    internal class Shape
    {
        public float Height = 1f;

        public float HipsY = 0.55f;
        public float KneeY = 0.28f;
        public float AnkleY = 0.05f;
        public float ToeZ;

        public float ArmY = 0.85f;
        public float HandX = 0.55f;
        public float ArmX = 0.16f;
        public float ShoulderX = 0.055f;

        public float NeckY = 0.86f;
        public float HeadY = 0.90f;
        public float HeadTopY = 1f;

        public float LegX = 0.09f;
        public float TorsoHalf;

        public bool ArmsOut;
        public bool Valid;

        public string Describe()
        {
            string t = "silhueta: ombro " + P(ShoulderX) + " · mão " + P(HandX) +
                       " · quadril " + P(HipsY) + " · joelho " + P(KneeY) +
                       " · tornozelo " + P(AnkleY) + " · " + (ArmsOut ? "T-pose" : "braços não abertos");
            return t;
        }

        private string P(float v)
        {
            return (v * 100f).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "% da altura";
        }
    }

    /* Tipo de esqueleto que a geometria pede. */
    internal class GeoKind
    {
        public string Kind = "objeto";   // humanoide | cadeia | objeto
        public string Detail = "";
    }

    internal static class RigFit
    {
        private const int Bands = 96;

        public static List<RigBone> Build(G3 mesh, string prefix, out string detail)
        {
            Shape ignored;
            return Build(mesh, prefix, out detail, out ignored);
        }

        public static List<RigBone> Build(G3 mesh, string prefix, out string detail, out Shape s)
        {
            string kind;
            return BuildAuto(mesh, prefix, out detail, out s, out kind);
        }

        /* Classifica a geometria para escolher o esqueleto:
           - humanoide: silhueta de personagem (T-pose ou em pé, com cabeça estreita)
           - cadeia: peça alongada (viga, estátua, tubo) → 3 ossos no eixo mais comprido
           - objeto: peça compacta (cubo, caixa, prop) → rig rígido de 1 osso */
        public static GeoKind Classify(G3 mesh, Shape s)
        {
            float[] min, max;
            mesh.Bounds(out min, out max);

            float w = Math.Max(max[0] - min[0], 1e-4f);
            float h = Math.Max(max[1] - min[1], 1e-4f);
            float d = Math.Max(max[2] - min[2], 1e-4f);

            float topW = SliceWidth(mesh, min[1] + h * 0.93f);
            float chest = 0f;
            for (int i = 4; i <= 7; i++)
            {
                float yc = min[1] + h * (0.45f + 0.05f * i);
                float sw = SliceWidth(mesh, yc);
                if (sw > chest) chest = sw;
            }

            bool headNarrow = chest > w * 0.20f && topW < chest * 0.72f;
            float waist = SliceWidth(mesh, min[1] + h * 0.35f);
            float shoulder = SliceWidth(mesh, min[1] + h * 0.72f);
            bool shoulders = waist > 1e-4f && shoulder > waist * 1.15f;   // ombro sobressai da cintura

            float widest = Math.Max(w, Math.Max(h, d));
            float narrowest = Math.Min(w, Math.Min(h, d));

            GeoKind k = new GeoKind();
            string dims = F(w) + " x " + F(h) + " x " + F(d);
            Log.W("classe: dims=" + w.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) +
                  "x" + h.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) +
                  "x" + d.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) +
                  " topW=" + F(topW) + " chest=" + F(chest) + " bracosAbertos=" + s.ArmsOut +
                  " cabecaEstreita=" + headNarrow + " ombro=" + F(shoulder) + " cintura=" + F(waist));

            /* braços abertos ou cabeça estreita + corpo mais alto que grosso:
               em T-pose a largura chega perto da altura, então comparar com a
               profundidade (d) é o que separa personagem de peça chapada */
            if ((s.ArmsOut || (headNarrow && shoulders && h >= 1.4f * d)) && h >= 1.05f * d && h >= 0.55f * w)
            {
                k.Kind = "humanoide";
                k.Detail = "geometria " + dims + " · personagem " + (s.ArmsOut ? "em T-pose" : "em pé");
            }
            else if (widest / narrowest >= 2.2f)
            {
                k.Kind = "cadeia";
                k.Detail = "geometria " + dims + " · peça alongada: cadeia de 3 ossos no eixo mais comprido";
            }
            else
            {
                k.Kind = "objeto";
                k.Detail = "geometria " + dims + " · peça compacta: rig rígido de 1 osso raiz";
            }

            return k;
        }

        /* X onde os triângulos cruzam um plano horizontal: a silhueta daquele corte. */
        private static List<float> CollectXs(G3 mesh, float yc)
        {
            List<float> xs = new List<float>();
            List<int> tris = mesh.I;

            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                int a = tris[t] * 3, b = tris[t + 1] * 3, c = tris[t + 2] * 3;
                if (a + 2 >= mesh.P.Count || b + 2 >= mesh.P.Count || c + 2 >= mesh.P.Count) continue;

                CrossX(mesh, a, b, yc, xs);
                CrossX(mesh, b, c, yc, xs);
                CrossX(mesh, c, a, yc, xs);
            }

            return xs;
        }

        private static void CrossX(G3 mesh, int i, int j, float yc, List<float> xs)
        {
            float y1 = mesh.P[i + 1], y2 = mesh.P[j + 1];
            if ((y1 - yc) * (y2 - yc) > 0f) return;   // aresta não cruza o plano
            if (y1 == y2) return;                     // aresta paralela ao plano

            float t = (yc - y1) / (y2 - y1);
            if (t < 0f || t > 1f) return;

            xs.Add(mesh.P[i] + (mesh.P[j] - mesh.P[i]) * t);
        }

        private static float SliceWidth(G3 mesh, float yc)
        {
            List<float> xs = CollectXs(mesh, yc);
            if (xs.Count == 0) return 0f;

            float lo = xs[0], hi = xs[0];
            for (int i = 1; i < xs.Count; i++)
            {
                if (xs[i] < lo) lo = xs[i];
                if (xs[i] > hi) hi = xs[i];
            }
            return hi - lo;
        }

        private static string F(float v)
        {
            return v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }

        /* Rigs para quem não é humanoide. */
        private static List<RigBone> Simple(G3 mesh, string kind)
        {
            float[] min, max;
            mesh.Bounds(out min, out max);

            float w = max[0] - min[0];
            float h = max[1] - min[1];
            float d = max[2] - min[2];

            List<RigBone> b = new List<RigBone>();

            if (kind == "objeto")
            {
                Add(b, "Root", -1, 0, 0, 0);   // peça rígida: só o pivô
                return b;
            }

            if (h >= w && h >= d)
            {
                Add(b, "Root", -1, 0, 0, 0);
                int mid = Add(b, "Spine", 0, 0, h * 0.5f, 0);
                Add(b, "Top", mid, 0, h, 0);
            }
            else if (w >= d)
            {
                int root = Add(b, "Root", -1, -w * 0.45f, h * 0.5f, 0);
                int mid = Add(b, "Mid", root, 0, h * 0.5f, 0);
                Add(b, "Tip", mid, w * 0.45f, h * 0.5f, 0);
            }
            else
            {
                int root = Add(b, "Root", -1, 0, h * 0.5f, -d * 0.45f);
                int mid = Add(b, "Mid", root, 0, h * 0.5f, 0);
                Add(b, "Tip", mid, 0, h * 0.5f, d * 0.45f);
            }

            return b;
        }

        public static List<RigBone> BuildAuto(G3 mesh, string prefix, out string detail, out Shape s, out string kind)
        {
            s = Measure(mesh);
            GeoKind geo = Classify(mesh, s);
            kind = geo.Kind;
            detail = geo.Detail + " · " + s.Describe();

            if (kind != "humanoide") return Simple(mesh, kind);

            List<RigBone> human = BuildHumanoid(mesh, prefix, out detail, out s);
            detail = geo.Detail + " · " + detail;
            return human;
        }

        /* Humanoide sempre: 23 ossos no molde clássico, mesmo que a classificação
           da geometria tenha escolhido cadeia/objeto (botão "T-pose"). */
        public static List<RigBone> BuildHumanoid(G3 mesh, string prefix, out string detail, out Shape s)
        {
            s = Measure(mesh);
            detail = "esqueleto humanoide (T-pose) · " + s.Describe();

            if (!s.Valid) return Rig.Humanoid(s.Height, 0, prefix.Length > 0);

            List<RigBone> b = new List<RigBone>();
            float h = s.Height;

            int hips = Add(b, prefix + "Hips", -1, 0, s.HipsY * h, 0);

            float span = s.NeckY * h - s.HipsY * h;
            if (span < 0.10f * h) span = 0.30f * h;

            int spine = Add(b, prefix + "Spine", hips, 0, s.HipsY * h + span * 0.22f, 0);
            int spine1 = Add(b, prefix + "Spine1", spine, 0, s.HipsY * h + span * 0.50f, 0);
            int spine2 = Add(b, prefix + "Spine2", spine1, 0, s.HipsY * h + span * 0.78f, 0);
            int neck = Add(b, prefix + "Neck", spine2, 0, s.NeckY * h, 0);
            int head = Add(b, prefix + "Head", neck, 0, s.HeadY * h, 0);
            Add(b, prefix + "HeadTop_End", head, 0, s.HeadTopY * h, 0);

            Side(b, prefix, "Left", 1f, spine2, hips, s);
            Side(b, prefix, "Right", -1f, spine2, hips, s);

            return b;
        }

        private static void Side(List<RigBone> b, string p, string side, float dir,
                                 int spine2, int hips, Shape s)
        {
            float h = s.Height;
            float armY = s.ArmY * h;

            int shoulder = Add(b, p + side + "Shoulder", spine2, dir * s.ShoulderX * h, armY, 0);
            int arm = Add(b, p + side + "Arm", shoulder, dir * s.ArmX * h, armY, 0);

            float fore = s.ArmX + (s.HandX - s.ArmX) * 0.55f;
            int foreIdx = Add(b, p + side + "ForeArm", arm, dir * fore * h, armY, 0);
            Add(b, p + side + "Hand", foreIdx, dir * s.HandX * h, armY, 0);

            float x = dir * s.LegX * h;
            int upLeg = Add(b, p + side + "UpLeg", hips, x, s.HipsY * h - 0.02f * h, 0);
            int leg = Add(b, p + side + "Leg", upLeg, x, s.KneeY * h, 0);
            int foot = Add(b, p + side + "Foot", leg, x, s.AnkleY * h, 0);
            Add(b, p + side + "ToeBase", foot, x, Math.Max(s.AnkleY * 0.35f, 0.012f) * h, s.ToeZ * h);
        }

        private static int Add(List<RigBone> list, string name, int parent, float x, float y, float z)
        {
            RigBone bone = new RigBone();
            bone.Name = name;
            bone.Parent = parent;
            bone.X = x;
            bone.Y = y;
            bone.Z = z;
            list.Add(bone);
            return list.Count - 1;
        }

        /* Mede a silhueta em bandas horizontais de Y. */
        public static Shape Measure(G3 mesh)
        {
            Shape s = new Shape();
            int n = mesh.P.Count / 3;
            if (n < 32) { s.Valid = false; return s; }

            float mnY = float.MaxValue, mxY = float.MinValue;
            float mnZ = float.MaxValue, mxZ = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                float y = mesh.P[i * 3 + 1];
                float z = mesh.P[i * 3 + 2];
                if (y < mnY) mnY = y;
                if (y > mxY) mxY = y;
                if (z < mnZ) mnZ = z;
                if (z > mxZ) mxZ = z;
            }

            float h = mxY - mnY;
            if (h < 1e-4f) { s.Valid = false; return s; }

            float[] bMinX = new float[Bands];
            float[] bMaxX = new float[Bands];
            float[] bMinZ = new float[Bands];
            float[] bMaxZ = new float[Bands];
            int[] bCount = new int[Bands];
            float[] bMinAbs = new float[Bands];

            for (int i = 0; i < Bands; i++)
            {
                bMinX[i] = float.MaxValue;
                bMaxX[i] = float.MinValue;
                bMinZ[i] = float.MaxValue;
                bMaxZ[i] = float.MinValue;
                bMinAbs[i] = float.MaxValue;
            }

            for (int i = 0; i < n; i++)
            {
                float x = mesh.P[i * 3];
                float y = mesh.P[i * 3 + 1];
                float z = mesh.P[i * 3 + 2];

                int band = (int)((y - mnY) / h * (Bands - 1));
                if (band < 0) band = 0;
                else if (band >= Bands) band = Bands - 1;

                if (x < bMinX[band]) bMinX[band] = x;
                if (x > bMaxX[band]) bMaxX[band] = x;
                if (z < bMinZ[band]) bMinZ[band] = z;
                if (z > bMaxZ[band]) bMaxZ[band] = z;
                float ax = x < 0 ? -x : x;
                if (ax < bMinAbs[band]) bMinAbs[band] = ax;
                bCount[band]++;
            }

            // meia-largura de cada banda, normalizada pela altura
            float[] half = new float[Bands];
            for (int i = 0; i < Bands; i++)
            {
                half[i] = bCount[i] == 0 ? 0 : Math.Max(-bMinX[i], bMaxX[i]) / h;
            }

            // 1) tronco: maior meia-largura entre 42% e 64% da altura
            float torso = 0;
            for (int i = 0; i < Bands; i++)
            {
                float at = (i + 0.5f) / Bands;
                if (at < 0.42f || at > 0.64f) continue;
                if (half[i] > torso) torso = half[i];
            }
            if (torso <= 0.01f)
            {
                s.Valid = false;
                return s;
            }
            s.TorsoHalf = torso;

            // 2) braços: banda mais larga acima da cintura (T-pose abre os braços)
            float armHalf = 0;
            int armBand = -1;
            for (int i = 0; i < Bands; i++)
            {
                float at = (i + 0.5f) / Bands;
                if (at < 0.60f || at > 0.96f) continue;
                if (half[i] > armHalf) { armHalf = half[i]; armBand = i; }
            }

            s.ArmsOut = armBand >= 0 && armHalf > torso * 1.30f;

            if (s.ArmsOut)
            {
                s.ArmY = (armBand + 0.5f) / Bands;
                s.HandX = armHalf * 0.96f;
                s.ArmX = torso * 1.02f;
                s.ShoulderX = torso * 0.52f;
            }
            else
            {
                // sem T-pose: proporções clássicas para os braços
                s.ArmY = 0.85f;
                s.HandX = 0.55f;
                s.ArmX = 0.16f;
                s.ShoulderX = 0.055f;
            }

            // 3) quadril: acima da virilha a malha é contínua e cruza a linha central; abaixo
            //    dela só existem as pernas. Então o quadril é o y MAIS BAIXO que ainda tem
            //    vértice perto do centro (ignorando a região dos pés).
            float centerY = 0;
            float centerLimit = 0.015f * h;
            for (int i = 0; i < n; i++)
            {
                float x = mesh.P[i * 3];
                float y = mesh.P[i * 3 + 1] - mnY;
                if (y < 0.25f * h || y > 0.72f * h) continue;
                if (Math.Abs(x) > centerLimit) continue;
                float rel = y / h;
                if (centerY <= 0 || rel < centerY) centerY = rel;
            }
            if (centerY <= 0) centerY = 0.48f;
            float hipsByCentre = centerY + 0.03f;   // pista 1: linha central da malha

            float core = 0.10f * torso;

            // 4) centro de cada perna entre 8% e 22% da altura
            float sumLeft = 0, sumRight = 0;
            int nLeft = 0, nRight = 0;
            for (int i = 0; i < n; i++)
            {
                float at = (mesh.P[i * 3 + 1] - mnY) / h;
                if (at < 0.08f || at > 0.22f) continue;
                float x = mesh.P[i * 3];
                if (x < -core * h * 0.5f) { sumLeft += -x; nLeft++; }
                else if (x > core * h * 0.5f) { sumRight += x; nRight++; }
            }

            float legX = 0;
            if (nLeft > 0) legX += (sumLeft / nLeft) / h;
            if (nRight > 0) legX += (sumRight / nRight) / h;
            if (nLeft > 0 && nRight > 0) legX *= 0.5f;
            s.LegX = legX > 0.005f ? Math.Min(legX, torso) : 0.09f;

            // 5) tornozelo: ponto mais estreito (a "canela") logo acima do pé
            float ankle = 0;
            float thin = float.MaxValue;
            for (int i = 0; i < Bands; i++)
            {
                float at = (i + 0.5f) / Bands;
                if (at < 0.03f || at > 0.18f) continue;
                if (bCount[i] < 8 || half[i] <= 0) continue;
                if (half[i] < thin) { thin = half[i]; ankle = at; }
            }
            if (ankle <= 0) ankle = 0.05f;
            if (ankle > 0.14f) ankle = 0.14f;

            // 6) quadril: cruza duas pistas da silhueta — a linha central da malha e, quando
            //    os braços estão em T-pose, a proporção virilha ≈ 0.64 × linha dos braços.
            float byArms = s.ArmsOut ? s.ArmY * 0.64f : 0f;
            float hips = Math.Max(hipsByCentre, byArms);
            hips = Math.Min(Math.Max(hips, 0.42f), 0.60f);

            // 7) joelho no meio da perna: medir o afunilamento confunde com a canela
            float knee = ankle + (hips - ankle) * 0.45f;
            if (knee > hips - 0.05f) knee = hips - 0.05f;

            s.AnkleY = ankle;
            s.KneeY = knee;
            s.HipsY = hips;

            // 6) pé aponta para o lado maior em Z
            float zPos = Math.Abs(mxZ) >= Math.Abs(mnZ) ? mxZ : mnZ;
            s.ToeZ = (zPos / h) * 0.55f;

            // 7) pescoço: banda mais estreita no alto (acima dos ombros)
            float neck = 0.86f;
            float neckHalf = float.MaxValue;
            for (int i = 0; i < Bands; i++)
            {
                if (bCount[i] == 0) continue;
                float at = (i + 0.5f) / Bands;
                if (at < 0.70f || at > 0.95f) continue;
                if (half[i] <= 0) continue;
                if (half[i] < neckHalf) { neckHalf = half[i]; neck = at; }
            }
            s.NeckY = neck;
            s.HeadY = Math.Min(neck + 0.05f, 0.97f);
            s.HeadTopY = 1.0f;
            s.Height = h;

            // coerência mínima
            if (s.HipsY < 0.30f || s.HipsY > 0.75f) s.HipsY = 0.55f;
            if (s.KneeY >= s.HipsY || s.KneeY <= s.AnkleY) s.KneeY = s.AnkleY + (s.HipsY - s.AnkleY) * 0.52f;
            if (s.NeckY <= s.HipsY) s.NeckY = s.HipsY + 0.25f;

            s.Valid = true;
            return s;
        }
    }
}
