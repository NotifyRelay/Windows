using Windows.System;

namespace NotifyRelay.Views.Onboarding;

public sealed partial class WelcomePage : Page
{
    public WelcomePage()
    {
        InitializeComponent();
    }

    private async void OnGitHubButton_Click(object sender, RoutedEventArgs e)
    {
        var uri = new Uri(Constants.ExternalUrl.AndroidGitHubRepoUrl);
        await Launcher.LaunchUriAsync(uri);
    }

    private void OnGetStartedButton_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(SyncPage));
    }
}
