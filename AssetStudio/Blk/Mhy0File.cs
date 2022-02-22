using System;
using System.IO;
using System.Linq;
using System.Text;
using K4os.Compression.LZ4;

namespace AssetStudio
{
    public partial class Mhy0File
    {
        public class Header
        {
            public int headerSize;
            public byte[] headerData;
            public int bundleCount;
            public int blockCount;
        }

        public class StorageBlock
        {
            public int compressedSize;
            public int uncompressedSize;
        }

        public class Node
        {
            public long offset;
            public long size;
            public string path;
        }

        public Header m_Header;
        public long OriginalPos;
        private StorageBlock[] m_BlocksInfo;
        private Node[] m_DirectoryInfo;

        public StreamFile[] fileList;
        private void Scramble2(byte[] input, int offset)
        {
            byte[] indexScramble = new byte[] {
                0x0B,0x02,0x08,0x0C,0x01,0x05,0x00,0x0F,0x06,0x07,0x09,0x03,0x0D,0x04,0x0E,0x0A,
                0x04,0x05,0x07,0x0A,0x02,0x0F,0x0B,0x08,0x0E,0x0D,0x09,0x06,0x0C,0x03,0x00,0x01,
                0x08,0x00,0x0C,0x06,0x04,0x0B,0x07,0x09,0x05,0x03,0x0F,0x01,0x0D,0x0A,0x02,0x0E,
            };
            byte[] v20_1 = new byte[] {
                0x48, 0x14, 0x36, 0xED, 0x8E, 0x44, 0x5B, 0xB6
            };
            byte[] v25 = {
                0xA7, 0x99, 0x66, 0x50, 0xB9, 0x2D, 0xF0, 0x78
            };
            byte[] v20_0 = new byte[16];
            for (int v17 = 0; v17 < 3; v17++)
            {
                for (int i = 0; i < 16; ++i)
                    v20_0[i] = input[offset + indexScramble[32 + -16 * v17 + i]];
                Buffer.BlockCopy(v20_0, 0, input, offset, 16);
                for (int j = 0; j < 16; ++j)
                {
                    byte v14 = input[offset + j];
                    int v1 = j % 8;
                    if (v14 == 0 || v25[v1] == 0)
                        v14 = (byte)(BlkFile.KeyScrambleTable[j % 4 * 256] ^ v20_1[j % 8]);
                    else
                        v14 = (byte)(v20_1[v1] ^ BlkFile.KeyScrambleTable[j % 4 * 256 | Mhy0Table1[(Mhy0Table2[v25[v1]] + Mhy0Table2[v14]) % 255]]);
                    input[offset + j] = v14;
                }
            }
        }

        private void Scramble(byte[] input, int offset, ulong a2, ulong a4)
        {
            var v10 = (int)((a4 + 15) & 0xFFFFFFF0);
            for (int i = 0; i < v10; i += 16)
                Scramble2(input, offset + i + 4);
            for (int j = 0; j < 4; j++)
                input[offset + j] ^= input[offset + j + 4];
            ulong v8 = (ulong)v10 + 4;
            int v13 = 0;
            while (v8 < a2 && v13 == 0)
            {
                for (ulong k = 0; k < a4; ++k)
                {
                    input[(ulong)offset + k + v8] ^= input[(ulong)offset + k + 4];
                    if (k + v8 >= a2 - 1)
                    {
                        v13 = 1;
                        break;
                    }
                }
                v8 += a4;
            }
        }
        private int ReadScrambledInt1(byte[] a, int offset)
        {
            return a[offset + 1] | (a[offset + 6] << 8) | (a[offset + 3] << 16) | (a[offset + 2] << 24);
        }

        private int ReadScrambledInt2(byte[] a, int offset)
        {
            return a[offset + 2] | (a[offset + 4] << 8) | (a[offset + 0] << 16) | (a[offset + 5] << 24);
        }

        private int CalcOffset(int value) => value * 0x113 + 6;

        private byte[] DecompressHeader(byte[] data)
        {
            var decompressedSize = ReadScrambledInt1(data, 0x20);
            var decompressed = new byte[decompressedSize];


            var numWrite = LZ4Codec.Decode(data, 0x27, data.Length - 0x27, decompressed, 0, decompressedSize);
            if (numWrite != decompressedSize)
                throw new IOException($"{string.Format("0x{0:x8}", OriginalPos)} doesn't point to a valid mhy0, Lz4 decompression error, write {numWrite} bytes but expected {decompressedSize} bytes");

            return decompressed;
        }

