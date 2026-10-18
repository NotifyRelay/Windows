namespace NotifyRelay.Services.Overlay.UI;

/// <summary>
/// 保留式树游标（Compose 风格的 diff 引擎）。
/// 每帧按「声明顺序」重新描述整棵树：同层节点以 (节点类型, Key) 匹配，
/// 命中则复用旧节点实例（连同槽位中的 DWrite 布局与画刷），未命中则构造新节点，
/// 本轮未被复用的旧节点在渲染线程统一释放。
/// 
/// 匹配规则：
///  - 有 Key：按 (类型, Key) 在旧同层节点中查找（允许乱序，位置无关）。
///  - 无 Key：按 (类型, 出现顺序) 与旧同层中同样无 Key 的节点依次配对。
/// 
/// 用法：<c>c.Node&lt;Column&gt;(null, n =&gt; n.Spacing = 4, () =&gt; { ...子节点... });</c>
/// </summary>
internal sealed class OverlayComposer
{
    /// <summary>某一层的 diff 状态。</summary>
    private sealed class Level
    {
        public readonly List<OverlayNode> New = new(4);
        public OverlayNode? Parent;
        public List<OverlayNode>? Old;
        public Dictionary<string, List<OverlayNode>>? KeyedOld;
        public List<OverlayNode>? UnkeyedOld;
        public int UnkeyedCursor;
        public readonly HashSet<OverlayNode> Consumed = new();
    }

    private readonly OverlayNode _root;
    private readonly Stack<Level> _levels = new(8);
    private readonly List<OverlayNode> _stale = new(8);
    private readonly Stack<OverlayNode> _nodeStack = new(8);

    /// <summary>本次 pass 是否发生了结构性变化（新建 / 移除 / 类型或 Key 变化）。</summary>
    public bool StructureChanged { get; private set; }

    /// <summary>本次 pass 是否执行过任意 update 回调（即可能产生新一帧的 UI 描述）。</summary>
    public bool Recomposed { get; private set; }

    public OverlayComposer(OverlayNode root) => _root = root;

    /// <summary>开始一轮 Compose：在根层建立一个 Level。</summary>
    public void Begin()
    {
        StructureChanged = false;
        Recomposed = false;
        _stale.Clear();
        _levels.Clear();
        _nodeStack.Clear();

        // 整棵树开启槽位扫描：子树同样需要标记本轮访问，否则其槽位无法回收
        _root.BeginSlots();
        // 根层的旧子节点同样参与 diff；Old 引用 live 列表，PopLevel 先遍历登记再清空，顺序安全
        _levels.Push(CreateLevel(_root, _root.Children));
    }

    /// <summary>结束一轮 Compose：提交根层、释放未复用节点与槽位。</summary>
    public void End()
    {
        while (_levels.Count > 1) PopLevel();   // 防御：children 回调未正常闭合
        PopLevel();
        // 槽位回收：本轮未被 Remember 访问的槽位在此释放（须在未复用节点释放之前，
        // 因为被移除节点的槽位随节点 Dispose 一并释放）
        _root.CommitSlots();

        for (int i = 0; i < _stale.Count; i++)
        {
            try { _stale[i].Dispose(); } catch { /* 释放异常不影响本帧 */ }
        }
        _stale.Clear();
    }

    /// <summary>
    /// 声明一个节点：命中则复用并执行 <paramref name="update"/>，未命中则用 <paramref name="factory"/> 构造。
    /// <paramref name="children"/> 内继续声明其子节点。
    /// </summary>
    public T Node<T>(string? key, Func<T> factory, Action<T> update, Action children)
        where T : OverlayNode
    {
        var level = _levels.Peek();
        var node = MatchExisting<T>(level, key);

        if (node == null)
        {
            node = factory();
            node.Key = key;
            StructureChanged = true;
        }

        level.New.Add(node);
        _nodeStack.Push(node);

        update(node);
        Recomposed = true;

        // 进入子层
        _levels.Push(CreateLevel(node, node.Children));
        children();
        PopLevel();

        _nodeStack.Pop();
        return node;
    }

