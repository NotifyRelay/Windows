using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Models.Render;
using NotifyRelay.Services;
using NotifyRelay.Services.Overlay;
using Vortice.DirectWrite;

namespace NotifyRelay.ViewModels.Settings;

public class DanmakuViewModel : INotifyPropertyChanged
{
    private readonly IGeneralSettingsService _settings;
    private readonly OverlayRenderService? _renderService;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool DanmakuNotificationEnabled
    {
        get => _settings.DanmakuNotificationEnabled;
        set { _settings.DanmakuNotificationEnabled = value; OnPropertyChanged(); }
    }

    public bool DanmakuMediaCardEnabled
    {
        get => _settings.DanmakuMediaCardEnabled;
        set { _settings.DanmakuMediaCardEnabled = value; OnPropertyChanged(); }
    }

    public bool DanmakuSuperIslandEnabled
    {
        get => _settings.DanmakuSuperIslandEnabled;
        set { _settings.DanmakuSuperIslandEnabled = value; OnPropertyChanged(); }
    }

    public bool GamebarRelayEnabled
    {
        get => _settings.GamebarRelayEnabled;
        set { _settings.GamebarRelayEnabled = value; OnPropertyChanged(); }
    }

    public int DanmakuFontSizePercent
    {
        get => _settings.DanmakuFontSizePercent;
        set { _settings.DanmakuFontSizePercent = value; OnPropertyChanged(); OnStyleChanged(); }
    }

    public int DanmakuSpeed
    {
        get => _settings.DanmakuSpeed;
        set { _settings.DanmakuSpeed = value; OnPropertyChanged(); OnStyleChanged(); }
    }

    public int DanmakuOpacityPercent
    {
        get => _settings.DanmakuOpacityPercent;
        set { _settings.DanmakuOpacityPercent = value; OnPropertyChanged(); OnStyleChanged(); }
    }

    public int DanmakuDisplayAreaPercent
    {
        get => _settings.DanmakuDisplayAreaPercent;
        set { _settings.DanmakuDisplayAreaPercent = value; OnPropertyChanged(); OnStyleChanged(); }
    }

    public int DanmakuDensity
    {
        get => _settings.DanmakuDensity;
        set { _settings.DanmakuDensity = value; OnPropertyChanged(); OnStyleChanged(); }
    }

    public string DanmakuFontFamily
    {
        get => _settings.DanmakuFontFamily;
        set { _settings.DanmakuFontFamily = value; OnPropertyChanged(); OnStyleChanged(); }
    }

    public bool DanmakuBold
    {
        get => _settings.DanmakuBold;
        set { _settings.DanmakuBold = value; OnPropertyChanged(); OnStyleChanged(); }
    }

    public string DanmakuColor
    {
        get => _settings.DanmakuColor;
        set { _settings.DanmakuColor = value; OnPropertyChanged(); OnStyleChanged(); }
    }

    public bool DanmakuBorderEnabled
    {
        get => _settings.DanmakuBorderEnabled;
        set { _settings.DanmakuBorderEnabled = value; OnPropertyChanged(); OnStyleChanged(); }
    }

    public int DanmakuBorderThickness
    {
        get => _settings.DanmakuBorderThickness;
        set { _settings.DanmakuBorderThickness = value; OnPropertyChanged(); OnStyleChanged(); }
    }

    public string DanmakuBorderColor
    {
        get => _settings.DanmakuBorderColor;
        set { _settings.DanmakuBorderColor = value; OnPropertyChanged(); OnStyleChanged(); }
    }

    public bool DanmakuShadowEnabled
    {
        get => _settings.DanmakuShadowEnabled;
        set { _settings.DanmakuShadowEnabled = value; OnPropertyChanged(); OnStyleChanged(); }
    }

    public int DanmakuShadowDepth
    {
        get => _settings.DanmakuShadowDepth;
        set { _settings.DanmakuShadowDepth = value; OnPropertyChanged(); OnStyleChanged(); }
    }

    public int DanmakuShadowOpacity
    {
        get => _settings.DanmakuShadowOpacity;
        set { _settings.DanmakuShadowOpacity = value; OnPropertyChanged(); OnStyleChanged(); }
    }

    public string DanmakuShadowColor
    {
        get => _settings.DanmakuShadowColor;
        set { _settings.DanmakuShadowColor = value; OnPropertyChanged(); OnStyleChanged(); }
    }

    public int DanmakuDisplayScreenMode
    {
        get => _settings.DanmakuDisplayScreenMode;
        set { _settings.DanmakuDisplayScreenMode = value; OnPropertyChanged(); OnStyleChanged(); }
    }

    public int DanmakuPerformanceMode
    {
        get => _settings.DanmakuPerformanceMode;
        set { _settings.DanmakuPerformanceMode = value; OnPropertyChanged(); OnStyleChanged(); }
    }

    /// <summary>系统可用字体族名称列表，用于设置页字体选择（通过 DirectWrite 枚举）。</summary>
    public List<string> FontFamilies { get; } = LoadSystemFontFamilies();

    public DanmakuViewModel()
    {
        _settings = Ioc.Default.GetRequiredService<IGeneralSettingsService>();
        _renderService = Ioc.Default.GetService<OverlayRenderService>();

        // 预加载测试图标（异步，启动后很快就绪）
        _ = EnsureTestIconLoadedAsync();
    }

