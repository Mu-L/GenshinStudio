using Org.Brotli.Dec;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace AssetStudio
{
    public static class ImportHelper
    {
        public static void MergeSplitAssets(string path, bool allDirectories = false)
        {
            var splitFiles = Directory.GetFiles(path, "*.split0", allDirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            foreach (var splitFile in splitFiles)
            {
                var destFile = Path.GetFileNameWithoutExtension(splitFile);
                var destPath = Path.GetDirectoryName(splitFile);
                var destFull = Path.Combine(destPath, destFile);
                if (!File.Exists(destFull))
                {
                    var splitParts = Directory.GetFiles(destPath, destFile + ".split*");
                    using (var destStream = File.Create(destFull))
                    {
                        for (int i = 0; i < splitParts.Length; i++)
                        {
                            var splitPart = destFull + ".split" + i;
                            using (var sourceStream = File.OpenRead(splitPart))
                            {
                                sourceStream.CopyTo(destStream);
                            }
                        }
                    }
                }
            }
        }

        public static string[] ProcessingSplitFiles(List<string> selectFile)
        {
            var splitFiles = selectFile.Where(x => x.Contains(".split"))
                .Select(x => Path.Combine(Path.GetDirectoryName(x), Path.GetFileNameWithoutExtension(x)))
                .Distinct()
                .ToList();
            selectFile.RemoveAll(x => x.Contains(".split"));
            foreach (var file in splitFiles)
            {
                if (File.Exists(file))
                {
                    selectFile.Add(file);
                }
            }
            return selectFile.Distinct().ToArray();
        }

        public static FileReader DecompressGZip(FileReader reader)
        {
            using (reader)
            {
                var stream = new MemoryStream();
                using (var gs = new GZipStream(reader.BaseStream, CompressionMode.Decompress))
                {
                    gs.CopyTo(stream);
                }
                stream.Position = 0;
                return new FileReader(reader.FullPath, stream);
            }
        }

        public static FileReader DecompressBrotli(FileReader reader)
        {
            using (reader)
            {
                var stream = new MemoryStream();
                using (var brotliStream = new BrotliInputStream(reader.BaseStream))
                {
                    brotliStream.CopyTo(stream);
                }
                stream.Position = 0;
                return new FileReader(reader.FullPath, stream);
            }
        }

        public static string GetRelativePath(string fromPath, string toPath)
        {
            if (string.IsNullOrEmpty(fromPath))
            {
                throw new ArgumentNullException("fromPath");
            }

            if (string.IsNullOrEmpty(toPath))
            {
                throw new ArgumentNullException("toPath");
            }

            Uri fromUri = new Uri(AppendDirectorySeparatorChar(fromPath));
            Uri toUri = new Uri(AppendDirectorySeparatorChar(toPath));

            if (fromUri.Scheme != toUri.Scheme)
            {
                return toPath;
            }

            Uri relativeUri = fromUri.MakeRelativeUri(toUri);
            string relativePath = Uri.UnescapeDataString(relativeUri.ToString());

            if (string.Equals(toUri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
            {
                relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            }

            return relativePath;
        }

        private static string AppendDirectorySeparatorChar(string path)
        {
            if (!Path.HasExtension(path) &&
                !path.Contains("CAB-") &&
                !path.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                return path + Path.DirectorySeparatorChar;
            }

            return path;
        }
    }
}
