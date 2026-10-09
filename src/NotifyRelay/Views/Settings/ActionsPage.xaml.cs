using NotifyRelay.Data.Items;

namespace NotifyRelay.Views.Settings;

public sealed partial class ActionsPage : Page
{
    public ActionsPage()
    {
        InitializeComponent();
        SetupBreadcrumb();
    }

    private void SetupBreadcrumb()
    {
        BreadcrumbHelper.Setup(BreadcrumbBar, clickedPageType =>
        {
            if (clickedPageType != null && clickedPageType != typeof(ActionsPage))
            {
                // Navigate back to general page
                if (Frame.CanGoBack)
                {
                    Frame.GoBack();
                }
            }
        },
            ("General".GetLocalizedResource(), typeof(GeneralPage)),
            ("Actions".GetLocalizedResource(), typeof(ActionsPage)));
    }
}
