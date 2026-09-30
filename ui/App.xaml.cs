using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Runtime.InteropServices;
using STAGE.Tools;

namespace STAGE
{
    public partial class App : Application
    {
        public static MainWindow MainWindow { get; private set; } = null!;

        public App()
        {
            this.InitializeComponent();

            this.UnhandledException += (_, e) =>
            {
                WriteStartupLog("Application.UnhandledException", e.Exception);
                e.Handled = false;
            };

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                WriteStartupLog("AppDomain.CurrentDomain.UnhandledException", e.ExceptionObject as Exception);
            };
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            try
            {
                WriteStartupLog("OnLaunched preflight", null);
                WriteStartupPreflightLog();
                YtDlpService.StartCookieListener();
                MainWindow = new MainWindow();
                MainWindow.Closed += (_, _) => YtDlpService.StopCookieListener();
                MainWindow.Activate();
            }
            catch (Exception ex)
            {
                WriteStartupLog("OnLaunched", ex);
                string startupLogPath = Path.Combine(AppEnvironment.CurrentDataDirectory, "startup.log");
                string fallbackLogPath = Path.Combine(AppContext.BaseDirectory, "startup.log");
                NativeMethods.MessageBoxW(IntPtr.Zero,
                    "Startup failure. See startup.log at:" + Environment.NewLine +
                    startupLogPath + Environment.NewLine +
                    Environment.NewLine +
                    "If that file is missing, check:" + Environment.NewLine +
                    fallbackLogPath,
                    AppEnvironment.DisplayName, 0x00000010);
                throw;
            }
        }

        internal static void WriteStartupLog(string source, Exception? ex)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("==== " + DateTime.Now.ToString("O") + " ====");
                sb.AppendLine(source);
                if (ex != null)
                {
                    AppendExceptionDetails(sb, ex, 0);
                }
                AppendStartupText(sb.ToString());
            }
            catch { }
        }

        private static void WriteStartupPreflightLog()
        {
            try
            {
                string root = AppContext.BaseDirectory;
                string[] requiredFiles =
                {
                    AppEnvironment.ExecutableBaseName + ".exe",
                    AppEnvironment.ExecutableBaseName + ".dll",
                    Path.Combine("ui", "App.xbf"),
                    Path.Combine("ui", "MainWindow.xbf"),
                    Path.Combine("Resources", "stage.png"),
                    "stage.ico",
                    "Microsoft.WinUI.dll",
                    "Microsoft.WindowsAppRuntime.Bootstrap.dll"
                };

                var sb = new StringBuilder();
                sb.AppendLine("BaseDirectory: " + root);
                sb.AppendLine("OSVersion: " + Environment.OSVersion);
                sb.AppendLine("Is64BitOperatingSystem: " + Environment.Is64BitOperatingSystem);
                sb.AppendLine("Is64BitProcess: " + Environment.Is64BitProcess);
                sb.AppendLine(".NET: " + Environment.Version);
                sb.AppendLine("ProcessArchitecture: " + RuntimeInformation.ProcessArchitecture);
                sb.AppendLine("OSArchitecture: " + RuntimeInformation.OSArchitecture);
                sb.AppendLine("AppVersion: " + Assembly.GetExecutingAssembly().GetName().Version);
                foreach (string relativePath in requiredFiles)
                {
                    string path = Path.Combine(root, relativePath);
                    var file = new FileInfo(path);
                    sb.AppendLine($"{relativePath}: exists={file.Exists}, length={(file.Exists ? file.Length : 0)}");
                }

                WriteStartupText("Startup preflight", sb.ToString());
            }
            catch (Exception ex)
            {
                WriteStartupLog("Startup preflight failed", ex);
            }
        }

        internal static void WriteStartupText(string source, string text)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("==== " + DateTime.Now.ToString("O") + " ====");
                sb.AppendLine(source);
                sb.AppendLine(text);
                AppendStartupText(sb.ToString());
            }
            catch { }
        }

        internal static void AppendStartupText(string text)
        {
            var paths = new[]
            {
                Path.Combine(AppEnvironment.CurrentDataDirectory, "startup.log"),
                Path.Combine(AppContext.BaseDirectory, "startup.log")
            };

            foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    string? directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(directory))
                        Directory.CreateDirectory(directory);
                    File.AppendAllText(path, text);
                }
                catch { }
            }
        }

        private static void AppendExceptionDetails(StringBuilder sb, Exception ex, int depth)
        {
            string prefix = new(' ', depth * 2);
            sb.AppendLine($"{prefix}Type: {ex.GetType().FullName}");
            sb.AppendLine($"{prefix}HResult: 0x{ex.HResult:X8}");
            sb.AppendLine($"{prefix}Message: {ex.Message}");
            sb.AppendLine($"{prefix}Source: {ex.Source}");
            sb.AppendLine($"{prefix}StackTrace:");
            sb.AppendLine(ex.StackTrace ?? $"{prefix}<none>");

            foreach (PropertyInfo property in ex.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.GetIndexParameters().Length != 0)
                    continue;
                if (property.Name is nameof(Exception.Message) or nameof(Exception.StackTrace)
                    or nameof(Exception.InnerException) or nameof(Exception.Data)
                    or nameof(Exception.TargetSite) or nameof(Exception.Source)
                    or nameof(Exception.HResult))
                    continue;

                try
                {
                    object? value = property.GetValue(ex);
                    sb.AppendLine($"{prefix}{property.Name}: {value}");
                }
                catch { }
            }

            if (ex.InnerException != null)
            {
                sb.AppendLine($"{prefix}InnerException:");
                AppendExceptionDetails(sb, ex.InnerException, depth + 1);
            }
        }

        private static class NativeMethods
        {
            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            public static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
        }
    }
}
