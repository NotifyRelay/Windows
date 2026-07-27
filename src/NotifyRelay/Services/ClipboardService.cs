using CommunityToolkit.WinUI;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.System;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace NotifyRelay.Services;

public class ClipboardService : IClipboardService
{
    private readonly ILogger<ClipboardService> logger;
    private readonly ISessionManager sessionManager;
    private readonly IPlatformNotificationHandler platformNotificationHandler;
    private readonly IDeviceManager deviceManager;
    private readonly DispatcherQueue dispatcher;
    private readonly IFileTransferService fileTransferService;

    private const int DirectTransferThreshold = 2 * 1024 * 1024; // 2MB threshold

    private static readonly Dictionary<string, string> SupportedImageFileTypes = new()
    {
        ["jpeg"] = "image/jpeg",
        ["jpg"] = "image/jpeg",
        ["png"] = "image/png",
        ["gif"] = "image/gif",
        ["bmp"] = "image/bmp",
        ["webp"] = "image/webp",
        ["tiff"] = "image/tiff",
        ["tif"] = "image/tiff",
        ["heic"] = "image/heic",
        ["heif"] = "image/heic",
        [".apng"] = "image/apng"
    };

    private bool isInternalUpdate; // To track if the clipboard change came from the remote device

    public ClipboardService(
        ILogger<ClipboardService> logger,
        ISessionManager sessionManager,
        IPlatformNotificationHandler platformNotificationHandler,
        IDeviceManager deviceManager,
        IFileTransferService fileTransferService)
    {
        this.logger = logger;
        this.sessionManager = sessionManager;
        this.platformNotificationHandler = platformNotificationHandler;
        this.deviceManager = deviceManager;
        this.fileTransferService = fileTransferService;
        dispatcher = App.MainWindow.DispatcherQueue;

        dispatcher.EnqueueAsync(() =>
        {
            try
            {
                Clipboard.ContentChanged += OnClipboardContentChanged;
                logger.LogInformation("剪贴板监视已启动");
            }
            catch (Exception ex)
            {
                logger.LogError("启动剪贴板监控失败：{ex}", ex);
            }
        });

        fileTransferService.FileReceived += async (sender, args) =>
        {
            await SetContentAsync(args.data, args.device);
        };
    }

