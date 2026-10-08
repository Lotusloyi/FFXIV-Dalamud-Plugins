namespace FishClock;

/// <summary>
/// 艾欧泽亚时间与天气预测算法，逐行移植自鱼糕（fish.ffmomola.com）前端实现，
/// 与该网站及游戏内天气完全保持一致。
/// </summary>
public static class EorzeaWeather
{
    // 常量（与鱼糕前端一致）
    private const double EorzeaMultiplier = 3600.0 / 175.0; // 地球毫秒 → 艾欧泽亚毫秒
    private const long EtHourMs = 3_600_000;                // 1 艾欧泽亚小时（毫秒）
    private const long EtDayMs = 24 * EtHourMs;             // 1 艾欧泽亚天
    private const long WeatherIntervalMs = 8 * EtHourMs;    // 天气周期 = 8 艾欧泽亚小时

    public static long NowEarthMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public static long ToEorzea(long earthMs) => (long)(earthMs * EorzeaMultiplier);

    public static long ToEarth(long etMs) => (long)Math.Ceiling(etMs / EorzeaMultiplier);

    public static int GetEtHour(long etMs) => (int)((etMs / EtHourMs) % 24);

    /// <summary>当前艾欧泽亚时间字符串（HH:mm）。</summary>
    public static string FormatEtTime(long earthMs)
    {
        var et = ToEorzea(earthMs);
        return $"{et / EtHourMs % 24:00}:{et / 60_000 % 60:00}";
    }

    /// <summary>天气种子算法（ZB 函数）。</summary>
    private static uint CalcSeed(long earthMs)
    {
        var t = earthMs / 1000;          // 地球秒
        var n = t / 175.0;
        var r = (n + 8 - n % 8) % 24;    // 0 / 8 / 16
        var o = (t / 4200) * 100 + (long)r;
        var o32 = (int)o;
        unchecked
        {
            var c = (uint)((o32 << 11) ^ o32);
            return ((c >> 8) ^ c) % 100;
        }
    }

    /// <summary>
    /// 计算某个地球时间点、某张天气表下的天气 ID。
    /// rates 为 [(天气ID, 累计概率)]，按累计概率升序。
    /// </summary>
    public static int GetWeather(List<(int WeatherId, int Cumulative)>? rates, long earthMs)
    {
        if (rates == null || rates.Count == 0)
            return 0;
        var seed = CalcSeed(earthMs);
        foreach (var (weatherId, cumulative) in rates)
        {
            if (seed < cumulative)
                return weatherId;
        }
        return 0;
    }

    /// <summary>未来几次天气变迁（地球时间起点 + 天气ID）。</summary>
    public static List<(long StartEarthMs, int WeatherId)> GetNextWeathers(
        List<(int WeatherId, int Cumulative)>? rates, int count, long? fromEarthMs = null)
    {
        var result = new List<(long, int)>();
        var earthMs = fromEarthMs ?? NowEarthMs;
        var et = ToEorzea(earthMs);
        var interval = et - et % WeatherIntervalMs; // 当前天气周期起点（艾欧泽亚毫秒）
        for (var i = 0; i < count; i++)
        {
            var startEarth = ToEarth(interval);
            result.Add((startEarth, GetWeather(rates, startEarth)));
            interval += WeatherIntervalMs;
        }
        return result;
    }

    /// <summary>当前正处于哪个天气周期起点（艾欧泽亚毫秒）。</summary>
    private static long CurrentIntervalStart(long earthMs)
    {
        var et = ToEorzea(earthMs);
        return et - et % WeatherIntervalMs;
    }

    /// <summary>
    /// 计算一条鱼在某个钓点（天气表）的下一次开放窗口。
    /// 返回 (开始地球毫秒, 结束地球毫秒)；null 表示在扫描范围内没有窗口。
    /// 逻辑与鱼糕 hT/dT 函数一致。
    /// </summary>
    public static (long Start, long End)? FindNextWindow(
        FishEntry fish, List<(int WeatherId, int Cumulative)>? rates,
        int maxDays = 3, long? fromEarthMs = null)
    {
        var earthMs = fromEarthMs ?? NowEarthMs;
        var checkpoint = CurrentIntervalStart(earthMs);
        var start = checkpoint - WeatherIntervalMs; // 多扫一个周期，捕捉“进行中”的窗口
        var limit = checkpoint + EtDayMs * maxDays;

        (long Start, long End)? best = null;
        for (var t = start; t < limit && (best == null || t < best.Value.End); t += WeatherIntervalMs)
        {
            foreach (var window in CheckInterval(fish, rates, t))
            {
                if (window.End <= earthMs)
                    continue; // 已结束
                if (best == null || window.Start < best.Value.Start)
                    best = window;
            }
        }
        return best == null || best.Value.End <= earthMs ? null : best;
    }

