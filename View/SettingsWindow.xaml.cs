using Microsoft.Win32;
using SyncRoomChatToolV2.Ai;
using SyncRoomChatToolV2.Properties;
using System.Diagnostics;
using System.Windows;

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
            this.Topmost = true;
        }

        private void SettingsWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            Settings.Default.LinkWaveFilePath = LinkWaveFilePath.Text;
            Settings.Default.VoiceVoxPath = VoiceVoxPath.Text;
            Settings.Default.VoiceVoxAddress = VoiceVoxAddress.Text;
            Settings.Default.GeminiApiKey = GeminiApiKey.Text;
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

        private void OpenAiPromptFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                new AiPromptStore().EnsureUserFiles();
                Process.Start(new ProcessStartInfo
                {
                    FileName = AppContext.BaseDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "フォルダを開けません", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
