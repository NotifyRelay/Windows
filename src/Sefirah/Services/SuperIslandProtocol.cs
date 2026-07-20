using NotifyRelay.Native;

namespace NotifyRelay.Services;

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
}