    /// <summary>枚举后续若干个开放窗口（含进行中）。</summary>
    public static List<(long Start, long End)> FindWindows(
        FishEntry fish, List<(int WeatherId, int Cumulative)>? rates,
        int count, int maxDays = 5, long? fromEarthMs = null)
    {
        var result = new List<(long Start, long End)>();
        var earthMs = fromEarthMs ?? NowEarthMs;
        var checkpoint = CurrentIntervalStart(earthMs);
        var t = checkpoint - WeatherIntervalMs;
        var limit = checkpoint + EtDayMs * maxDays;

        for (; t < limit && result.Count < count; t += WeatherIntervalMs)
        {
            foreach (var window in CheckInterval(fish, rates, t))
            {
                if (window.End <= earthMs)
                    continue;
                if (result.Count > 0 && result[^1].End == window.Start)
                    result[^1] = (result[^1].Start, window.End); // 合并相邻窗口
                else
                    result.Add(window);
            }
        }
        return result;
    }

    /// <summary>检查单个天气周期是否满足鱼的开放条件（hT 函数移植）。</summary>
    private static IEnumerable<(long Start, long End)> CheckInterval(
        FishEntry fish, List<(int WeatherId, int Cumulative)>? rates, long intervalStartEtMs)
    {
        var hour = GetEtHour(intervalStartEtMs);
        var intervals = IntersectHours(hour, hour + 8, fish.StartHour, fish.EndHour);
        if (intervals.Count == 0)
            yield break;

        // 与鱼糕一致：previousWeatherIds（前置天气）对“上一周期”判定，weatherIds（窗口天气）对“本周期”判定
        var prevOk = fish.PreviousWeatherIds.Count == 0 ||
                     fish.PreviousWeatherIds.Contains(GetWeather(rates, ToEarth(intervalStartEtMs - WeatherIntervalMs)));
        var currOk = fish.WeatherIds.Count == 0 ||
                     fish.WeatherIds.Contains(GetWeather(rates, ToEarth(intervalStartEtMs)));
        if (!prevOk || !currOk)
            yield break;

        var dayStart = intervalStartEtMs - intervalStartEtMs % EtDayMs;
        foreach (var (h1, h2) in intervals)
        {
            yield return (ToEarth(dayStart + (long)(h1 * EtHourMs)),
                          ToEarth(dayStart + (long)(h2 * EtHourMs)));
        }
    }

    /// <summary>两个艾欧泽亚小时区间的交集（支持跨午夜，e5/t5 移植）。</summary>
    private static List<(float Start, float End)> IntersectHours(float a1, float a2, float b1, float b2)
    {
        var result = new List<(float, float)>();
        foreach (var (s, e) in SplitOverMidnight((b1, b2)))
        {
            var start = Math.Max(a1, s);
            var end = Math.Min(a2, e);
            if (start < end)
                result.Add((start, end));
        }
        return result;
    }

    private static IEnumerable<(float Start, float End)> SplitOverMidnight((float Start, float End) range)
    {
        if (range.Start > range.End)
        {
            yield return (0, range.End);
            yield return (range.Start, 24);
        }
        else
        {
            yield return range;
        }
    }

    /// <summary>格式化为艾欧泽亚时刻，如 19:00。</summary>
    public static string FormatEtHour(float hour)
    {
        if (hour >= 24) hour = 0;
        return $"{(int)hour:00}:00";
    }

    /// <summary>倒计时文本。</summary>
    public static string FormatCountdown(long targetEarthMs, long nowEarthMs)
    {
        var span = TimeSpan.FromMilliseconds(targetEarthMs - nowEarthMs);
        if (span.TotalHours >= 24)
            return $"{(int)span.TotalDays} 天 {span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}";
        return span < TimeSpan.Zero ? "00:00:00" : $"{span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}";
    }
}
