using System;
using System.IO;
using System.Reflection;

namespace STAGE
{
    internal static class AppEnvironment
    {
        public const string ExecutableBaseName = "STAGE";
        public const string DisplayName = "S.T.A.G.E.";
        public const string LocalAppDataFolderName = "STAGE";
        public const string RegistrySubKey = @"SOFTWARE\ScdConverter";

        public static string CurrentDataDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            LocalAppDataFolderName,
            "current");

        public static string CrashLogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            LocalAppDataFolderName,
            "crash.log");

        public static string DisplayVersion
        {
            get
            {
                Version? version = Assembly.GetExecutingAssembly().GetName().Version;
                return version == null
                    ? DisplayName
                    : $"{DisplayName} {version.Major}.{version.Minor}.{version.Build}";
            }
        }
    }
}
