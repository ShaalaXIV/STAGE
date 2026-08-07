using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.IO.Compression;

namespace STAGE.Tools;

public static class DependencyUpdateService
{
    private static readonly string ToolDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "STAGE", "current", "tools");
    private static readonly string LocalFfmpegPath = Path.Combine(ToolDirectory, "ffmpeg.exe");
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(20);

    public static string GetFfmpegPath()
    {
        if (File.Exists(LocalFfmpegPath))
            return LocalFfmpegPath;

        string bundledPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        return File.Exists(bundledPath) ? bundledPath : LocalFfmpegPath;
    }

    public static async Task EnsureDependenciesUpToDateAsync(
        Action<string>? onStatus = null,
        bool allowLargeDownloads = true)
    {
        Directory.CreateDirectory(ToolDirectory);
        onStatus?.Invoke("Checking yt-dlp updates...");
        await YtDlpService.EnsureUpToDateAsync();

        if (allowLargeDownloads)
        {
            onStatus?.Invoke("Checking FFmpeg updates...");
            await EnsureFfmpegUpToDateAsync();
        }
        else
        {
            onStatus?.Invoke("Preparing FFmpeg...");
            EnsureBundledFfmpegAvailable();
        }
    }

    public static async Task EnsureFfmpegUpToDateAsync()
    {
        EnsureBundledFfmpegAvailable();
        string latestTag = await GetLatestFfmpegTagAsync();
        string currentTag = Settings.FfmpegBuildTag;

        if (File.Exists(LocalFfmpegPath) && string.Equals(currentTag, latestTag, StringComparison.OrdinalIgnoreCase))
            return;

        await DownloadLatestFfmpegAsync();
        Settings.FfmpegBuildTag = latestTag;
    }

    private static async Task<string> GetLatestFfmpegTagAsync()
    {
        using var client = CreateHttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("STAGE/1.0");

        string json = await client.GetStringAsync("https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/latest");
        var obj = JObject.Parse(json);
        return obj.Value<string>("tag_name") ?? "unknown";
    }

    private static async Task DownloadLatestFfmpegAsync()
    {
        string zipPath = Path.Combine(Path.GetTempPath(), $"stage-ffmpeg-{Guid.NewGuid():N}.zip");
        string extractRoot = Path.Combine(Path.GetTempPath(), $"stage-ffmpeg-{Guid.NewGuid():N}");

        try
        {
            using var client = CreateHttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("STAGE/1.0");
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

    private static void EnsureBundledFfmpegAvailable()
    {
        if (File.Exists(LocalFfmpegPath))
            return;

        string bundledPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (!File.Exists(bundledPath))
            return;

        Directory.CreateDirectory(ToolDirectory);
        File.Copy(bundledPath, LocalFfmpegPath, overwrite: true);
    }

    private static HttpClient CreateHttpClient() => new()
    {
        Timeout = NetworkTimeout
    };

    public static string GetFfmpegVersion()
    {
        string path = GetFfmpegPath();
        using var process = new Process();
        process.StartInfo.FileName = path;
        process.StartInfo.Arguments = "-version";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.Start();
        string firstLine = process.StandardOutput.ReadLine() ?? "Unknown";
        if (!process.WaitForExit(5000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch { }
            return "FFmpeg version check timed out.";
        }
        return firstLine;
    }
}
