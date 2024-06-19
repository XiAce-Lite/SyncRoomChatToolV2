using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SyncRoomChatToolV2.ModelView
{
    public class Chat : INotifyPropertyChanged
    {
        private ObservableCollection<Chat>? _Children;
        private string _UserName = "";
        private string _Time = "";
        private string _Message = "";
        private string _Link = "";
        private bool _IsYourSelf = false;

        public string UserName
        {
            get { return _UserName; }
            set { _UserName = value; OnPropertyChanged(nameof(UserName)); }
        }

        public string ChatTime
        {
            get { return _Time; }
            set { _Time = value; OnPropertyChanged(nameof(ChatTime)); }
        }

        public string Message
        {
            get { return _Message; }
            set { _Message = value; OnPropertyChanged(nameof(Message)); }
        }

        public string Link
        {
            get { return _Link; }
            set { _Link = value; OnPropertyChanged(nameof(Link)); }
        }

        public bool IsYourSelf
        {
            get { return _IsYourSelf; }
            set { _IsYourSelf = value; OnPropertyChanged(nameof(IsYourSelf)); }
        }

        public bool IsNotYourSelf
        {
            get { return !_IsYourSelf; }
        }

        public ObservableCollection<Chat>? Children
        {
            get { return _Children; }
            set { _Children = value; OnPropertyChanged(nameof(Children)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public void Add(Chat child)
        {
            if (null == Children) Children = [];
            Children.Add(child);
        }

        public void Remove(Chat child)
        {
            Children?.Remove(child);
        }
    }
}
