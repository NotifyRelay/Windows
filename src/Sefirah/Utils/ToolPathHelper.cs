using System.IO;

namespace NotifyRelay.Utils;

public static class ToolPathHelper
{
    public static void TrySetCompanionTool(string selectedPath, string companionName, Action<string> setPath)
    {
        var directory = Path.GetDirectoryName(selectedPath);
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        var companionPath = Path.GetFullPath(Path.Combine(directory, companionName));
        if (File.Exists(companionPath))
        {
            setPath(companionPath);
        }
    }
}
