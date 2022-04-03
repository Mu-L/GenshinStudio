using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssetStudio
{
    public class ResourceIndex
    {
        public Dictionary<int, List<int>> BundleDependencyMap;
        public Dictionary<int, Block> BlockInfoMap;
        public Dictionary<int, byte> BlockMap;
        public Dictionary<ulong, ulong> AssetMap;
        public List<Dictionary<uint, BundleInfo>> AssetLocationMap;
        public List<int> BlockSortList;
        public ResourceIndex()
        {
            BlockSortList = new List<int>();
            AssetMap = new Dictionary<ulong, ulong>();
            AssetLocationMap = new List<Dictionary<uint, BundleInfo>>(0x100);
            for (int i = 0; i < AssetLocationMap.Capacity; i++)
            {
                AssetLocationMap.Add(new Dictionary<uint, BundleInfo>(0x1FF));
            }
            BundleDependencyMap = new Dictionary<int, List<int>>();
            BlockInfoMap = new Dictionary<int, Block>();
            BlockMap = new Dictionary<int, byte>();
        }
        public async Task<bool> FromFile(string path)
        {
            Clear();
            using (var stream = File.OpenRead(path))
            {
                var bytes = new byte[stream.Length];
                var count = await stream.ReadAsync(bytes, 0, (int)stream.Length);

                if (count != stream.Length) throw new Exception("Error While Reading File");
                var json = Encoding.UTF8.GetString(bytes);

                var obj = JsonConvert.DeserializeObject<AssetIndex>(json);
                if (obj != null)
                {
                    return MapToResourceIndex(obj);
                }
            }
            return false;
        }
        public void Clear()
        {
            BundleDependencyMap.Clear();
            BlockInfoMap.Clear();
            BlockMap.Clear();
            AssetMap.Clear();
            AssetLocationMap.ForEach(x => x.Clear());
            BlockSortList.Clear();
        }
        public bool MapToResourceIndex(AssetIndex asset_index)
        {
            try
            {
                BundleDependencyMap = asset_index.Dependencies;
                BlockSortList = asset_index.SortList.ConvertAll(x => (int)x);
                foreach (var asset in asset_index.SubAssets)
                {
                    foreach (var subAsset in asset.Value)
                    {
                        var bundleInfo = new BundleInfo(asset.Key, subAsset.Name);
                        var blockInfo = asset_index.Assets[asset.Key];
                        ulong key = (((ulong)blockInfo.Id) << 32) | subAsset.PathHashLast;
                        AssetLocationMap[subAsset.PathHashPre].Add(subAsset.PathHashLast, bundleInfo);
                        AssetMap[key] = ((ulong)subAsset.PathHashLast) << 8 | subAsset.PathHashPre;
                    }
                }
                foreach (var asset in asset_index.Assets)
                {
                    var block = new Block((int)asset.Value.Id, (int)asset.Value.Offset);
                    BlockInfoMap.Add(asset.Key, block);

                    if (!BlockMap.ContainsKey((int)asset.Value.Id))
                        BlockMap.Add((int)asset.Value.Id, asset.Value.Language);
                }
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }
        public List<BundleInfo> GetAllAssets()
        {
            var hashes = new List<BundleInfo>();
            for (int i = 0; i < AssetLocationMap.Capacity; i++)
            {
                foreach (var pair in AssetLocationMap[i])
                {
                    hashes.Add(pair.Value);
                }
            }
            return hashes;
        }
        public List<BundleInfo> GetAssets(int bundle)
        {
            var hashes = new List<BundleInfo>();
            for (int i = 0; i < AssetLocationMap.Capacity; i++)
            {
                foreach (var pair in AssetLocationMap[i])
                {
                    if (pair.Value.Bundle == bundle)
                        hashes.Add(pair.Value);
                }
            }
            return hashes;
        }
        public BundleInfo GetBundleInfo(ulong hash)
        {
            var asset = new Asset(hash);
            if (AssetLocationMap.ElementAtOrDefault(asset.Pre) != null)
            {
                if (AssetLocationMap[asset.Pre].TryGetValue(asset.Last, out var bundleInfo)) return bundleInfo;
            }
            return null;
        }
        public string GetBundlePath(uint last)
        {
            foreach (var location in AssetLocationMap)
            {
                if (location.TryGetValue(last, out var bundleInfo)) return bundleInfo.Path;
            }
            return null;
        }
        public ulong GetAssetIndex(ulong blkHash)
        {
            if (AssetMap.TryGetValue(blkHash, out var value)) return value;
            else return 0;
        }
        public List<uint> GetAllAssetIndices(int bundle)
        {
            var hashes = new List<uint>();
            for (int i = 0; i < AssetLocationMap.Capacity; i++)
            {
                foreach (var pair in AssetLocationMap[i])
                {
                    if (pair.Value.Bundle == bundle)
                        hashes.Add(pair.Key);
                }
            }
            return hashes;
        }
        public List<int> GetBundles(int id)
        {
            var bundles = new List<int>();
            foreach (var block in BlockInfoMap)
            {
                if (block.Value.Id == id)
                {
                    bundles.Add(block.Key);
                }
            }
            return bundles;
        }
        public Block GetBlockInfo(int bundle)
        {
            if (BlockInfoMap.TryGetValue(bundle, out var blk)) return blk;
            else return null;
        }
        public BlockFile GetBlockFile(int id)
        {
            if (BlockMap.TryGetValue(id, out var languageCode))
            {
                return new BlockFile(languageCode, id);
            }
            return null;
        }
        public int GetBlockID(int bundle)
        {
            if (BlockInfoMap.TryGetValue(bundle, out var block))
            {
                return block.Id;
            }
            else return 0;
        }
        public List<int> GetBundleDep(int bundle)
        {
            if (BundleDependencyMap.TryGetValue(bundle, out var dep)) return dep;
            else return null;
        }
        public bool CheckIsLegitAssetPath(ulong hash)
        {
            var asset = new Asset(hash);
            return AssetLocationMap[asset.Pre].ContainsKey(asset.Last);
        }

        //public void AddAssetLocation(BundleInfo bundle, Asset asset)
        //{
        //    AssetMap[asset.Last] = asset.Hash;
        //    AssetLocationMap[asset.Pre].Add(asset.Last, bundle);
        //}
    }
    public class BundleInfo
    {
        public int Bundle;
        public string Path;
        public BundleInfo(int bundle, string path)
        {
            Bundle = bundle;
            Path = path;
        }
    }
    public class Asset
    {
        public ulong Hash;
        public uint Last => (uint)(Hash >> 8);
        public byte Pre => (byte)(Hash & 0xFF);
        public Asset(ulong hash)
        {
            Hash = hash;
        }
    }
    public class Block
    {
        public int Id;
        public int Offset;

        public Block(int id, int offset)
        {
            Id = id;
            Offset = offset;
        }
    }
    public class BlockInfo : IComparable<BlockInfo>
    {
        public string Path;
        public long Offset;

        public BlockInfo() : this("", 0) { }
        public BlockInfo(string path, long offset)
        {
            Path = path;
            Offset = offset;
        }
        public int CompareTo(BlockInfo other)
        {
            if (other == null) return 1;

            int result;
            if (other == null)
                throw new ArgumentException("Object is not a BlockInfo");

            result = Path.CompareTo(other.Path);

            if (result == 0)
                result = Offset.CompareTo(other.Offset);

            return result;
        }
    }
    public class BlockFile
    {
        public int LanguageCode;
        public int Id;
        public BlockFile(int languageCode, int id)
        {
            LanguageCode = languageCode;
            Id = id;
        }
    }

    public class AssetIndex
    {
        public Dictionary<string, string> Types { get; set; }
        public class SubAssetInfo
        {
            public string Name { get; set; }
            public byte PathHashPre { get; set; }
            public uint PathHashLast { get; set; }
        }
        public Dictionary<int, List<SubAssetInfo>> SubAssets { get; set; }
        public Dictionary<int, List<int>> Dependencies { get; set; }
        public List<uint> PreloadBlocks { get; set; }
        public List<uint> PreloadShaderBlocks { get; set; }
        public class BlockInfo
        {
            public byte Language { get; set; }
            public uint Id { get; set; }
            public uint Offset { get; set; }
        }
        public Dictionary<int, BlockInfo> Assets { get; set; }
        public List<uint> SortList { get; set; }
    }
}
