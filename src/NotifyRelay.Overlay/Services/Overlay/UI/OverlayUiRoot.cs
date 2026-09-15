using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace NotifyRelay.Services.Overlay.UI;

/// <summary>
/// 每屏一棵的声明式 UI 运行时：持有 <b>TopCardsRoot</b>（媒体卡片 + 超级岛）与
/// <b>ElementsRoot</b>（5 个叠加层元素）两个根，驱动 Compose → Measure → Place → Paint 全流程。
/// 两个根分开是为了保持与现状一致的 z 序：顶部卡片 → 弹幕 → 元素。
/// 仅渲染线程访问。
/// </summary>
internal sealed class OverlayUiRoot : IDisposable
{
    private readonly UiRootNode _topCardsRoot = new();
    private readonly UiRootNode _elementsRoot = new();
    private readonly OverlayComposer _topCardsComposer;
    private readonly OverlayComposer _elementsComposer;
    private readonly BrushCache _brushes = new();
    private readonly MeasureScope _measureScope;

    public OverlayUiRoot(ID2D1Factory d2dFactory, IDWriteFactory dwFactory)
    {
        _measureScope = new MeasureScope(dwFactory, d2dFactory);
        _topCardsComposer = new OverlayComposer(_topCardsRoot);
        _elementsComposer = new OverlayComposer(_elementsRoot);
    }

    /// <summary>顶部卡片根（媒体 + 超级岛）。</summary>
    public UiRootNode TopCardsRoot => _topCardsRoot;

    /// <summary>元素根（心率 / 时钟 / 键盘 / 罗技 / 余额）。</summary>
    public UiRootNode ElementsRoot => _elementsRoot;

    // ---------- 顶部卡片 ----------

    /// <summary>开始顶部卡片的 Compose（调用方在回调内声明子树）。</summary>
    public OverlayComposer BeginTopCards()
    {
        _topCardsComposer.Begin();
        return _topCardsComposer;
    }

    /// <summary>结束顶部卡片 Compose，并执行 Measure / Place。</summary>
    public void EndTopCards(ScreenOverlay o)
    {
        _topCardsComposer.End();
        Layout(_topCardsRoot, o);
    }

    /// <summary>绘制顶部卡片。</summary>
    public void PaintTopCards(PaintScope s) => _topCardsRoot.PaintNode(s);

    // ---------- 元素 ----------

    /// <summary>开始元素层的 Compose（调用方在回调内声明子树）。</summary>
    public OverlayComposer BeginElements()
    {
        _elementsComposer.Begin();
        return _elementsComposer;
    }

    /// <summary>结束元素层 Compose，并执行 Measure / Place。</summary>
    public void EndElements(ScreenOverlay o)
    {
        _elementsComposer.End();
        Layout(_elementsRoot, o);
    }

    /// <summary>绘制元素层。</summary>
    public void PaintElements(PaintScope s) => _elementsRoot.PaintNode(s);

    // ---------- 生命周期 ----------

    /// <summary>创建本帧的绘制上下文。</summary>
    public PaintScope CreatePaintScope(ID2D1DCRenderTarget rt, double now, double freq)
        => new(rt, _measureScope.DwFactory, _measureScope.D2DFactory, _brushes, now, freq);

    /// <summary>
    /// 覆盖层重建 / 渲染目标失效时重置：清空节点树与槽位（释放 DWrite 资源）并丢弃画刷缓存。
    /// 下一帧 Compose 会按当前数据重建整棵树（懒重建）。
    /// </summary>
    public void Reset()
    {
        _topCardsRoot.Dispose();
        _elementsRoot.Dispose();
        _brushes.Clear();
    }

    public void Dispose()
    {
        _topCardsRoot.Dispose();
        _elementsRoot.Dispose();
        _brushes.Dispose();
    }

    /// <summary>
    /// 每帧 Compose 后执行 Measure / Place（根尺寸即屏宽高）。
    /// 文本布局与文本格式在节点槽位中缓存，重复测量的成本仅为尺寸算术；
    /// 首次或屏幕尺寸变化时才需要真正重建 DWrite 资源。
    /// </summary>
    private void Layout(UiRootNode root, ScreenOverlay o)
    {
        var constraints = new Constraints(o.Width, o.Height);
        var bounds = new Rect(0f, 0f, o.Width, o.Height);
        root.MeasureNode(_measureScope, constraints);
        root.PlaceNode(bounds);
    }
}
