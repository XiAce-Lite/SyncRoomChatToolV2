using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace SyncRoomChatToolV2.ModelView
{
    public class Member : INotifyPropertyChanged
    {
        private ObservableCollection<Member>? _Children;
        private string _Name = "";
        private string _Part = "";
        private Image? _Image = null;

        public string MemberName
        {
            get { return _Name; }
            set { _Name = value; OnPropertyChanged(nameof(MemberName)); }
        }

        public string MemberPart
        {
            get { return _Part; }
            set { _Part = value; OnPropertyChanged(nameof(MemberPart)); }
        }

        public Image? MemberImage
        {
            get { return _Image; }
            set { _Image = value; OnPropertyChanged(nameof(MemberImage)); }
        }

        public ObservableCollection<Member>? Children
        {
            get { return _Children; }
            set { _Children = value; OnPropertyChanged(nameof(Children)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public void Add(Member child)
        {
            if (null == Children) Children = [];
            Children.Add(child);
        }

        public void Remove(Member child) {
            Children?.Remove(child);
        }
    }
}
