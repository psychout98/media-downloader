namespace MediaDownloader.Shared.Constants;

public static class VideoExtensions
{
    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".m4v", ".wmv", ".flv", ".mov"
    };

    public static bool IsVideoFile(string path)
    {
        var ext = Path.GetExtension(path);
        return All.Contains(ext);
    }
}
