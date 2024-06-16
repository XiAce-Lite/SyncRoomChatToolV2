
using System.Collections.ObjectModel;

namespace SyncRoomChatToolV2.ModelView
{
    public class MainWindowViewModel
    {
        public Info Info { get; set; }
        public ObservableCollection<Member> Members { get; set; }

        public MainWindowViewModel() { 
            Info = new Info();
            Members = [];
        }
    }
}
