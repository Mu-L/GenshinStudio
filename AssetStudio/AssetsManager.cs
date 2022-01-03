using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using static AssetStudio.ImportHelper;

namespace AssetStudio
{
    public class AssetsManager
    {
        public string SpecifyUnityVersion;
        public List<SerializedFile> assetsFileList = new List<SerializedFile>();

        internal Dictionary<string, int> assetsFileIndexCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<string, BinaryReader> resourceFileReaders = new Dictionary<string, BinaryReader>(StringComparer.OrdinalIgnoreCase);

        private List<string> importFiles = new List<string>();
        private HashSet<string> importFilesHash = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> assetsFileListHash = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<Guid, string> cabMap = new Dictionary<Guid, string>();

        public void BuildCABMap(string[] blks)
        {
            // the cab map is comprised of an array of paths followed by a map of cab identifiers to indices into that path array
            // it would be more correct to load only the required mhy0 from the .blk, but i'm lazy
            // TODO: this should be moved into library code and not gui code, also should be made async
            using (var mapFile = File.Create("cabMap.bin"))
            {
                using (var mapWriter = new BinaryWriter(mapFile))
                {
                    int collisions = 0;
                    var cabMapInt = new Dictionary<Guid, int>();
                    cabMap.Clear();
                    mapWriter.Write(blks.Length);
                    for (int i = 0; i < blks.Length; i++)
                    {
                        Logger.Info(String.Format("processing blk {0}/{1} ({2})", i + 1, blks.Length, blks[i]));
                        Progress.Report(i + 1, blks.Length);
                        mapWriter.Write(blks[i]);

                        var blkFile = new BlkFile(new FileReader(blks[i]));
                        foreach (var mhy0 in blkFile.Files)
                        {
                            if (mhy0.ID == Guid.Empty)
                                continue;
                            if (cabMap.ContainsKey(mhy0.ID))
                            {
                                //throw new InvalidDataException("mhy0 id collision found");
                                // i don't think i can do much about this
                                // colliding mhy0s appear to just contain the same data anyways...
                                //Logger.Warning(String.Format("mhy0 id collision! {0} {1}", mhy0.ID, cabDuplicateMap[mhy0.ID]));
                                collisions++;
                                continue;
                            }
                            cabMapInt.Add(mhy0.ID, i);
                            cabMap.Add(mhy0.ID, blks[i]);
                        }
                    }
                    mapWriter.Write(cabMap.Count);
                    foreach (var mapEntry in cabMapInt)
                    {
                        mapWriter.Write(mapEntry.Key.ToByteArray()); // ToByteArray has a weird order
                        mapWriter.Write(mapEntry.Value);
                    }
                    Logger.Info(String.Format("finished processing {0} blks with {1} cabs and {2} id collisions", blks.Length, cabMap.Count, collisions));
                }
            }
        }

        public void LoadCABMap()
        {
            Logger.Info(String.Format("loading cab map"));
            try
            {
                using (var mapFile = File.OpenRead("cabMap.bin"))
                {
                    using (var mapReader = new BinaryReader(mapFile))
                    {
                        cabMap.Clear();
                        var blkCount = mapReader.ReadInt32();
                        var blkPaths = new List<string>();
                        for (int i = 0; i < blkCount; i++)
                        {
                            blkPaths.Add(mapReader.ReadString());
                        }

                        var mhy0Count = mapReader.ReadInt32();
                        for (int i = 0; i < mhy0Count; i++)
                        {
                            cabMap.Add(new Guid(mapReader.ReadBytes(16)), blkPaths[mapReader.ReadInt32()]);
                        }
                        Logger.Info(String.Format("loaded cab map with {0} blks and {1} entries", blkCount, mhy0Count));
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error("couldn't load the cab map, please rebuild it");
                Console.WriteLine(e.ToString());
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
                case FileType.BlkFile:
                    LoadBlkFile(reader);
                    break;
            }
        }

        private void LoadAssetsFile(FileReader reader)
        {
            if (!assetsFileListHash.Contains(reader.FileName))
            {
                Logger.Info($"Loading {reader.FullPath}");
                try
                {
                    var assetsFile = new SerializedFile(reader, this);
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

        private SerializedFile LoadAssetsFromMemory(FileReader reader, string originalPath, string unityVersion = null)
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
                    return assetsFile;
                }
                catch (Exception e)
                {
                    Logger.Error($"Error while reading assets file {reader.FullPath} from {Path.GetFileName(originalPath)}", e);
                    resourceFileReaders.Add(reader.FileName, reader);
                }
            }
            else
                Logger.Info($"Skipping {originalPath} ({reader.FileName})");
            return null;
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

        private void LoadBlkFile(FileReader reader, Guid? targetId = null)
        {
            if (targetId == null)
                Logger.Info("Loading " + reader.FileName);
            else
                Logger.Info("Loading " + reader.FileName + " with target ID " + targetId.Value.ToString());
            try
            {
                var blkFile = new BlkFile(reader);
                bool targetFound = false;
                for (int i = 0; i < blkFile.Files.Count; i++)
                {
                    //Console.WriteLine(blkFile.Files[i].ID);
                    if (targetId.HasValue && targetId.Value != blkFile.Files[i].ID)
                        continue;
                    targetFound = true;
                    // TODO: proper dummyPath
                    var dummyPath = Path.Combine(Path.GetDirectoryName(reader.FullPath),
                        string.Format("{0}_{1}", reader.FileName, blkFile.Files[i].ID.ToString()));
                    var subReader = new FileReader(dummyPath, new MemoryStream(blkFile.Files[i].Data));
                    var asset = LoadAssetsFromMemory(subReader, dummyPath);
                    if (asset == null)
                    {
                        //Logger.Error("what");
                        continue;
                    }
                    foreach (var sharedFile in asset.m_Externals)
                    {
                        var sharedFileName = sharedFile.fileName;
                        var sharedFileNameWithID = string.Format("{0}_{1}", sharedFileName, sharedFile.cabId.ToString());

                        if (!sharedFileName.EndsWith(".blk"))
                        {
                            // this will directly load .blk files, so anything that isn't one is not supported
                            Logger.Warning(String.Format("attempted to load non-blk shared file ({0})", sharedFileName));
                            continue;
                        }

                        if (!importFilesHash.Contains(sharedFileNameWithID))
                        {
                            var sharedFilePath = Path.Combine(Path.GetDirectoryName(reader.FullPath), sharedFileName);
                            if (!File.Exists(sharedFilePath))
                            {
                                var findFiles = Directory.GetFiles(Path.GetDirectoryName(reader.FullPath), sharedFileName, SearchOption.AllDirectories);
                                if (findFiles.Length > 0)
                                {
                                    sharedFilePath = findFiles[0];
                                }
                            }

                            if (File.Exists(sharedFilePath))
                            {
                                // TODO: proper integration with the loading bar
                                LoadBlkFile(new FileReader(sharedFilePath), sharedFile.cabId);
                                //importFiles.Add(sharedFilePath);
                                importFilesHash.Add(sharedFileNameWithID);
                            }
                        }
                    }
                }
                if (blkFile.Files.Count > 0 && !targetFound)
                    Logger.Warning("failed to find target mhy0");
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
                                obj = new MeshRenderer(objectReader);
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
                            // mihoyo
                            case ClassIDType.MiHoYoBinData:
                                obj = new MiHoYoBinData(objectReader);
                                break;
                            default:
                                //Logger.Warning(String.Format("unhandled type {0}", objectReader.type.ToString()));
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