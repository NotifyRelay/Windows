using NotifyRelay.Data.Models;
using NotifyRelay.Helpers;
using SQLite;

namespace NotifyRelay.Data.AppDatabase.Models;

public partial class RemoteDeviceEntity
{
    [PrimaryKey]
    public string DeviceId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    // Notify 协议远端公钥（用于 HKDF 派生对称密钥）
    public string? PublicKey { get; set; }

    // 共享密钥（仅历史迁移读取源；迁移后清空。密钥真正的持有方为 Rust 私有库）
    public byte[]? SharedSecret { get; set; }

    public byte[]? WallpaperBytes { get; set; }

    public DateTime? LastConnected { get; set; }

    public bool HasSentftpRequest { get; set; } = false;

    [Column("IpAddresses")]
    public string? IpAddressesJson { get; set; }

    [Ignore]
    public List<string> IpAddresses
    {
        get => string.IsNullOrEmpty(IpAddressesJson) ? [] : JsonSerializer.Deserialize<List<string>>(IpAddressesJson) ?? [];
        set => IpAddressesJson = value is null ? null : JsonSerializer.Serialize(value);
    }

    #region Helpers
    internal async Task<PairedDevice> ToPairedDevice()
    {
        return new PairedDevice(DeviceId)
        {
            Name = Name,
            Model = Model,
            IpAddresses = IpAddresses,
            Wallpaper = await ImageHelper.ToBitmapAsync(WallpaperBytes),
            // SharedSecret 由 Rust 私有库持有，此处不外泄
            RemotePublicKey = PublicKey,
            HasSentftpRequest = HasSentftpRequest,
        };
    }
    #endregion
}
