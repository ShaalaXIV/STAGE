using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.IO.Compression;

namespace Pickles_Playlist_Editor.Tools;

public static class DependencyUpdateService
{
    private static readonly string ToolDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PicklesPlaylistEditor", "current", "tools");
    private static readonly string LocalFfmpegPath = Path.Combine(ToolDirectory, "ffmpeg.exe");

    public static string GetFfmpegPath() => LocalFfmpegPath;

    public static async Task EnsureDependenciesUpToDateAsync(Action<string>? onStatus = null)
    {
        Directory.CreateDirectory(ToolDirectory);
        onStatus?.Invoke("Checking yt-dlp updates...");
        await YtDlpService.EnsureUpToDateAsync();

        onStatus?.Invoke("Checking FFmpeg updates...");
        await EnsureFfmpegUpToDateAsync();
    }

    public static async Task EnsureFfmpegUpToDateAsync()
    {
        string latestTag = await GetLatestFfmpegTagAsync();
        string currentTag = Settings.FfmpegBuildTag;

        if (File.Exists(LocalFfmpegPath) && string.Equals(currentTag, latestTag, StringComparison.OrdinalIgnoreCase))
            return;

        await DownloadLatestFfmpegAsync();
        Settings.FfmpegBuildTag = latestTag;
    }

    private static async Task<string> GetLatestFfmpegTagAsync()
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PicklesPlaylistEditor/1.0");

        string json = await client.GetStringAsync("https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/latest");
        var obj = JObject.Parse(json);
        return obj.Value<string>("tag_name") ?? "unknown";
    }

    private static async Task DownloadLatestFfmpegAsync()
    {
        string zipPath = Path.Combine(Path.GetTempPath(), $"pickles-ffmpeg-{Guid.NewGuid():N}.zip");
        string extractRoot = Path.Combine(Path.GetTempPath(), $"pickles-ffmpeg-{Guid.NewGuid():N}");

        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PicklesPlaylistEditor/1.0");
            await using (var netStream = await client.GetStreamAsync("https://github.com/BtbN/FFmpeg-Builds/releases/latest/download/ffmpeg-master-latest-win64-gpl.zip"))
            await using (var fileStream = File.Create(zipPath))
            {
                await netStream.CopyToAsync(fileStream);
            }

            ZipFile.ExtractToDirectory(zipPath, extractRoot, overwriteFiles: true);
            string? ffmpegPath = Directory.EnumerateFiles(extractRoot, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(ffmpegPath))
                throw new InvalidOperationException("Unable to locate ffmpeg.exe in downloaded archive.");

            Directory.CreateDirectory(ToolDirectory);
            File.Copy(ffmpegPath, LocalFfmpegPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(zipPath))
                File.Delete(zipPath);
            if (Directory.Exists(extractRoot))
                Directory.Delete(extractRoot, recursive: true);
        }
    }

    public static string GetFfmpegVersion()
    {
        string path = File.Exists(LocalFfmpegPath) ? LocalFfmpegPath : "ffmpeg.exe";
        using var process = new Process();
        process.StartInfo.FileName = path;
        process.StartInfo.Arguments = "-version";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.Start();
        string firstLine = process.StandardOutput.ReadLine() ?? "Unknown";
        process.WaitForExit();
        return firstLine;
    }
}
