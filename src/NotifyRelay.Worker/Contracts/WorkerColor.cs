namespace NotifyRelay.Worker.Contracts;

/// <summary>与平台无关的 ARGB 颜色值。</summary>
public readonly record struct WorkerColor(byte A, byte R, byte G, byte B)
{
    /// <summary>不透明黑色。</summary>
    public static WorkerColor Black { get; } = new(255, 0, 0, 0);
}
