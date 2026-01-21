using NotifyRelay.Data.Models;

namespace NotifyRelay.Helpers;

public static class FileHelper
{
    public static async Task<FileMetadata> ToFileMetadata(this StorageFile file)
    {
        return new FileMetadata
        {
            FileName = file.Name,
            MimeType = file.ContentType,
            FileSize = (long)(await file.GetBasicPropertiesAsync()).Size,
        };
    }
}
