using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.Devices.Lights;

namespace NotifyRelay.DeviceCtrl.DynamicLighting;

public class LampArrayDeviceInfo : INotifyPropertyChanged
{
    private string _id = string.Empty;
    private string _name = string.Empty;
    private LampArray? _lampArray;
    private bool _isAvailable;
    private int _lampCount;
    private LampArrayKind _kind;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public LampArray? LampArray
    {
        get => _lampArray;
        set => SetField(ref _lampArray, value);
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        set => SetField(ref _isAvailable, value);
    }

    public int LampCount
    {
        get => _lampCount;
        set => SetField(ref _lampCount, value);
    }

    public LampArrayKind Kind
    {
        get => _kind;
        set => SetField(ref _kind, value);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}