using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AssetStudio
{
    public class ObjectReader : EndianBinaryReader
    {
        public SerializedFile assetsFile;
        public long m_PathID;
        public long byteStart;
        public uint byteSize;
        public ClassIDType type;
        public SerializedType serializedType;
        public BuildTarget platform;
        public SerializedFileFormatVersion m_Version;

        public int[] version => assetsFile.version;
        public BuildType buildType => assetsFile.buildType;

        public ObjectReader(EndianBinaryReader reader, SerializedFile assetsFile, ObjectInfo objectInfo) : base(reader.BaseStream, reader.endian)
        {
            this.assetsFile = assetsFile;
            m_PathID = objectInfo.m_PathID;
            byteStart = objectInfo.byteStart;
            byteSize = objectInfo.byteSize;
            if (Enum.IsDefined(typeof(ClassIDType), objectInfo.classID))
            {
                type = (ClassIDType)objectInfo.classID;
                //Logger.Info(String.Format("object type {0} ({1})", type, objectInfo.classID));
            }
            else
            {
                Logger.Error(String.Format("unknown type {0}", objectInfo.classID));
                type = ClassIDType.UnknownType;
            }
            serializedType = objectInfo.serializedType;
            platform = assetsFile.m_TargetPlatform;
            m_Version = assetsFile.header.m_Version;
        }

        public void Reset()
        {
            Position = byteStart;
        }
    }
}