    private static List<string> LoadSystemFontFamilies()
    {
        var names = new List<string>();
        try
        {
            using var factory = DWrite.DWriteCreateFactory<IDWriteFactory>();
            using var collection = factory.GetSystemFontCollection(false);
            uint count = collection.FontFamilyCount;
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (uint i = 0; i < count; i++)
            {
                using var family = collection.GetFontFamily(i);
                using var localized = family.FamilyNames;
                uint index;
                string? name = null;
                // 优先英文规范名（与默认值 "Microsoft YaHei" 及 CreateTextFormat 匹配最稳），其次中性/中文
                if (localized.FindLocaleName("en-US", out index) ||
                    localized.FindLocaleName(string.Empty, out index) ||
                    localized.FindLocaleName("zh-CN", out index))
                {
                    name = localized.GetString(index);
                }
                else if (localized.Count > 0)
                {
                    name = localized.GetString(0);
                }

                if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                    names.Add(name);
            }
            names.Sort(System.StringComparer.CurrentCultureIgnoreCase);
        }
        catch
        {
            // 枚举失败时不阻断设置页
        }

        if (names.Count == 0)
            names.Add("Microsoft YaHei");
        return names;
    }

    public void SendTestDanmaku()
    {
        // 优先使用已预加载的图标字节，未就绪时同步回退到嵌入资源
        var icon = _testIconBytes ?? LoadTestIconFromEmbeddedResource();
        _renderService?.ShowDanmaku("NotifyRelay", "测试", "这是一条测试弹幕通知", icon, "本机");
    }

    private static byte[]? _testIconBytes;
    private static Task? _testIconLoadTask;

    /// <summary>异步加载内置测试图标（作为字节流）。优先用 ms-appx 包资源 URI（打包/非打包均可用，且无需文件系统路径），
    /// 失败回退到嵌入资源。与媒体块专辑图一致，最终都以 byte[] 形式传给渲染层。</summary>
    private static Task EnsureTestIconLoadedAsync()
    {
        if (_testIconLoadTask is not null)
            return _testIconLoadTask;

        _testIconLoadTask = Task.Run(async () =>
        {
            try
            {
                var file = await Windows.Storage.StorageFile.GetFileFromApplicationUriAsync(
                    new Uri("ms-appx:///Assets/NotifyRelayAppIcon.png"));
                using var stream = await file.OpenReadAsync();
                using var reader = new Windows.Storage.Streams.DataReader(stream);
                await reader.LoadAsync((uint)stream.Size);
                var bytes = new byte[(int)stream.Size];
                reader.ReadBytes(bytes);
                _testIconBytes = bytes;
            }
            catch
            {
                _testIconBytes ??= LoadTestIconFromEmbeddedResource();
            }
        });

        return _testIconLoadTask;
    }

    /// <summary>从嵌入资源读取图标字节流（作为兜底方案）。</summary>
    private static byte[]? LoadTestIconFromEmbeddedResource()
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("Assets.NotifyRelayAppIcon.png", StringComparison.OrdinalIgnoreCase));
            if (resourceName != null)
            {
                using var stream = asm.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }
        catch
        {
            // 读取失败则不附带图标
        }

        return null;
    }

    private void OnStyleChanged()
    {
        var style = new DanmakuStyleSettings
        {
            FontSizePercent = DanmakuFontSizePercent,
            Speed = DanmakuSpeed,
            OpacityPercent = DanmakuOpacityPercent,
            DisplayAreaPercent = DanmakuDisplayAreaPercent,
            Density = DanmakuDensity,
            FontFamilyName = DanmakuFontFamily,
            Bold = DanmakuBold,
            ColorR = ParseColorR(DanmakuColor, 255),
            ColorG = ParseColorG(DanmakuColor, 255),
            ColorB = ParseColorB(DanmakuColor, 255),
            BorderEnabled = DanmakuBorderEnabled,
            BorderThickness = DanmakuBorderThickness,
            BorderColorR = ParseColorR(DanmakuBorderColor, 0),
            BorderColorG = ParseColorG(DanmakuBorderColor, 0),
            BorderColorB = ParseColorB(DanmakuBorderColor, 0),
            ShadowEnabled = DanmakuShadowEnabled,
            ShadowDepth = DanmakuShadowDepth,
            ShadowOpacity = DanmakuShadowOpacity,
            ShadowColorR = ParseColorR(DanmakuShadowColor, 0),
            ShadowColorG = ParseColorG(DanmakuShadowColor, 0),
            ShadowColorB = ParseColorB(DanmakuShadowColor, 0),
            DisplayScreenMode = DanmakuDisplayScreenMode,
            PerformanceMode = DanmakuPerformanceMode
        };
        _renderService?.UpdateStyle(style);
    }

    private static byte ParseColorR(string? hex, byte fallback)
    {
        if (string.IsNullOrEmpty(hex) || !hex.StartsWith("#")) return fallback;
        hex = hex.TrimStart('#');
        if (hex.Length != 6) return fallback;
        try { return byte.Parse(hex[..2], System.Globalization.NumberStyles.HexNumber); }
        catch { return fallback; }
    }

    private static byte ParseColorG(string? hex, byte fallback)
    {
        if (string.IsNullOrEmpty(hex) || !hex.StartsWith("#")) return fallback;
        hex = hex.TrimStart('#');
        if (hex.Length != 6) return fallback;
        try { return byte.Parse(hex[2..4], System.Globalization.NumberStyles.HexNumber); }
        catch { return fallback; }
    }

    private static byte ParseColorB(string? hex, byte fallback)
    {
        if (string.IsNullOrEmpty(hex) || !hex.StartsWith("#")) return fallback;
        hex = hex.TrimStart('#');
        if (hex.Length != 6) return fallback;
        try { return byte.Parse(hex[4..6], System.Globalization.NumberStyles.HexNumber); }
        catch { return fallback; }
    }

    protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
