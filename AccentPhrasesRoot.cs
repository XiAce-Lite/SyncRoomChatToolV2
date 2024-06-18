using Newtonsoft.Json;

namespace SyncRoomChatToolV2
{
    internal class AccentPhrasesRoot
    {
        [JsonProperty("accent_phrases")]
        public List<AccentPhrases> AccentPhrases { get; set; } = new List<AccentPhrases> { };
        [JsonProperty("speedScale")]
        public double SpeedScale { get; set; } = 0;
        [JsonProperty("pitchScale")]
        public double PitchScale { get; set; } = 0;
        [JsonProperty("intonationScale")]
        public double IntonationScale { get; set; } = 0;
        [JsonProperty("volumeScale")]
        public double VolumeScale { get; set; } = 0;
        [JsonProperty("prePhonemeLength")]
        public double PrePhonemeLength { get; set; } = 0;
        [JsonProperty("postPhonemeLength")]
        public double PostPhonemeLength { get; set; } = 0;
        [JsonProperty("outputSamplingRate")]
        public double OutputSamplingRate { get; set; } = 0;
        [JsonProperty("outputStereo")]
        public bool OutputStereo { get; set; } = true;
        [JsonProperty("kana")]
        public string Kana { get; set; } = "string";
    }
}
