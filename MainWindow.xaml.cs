using SyncRoomChatToolV2.ModelView;
using SyncRoomChatToolV2.Properties;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Speech.Synthesis;
using System.Text.RegularExpressions;

namespace SyncRoomChatToolV2
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel MainVM = new();
        [GeneratedRegex("https?://")]
        private static partial Regex httpReg();
        private static string LastURL = "";

        public MainWindow()
        {
            //前のバージョンのプロパティを引き継ぐぜ。
            Settings.Default.Upgrade();

            InitializeComponent();

            ContentRendered += MainWindow_ContentRendered;
            Closing += MainWindow_Closing;

#nullable disable warnings
            MainVM.Info.SysInfo = "起動中…";
            MainVM.Info.ChatLog = "チャットログが出る予定";
#nullable restore
            DataContext = MainVM;
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            //Windowロケーションとサイズの保管
            Settings.Default.WindowLocation = new System.Drawing.Point((int)Left, (int)Top);
            Settings.Default.WindowSize = new System.Drawing.Size((int)Width, (int)Height);

            //GridSplitterの位置保管
            var widthes = SplitGrid.ColumnDefinitions.Select(p => p.ActualWidth).ToArray();

            Settings.Default.GridRowWidthA = widthes[0];
            Settings.Default.GridRowWidthB = widthes[2];

            Settings.Default.Save();
        }

        private void MainWindow_ContentRendered(object? sender, EventArgs e)
        {

            //Windowロケーションとサイズの復元
            if (Settings.Default.WindowLocation.X > SystemParameters.WorkArea.Width)
            {
                Left = 10;
            }
            else
            {
                Left = Settings.Default.WindowLocation.X;
            }

            if (Settings.Default.WindowLocation.Y > SystemParameters.WorkArea.Height)
            {
                Top = 10; 
            }
            else
            {
                Top = Settings.Default.WindowLocation.Y;
            }

            Width = Settings.Default.WindowSize.Width;
            Height = Settings.Default.WindowSize.Height;

            //GridSplitterの位置復元
            var widthA = Settings.Default.GridRowWidthA;
            var widthB = Settings.Default.GridRowWidthB;

            SplitGrid.ColumnDefinitions[0].Width = new GridLength(widthA, GridUnitType.Star);
            SplitGrid.ColumnDefinitions[2].Width = new GridLength(widthB, GridUnitType.Star);
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            _ = GetChat();
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        async Task GetChat()
        {
            string msg = "";

            SpeechSynthesizer synth = new ()
            {
                Rate = -1
            };
            synth.SelectVoice("Microsoft Haruka Desktop");

            while (true) {
                TargetProcess targetProc = new ("SYNCROOM2");
#nullable disable warnings
                MainVM.Info.SysInfo = msg;
                MainVM.Info.ChatLog = "";
#nullable restore
                await Task.Delay(1000);

                if (targetProc.IsAlive)
                {
                    msg = "SyncRoomが起動されています。";

                    AutomationElement? rootElement = null;
                    Process[] procs = Tools.GetProcessesByWindowTitle("SYNCROOM");
                    if (procs.Length == 0)
                    {
                        msg = "SyncRoomが立ち上がってません。No 'SYNCROOM' Title Window.";
                        continue;
                    }

                    foreach (Process proc in procs)
                    {
                        if (proc.MainWindowTitle == "SYNCROOM")
                        {
                            //MainWindotTitle が "SYNCROOM"なプロセス＝ターゲットのプロセスは、SYNCROOM2.exeが中で作った別プロセスのようで
                            //こんな面倒なやり方をしてみている。
                            rootElement = AutomationElement.FromHandle(proc.MainWindowHandle);
                            break;
                        }
                    }

                    if (rootElement is null)
                    {
                        msg = "SyncRoomが立ち上がってません。Element Is Null.";
                        continue;
                    }

                    string oldMessage = "";

                    //TreeWalker遅いのかなぁ。凄ぇ重い。
                    TreeWalker twName = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "name"));
                    TreeWalker twTime = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "time"));
                    TreeWalker twMessage = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "message"));
                    TreeWalker tw2 = new(new PropertyCondition(AutomationElement.IsControlElementProperty, true));

                    if (rootElement is not null)
                    {
                        while (true)
                        {
#nullable disable warnings
                            MainVM.Info.SysInfo = msg;
#nullable restore
                            await Task.Delay((int)Properties.Settings.Default.waitTiming);

                            try
                            {
                                //chatListのAutomationIdを持つ要素の下に、divisionってAutomationIdを持つ要素群＝チャットの各行っつうか名前と時間とチャット内容が入っとる。
                                AutomationElement chatList = rootElement.FindFirst(TreeScope.Element | TreeScope.Descendants,
                                                                                    new PropertyCondition(AutomationElement.AutomationIdProperty, "chatList"));

                                AutomationElement elName = twName.GetLastChild(chatList);
                                if (elName is null)
                                {
                                    msg = "未入室あるいは未入力";
                                    break;
                                }

                                elName = tw2.GetLastChild(elName);

                                AutomationElement elTime = twTime.GetLastChild(chatList);
                                elTime = tw2.GetLastChild(elTime);

                                AutomationElement elMessage = twMessage.GetLastChild(chatList);
                                elMessage = tw2.GetLastChild(elMessage);

                                //string chatLine = $"{elName.Current.Name} {elTime.Current.Name} {elMessage.Current.Name}";
                                string chatLine = $"[{elTime.Current.Name}] {elMessage.Current.Name}";

                                if ((elMessage.Current.Name != oldMessage)&&(!string.IsNullOrEmpty(oldMessage)))
                                {
                                    //Debug.WriteLine($"{chatLine}");
                                    string newComment = elMessage.Current.Name;
                                    Match match;
                                    match = httpReg().Match(newComment);
                                    if (match.Success)
                                    {
                                        string UriString = newComment[match.Index..];
                                        Uri u = new(UriString);

                                        if (Properties.Settings.Default.OpenLink)
                                        {
                                            if (UriString != LastURL)
                                            {
                                                if (u.IsAbsoluteUri)
                                                {
                                                    Tools.OpenUrl(UriString);
                                                    newComment = "リンクが張られました";
                                                }
                                            }
                                            else
                                            {
                                                newComment = "";
                                            }
                                        }
                                        LastURL = UriString;
                                    }

                                    MainVM.Info.ChatLog += System.Environment.NewLine + chatLine;

                                    if (!string.IsNullOrEmpty(newComment))
                                    {
                                        synth.Speak(newComment);
                                    }
                                }
                                oldMessage = elMessage.Current.Name;

                                msg = "監視中…";
#nullable disable warnings
                                MainVM.Info.SysInfo = msg;
#nullable restore
                            }
                            catch (Exception)
                            {
                                msg = "入室してください。";
                                continue;
                            }
                        }
                    }
                    else
                    {
                        msg = "入室してください。";
                    }
                }
                else
                {
                    msg = $"SyncRoomが立ち上がってません。No Process. {DateTime.Now}";
                }
            }
        }
    }
}