using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Linq;

namespace STAGE
{
    internal class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                LogCrash("AppDomain: " + (e.ExceptionObject?.ToString() ?? "unknown"));

            try
            {
                try
                {
                    UserAssetStore.MigrateConfiguredAssets();
                }
                catch (Exception ex)
                {
                    LogCrash("User asset migration: " + ex);
                }

                try
                {
                    _ = ModBackupService.CleanupExpiredAutomaticBackups(
                        Settings.ManagedModFolders,
                        Settings.AutoBackupRetentionDays);
                }
                catch (Exception ex)
                {
                    LogCrash("Automatic backup retention: " + ex);
                }

                global::WinRT.ComWrappersSupport.InitializeComWrappers();
                global::Microsoft.UI.Xaml.Application.Start((p) =>
                {
                    try
                    {
                        var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                            global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
                        global::System.Threading.SynchronizationContext.SetSynchronizationContext(context);
                        _ = new App();
                    }
                    catch (Exception ex)
                    {
                        LogCrash("Application.Start callback: " + ex);
                        global::System.Environment.Exit(1);
                    }
                });
            }
            catch (Exception ex)
            {
                LogCrash("Main: " + ex);
                global::System.Environment.Exit(1);
            }
        }

        static void LogCrash(string message)
        {
            string text = $"[{DateTime.Now}]\n{message}\n";
            string[] paths =
            {
                AppEnvironment.CrashLogPath,
                Path.Combine(AppContext.BaseDirectory, "crash.log")
            };

            foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, text);
                }
                catch { }
            }
        }
    }
}
