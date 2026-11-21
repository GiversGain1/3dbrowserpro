// 3D Browser Pro — GLB/glTF -> FBX (ASCII 7.4) com esqueleto humanoide opcional.
// Sem dependências: lê o GLB na mão, monta um skin simples e escreve FBX que o
// three.js/Mixamo entendem.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Web.Script.Serialization;

namespace BP3D
{
    internal class G3
    {
        public List<float> P = new List<float>();
        public List<float> N = new List<float>();
        public List<float> T = new List<float>();
        public List<int> I = new List<int>();

        public bool HasNormals { get { return N.Count == P.Count; } }
        public bool HasUV { get { return T.Count == (P.Count / 3) * 2; } }
        public int Triangles { get { return I.Count / 3; } }

        public void Bounds(out float[] min, out float[] max)
        {
            min = new float[] { float.MaxValue, float.MaxValue, float.MaxValue };
            max = new float[] { float.MinValue, float.MinValue, float.MinValue };
            for (int i = 0; i + 2 < P.Count; i += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    float v = P[i + k];
                    if (v < min[k]) min[k] = v;
                    if (v > max[k]) max[k] = v;
                }
            }
            if (P.Count == 0) { min = new float[3]; max = new float[3]; }
        }
    }

    internal static class GltfRead
    {
        private static readonly JavaScriptSerializer Js = new JavaScriptSerializer();
        static GltfRead() { Js.MaxJsonLength = int.MaxValue; }

        public static G3 Load(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 20) throw new Exception("arquivo vazio");

            string json = null;
            byte[] bin = null;

            uint magic = BitConverter.ToUInt32(bytes, 0);
            if (magic == 0x46546C67)
            {
                int off = 12;
                while (off + 8 <= bytes.Length)
                {
                    uint len = BitConverter.ToUInt32(bytes, off);
                    uint type = BitConverter.ToUInt32(bytes, off + 4);
                    int start = off + 8;
                    if (len > 0 && start + (int)len > bytes.Length) break;
                    if (type == 0x4E4F534A) json = Encoding.UTF8.GetString(bytes, start, (int)len);
                    else if (type == 0x004E4942)
                    {
                        bin = new byte[len];
                        Array.Copy(bytes, start, bin, 0, (int)len);
                    }
                    off = start + (int)len;
                    while (off % 4 != 0) off++;
                }
            }
            else
            {
                json = Encoding.UTF8.GetString(bytes);
            }

            if (string.IsNullOrEmpty(json)) throw new Exception("JSON do glTF não encontrado");

            Dictionary<string, object> root = Js.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null) throw new Exception("glTF inválido");

            G3 out3 = new G3();
            object[] nodes = Arr(root, "nodes");
            object[] meshes = Arr(root, "meshes");
            object[] scenes = Arr(root, "scenes");

            // só um buffer binário: GLB ou data-URI
            if (bin == null) bin = BufferBytes(root, 0);

            List<int> roots = new List<int>();
            object[] sceneNodes = scenes != null && scenes.Length > 0 ? Arr(AsDict(scenes[0]), "nodes") : null;
            if (sceneNodes != null)
            {
                for (int i = 0; i < sceneNodes.Length; i++) roots.Add(ToI(sceneNodes[i]));
            }
            else if (nodes != null)
            {
                for (int i = 0; i < nodes.Length; i++) roots.Add(i);
            }

            float[] id = new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
            for (int i = 0; i < roots.Count; i++) Walk(root, bin, nodes, meshes, roots[i], id, out3, 0);

            if (out3.I.Count == 0)
            {
                // sem índice: triangula os vértices na ordem
                int n = out3.P.Count / 3;
                for (int i = 0; i + 2 < n; i += 3) out3.I.AddRange(new int[] { i, i + 1, i + 2 });
            }

            return out3;
        }

        private static void Walk(Dictionary<string, object> root, byte[] bin, object[] nodes, object[] meshes,
                                 int index, float[] parent, G3 out3, int depth)
        {
            if (depth > 96 || nodes == null || index < 0 || index >= nodes.Length) return;

            Dictionary<string, object> node = AsDict(nodes[index]);
            if (node == null) return;

            float[] m = Mul(parent, LocalMatrix(node));

            int meshIndex = ToI(Get(node, "mesh"), -1);
            if (meshIndex >= 0 && meshes != null && meshIndex < meshes.Length)
            {
                AddMesh(root, bin, AsDict(meshes[meshIndex]), m, out3);
            }

            object[] kids = Arr(node, "children");
            if (kids != null)
            {
                for (int i = 0; i < kids.Length; i++) Walk(root, bin, nodes, meshes, ToI(kids[i]), m, out3, depth + 1);
            }
        }

        private static void AddMesh(Dictionary<string, object> root, byte[] bin, Dictionary<string, object> mesh,
                                    float[] world, G3 out3)
        {
            object[] prims = mesh == null ? null : Arr(mesh, "primitives");
            if (prims == null) return;

            for (int p = 0; p < prims.Length; p++)
            {
                Dictionary<string, object> prim = AsDict(prims[p]);
                if (prim == null) continue;
                if (ToI(Get(prim, "mode"), 4) != 4) continue;   // só triângulos

                Dictionary<string, object> attrs = AsDict(Get(prim, "attributes"));
                if (attrs == null) continue;

                float[] pos = ReadFloats(root, bin, ToI(Get(attrs, "POSITION"), -1), 3);
                if (pos == null || pos.Length == 0) continue;

                float[] nrm = ReadFloats(root, bin, ToI(Get(attrs, "NORMAL"), -1), 3);
                float[] uv = ReadFloats(root, bin, ToI(Get(attrs, "TEXCOORD_0"), -1), 2);
                int[] idx = ReadIndices(root, bin, ToI(Get(prim, "indices"), -1));

                int baseV = out3.P.Count / 3;
                int vcount = pos.Length / 3;

                // posição e normal em espaço de mundo
                for (int i = 0; i < vcount; i++)
                {
                    float x = pos[i * 3], y = pos[i * 3 + 1], z = pos[i * 3 + 2];
                    out3.P.Add(world[0] * x + world[4] * y + world[8] * z + world[12]);
                    out3.P.Add(world[1] * x + world[5] * y + world[9] * z + world[13]);
                    out3.P.Add(world[2] * x + world[6] * y + world[10] * z + world[14]);

                    if (nrm != null && nrm.Length >= (i + 1) * 3)
                    {
                        float nx = nrm[i * 3], ny = nrm[i * 3 + 1], nz = nrm[i * 3 + 2];
                        float tx = world[0] * nx + world[4] * ny + world[8] * nz;
                        float ty = world[1] * nx + world[5] * ny + world[9] * nz;
                        float tz = world[2] * nx + world[6] * ny + world[10] * nz;
                        float len = (float)Math.Sqrt(tx * tx + ty * ty + tz * tz);
                        if (len < 1e-6f) { tx = 0; ty = 1; tz = 0; len = 1; }
                        out3.N.Add(tx / len); out3.N.Add(ty / len); out3.N.Add(tz / len);
                    }
                    else
                    {
                        out3.N.Add(0); out3.N.Add(1); out3.N.Add(0);
                    }

                    if (uv != null && uv.Length >= (i + 1) * 2) { out3.T.Add(uv[i * 2]); out3.T.Add(uv[i * 2 + 1]); }
                    else { out3.T.Add(0); out3.T.Add(0); }
                }

                if (idx != null)
                {
                    for (int i = 0; i + 2 < idx.Length; i += 3)
                    {
                        out3.I.Add(baseV + idx[i]);
                        out3.I.Add(baseV + idx[i + 1]);
                        out3.I.Add(baseV + idx[i + 2]);
                    }
                }
                else
                {
                    for (int i = 0; i + 2 < vcount; i += 3)
                    {
                        out3.I.Add(baseV + i);
                        out3.I.Add(baseV + i + 1);
                        out3.I.Add(baseV + i + 2);
                    }
                }
            }
        }

        /* ---- acessores ---- */

        private static float[] ReadFloats(Dictionary<string, object> root, byte[] bin, int accIndex, int comps)
        {
            if (accIndex < 0) return null;
            object[] accs = Arr(root, "accessors");
            if (accs == null || accIndex >= accs.Length) return null;

            Dictionary<string, object> acc = AsDict(accs[accIndex]);
            int count = ToI(Get(acc, "count"), 0);
            int comp = ToI(Get(acc, "componentType"), 5126);
            if (count <= 0) return null;

            int stride = Components(Get(acc, "type"));
            float[] result = new float[count * comps];
            int viewOffset = 0;
            int viewStride = 0;
            byte[] raw = Bin(root, bin, ToI(Get(acc, "bufferView"), -1), out viewOffset, out viewStride);
            if (raw == null)
            {
                if (comp == 5126) return null;
                return result;
            }

            int accOffset = ToI(Get(acc, "byteOffset"), 0);
            int bytesPer = comp == 5126 ? 4 : comp == 5125 ? 4 : comp == 5123 ? 2 : 1;
            int elementSize = bytesPer * stride;
            int step = viewStride > 0 ? viewStride : elementSize;

            for (int i = 0; i < count; i++)
            {
                int at = viewOffset + accOffset + i * step;
                for (int c = 0; c < comps && c < stride; c++)
                {
                    int p = at + c * bytesPer;
                    if (p + bytesPer > raw.Length) break;
                    result[i * comps + c] = Sample(raw, p, comp);
                }
            }
            return result;
        }

        private static int[] ReadIndices(Dictionary<string, object> root, byte[] bin, int accIndex)
        {
            if (accIndex < 0) return null;
            object[] accs = Arr(root, "accessors");
            if (accs == null || accIndex >= accs.Length) return null;

            Dictionary<string, object> acc = AsDict(accs[accIndex]);
            int count = ToI(Get(acc, "count"), 0);
            int comp = ToI(Get(acc, "componentType"), 5123);
            if (count <= 0) return null;

            int[] result = new int[count];
            int viewOffset = 0;
            int viewStride = 0;
            byte[] raw = Bin(root, bin, ToI(Get(acc, "bufferView"), -1), out viewOffset, out viewStride);
            if (raw == null) return null;

            int accOffset = ToI(Get(acc, "byteOffset"), 0);
            int bytesPer = comp == 5125 ? 4 : comp == 5123 ? 2 : 1;
            int step = viewStride > 0 ? viewStride : bytesPer;

            for (int i = 0; i < count; i++)
            {
                int p = viewOffset + accOffset + i * step;
                if (p + bytesPer > raw.Length) break;
                result[i] = comp == 5125 ? (int)BitConverter.ToUInt32(raw, p)
                          : comp == 5123 ? (int)BitConverter.ToUInt16(raw, p)
                          : (int)raw[p];
            }
            return result;
        }

        private static float Sample(byte[] raw, int at, int comp)
        {
            switch (comp)
            {
                case 5126: return BitConverter.ToSingle(raw, at);
                case 5125: return BitConverter.ToUInt32(raw, at);
                case 5123: return BitConverter.ToUInt16(raw, at);
                case 5121: return raw[at];
                case 5122: return BitConverter.ToInt16(raw, at);
                case 5120: return (sbyte)raw[at];
                default: return 0;
            }
        }

        private static byte[] Bin(Dictionary<string, object> root, byte[] bin, int viewIndex, out int offset, out int stride)
        {
            offset = 0;
            stride = 0;
            if (viewIndex < 0) return null;

            object[] views = Arr(root, "bufferViews");
            if (views == null || viewIndex >= views.Length) return null;

            Dictionary<string, object> view = AsDict(views[viewIndex]);
            int buffer = ToI(Get(view, "buffer"), 0);
            offset = ToI(Get(view, "byteOffset"), 0);
            stride = ToI(Get(view, "byteStride"), 0);

            byte[] data = buffer == 0 ? bin : BufferBytes(root, buffer);
            return data;
        }

        private static byte[] BufferBytes(Dictionary<string, object> root, int index)
        {
            object[] buffers = Arr(root, "buffers");
            if (buffers == null || index >= buffers.Length) return new byte[0];

            string uri = ToS(Get(AsDict(buffers[index]), "uri"));
            if (string.IsNullOrEmpty(uri) || uri.IndexOf("base64,", StringComparison.Ordinal) < 0) return new byte[0];

            try
            {
                int comma = uri.IndexOf("base64,", StringComparison.Ordinal) + 7;
                return Convert.FromBase64String(uri.Substring(comma));
            }
            catch (Exception) { return new byte[0]; }
        }

        private static int Components(object type)
        {
            string t = Convert.ToString(type);
            if (t == "VEC3") return 3;
            if (t == "VEC4") return 4;
            if (t == "VEC2") return 2;
            if (t == "MAT4") return 16;
            return 1;
        }

        /* ---- matrizes ---- */

        private static float[] LocalMatrix(Dictionary<string, object> node)
        {
            object[] matrix = Arr(node, "matrix");
            if (matrix != null && matrix.Length == 16)
            {
                float[] m = new float[16];
                for (int i = 0; i < 16; i++) m[i] = ToF(matrix[i]);
                return m;
            }

            float[] t = Vec3(node, "translation", 0, 0, 0);
            float[] r = Vec4(node, "rotation", 0, 0, 0, 1);   // x,y,z,w
            float[] s = Vec3(node, "scale", 1, 1, 1);

            float x = r[0], y = r[1], z = r[2], w = r[3];
            float x2 = x + x, y2 = y + y, z2 = z + z;
            float xx = x * x2, xy = x * y2, xz = x * z2;
            float yy = y * y2, yz = y * z2, zz = z * z2;
            float wx = w * x2, wy = w * y2, wz = w * z2;

            return new float[]
            {
                (1 - (yy + zz)) * s[0], (xy + wz) * s[0], (xz - wy) * s[0], 0,
                (xy - wz) * s[1], (1 - (xx + zz)) * s[1], (yz + wx) * s[1], 0,
                (xz + wy) * s[2], (yz - wx) * s[2], (1 - (xx + yy)) * s[2], 0,
                t[0], t[1], t[2], 1
            };
        }

        private static float[] Mul(float[] a, float[] b)
        {
            float[] r = new float[16];
            for (int col = 0; col < 4; col++)
            {
                for (int row = 0; row < 4; row++)
                {
                    float sum = 0;
                    for (int k = 0; k < 4; k++) sum += a[k * 4 + row] * b[col * 4 + k];
                    r[col * 4 + row] = sum;
                }
            }
            return r;
        }

        private static float[] Vec3(Dictionary<string, object> d, string key, float dx, float dy, float dz)
        {
            object[] v = Arr(d, key);
            if (v == null || v.Length < 3) return new float[] { dx, dy, dz };
            return new float[] { ToF(v[0]), ToF(v[1]), ToF(v[2]) };
        }

        private static float[] Vec4(Dictionary<string, object> d, string key, float dx, float dy, float dz, float dw)
        {
            object[] v = Arr(d, key);
            if (v == null || v.Length < 4) return new float[] { dx, dy, dz, dw };
            return new float[] { ToF(v[0]), ToF(v[1]), ToF(v[2]), ToF(v[3]) };
        }

        /* ---- helpers de JSON ---- */

        public static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v)) return null;
            return v;
        }

        public static Dictionary<string, object> AsDict(object o)
        {
            return o as Dictionary<string, object>;
        }

        public static object[] Arr(Dictionary<string, object> d, string key) { return Get(d, key) as object[]; }

        public static int ToI(object o) { return ToI(o, 0); }

        public static int ToI(object o, int fallback)
        {
            if (o == null) return fallback;
            try { return Convert.ToInt32(o, CultureInfo.InvariantCulture); } catch (Exception) { return fallback; }
        }

        public static float ToF(object o)
        {
            if (o == null) return 0;
            try { return Convert.ToSingle(o, CultureInfo.InvariantCulture); } catch (Exception) { return 0; }
        }

        public static string ToS(object o) { return o == null ? "" : Convert.ToString(o, CultureInfo.InvariantCulture); }
    }

    /* Esqueleto humanoide em T-pose, com nomes do Mixamo. */
    internal class RigBone
    {
        public string Name;
        public int Parent = -1;
        public float X, Y, Z;
    }

    internal static class Rig
    {
        public static List<RigBone> Humanoid(float height, float widthX, bool mixamoNames)
        {
            if (height < 1e-4f) height = 1f;
            float h = height;
            string p = mixamoNames ? "mixamorig:" : "";

            List<RigBone> b = new List<RigBone>();
            int hips = Add(b, p + "Hips", -1, 0, 0.55f * h);
            int spine = Add(b, p + "Spine", hips, 0, 0.62f * h);
            int spine1 = Add(b, p + "Spine1", spine, 0, 0.70f * h);
            int spine2 = Add(b, p + "Spine2", spine1, 0, 0.78f * h);
            int neck = Add(b, p + "Neck", spine2, 0, 0.86f * h);
            int head = Add(b, p + "Head", neck, 0, 0.90f * h);
            Add(b, p + "HeadTop_End", head, 0, 1.00f * h);

            float shoulderX = 0.055f * h;
            float armX = 0.16f * h;
            float elbowX = 0.36f * h;
            float handX = 0.55f * h;

            Side(b, p, "Left", 1f, spine2, shoulderX, armX, elbowX, handX, h);
            Side(b, p, "Right", -1f, spine2, shoulderX, armX, elbowX, handX, h);

            return b;
        }

        private static void Side(List<RigBone> b, string p, string side, float dir, int spine2,
                                 float shoulderX, float armX, float elbowX, float handX, float h)
        {
            int shoulder = Add(b, p + side + "Shoulder", spine2, dir * shoulderX, 0.85f * h);
            int arm = Add(b, p + side + "Arm", shoulder, dir * armX, 0.85f * h);
            int fore = Add(b, p + side + "ForeArm", arm, dir * elbowX, 0.85f * h);
            Add(b, p + side + "Hand", fore, dir * handX, 0.85f * h);

            int upLeg = Add(b, p + side + "UpLeg", 0, dir * 0.09f * h, 0.52f * h);
            int leg = Add(b, p + side + "Leg", upLeg, dir * 0.09f * h, 0.28f * h);
            int foot = Add(b, p + side + "Foot", leg, dir * 0.09f * h, 0.05f * h);
            Add(b, p + side + "ToeBase", foot, dir * 0.09f * h, 0.02f * h);
        }

        private static int Add(List<RigBone> list, string name, int parent, float x, float y)
        {
            RigBone bone = new RigBone();
            bone.Name = name;
            bone.Parent = parent;
            bone.X = x;
            bone.Y = y;
            bone.Z = 0;
            list.Add(bone);
            return list.Count - 1;
        }

        /* Peso por distância ao segmento do osso; até 4 ossos por vértice. */
        public static List<int>[] Skinning(G3 mesh, List<RigBone> bones, out List<float>[] weights)
        {
            int n = mesh.P.Count / 3;
            List<int>[] idx = new List<int>[bones.Count];
            weights = new List<float>[bones.Count];
            for (int i = 0; i < bones.Count; i++)
            {
                idx[i] = new List<int>();
                weights[i] = new List<float>();
            }

            float[] best = new float[4];
            int[] bestBone = new int[4];

            for (int v = 0; v < n; v++)
            {
                float vx = mesh.P[v * 3], vy = mesh.P[v * 3 + 1], vz = mesh.P[v * 3 + 2];

                for (int k = 0; k < 4; k++) { best[k] = float.MaxValue; bestBone[k] = -1; }

                for (int i = 0; i < bones.Count; i++)
                {
                    RigBone bi = bones[i];
                    float ax = bi.X, ay = bi.Y, az = bi.Z;
                    RigBone par = bi.Parent >= 0 ? bones[bi.Parent] : null;
                    float bx = par != null ? par.X : ax;
                    float by = par != null ? par.Y : ay + 0.05f;
                    float bz = par != null ? par.Z : az;

                    float d = DistToSegment(vx, vy, vz, ax, ay, az, bx, by, bz);

                    for (int k = 0; k < 4; k++)
                    {
                        if (d < best[k])
                        {
                            for (int j = 3; j > k; j--) { best[j] = best[j - 1]; bestBone[j] = bestBone[j - 1]; }
                            best[k] = d;
                            bestBone[k] = i;
                            break;
                        }
                    }
                }

                float total = 0;
                float[] w = new float[4];
                for (int k = 0; k < 4; k++)
                {
                    if (bestBone[k] < 0) continue;
                    float d = best[k];
                    w[k] = 1f / (d * d + 1e-6f);
                    total += w[k];
                }
                if (total <= 0) continue;

                for (int k = 0; k < 4; k++)
                {
                    if (bestBone[k] < 0 || w[k] <= 0) continue;
                    idx[bestBone[k]].Add(v);
                    weights[bestBone[k]].Add(w[k] / total);
                }
            }

            return idx;
        }

        private static float DistToSegment(float px, float py, float pz,
                                           float ax, float ay, float az,
                                           float bx, float by, float bz)
        {
            float vx = bx - ax, vy = by - ay, vz = bz - az;
            float wx = px - ax, wy = py - ay, wz = pz - az;
            float len2 = vx * vx + vy * vy + vz * vz;
            float t = len2 <= 1e-9f ? 0 : (wx * vx + wy * vy + wz * vz) / len2;
            if (t < 0) t = 0; else if (t > 1) t = 1;
            float dx = wx - vx * t, dy = wy - vy * t, dz = wz - vz * t;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }

}
