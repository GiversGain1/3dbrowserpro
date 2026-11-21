using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace BP3D
{
    /* Conversão GLB -> glTF autocontido (buffer binário em base64). */
    internal static class Glb
    {
        private static readonly JavaScriptSerializer Js = new JavaScriptSerializer();
        static Glb() { Js.MaxJsonLength = int.MaxValue; }

        public static string ToGltfJson(byte[] buffer)
        {
            if (buffer == null || buffer.Length < 20) throw new Exception("GLB inválido");
            uint magic = BitConverter.ToUInt32(buffer, 0);
            if (magic != 0x46546C67) throw new Exception("GLB inválido");
            uint version = BitConverter.ToUInt32(buffer, 4);
            if (version != 2) throw new Exception("só GLB versão 2");

            if (BitConverter.ToUInt32(buffer, 8) != buffer.Length)
                throw new InvalidDataException("O tamanho declarado pelo GLB não corresponde ao arquivo.");

            int offset = 12;
            byte[] bin = null;
            string jsonText = null;

            while (offset + 8 <= buffer.Length)
            {
                uint len = BitConverter.ToUInt32(buffer, offset);
                uint type = BitConverter.ToUInt32(buffer, offset + 4);
                int start = offset + 8;
                if (len % 4 != 0 || len > buffer.Length - start)
                    throw new InvalidDataException("Bloco GLB incompleto ou desalinhado.");
                if (offset == 12 && type != 0x4E4F534A)
                    throw new InvalidDataException("O primeiro bloco do GLB deve ser JSON.");

                if (type == 0x4E4F534A) jsonText = Encoding.UTF8.GetString(buffer, start, (int)len);
                else if (type == 0x004E4942)
                {
                    bin = new byte[len];
                    Array.Copy(buffer, start, bin, 0, (int)len);
                }

                offset = start + (int)len;
            }

            if (offset != buffer.Length) throw new InvalidDataException("Bloco GLB truncado.");
            if (jsonText == null) throw new Exception("chunk JSON não encontrado");

            Dictionary<string, object> root = Js.DeserializeObject(jsonText) as Dictionary<string, object>;
            if (root == null) throw new Exception("JSON do GLB inválido");

            if (bin != null)
            {
                string uri = "data:application/octet-stream;base64," + Convert.ToBase64String(bin);
                object rawBuffers = root.ContainsKey("buffers") ? root["buffers"] : null;
                object[] arr = rawBuffers as object[];
                ArrayList list = rawBuffers as ArrayList;
                Dictionary<string, object> b0 = null;
                int count = arr != null ? arr.Length : (list != null ? list.Count : 0);
                if (count > 0) b0 = (arr != null ? arr[0] : list[0]) as Dictionary<string, object>;

                if (b0 == null)
                {
                    Dictionary<string, object> nb = new Dictionary<string, object>();
                    nb["byteLength"] = bin.Length;
                    nb["uri"] = uri;
                    ArrayList created = new ArrayList();
                    created.Add(nb);
                    root["buffers"] = created;
                }
                else
                {
                    b0["uri"] = uri;
                }
            }

            return Js.Serialize(root);
        }
    }

}
