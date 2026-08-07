using libZPlay;
using PersistentCollection;
using STAGE.Tools;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace STAGE.Utils
{
    internal class BPMDetector
    {
        private static readonly string s_dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "S.T.A.G.E.");

        private static PersistentDictionary<string, int> bpmCache = new PersistentDictionary<string, int>(
            Path.Combine(EnsureDataDir(), "bpm_cache.dat"));
        private static PersistentDictionary<string, int> durationCache = new PersistentDictionary<string, int>(
            Path.Combine(s_dataDir, "duration_cache.dat"));
        private static readonly object s_cacheLock = new();
        private static readonly SemaphoreSlim s_analysisGate = new(1, 1);

        private static string EnsureDataDir()
        {
            Directory.CreateDirectory(s_dataDir);
            return s_dataDir;
        }

        internal static bool IsFirstTimeMessage()
        {
            lock (s_cacheLock)
            {
                try
                {
                    return bpmCache["FIRST_TIME_MESSAGE_SHOWN"] != 7;
                }
                catch
                {
                    return true;
                }
            }
        }

        internal static void MarkFirstTimeMessageShown()
        {
            lock (s_cacheLock)
            {
                bpmCache["FIRST_TIME_MESSAGE_SHOWN"] = 7;
            }
        }

        private struct SongAttributes
        {
            public int BPM;
            public TimeSpan Duration;
        }

        static private SongAttributes GetAttribtesFromFile(string oggFile)
        {
            ZPlay player = null;
            try
            {
                player = new ZPlay();
                int duration = 0;

                // Open the OGG file
                if (!player.OpenFile(oggFile, TStreamFormat.sfOgg))
                {
                    Console.WriteLine("Failed to open audio file.");
                    return new SongAttributes();
                }

                // Attempt to get stream info (duration) and cache it
                try
                {
                    var info = new TStreamInfo();
                    player.GetStreamInfo(ref info);
                    // info.Length.ms holds length in milliseconds
                    var ms = info.Length.ms;
                    if (ms > 0)
                    {
                        duration = (int)Math.Round(ms / 1000.0);
                    }
                }
                catch
                {
                    // ignore failures to obtain duration but continue BPM detection
                }

                // Detect BPM
                int retval = 0;

                // Parameters: using peak method (tbmDefault recommended in libZPlay docs)
                try
                {
                    retval = player.DetectBPM(TBPMDetectionMethod.dmPeaks);
                }
                catch
                {
                    retval = 0;
                }

                return new SongAttributes() { BPM = retval, Duration = new TimeSpan(0, 0, duration) };
            }
            catch (Exception ex)
            {
                return new SongAttributes();
            }
            finally
            {
                player?.Close();
            }
        }

        private static string ResolveScdPath(string scdFile)
        {
            if (Path.IsPathRooted(scdFile))
                return Path.GetFullPath(scdFile);

            return Path.GetFullPath(Path.Combine(Settings.PenumbraLocation, Settings.ModName, scdFile));
        }

        public static bool TryGetCachedDuration(string scdFile, out TimeSpan duration)
        {
            string path = ResolveScdPath(scdFile);
            lock (s_cacheLock)
            {
                if (durationCache.ContainsKey(path))
                {
                    duration = TimeSpan.FromSeconds(durationCache[path]);
                    return true;
                }
            }

            duration = TimeSpan.Zero;
            return false;
        }

        public static bool TryGetCachedBPM(string scdFile, out int bpm)
        {
            string path = ResolveScdPath(scdFile);
            lock (s_cacheLock)
            {
                if (bpmCache.ContainsKey(path))
                {
                    bpm = bpmCache[path];
                    return true;
                }
            }

            bpm = 0;
            return false;
        }

        private static SongAttributes AnalyzeFile(string path)
        {
            string tmpOgg = Path.Combine(Path.GetTempPath(), $"stage-bpm-{Guid.NewGuid():N}.ogg");
            try
            {
                ScdOggExtractor.ExtractOgg(path, tmpOgg);
                SongAttributes attributes = GetAttribtesFromFile(tmpOgg);
                lock (s_cacheLock)
                {
                    bpmCache[path] = attributes.BPM;
                    durationCache[path] = (int)attributes.Duration.TotalSeconds;
                }
                return attributes;
            }
            finally
            {
                try
                {
                    if (File.Exists(tmpOgg))
                        File.Delete(tmpOgg);
                }
                catch
                {
                    // A failed temp cleanup should not fail the audio scan.
                }
            }
        }

        public static async Task AnalyzeQueuedAsync(string scdFile, CancellationToken cancellationToken)
        {
            string path = ResolveScdPath(scdFile);
            if (TryGetCachedBPM(path, out _))
                return;

            await s_analysisGate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryGetCachedBPM(path, out _))
                    await Task.Run(() => AnalyzeFile(path), cancellationToken);
            }
            finally
            {
                s_analysisGate.Release();
            }

            await Task.Delay(75, cancellationToken);
        }

        public static TimeSpan GetDuration(string scdFile)
        {
            string path = ResolveScdPath(scdFile);
            if (TryGetCachedDuration(path, out TimeSpan duration))
                return duration;

            s_analysisGate.Wait();
            try
            {
                if (TryGetCachedDuration(path, out duration))
                    return duration;
                return AnalyzeFile(path).Duration;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return TimeSpan.Zero;
            }
            finally
            {
                s_analysisGate.Release();
            }
        }

        public static void UpdateCacheForSCD(string oldFullPath, string newFullPath)
        {
            lock (s_cacheLock)
            {
                try
                {
                    if (bpmCache.ContainsKey(oldFullPath))
                    {
                        bpmCache[newFullPath] = bpmCache[oldFullPath];
                        bpmCache.Remove(oldFullPath);
                    }
                    if (durationCache.ContainsKey(oldFullPath))
                    {
                        durationCache[newFullPath] = durationCache[oldFullPath];
                        durationCache.Remove(oldFullPath);
                    }
                }
                catch
                {
                    // Ignore cache update failures.
                }
            }
        }

        public static int GetBPMFromSCD(string scdFile)
        {
            string path = ResolveScdPath(scdFile);
            if (TryGetCachedBPM(path, out int bpm))
                return bpm;

            s_analysisGate.Wait();
            try
            {
                if (TryGetCachedBPM(path, out bpm))
                    return bpm;
                return AnalyzeFile(path).BPM;
            }
            catch
            {
                return 0;
            }
            finally
            {
                s_analysisGate.Release();
            }
        }
    }
}
