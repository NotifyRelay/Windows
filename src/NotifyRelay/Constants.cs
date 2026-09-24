namespace NotifyRelay;

public static class Constants
{
    public static class Notification
    {
        public const string FileTransferGroup = "file-transfer";
    }

    public static class ToastNotificationType
    {
        public const string FileTransfer = "FileTransfer";
        public const string RemoteNotification = "RemoteNotification";
        public const string Clipboard = "Clipboard";
    }
    public static class ExternalUrl
    {
        public const string ReleasesUrl = @"https://github.com/xzy-nine/NotifyRelay-pc/releases/latest";
        public const string AndroidGitHubRepoUrl = @"https://github.com/NotifyRelay/Android";
        public const string GitHubRepoUrl = @"https://github.com/xzy-nine/NotifyRelay-pc";
        public const string FeatureRequestUrl = @"https://github.com/xzy-nine/NotifyRelay-pc/issues/new?template=request_feature.yml";
        public const string BugReportUrl = @"https://github.com/xzy-nine/NotifyRelay-pc/issues/new?template=report_issue.yml";
    }

    public static class UserEnvironmentPaths
    {
        public static readonly string DownloadsPath = GetDownloadsPath();
        public static readonly string UserProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        private static string GetDownloadsPath()
        {
            string homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(homePath, "Downloads");

        }
    }
}
