using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Items;
using NotifyRelay.DeviceCtrl.MonitorBrightness;
using Windows.Storage.Pickers;

namespace NotifyRelay.Views.Settings;

public sealed partial class MonitorBrightnessSettingsPage : Page
{
    public MonitorBrightnessViewModel ViewModel => (MonitorBrightnessViewModel)DataContext;

    public MonitorBrightnessSettingsPage()
    {
        InitializeComponent();
        Loaded += MonitorBrightnessSettingsPage_Loaded;
        SetupBreadcrumb();
    }

    private void MonitorBrightnessSettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.SyncStatusChanged += OnSyncStatusChanged;
        ViewModel.LoadMonitors();
        InitializeMonitorSelections();
        UpdateSyncStatusUI();
    }

    private void SetupBreadcrumb()
    {
        BreadcrumbBar.ItemsSource = new ObservableCollection<BreadcrumbBarItemModel>
        {
            new("显示器亮度", typeof(MonitorBrightnessSettingsPage))
        };
        BreadcrumbBar.ItemClicked += BreadcrumbBar_ItemClicked;
    }

    private void BreadcrumbBar_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        var items = BreadcrumbBar.ItemsSource as ObservableCollection<BreadcrumbBarItemModel>;
        var clickedItem = items?[args.Index];

        // Navigate back if needed
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private void OnSyncStatusChanged(object sender, EventArgs e)
    {
        UpdateSyncStatusUI();
    }

    private void UpdateSyncStatusUI()
    {
        if (ViewModel.IsSyncEnabled)
        {
            SyncStatusTextBlock.Text = "状态：运行中";
            SyncStatusTextBlock.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Green);
        }
        else
        {
            SyncStatusTextBlock.Text = "状态：未启动";
            SyncStatusTextBlock.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray);
        }
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var openPicker = new FileOpenPicker();
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hWnd);

        openPicker.ViewMode = PickerViewMode.List;
        openPicker.FileTypeFilter.Add(".exe");

        var file = await openPicker.PickSingleFileAsync();
        if (file != null)
        {
            ViewModel.ControlMyMonitorPath = file.Path;
        }
    }



    private void InitializeMonitorSelections()
    {
        UpdateMonitorsButtonContent();
    }

    private void MonitorsFlyout_Opened(object sender, object e)
    {
        // Initialize checkbox states when flyout opens
        var selected = ViewModel.SelectedMonitors ?? new List<string>();
        bool treatAll = !selected.Any() || selected.Contains("All");

        foreach (var cb in FindVisualChildren<CheckBox>(MonitorsItemsControl))
        {
            if (cb.Tag is string id)
            {
                cb.IsChecked = treatAll || selected.Contains(id);
            }
        }
    }

    private void MonitorCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.Tag is string id)
        {
            var list = ViewModel.SelectedMonitors?.ToList() ?? new List<string>();
            if (!list.Contains(id)) list.Add(id);
            ViewModel.SelectedMonitors = list;
            UpdateMonitorsButtonContent();
        }
    }

    private void MonitorCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.Tag is string id)
        {
            var list = ViewModel.SelectedMonitors?.ToList() ?? new List<string>();
            if (list.Contains(id)) list.Remove(id);
            ViewModel.SelectedMonitors = list;
            UpdateMonitorsButtonContent();
        }
    }

    private void UpdateMonitorsButtonContent()
    {
        var selected = ViewModel.SelectedMonitors ?? new List<string>();
        if (!selected.Any() || selected.Contains("All") || selected.Count == ViewModel.AvailableMonitors.Count)
        {
            MonitorsDropdownButton.Content = "All";
            return;
        }

        var names = ViewModel.AvailableMonitors
            .Where(m => selected.Contains(m.Id))
            .Select(m => m.Name)
            .ToList();
        MonitorsDropdownButton.Content = names.Any() ? string.Join(", ", names) : "All";
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root == null) yield break;
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var count = VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(current, i);
                if (child is T t) yield return t;
                queue.Enqueue(child);
            }
        }
    }
}

public class MonitorBrightnessViewModel
{
    private readonly IGeneralSettingsService _generalSettingsService;
    private readonly MonitorBrightnessService _monitorBrightnessService;

    public event EventHandler SyncStatusChanged;

    public string ControlMyMonitorPath
    {
        get => _generalSettingsService.ControlMyMonitorPath ?? string.Empty;
        set
        {
            _generalSettingsService.ControlMyMonitorPath = value;
        }
    }

    public bool IsSyncEnabled => _monitorBrightnessService.IsRunning;

    public ObservableCollection<NotifyRelay.DeviceCtrl.MonitorBrightness.MonitorInfo> AvailableMonitors { get; private set; } = new();

    public List<string> SelectedMonitors
    {
        get => _generalSettingsService.SelectedMonitors;
        set => _generalSettingsService.SelectedMonitors = value;
    }

    public bool SyncEnabled
    {
        get => _monitorBrightnessService.IsRunning;
        set
        {
            if (value && !_monitorBrightnessService.IsRunning)
            {
                _monitorBrightnessService.StartSync();
            }
            else if (!value && _monitorBrightnessService.IsRunning)
            {
                _monitorBrightnessService.StopSync();
            }
        }
    }

    public MonitorBrightnessViewModel()
    {
        _generalSettingsService = Ioc.Default.GetService<IGeneralSettingsService>()!;
        _monitorBrightnessService = Ioc.Default.GetService<MonitorBrightnessService>()!;
        _monitorBrightnessService.StatusChanged += (s, e) => SyncStatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void LoadMonitors()
    {
        var monitors = _monitorBrightnessService.GetAvailableMonitors();
        AvailableMonitors.Clear();
        foreach (var monitor in monitors)
        {
            AvailableMonitors.Add(monitor);
        }
    }

    public void ToggleSync()
    {
        if (_monitorBrightnessService.IsRunning)
        {
            _monitorBrightnessService.StopSync();
        }
        else
        {
            _monitorBrightnessService.StartSync();
        }
    }
}
