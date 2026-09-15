using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace NotifyRelay.Services.Overlay.UI;

/// <summary>
/// 测量上下文：一次 Measure 遍历内所有节点共享的测量环境。
/// 只暴露与渲染目标无关的工厂（DWrite / D2D），不提供画刷 —— 测量阶段不应触碰 rt 绑定资源。
/// </summary>
internal sealed class MeasureScope
{
    public MeasureScope(IDWriteFactory dwFactory, ID2D1Factory d2dFactory)
    {
        DwFactory = dwFactory;
        D2DFactory = d2dFactory;
    }

    public IDWriteFactory DwFactory { get; }

    public ID2D1Factory D2DFactory { get; }

    /// <summary>创建文本格式（统一 Normal 字型/拉伸）。</summary>
    public IDWriteTextFormat CreateTextFormat(string fontFamily, FontWeight weight, float size)
        => DwFactory.CreateTextFormat(fontFamily, null!, weight,
            FontStyle.Normal, FontStretch.Normal, size);

    /// <summary>创建单行、不换行、宽度不限的文本布局（用于精确量测）。</summary>
    public IDWriteTextLayout CreateMeasureLayout(string text, IDWriteTextFormat format, float measureWidth = 10000f)
    {
        var layout = DwFactory.CreateTextLayout(text, format, measureWidth, float.PositiveInfinity);
        layout.WordWrapping = WordWrapping.NoWrap;
        return layout;
    }

    /// <summary>量测单行文本宽度（临时资源即测即放）。</summary>
    public float MeasureTextWidth(string text, string fontFamily, FontWeight weight, float size)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        using var format = CreateTextFormat(fontFamily, weight, size);
        using var layout = CreateMeasureLayout(text, format);
        return layout.Metrics.WidthIncludingTrailingWhitespace;
    }
}

/// <summary>
/// 声明式 UI 节点基类。
/// 节点树跨帧保留：由 <see cref="OverlayComposer"/> 按 (类型, Key) diff 复用，
/// 被移除的节点在渲染线程统一释放（<see cref="OnDispose"/> + 槽位）。
/// </summary>
internal abstract class OverlayNode : IDisposable
{
    /// <summary>同层 diff 用的稳定标识（与节点类型共同构成匹配键；null 表示仅按类型 + 位置匹配）。</summary>
    public string? Key { get; set; }

    /// <summary>子节点列表，顺序即绘制顺序（后绘制者在上）。</summary>
    internal readonly List<OverlayNode> Children = new(4);

    /// <summary>Remember 槽位表：存放跨帧复用的 DWrite 布局 / 格式 等非 rt 资源。</summary>
    internal readonly SlotTable Slots = new();

    /// <summary>测量阶段：在给定约束下上报自身尺寸（需递归测量子节点）。</summary>
    protected abstract Size Measure(MeasureScope s, Constraints c);

    /// <summary>定位阶段：父节点已确定本节点的最终矩形。</summary>
    protected abstract void Place(Rect rect);

    /// <summary>绘制阶段：每帧调用，使用缓存的布局/几何绘制。</summary>
    protected abstract void Paint(PaintScope s);

    /// <summary>释放本节点持有的非托管资源（渲染线程调用）。子节点由基类递归释放。</summary>
    protected virtual void OnDispose() { }

    /// <summary>本节点最后一次 Measure 上报的尺寸（Place 之前由父节点读取）。</summary>
    internal Size MeasuredSize { get; private set; }

    /// <summary>本节点最后一次 Place 得到的矩形。</summary>
    internal Rect Bounds { get; private set; }

    // ---------- 运行时入口（仅供 OverlayUiRoot / 容器节点调用） ----------

    internal Size MeasureNode(MeasureScope s, Constraints c)
    {
        MeasuredSize = Measure(s, c);
        return MeasuredSize;
    }

    internal void PlaceNode(Rect rect)
    {
        Bounds = rect;
        Place(rect);
    }

    internal void PaintNode(PaintScope s) => Paint(s);

    /// <summary>本层（含自身与全部后代）开启一轮槽位扫描。</summary>
    internal void BeginSlots()
    {
        Slots.BeginPass();
        for (int i = 0; i < Children.Count; i++) Children[i].BeginSlots();
    }

    /// <summary>本层（含自身与全部后代）的槽位提交：先子树后自身，未复用的槽位在此释放。</summary>
    internal void CommitSlots()
    {
        for (int i = 0; i < Children.Count; i++) Children[i].CommitSlots();
        Slots.EndPass();
    }

    /// <summary>递归释放本节点与全部后代。</summary>
    public void Dispose()
    {
        for (int i = 0; i < Children.Count; i++) Children[i].Dispose();
        Children.Clear();
        try { OnDispose(); } catch { /* 释放异常不得影响整棵树的清理 */ }
        Slots.Dispose();
    }
}

/// <summary>
/// Remember 槽位表：按 key 存放跨帧复用的对象。
/// 每次重组 pass 开始时标记扫描，pass 结束时释放本轮未被访问的槽位 —— 
/// 对应 plan §3.2「key 组合变化时整表清空，IDisposable 在渲染线程释放」。
/// </summary>
internal sealed class SlotTable : IDisposable
{
    private readonly Dictionary<string, object> _slots = new(4);
    private readonly List<string> _touched = new(4);
    private bool _passActive;

    /// <summary>取槽位；不存在时用工厂创建。</summary>
    public T Remember<T>(string key, Func<T> factory) where T : class
    {
        if (_slots.TryGetValue(key, out var existing) && existing is T typed)
        {
            MarkTouched(key);
            return typed;
        }

        // 类型不匹配（同 key 换了类型）：释放旧值后重建
        if (existing != null)
        {
            DisposeValue(existing);
            _slots.Remove(key);
        }

        var created = factory();
        _slots[key] = created;
        MarkTouched(key);
        return created;
    }

    /// <summary>取已存在的槽位，不存在返回 null（不创建，不标记访问）。</summary>
    public T? Get<T>(string key) where T : class
        => _slots.TryGetValue(key, out var v) && v is T typed ? typed : null;

    /// <summary>显式替换槽位值（旧值释放）。</summary>
    public void Set(string key, object? value)
    {
        if (_slots.TryGetValue(key, out var existing))
        {
            DisposeValue(existing);
            _slots.Remove(key);
        }
        if (value != null)
        {
            _slots[key] = value;
            MarkTouched(key);
        }
    }

    /// <summary>开始一轮重组：清空本轮访问标记。</summary>
    public void BeginPass()
    {
        _passActive = true;
        _touched.Clear();
    }

    /// <summary>结束一轮重组：释放本轮未被访问的槽位。</summary>
    public void EndPass()
    {
        if (!_passActive) return;
        _passActive = false;
        if (_touched.Count == _slots.Count) return;

        var stale = new List<string>();
        foreach (var key in _slots.Keys)
        {
            bool touched = false;
            for (int i = 0; i < _touched.Count; i++)
            {
                if (string.Equals(_touched[i], key, StringComparison.Ordinal)) { touched = true; break; }
            }
            if (!touched) stale.Add(key);
        }
        foreach (var key in stale)
        {
            DisposeValue(_slots[key]);
            _slots.Remove(key);
        }
    }

    private void MarkTouched(string key) => _touched.Add(key);

    private static void DisposeValue(object value)
    {
        try
        {
            if (value is IDisposable d) d.Dispose();
        }
        catch { /* 释放异常不得中断槽位清理 */ }
    }

    public void Dispose()
    {
        foreach (var value in _slots.Values) DisposeValue(value);
        _slots.Clear();
        _touched.Clear();
    }
}