    /// <summary>声明一个节点（节点类型需有无参构造函数）。</summary>
    public T Node<T>(string? key, Action<T> update, Action children) where T : OverlayNode, new()
        => Node(key, static () => new T(), update, children);

    /// <summary>声明一个叶子节点（无子节点）。</summary>
    public T Leaf<T>(string? key, Func<T> factory, Action<T> update) where T : OverlayNode
        => Node(key, factory, update, static () => { });

    /// <summary>声明一个叶子节点（节点类型需有无参构造函数）。</summary>
    public T Leaf<T>(string? key, Action<T> update) where T : OverlayNode, new()
        => Node(key, static () => new T(), update, static () => { });

    /// <summary>在当前节点槽位中取用（或创建）跨帧复用的对象。</summary>
    public T Remember<T>(string key, Func<T> factory) where T : class
    {
        var owner = _nodeStack.Count > 0 ? _nodeStack.Peek() : _root;
        return owner.Slots.Remember(key, factory);
    }

    // ---------- 内部 ----------

    private static Level CreateLevel(OverlayNode parent, List<OverlayNode>? old)
    {
        var level = new Level { Parent = parent, Old = old };
        if (old is { Count: > 0 })
        {
            level.KeyedOld = new Dictionary<string, List<OverlayNode>>(old.Count);
            level.UnkeyedOld = new List<OverlayNode>(old.Count);
            for (int i = 0; i < old.Count; i++)
            {
                var child = old[i];
                if (child.Key == null)
                {
                    level.UnkeyedOld.Add(child);
                    continue;
                }
                string mapKey = KeyOf(child);
                if (!level.KeyedOld.TryGetValue(mapKey, out var bucket))
                {
                    bucket = new List<OverlayNode>(1);
                    level.KeyedOld[mapKey] = bucket;
                }
                bucket.Add(child);
            }
        }
        return level;
    }

    /// <summary>类型 + Key 构成的匹配键。</summary>
    private static string KeyOf(OverlayNode node) => node.GetType().FullName + "\u0001" + node.Key;

    /// <summary>尝试在旧同层节点中找到可复用的同类型节点。</summary>
    private static T? MatchExisting<T>(Level level, string? key) where T : OverlayNode
    {
        if (key != null)
        {
            if (level.KeyedOld == null) return null;
            string mapKey = typeof(T).FullName + "\u0001" + key;
            if (!level.KeyedOld.TryGetValue(mapKey, out var bucket)) return null;
            for (int i = 0; i < bucket.Count; i++)
            {
                var candidate = bucket[i];
                if (level.Consumed.Contains(candidate)) continue;
                level.Consumed.Add(candidate);
                return (T)candidate;
            }
            return null;
        }

        // 无 Key：按出现顺序与旧同层中同样无 Key 的节点配对（类型须一致）
        if (level.UnkeyedOld == null) return null;
        var list = level.UnkeyedOld;
        for (int i = level.UnkeyedCursor; i < list.Count; i++)
        {
            var candidate = list[i];
            if (level.Consumed.Contains(candidate)) continue;
            if (candidate is not T typed) continue;
            level.UnkeyedCursor = i + 1;
            level.Consumed.Add(candidate);
            return typed;
        }
        return null;
    }

    /// <summary>结束当前层：提交子列表并把未复用的旧节点登记为待释放（延迟到 End 统一释放）。</summary>
    private void PopLevel()
    {
        var level = _levels.Pop();
        var parent = level.Parent!;

        if (level.Old != null)
        {
            for (int i = 0; i < level.Old.Count; i++)
            {
                var old = level.Old[i];
                if (level.Consumed.Contains(old)) continue;
                _stale.Add(old);
            }
        }

        parent.Children.Clear();
        parent.Children.AddRange(level.New);
        level.New.Clear();
    }
}
