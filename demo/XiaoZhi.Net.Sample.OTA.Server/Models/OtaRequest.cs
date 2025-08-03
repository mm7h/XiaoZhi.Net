//The file is referenced from https://github.com/zhulige/xiaozhi-sharp
using System.Text.Json.Serialization;

namespace Demo.OTA.Server.Models
{
    public class OtaRequest
    {
        public ApplicationInfo Application { get; set; } = new ApplicationInfo();
        public string MacAddress { get; set; } = "";
        public string Uuid { get; set; } = "";
        public string? ChipModelName { get; set; }
        public long? FlashSize { get; set; }
        public long? PsramSize { get; set; }
        public List<PartitionInfo>? PartitionTable { get; set; }
        public BoardInfo Board { get; set; } = new BoardInfo();
        public int? Version { get; set; }
        public string? Language { get; set; }
        public long? MinimumFreeHeapSize { get; set; }

        public OtaInfo? Ota { get; set; }
    }

    /// <summary>
    /// 应用程序信息
    /// </summary>
    public class ApplicationInfo
    {
        public string? Name { get; set; } = "xiaozhi";

        public string Version { get; set; } = "1.0.0";

        [JsonPropertyName("elf_sha256")]
        public string ElfSha256 { get; set; } = "";

        public string? CompileTime { get; set; }

        public string? IdfVersion { get; set; }
    }

    /// <summary>
    /// 分区信息
    /// </summary>
    public class PartitionInfo
    {
        public string Label { get; set; } = "";

        public int Type { get; set; }

        public int Subtype { get; set; }

        public long Address { get; set; }

        public long Size { get; set; }
    }

    /// <summary>
    /// 开发板信息
    /// </summary>
    public class BoardInfo
    {
        public string Type { get; set; } = "";

        public string Name { get; set; } = "";

        public string? Ssid { get; set; }

        public int? Rssi { get; set; }

        public int? Channel { get; set; }

        public string? Ip { get; set; }

        public string? Mac { get; set; }

        public string? Revision { get; set; }

        public string? Carrier { get; set; }

        public string? Csq { get; set; }

        public string? Imei { get; set; }

        public string? Iccid { get; set; }
    }

    /// <summary>
    /// OTA信息
    /// </summary>
    public class OtaInfo
    {
        public string Label { get; set; } = "";
    }
}
