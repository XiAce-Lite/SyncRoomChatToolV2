using Newtonsoft.Json;

namespace SyncRoomChatToolV2
{
    //audio_queryした後に帰ってくるレスポンスを編集するためのクラス群。
    internal class AccentPhrases
    {
        [JsonProperty("moras")]
        public List<Mora> Moras { get; set; } = new List<Mora>();
        [JsonProperty("accent")]
        public double Accent { get; set; } = 0;
        [JsonProperty("pause_mora")]
        public PauseMora PauseMora { get; set; } = new PauseMora();
        [JsonProperty("is_interrogative")]
        public bool IsInterrogative { get; set; } = false;
    }
}
