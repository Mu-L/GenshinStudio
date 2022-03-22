using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static AssetStudio.ImportHelper;

namespace AssetStudio
{
    public class AssetsManager
    {
        public string SpecifyUnityVersion;
        public Dictionary<string, BlockInfo> BLKMap = new Dictionary<string, BlockInfo>();
        public Dictionary<string, string> CABMap = new Dictionary<string, string>();
        public List<SerializedFile> assetsFileList = new List<SerializedFile>();
        public ResourceIndex resourceIndex = new ResourceIndex();

        internal Dictionary<string, int> assetsFileIndexCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<string, BinaryReader> resourceFileReaders = new Dictionary<string, BinaryReader>(StringComparer.OrdinalIgnoreCase);

        private List<string> importFiles = new List<string>();
        private HashSet<string> importFilesHash = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> assetsFileListHash = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public void BuildBlkMap(List<string> files)
        {
            Logger.Info(string.Format("Building BLKMap"));
            try
            {
                var collision = 0;
                var offsets = new List<long>();
                BLKMap.Clear();
                Progress.Reset();
                for (int i = 0; i < files.Count; i++)
                {
                    var file = files[i];
                    using (var reader = new FileReader(file))
                    {
                        var blkfile = new BlkFile(reader);
                        foreach (var mhy0 in blkfile.Files)
                        {
                            foreach (var f in mhy0.fileList)
                            {
                                if (BLKMap.ContainsKey(f.path))
                                {
                                    collision += 1;
                                    continue;
                                }
                                BLKMap.Add(f.path, new BlockInfo(file, mhy0.OriginalPos));
                            }
                        }
                    }
                    Progress.Report(i + 1, files.Count);
                }

                BLKMap = BLKMap.OrderBy(pair => pair.Value).ToDictionary(pair => pair.Key, pair => pair.Value);
                var outputFile = new FileInfo(@"BLKMap.bin");

                using (var binaryFile = outputFile.Create())
                using (var writter = new BinaryWriter(binaryFile))
                {
                    writter.Write(BLKMap.Count);
                    foreach (var cab in BLKMap)
                    {
                        writter.Write(cab.Key);
                        writter.Write(cab.Value.Path);
                        writter.Write(cab.Value.Offset);
                    }
                }
                Logger.Info($"BLKMap build successfully with {collision} collisions !!");
            }
            catch (Exception e)
            {
                Logger.Warning($"BLKMap was not build, {e.Message}");
            }
        }
        public void LoadBlkMap()
        {
            Logger.Info(string.Format("Loading BLKMap"));
            try
            {
                BLKMap.Clear();
                using (var binaryFile = File.OpenRead("BLKMap.bin"))
                using (var reader = new BinaryReader(binaryFile))
                {
                    var count = reader.ReadInt32();
                    BLKMap = new Dictionary<string, BlockInfo>(count);
                    for (int i = 0; i < count; i++)
                    {
                        var cab = reader.ReadString();
                        var path = reader.ReadString();
                        var offset = reader.ReadInt64();
                        BLKMap.Add(cab, new BlockInfo(path, offset));
                    }
                }
                Logger.Info(string.Format("Loaded BLKMap !!"));
            }
            catch (Exception e)
            {
                Logger.Warning($"BLKMap was not loaded, {e.Message}");
            }
        }
        public void BuildCABMap(List<string> files)
        {
            Logger.Info(string.Format("Building CABMap"));
            try
            {
                var collision = 0;
                var offsets = new List<long>();
                CABMap.Clear();
                Progress.Reset();
                for (int i = 0; i < files.Count; i++)
                {
                    var file = files[i];
                    if (CABMap.ContainsKey(Path.GetFileNameWithoutExtension(file)))
                    {
                        collision += 1;
                        continue;
                    }
                    CABMap.Add(Path.GetFileNameWithoutExtension(file), file);
                    Progress.Report(i + 1, files.Count);
                }

                CABMap = CABMap.OrderBy(pair => pair.Value).ToDictionary(pair => pair.Key, pair => pair.Value);
                var outputFile = new FileInfo(@"CABMap.bin");

                using (var binaryFile = outputFile.Create())
                using (var writter = new BinaryWriter(binaryFile))
                {
                    writter.Write(CABMap.Count);
                    foreach (var cab in CABMap)
                    {
                        writter.Write(cab.Key);
                        writter.Write(cab.Value);
                    }
                }
                Logger.Info($"CABMap build successfully with {collision} collisions !!");
            }
            catch (Exception e)
            {
                Logger.Warning($"CABMap was not build, {e.Message}");
            }
        }
        public void LoadCABMap()
        {
            Logger.Info(string.Format("Loading CABMap"));
            try
            {
                CABMap.Clear();
                using (var binaryFile = File.OpenRead("CABMap.bin"))
                using (var reader = new BinaryReader(binaryFile))
                {
                    var count = reader.ReadInt32();
                    CABMap = new Dictionary<string, string>(count);
                    for (int i = 0; i < count; i++)
                    {
                        var key = reader.ReadString();
                        var value = reader.ReadString();
                        CABMap.Add(key, value);
                    }
                }
                Logger.Info(string.Format("Loaded CABMap !!"));
            }
            catch (Exception e)
            {
                Logger.Warning($"CABMap was not loaded, {e.Message}");
            }
        }
        public async Task<bool> LoadAIJSON(string file)
        {
            Logger.Info(string.Format("Loading AssetIndex JSON"));
            try
            {
                return await resourceIndex.FromFile(file);
            }
            catch (Exception e)
            {
                Logger.Error("AssetIndex JSON was not loaded");
                Console.WriteLine(e.ToString());
                return false;
            }
        }

