using Lz4;
using SevenZip.Buffer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssetStudio
{
    public partial class Mhy0File
    {
        private byte[] _data = null;
        private Guid _id = Guid.Empty;
        public byte[] Data => _data; 
        public Guid ID => _id;
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

        private byte[] DecompressHeader(byte[] data)
        {
            var decompressedSize = ReadScrambledInt1(data, 0x20);
            var decompressed = new byte[decompressedSize];

            var lz4 = new Lz4DecoderStream(new MemoryStream(data, 0x27, data.Length - 0x27));
            lz4.Read(decompressed, 0, decompressedSize);

            return decompressed;
        }

        public Mhy0File(EndianBinaryReader reader)
        {
            var originalPos = reader.Position;
            var magic = reader.ReadUInt32();
            if (magic != 0x3079686D)
                throw new Exception("not a mhy0");
            var headerSize = reader.ReadInt32();
            var headerData = reader.ReadBytes(headerSize);

            Scramble(headerData, 0, 0x39, 0x1C);
            //File.WriteAllBytes("decrypted.bin", data);

            var decompressed = DecompressHeader(headerData);
            //File.WriteAllBytes("decompressed.bin", decompressed);

            var cabCount = ReadScrambledInt2(decompressed, 0); // not the best name
            var entryCount = ReadScrambledInt2(decompressed, cabCount * 0x113 + 6);

            var name = Encoding.UTF8.GetString(decompressed.Skip(6).TakeWhile(b => !b.Equals(0)).ToArray());
            if (name.StartsWith("CAB-"))
            {
                //var id1 = Convert.ToUInt64(name.Substring(4, 16), 16);
                //var id2 = Convert.ToUInt64(name.Substring(20, 16), 16);
                //_id = id1 ^ id2;
                //Console.WriteLine(name.Substring(4));
                _id = new Guid(name.Substring(4));
                //Console.WriteLine(_id);
            }

            var compressedEntrySizes = new List<int>(entryCount);
            var decompressedEntrySizes = new List<int>(entryCount);
            for (int i = 0; i < entryCount; i++)
            {
                var offset = i * 13 + cabCount * 0x113 + 6;
                compressedEntrySizes.Add(ReadScrambledInt2(decompressed, offset + 6));
                decompressedEntrySizes.Add(ReadScrambledInt1(decompressed, offset + 0xC));
            }

            reader.Position = originalPos + headerSize + 8;
            var finalData = new byte[decompressedEntrySizes.Sum()];
            var finalDataPos = 0;
            for (int i = 0; i < entryCount; i++)
            {
                var compressedEntry = reader.ReadBytes(compressedEntrySizes[i]);
                if (compressedEntry.Length >= 0x21)
                    Scramble(compressedEntry, 0, 0x21, 8);

                var lz4 = new Lz4DecoderStream(new MemoryStream(compressedEntry, 0xC, compressedEntry.Length - 0xC));
                lz4.Read(finalData, finalDataPos, decompressedEntrySizes[i]);
                finalDataPos += decompressedEntrySizes[i];
            }

            _data = finalData;
        }
    }
}
