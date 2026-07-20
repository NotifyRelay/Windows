namespace NotifyRelay.DeviceCtrl.DynamicLighting.Interfaces;

public interface ILightingInputProvider
{
    string Name { get; }
    event EventHandler<NumericValueChangedEventArgs> ValueChanged;
    double CurrentValue { get; }
    double MinValue { get; }
    double MaxValue { get; }
    void Start();
    void Stop();
}

public class NumericValueChangedEventArgs : EventArgs
{
    public double Value { get; }
    public DateTime Timestamp { get; }

    public NumericValueChangedEventArgs(double value)
    {
        Value = value;
        Timestamp = DateTime.Now;
    }
}