using SyncRoomChatToolV2.Properties;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;

namespace SyncRoomChatToolV2
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        public MainWindow()
        {
            //前のバージョンのプロパティを引き継ぐぜ。
            Settings.Default.Upgrade();

            InitializeComponent();
            ContentRendered += MainWindow_ContentRendered;
            Closing += MainWindow_Closing;
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
            Left = Settings.Default.WindowLocation.X;
            Top = Settings.Default.WindowLocation.Y;
            Width = Settings.Default.WindowSize.Width;
            Height = Settings.Default.WindowSize.Height;

            //GridSplitterの位置復元
            var widthA = Settings.Default.GridRowWidthA;
            var widthB = Settings.Default.GridRowWidthB;

            SplitGrid.ColumnDefinitions[0].Width = new GridLength(widthA, GridUnitType.Star);
            SplitGrid.ColumnDefinitions[2].Width = new GridLength(widthB, GridUnitType.Star);
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void StartButton_Click(object sender, EventArgs e)
        {
            AutomationElement ?rootElement = null;
            Process[] procs = GetProcessesByWindowTitle("SYNCROOM");
            if (procs.Length == 0)
            {
                //SyncRoomが立ち上がってません。
                return;
            }

            foreach (Process proc in procs) { 
                //v1系が同時起動だった場合はどうなるか分からんのだが。
                if (proc.MainWindowTitle == "SYNCROOM") {
                    //MainWindotTitle が "SYNCROOM"なプロセス＝ターゲットのプロセスは、SYNCROOM2.exeが中で作った別プロセスのようで
                    //こんな面倒なやり方をしてみている。
                    rootElement = AutomationElement.FromHandle(proc.MainWindowHandle);
                    break; 
                }
            }

            //ToDo: 入室中かどうかのチェック方法を探さないといけない。ID=chatListが存在しないかな？未入室の場合。

            string oldMessage = "";
            while(rootElement != null)
            {
                AutomationElement chatList = rootElement.FindFirst(TreeScope.Element | TreeScope.Descendants,
                                                                 new PropertyCondition(AutomationElement.AutomationIdProperty, "chatList"));

                //chatListのAutomationIdを持つ要素の下に、divisionってAutomationIdを持つ要素群＝チャットの各行っつうか名前と時間とチャット内容が入っとる。
                AutomationElementCollection messages = chatList.FindAll(TreeScope.Element, new PropertyCondition(AutomationElement.AutomationIdProperty, "division"));

                //TreeWalker遅いのかなぁ。凄ぇ重い。
                TreeWalker twName = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "name"));
                TreeWalker twTime = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "time"));
                TreeWalker twMessage = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "message"));

                AutomationElement elName = twName.GetLastChild(chatList);
                TreeWalker tw2 = new(new PropertyCondition(AutomationElement.IsControlElementProperty, true));
                elName = tw2.GetLastChild(elName);

                AutomationElement elTime = twTime.GetLastChild(chatList);
                tw2 = new(new PropertyCondition(AutomationElement.IsControlElementProperty, true));
                elTime = tw2.GetLastChild(elTime);

                AutomationElement elMessage = twMessage.GetLastChild(chatList);
                tw2 = new(new PropertyCondition(AutomationElement.IsControlElementProperty, true));
                elMessage = tw2.GetLastChild(elMessage);

                Task.Delay(100).Wait();
                var timeStamp = DateTime.Now;
                string chatLine = $"{elName.Current.Name} {elTime.Current.Name} {elMessage.Current.Name}";

                if (chatLine != oldMessage) 
                {
                    Debug.WriteLine($"{chatLine} {timeStamp}");
                    oldMessage = chatLine;
                }
            };
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {

        }

        /// <summary>
        /// 指定された文字列を含むウィンドウタイトルを持つプロセスを取得します。
        /// </summary>
        /// <param name="windowTitle">ウィンドウタイトルに含む文字列。</param>
        /// <returns>該当するプロセスの配列。</returns>
        public static System.Diagnostics.Process[] GetProcessesByWindowTitle(string windowTitle)
        {
            System.Collections.ArrayList list = new System.Collections.ArrayList();

            //すべてのプロセスを列挙する
            foreach (System.Diagnostics.Process p
                in System.Diagnostics.Process.GetProcesses())
            {
                //指定された文字列がメインウィンドウのタイトルに含まれているか調べる
                if (0 <= p.MainWindowTitle.IndexOf(windowTitle))
                {
                    //含まれていたら、コレクションに追加
                    list.Add(p);
                }
            }

            //コレクションを配列にして返す
            return (System.Diagnostics.Process[])
                list.ToArray(typeof(System.Diagnostics.Process));
        }
    }
}