        public void LoadFiles(params string[] files)
        {
            var path = Path.GetDirectoryName(Path.GetFullPath(files[0]));
            MergeSplitAssets(path);
            var toReadFile = ProcessingSplitFiles(files.ToList());
            Load(toReadFile);
        }

        public void LoadFolder(string path)
        {
            MergeSplitAssets(path, true);
            var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories).ToList();
            var toReadFile = ProcessingSplitFiles(files);
            Load(toReadFile);
        }

        private void Load(string[] files)
        {
            foreach (var file in files)
            {
                importFiles.Add(file);
                importFilesHash.Add(Path.GetFileName(file));
            }

            Progress.Reset();
            //use a for loop because list size can change
            for (var i = 0; i < importFiles.Count; i++)
            {
                LoadFile(importFiles[i]);
                Progress.Report(i + 1, importFiles.Count);
            }

            importFiles.Clear();
            importFilesHash.Clear();
            assetsFileListHash.Clear();

            ReadAssets();
            ProcessAssets();
        }

        private void LoadFile(string fullName)
        {
            var reader = new FileReader(fullName);
            LoadFile(reader);
        }

        private void LoadFile(FileReader reader)
        {
            switch (reader.FileType)
            {
                case FileType.AssetsFile:
                    LoadAssetsFile(reader);
                    break;
                case FileType.BundleFile:
                    LoadBundleFile(reader);
                    break;
                case FileType.BlkFile:
                    LoadBlkFile(reader);
                    break;
                case FileType.WebFile:
                    LoadWebFile(reader);
                    break;
                case FileType.GZipFile:
                    LoadFile(DecompressGZip(reader));
                    break;
                case FileType.BrotliFile:
                    LoadFile(DecompressBrotli(reader));
                    break;
                case FileType.ZipFile:
                    LoadZipFile(reader);
                    break;
            }
        }

