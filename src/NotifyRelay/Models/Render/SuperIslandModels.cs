namespace NotifyRelay.Models.Render;

/// <summary>
/// 超级岛 param_v2 结构化模型（从 Android superislandui model 层移植）。
/// </summary>

// ---------- 总容器 ----------

/// <summary>
/// param_v2 总容器，按字段分支选择不同模板组件。
/// </summary>
public sealed class ParamV2
{
    public BaseInfoData? BaseInfo { get; set; }
    public ChatInfoData? ChatInfo { get; set; }
    public HighlightInfoData? HighlightInfo { get; set; }
    public AnimTextInfoData? AnimTextInfo { get; set; }
    public PicInfoData? PicInfo { get; set; }
    public ProgressData? ProgressInfo { get; set; }
    public MultiProgressData? MultiProgressInfo { get; set; }
    public List<ActionData>? Actions { get; set; }
    public HintInfoData? HintInfo { get; set; }
    public TextButtonData? TextButton { get; set; }
    public ParamIslandData? ParamIsland { get; set; }
    public string? Business { get; set; }
    public string? AodPic { get; set; }
    public string? PicFunction { get; set; }
}

// ---------- templates ----------

/// <summary>基础信息模板：文本组件1和2。</summary>
public sealed class BaseInfoData
{
    public int Type { get; set; } = 1;
    public string? Title { get; set; }
    public string? SubTitle { get; set; }
    public string? ExtraTitle { get; set; }
    public string? SpecialTitle { get; set; }
    public string? Content { get; set; }
    public string? SubContent { get; set; }
    public string? PicFunction { get; set; }
    public string? PicFunctionDark { get; set; }
    public string? ColorTitle { get; set; }
    public string? ColorSubTitle { get; set; }
    public string? ColorExtraTitle { get; set; }
    public string? ColorSpecialTitle { get; set; }
    public string? ColorSpecialBg { get; set; }
    public string? ColorContent { get; set; }
    public string? ColorSubContent { get; set; }
    public bool ShowDivider { get; set; }
    public bool ShowContentDivider { get; set; }
}

/// <summary>聊天信息模板：IM图文组件（头像 + 主要/次要文本）。</summary>
public sealed class ChatInfoData
{
    public string? PicProfile { get; set; }
    public string? PicProfileDark { get; set; }
    public string? AppIconPkg { get; set; }
    public string? Title { get; set; }
    public string? Content { get; set; }
    public TimerInfoData? TimerInfo { get; set; }
    public string? ColorTitle { get; set; }
    public string? ColorContent { get; set; }
}

/// <summary>高亮信息模板：强调图文组件。</summary>
public sealed class HighlightInfoData
{
    public string? Title { get; set; }
    public TimerInfoData? TimerInfo { get; set; }
    public string? Content { get; set; }
    public string? PicFunction { get; set; }
    public string? PicFunctionDark { get; set; }
    public string? SubContent { get; set; }
    public int? Type { get; set; }
    public string? ColorTitle { get; set; }
    public string? ColorContent { get; set; }
    public string? ColorSubContent { get; set; }
    public string? BigImageLeft { get; set; }
    public string? BigImageRight { get; set; }
    public bool IconOnly { get; set; }
}

/// <summary>提示信息模板：按钮组件2和3。</summary>
public sealed class HintInfoData
{
    public int Type { get; set; } = 1;
    public string? Title { get; set; }
    public TimerInfoData? TimerInfo { get; set; }
    public string? SubTitle { get; set; }
    public string? Content { get; set; }
    public string? SubContent { get; set; }
    public string? PicContent { get; set; }
    public string? ColorTitle { get; set; }
    public string? ColorSubTitle { get; set; }
    public string? ColorContent { get; set; }
    public string? ColorSubContent { get; set; }
    public string? ColorContentBg { get; set; }
    public ActionData? ActionInfo { get; set; }
}

/// <summary>图片信息模板：识别图形组件。</summary>
public sealed class PicInfoData
{
    public int Type { get; set; } = 1;
    public string? Pic { get; set; }
    public string? PicDark { get; set; }
    public ActionData? ActionInfo { get; set; }
    public string? Title { get; set; }
    public string? ColorTitle { get; set; }
}

// ---------- components ----------

/// <summary>进度信息。</summary>
public sealed class ProgressData
{
    public int Progress { get; set; }
    public string? ColorProgress { get; set; }
    public string? ColorProgressEnd { get; set; }
    public string? PicForward { get; set; }
    public string? PicMiddle { get; set; }
    public string? PicMiddleUnselected { get; set; }
    public string? PicEnd { get; set; }
    public string? PicEndUnselected { get; set; }
    public bool IsCCW { get; set; }
    public bool IsAutoProgress { get; set; }
}

/// <summary>多段进度信息。</summary>
public sealed class MultiProgressData
{
    public string Title { get; set; } = string.Empty;
    public int Progress { get; set; }
    public string? Color { get; set; }
    public int? Points { get; set; }
    public string? PicForward { get; set; }
    public string? PicForwardWait { get; set; }
    public string? PicForwardBox { get; set; }
    public string? PicMiddle { get; set; }
    public string? PicMiddleUnselected { get; set; }
    public string? PicEnd { get; set; }
    public string? PicEndUnselected { get; set; }
}

