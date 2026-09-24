using NotifyRelay.Native;

namespace NotifyRelay.Services.Protocol;

public static class SuperIslandProtocol
{
    public const string FeatureKeyName = "si_feature_id";
    public const string TerminateValue = "__END__";

    /// <summary>
    /// 计算"岛"的特征ID。
    /// 实际计算委托给 Rust Core (nrc_compute_feature_id)。
    /// </summary>
    public static string? ComputeFeatureId(
        string? superPkg,
        string? paramV2Raw,
        string? title,
        string? text,
        string? instanceId = null)
    {
        return NotifyRelayCore.Safe.ComputeFeatureId(
            superPkg ?? "",
            paramV2Raw ?? "",
            title ?? "",
            text ?? "",
            instanceId ?? ""
        );
    }

    /// <summary>
    /// 超级岛入站解析：委托给 Rust Core (nrc_parse_superisland_inbound)。
    /// 返回归一结构 JSON 字符串（featureId/packageName/appName/title/text/paramV2Raw/pics/isEnd/sourceKey）。
    /// </summary>
    public static string? ParseSuperIslandInbound(string deviceUuid, string pkg, string fullJson)
    {
        return NotifyRelayCore.Safe.ParseSuperIslandInbound(deviceUuid, pkg, fullJson);
    }
}
