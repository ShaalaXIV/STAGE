using System.Collections.ObjectModel;
using System.ComponentModel;

namespace STAGE
{
    /// <summary>
    /// View model bound to TreeView via ItemsSource.
    /// </summary>
    public sealed class PlaylistNodeContent : INotifyPropertyChanged
    {
        private string _displayText = "";
        private bool _isExpanded;

        public string Name { get; set; } = "";

        // Relative SCD path used as a stable key for song-level operations.
        // Empty for non-song nodes or virtual entries without an SCD file.
        public string SongScdPath { get; set; } = "";

        // Optional source JSON path for non-playlist views, such as VFX/PAP groups.
        public string SourceJsonPath { get; set; } = "";

        public string AssetGamePath { get; set; } = "";

        public string AssetLocalPath { get; set; } = "";

        // Optional descriptive kind for non-playlist views.
        public string AssetKind { get; set; } = "";

        public string DisplayText
        {
            get => _displayText;
            set
            {
                _displayText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayText)));
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }

        // Playlist mode: 0 = root, 1 = playlist, 2 = song.
        // VFX/PAP mode: 0 = root, 1 = category, 2 = group, 3 = option, 4 = file mapping.
        public int Level { get; set; }

        // Segoe Fluent Icons glyph code
        public string IconGlyph { get; set; } = "";

        public PlaylistNodeContent? Parent { get; private set; }
        public ObservableCollection<PlaylistNodeContent> Children { get; } = new();

        public void AddChild(PlaylistNodeContent child)
        {
            child.Parent = this;
            Children.Add(child);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public static string PlaylistGlyph => "\uE142";  // FolderOpen
        public static string SongGlyph => "\uE8D6";      // MusicNote2
        public static string RootGlyph => "\uE8B7";      // Library
        public static string VfxGlyph => "\uE790";       // Color
        public static string PapGlyph => "\uE7C3";       // Character
        public static string OptionGlyph => "\uE8EF";    // CheckList

        public override string ToString() => DisplayText;
    }
}
