using NotifyRelay.Data.Items;
using NotifyRelay.Utils;

namespace NotifyRelay.Views.Settings;

public sealed partial class ScrcpyAdbSettingsPage : Page
{
    public ScrcpyAdbSettingsPage()
    {
        InitializeComponent();
        SetupBreadcrumb();
    }

    private void SetupBreadcrumb()
    {
        BreadcrumbBar.ItemsSource = new ObservableCollection<BreadcrumbBarItemModel>
        {
            new("ScrcpyAdbSettings".GetLocalizedResource(), typeof(ScrcpyAdbSettingsPage))
        };
        BreadcrumbBar.ItemClicked += BreadcrumbBar_ItemClicked;
    }

    private void BreadcrumbBar_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    public async void SelectScrcpyLocation_Click(object sender, RoutedEventArgs e)
    {
        var file = await PickerHelper.PickFileAsync();
        if (file?.Path is string path)
        {
            ViewModel.ScrcpyPath = path;
            ToolPathHelper.TrySetCompanionTool(path, "adb.exe", p => ViewModel.AdbPath = p);
        }
    }

    private async void SelectAdbLocation_Click(object sender, RoutedEventArgs e)
    {
        var file = await PickerHelper.PickFileAsync();
        if (file?.Path is string path)
        {
            ViewModel.AdbPath = path;
            ToolPathHelper.TrySetCompanionTool(path, "scrcpy.exe", p => ViewModel.ScrcpyPath = p);
        }
    }

}
