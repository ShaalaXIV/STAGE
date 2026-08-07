using STAGE.Tools;
using System.Diagnostics;
using System.Text;

namespace STAGE.Utils
{
    internal static class FFMpeg
    {
        public static void ConvertMp3ToOgg(string mp3Path, string oggName)
        {
            Run("-i " + '"' + mp3Path + '"' + " -vn -acodec libvorbis -f ogg -q 7 -af \"apad=pad_dur=5\" " + '"' + oggName + '"');
        }

        public static void StripVideo(string oggName)
        {
            string tmp = Path.Combine(Path.GetDirectoryName(oggName), "tmp_" + Path.GetFileName(oggName));
            Run("-i " + '"' + oggName + '"' + " -vn -codec:a libvorbis -q 7  " + '"' + tmp + '"');
            File.Move(tmp, oggName, true);
        }

        public static void PrepareScdAudio(string oggName, bool normalizeVolume, float volumeLevel)
        {
            var filters = new List<string>
            {
                "asetpts=PTS-STARTPTS",
                "aresample=44100"
            };

            if (normalizeVolume)
                filters.Add("loudnorm=I=-16:LRA=11:TP=-1.0");

            float volumeFactor = EqualizerSettings.UserValueToVolumeFactor(volumeLevel);
            if (Math.Abs(volumeFactor - 1f) > 0.01f)
            {
                filters.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"volume={volumeFactor}"));
            }

            filters.Add("alimiter=limit=0.97");
            TranscodeInPlace(oggName, string.Join(",", filters));
        }

        public static void Equalize(string oggName, string filterChain)
        {
            TranscodeInPlace(oggName, filterChain);
        }

        public static void PrepareImportAudio(
            string sourcePath, string outputOggPath, EqualizerSettings settings, bool normalizeVolume)
        {
            Run($"-y -fflags +genpts -avoid_negative_ts make_zero -i \"{sourcePath}\" -vn " +
                $"-af \"{settings.ToFilterChain(normalizeVolume)}\" -ar 44100 -ac 2 " +
                $"-c:a libvorbis -q:a 8 \"{outputOggPath}\"");
        }

        private static void TranscodeInPlace(string oggName, string filterChain)
        {
            string tmp = Path.Combine(Path.GetDirectoryName(oggName), "tmp_" + Path.GetFileName(oggName));
            try
            {
                Run($"-y -fflags +genpts -avoid_negative_ts make_zero -i \"{oggName}\" -vn " +
                    $"-af \"{filterChain}\" -ar 44100 -ac 2 -c:a libvorbis -q:a 8 \"{tmp}\"");
                File.Move(tmp, oggName, true);
            }
            finally
            {
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }
        }

        private static void Run(string arguments, int timeoutMs = 180_000)
        {
            using var process = new Process();
            process.StartInfo.FileName = File.Exists(DependencyUpdateService.GetFfmpegPath())
                ? DependencyUpdateService.GetFfmpegPath()
                : "ffmpeg.exe";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.Arguments = "-nostdin -hide_banner -v error " + arguments;

            var errors = new StringBuilder();
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    errors.AppendLine(e.Data);
            };

            if (!process.Start())
                throw new InvalidOperationException("Failed to start FFmpeg.");

            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
            if (!process.WaitForExit(timeoutMs))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException($"FFmpeg timed out after {timeoutMs / 1000} seconds.");
            }

            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                string message = errors.ToString().Trim();
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(message)
                    ? $"FFmpeg failed with exit code {process.ExitCode}."
                    : message);
            }
        }
    }
}
