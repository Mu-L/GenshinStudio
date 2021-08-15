using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssetStudio
{
    public sealed class MiHoYoBinData : Object
    {
        byte[] m_Data = null;

        public MiHoYoBinData(ObjectReader reader) : base(reader)
        {
            m_Data = reader.ReadBytes((int)reader.byteSize);
        }

        public new string Dump()
        {
            try
            {
                return Encoding.UTF8.GetString(m_Data);
            }
            catch
            {
                Logger.Warning("couldn't encode bin data as string");
                return null;
            }
        }
    }
}
