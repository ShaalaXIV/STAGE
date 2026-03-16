using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;

namespace Pickles_Playlist_Editor
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
                MainWindow = new MainWindow();
                MainWindow.Activate();
            }
            catch (Exception ex)
            {
                WriteStartupLog("OnLaunched", ex);
                NativeMethods.MessageBoxW(IntPtr.Zero,
                    "Startup failure. See startup.log in %LOCALAPPDATA%\\PicklesPlaylistEditor\\current", 
                    "Pickles Playlist Editor", 0x00000010);
                throw;
            }
        }

        private static void WriteStartupLog(string source, Exception? ex)
        {
            try
            {
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PicklesPlaylistEditor", "current");
                Directory.CreateDirectory(root);
                string path = Path.Combine(root, "startup.log");
                var sb = new StringBuilder();
                sb.AppendLine("==== " + DateTime.Now.ToString("O") + " ====");
                sb.AppendLine(source);
                if (ex != null)
                {
                    sb.AppendLine(ex.ToString());
                }
                File.AppendAllText(path, sb.ToString());
            }
            catch { }
        }

        private static class NativeMethods
        {
            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            public static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
        }
    }
}