/// <summary>计时器信息。</summary>
public sealed class TimerInfoData
{
    /// <summary>-2 倒计时暂停，-1 倒计时开始，0 默认（正计时开始），1 正计时中，2 正计时暂停。</summary>
    public int TimerType { get; set; }
    public long TimerWhen { get; set; }
    public long TimerTotal { get; set; }
    public long TimerSystemCurrent { get; set; }
}

/// <summary>操作信息（按钮）。</summary>
public sealed class ActionData
{
    public string? Action { get; set; }
    public string? ActionIcon { get; set; }
    public string? ActionIconDark { get; set; }
    public string? ActionTitle { get; set; }
    public string? ActionTitleColor { get; set; }
    public string? ActionBgColor { get; set; }
    public int? ActionIntentType { get; set; }
    public string? ActionIntent { get; set; }
    public bool? ClickWithCollapse { get; set; }
    public int? Type { get; set; }
    public ProgressData? ProgressInfo { get; set; }
}

/// <summary>文本按钮模板：按钮组（纯文字按钮，1-2 个）。</summary>
public sealed class TextButtonData
{
    public List<ActionData> Actions { get; set; } = [];
}

/// <summary>动画文本组件。</summary>
public sealed class AnimTextInfoData
{
    public string? IconSrc { get; set; }
    public string? IconSrcDark { get; set; }
    public string? Title { get; set; }
    public string? Content { get; set; }
    public TimerInfoData? TimerInfo { get; set; }
    public string? ColorTitle { get; set; }
    public string? ColorContent { get; set; }
}

// ---------- param_island / A-B 区 ----------

/// <summary>param_island 节点：小岛摘要区 + 大岛摘要区。</summary>
public sealed class ParamIslandData
{
    public SmallIslandAreaData? SmallIslandArea { get; set; }
    public BigIslandAreaData? BigIslandArea { get; set; }
}

/// <summary>小岛区域（收起态）摘要。</summary>
public sealed class SmallIslandAreaData
{
    public string? PrimaryText { get; set; }
    public string? SecondaryText { get; set; }
    public string? IconKey { get; set; }
    public ProgressData? ProgressInfo { get; set; }
}

/// <summary>大岛区域（摘要态 A/B 区）承载。</summary>
public sealed class BigIslandAreaData
{
    public string? PrimaryText { get; set; }
    public string? SecondaryText { get; set; }
    public string? LeftImage { get; set; }
    public string? RightImage { get; set; }
    public string? VerificationCode { get; set; }
    public bool IsVerificationCode { get; set; }
    public AComponentData? AComponent { get; set; }
    public BComponentData? BComponent { get; set; }
}

/// <summary>A区（左侧 imageTextInfoLeft）组件。</summary>
public abstract class AComponentData
{
    public int Type { get; protected set; }
    public string? Title { get; set; }
    public string? Content { get; set; }
    public bool NarrowFont { get; set; }
    public bool ShowHighlightColor { get; set; }
    public string? PicKey { get; set; }
}

/// <summary>A区：图文组件1。</summary>
public sealed class AImageText1Data : AComponentData
{
    public AImageText1Data() => Type = 1;
}

/// <summary>A区：图文组件5（静态图标）。</summary>
public sealed class AImageText5Data : AComponentData
{
    public AImageText5Data() => Type = 5;
}

/// <summary>B区（右侧 imageTextInfoRight 及其它组件）接口。</summary>
public abstract class BComponentData
{
    public string Kind { get; set; } = "empty";
}

/// <summary>B区：图文组件2/3/6 与纯文本组件。</summary>
public sealed class BImageTextData : BComponentData
{
    public string? FrontTitle { get; set; }
    public string? Title { get; set; }
    public string? Content { get; set; }
    public bool NarrowFont { get; set; }
    public bool ShowHighlightColor { get; set; }
    public string? PicKey { get; set; }
}

/// <summary>B区：定宽/等宽数字文本（digit 或计时器）。</summary>
public sealed class BDigitInfoData : BComponentData
{
    public string? Digit { get; set; }
    public TimerInfoData? Timer { get; set; }
    public string? Content { get; set; }
    public bool ShowHighlightColor { get; set; }
}

/// <summary>B区：进度文本（进度圆环 + 文本）。</summary>
public sealed class BProgressTextInfoData : BComponentData
{
    public string? FrontTitle { get; set; }
    public string? Title { get; set; }
    public string? Content { get; set; }
    public bool NarrowFont { get; set; }
    public bool ShowHighlightColor { get; set; }
    public int Progress { get; set; }
    public string? ColorReach { get; set; }
    public string? ColorUnReach { get; set; }
    public bool IsCCW { get; set; }
    public string? PicKey { get; set; }
}

/// <summary>B区：图片组件。</summary>
public sealed class BPicInfoData : BComponentData
{
    public string PicKey { get; set; } = string.Empty;
    public int Type { get; set; } = 1;
}

/// <summary>B区：空占位。</summary>
public sealed class BEmptyData : BComponentData
{
    public BEmptyData() => Kind = "empty";
}
