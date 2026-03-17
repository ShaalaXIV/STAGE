using VfxEditor.ScdFormat;

namespace Pickles_Playlist_Editor
{
    public sealed class ScdParameterSettings
    {
        public float SoundVolume { get; set; }
        public int BusDuckingFadeTime { get; set; }
        public float BusDuckingVolume { get; set; }
        public int BusDuckingNumber { get; set; }
        public int AttributeVersion { get; set; }
        public int AttributeConditionFirst { get; set; }
        public bool LoopEnabled { get; set; }
        public bool TrackLoopEnabled { get; set; }
        public float LayoutMinRange { get; set; }
        public float LayoutMaxRange { get; set; }
        public float LayoutHeightLow { get; set; }
        public float LayoutHeightHigh { get; set; }
        public float LayoutRangeVolume { get; set; }
        public bool LayoutParametersAvailable { get; set; }

        public static ScdParameterSettings FromScd(ScdFile scd)
        {
            var settings = new ScdParameterSettings
            {
                SoundVolume = scd.Sounds.Count > 0 ? scd.Sounds[0].Volume.Value : 1f,
                BusDuckingFadeTime = scd.Sounds.Count > 0 ? scd.Sounds[0].BusDucking.FadeTime.Value : 1200,
                BusDuckingVolume = scd.Sounds.Count > 0 ? scd.Sounds[0].BusDucking.Volume.Value : 0f,
                BusDuckingNumber = scd.Sounds.Count > 0 ? scd.Sounds[0].BusDucking.Number.Value : 1,
                AttributeVersion = scd.Attributes.Count > 0 ? scd.Attributes[0].Version.Value : 1,
                AttributeConditionFirst = scd.Attributes.Count > 0 ? scd.Attributes[0].ConditionFirst.Value : 0,
                LoopEnabled = scd.Sounds.Count > 0 && scd.Sounds[0].Attributes.Value.HasFlag(VfxEditor.ScdFormat.SoundAttribute.Loop),
                TrackLoopEnabled = scd.Tracks.Count > 0 && scd.Tracks[0].Items.Count > 0 && scd.Tracks[0].Items[^1].Type.Value == TrackCmd.EndForLoop,
            };

            settings.ReadLayoutValues(scd);
            return settings;
        }

        public void ApplyTo(ScdFile scd)
        {
            if (scd.Sounds.Count > 0)
            {
                scd.Sounds[0].Volume.Value = SoundVolume;
                scd.Sounds[0].BusDucking.FadeTime.Value = BusDuckingFadeTime;
                scd.Sounds[0].BusDucking.Volume.Value = BusDuckingVolume;
                scd.Sounds[0].BusDucking.Number.Value = (byte)System.Math.Clamp(BusDuckingNumber, byte.MinValue, byte.MaxValue);

                if (LoopEnabled)
                    scd.Sounds[0].Attributes.Value |= VfxEditor.ScdFormat.SoundAttribute.Loop;
                else
                    scd.Sounds[0].Attributes.Value &= ~VfxEditor.ScdFormat.SoundAttribute.Loop;
            }

            if (scd.Attributes.Count > 0)
            {
                scd.Attributes[0].Version.Value = (byte)System.Math.Clamp(AttributeVersion, byte.MinValue, byte.MaxValue);
                scd.Attributes[0].ConditionFirst.Value = (byte)System.Math.Clamp(AttributeConditionFirst, byte.MinValue, byte.MaxValue);
            }

            if (scd.Tracks.Count > 0)
            {
                foreach (var track in scd.Tracks)
                {
                    if (track.Items.Count == 0) continue;
                    var endItem = track.Items[^1];
                    if (endItem.Type.Value != TrackCmd.End && endItem.Type.Value != TrackCmd.EndForLoop) continue;
                    endItem.Type.Value = TrackLoopEnabled ? TrackCmd.EndForLoop : TrackCmd.End;
                }
            }

            ApplyLayoutValues(scd);
        }

        private void ReadLayoutValues(ScdFile scd)
        {
            if (scd.Sounds.Count == 0)
            {
                LayoutParametersAvailable = false;
                return;
            }

            var data = scd.Sounds[0].Layout.GetData();
            if (TryReadLayoutData(data, out var min, out var max, out var lowHeight, out var highHeight, out var rangeVolume))
            {
                LayoutMinRange = min;
                LayoutMaxRange = max;
                LayoutHeightLow = lowHeight;
                LayoutHeightHigh = highHeight;
                LayoutRangeVolume = rangeVolume;
                LayoutParametersAvailable = true;
            }
            else
            {
                LayoutParametersAvailable = false;
            }
        }