        private void LoadAssetsFile(FileReader reader)
        {
            if (!assetsFileListHash.Contains(reader.FileName))
            {
                Logger.Info($"Loading {reader.FileName}");
                try
                {
                    var assetsFile = new SerializedFile(reader, this, reader.FullPath);
                    CheckStrippedVersion(assetsFile);
                    assetsFileList.Add(assetsFile);
                    assetsFileListHash.Add(assetsFile.fileName);

                    foreach (var sharedFile in assetsFile.m_Externals)
                    {
                        var sharedFileName = sharedFile.fileName;

                        if (!importFilesHash.Contains(sharedFileName))
                        {
                            var sharedFilePath = Path.Combine(Path.GetDirectoryName(reader.FullPath), sharedFileName);
                            if (!File.Exists(sharedFilePath))
                            {
                                var findFiles = Directory.GetFiles(Path.GetDirectoryName(reader.FullPath), sharedFileName, SearchOption.AllDirectories);
                                if (findFiles.Length > 0)
                                {
                                    sharedFilePath = findFiles[0];
                                }
                                else
                                {
                                    CABMap.TryGetValue(sharedFileName, out sharedFilePath);
                                }
                            }

                            if (File.Exists(sharedFilePath))
                            {
                                importFiles.Add(sharedFilePath);
                                importFilesHash.Add(sharedFileName);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Error($"Error while reading assets file {reader.FullPath}", e);
                    reader.Dispose();
                }
            }
            else
            {
                Logger.Info($"Skipping {reader.FullPath}");
                reader.Dispose();
            }
        }

        private void LoadAssetsFromMemory(FileReader reader, string originalPath, string unityVersion = null)
        {
            if (!assetsFileListHash.Contains(reader.FileName))
            {
                try
                {
                    var assetsFile = new SerializedFile(reader, this);
                    assetsFile.originalPath = originalPath;
                    if (!string.IsNullOrEmpty(unityVersion) && assetsFile.header.m_Version < SerializedFileFormatVersion.kUnknown_7)
                    {
                        assetsFile.SetVersion(unityVersion);
                    }
                    CheckStrippedVersion(assetsFile);
                    assetsFileList.Add(assetsFile);
                    assetsFileListHash.Add(assetsFile.fileName);
                }
                catch (Exception e)
                {
                    Logger.Error($"Error while reading assets file {reader.FullPath} from {Path.GetFileName(originalPath)}", e);
                    resourceFileReaders.Add(reader.FileName, reader);
                }
            }
            else
                Logger.Info($"Skipping {originalPath} ({reader.FileName})");
        }

        private void LoadBundleFile(FileReader reader, string originalPath = null)
        {
            Logger.Info("Loading " + reader.FullPath);
            try
            {
                var bundleFile = new BundleFile(reader);
                foreach (var file in bundleFile.fileList)
                {
                    var dummyPath = Path.Combine(Path.GetDirectoryName(reader.FullPath), file.fileName);
                    var subReader = new FileReader(dummyPath, file.stream);
                    if (subReader.FileType == FileType.AssetsFile)
                    {
                        LoadAssetsFromMemory(subReader, originalPath ?? reader.FullPath, bundleFile.m_Header.unityRevision);
                    }
                    else
                    {
                        resourceFileReaders[file.fileName] = subReader; //TODO
                    }
                }
            }
            catch (Exception e)
            {
                var str = $"Error while reading bundle file {reader.FullPath}";
                if (originalPath != null)
                {
                    str += $" from {Path.GetFileName(originalPath)}";
                }
                Logger.Error(str, e);
            }
            finally
            {
                reader.Dispose();
            }
        }

        private void LoadWebFile(FileReader reader)
        {
            Logger.Info("Loading " + reader.FullPath);
            try
            {
                var webFile = new WebFile(reader);
                foreach (var file in webFile.fileList)
                {
                    var dummyPath = Path.Combine(Path.GetDirectoryName(reader.FullPath), file.fileName);
                    var subReader = new FileReader(dummyPath, file.stream);
                    switch (subReader.FileType)
                    {
                        case FileType.AssetsFile:
                            LoadAssetsFromMemory(subReader, reader.FullPath);
                            break;
                        case FileType.BundleFile:
                            LoadBundleFile(subReader, reader.FullPath);
                            break;
                        case FileType.WebFile:
                            LoadWebFile(subReader);
                            break;
                        case FileType.ResourceFile:
                            resourceFileReaders[file.fileName] = subReader; //TODO
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error($"Error while reading web file {reader.FullPath}", e);
            }
            finally
            {
                reader.Dispose();
            }
        }

        private void LoadZipFile(FileReader reader)
        {
            Logger.Info("Loading " + reader.FileName);
            try
            {
                using (ZipArchive archive = new ZipArchive(reader.BaseStream, ZipArchiveMode.Read))
                {
                    List<string> splitFiles = new List<string>();
                    // register all files before parsing the assets so that the external references can be found
                    // and find split files
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (entry.Name.Contains(".split"))
                        {
                            string baseName = Path.GetFileNameWithoutExtension(entry.Name);
                            string basePath = Path.Combine(Path.GetDirectoryName(entry.FullName), baseName);
                            if (!splitFiles.Contains(basePath))
                            {
                                splitFiles.Add(basePath);
                                importFilesHash.Add(baseName);
                            }
                        }
                        else
                        {
                            importFilesHash.Add(entry.Name);
                        }
                    }

                    // merge split files and load the result
                    foreach (string basePath in splitFiles)
                    {
                        try
                        {
                            Stream splitStream = new MemoryStream();
                            int i = 0;
                            while (true)
                            {
                                string path = $"{basePath}.split{i++}";
                                ZipArchiveEntry entry = archive.GetEntry(path);
                                if (entry == null)
                                    break;
                                using (Stream entryStream = entry.Open())
                                {
                                    entryStream.CopyTo(splitStream);
                                }
                            }
                            splitStream.Seek(0, SeekOrigin.Begin);
                            FileReader entryReader = new FileReader(basePath, splitStream);
                            LoadFile(entryReader);
                        }
                        catch (Exception e)
                        {
                            Logger.Error($"Error while reading zip split file {basePath}", e);
                        }
                    }

                    // load all entries
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        try
                        {
                            string dummyPath = Path.Combine(Path.GetDirectoryName(reader.FullPath), reader.FileName, entry.FullName);
                            // create a new stream
                            // - to store the deflated stream in
                            // - to keep the data for later extraction
                            Stream streamReader = new MemoryStream();
                            using (Stream entryStream = entry.Open())
                            {
                                entryStream.CopyTo(streamReader);
                            }
                            streamReader.Position = 0;

                            FileReader entryReader = new FileReader(dummyPath, streamReader);
                            LoadFile(entryReader);
                            if (entryReader.FileType == FileType.ResourceFile)
                            {
                                entryReader.Position = 0;
                                if (!resourceFileReaders.ContainsKey(entry.Name))
                                {
                                    resourceFileReaders.Add(entry.Name, entryReader);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.Error($"Error while reading zip entry {entry.FullName}", e);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error($"Error while reading zip file {reader.FileName}", e);
            }
            finally
            {
                reader.Dispose();
            }
        }

        private void LoadBlkFile(FileReader reader)
        {
            if (reader.MHY0Pos == -1)
                Logger.Info("Loading " + reader.FileName);
            else
                Logger.Info("Loading mhy0 in " + reader.FileName + " at " + string.Format("0x{0:x8}", reader.MHY0Pos));
            try
            {
                BlkFile blkFile;
                blkFile = new BlkFile(reader);
                foreach (var mhy0 in blkFile.Files)
                {
                    foreach (var file in mhy0.fileList)
                    {
                        var dummyPath = Path.Combine(Path.GetDirectoryName(reader.FullPath), file.fileName);
                        var subReader = new FileReader(dummyPath, file.stream);
                        if (subReader.FileType == FileType.AssetsFile)
                        {
                            var assetsFile = new SerializedFile(subReader, this, reader.FullPath);
                            CheckStrippedVersion(assetsFile);
                            assetsFileList.Add(assetsFile);
                            assetsFileListHash.Add(assetsFile.fileName);

                            foreach (var sharedFile in assetsFile.m_Externals)
                            {
                                var sharedFileName = sharedFile.fileName;

                                if (!importFilesHash.Contains(sharedFileName))
                                {
                                    var sharedFilePath = Path.Combine(Path.GetDirectoryName(reader.FullPath), sharedFileName);
                                    var blockInfo = new BlockInfo();

                                    if (!File.Exists(sharedFilePath))
                                    {
                                        var findFiles = Directory.GetFiles(Path.GetDirectoryName(reader.FullPath), sharedFileName, SearchOption.AllDirectories);
                                        if (findFiles.Length > 0)
                                        {
                                            sharedFilePath = findFiles[0];
                                        }
                                        else
                                        {
                                            if (BLKMap.TryGetValue(sharedFileName, out blockInfo))
                                                sharedFilePath = blockInfo.Path;
                                        }
                                    }

                                    if (File.Exists(sharedFilePath))
                                    {
                                        using (Stream shardFileReader = File.OpenRead(sharedFilePath))
                                        {
                                            FileReader fileReader = new FileReader(sharedFilePath, shardFileReader, blockInfo.Offset);
                                            LoadFile(fileReader);
                                            importFilesHash.Add(sharedFileName);
                                        }
                                        //LoadBlkFile(new FileReader(sharedFilePath), blockInfo.Offset);
                                    }
                                }
                            }
                        }
                        else
                        {
                            resourceFileReaders[file.fileName] = subReader; //TODO
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error($"Error while reading blk file {reader.FileName}", e);
            }
            finally
            {
                reader.Dispose();
            }
        }

        public void CheckStrippedVersion(SerializedFile assetsFile)
        {
            if (assetsFile.IsVersionStripped && string.IsNullOrEmpty(SpecifyUnityVersion))
            {
                throw new Exception("The Unity version has been stripped, please set the version in the options");
            }
            if (!string.IsNullOrEmpty(SpecifyUnityVersion))
            {
                assetsFile.SetVersion(SpecifyUnityVersion);
            }
        }

        public void Clear()
        {
            foreach (var assetsFile in assetsFileList)
            {
                assetsFile.Objects.Clear();
                assetsFile.reader.Close();
            }
            assetsFileList.Clear();

            foreach (var resourceFileReader in resourceFileReaders)
            {
                resourceFileReader.Value.Close();
            }
            resourceFileReaders.Clear();

            assetsFileIndexCache.Clear();
        }

        private void ReadAssets()
        {
            Logger.Info("Read assets...");

            var progressCount = assetsFileList.Sum(x => x.m_Objects.Count);
            int i = 0;
            Progress.Reset();
            foreach (var assetsFile in assetsFileList)
            {
                foreach (var objectInfo in assetsFile.m_Objects)
                {
                    var objectReader = new ObjectReader(assetsFile.reader, assetsFile, objectInfo);
                    try
                    {
                        Object obj;
                        switch (objectReader.type)
                        {
                            case ClassIDType.Animation:
                                obj = new Animation(objectReader);
                                break;
                            case ClassIDType.AnimationClip:
                                obj = new AnimationClip(objectReader);
                                break;
                            case ClassIDType.Animator:
                                obj = new Animator(objectReader);
                                break;
                            case ClassIDType.AnimatorController:
                                obj = new AnimatorController(objectReader);
                                break;
                            case ClassIDType.AnimatorOverrideController:
                                obj = new AnimatorOverrideController(objectReader);
                                break;
                            case ClassIDType.AssetBundle:
                                obj = new AssetBundle(objectReader);
                                break;
                            case ClassIDType.AudioClip:
                                obj = new AudioClip(objectReader);
                                break;
                            case ClassIDType.Avatar:
                                obj = new Avatar(objectReader);
                                break;
                            case ClassIDType.Font:
                                obj = new Font(objectReader);
                                break;
                            case ClassIDType.GameObject:
                                obj = new GameObject(objectReader);
                                break;
                            case ClassIDType.IndexObject:
                                obj = new IndexObject(objectReader);
                                break;
                            case ClassIDType.Material:
                                obj = new Material(objectReader);
                                break;
                            case ClassIDType.Mesh:
                                obj = new Mesh(objectReader);
                                break;
                            case ClassIDType.MeshFilter:
                                obj = new MeshFilter(objectReader);
                                break;
                            case ClassIDType.MeshRenderer:
                                if (!Renderer.Parsable) continue;
                                obj = new MeshRenderer(objectReader);
                                break;
                            case ClassIDType.MiHoYoBinData:
                                obj = new MiHoYoBinData(objectReader);
                                break;
                            case ClassIDType.MonoBehaviour:
                                obj = new MonoBehaviour(objectReader);
                                break;
                            case ClassIDType.MonoScript:
                                obj = new MonoScript(objectReader);
                                break;
                            case ClassIDType.MovieTexture:
                                obj = new MovieTexture(objectReader);
                                break;
                            case ClassIDType.PlayerSettings:
                                obj = new PlayerSettings(objectReader);
                                break;
                            case ClassIDType.RectTransform:
                                obj = new RectTransform(objectReader);
                                break;
                            case ClassIDType.Shader:
                                obj = new Shader(objectReader);
                                break;
                            case ClassIDType.SkinnedMeshRenderer:
                                if (!Renderer.Parsable) continue;
                                obj = new SkinnedMeshRenderer(objectReader);
                                break;
                            case ClassIDType.Sprite:
                                obj = new Sprite(objectReader);
                                break;
                            case ClassIDType.SpriteAtlas:
                                obj = new SpriteAtlas(objectReader);
                                break;
                            case ClassIDType.TextAsset:
                                obj = new TextAsset(objectReader);
                                break;
                            case ClassIDType.Texture2D:
                                obj = new Texture2D(objectReader);
                                break;
                            case ClassIDType.Transform:
                                obj = new Transform(objectReader);
                                break;
                            case ClassIDType.VideoClip:
                                obj = new VideoClip(objectReader);
                                break;
                            case ClassIDType.ResourceManager:
                                obj = new ResourceManager(objectReader);
                                break;
                            default:
                                obj = new Object(objectReader);
                                break;
                        }
                        assetsFile.AddObject(obj);
                    }
                    catch (Exception e)
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine("Unable to load object")
                            .AppendLine($"Assets {assetsFile.fileName}")
                            .AppendLine($"Path {assetsFile.originalPath}")
                            .AppendLine($"Type {objectReader.type}")
                            .AppendLine($"PathID {objectInfo.m_PathID}")
                            .Append(e);
                        Logger.Error(sb.ToString());
                    }

                    Progress.Report(++i, progressCount);
                }
            }
        }

        private void ProcessAssets()
        {
            Logger.Info("Process Assets...");

            foreach (var assetsFile in assetsFileList)
            {
                foreach (var obj in assetsFile.Objects)
                {
                    if (obj is GameObject m_GameObject)
                    {
                        foreach (var pptr in m_GameObject.m_Components)
                        {
                            if (pptr.TryGet(out var m_Component))
                            {
                                switch (m_Component)
                                {
                                    case Transform m_Transform:
                                        m_GameObject.m_Transform = m_Transform;
                                        break;
                                    case MeshRenderer m_MeshRenderer:
                                        m_GameObject.m_MeshRenderer = m_MeshRenderer;
                                        break;
                                    case MeshFilter m_MeshFilter:
                                        m_GameObject.m_MeshFilter = m_MeshFilter;
                                        break;
                                    case SkinnedMeshRenderer m_SkinnedMeshRenderer:
                                        m_GameObject.m_SkinnedMeshRenderer = m_SkinnedMeshRenderer;
                                        break;
                                    case Animator m_Animator:
                                        m_GameObject.m_Animator = m_Animator;
                                        break;
                                    case Animation m_Animation:
                                        m_GameObject.m_Animation = m_Animation;
                                        break;
                                }
                            }
                        }
                    }
                    else if (obj is SpriteAtlas m_SpriteAtlas)
                    {
                        if (m_SpriteAtlas.m_IsVariant)
                        {
                            continue;
                        }
                        foreach (var m_PackedSprite in m_SpriteAtlas.m_PackedSprites)
                        {
                            if (m_PackedSprite.TryGet(out var m_Sprite))
                            {
                                if (m_Sprite.m_SpriteAtlas.IsNull)
                                {
                                    m_Sprite.m_SpriteAtlas.Set(m_SpriteAtlas);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
