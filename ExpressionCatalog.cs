using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace STAGE
{
    public sealed class ExpressionCatalogDocument
    {
        [JsonProperty("source")]
        public string Source { get; set; } = "";

        [JsonProperty("sourceUrl")]
        public string SourceUrl { get; set; } = "";

        [JsonProperty("expressions")]
        public List<ExpressionCatalogItem> Expressions { get; set; } = new();
    }

    public sealed class ExpressionCatalogItem
    {
        [JsonProperty("key")]
        public string Key { get; set; } = "";

        [JsonProperty("displayName")]
        public string DisplayName { get; set; } = "";

        [JsonProperty("papName")]
        public string PapName { get; set; } = "";

        [JsonProperty("previews")]
        public List<string> Previews { get; set; } = new();
    }

    public static class ExpressionCatalog
    {
        public static string DirectoryPath =>
            Path.Combine(AppContext.BaseDirectory, "Assets", "Expressions");

        public static ExpressionCatalogDocument Load()
        {
            string path = Path.Combine(DirectoryPath, "catalog.json");
            if (!File.Exists(path))
                throw new FileNotFoundException("The facial expression catalog is missing.", path);

            return JsonConvert.DeserializeObject<ExpressionCatalogDocument>(File.ReadAllText(path))
                   ?? throw new InvalidDataException("The facial expression catalog could not be read.");
        }
    }
}