        private void ApplyLayoutValues(ScdFile scd)
        {
            if (!LayoutParametersAvailable || scd.Sounds.Count == 0) return;

            var data = scd.Sounds[0].Layout.GetData();
            switch (data)
            {
                case LayoutPointData layout:
                    SetLayoutData(layout.MaxRange, layout.MinRange, layout.Height, layout.RangeVolume);
                    break;
                case LayoutPointDirData layout:
                    SetLayoutData(layout.MaxRange, layout.MinRange, layout.Height, layout.RangeVolume);
                    break;
                case LayoutLineData layout:
                    SetLayoutData(layout.MaxRange, layout.MinRange, layout.Height, layout.RangeVolume);
                    break;
                case LayoutPolylineData layout:
                    SetLayoutData(layout.MaxRange, layout.MinRange, layout.Height, layout.RangeVolume);
                    break;
                case LayoutPolygonData layout:
                    SetLayoutData(layout.MaxRange, layout.MinRange, layout.Height, layout.RangeVolume);
                    break;
                case LayoutLineExtControllerData layout:
                    SetLayoutData(layout.MaxRange, layout.MinRange, layout.Height, layout.RangeVolume);
                    break;
                case LayoutSurfaceData layout:
                    SetLayoutData(layout.MaxRange, layout.MinRange, layout.Height, layout.RangeVolume);
                    break;
            }
        }

        private void SetLayoutData(VfxEditor.Parsing.ParsedFloat maxRange, VfxEditor.Parsing.ParsedFloat minRange, VfxEditor.Parsing.ParsedFloat2 height, VfxEditor.Parsing.ParsedFloat rangeVolume)
        {
            minRange.Value = LayoutMinRange;
            maxRange.Value = LayoutMaxRange;
            height.Value = new System.Numerics.Vector2(LayoutHeightLow, LayoutHeightHigh);
            rangeVolume.Value = LayoutRangeVolume;
        }

        private static bool TryReadLayoutData(ScdLayoutData? data, out float min, out float max, out float heightLow, out float heightHigh, out float rangeVolume)
        {
            min = 0f;
            max = 0f;
            heightLow = 0f;
            heightHigh = 0f;
            rangeVolume = 0f;

            switch (data)
            {
                case LayoutPointData layout:
                    min = layout.MinRange.Value;
                    max = layout.MaxRange.Value;
                    heightLow = layout.Height.Value.X;
                    heightHigh = layout.Height.Value.Y;
                    rangeVolume = layout.RangeVolume.Value;
                    return true;
                case LayoutPointDirData layout:
                    min = layout.MinRange.Value;
                    max = layout.MaxRange.Value;
                    heightLow = layout.Height.Value.X;
                    heightHigh = layout.Height.Value.Y;
                    rangeVolume = layout.RangeVolume.Value;
                    return true;
                case LayoutLineData layout:
                    min = layout.MinRange.Value;
                    max = layout.MaxRange.Value;
                    heightLow = layout.Height.Value.X;
                    heightHigh = layout.Height.Value.Y;
                    rangeVolume = layout.RangeVolume.Value;
                    return true;
                case LayoutPolylineData layout:
                    min = layout.MinRange.Value;
                    max = layout.MaxRange.Value;
                    heightLow = layout.Height.Value.X;
                    heightHigh = layout.Height.Value.Y;
                    rangeVolume = layout.RangeVolume.Value;
                    return true;
                case LayoutPolygonData layout:
                    min = layout.MinRange.Value;
                    max = layout.MaxRange.Value;
                    heightLow = layout.Height.Value.X;
                    heightHigh = layout.Height.Value.Y;
                    rangeVolume = layout.RangeVolume.Value;
                    return true;
                case LayoutLineExtControllerData layout:
                    min = layout.MinRange.Value;
                    max = layout.MaxRange.Value;
                    heightLow = layout.Height.Value.X;
                    heightHigh = layout.Height.Value.Y;
                    rangeVolume = layout.RangeVolume.Value;
                    return true;
                case LayoutSurfaceData layout:
                    min = layout.MinRange.Value;
                    max = layout.MaxRange.Value;
                    heightLow = layout.Height.Value.X;
                    heightHigh = layout.Height.Value.Y;
                    rangeVolume = layout.RangeVolume.Value;
                    return true;
                default:
                    return false;
            }
        }
    }
}
