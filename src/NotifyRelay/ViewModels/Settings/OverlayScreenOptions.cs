using NotifyRelay.Services.Overlay;

namespace NotifyRelay.ViewModels.Settings;

/// <summary>
/// 覆盖层设置页共用的目标屏选项构建器。
/// 统一「PRIMARY + 显示器枚举 + 回显选中」三段逻辑，
/// 替代时钟 / 心率 / 罗技电池 / DeepSeek 余额 4 份逐字重复的副本。
/// </summary>
internal static class OverlayScreenOptions
{
    /// <summary>主显示器选项的固定 Id（与渲染层 IsTargetScreen 的 "primary" 判定对应）。</summary>
    public const string PrimaryId = "PRIMARY";

    /// <summary>
    /// 构建目标屏下拉选项并回显已保存的选中项。
    /// 枚举失败时仅保留主显示器选项（与各页原实现一致）。
    /// </summary>
    /// <param name="screenCatalog">显示器枚举契约；为 null 时仅返回主显示器选项。</param>
    /// <param name="savedId">已保存的目标屏 Id。</param>
    /// <returns>选项列表与选中项。</returns>
    public static (List<ScreenOption> Screens, ScreenOption Selected) Build(
        IScreenCatalog? screenCatalog, string? savedId)
    {
        var screens = new List<ScreenOption>
        {
            new() { Id = PrimaryId, DisplayName = "主显示器" }
        };

        try
        {
            var list = screenCatalog?.GetScreens();
            if (list != null)
            {
                int index = 1;
                foreach (var screen in list)
                {
                    screens.Add(new ScreenOption
                    {
                        Id = screen.DeviceName,
                        DisplayName = $"显示器 {index}{(screen.IsPrimary ? " (主)" : "")} · {screen.DeviceName}"
                    });
                    index++;
                }
            }
        }
        catch
        {
            // 枚举失败时仅保留主显示器选项
        }

        // 回显比较统一为 OrdinalIgnoreCase：取值来自同一列表，忽略大小写是超集，无回归
        var selected = screens.FirstOrDefault(s =>
            string.Equals(s.Id, savedId, StringComparison.OrdinalIgnoreCase)) ?? screens[0];
        return (screens, selected);
    }

    /// <summary>就地构建：清空并填充传入集合，同时返回选中项（供 ViewModel 字段赋值）。</summary>
    public static ScreenOption BuildInto(List<ScreenOption> target,
        IScreenCatalog? screenCatalog, string? savedId)
    {
        var (screens, selected) = Build(screenCatalog, savedId);
        target.Clear();
        target.AddRange(screens);
        return selected;
    }
}
