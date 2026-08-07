using System;
using System.Collections.Generic;
using System.Globalization;

namespace STAGE
{
    public enum EqualizerPreset
    {
        Neutral,
        ClubBass,
        Edm,
        Rock,
        Pop,
        OrchestralScore,
        VocalBoost,
        ChillLofi
    }

    public sealed class EqualizerSettings
    {
        public static readonly string[] PresetNames =
        [
            "Neutral",
            "Club / Bass",
            "EDM",
            "Rock",
            "Pop",
            "Orchestral / Score",
            "Vocal Boost",
            "Chill / Lo-Fi"
        ];

        private static readonly float[][] PresetGains =
        [
            [0f, 0f, 0f, 0f, 0f],
            [4f, -1.5f, 0f, 0.5f, 1f],
            [3f, -1f, 0f, 1.5f, 1.5f],
            [2.5f, -1f, 0f, 2f, 2f],
            [2f, -1f, 1f, 2f, 2f],
            [-1f, 0f, 0.5f, 1.5f, 2f],
            [-1f, -1f, 3f, 2f, 1f],
            [0f, 2f, 0f, -1f, -2f]
        ];

        public static readonly float[] BandFrequencies = [60f, 230f, 910f, 3600f, 14000f];

        public float BassGain { get; set; }
        public float LowMidGain { get; set; }
        public float MidGain { get; set; }
        public float HighMidGain { get; set; }
        public float TrebleGain { get; set; }
        public float VolumeLevel { get; set; } = 1.5f;

        public void ApplyPreset(EqualizerPreset preset)
        {
            var gains = PresetGains[(int)preset];
            BassGain = gains[0];
            LowMidGain = gains[1];
            MidGain = gains[2];
            HighMidGain = gains[3];
            TrebleGain = gains[4];
        }

        public float[] GetGains() => [BassGain, LowMidGain, MidGain, HighMidGain, TrebleGain];

        public string ToFilterChain(bool normalizeVolume = false)
        {
            var filters = new List<string>
            {
                "asetpts=PTS-STARTPTS",
                "aresample=44100"
            };

            if (normalizeVolume)
                filters.Add("loudnorm=I=-16:LRA=11:TP=-1.0");

            float volumeFactor = UserValueToVolumeFactor(VolumeLevel);
            if (Math.Abs(volumeFactor - 1f) > 0.01f)
            {
                filters.Add(string.Create(CultureInfo.InvariantCulture, $"volume={volumeFactor}"));
            }

            var gains = GetGains();
            for (int i = 0; i < BandFrequencies.Length; i++)
            {
                if (Math.Abs(gains[i]) > 0.01f)
                    filters.Add(BuildBand(BandFrequencies[i], gains[i]));
            }

            // Boosted bands can exceed full scale, so the limiter must remain last.
            filters.Add("alimiter=limit=0.97");
            return string.Join(",", filters);
        }

        public static float UserValueToVolumeFactor(float userValue)
        {
            userValue = Math.Clamp(userValue, 1f, 5f);
            return 0.625f * userValue + 0.375f;
        }

        private static string BuildBand(float frequency, float gain)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"equalizer=f={frequency}:width_type=o:width=2:g={gain}");
        }
    }
}