        private void ReadHeader(EndianBinaryReader reader)
        {
            OriginalPos = reader.Position;
            var magic = reader.ReadUInt32();
            if (magic != 0x3079686D)
                throw new Exception("not a mhy0");

            m_Header = new Header();
            m_Header.headerSize = reader.ReadInt32();
            m_Header.headerData = reader.ReadBytes(m_Header.headerSize);

            Scramble(m_Header.headerData, 0, 0x39, 0x1C);
            m_Header.headerData = DecompressHeader(m_Header.headerData);
        }
        private void ReadBlocksInfoAndDirectory(EndianBinaryReader reader)
        {
            m_Header.bundleCount = ReadScrambledInt2(m_Header.headerData, 0);
            m_Header.blockCount = ReadScrambledInt2(m_Header.headerData, CalcOffset(m_Header.bundleCount));

            m_BlocksInfo = new StorageBlock[m_Header.blockCount];
            for (int i = 0; i < m_Header.blockCount; i++)
            {
                var offset = i * 13 + CalcOffset(m_Header.bundleCount);
                m_BlocksInfo[i] = new StorageBlock
                {
                    uncompressedSize = ReadScrambledInt1(m_Header.headerData, offset + 0xC),
                    compressedSize = ReadScrambledInt2(m_Header.headerData, offset + 6)
                };
            }

            m_DirectoryInfo = new Node[m_Header.bundleCount];
            for (int i = 0; i < m_Header.bundleCount; i++)
            {
                var offset = CalcOffset(i);
                m_DirectoryInfo[i] = new Node
                {
                    offset = ReadScrambledInt2(m_Header.headerData, offset + 0x100 + 6),
                    size = ReadScrambledInt1(m_Header.headerData, offset + 0x100 + 0xC),
                    path = Encoding.UTF8.GetString(m_Header.headerData.Skip(offset).TakeWhile(b => !b.Equals(0)).ToArray()),
                };
            }
        }

        private Stream CreateBlocksStream(string path)
        {
            Stream blocksStream;
            var uncompressedSizeSum = m_BlocksInfo.Sum(x => x.uncompressedSize);
            if (uncompressedSizeSum >= int.MaxValue)
                blocksStream = new FileStream(path + ".temp", FileMode.Create, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
            else
                blocksStream = new MemoryStream(uncompressedSizeSum);
            return blocksStream;
        }

        private void ReadBlocks(EndianBinaryReader reader, Stream blocksStream)
        {
            reader.Position = OriginalPos + m_Header.headerSize + 8;
            foreach (var blockInfo in m_BlocksInfo)
            {
                var compressedSize = blockInfo.compressedSize;
                var compressedBytes = BigArrayPool<byte>.Shared.Rent(compressedSize);
                reader.Read(compressedBytes, 0, compressedSize);
                if (compressedSize < 16)
                    throw new Exception($"Wrong compressed length: {compressedSize}");
                Scramble(compressedBytes, 0, (ulong) Math.Min(compressedBytes.Length, 33), 8);
                var uncompressedSize = blockInfo.uncompressedSize;
                var uncompressedBytes = BigArrayPool<byte>.Shared.Rent(uncompressedSize);
                var numWrite = LZ4Codec.Decode(compressedBytes, 0xC, compressedSize - 0xC, uncompressedBytes, 0, uncompressedSize);
                if (numWrite != uncompressedSize)
                    throw new IOException($"{string.Format("0x{0:x8}", OriginalPos)} doesn't point to a valid mhy0, Lz4 decompression error, write {numWrite} bytes but expected {uncompressedSize} bytes");
                blocksStream.Write(uncompressedBytes, 0, uncompressedSize);
                BigArrayPool<byte>.Shared.Return(compressedBytes);
                BigArrayPool<byte>.Shared.Return(uncompressedBytes);
            }
        }

        private void ReadFiles(Stream blocksStream, string path)
        {
            fileList = new StreamFile[m_DirectoryInfo.Length];
            for (int i = 0; i < m_DirectoryInfo.Length; i++)
            {
                var node = m_DirectoryInfo[i];
                var file = new StreamFile();
                fileList[i] = file;
                file.path = node.path;
                file.fileName = Path.GetFileName(node.path);
                if (node.size >= int.MaxValue)
                {
                    var extractPath = path + "_unpacked" + Path.DirectorySeparatorChar;
                    Directory.CreateDirectory(extractPath);
                    file.stream = new FileStream(extractPath + file.fileName, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite);
                }
                else
                    file.stream = new MemoryStream((int)node.size);
                blocksStream.Position = node.offset;
                blocksStream.CopyTo(file.stream, node.size);
                file.stream.Position = 0;
            }
        }

        public Mhy0File(EndianBinaryReader reader, string path)
        {
            ReadHeader(reader);
            ReadBlocksInfoAndDirectory(reader);
            using (var blocksStream = CreateBlocksStream(path))
            {
                ReadBlocks(reader, blocksStream);
                ReadFiles(blocksStream, path);
            }
        }
    }
}
