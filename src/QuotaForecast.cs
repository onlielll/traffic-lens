using System;

namespace ProxyTrafficFloat;

public readonly record struct UsageTrend(double DailyBytes, int Days);

// Only a few scalar values live in the resident; historical rows stay in SQLite.
public readonly record struct QuotaForecast(double RemainingBytes, double DailyBytes, int RecordedDays)
{
    public const double BytesPerGB = 1_000_000_000;
    public double? Days => DailyBytes > 0 && RecordedDays > 0 ? RemainingBytes / DailyBytes : null;
    public string DurationText => RemainingBytes <= 0 ? Strings.T("额度已用完") : Days switch
    {
        null => Strings.T("暂无可用趋势"),
        < 1 => Strings.T("不到 1 天"),
        > 999 => Strings.T("超过 999 天"),
        var value => Strings.Format("约 {0:0} 天", value)
    };
    public static QuotaForecast? Calculate(Settings config, long observedBytes, UsageTrend trend) => !config.HasQuota ? null :
        new(Math.Max(0, config.QuotaGB * BytesPerGB - Math.Max(0, observedBytes - config.QuotaBaselineBytes)), trend.DailyBytes, trend.Days);
}