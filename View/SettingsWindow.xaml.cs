using Microsoft.Win32;
using System.Windows;
using SyncRoomChatToolV2.Properties;

namespace SyncRoomChatToolV2.View
{
    /// <summary>
    /// Settings.xaml の相互作用ロジック
    /// </summary>
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            Closing += SettingsWindow_Closing;
        }

        private void SettingsWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            Settings.Default.LinkWaveFilePath = LinkWaveFilePath.Text;
            Settings.Default.VoiceVoxPath = VoiceVoxPath.Text;
            Settings.Default.VoiceVoxAddress = VoiceVoxAddress.Text;
            Settings.Default.Save();
        }

        private void BtnReturn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OpenLinkWaveFilePath_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                InitialDirectory = System.Environment.CurrentDirectory,
                RestoreDirectory = true,
                Filter = "音声ファイル(*.wav)|*.wav|すべてのファイル(*.*)|*.*",
                FilterIndex = 1,
                Title = "リンクが貼られた時の固定音声を選択"
            };

            var result = ofd.ShowDialog();
            if (result == true)
            {
                LinkWaveFilePath.Text = ofd.FileName;
                Settings.Default.LinkWaveFilePath = LinkWaveFilePath.Text;
                Settings.Default.Save();
            }
        }

        private void OpenVoiceVoxPath_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                InitialDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs\\VOICEVOX"),
                RestoreDirectory = true,
                Filter = "実行ファイル(*.exe)|*.exe|すべてのファイル(*.*)|*.*",
                FilterIndex = 1,
                Title = "リンクが貼られた時の固定音声を選択"
            };

            var result = ofd.ShowDialog();
            if (result == true)
            {
                VoiceVoxPath.Text = ofd.FileName;
                Settings.Default.VoiceVoxPath = VoiceVoxPath.Text;
                Settings.Default.Save();
            }
        }
    }
}
