using SyncRoomChatToolV2.ModelView;
using SyncRoomChatToolV2.Properties;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Speech.Synthesis;
using System.Text.RegularExpressions;
using System.Windows.Controls;

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

            _ = GetChat();
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

            //外のループ。プロセス確認用。
            while (true) {
                TargetProcess targetProc = new ("SYNCROOM2");

                MainVM.Info.SysInfo = msg;
                MainVM.Info.ChatLog = "";

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
                        msg = "SyncRoomが立ち上がってません。RootElement Is Null.";
                        continue;
                    }

                    AutomationElement studio = rootElement.FindFirst(TreeScope.Element | TreeScope.Descendants,
                                                                     new PropertyCondition(AutomationElement.AutomationIdProperty, "studio"));

                    string oldMessage = "";

                    TreeWalker twName = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "name"));
                    TreeWalker twTime = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "time"));
                    TreeWalker twMessage = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "message"));
                    TreeWalker twPart = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "part"));
                    TreeWalker twRack = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "rack"));
                    TreeWalker twDivision = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "division"));

                    TreeWalker twControl = new(new PropertyCondition(AutomationElement.IsControlElementProperty, true));

                    MainVM.Members?.Clear();

                    if (studio is not null)
                    {
                        //メインのループ。チャット取得用。
                        while (true)
                        {

                            MainVM.Info.SysInfo = msg;
                            //MainVM.Members.Clear();

                            await Task.Delay((int)Properties.Settings.Default.waitTiming);

                            try
                            {
                                AutomationElement rack = twRack.GetFirstChild(studio);

                                if (rack is not null)
                                {
                                    AutomationElement roomOwner = twDivision.GetFirstChild(rack);
                                    var tempName = twName.GetFirstChild(roomOwner);
                                    var tempPart = twPart.GetFirstChild(roomOwner);
                                    var tempNameText = twControl.GetFirstChild(tempName);
                                    var tempPartText = twControl.GetFirstChild(tempPart);

                                    Member item = new();
                                    if (tempNameText.Current.Name != null)
                                    {
                                        item.MemberName = tempNameText.Current.Name;
                                    }
                                    if (tempPartText.Current.Name != null)
                                    {
                                        item.MemberPart = tempPartText.Current.Name;
                                    }
#nullable disable warnings
                                    MainVM.Members.Add(item);
#nullable restore
                                    var roomMember = twDivision.GetNextSibling(roomOwner);
                                    while (roomMember is not null)
                                    {
                                        tempName = twName.GetFirstChild(roomMember);
                                        tempPart = twPart.GetFirstChild(roomMember);
                                        if (tempName is null)
                                        {
                                            break;
                                        }
                                        tempNameText = twControl.GetFirstChild(tempName);
                                        tempPartText = twControl.GetFirstChild(tempPart);

                                        item = new();
                                        if (tempNameText.Current.Name != null)
                                        {
                                            item.MemberName = tempNameText.Current.Name;
                                        }
                                        if (tempPartText.Current.Name != null)
                                        {
                                            item.MemberPart = tempPartText.Current.Name;
                                        }
                                        MainVM.Members.Add(item);
                                        roomMember = twDivision.GetNextSibling(roomMember);
                                    }
                                }
                                else
                                {
                                    break;
                                }

                                //chatListのAutomationIdを持つ要素の下に、divisionってAutomationIdを持つ要素群＝チャットの各行っつうか名前と時間とチャット内容が入っとる。
                                AutomationElement chatList = studio.FindFirst(TreeScope.Element | TreeScope.Descendants,
                                                                                    new PropertyCondition(AutomationElement.AutomationIdProperty, "chatList"));

                                AutomationElement elName = twName.GetLastChild(chatList);
                                if (elName is null)
                                {
                                    msg = "チャット入力待ち";
                                    continue;
                                }

                                elName = twControl.GetLastChild(elName);

                                AutomationElement elTime = twTime.GetLastChild(chatList);
                                elTime = twControl.GetLastChild(elTime);

                                AutomationElement elMessage = twMessage.GetLastChild(chatList);
                                elMessage = twControl.GetLastChild(elMessage);

                                //string chatLine = $"{elName.Current.Name} {elTime.Current.Name} {elMessage.Current.Name}";
                                string chatLine = $"[{elTime.Current.Name}] {elMessage.Current.Name}";

                                if ((elMessage.Current.Name != oldMessage)&&(!string.IsNullOrEmpty(oldMessage)))
                                {
                                    MainVM.Info.ChatLog += System.Environment.NewLine + chatLine;

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

                                    if (!string.IsNullOrEmpty(newComment))
                                    {
                                        synth.Speak(newComment);
                                    }
                                }
                                oldMessage = elMessage.Current.Name;

                                msg = "監視中…";

                                MainVM.Info.SysInfo = msg;

                            }
                            catch (Exception e)
                            {
                                msg = $"多分チャットウィンドウが見えてません。{e.Message}";
                                continue;
                            }
                        }
                    }
                    else
                    {
                        msg = "入室してください。studio is null.";
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