using System.Text.Json;
using System.Text.Json.Serialization;

namespace FishClock;

/// <summary>一条鱼的开放窗口数据（解码自鱼糕 protobuf 数据）。</summary>
public sealed class FishEntry
{
    [JsonPropertyName("item")] public uint ItemId { get; set; }
    [JsonPropertyName("s")] public float StartHour { get; set; }
    [JsonPropertyName("e")] public float EndHour { get; set; }
    [JsonPropertyName("spots")] public List<uint> SpotIds { get; set; } = [];
    [JsonPropertyName("w")] public List<int> WeatherIds { get; set; } = [];
    [JsonPropertyName("pw")] public List<int> PreviousWeatherIds { get; set; } = [];
    [JsonPropertyName("fol")] public bool HasFolklore { get; set; }
    [JsonPropertyName("int")] public int IntuitionSeconds { get; set; }
    [JsonPropertyName("pred")] public List<uint> PredatorItemIds { get; set; } = [];

    /// <summary>是否无时间/天气限制（常驻）。</summary>
    public bool AlwaysAvailable =>
        StartHour == 0 && EndHour == 24 && WeatherIds.Count == 0 && PreviousWeatherIds.Count == 0;
}

/// <summary>一个钓点。</summary>
public sealed class SpotEntry
{
    [JsonPropertyName("id")] public uint Id { get; set; }
    [JsonPropertyName("pn")] public uint PlaceNameId { get; set; }
    [JsonPropertyName("tt")] public uint TerritoryTypeId { get; set; }
    [JsonPropertyName("items")] public List<uint> ItemIds { get; set; } = [];
}

/// <summary>解码后的内置数据集合。</summary>
public sealed class FishDataSet
{
    public List<FishEntry> Fish { get; set; } = [];
    public List<SpotEntry> Spots { get; set; } = [];
    public Dictionary<uint, SpotEntry> SpotById { get; } = [];

    public static FishDataSet Load()
    {
        var asm = typeof(FishDataSet).Assembly;
        using var stream = asm.GetManifestResourceStream("FishClock.Data.fishdata.json.gz")
                           ?? throw new InvalidDataException("找不到内置鱼类数据 fishdata.json.gz");
        using var gzip = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress);
        var set = JsonSerializer.Deserialize<FishDataSet>(gzip, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidDataException("鱼类数据解析失败");
        foreach (var spot in set.Spots)
            set.SpotById[spot.Id] = spot;
        return set;
    }
}