    private async void OnClipboardContentChanged(object? sender, object? e)
    {
        if (isInternalUpdate)
        {
            logger.LogDebug("内部更新，跳过剪贴板发送");
            return;
        }

        await dispatcher.EnqueueAsync(async () =>
        {
            try
            {
                logger.LogDebug("剪贴板内容已更改，开始处理");

                // 记录所有配对设备的状态
                logger.LogDebug("配对设备总数：{count}", deviceManager.PairedDevices.Count);
                foreach (var device in deviceManager.PairedDevices)
                {
                    logger.LogDebug("设备 {name} (ID: {id}): ConnectionStatus={status}, ClipboardSyncEnabled={clipboardSync}",
                        device.Name, device.Id, device.ConnectionStatus, device.DeviceSettings?.ClipboardSyncEnabled);
                }

                // Check if any connected devices have clipboard sync enabled
                var devicesWithClipboardSync = deviceManager.PairedDevices
                    .Where(device => device.ConnectionStatus &&
                                    device.DeviceSettings?.ClipboardSyncEnabled == true)
                    .ToList();

                logger.LogDebug("符合条件的设备数量：{count}", devicesWithClipboardSync.Count);

                if (devicesWithClipboardSync.Count == 0)
                {
                    logger.LogDebug("没有符合条件的设备，跳过剪贴板发送");
                    return;
                }

                logger.LogDebug("准备发送剪贴板内容到 {count} 个设备", devicesWithClipboardSync.Count);

                var dataPackageView = Clipboard.GetContent();

                if (dataPackageView.Contains(StandardDataFormats.Text))
                {
                    await TryHandleTextContent(dataPackageView, devicesWithClipboardSync);
                    return;
                }

                // Check if any device has image clipboard enabled
                var devicesWithImageSync = devicesWithClipboardSync
                    .Where(d => d.DeviceSettings?.ImageToClipboardEnabled == true)
                    .ToList();

                if (devicesWithImageSync.Count != 0)
                {
                    if (dataPackageView.Contains(StandardDataFormats.StorageItems))
                    {
                        var storageItems = await dataPackageView.GetStorageItemsAsync();
                        var file = storageItems.OfType<StorageFile>().FirstOrDefault();
                        if (file is IStorageFile)
                        {
                            var mimeType = file.ContentType;
                            var fileExtension = file.FileType[1..];

                            // Validate that this is a supported image type and get MIME type
                            if (!SupportedImageFileTypes.TryGetValue(fileExtension, out var detectedMimeType))
                                return;

                            // Content type from StorageFile can be unreliable
                            if (string.IsNullOrEmpty(mimeType))
                            {
                                mimeType = detectedMimeType;
                            }

                            logger.LogInformation("文件名：{fileName}，扩展名：{fileExtension}，MIME 类型：{mimeType}", file.Name, fileExtension, mimeType);

                            if ((long)(await file.GetBasicPropertiesAsync()).Size > DirectTransferThreshold)
                                await HandleLargeImageTransfer(file, fileExtension, mimeType, devicesWithImageSync);
                            else
                                await HandleSmallImageTransfer(await file.OpenStreamForReadAsync(), mimeType, devicesWithImageSync);
                        }
                        return;
                    }

                    if (dataPackageView.Contains(StandardDataFormats.Bitmap))
                    {
                        var bitmapRef = await dataPackageView.GetBitmapAsync();
                        var bitmap = await bitmapRef.OpenReadAsync();
                        var stream = new MemoryStream();
                        var decoder = await BitmapDecoder.CreateAsync(bitmap);
                        var softwareBitmap = await decoder.GetSoftwareBitmapAsync();
                        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream.AsRandomAccessStream());
                        encoder.SetSoftwareBitmap(softwareBitmap);
                        await encoder.FlushAsync();
                        stream.Position = 0;
                        await HandleSmallImageTransfer(stream, "image/png", devicesWithImageSync);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError("处理剪贴板内容时出错：{ex}", ex);
            }
        });
    }


    private async Task TryHandleTextContent(DataPackageView dataPackageView, List<PairedDevice> devices)
    {
        if (!dataPackageView.Contains(StandardDataFormats.Text)) return;

        string? text = await dataPackageView.GetTextAsync();
        if (string.IsNullOrEmpty(text)) return;

        // Convert Windows CRLF to Unix LF 
        text = text.Replace("\r\n", "\n");

        var rawJson = JsonSerializer.Serialize(new
        {
            type = "DATA_CLIPBOARD",
            clipboardType = "text",
            content = text
        });
        var serializedMessage = rawJson;
        if (serializedMessage == null) return;
        foreach (var device in devices)
        {
            if (device.ConnectionStatus)
            {
                sessionManager.SendMessage(device.Id, serializedMessage);
            }
        }
        return;
    }

    private async Task HandleLargeImageTransfer(StorageFile file, string fileType, string mimeType, List<PairedDevice> devices)
    {
        var metadata = new FileMetadata
        {
            FileName = $"sefirah_clipboard_image.{fileType}",
            MimeType = mimeType,
            FileSize = (long)(await file.GetBasicPropertiesAsync()).Size
        };

        await Task.Run(async () =>
        {
            foreach (var device in devices)
            {
                await fileTransferService.SendFile(file, metadata, device, FileTransferType.Clipboard);
            }
        });
    }

    private async Task HandleSmallImageTransfer(Stream stream, string mimeType, List<PairedDevice> devices)
    {
        stream.Position = 0;
        byte[] buffer = new byte[stream.Length];
        await stream.ReadExactlyAsync(buffer);

        var rawJson = JsonSerializer.Serialize(new
        {
            type = "DATA_CLIPBOARD",
            clipboardType = mimeType,
            content = Convert.ToBase64String(buffer)
        });
        var serializedMessage = rawJson;
        if (serializedMessage == null) return;

        foreach (var device in devices)
        {
            if (device.ConnectionStatus)
            {
                sessionManager.SendMessage(device.Id, serializedMessage);
            }
        }
    }

    public async Task SetContentAsync(object content, PairedDevice sourceDevice)
    {
        if (!sourceDevice.DeviceSettings.ClipboardSyncEnabled) return;

        await dispatcher.EnqueueAsync(async () =>
        {
            try
            {
                isInternalUpdate = true;
                var dataPackage = new DataPackage();

                switch (content)
                {
                    case StorageFile file:
                        // Set package family name for proper file handling
                        dataPackage.Properties.PackageFamilyName =
                            Package.Current.Id.FamilyName;
                        // Pass false as second parameter to indicate the app isn't taking ownership of the files
                        dataPackage.SetStorageItems([file], false);
                        break;
                    case string textContent:
                        // 检查是否为图片类型的 Base64 编码
                        if (textContent.Length > 20 && textContent.IsBase64String())
                        {
                            // 尝试将 Base64 字符串转换为 Bitmap
                            var bitmap = await ConvertBase64ToBitmapAsync(textContent);
                            if (bitmap is not null)
                            {
                                // 设置图片到剪贴板
                                dataPackage.SetBitmap(bitmap);
                                logger.LogInformation("剪贴板内容已设置为图片");
                            }
                            else
                            {
                                // 如果转换失败，作为文本处理
                                dataPackage.SetText(textContent);
                                logger.LogWarning("无法将 Base64 字符串转换为图片，作为文本处理");
                            }
                        }
                        else
                        {
                            // 普通文本处理
                            dataPackage.SetText(textContent);
                            Uri.TryCreate(textContent, UriKind.Absolute, out Uri? uri);
                            bool isValidUri = IsValidWebUrl(uri);
                            if (sourceDevice.DeviceSettings.OpenLinksInBrowser && isValidUri)
                            {
                                await Launcher.LaunchUriAsync(uri);
                            }
                            else if (isValidUri && sourceDevice.DeviceSettings.ShowClipboardToast)
                            {
                                platformNotificationHandler.ShowClipboardNotificationWithActions(
                                    "Clipboard data received",
                                    "Click to open link in browser",
                                    "Open in browser",
                                    textContent);
                            }
                        }
                        break;
                    default:
                        throw new ArgumentException($"Unsupported content type: {content.GetType()}");
                }

                Clipboard.SetContent(dataPackage);
                await Task.Delay(50);
                logger.LogInformation("剪贴板内容已设置：{Content}", content);

                if (sourceDevice.DeviceSettings.ShowClipboardToast && content is not string)
                {
                    platformNotificationHandler.ShowClipboardNotification(
                        "Clipboard data received",
                        $"Content type: {content.GetType().Name}");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "设置剪贴板内容时出错");
                throw;
            }
            finally
            {
                isInternalUpdate = false;
            }
        });
    }

    /// <summary>
    /// 将 Base64 字符串转换为 Bitmap
    /// </summary>
    /// <param name="base64String">Base64 字符串</param>
    /// <returns>Bitmap 对象</returns>
    private async Task<RandomAccessStreamReference?> ConvertBase64ToBitmapAsync(string base64String)
    {
        try
        {
            // 解码 Base64 字符串
            byte[] imageBytes = Convert.FromBase64String(base64String);

            // 创建内存流
            using var memoryStream = new MemoryStream(imageBytes);

            // 创建随机访问流
            var randomAccessStream = new InMemoryRandomAccessStream();
            await memoryStream.CopyToAsync(randomAccessStream.AsStreamForWrite());
            randomAccessStream.Seek(0);

            // 验证是否为有效的图片
            var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
            if (decoder == null)
            {
                logger.LogWarning("无法创建 BitmapDecoder，Base64 字符串可能不是有效的图片");
                return null;
            }

            // 返回随机访问流引用
            return RandomAccessStreamReference.CreateFromStream(randomAccessStream);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "将 Base64 字符串转换为 Bitmap 时出错");
            return null;
        }
    }

    public static bool IsValidWebUrl(Uri? uri)
    {
        return uri != null &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
               !string.IsNullOrWhiteSpace(uri.Host) &&
               uri.Host.Contains('.');
    }

    public async Task ProcessClipboardMessageAsync(PairedDevice device, string payload)
    {
        logger.LogDebug("处理DATA_CLIPBOARD消息，内容: {payload}", payload);
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            var clipboardType = root.TryGetProperty("clipboardType", out var typeProp) ? typeProp.GetString() : "text";
            var content = root.TryGetProperty("content", out var contentProp) ? contentProp.GetString() : string.Empty;

            await SetContentAsync(content ?? string.Empty, device);
            logger.LogDebug("已处理剪贴板消息，类型: {clipboardType}", clipboardType);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "解析剪贴板消息失败: {payload}", payload);
        }
    }
}
