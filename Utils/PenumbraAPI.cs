using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace STAGE.Utils
{
    internal static class PenumbraApi
    {
        private static readonly HttpClient s_client = new()
        {
            BaseAddress = new Uri("http://localhost:42069/"),
            Timeout = TimeSpan.FromSeconds(10),
        };
        private static readonly object s_reloadLock = new();
        private static CancellationTokenSource? s_pendingReload;

        public static string GetPenumbraDirectory()
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "XIVLauncher",
                "pluginConfigs",
                "Penumbra.json");
            if (!File.Exists(path))
                return "";

            try
            {
                var obj = JObject.Parse(File.ReadAllText(path));
                return (string)obj["ModDirectory"];
            }
            catch
            {
                return "";
            }
        }

        public static async Task<bool> ReloadMod(string path, string name = null)
        {
            Dictionary<string, string> args = new();

            if (name != null)
                args.Add("Name", name);
            if (path != null)
                args.Add("Path", path);

            return await Request("/reloadmod", args);
        }

        public static void ScheduleReloadMod(string path, string name = null)
        {
            CancellationToken token;
            lock (s_reloadLock)
            {
                s_pendingReload?.Cancel();
                s_pendingReload?.Dispose();
                s_pendingReload = new CancellationTokenSource();
                token = s_pendingReload.Token;
            }

            _ = ReloadAfterWritesSettleAsync(path, name, token);
        }

        public static void ScheduleReloadModFolder(string modDirectory)
        {
            if (string.IsNullOrWhiteSpace(modDirectory))
                return;

            string modName = Path.GetFileName(modDirectory);
            if (PenumbraMeta.TryLoad(modDirectory, out var meta) &&
                !string.IsNullOrWhiteSpace(meta!.Name))
            {
                modName = meta.Name;
            }

            ScheduleReloadMod(modDirectory, modName);
        }

        private static async Task ReloadAfterWritesSettleAsync(
            string path, string name, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(350, cancellationToken);
                bool reloaded = await ReloadMod(path, name);
                if (!reloaded)
                {
                    App.WriteStartupText(
                        "Penumbra reload failed",
                        $"Path: {path}{Environment.NewLine}Name: {name}");
                }
            }
            catch (OperationCanceledException)
            {
                // A newer completed write superseded this reload request.
            }
        }

        private static async Task<bool> Request(string urlPath, object data = null)
        {
            data ??= new object();
            try
            {
                using StringContent jsonContent = new(
                    JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
                string route = "api/" + urlPath.TrimStart('/');
                using HttpResponseMessage response = await s_client.PostAsync(route, jsonContent);
                string responseBody = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    App.WriteStartupText(
                        "Penumbra API rejected request",
                        $"Route: {route}{Environment.NewLine}" +
                        $"Status: {(int)response.StatusCode} {response.ReasonPhrase}{Environment.NewLine}" +
                        $"Request: {JsonConvert.SerializeObject(data)}{Environment.NewLine}" +
                        $"Response: {responseBody}");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                App.WriteStartupLog("Penumbra API request failed", ex);
                return false;
            }
        }
    }
}
