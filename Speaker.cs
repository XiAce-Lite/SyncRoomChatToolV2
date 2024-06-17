using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SyncRoomChatToolV2
{
    internal class Speaker
    {
        public int StyleId { get; set; }
        public string? UserName { get; set; }
        public bool ChimeFlg { get; set; }
        public bool SpeechFlg { get; set; }
        public double SpeedScale { get; set; } = 1;
    }
}
