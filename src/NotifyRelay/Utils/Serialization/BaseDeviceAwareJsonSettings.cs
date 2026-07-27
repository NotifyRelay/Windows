using System.Runtime.CompilerServices;
using NotifyRelay.Data.AppDatabase;
using NotifyRelay.Data.EventArguments;
using NotifyRelay.Utils.Serialization.Implementation;

namespace NotifyRelay.Utils.Serialization;

/// <summary>
/// A base class for device-specific settings that stores settings in database per device.
/// </summary>
internal abstract class BaseDeviceAwareJsonSettings : BaseObservableJsonSettings
{
    private readonly string _deviceId;

    protected BaseDeviceAwareJsonSettings(string deviceId, ISettingsSharingContext parentContext, DatabaseContext dbContext)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("Device ID cannot be null or whitespace", nameof(deviceId));

        _deviceId = deviceId;

        JsonSettingsSerializer = new JsonSettingsSerializer();
        JsonSettingsDatabase = new SqliteSettingsDatabase(dbContext, deviceId);
        IsAvailable = true;
    }

    public string DeviceId => _deviceId;

    /// <summary>
    /// Sets a setting value (no prefix needed since we have our own file)
    /// </summary>
    protected override bool Set<TValue>(TValue? value, [CallerMemberName] string propertyName = "") where TValue : default
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return false;
        }

        if (JsonSettingsDatabase is not null &&
            (!JsonSettingsDatabase.GetValue<TValue>(propertyName)?.Equals(value) ?? true) &&
            JsonSettingsDatabase.SetValue(propertyName, value))
        {
            RaiseOnSettingChangedEvent(this, new SettingChangedEventArgs(propertyName, value));
            OnPropertyChanged(propertyName);
            return true;
        }

        return false;
    }
}