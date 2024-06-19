using System.Windows.Controls;

namespace SyncRoomChatToolV2.UserControls
{
    /// <summary>
    /// Chat.xaml の相互作用ロジック
    /// </summary>
    public partial class ChatControl : UserControl
    {
        public ChatControl()
        {
            InitializeComponent();
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                Tools.OpenUrl(e.Uri.AbsoluteUri);
                e.Handled = true;
            }
            catch
            {
                // 特に何もしない
            }
        }
    }
}
