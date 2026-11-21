using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BP3D
{
    internal static partial class Program
    {

        private static bool RunCli(string[] args)
        {
            if (args.Length == 0 || !args[0].StartsWith("--", StringComparison.Ordinal)) return false;

            Paths.EnsureData();
            string command = args[0];
            if (command == "--conv" && args.Length == 4) CliConv(args);
            else if (command == "--skin" && args.Length >= 2 && args.Length <= 3) CliSkin(args);
            else if (command == "--fbx" && args.Length >= 2 && args.Length <= 4) CliFbx(args);
            else ReportCliError("Use --conv <formato> <entrada> <saída>, --skin <entrada> [saída] ou --fbx <entrada> [saída] [triângulos].");

            return true;
        }

        private static void WriteCliOutput(string input, string output, byte[] bytes)
        {
            if (string.Equals(Path.GetFullPath(input), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Use caminhos diferentes para o original e o arquivo gerado.");

            using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
                stream.Write(bytes, 0, bytes.Length);
        }

        private static void ReportCliError(string message)
        {
            Environment.ExitCode = 1;
            Console.Error.WriteLine(message);
            Log.W("Conversão falhou: " + message);
        }

        /* Converter o glb no formato escolhido no preview:
           "3D Browser Pro.exe" --conv obj modelo.glb modelo.obj */
        private static void CliConv(string[] args)
        {
            try
            {
                string fmt = args[1].Trim().TrimStart('.').ToLowerInvariant();
                string input = args[2];
                string output = args[3];
                byte[] src = File.ReadAllBytes(input);
                string baseName = Path.GetFileNameWithoutExtension(input);

                byte[] outBytes;
                string info;

                if (fmt == "gltf")
                {
                    outBytes = Encoding.UTF8.GetBytes(Glb.ToGltfJson(src));
                    info = "glTF com buffer binário incorporado";
                }
                else if (fmt == "glb") outBytes = GlbSkin.Apply(src, out info);
                else if (fmt == "fbx") outBytes = FbxFromGlbStatic(src, baseName, out info, null, 0);
                else if (fmt == "humanoid")
                {
                    float ox, oy, oz;
                    string detail;
                    List<RigBone> bones = HumanoidRig(src, out ox, out oy, out oz, out detail);
                    G3 m = GltfRead.Load(src);
                    float[] mn, mx;
                    m.Bounds(out mn, out mx);
                    for (int i = 0; i + 2 < m.P.Count; i += 3)
                    {
                        m.P[i] -= ox;
                        m.P[i + 1] -= oy;
                        m.P[i + 2] -= oz;
                    }
                    outBytes = Fbx.Build(m, baseName, bones);
                    info = bones.Count + " ossos (humanoide forçado)";
                }
                else
                {
                    G3 mesh = GltfRead.Load(src);
                    if (fmt == "obj") outBytes = MeshOut.Obj(mesh, baseName);
                    else if (fmt == "stl") outBytes = MeshOut.Stl(mesh, baseName);
                    else if (fmt == "ply") outBytes = MeshOut.Ply(mesh, baseName);
                    else throw new ArgumentException("Formato de conversão não suportado: " + fmt);
                    info = mesh.Triangles + " triângulos (só geometria)";
                }

                WriteCliOutput(input, output, outBytes);
                Log.W("conv (cli): " + fmt + " · " + info + " · " + outBytes.Length + " B -> " + output);
            }
            catch (Exception ex) { ReportCliError(ex.Message); }
        }

        /* Aplicar esqueleto num glb e gravar outro glb já com skin:
           "3D Browser Pro.exe" --skin modelo.glb modelo_com_esqueleto.glb */
        private static void CliSkin(string[] args)
        {
            try
            {
                string input = args[1];
                string output = args.Length >= 3 ? args[2] : Path.Combine(
                    Path.GetDirectoryName(input), Path.GetFileNameWithoutExtension(input) + "_esqueleto.glb");
                string info;
                byte[] glb = GlbSkin.Apply(File.ReadAllBytes(input), out info);
                WriteCliOutput(input, output, glb);
                Log.W("skin (cli): " + info + " · " + glb.Length + " B -> " + output);
            }
            catch (Exception ex) { ReportCliError(ex.Message); }
        }

        private static void CliFbx(string[] args)
        {
            try
            {
                string input = args[1];
                string output = args.Length >= 3 ? args[2] : Path.ChangeExtension(input, ".fbx");
                // 4º argumento opcional: teto de triângulos (FBX leve), ex.: --fbx in.glb out.fbx 20000
                int maxTris = 0;
                if (args.Length == 4 && (!int.TryParse(args[3], out maxTris) || maxTris < 0))
                    throw new ArgumentException("O limite de triângulos deve ser um inteiro não negativo.");
                string info;
                byte[] fbx = FbxFromGlbStatic(File.ReadAllBytes(input), Path.GetFileNameWithoutExtension(input), out info, null, maxTris);
                WriteCliOutput(input, output, fbx);
                Log.W("fbx (cli): " + info + " -> " + output);
            }
            catch (Exception ex) { ReportCliError(ex.Message); }
        }

    }
}
