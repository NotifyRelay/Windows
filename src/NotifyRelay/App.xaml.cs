using System.Runtime.InteropServices;
using CommunityToolkit.WinUI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Windows.AppLifecycle;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Enums;
using NotifyRelay.Helpers;
using NotifyRelay.Platforms.Windows.Interop;
using NotifyRelay.Views;
using NotifyRelay.Views.Onboarding;
using Windows.ApplicationModel.Activation;
using WinRT.Interop;
using WinUIEx;
using AppInstance = Microsoft.Windows.AppLifecycle.AppInstance;
using LaunchActivatedEventArgs = Microsoft.UI.Xaml.LaunchActivatedEventArgs;

namespace NotifyRelay;

public partial class App : Microsoft.UI.Xaml.Application
{
    /// <summary>
    /// 单实例注册键。Windows App SDK 桌面应用默认允许多实例，
    /// 必须显式 <see cref="AppInstance.FindOrRegisterForKey"/> 才能保证单实例。
    /// </summary>
    private const string SingleInstanceKey = "NotifyRelay.MainInstance";

    public static TaskCompletionSource? SplashScreenLoadingTCS { get; private set; }
    public static bool HandleClosedEvents { get; set; } = true;
    public static nint WindowHandle { get; private set; }
    public static Window MainWindow { get; private set; } = null!;
    protected IHost? Host { get; private set; }

