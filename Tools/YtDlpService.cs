using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using STAGE.Utils;

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

public enum CookieStatus
{
    NotFound,
    Valid,
    Expired,
    Invalid
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
    // Keep downloaded tools and authentication outside Velopack's replaceable "current" folder.
    private static readonly string ToolDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "STAGE", "tools");
    private static readonly string LegacyToolDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "STAGE", "current", "tools");
    private static readonly string LocalYtDlpPath = Path.Combine(ToolDirectory, YtDlpExeName);
    private static readonly string CookiesSavePath = Path.Combine(ToolDirectory, "cookies.txt");
    private static readonly string DenoExePath = Path.Combine(ToolDirectory, "deno.exe");
    private static readonly string DenoZipPath = Path.Combine(ToolDirectory, "deno.zip");
    private static string? _cookiesPath;
    private static TcpListener? _cookieListener;
    private static Thread? _cookieListenerThread;
    private static volatile bool _isListeningForCookies;

    public static bool HasCookies => !string.IsNullOrEmpty(_cookiesPath) && File.Exists(_cookiesPath);
    public static bool HasSoundCloudSignIn => Settings.HasSoundCloudToken;
    public static string WebViewDataDirectory
    {
        get
        {
            string path = Path.Combine(ToolDirectory, "webview2");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static void ClearSoundCloudSignIn()
    {
        Settings.SoundCloudToken = string.Empty;
        try
        {
            string path = Path.Combine(ToolDirectory, "webview2");
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch { }
    }

    public static CookieStatus GetCookieStatus()
    {
        if (!HasCookies)
            return CookieStatus.NotFound;

        try
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            bool sawCookie = false;
            long latestExpiry = 0;
            foreach (string rawLine in File.ReadLines(_cookiesPath!))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;
                string[] fields = line.Split('\t');
                if (fields.Length < 7)
                    continue;
                sawCookie = true;
                if (long.TryParse(fields[4], out long expiry) && expiry > latestExpiry)
                    latestExpiry = expiry;
            }
            if (!sawCookie) return CookieStatus.Invalid;
            if (latestExpiry > 0 && latestExpiry < now) return CookieStatus.Expired;
            return CookieStatus.Valid;
        }
        catch { return CookieStatus.Invalid; }
    }

    public static void StartCookieListener()
    {
        MigrateLegacyFiles();
        _cookiesPath = FindCookiesFile();
        try
        {
            _cookieListener = new TcpListener(IPAddress.Loopback, 9696);
            _cookieListener.Start();
            _isListeningForCookies = true;
            _cookieListenerThread = new Thread(CookieListenerLoop)
            {
                IsBackground = true,
                Name = "VRCVideoCacherCookieListener"
            };
            _cookieListenerThread.Start();
        }
        catch { }
    }

    public static void StopCookieListener()
    {
        _isListeningForCookies = false;
        try { _cookieListener?.Stop(); } catch { }
    }

    private static string? FindCookiesFile()
    {
        if (File.Exists(CookiesSavePath)) return CookiesSavePath;
        string shared = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCVideoCacher", "youtube_cookies.txt");
        return File.Exists(shared) ? shared : null;
    }

    private static void CookieListenerLoop()
    {
        while (_isListeningForCookies && _cookieListener != null)
        {
            try
            {
                using var client = _cookieListener.AcceptTcpClient();
                client.ReceiveTimeout = 5000;
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                int contentLength = 0;
                bool isPost = false;
                string? line;
                while (!string.IsNullOrEmpty(line = reader.ReadLine()))
                {
                    if (line.StartsWith("POST ", StringComparison.OrdinalIgnoreCase)) isPost = true;
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(line[15..].Trim(), out contentLength);
                }

                if (isPost && contentLength is > 0 and <= 2_000_000)
                {
                    char[] chars = new char[contentLength];
                    int read = reader.ReadBlock(chars, 0, contentLength);
                    string body = new(chars, 0, read);
                    if (body.Contains(".youtube.com", StringComparison.OrdinalIgnoreCase) && body.Contains('\t'))
                    {
                        Directory.CreateDirectory(ToolDirectory);
                        string tempPath = CookiesSavePath + ".tmp";
                        File.WriteAllText(tempPath, body, Encoding.UTF8);
                        File.Move(tempPath, CookiesSavePath, overwrite: true);
                        _cookiesPath = CookiesSavePath;
                    }
                }

                byte[] response = Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nAccess-Control-Allow-Origin: *\r\nAccess-Control-Allow-Methods: POST, OPTIONS\r\nAccess-Control-Allow-Headers: Content-Type\r\nConnection: close\r\nContent-Length: 2\r\n\r\nOK");
                stream.Write(response);
            }
            catch (SocketException) when (!_isListeningForCookies) { break; }
            catch { }
        }
    }

    private static void MigrateLegacyFiles()
    {
        try
        {
            Directory.CreateDirectory(ToolDirectory);
            foreach (string name in new[] { YtDlpExeName, "cookies.txt" })
            {
                string oldPath = Path.Combine(LegacyToolDirectory, name);
                string newPath = Path.Combine(ToolDirectory, name);
                if (File.Exists(oldPath) && !File.Exists(newPath))
                    File.Copy(oldPath, newPath);
            }
        }
        catch { }
    }

    public static async Task EnsureUpToDateAsync()
    {
        Directory.CreateDirectory(ToolDirectory);
        MigrateLegacyFiles();
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
                var bytes = await client.GetByteArrayAsync("https://github.com/yt-dlp/yt-dlp-nightly-builds/releases/latest/download/yt-dlp.exe");
                await File.WriteAllBytesAsync(LocalYtDlpPath, bytes);
            }
        }

        if (!File.Exists(DenoExePath))
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("STAGE/1.0");
            var bytes = await client.GetByteArrayAsync("https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip");
            await File.WriteAllBytesAsync(DenoZipPath, bytes);
            await Task.Run(() => ZipFile.ExtractToDirectory(DenoZipPath, ToolDirectory, overwriteFiles: true));
            try { File.Delete(DenoZipPath); } catch { }
        }

        try
        {
            await RunYtDlpAsync("--update-to nightly", UpdateTimeout);
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
        var service = MediaUrlInfo.Classify(url);
        if (service == MediaService.SoundCloud)
        {
            string? jar = CreateSoundCloudCookieJar();
            try
            {
                return await DownloadAudioCoreAsync(url, outputDirectory, mode, YtCookieBrowser.None, onProgress, cookieFilePath: jar);
            }
            finally
            {
                if (!string.IsNullOrEmpty(jar)) try { File.Delete(jar); } catch { }
            }
        }

        // Browser selection remains available as a manual override. Automatic mode first
        // tries the public video, then uses the cookie jar supplied by VRCVideoCacher only
        // when yt-dlp reports a YouTube sign-in/age restriction.
        if (cookieBrowser == YtCookieBrowser.None)
        {
            try
            {
                return await DownloadAudioCoreAsync(url, outputDirectory, mode, cookieBrowser, onProgress);
            }
            catch (InvalidOperationException ex) when (GetCookieStatus() == CookieStatus.Valid && LooksLikeAuthenticationFailure(ex.Message))
            {
                onProgress?.Invoke(new YtDlpProgressInfo { Stage = "Retrying with saved YouTube sign-in", Current = 1, Total = 1 });
                return await DownloadAudioCoreAsync(url, outputDirectory, mode, cookieBrowser, onProgress, useSavedCookies: true);
            }
        }

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
        Action<YtDlpProgressInfo>? onProgress,
        bool useSavedCookies = false,
        string? cookieFilePath = null)
    {
        Directory.CreateDirectory(outputDirectory);
        string playlistFlag = mode == YtDownloadMode.Playlist ? "--yes-playlist" : "--no-playlist";
        string cookieArgument = !string.IsNullOrEmpty(cookieFilePath)
            ? $"--cookies \"{cookieFilePath}\""
            : useSavedCookies && HasCookies
                ? $"--cookies \"{_cookiesPath}\""
                : GetCookieArgument(cookieBrowser);
        string denoArgument = File.Exists(DenoExePath) ? $"--js-runtimes \"deno:{DenoExePath}\"" : string.Empty;

        var infoJson = await RunYtDlpAsync(
            $"--dump-single-json --flat-playlist --no-warnings --skip-download -f \"bestaudio/best\" {denoArgument} {cookieArgument} {playlistFlag} -- \"{url}\"",
            MetadataTimeout);
        var parsed = JObject.Parse(infoJson);
        var title = parsed.Value<string>("title") ?? "YouTube Download";
        bool isPlaylist = string.Equals(parsed.Value<string>("_type"), "playlist", StringComparison.OrdinalIgnoreCase);
        int totalItems = Math.Max(1, parsed["entries"]?.Count() ?? (isPlaylist ? 0 : 1));

        string template = "%(title)s.%(ext)s";
        await RunYtDlpWithProgressAsync(
            $"-f \"bestaudio/best\" -x --audio-format vorbis --audio-quality 5 --newline --no-warnings " +
            $"{denoArgument} {cookieArgument} {playlistFlag} -o \"{Path.Combine(outputDirectory, template)}\" -- \"{url}\"",
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

    public static bool LooksLikeAuthenticationFailure(string message)
    {
        return message.Contains("Sign in to confirm your age", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("age-restricted", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Sign in to confirm you're not a bot", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("members-only", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("private video", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("cookies", StringComparison.OrdinalIgnoreCase) &&
               message.Contains("authentication", StringComparison.OrdinalIgnoreCase);
    }

    private static string? CreateSoundCloudCookieJar()
    {
        string token = Settings.SoundCloudToken;
        if (string.IsNullOrEmpty(token)) return null;
        try
        {
            string path = Path.Combine(Path.GetTempPath(), $"stage-sc-{Guid.NewGuid():N}.txt");
            long expiry = DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds();
            var text = new StringBuilder()
                .AppendLine("# Netscape HTTP Cookie File")
                .AppendLine($".soundcloud.com\tTRUE\t/\tTRUE\t{expiry}\toauth_token\t{token}")
                .ToString();
            File.WriteAllText(path, text);
            return path;
        }
        catch (Exception ex)
        {
            Logger.LogError("Could not prepare SoundCloud sign-in: {Error}", ex.Message);
            return null;
        }
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
