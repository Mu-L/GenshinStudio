using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AssetStudio
{
    public class ResourceIndex
    {
        public Dictionary<int, List<int>> BundleDependencyMap;
        public Dictionary<int, Block> BlockInfoMap;
        public Dictionary<int, byte> BlockMap;
        public Dictionary<int, ulong> AssetMap;
        public List<Dictionary<int, BundleInfo>> AssetLocationMap;
        public List<int> BlockSortList;
        public ResourceIndex()
        {
            BlockSortList = new List<int>();
            AssetMap = new Dictionary<int, ulong>();
            AssetLocationMap = new List<Dictionary<int, BundleInfo>>(0x100);
            for (int i = 0; i < AssetLocationMap.Capacity; i++)
            {
                AssetLocationMap.Add(new Dictionary<int, BundleInfo>(0x1FF));
            }
            BundleDependencyMap = new Dictionary<int, List<int>>();
            BlockInfoMap = new Dictionary<int, Block>();
            BlockMap = new Dictionary<int, byte>();
        }
        public bool FromFile(string path)
        {
            var file = File.OpenText(path);
            JsonSerializer serializer = new JsonSerializer();
            ResourceIndex obj = serializer.Deserialize(file, typeof(ResourceIndex)) as ResourceIndex;
            if (obj != null)
            {
                BundleDependencyMap = obj.BundleDependencyMap;
                BlockInfoMap = obj.BlockInfoMap;
                BlockMap = obj.BlockMap;
                AssetMap = obj.AssetMap;
                AssetLocationMap = obj.AssetLocationMap;
                BlockSortList = obj.BlockSortList;
                return true;
            }
            return false;
        }
        public void Clear()
        {
            BundleDependencyMap.Clear();
            BlockInfoMap.ToList().Clear();
            AssetLocationMap.ToList().ForEach(x => x.Clear());
            BlockMap.Clear();
        }
        public List<BundleInfo> GetAllAssets()
        {
            var hashes = new List<BundleInfo>();
            for (int i = 0; i < AssetLocationMap.Capacity; i++)
            {
                foreach(var pair in AssetLocationMap[i])
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
        public string GetBundlePath(int last)
        {
            foreach(var location in AssetLocationMap)
            {
                if (location.TryGetValue(last, out var bundleInfo)) return bundleInfo.Path;
            }
            return null;
        }
        public ulong GetAssetIndex(int container)
        {
            if (AssetMap.TryGetValue(container, out var value)) return value;
            else return 0;
        }
        public List<int> GetAllAssetIndices(int bundle)
        {
            var hashes = new List<int>();
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
        public void AddAssetLocation(BundleInfo bundle, Asset asset)
        {
            AssetMap[asset.Last] = asset.Hash;
            AssetLocationMap[asset.Pre].Add(asset.Last, bundle);
        }
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
        public int Last => unchecked((int)(Hash >> 8));
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
        public override bool Equals(object obj)
        {
            return obj is Block block && Id == block.Id && Offset == block.Offset;
        }
        public override int GetHashCode()
        {
            return Id.GetHashCode() + Offset.GetHashCode();
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
}
