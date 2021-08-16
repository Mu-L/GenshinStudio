using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssetStudio
{
    public partial class BlkFile
    {
        private List<Mhy0File> _files = null;
        public List<Mhy0File> Files => _files;

        public BlkFile(FileReader reader)
        {
            // hopefully this doesn't break anything
            reader.endian = EndianType.LittleEndian;
            // skip magic, unknown field
            reader.ReadBytes(8);

            var key = reader.ReadBytes(16);
            // skip useless half of key
            reader.ReadBytes(16);
            BlkKeyScramble(key);

            var xorpadSize = reader.ReadUInt16();
            var data = reader.ReadBytes((int)(reader.Length - reader.Position));

            var xorpad = CreateDecryptVector(key, data, Math.Min(xorpadSize, (ushort)0x1000), 0x1000);
            for (int i = 0; i < data.Length; i++)
                data[i] ^= xorpad[i & 0xFFF];
            //File.WriteAllBytes("decrypted.bin", data);

            var memReader = new EndianBinaryReader(new MemoryStream(data), reader.endian);
            _files = new List<Mhy0File>();
            while (memReader.Position != memReader.BaseStream.Length)
                _files.Add(new Mhy0File(memReader));
        }

        private byte XorCombine(byte[] input, int offset, int size = 16)
        {
            byte ret = 0;
            for (int i = offset; i < offset + size; i++)
                ret ^= input[i];
            return ret;
        }

        private void BlkKeyScramble(byte[] key)
        {
            // key_scramble1
            for (uint i = 0; i < 0x10; i++)
                key[i] = KeyScrambleTable[((i & 3) << 8) | key[i]];

            // key_scramble2
            byte[] expandedKey = new byte[256];
            for (uint i = 0; i < 16; i++)
                expandedKey[i * 16] = key[i];
            for (uint i = 0; i < 256; i++)
                expandedKey[i] ^= (byte)(BlkStuff1p1[i] ^ StackStuff[i]);

            byte[] indexScramble = new byte[]
            {
                0,  13, 10, 7,
                4,  1,  14, 11,
                8,  5,  2,  15,
                12, 9,  6,  3
            };
            uint[] scratch = new uint[4];
            byte[] scratchByte = new byte[16]; // c# so no pointer casting
            for (ulong i = 1; i < 10; i++)
            {
                // avoid reallocating
                for (int j = 0; j < 4; j++)
                    scratch[j] = 0;
                for (ulong j = 0; j < 4; j++)
                {
                    byte temp;
                    temp = XorCombine(expandedKey, 16 * indexScramble[4 * j]);
                    scratch[j] ^= BlkStuff1p2[temp];
                    temp = XorCombine(expandedKey, 16 * indexScramble[4 * j + 1]);
                    scratch[j] ^= BlkStuff1p3[temp];
                    temp = XorCombine(expandedKey, 16 * indexScramble[4 * j + 2]);
                    scratch[j] ^= BlkStuff1p4[temp];
                    temp = XorCombine(expandedKey, 16 * indexScramble[4 * j + 3]);
                    scratch[j] ^= BlkStuff1p5[temp];
                }
                for (int j = 0; j < 256; j++)
                    expandedKey[j] = 0;
                Buffer.BlockCopy(scratch, 0, scratchByte, 0, scratchByte.Length);
                for (int j = 0; j < 16; j++)
                    expandedKey[j * 16] = scratchByte[j];
                for (ulong j = 0; j < 256; j++)
                {
                    ulong v10 = j + (i << 8);
                    expandedKey[j] ^= (byte)(BlkStuff1p1[v10] ^ StackStuff[v10]);
                }
            }

            for (int i = 0; i < 16; i++)
            {
                byte t = XorCombine(expandedKey, 16 * indexScramble[i]);
                scratchByte[i] = (byte)(BlkStuff1p6[t] ^ ~t);
            }
            for (int i = 0; i < 256; i++)
                expandedKey[i] = 0;
            for (int i = 0; i < 16; i++)
                expandedKey[i * 16] = scratchByte[i];
            for (int i = 0; i < 256; i++)
                expandedKey[i] ^= (byte)(BlkStuff1p7[i] ^ StackStuff[i + 0xA00]);

            for (int i = 0; i < 16; i++)
                key[i] = XorCombine(expandedKey, 16 * i);
            byte[] hard_key = new byte[] { 0xE3, 0xFC, 0x2D, 0x26, 0x9C, 0xC5, 0xA2, 0xEC, 0xD3, 0xF8, 0xC6, 0xD3, 0x77, 0xC2, 0x49, 0xB9 };
            for (int i = 0; i < 16; i++)
                key[i] ^= hard_key[i];
        }

        private byte[] CreateDecryptVector(byte[] key, byte[] encryptedData, ushort blockSize, ushort xorpadSize)
        {
            long XorLong(int offset, long input)
            {
                // hopefully this gets optimized
                var og = BitConverter.ToInt64(encryptedData, offset);
                return og ^ input;
            }
            long v12 = -1;
            for (int v9 = 0; v9 < blockSize >> 3; v9++)
                v12 = XorLong(v9 * 8, v12);

            var key0 = BitConverter.ToUInt64(key, 0);
            var key1 = BitConverter.ToUInt64(key, 8);
            var seed = key0 ^ key1 ^ (ulong)v12 ^ 0x567BA22BABB08098;

            var rand = new MT19937_64(seed);
            var xorpad = new byte[xorpadSize];
            for (int i = 0; i < xorpadSize >> 3; i++)
                Buffer.BlockCopy(BitConverter.GetBytes(rand.Int63()), 0, xorpad, i * 8, 8);
            return xorpad;
        }
    }
}
