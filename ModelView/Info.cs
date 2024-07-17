using System.ComponentModel;

namespace SyncRoomChatToolV2.ModelView
{
    public class Info : INotifyPropertyChanged
    {
        private string sysInfo = "";
        //private string chatLog = "";
        private bool isEntered = false;

        public string SysInfo
        {
            get => sysInfo;
            set { sysInfo = value; OnPropertyChanged(nameof(SysInfo)); }
        }
        /*
        public string ChatLog
        {
            get => chatLog;
            set { chatLog = value; OnPropertyChanged(nameof(ChatLog)); }
        }
        */

        public bool IsEntered { 
            get => isEntered;
            set { isEntered = value; OnPropertyChanged(nameof(IsEntered)); } 
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
