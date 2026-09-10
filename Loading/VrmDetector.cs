using System;
using System.IO;
using System.Text;

namespace AvatarReplacer
{
    public enum VrmFormat
    {
        Unknown,
        Vrm0X,
        Vrm10
    }

    public sealed class VrmDetectionResult
    {
        public VrmFormat Format { get; internal set; }
        public bool IsGlb { get; internal set; }
        public int JsonChunkBytes { get; internal set; }
        public string Error { get; internal set; }
    }

    /// <summary>Reads the GLB JSON chunk instead of guessing from the first 1 MB.</summary>
    public static class VrmDetector
    {
        private const uint GlbMagic = 0x46546C67; // glTF
        private const uint JsonChunkType = 0x4E4F534A; // JSON

        public static VrmDetectionResult Detect(string path)
        {
            var result = new VrmDetectionResult { Format = VrmFormat.Unknown };
            try
            {
                using (var fs = File.OpenRead(path))
                using (var br = new BinaryReader(fs))
                {
                    if (fs.Length < 20)
                    {
                        result.Error = "File is too small to be a GLB.";
                        return result;
                    }

                    uint magic = br.ReadUInt32();
                    uint version = br.ReadUInt32();
                    uint length = br.ReadUInt32();
                    result.IsGlb = magic == GlbMagic && version == 2 && length <= fs.Length;
                    if (!result.IsGlb)
                    {
                        result.Error = "Not a valid GLB v2 container.";
                        return result;
                    }

                    while (fs.Position + 8 <= fs.Length)
                    {
                        uint chunkLength = br.ReadUInt32();
                        uint chunkType = br.ReadUInt32();
                        if (chunkLength > fs.Length - fs.Position || chunkLength > int.MaxValue)
                            break;

                        if (chunkType == JsonChunkType)
                        {
                            result.JsonChunkBytes = (int)chunkLength;
                            byte[] jsonBytes = br.ReadBytes((int)chunkLength);
                            string json = Encoding.UTF8.GetString(jsonBytes).TrimEnd('\0', ' ', '\t', '\r', '\n');

                            if (json.IndexOf("VRMC_vrm", StringComparison.Ordinal) >= 0)
                                result.Format = VrmFormat.Vrm10;
                            else if (json.IndexOf("\"VRM\"", StringComparison.Ordinal) >= 0 ||
                                     json.IndexOf("VRM", StringComparison.Ordinal) >= 0)
                                result.Format = VrmFormat.Vrm0X;
                            return result;
                        }

                        fs.Position += chunkLength;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }
            return result;
        }
    }
}
