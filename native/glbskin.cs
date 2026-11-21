using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BP3D
{
    /* Lê o objeto 3D, deduz o esqueleto e devolve um GLB NOVO já com ossos e skin:
       nós dos ossos, skin com inverseBindMatrices, atributos JOINTS_0/WEIGHTS_0.
       É o que o Blender faz com "auto rig": o arquivo baixado já vem com esqueleto. */
    internal static class GlbSkin
    {
        private const int ARRAY_BUFFER = 34962;
        private const int ELEMENT_ARRAY = 34963;

        /* O arquivo já veio com esqueleto? Então não reescrevemos (o do autor manda). */
        public static bool HasSkin(byte[] src)
        {
            try
            {
                string json = JsonOf(src);
                return json != null && json.IndexOf("\"skins\"", StringComparison.Ordinal) >= 0;
            }
            catch (Exception) { return false; }
        }

        private static string JsonOf(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 12) return null;

            uint magic = BitConverter.ToUInt32(bytes, 0);
            if (magic != 0x46546C67) return Encoding.UTF8.GetString(bytes);

            int off = 12;
            while (off + 8 <= bytes.Length)
            {
                uint len = BitConverter.ToUInt32(bytes, off);
                uint type = BitConverter.ToUInt32(bytes, off + 4);
                int start = off + 8;
                if (len == 0 || start + (int)len > bytes.Length) break;
                if (type == 0x4E4F534A) return Encoding.UTF8.GetString(bytes, start, (int)len);
                off = start + (int)len;
                while (off % 4 != 0) off++;
            }
            return null;
        }

        public static byte[] Apply(byte[] src, out string info)
        {
            Program.RigData d = Program.RigOf(src);
            return Build(d, out info);
        }

        /* Esqueleto desenhado à mão no visualizador: usa esses ossos em vez do rig automático. */
        public static byte[] Apply(byte[] src, List<RigBone> custom, out string info)
        {
            if (custom == null || custom.Count == 0) return Apply(src, out info);

            Program.RigData d = Program.RigOf(src);
            d.Bones = custom;
            d.Kind = "desenhado";
            return Build(d, out info);
        }

        public static byte[] Build(Program.RigData d, out string info)
        {
            List<RigBone> bones = d.Bones;
            G3 mesh = d.Mesh;
            int verts = mesh.P.Count / 3;

            if (verts == 0) throw new Exception("malha vazia");
            if (bones == null || bones.Count == 0) throw new Exception("sem esqueleto");
            if (mesh.I.Count == 0) throw new Exception("sem triângulos");

            info = bones.Count + " osso(s) [" + d.Kind + "] · " + verts + " vértices";

            /* ---------- pesos por vértice (4 maiores, normalizados) ---------- */
            List<float>[] boneWeights;
            List<int>[] boneVerts = Rig.Skinning(mesh, bones, out boneWeights);

            List<int>[] vBones = new List<int>[verts];
            List<float>[] vWeights = new List<float>[verts];
            for (int b = 0; b < boneVerts.Length; b++)
            {
                for (int k = 0; k < boneVerts[b].Count; k++)
                {
                    int v = boneVerts[b][k];
                    if (v < 0 || v >= verts) continue;
                    if (vBones[v] == null) { vBones[v] = new List<int>(4); vWeights[v] = new List<float>(4); }
                    vBones[v].Add(b);
                    vWeights[v].Add(boneWeights[b][k]);
                }
            }

            ushort[] joints = new ushort[verts * 4];
            float[] weights = new float[verts * 4];

            for (int v = 0; v < verts; v++)
            {
                List<int> bi = vBones[v];
                List<float> bw = vWeights[v];
                int n = bi == null ? 0 : bi.Count;
                int slot = 0;

                if (n > 4)
                {
                    // mantém os 4 mais pesados
                    while (slot < 4)
                    {
                        int best = -1;
                        float bestW = -1f;
                        for (int k = 0; k < bi.Count; k++)
                        {
                            if (bw[k] > bestW) { bestW = bw[k]; best = k; }
                        }
                        if (best < 0) break;
                        joints[v * 4 + slot] = (ushort)bi[best];
                        weights[v * 4 + slot] = bw[best];
                        bw[best] = -1f;
                        slot++;
                    }
                    n = slot;
                }
                else
                {
                    for (int k = 0; k < n; k++)
                    {
                        joints[v * 4 + k] = (ushort)bi[k];
                        weights[v * 4 + k] = bw[k];
                    }
                }

                // normaliza; vértice órfão fica preso na raiz
                float sum = 0;
                for (int k = 0; k < 4; k++) sum += weights[v * 4 + k];
                if (sum <= 1e-6f)
                {
                    joints[v * 4] = 0;
                    weights[v * 4] = 1f;
                    for (int k = 1; k < 4; k++) { joints[v * 4 + k] = 0; weights[v * 4 + k] = 0f; }
                }
                else
                {
                    for (int k = 0; k < 4; k++) weights[v * 4 + k] /= sum;
                }
            }

            /* ---------- buffers binários ---------- */
            Bin bin = new Bin();
            List<string> views = new List<string>();
            List<string> accessors = new List<string>();

            float[] pos = mesh.P.ToArray();
            float[] min, max;
            mesh.Bounds(out min, out max);

            int vPos = AddView(bin, Floats(pos), ARRAY_BUFFER, views);
            string mm = ",\"min\":[" + N(min[0]) + "," + N(min[1]) + "," + N(min[2]) + "]" +
                        ",\"max\":[" + N(max[0]) + "," + N(max[1]) + "," + N(max[2]) + "]";
            int aPos = AddAccessor(accessors, vPos, 5126, verts, "VEC3", mm);

            int aNrm = -1;
            if (mesh.N.Count == pos.Length && pos.Length > 0)
            {
                int vNrm = AddView(bin, Floats(mesh.N.ToArray()), ARRAY_BUFFER, views);
                aNrm = AddAccessor(accessors, vNrm, 5126, verts, "VEC3", "");
            }

            int aUv = -1;
            if (mesh.T.Count == verts * 2 && verts > 0)
            {
                int vUv = AddView(bin, Floats(mesh.T.ToArray()), ARRAY_BUFFER, views);
                aUv = AddAccessor(accessors, vUv, 5126, verts, "VEC2", "");
            }

            int vJ = AddView(bin, UShorts(joints), ARRAY_BUFFER, views);
            int aJ = AddAccessor(accessors, vJ, 5123, verts, "VEC4", "");

            int vW = AddView(bin, Floats(weights), ARRAY_BUFFER, views);
            int aW = AddAccessor(accessors, vW, 5126, verts, "VEC4", "");

            int[] tri = mesh.I.ToArray();
            bool wide = verts > 65535;
            int vI = AddView(bin, wide ? UInts(tri) : UShorts(tri), ELEMENT_ARRAY, views);
            int aI = AddAccessor(accessors, vI, wide ? 5125 : 5123, tri.Length, "SCALAR", "");

            // inverseBindMatrices: o rig é só de juntas (sem rotação/escama), então o
            // inverso da pose de bind é transladar pelo oposto da posição global do osso
            float[] ibm = new float[bones.Count * 16];
            for (int i = 0; i < bones.Count; i++)
            {
                RigBone b = bones[i];
                int o = i * 16;
                ibm[o + 0] = 1f;
                ibm[o + 5] = 1f;
                ibm[o + 10] = 1f;
                ibm[o + 15] = 1f;
                ibm[o + 12] = -b.X;
                ibm[o + 13] = -b.Y;
                ibm[o + 14] = -b.Z;
            }
            int vIbm = AddView(bin, Floats(ibm), 0, views);
            int aIbm = AddAccessor(accessors, vIbm, 5126, bones.Count, "MAT4", "");

            bin.Align4();

            /* ---------- JSON ---------- */
            StringBuilder nodes = new StringBuilder();
            List<int> sceneRoots = new List<int>();

            for (int i = 0; i < bones.Count; i++)
            {
                RigBone b = bones[i];
                RigBone p = b.Parent >= 0 && b.Parent < bones.Count ? bones[b.Parent] : null;

                float lx = p == null ? b.X : b.X - p.X;
                float ly = p == null ? b.Y : b.Y - p.Y;
                float lz = p == null ? b.Z : b.Z - p.Z;

                StringBuilder kids = new StringBuilder();
                for (int j = 0; j < bones.Count; j++)
                {
                    if (bones[j].Parent != i) continue;
                    if (kids.Length > 0) kids.Append(',');
                    kids.Append(j);
                }

                if (nodes.Length > 0) nodes.Append(',');
                nodes.Append("{\"name\":\"").Append(Esc(b.Name)).Append("\",\"translation\":[")
                     .Append(N(lx)).Append(',').Append(N(ly)).Append(',').Append(N(lz)).Append(']')
                     .Append(kids.Length > 0 ? ",\"children\":[" + kids + "]" : "")
                     .Append('}');

                if (p == null) sceneRoots.Add(i);
            }

            int meshNode = bones.Count;
            nodes.Append(',')
                 .Append("{\"name\":\"Modelo\",\"mesh\":0,\"skin\":0}")
                 ;

            StringBuilder acc = new StringBuilder();
            for (int i = 0; i < accessors.Count; i++)
            {
                if (i > 0) acc.Append(',');
                acc.Append(accessors[i]);
            }

            StringBuilder vw = new StringBuilder();
            for (int i = 0; i < views.Count; i++)
            {
                if (i > 0) vw.Append(',');
                vw.Append(views[i]);
            }

            StringBuilder attrs = new StringBuilder();
            attrs.Append("\"POSITION\":").Append(aPos);
            if (aNrm >= 0) attrs.Append(",\"NORMAL\":").Append(aNrm);
            if (aUv >= 0) attrs.Append(",\"TEXCOORD_0\":").Append(aUv);
            attrs.Append(",\"JOINTS_0\":").Append(aJ);
            attrs.Append(",\"WEIGHTS_0\":").Append(aW);

            StringBuilder jointList = new StringBuilder();
            for (int i = 0; i < bones.Count; i++)
            {
                if (i > 0) jointList.Append(',');
                jointList.Append(i);
            }

            StringBuilder roots = new StringBuilder();
            for (int i = 0; i < sceneRoots.Count; i++)
            {
                if (i > 0) roots.Append(',');
                roots.Append(sceneRoots[i]);
            }

            StringBuilder rootNodes = new StringBuilder(roots.ToString());
            if (rootNodes.Length > 0) rootNodes.Append(',');
            rootNodes.Append(meshNode);

            StringBuilder json = new StringBuilder();
            json.Append("{\"asset\":{\"version\":\"2.0\",\"generator\":\"3D Browser Pro — esqueleto automático\"}");
            json.Append(",\"scene\":0");
            json.Append(",\"scenes\":[{\"name\":\"Cena\",\"nodes\":[").Append(rootNodes).Append("]}]");
            json.Append(",\"nodes\":[").Append(nodes).Append(']');
            json.Append(",\"meshes\":[{\"name\":\"Malha\",\"primitives\":[{");
            json.Append("\"attributes\":{").Append(attrs).Append("}");
            json.Append(",\"indices\":").Append(aI);
            json.Append(",\"material\":0,\"mode\":4}]}]");
            json.Append(",\"materials\":[{\"name\":\"material\",\"doubleSided\":true,");
            json.Append("\"pbrMetallicRoughness\":{\"baseColorFactor\":[0.82,0.82,0.86,1],\"metallicFactor\":0,\"roughnessFactor\":0.85}}]");
            json.Append(",\"skins\":[{\"name\":\"Armature\",\"joints\":[").Append(jointList).Append(']');
            json.Append(",\"inverseBindMatrices\":").Append(aIbm);
            json.Append(",\"skeleton\":").Append(sceneRoots.Count > 0 ? sceneRoots[0] : 0).Append("}]");
            json.Append(",\"accessors\":[").Append(acc).Append(']');
            json.Append(",\"bufferViews\":[").Append(vw).Append(']');
            json.Append(",\"buffers\":[{\"byteLength\":").Append(bin.Length).Append("}]");
            json.Append('}');

            return Glb(json.ToString(), bin.ToArray());
        }

        /* ---------- montagem do container GLB ---------- */

        private static byte[] Glb(string json, byte[] binBytes)
        {
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            int jsonPadded = (jsonBytes.Length + 3) / 4 * 4;
            int binPadded = (binBytes.Length + 3) / 4 * 4;
            int total = 12 + 8 + jsonPadded + 8 + binPadded;

            byte[] outBytes = new byte[total];
            int o = 0;

            PutU32(outBytes, ref o, 0x46546C67);   // "glTF"
            PutU32(outBytes, ref o, 2);
            PutU32(outBytes, ref o, (uint)total);

            PutU32(outBytes, ref o, (uint)jsonPadded);
            PutU32(outBytes, ref o, 0x4E4F534A);   // "JSON"
            Array.Copy(jsonBytes, 0, outBytes, o, jsonBytes.Length);
            for (int i = jsonBytes.Length; i < jsonPadded; i++) outBytes[o + i] = 0x20;   // espaço
            o += jsonPadded;

            PutU32(outBytes, ref o, (uint)binPadded);
            PutU32(outBytes, ref o, 0x004E4942);   // "BIN"
            Array.Copy(binBytes, 0, outBytes, o, binBytes.Length);
            o += binPadded;

            return outBytes;
        }

        private static void PutU32(byte[] buf, ref int o, uint v)
        {
            buf[o++] = (byte)(v & 0xFF);
            buf[o++] = (byte)((v >> 8) & 0xFF);
            buf[o++] = (byte)((v >> 16) & 0xFF);
            buf[o++] = (byte)((v >> 24) & 0xFF);
        }

        /* ---------- ajudantes ---------- */

        private class Bin
        {
            private readonly MemoryStream S = new MemoryStream();

            public int Length { get { return (int)S.Length; } }

            public void Align4()
            {
                while (S.Length % 4 != 0) S.WriteByte(0);
            }

            public void Push(byte[] data, out int offset, out int length)
            {
                Align4();
                offset = (int)S.Length;
                length = data.Length;
                S.Write(data, 0, data.Length);
            }

            public byte[] ToArray()
            {
                byte[] b = S.ToArray();
                int padded = (b.Length + 3) / 4 * 4;
                if (padded == b.Length) return b;
                byte[] p = new byte[padded];
                Array.Copy(b, p, b.Length);
                return p;
            }
        }

        private static int AddView(Bin bin, byte[] data, int target, List<string> views)
        {
            int offset, length;
            bin.Push(data, out offset, out length);

            string v = "{\"buffer\":0,\"byteOffset\":" + offset + ",\"byteLength\":" + length +
                       (target > 0 ? ",\"target\":" + target : "") + "}";
            views.Add(v);
            return views.Count - 1;
        }

        private static int AddAccessor(List<string> acc, int view, int componentType, int count, string type, string extra)
        {
            acc.Add("{\"bufferView\":" + view + ",\"componentType\":" + componentType +
                    ",\"count\":" + count + ",\"type\":\"" + type + "\"" + extra + "}");
            return acc.Count - 1;
        }

        private static byte[] Floats(float[] v)
        {
            byte[] b = new byte[v.Length * 4];
            for (int i = 0; i < v.Length; i++)
            {
                byte[] f = BitConverter.GetBytes(v[i]);
                b[i * 4] = f[0]; b[i * 4 + 1] = f[1]; b[i * 4 + 2] = f[2]; b[i * 4 + 3] = f[3];
            }
            return b;
        }

        private static byte[] UShorts(ushort[] v)
        {
            byte[] b = new byte[v.Length * 2];
            for (int i = 0; i < v.Length; i++)
            {
                b[i * 2] = (byte)(v[i] & 0xFF);
                b[i * 2 + 1] = (byte)((v[i] >> 8) & 0xFF);
            }
            return b;
        }

        private static byte[] UShorts(int[] v)
        {
            byte[] b = new byte[v.Length * 2];
            for (int i = 0; i < v.Length; i++)
            {
                b[i * 2] = (byte)(v[i] & 0xFF);
                b[i * 2 + 1] = (byte)((v[i] >> 8) & 0xFF);
            }
            return b;
        }

        private static byte[] UInts(int[] v)
        {
            byte[] b = new byte[v.Length * 4];
            for (int i = 0; i < v.Length; i++)
            {
                b[i * 4] = (byte)(v[i] & 0xFF);
                b[i * 4 + 1] = (byte)((v[i] >> 8) & 0xFF);
                b[i * 4 + 2] = (byte)((v[i] >> 16) & 0xFF);
                b[i * 4 + 3] = (byte)((v[i] >> 24) & 0xFF);
            }
            return b;
        }

        private static string N(float v)
        {
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "/").Replace("\"", "'");
        }
    }
}
