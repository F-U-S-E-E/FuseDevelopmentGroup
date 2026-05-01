using System;
using System.Collections.Generic;

namespace RAIL.Data
{
    public sealed class RailAudioRoot
    {
        public Dictionary<string, RailWhistleAudio> Whistles { get; set; } = new Dictionary<string, RailWhistleAudio>();
        public Dictionary<string, RailHornAudio> Horns { get; set; } = new Dictionary<string, RailHornAudio>();
        public Dictionary<string, RailBellAudio> Bells { get; set; } = new Dictionary<string, RailBellAudio>();
    }

    public sealed class RailWhistleAudio
    {
        public string Name { get; set; }
        public string Clip { get; set; }
        public RailAudioAssetReference Model { get; set; }
        public float? RampUpPitch { get; set; }
        public float? LerpSpeed { get; set; }
        public float? AirLerpSpeed { get; set; }
    }

    public sealed class RailHornAudio
    {
        public string Name { get; set; }
        public RailHornLayer[] Layers { get; set; } = Array.Empty<RailHornLayer>();
    }

    public sealed class RailHornLayer
    {
        public string File { get; set; }
        public RailAudioKeyframe[] Keyframes { get; set; } = Array.Empty<RailAudioKeyframe>();
    }

    public sealed class RailBellAudio
    {
        public string Name { get; set; }
        public string File { get; set; }
        public float[] IndexTimes { get; set; } = Array.Empty<float>();
    }

    public sealed class RailAudioAssetReference
    {
        public string AssetPackIdentifier { get; set; }
        public string AssetIdentifier { get; set; }
    }

    public sealed class RailAudioKeyframe
    {
        public float T { get; set; }
        public float Value { get; set; }
    }
}
