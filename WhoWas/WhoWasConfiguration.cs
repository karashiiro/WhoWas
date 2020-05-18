using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Dalamud.Configuration;
using Dalamud.Plugin;
using Newtonsoft.Json;

namespace WhoWas
{
    public class WhoWasConfiguration : IPluginConfiguration
    {
        public int Version { get; set; }

        [JsonIgnore]
        public IList<Character> Characters { get; private set; }

        // ReSharper disable once MemberCanBePrivate.Global
        public string InternalCharacterList { get; set; }

        public WhoWasConfiguration()
        {
            Characters = new List<Character>();
        }

        [JsonIgnore]
        private DalamudPluginInterface pluginInterface;

        public void Initialize(DalamudPluginInterface pluginInterface)
        {
            if (InternalCharacterList != null)
                Decompress();
            this.pluginInterface = pluginInterface;
        }

        public void Save()
        {
            Compress();
            this.pluginInterface.SavePluginConfig(this);
        }

        private void Compress()
        {
            using var bodyStream = new MemoryStream(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(Characters)));
            using var compressedBodyStream = new MemoryStream();

            var gzipStream = new GZipStream(compressedBodyStream, CompressionMode.Compress);
            bodyStream.CopyTo(gzipStream);
            gzipStream.Dispose();

            InternalCharacterList = Convert.ToBase64String(compressedBodyStream.ToArray());
        }

        private void Decompress()
        {
            using var internalBodyStream = new MemoryStream(Convert.FromBase64String(InternalCharacterList));
            using var decompressedBodyStream = new MemoryStream();

            var gzipStream = new GZipStream(internalBodyStream, CompressionMode.Decompress);
            gzipStream.CopyTo(decompressedBodyStream);
            gzipStream.Dispose();

            Characters = JsonConvert.DeserializeObject<IList<Character>>(Encoding.UTF8.GetString(decompressedBodyStream.ToArray()));
        }
    }
}
