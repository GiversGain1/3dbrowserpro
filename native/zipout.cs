// 3D Browser Pro — zip deflate em memória (FBX + modelo original num pacote só).
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;

namespace BP3D
{
    internal static class ZipOut
    {
        public static byte[] Build(List<string> names, List<byte[]> files)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (ZipArchive zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    for (int i = 0; i < names.Count && i < files.Count; i++)
                    {
                        if (files[i] == null) continue;
                        ZipArchiveEntry e = zip.CreateEntry(names[i]);
                        using (Stream s = e.Open())
                        {
                            s.Write(files[i], 0, files[i].Length);
                        }
                    }
                }
                return ms.ToArray();
            }
        }
    }
}
