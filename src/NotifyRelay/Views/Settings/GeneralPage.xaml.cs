using Microsoft.UI.Xaml.Media.Animation;
using NotifyRelay.Data.Items;
using NotifyRelay.Utils;
using Windows.System;

namespace NotifyRelay.Views.Settings;

public sealed partial class GeneralPage : Page
{
    public GeneralPage()
    {
        InitializeComponent();
        SetupBreadcrumb();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            Focus(FocusState.Pointer);
            e.Handled = true;
        }
    }

    private void SetupBreadcrumb()
    {
        BreadcrumbHelper.Setup(BreadcrumbBar, clickedPageType =>
        {
            if (clickedPageType != typeof(ActionsPage))
            {
                // Navigate back to general page
                if (Frame.CanGoBack)
                {
                    Frame.GoBack();
                }
            }
        },
            ("General".GetLocalizedResource(), typeof(GeneralPage)));
    }

    public async void SelectSaveLocation_Click(object sender, RoutedEventArgs e)
    {
        if (await PickerHelper.PickFolderAsync() is StorageFolder folder)
        {
            ViewModel.ReceivedFilesPath = folder.Path;
        }
    }

    private void OpenActionsSettings(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(ActionsPage), null, new SuppressNavigationTransitionInfo());
    }
}

