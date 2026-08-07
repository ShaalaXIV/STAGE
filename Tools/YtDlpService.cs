using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace STAGE.Tools;

public class YtDlpDownloadResult
{
    public required bool IsPlaylist { get; init; }
    public required string Title { get; init; }
    public required List<string> DownloadedFiles { get; init; }
}

public enum YtDownloadMode
{
    Single,
    Playlist
}

public enum YtCookieBrowser
{
    None,
    Firefox,
    Chrome,
    Edge
}

public sealed class YtDlpProgressInfo
{
    public required string Stage { get; init; }
    public required int Current { get; init; }
    public required int Total { get; init; }
    public double? Percent { get; init; }
}

public static class YtDlpService
{
    private const string YtDlpExeName = "yt-dlp.exe";
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan UpdateTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);
    private static readonly string ToolDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "STAGE", "current", "tools");
    private static readonly string LocalYtDlpPath = Path.Combine(ToolDirectory, YtDlpExeName);

    public static async Task EnsureUpToDateAsync()
    {
        Directory.CreateDirectory(ToolDirectory);
        if (!File.Exists(LocalYtDlpPath))
        {
            string bundledPath = GetBundledYtDlpPath();
            if (File.Exists(bundledPath))
            {
                File.Copy(bundledPath, LocalYtDlpPath, overwrite: true);
            }
            else
            {
                using var client = new HttpClient { Timeout = NetworkTimeout };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("STAGE/1.0");
                var bytes = await client.GetByteArrayAsync("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe");
                await File.WriteAllBytesAsync(LocalYtDlpPath, bytes);
            }
        }

        try
        {
            await RunYtDlpAsync("-U", UpdateTimeout);
        }
        catch
        {
            // Auto-update is best-effort; keep using the existing bundled/local copy.
        }
    }

    public static string GetYtDlpPath()
    {
        if (File.Exists(LocalYtDlpPath))
            return LocalYtDlpPath;

        string bundledPath = GetBundledYtDlpPath();
        return File.Exists(bundledPath) ? bundledPath : LocalYtDlpPath;
    }

    private static string GetBundledYtDlpPath() =>
        Path.Combine(AppContext.BaseDirectory, "Tools", YtDlpExeName);

    public static async Task<YtDlpDownloadResult> DownloadAudioAsync(
        string url,
        string outputDirectory,
        YtDownloadMode mode,
        YtCookieBrowser cookieBrowser = YtCookieBrowser.None,
        Action<YtDlpProgressInfo>? onProgress = null)
    {
        try
        {
            return await DownloadAudioCoreAsync(url, outputDirectory, mode, cookieBrowser, onProgress);
        }
        catch (InvalidOperationException ex) when (cookieBrowser != YtCookieBrowser.None && IsBrowserCookieFailure(ex.Message))
        {
            onProgress?.Invoke(new YtDlpProgressInfo { Stage = "Retrying without browser cookies", Current = 1, Total = 1 });
            try
            {
                return await DownloadAudioCoreAsync(url, outputDirectory, mode, YtCookieBrowser.None, onProgress);
            }
            catch (Exception fallbackEx)
            {
                throw new InvalidOperationException(
                    $"{GetBrowserDisplayName(cookieBrowser)} cookies could not be used. " +
                    $"Close all {GetBrowserDisplayName(cookieBrowser)} windows and background processes, make sure this app is running as your normal Windows user, or try Firefox cookies instead. " +
                    $"Retrying without cookies also failed: {fallbackEx.Message}",
                    fallbackEx);
            }
        }
    }

    private static async Task<YtDlpDownloadResult> DownloadAudioCoreAsync(
        string url,
        string outputDirectory,
        YtDownloadMode mode,
        YtCookieBrowser cookieBrowser,
        Action<YtDlpProgressInfo>? onProgress)
    {
        Directory.CreateDirectory(outputDirectory);
        string playlistFlag = mode == YtDownloadMode.Playlist ? "--yes-playlist" : "--no-playlist";
        string cookieArgument = GetCookieArgument(cookieBrowser);

        var infoJson = await RunYtDlpAsync(
            $"--dump-single-json --no-warnings --skip-download -f \"bestaudio/best\" {cookieArgument} {playlistFlag} \"{url}\"",
            MetadataTimeout);
        var parsed = JObject.Parse(infoJson);
        var title = parsed.Value<string>("title") ?? "YouTube Download";
        bool isPlaylist = string.Equals(parsed.Value<string>("_type"), "playlist", StringComparison.OrdinalIgnoreCase);
        int totalItems = Math.Max(1, parsed["entries"]?.Count() ?? (isPlaylist ? 0 : 1));

        string template = "%(title)s.%(ext)s";
        await RunYtDlpWithProgressAsync(
            $"-f \"bestaudio/best\" -x --audio-format vorbis --audio-quality 5 --newline --no-warnings " +
            $"{cookieArgument} {playlistFlag} -o \"{Path.Combine(outputDirectory, template)}\" \"{url}\"",
            totalItems,
            onProgress);

        var files = Directory.GetFiles(outputDirectory, "*.ogg", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x, STAGE.Utils.NaturalStringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
            throw new InvalidOperationException("yt-dlp completed but no OGG files were created.");

        return new YtDlpDownloadResult
        {
            IsPlaylist = isPlaylist,
            Title = title,
            DownloadedFiles = files
        };
    }

    private static bool IsBrowserCookieFailure(string message)
    {
        return message.Contains("Could not copy Chrome cookie database", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Failed to decrypt with DPAPI", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) &&
               message.Contains("Network\\Cookies", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetBrowserDisplayName(YtCookieBrowser browser)
    {
        return browser switch
        {
            YtCookieBrowser.Firefox => "Firefox",
            YtCookieBrowser.Chrome => "Chrome",
            YtCookieBrowser.Edge => "Microsoft Edge",
            _ => "Browser"
        };
    }

    private static string GetCookieArgument(YtCookieBrowser browser)
    {
        return browser switch
        {
            YtCookieBrowser.Firefox => "--cookies-from-browser firefox",
            YtCookieBrowser.Chrome => "--cookies-from-browser chrome",
            YtCookieBrowser.Edge => "--cookies-from-browser edge",
            _ => string.Empty
        };
    }

    private static async Task<string> RunYtDlpAsync(string arguments, TimeSpan? timeout = null)
    {
        using var process = new Process();
        process.StartInfo.FileName = GetYtDlpPath();
        process.StartInfo.Arguments = $"--ignore-config {arguments}";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;

        process.Start();
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        await WaitForExitOrTimeoutAsync(process, timeout);
        string stdout = await stdoutTask;
        string stderr = await stderrTask;

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"yt-dlp failed ({process.ExitCode}): {stderr}");

        return stdout;
    }

    private static async Task RunYtDlpWithProgressAsync(string arguments, int totalItems, Action<YtDlpProgressInfo>? onProgress)
    {
        using var process = new Process();
        process.StartInfo.FileName = GetYtDlpPath();
        process.StartInfo.Arguments = $"--ignore-config {arguments}";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;

        var stderrBuffer = new List<string>();
        int currentItem = 0;
        int total = Math.Max(1, totalItems);

        process.Start();
        Task stderrTask = Task.Run(async () =>
        {
            while (!process.StandardError.EndOfStream)
            {
                string? errLine = await process.StandardError.ReadLineAsync();
                if (!string.IsNullOrWhiteSpace(errLine))
                    stderrBuffer.Add(errLine.Trim());
            }
        });

        Task outputTask = Task.Run(async () =>
        {
            while (!process.StandardOutput.EndOfStream)
            {
                string? line = await process.StandardOutput.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (line.StartsWith("[download] Destination:", StringComparison.OrdinalIgnoreCase))
                {
                    currentItem = Math.Min(total, currentItem + 1);
                    onProgress?.Invoke(new YtDlpProgressInfo { Stage = "Downloading", Current = currentItem, Total = total });
                    continue;
                }

                if (line.StartsWith("[download]", StringComparison.OrdinalIgnoreCase) && line.Contains('%'))
                {
                    double? percent = TryParsePercent(line);
                    onProgress?.Invoke(new YtDlpProgressInfo { Stage = "Downloading", Current = Math.Max(1, currentItem), Total = total, Percent = percent });
                    continue;
                }

                if (line.StartsWith("[ExtractAudio]", StringComparison.OrdinalIgnoreCase))
                {
                    onProgress?.Invoke(new YtDlpProgressInfo { Stage = "Converting", Current = Math.Max(1, currentItem), Total = total });
                }
            }
        });

        await WaitForExitOrTimeoutAsync(process, DownloadTimeout);
        await Task.WhenAll(outputTask, stderrTask);
        if (process.ExitCode != 0)
        {
            string err = string.Join(Environment.NewLine, stderrBuffer.Where(x => !string.IsNullOrWhiteSpace(x)));
            throw new InvalidOperationException($"yt-dlp failed ({process.ExitCode}): {err}");
        }
    }

    private static async Task WaitForExitOrTimeoutAsync(Process process, TimeSpan? timeout)
    {
        if (timeout == null)
        {
            await process.WaitForExitAsync();
            return;
        }

        using var cts = new CancellationTokenSource(timeout.Value);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch { }
            throw new TimeoutException($"yt-dlp timed out after {timeout.Value.TotalSeconds:0} seconds.");
        }
    }

    private static double? TryParsePercent(string line)
    {
        int percentIdx = line.IndexOf('%');
        if (percentIdx <= 0)
            return null;

        int start = percentIdx - 1;
        while (start >= 0 && (char.IsDigit(line[start]) || line[start] == '.'))
            start--;

        string token = line.Substring(start + 1, percentIdx - start - 1);
        if (double.TryParse(token, out double value))
            return value;

        return null;
    }
}
