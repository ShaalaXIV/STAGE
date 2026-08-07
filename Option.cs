using System;
using System.Collections.Generic;

namespace STAGE
{
    public class Option
    {
        public Option()
        {
            Files = new Dictionary<string, string>();
            FileSwaps = new Dictionary<string, string>();
            Manipulations = new List<string>();
        }

        public string Name { get; set; } = "";
        public string Id { get; set; } = "";
        public int? Priority { get; set; }
        public string Description { get; set; } = "";
        public Dictionary<string, string> Files { get; set; }
        public Dictionary<string, string> FileSwaps { get; set; }
        public List<string> Manipulations { get; set; }

        public bool ShouldSerializeId() => !string.IsNullOrWhiteSpace(Id);
        public bool ShouldSerializePriority() => Priority.HasValue;
        public bool ShouldSerializeFiles() => Files != null && Files.Count > 0;
        public bool ShouldSerializeFileSwaps() => FileSwaps != null && FileSwaps.Count > 0;
        public bool ShouldSerializeManipulations() => Manipulations != null && Manipulations.Count > 0;
    }
}
