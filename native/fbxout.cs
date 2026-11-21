// 3D Browser Pro — escritor de FBX ASCII 7.4 (malha + skin opcional).
// IMPORTANTE: o parser de FBX do three.js é indentado por TAB (^\t{N}), então
// todo nível de aninhamento usa TAB, com a chave de fechamento no nível do pai.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BP3D
{
    internal static class Fbx
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static void L(StringBuilder s, int tabs, string text)
        {
            for (int i = 0; i < tabs; i++) s.Append('\t');
            s.Append(text).Append('\n');
        }

        public static byte[] Build(G3 mesh, string name, List<RigBone> bones)
        {
            StringBuilder s = new StringBuilder(1 << 20);
            string safeName = (name ?? "modelo").Replace("\"", "'");

            int boneCount = bones == null ? 0 : bones.Count;
            bool useSkin = boneCount > 0;

            // O cabeçalho precisa escapar da checagem isFbxFormatASCII do three.js, que lê
            // os caracteres 0,1,3,6,10,15,21,28,...,91 e compara com "Kaydara FBX Binary\\".
            // É por isso que o segundo comentário é uma linha de traços, como no Blender.
            L(s, 0, "; FBX 7.4.0 project file");
            L(s, 0, "; --------------------------------------------------------------------");
            L(s, 0, "");

            L(s, 0, "FBXHeaderExtension:  {");
            L(s, 1, "FBXHeaderVersion: 1003");
            L(s, 1, "FBXVersion: 7400");
            L(s, 1, "Creator: \"3D Browser Pro\"");
            L(s, 0, "}");

            L(s, 0, "GlobalSettings:  {");
            L(s, 1, "Version: 1000");
            L(s, 1, "Properties70:  {");
            L(s, 2, "P: \"UpAxis\", \"int\", \"Integer\", \"\",1");
            L(s, 2, "P: \"UpAxisSign\", \"int\", \"Integer\", \"\",1");
            L(s, 2, "P: \"FrontAxis\", \"int\", \"Integer\", \"\",2");
            L(s, 2, "P: \"FrontAxisSign\", \"int\", \"Integer\", \"\",1");
            L(s, 2, "P: \"CoordAxis\", \"int\", \"Integer\", \"\",0");
            L(s, 2, "P: \"CoordAxisSign\", \"int\", \"Integer\", \"\",1");
            L(s, 2, "P: \"OriginalUpAxis\", \"int\", \"Integer\", \"\",1");
            L(s, 2, "P: \"UnitScaleFactor\", \"double\", \"Number\", \"\",1");
            L(s, 1, "}");
            L(s, 0, "}");

            int modelCount = 1 + (useSkin ? boneCount : 0);
            int deformers = useSkin ? 1 + boneCount : 0;

            L(s, 0, "Definitions:  {");
            L(s, 1, "Version: 100");
            L(s, 1, "Count: " + (modelCount + 1 + deformers + 1));
            L(s, 1, "ObjectType: \"GlobalSettings\" {");
            L(s, 2, "Count: 1");
            L(s, 1, "}");
            L(s, 1, "ObjectType: \"Model\" {");
            L(s, 2, "Count: " + modelCount);
            L(s, 1, "}");
            L(s, 1, "ObjectType: \"Geometry\" {");
            L(s, 2, "Count: 1");
            L(s, 1, "}");
            if (useSkin)
            {
                L(s, 1, "ObjectType: \"Deformer\" {");
                L(s, 2, "Count: " + deformers);
                L(s, 1, "}");
            }
            L(s, 0, "}");

            L(s, 0, "Objects:  {");
            WriteGeometry(s, mesh, 100000, safeName);
            WriteModel(s, 100001, safeName, "Mesh");

            if (useSkin)
            {
                L(s, 1, "Deformer: 100002, \"Deformer::Skin\", \"Skin\" {");
                L(s, 2, "Version: 101");
                L(s, 2, "Link_DeformAcuracy: 50");
                L(s, 1, "}");

                List<float>[] weights;
                List<int>[] perBone = Rig.Skinning(mesh, bones, out weights);

                for (int i = 0; i < boneCount; i++)
                {
                    WriteBoneModel(s, 200000 + i, bones, i);
                    WriteCluster(s, 300000 + i, bones[i].Name, perBone[i], weights[i], bones[i]);
                }
            }

            L(s, 0, "}");

            L(s, 0, "Connections:  {");
            L(s, 1, "C: \"OO\",100001,0");
            L(s, 1, "C: \"OO\",100000,100001");

            if (useSkin)
            {
                L(s, 1, "C: \"OO\",100002,100001");
                for (int i = 0; i < boneCount; i++)
                {
                    int boneId = 200000 + i;
                    int clusterId = 300000 + i;
                    L(s, 1, "C: \"OO\"," + clusterId + ",100002");
                    // osso ligado ao próprio cluster: é por aqui que o loader acha o esqueleto
                    L(s, 1, "C: \"OO\"," + boneId + "," + clusterId);
                    L(s, 1, "C: \"OO\"," + boneId + "," +
                             (bones[i].Parent >= 0 ? (200000 + bones[i].Parent).ToString(Inv) : "0"));
                }
            }

            L(s, 0, "}");

            L(s, 0, "Takes:  {");
            L(s, 1, "Current: \"\"");
            L(s, 0, "}");

            return Encoding.ASCII.GetBytes(s.ToString());
        }

        private static void WriteGeometry(StringBuilder s, G3 mesh, int id, string name)
        {
            L(s, 1, "Geometry: " + id + ", \"Geometry::" + name + "\", \"Mesh\" {");

            L(s, 2, "Vertices: *" + mesh.P.Count + " {");
            L(s, 3, "a: " + Floats(mesh.P));
            L(s, 2, "}");

            L(s, 2, "PolygonVertexIndex: *" + mesh.I.Count + " {");
            StringBuilder idx = new StringBuilder(mesh.I.Count * 6);
            for (int i = 0; i < mesh.I.Count; i++)
            {
                if (i > 0) idx.Append(',');
                // último vértice de cada face vai negado (~i)
                idx.Append((i % 3) == 2 ? (~mesh.I[i]).ToString(Inv) : mesh.I[i].ToString(Inv));
            }
            L(s, 3, "a: " + idx);
            L(s, 2, "}");

            L(s, 2, "GeometryVersion: 124");

            L(s, 2, "LayerElementNormal: 0 {");
            L(s, 3, "Version: 101");
            L(s, 3, "Name: \"\"");
            L(s, 3, "MappingInformationType: \"ByPolygonVertex\"");
            L(s, 3, "ReferenceInformationType: \"Direct\"");
            L(s, 3, "Normals: *" + (mesh.I.Count * 3) + " {");
            StringBuilder nrm = new StringBuilder(mesh.I.Count * 24);
            for (int i = 0; i < mesh.I.Count; i++)
            {
                if (i > 0) nrm.Append(',');
                int v = mesh.I[i] * 3;
                nrm.Append(N(mesh.N[v])).Append(',').Append(N(mesh.N[v + 1])).Append(',').Append(N(mesh.N[v + 2]));
            }
            L(s, 4, "a: " + nrm);
            L(s, 3, "}");
            L(s, 2, "}");

            L(s, 2, "LayerElementUV: 0 {");
            L(s, 3, "Version: 101");
            L(s, 3, "Name: \"UVChannel_1\"");
            L(s, 3, "MappingInformationType: \"ByPolygonVertex\"");
            L(s, 3, "ReferenceInformationType: \"IndexToDirect\"");
            L(s, 3, "UV: *" + (mesh.I.Count * 2) + " {");
            StringBuilder uvs = new StringBuilder(mesh.I.Count * 16);
            for (int i = 0; i < mesh.I.Count; i++)
            {
                if (i > 0) uvs.Append(',');
                int v = mesh.I[i] * 2;
                uvs.Append(N(mesh.T[v])).Append(',').Append(N(mesh.T[v + 1]));
            }
            L(s, 4, "a: " + uvs);
            L(s, 3, "}");
            L(s, 3, "UVIndex: *" + mesh.I.Count + " {");
            StringBuilder uvi = new StringBuilder(mesh.I.Count * 6);
            for (int i = 0; i < mesh.I.Count; i++)
            {
                if (i > 0) uvi.Append(',');
                uvi.Append(i.ToString(Inv));
            }
            L(s, 4, "a: " + uvi);
            L(s, 3, "}");
            L(s, 2, "}");

            L(s, 2, "Layer: 0 {");
            L(s, 3, "Version: 100");
            L(s, 3, "LayerElement:  {");
            L(s, 4, "Type: \"LayerElementNormal\"");
            L(s, 4, "TypedIndex: 0");
            L(s, 3, "}");
            L(s, 3, "LayerElement:  {");
            L(s, 4, "Type: \"LayerElementUV\"");
            L(s, 4, "TypedIndex: 0");
            L(s, 3, "}");
            L(s, 2, "}");

            L(s, 1, "}");
        }

        private static void WriteModel(StringBuilder s, int id, string name, string type)
        {
            L(s, 1, "Model: " + id + ", \"Model::" + name + "\", \"" + type + "\" {");
            L(s, 2, "Version: 232");
            L(s, 2, "Properties70:  {");
            L(s, 3, "P: \"DefaultAttributeIndex\", \"int\", \"Integer\", \"\",0");
            L(s, 2, "}");
            if (type == "Mesh")
            {
                L(s, 2, "Shading: T");
                L(s, 2, "Culling: \"CullingOff\"");
            }
            L(s, 1, "}");
        }

        private static void WriteBoneModel(StringBuilder s, int id, List<RigBone> bones, int i)
        {
            RigBone bone = bones[i];
            float px = bone.Parent >= 0 ? bones[bone.Parent].X : 0;
            float py = bone.Parent >= 0 ? bones[bone.Parent].Y : 0;
            float pz = bone.Parent >= 0 ? bones[bone.Parent].Z : 0;

            L(s, 1, "Model: " + id + ", \"Model::" + bone.Name.Replace("\"", "'") + "\", \"LimbNode\" {");
            L(s, 2, "Version: 232");
            L(s, 2, "Properties70:  {");
            L(s, 3, "P: \"InheritType\", \"enum\", \"\", \"\",1");
            L(s, 3, "P: \"Lcl Translation\", \"Lcl Translation\", \"\", \"A\"," +
                    N(bone.X - px) + "," + N(bone.Y - py) + "," + N(bone.Z - pz));
            L(s, 3, "P: \"Lcl Rotation\", \"Lcl Rotation\", \"\", \"A\",0,0,0");
            L(s, 3, "P: \"Lcl Scaling\", \"Lcl Scaling\", \"\", \"A\",1,1,1");
            L(s, 2, "}");
            L(s, 1, "}");
        }

        private static void WriteCluster(StringBuilder s, int id, string boneName,
                                         List<int> indexes, List<float> weights, RigBone bone)
        {
            L(s, 1, "Deformer: " + id + ", \"SubDeformer::" + boneName.Replace("\"", "'") + "\", \"Cluster\" {");
            L(s, 2, "Version: 100");
            L(s, 2, "UserData: \"\"");

            StringBuilder ids = new StringBuilder(indexes.Count * 6);
            for (int i = 0; i < indexes.Count; i++)
            {
                if (i > 0) ids.Append(',');
                ids.Append(indexes[i].ToString(Inv));
            }
            L(s, 2, "Indexes: *" + indexes.Count + " {");
            L(s, 3, "a: " + ids);
            L(s, 2, "}");

            StringBuilder w = new StringBuilder(weights.Count * 8);
            for (int i = 0; i < weights.Count; i++)
            {
                if (i > 0) w.Append(',');
                w.Append(N(weights[i]));
            }
            L(s, 2, "Weights: *" + weights.Count + " {");
            L(s, 3, "a: " + w);
            L(s, 2, "}");

            L(s, 2, "Transform: *16 {");
            L(s, 3, "a: " + Matrix(0, 0, 0));
            L(s, 2, "}");

            L(s, 2, "TransformLink: *16 {");
            L(s, 3, "a: " + Matrix(bone.X, bone.Y, bone.Z));
            L(s, 2, "}");

            L(s, 1, "}");
        }

        private static string Floats(List<float> list)
        {
            StringBuilder sb = new StringBuilder(list.Count * 8);
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(N(list[i]));
            }
            return sb.ToString();
        }

        /* coluna-major; translação em 12..14, como o FBX espera */
        private static string Matrix(float x, float y, float z)
        {
            return "1,0,0,0,0,1,0,0,0,0,1,0," + N(x) + "," + N(y) + "," + N(z) + ",1";
        }

        private static string N(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) v = 0;
            string t = v.ToString("0.######", Inv);
            return t == "-0" ? "0" : t;
        }
    }
}
