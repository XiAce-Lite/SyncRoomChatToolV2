using Newtonsoft.Json;

namespace SyncRoomChatToolV2
{
    internal class Mora
    {
        [JsonProperty("text")]
        public string Text { get; set; } = "string";
        [JsonProperty("consonant")]
        public string Consonant { get; set; } = "string";
        [JsonProperty("consonant_length")]
        public string? ConsonantLength { get; set; }
        [JsonProperty("vowel")]
        public string Vowel { get; set; } = "string";
        [JsonProperty("vowel_length")]
        public double VowelLength { get; set; } = 0;
        [JsonProperty("pitch")]
        public double Pitch { get; set; } = 0;
    }
}