    public App()
    {
        InitializeComponent();

        // Configure exception handlers
        UnhandledException += (sender, e) => AppLifecycleHelper.HandleAppUnhandledException(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (sender, e) => AppLifecycleHelper.HandleAppUnhandledException(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (sender, e) => AppLifecycleHelper.HandleAppUnhandledException(e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 单实例闸门：必须在创建窗口、启动服务（绑定 5152-5169 端口）之前完成。
        // 若已有实例持有该键，则把本次激活重定向过去并结束本进程。
        if (!TryClaimSingleInstance())
            return;

        _ = ActivateAsync();

        async Task ActivateAsync()
        {
            MainWindow = new Window
            {
                SystemBackdrop = new MicaBackdrop()
            };
            MainWindow.AppWindow.Title = "NotifyRelay";
            MainWindow.ExtendsContentIntoTitleBar = true;
            MainWindow.SetIcon(@"Assets\Icons\NotifyRelayLight.ico");
            WindowHandle = WindowNative.GetWindowHandle(MainWindow);
            var host = AppLifecycleHelper.BuildHost();
            Host = host;
            Ioc.Default.ConfigureServices(Host.Services);
            await Host.StartAsync();

            var appActivationArguments = AppInstance.GetCurrent().GetActivatedEventArgs();
            bool isStartupTask = appActivationArguments.Data is IStartupTaskActivatedEventArgs;

            HookEventsForWindow();
            bool isStartupRegistered = ApplicationData.Current.LocalSettings.Values["isStartupRegistered"] == null;
            if (isStartupRegistered)
            {
                await AppLifecycleHelper.HandleStartupTaskAsync(true);
                ApplicationData.Current.LocalSettings.Values["isStartupRegistered"] = true;
            }
            var rootFrame = EnsureWindowIsInitialized();
            if (rootFrame is null)
                return;

            if (isStartupTask)
            {
                var userSettingsService = Ioc.Default.GetRequiredService<IUserSettingsService>();
                var startupOption = userSettingsService.GeneralSettingsService.StartupOption;
                switch (startupOption)
                {
                    case StartupOptions.InTray:
                        // Don't activate or show the window
                        break;
                    case StartupOptions.Minimized:
                        // Need to show the window first, then minimize it
                        MainWindow.Activate();
                        await Task.Delay(200);
                        OverlappedPresenter overlappedPresenter = (MainWindow.AppWindow.Presenter as OverlappedPresenter) ?? OverlappedPresenter.Create();
                        if (overlappedPresenter.IsMinimizable)
                        {
                            overlappedPresenter.Minimize();
                        }
                        break;
                    default:
                        MainWindow.Activate();
                        MainWindow.AppWindow.Show();
                        break;
                }
                ;
            }
            else
            {
                MainWindow.Activate();
                // Wait for the Window to initialize
                await Task.Delay(10);
                MainWindow.AppWindow.Show();
            }

            rootFrame.Navigate(typeof(Views.SplashScreen));

            SplashScreenLoadingTCS = new TaskCompletionSource();
            try
            {
                await SplashScreenLoadingTCS!.Task.WaitAsync(TimeSpan.FromMilliseconds(500));
            }
            catch (TimeoutException)
            {
            }
            SplashScreenLoadingTCS = null;

            await AppLifecycleHelper.InitializeAppComponentsAsync();

            bool isOnboarding = ApplicationData.Current.LocalSettings.Values["HasCompletedOnboarding"] == null;
            if (isOnboarding)
            {
                // Navigate to onboarding page
                rootFrame.Navigate(typeof(WelcomePage), null, new SuppressNavigationTransitionInfo());
            }
            else
            {
                // Navigate to main page
                rootFrame.Navigate(typeof(MainPage), null, new SuppressNavigationTransitionInfo());
            }
        }
    }

    public Frame? EnsureWindowIsInitialized()
    {
        try
        {
            //  NOTE:
            //  Do not repeat app initialization when the Window already has content,
            //  just ensure that the window is active
            if (MainWindow.Content is not Frame rootFrame)
            {
                // Create a Frame to act as the navigation context and navigate to the first page
                rootFrame = new() { CacheSize = 1 };
                rootFrame.NavigationFailed += OnNavigationFailed;

                // Place the frame in the current Window
                MainWindow.Content = rootFrame;
            }

            return rootFrame;
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>
    /// 单实例闸门。返回 false 表示本进程不应继续启动（激活已重定向到既有实例）。
    /// </summary>
    private static bool TryClaimSingleInstance()
    {
        try
        {
            var instance = AppInstance.FindOrRegisterForKey(SingleInstanceKey);
            if (instance.IsCurrent)
            {
                // 本实例持有该键，接管后续重定向过来的激活
                instance.Activated += OnActivated;
                return true;
            }

            // 已有实例持有该键：把本次激活重定向过去，然后结束本进程。
            // 重定向必须等待完成，而等待期间必须继续泵消息，否则 RedirectActivationToAsync
            // 无法推进（见 WindowsAppSDK issue #1709）——因此用事件 + CoWaitForMultipleObjects，
            // 与原 Program.cs 的做法一致。
            RedirectActivationTo(instance);

            // 重定向已完成。本进程尚未创建窗口、也未启动任何服务（不占用 5152-5169 端口），
            // 直接退出即可，等同于原先从 Main 返回。
            Environment.Exit(0);
            return false;
        }
        catch (COMException)
        {
            // AppInstance 不可用（例如非打包运行）时退化为正常单次启动
            return true;
        }
    }

    /// <summary>
    /// 把本次激活重定向到既有实例，并阻塞等待其完成；等待期间持续泵消息。
    /// </summary>
    private static void RedirectActivationTo(AppInstance keyInstance)
    {
        var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();

        nint redirectEventHandle = InteropHelpers.CreateEvent(nint.Zero, true, false, null!);

        // 在后台线程发起重定向，避免等待与 UI 线程互相阻塞
        Task.Run(() =>
        {
            try
            {
                keyInstance.RedirectActivationToAsync(activatedArgs).AsTask().Wait();
            }
            finally
            {
                InteropHelpers.SetEvent(redirectEventHandle);
            }
        });

        const uint CWMO_DEFAULT = 0;
        const uint INFINITE = 0xFFFFFFFF;

        _ = InteropHelpers.CoWaitForMultipleObjects(CWMO_DEFAULT, INFINITE, 1, [redirectEventHandle], out _);
    }

    private static async void OnActivated(object? sender, AppActivationArguments args)
    {
        if (Current is App app)
        {
            await app.OnActivatedAsync(args);
        }
    }

    /// <summary>
    /// Gets invoked when the application is activated.
    /// </summary>
    public async Task OnActivatedAsync(AppActivationArguments activatedEventArgs)
    {
        // 激活可能早于窗口创建到达，此时无处可投递，直接忽略
        var window = MainWindow;
        if (window is null)
            return;

        // InitializeApplication accesses UI, needs to be called on UI thread
        await window.DispatcherQueue.EnqueueAsync(() => InitializeApplicationAsync(activatedEventArgs));
    }

    public static async Task InitializeApplicationAsync(AppActivationArguments activatedEventArgs)
    {
        try
        {
            switch (activatedEventArgs.Data)
            {
                case ShareTargetActivatedEventArgs shareArgs:
                    await HandleShareTargetActivation(shareArgs);
                    break;
                default:
                    MainWindow.AppWindow.Show();
                    MainWindow.Activate();
                    break;
            }
        }
        catch (COMException)
        {
            // Data not available 
            // Can happen when share data operation is not completed
            return;
        }
    }

    private void HookEventsForWindow()
    {
        MainWindow.Closed += Window_Closed;
    }

    private void Window_Closed(object sender, WindowEventArgs args)
    {
        if (HandleClosedEvents)
        {
            args.Handled = true;
            MainWindow.Hide();
        }
    }

    public static async Task HandleShareTargetActivation(ShareTargetActivatedEventArgs args)
    {
        var shareOperation = args.ShareOperation;
        var fileTransferService = Ioc.Default.GetRequiredService<IFileTransferService>();
        var items = await shareOperation.Data.GetStorageItemsAsync();
        shareOperation.ReportDataRetrieved();
        shareOperation.ReportCompleted();
        fileTransferService.SendFiles(items);
    }

    private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
            => new Exception("加载页面失败：" + e.SourcePageType.FullName);
}
