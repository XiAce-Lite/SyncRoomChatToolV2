using Microsoft.Toolkit.Uwp.Notifications;
using NAudio.Wave;
using Newtonsoft.Json;
using SyncRoomChatToolV2.ModelView;
using SyncRoomChatToolV2.Properties;
using SyncRoomChatToolV2.View;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Speech.Synthesis;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace SyncRoomChatToolV2
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel MainVM = new();
        private readonly bool DemoMode = false;
        private string yourName = "";

        //チャット入力時にも必要なので
        //webAreaとStudioエレメントのスコープを上げてみる。
        //appエレメントがControlじゃないので取れなかっ（多分やり方あるんだろうけど。RootWebAreaの方が上位なんだよなぁ）
        AutomationElement? studio = null;
        AutomationElement? rootWebArea = null;

        #region 正規表現のエリア
        [GeneratedRegex("https?://")]
        private static partial Regex httpReg();

        [GeneratedRegex("[ぁ-んァ-ヶｱ-ﾝﾞﾟ一-龠！-／：-＠［-｀｛-～、-〜”’・]")]
        private static partial Regex jpReg();

        [GeneratedRegex(@"^\/\d{1,9}")]
        private static partial Regex styleReg();

        [GeneratedRegex(@"\d{1,2}")]
        private static partial Regex numReg();

        [GeneratedRegex(@"^/p", RegexOptions.IgnoreCase)]
        private static partial Regex speedReg();

        [GeneratedRegex(@"^[[0-9]+[.]?[0-9]{1,1}|[0-9]+]")]
        private static partial Regex num2Reg();

        [GeneratedRegex(@"^/s", RegexOptions.IgnoreCase)]
        private static partial Regex speechReg();

        [GeneratedRegex("ツイキャスユーザ")]
        private static partial Regex twiCasUserReg();

        [GeneratedRegex(@"(ω)|((８|8){2,})|((８|8){1,})|((ｗ|w){2,})|((ｗ|w){1,}$)", RegexOptions.IgnoreCase)]
        private static partial Regex MultiChatReg();
        #endregion

        private static string LastURL = "";

        static readonly List<Speaker> VoiceLists = [];
        private static readonly string VoiceVoxDefaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs\\VOICEVOX\\vv-engine\\run.exe");
        private static readonly string VoiceVoxDefaultOldPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs\\VOICEVOX\\run.exe");

        private static readonly Dictionary<string, Speaker> UserTable = [];
        private static readonly List<Speaker> StyleDef = [];
        private static readonly int[] RandTable = [0, 1, 2, 3, 6, 7, 8, 9, 10, 14, 16, 20, 23, 29];

        private static BitmapSource? CaptureAndConvert(AutomationElement avatar)
        {
            try
            {
                if (avatar is null)
                {
                    return null;
                }
                var rect = avatar.Current.BoundingRectangle;
                if (rect.IsEmpty)
                {
                    return null;
                }
                // Set the bitmap object to the size of the screen
                using var bmpScreenshot = new Bitmap((int)rect.Width, (int)rect.Height, PixelFormat.Format32bppArgb);

                // Create a graphics object from the bitmap
                using var gfxScreenshot = Graphics.FromImage(bmpScreenshot);

                // Take the screenshot from the upper left corner to the right bottom corner
                gfxScreenshot.CopyFromScreen((int)rect.X, (int)rect.Y, 0, 0,
                                                 new System.Drawing.Size((int)((int)rect.Width), (int)((int)rect.Height)), CopyPixelOperation.SourceCopy);

                var buffer = new byte[bmpScreenshot.Size.Height * bmpScreenshot.Size.Width * 4];
                using var stream = new MemoryStream(buffer);

                bmpScreenshot.Save(stream, ImageFormat.Png);
                stream.Seek(0, SeekOrigin.Begin);
                BitmapSource bitmapSource = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

                //bmpScreenshot.Dispose();
                //gfxScreenshot.Dispose();
                //stream.Dispose();

                return bitmapSource;
            }
            catch (Exception)
            {
                throw;
            }
        }

        private static void UpdateUserOption(string UserName, int StyleId, bool ChimeFlg, bool SpeechFlg, double SpeedScale)
        {
            if (UserTable.TryGetValue(UserName, out var item))
            {
                item.StyleId = StyleId;
                item.UserName = UserName;
                item.ChimeFlg = ChimeFlg;
                item.SpeechFlg = SpeechFlg;
                item.SpeedScale = SpeedScale;
            }
            else
            {
                Speaker addLine = new()
                {
                    StyleId = StyleId,
                    UserName = UserName,
                    ChimeFlg = ChimeFlg,
                    SpeechFlg = SpeechFlg,
                    SpeedScale = SpeedScale
                };
                UserTable[UserName] = addLine;
            }
        }

        // VoiceVoxWarmUp メソッドの修正
        private static async Task VoiceVoxWarmUp()
        {
            await Task.Run(() =>
            {
                string[] testMessages = ["テストです。", "これはウォームアップ用の長めの文章です。", "VOICEVOXの動作確認をおこなっています。"];
                int styleId = 2;
                string baseUrl = Settings.Default.VoiceVoxAddress;
                if (string.IsNullOrEmpty(baseUrl)) baseUrl = "http://127.0.0.1:50021";
                if (!baseUrl.EndsWith('/')) baseUrl += "/";

                foreach (var testMessage in testMessages)
                {
                    string url = baseUrl + $"audio_query?text='{testMessage}'&speaker={styleId}";
                    var client = new ServiceHttpClient(url, ServiceHttpClient.RequestType.none);
                    string queryResponse = "";
                    var ret = client.Post(ref queryResponse, "");
                    if (ret is null) continue;

                    var queryJson = JsonConvert.DeserializeObject<AccentPhrasesRoot>(queryResponse.ToString());
                    if (queryJson is null) continue;
                    queryJson.VolumeScale = Settings.Default.Volume;
                    queryJson.SpeedScale = 1.0;
                    queryResponse = JsonConvert.SerializeObject(queryJson);

                    if (ret.StatusCode.Equals(HttpStatusCode.OK))
                    {
                        url = baseUrl + $"synthesis?speaker={styleId}";
                        client = new ServiceHttpClient(url, ServiceHttpClient.RequestType.none);

                        string wavFile = Path.Combine(Path.GetTempPath(), $"chat_warmup_{Guid.NewGuid()}.wav");
                        ret = client.Post(ref queryResponse, wavFile);
#if DEBUG
                        if (ret.StatusCode.Equals(HttpStatusCode.OK))
                        {
                            // デバッグ時のみ再生
                            PlayWavAsync(wavFile).Wait();
                        }
#endif
                    }
                }
            });
        }

        private static async Task SpeechMessageAsync(string UserName, string Message)
        {
            int Lang = 0;
            int StyleId = 2;
            bool ChimeFlg = false;
            bool SpeechFlg = true;
            double SpeedScale = 1;

            //しゃべらないなら抜ける。外で判断してるっけ？
            //if (Settings.Default.CanSpeech == false) { return; }

            //Microsoft Harukaの設定
            SpeechSynthesizer synth = new()
            {
                Rate = -1
            };
            synth.SelectVoice("Microsoft Haruka Desktop");

            //正規表現用match作成
            Match match;

            //絵文字っぽいのが入っているかどうかのチェック。半角スペースに置換
            var newCommentChar = Message.ToCharArray();
            for (int i = 0; i < newCommentChar.Length; i++)
            {
                switch (char.GetUnicodeCategory(newCommentChar[i]))
                {
                    case System.Globalization.UnicodeCategory.Surrogate:
                        newCommentChar[i] = Convert.ToChar(" ");
                        break;
                    case System.Globalization.UnicodeCategory.OtherSymbol:
                        newCommentChar[i] = Convert.ToChar(" ");
                        break;
                    case System.Globalization.UnicodeCategory.PrivateUse:
                        newCommentChar[i] = Convert.ToChar(" ");
                        break;
                }
            }

            Message = new string(newCommentChar);
            Message = Message.Trim();

            //ωのチェック。これうざいので。
            Message = Message.Replace("ω", "");

            if (string.IsNullOrEmpty(Message)) { return; }

            //英数のみかのチェックというか、指定のワードが入ってるかどうか（主に日本語）
            match = jpReg().Match(Message);
            if (match.Success == false) { Lang = 1; }

            //ランダム音声割り当て用。ここ、コメントしたら全員デフォでしゃべる。
            Random rnd = new() { };
            StyleId = RandTable[rnd.Next(RandTable.Length)];

            //AivisSpeech用のBaseUrlだった場合、StyleIdの初期値を変える。
            if (Settings.Default.VoiceVoxAddress.Contains("10101"))
            {
                StyleId = 888753760;
            }

            bool existsFlg = UserTable.ContainsKey(UserName);
            if (existsFlg)
            {
                var item2 = UserTable[UserName];
                StyleId = item2.StyleId;
                ChimeFlg = item2.ChimeFlg;
                SpeechFlg = item2.SpeechFlg;
                SpeedScale = item2.SpeedScale;
            }
            else
            {
                UpdateUserOption(UserName, StyleId, ChimeFlg, SpeechFlg, SpeedScale);
            }
            //ランダムここまで

            //行頭のコマンド有無のチェック。スタイル指定。
            match = styleReg().Match(Message);
            if (match.Success)
            {
                Message = Message.Replace(match.ToString(), "");
                //[数値]な形式の数値ではある。桁指定したので、[0]～[99]まで。
                match = numReg().Match(match.ToString());
                if (match.Success)
                {
                    //数値は取れたので範囲チェック。StyleIdの一覧と比較。
                    if (StyleDef.Exists(x => x.StyleId == int.Parse(match.ToString())))
                    {
                        StyleId = int.Parse(match.ToString());

                        //[]で指定された数値が、スタイル一覧と合致した場合は、UserTableになければ追加、あれば更新。
                        UpdateUserOption(UserName, StyleId, ChimeFlg, SpeechFlg, SpeedScale);
                    }
                }
            }

            //行頭のコマンド有無のチェック。スピード指定。
            match = speedReg().Match(Message);
            if (match.Success)
            {
                //まずは/pで始まってるか。見つかったらそれはコメントから除去
                Message = Message.Replace(match.ToString(), "");
                match = num2Reg().Match(Message);
                if (match.Success)
                {
                    //次に数字があるか。
                    Message = Message.Replace(match.ToString(), "");
                    SpeedScale = Convert.ToDouble(match.ToString());
                    if (SpeedScale > 1.8)
                    {
                        SpeedScale = 1.8;
                    }
                    if (SpeedScale < 0.4)
                    {
                        SpeedScale = 0.4;
                    }
                    UpdateUserOption(UserName, StyleId, ChimeFlg, SpeechFlg, SpeedScale);
                }
            }

            //行頭コマンドチェック。/s はスピーチのトグル
            match = speechReg().Match(Message);
            if (match.Success)
            {
                Message = Message.Replace(match.ToString(), "");
                UpdateUserOption(UserName, StyleId, ChimeFlg, !SpeechFlg, SpeedScale);
            }

            /*
            //行頭コマンドチェック。/c はチャイムのトグル
            match = chimeReg().Match(Message);
            if (match.Success)
            {
                Message = Message.Replace(match.ToString(), "");
                UpdateUserOption(existsFlg, UserName, StyleId, !ChimeFlg, SpeechFlg, SpeedScale);
            }
            */

            //UserTableから、StyleIdその他の取り出し。
            if (UserTable.TryGetValue(UserName, out var item))
            {
                StyleId = item.StyleId;
                ChimeFlg = item.ChimeFlg;
                SpeechFlg = item.SpeechFlg;
                //break;
            }

            //スピーチフラグチェック。スピーチしない＝抜ける
            if (SpeechFlg == false) { return; }

            //名前にツイキャスユーザが入っている場合。
            if (twiCasUserReg().Match(Message).Success) { StyleId = 8; }

            // まとめて判定・置換
            match = MultiChatReg().Match(Message);
            while (match.Success)
            {
                if (match.Groups[1].Success) // ω
                {
                    Message = Message.Replace("ω", "");
                }
                else if (match.Groups[2].Success) // 8888, ８８８８
                {
                    Message = Message.Replace(match.Groups[2].Value, "、パチパチパチ");
                }
                else if (match.Groups[4].Success) // 88, ８８
                {
                    Message = Message.Replace(match.Groups[4].Value, "、パチ");
                }
                else if (match.Groups[6].Success) // ｗｗｗ
                {
                    Message = Message.Replace(match.Groups[6].Value, "、ふふっ");
                    Lang = 0;
                }
                else if (match.Groups[8].Success) // ｗ
                {
                    Message = Message.Replace(match.Groups[8].Value, "、ふふっ");
                    Lang = 0;
                }
                match = match.NextMatch();
            }

            //文字数制限
            if (Message.Length > (int)Settings.Default.CutLength)
            {
                string[] cutText = ["、以下略。", ", Omitted below"];
                Message = Message[..(int)(Settings.Default.CutLength - 1)];
                Message += cutText[Lang];
            }

            if (Lang == 1)
            {
                synth.SelectVoice("Microsoft Zira Desktop");
                synth.Speak(Message);
                return;
            }

            if ((Settings.Default.UseVoiceVox == false))
            {
                synth.Speak(Message);
                return;
            }

            //VOICEVOX用
            string baseUrl = Settings.Default.VoiceVoxAddress;

            if (string.IsNullOrEmpty(baseUrl))
            {
                //たまに消えるよね、君。
                baseUrl = "http://127.0.0.1:50021";
            }

            if (baseUrl.Substring(baseUrl.Length - 1, 1) != "/")
            {
                baseUrl += "/";
            }

            //クエリー作成
            string url = baseUrl + $"audio_query?text='{Message}'&speaker={StyleId}";

            var client = new ServiceHttpClient(url, ServiceHttpClient.RequestType.none);
            string QueryResponce = "";

            var ret = client.Post(ref QueryResponce, "");

            if (ret is null) { return; }

            //音声合成
            var queryJson = JsonConvert.DeserializeObject<AccentPhrasesRoot>(QueryResponce.ToString());
            if (queryJson is null) { return; }
            queryJson.VolumeScale = Settings.Default.Volume;
            queryJson.SpeedScale = SpeedScale;
            QueryResponce = JsonConvert.SerializeObject(queryJson);

            if (ret.StatusCode.Equals(HttpStatusCode.OK))
            {
                url = baseUrl + $"synthesis?speaker={StyleId}";
                client = new ServiceHttpClient(url, ServiceHttpClient.RequestType.none);

                string wavFile = Path.Combine(Path.GetTempPath(), "chat.wav"); //Path.GetRandomFileName());
                ret = client.Post(ref QueryResponce, wavFile);
                if (ret.StatusCode.Equals(HttpStatusCode.OK))
                {
                    /*
                    //再生する。
                    using var waveReader = new WaveFileReader(wavFile);
                    using var waveOut = new WaveOut();
                    waveOut.Init(waveReader);
                    waveOut.Play();

                    while (waveOut.PlaybackState == PlaybackState.Playing)
                    {
                        Task.Delay(50);
                    }
                    */
                    //非同期で再生する。
                    await PlayWavAsync(wavFile);
                }
            }
        }

        private static async Task PlayWavAsync(string wavFile)
        {
            using var waveReader = new WaveFileReader(wavFile);
            using var waveOut = new WaveOut();
            var tcs = new TaskCompletionSource();
            waveOut.Init(waveReader);
            waveOut.PlaybackStopped += (s, e) => tcs.SetResult();
            waveOut.Play();
            await tcs.Task;
        }

        public MainWindow()
        {
            //前のバージョンのプロパティを引き継ぐぜ。
            Settings.Default.Upgrade();

            InitializeComponent();

            //100ms以下は流石に速すぎると思うの。
            if (Settings.Default.WaitValue < 100) { Settings.Default.WaitValue = 100; }
            //文字列カットも20文字未満は流石に切りすぎだと思うの。
            if (Settings.Default.CutLength < 20) { Settings.Default.CutLength = 20; }

            //VOICEVOXのパス設定がされていなくて（初回起動時想定。デフォルトコンフィグは空なので）
            if (String.IsNullOrEmpty(Settings.Default.VoiceVoxPath))
            {
                //VOICEVOXデフォルトパスにRun.exeが居る＝インストールされているとみなし、
                if (File.Exists(VoiceVoxDefaultPath))
                {
                    //設定に保存する＝VOICEVOXが使えると見なす。
                    //VOICEVOX 0.16 以降のバージョンパス（vv-engine）
                    Settings.Default.VoiceVoxPath = VoiceVoxDefaultPath;
                }
                else
                {
                    //パス設定なし＝初回＆VOICEVOX 0.16 未満のバージョン（旧パス）
                    Settings.Default.VoiceVoxPath = VoiceVoxDefaultOldPath;
                }
            }
            else
            {
                if (!File.Exists(Settings.Default.VoiceVoxPath))
                {
                    //VOICEVOXデフォルトパスにRun.exeが居る＝インストールされているとみなし、
                    if (File.Exists(VoiceVoxDefaultPath))
                    {
                        //設定に保存する＝VOICEVOXが使えると見なす。
                        //VOICEVOX 0.16 以降のバージョンパス（vv-engine）
                        Settings.Default.VoiceVoxPath = VoiceVoxDefaultPath;
                    }
                    else
                    {
                        //パス設定なし＝初回＆VOICEVOX 0.16 未満のバージョン（旧パス）
                        Settings.Default.VoiceVoxPath = VoiceVoxDefaultOldPath;
                    }
                }
            }

            //存在しないリンクが貼られてた際の固定音声ファイルが指定されている＝裏で直接コンフィグファイルをイジった想定
            if (!File.Exists(Settings.Default.LinkWaveFilePath))
            {
                //固定ファイルなしとする。
                Settings.Default.LinkWaveFilePath = "";
            }

            //VOICEVOXのローカルアドレスチェック
            if (String.IsNullOrEmpty(Settings.Default.VoiceVoxAddress))
            {
                Settings.Default.VoiceVoxAddress = "http://127.0.0.1:50021";
            }
            Settings.Default.Save();

            //VOICEVOXエンジンの起動チェック
            TargetProcess tp = new("run");
            if (!string.IsNullOrEmpty(Settings.Default.VoiceVoxPath))
            {
                if (Path.Exists(Settings.Default.VoiceVoxPath))
                {
                    if (tp.IsAlive == false)
                    {
                        try
                        {
                            //自動起動をトライするが、失敗したって平気さ。知らねぇよ。
                            ProcessStartInfo processStartInfo = new()
                            {
                                FileName = Settings.Default.VoiceVoxPath,
                                WindowStyle = ProcessWindowStyle.Hidden
                            };
                            Process.Start(processStartInfo);
                        }
                        catch
                        {
                            SpeechSynthesizer synth = new();
                            synth.SelectVoice("Microsoft Haruka Desktop");
                            synth.Speak($"エラーが発生しています。VOICEVOXの自動起動に失敗しました。");
                            System.Windows.Application.Current.Shutdown();
                        }
                    }

                    string url = Settings.Default.VoiceVoxAddress;
                    if (!string.IsNullOrEmpty(url))
                    {
                        if (url.Substring(url.Length - 1, 1) != "/")
                        {
                            url += "/";
                        }
                        url += "speakers";
                        var client = new ServiceHttpClient(url, ServiceHttpClient.RequestType.none);
                        var ret = client.Get();
                        if (ret != null)
                        {
                            //Jsonのデシリアライズ。VOICEVOXのStyleIdの一覧を作る。
#nullable disable warnings
                            List<SpeakerFromAPI> VoiceVoxSpeakers = JsonConvert.DeserializeObject<List<SpeakerFromAPI>>(ret.ToString());

                            foreach (SpeakerFromAPI speaker in VoiceVoxSpeakers)
                            {
                                foreach (StyleFromAPI st in speaker.Styles)
                                {
                                    Speaker addLine = new()
                                    {
                                        StyleId = st.Id
                                    };

                                    //ホントはSyncRoomのユーザ用のClassだけど、Voiceの一覧にも流用
                                    //ホントは自分のIDとボイス名だけでもいい気がするんだけど、そのマッチは面倒だったので。
                                    Speaker addVoice = new()
                                    {
                                        UserName = $"{speaker.Name}({st.Name})",
                                        StyleId = addLine.StyleId
                                    };

                                    VoiceLists.Add(addVoice);
                                    StyleDef.Add(addLine);
                                }
                            }
#nullable restore
                            /* autoCompListが使えるかも分からんし、一旦コメントアウト */
                            VoiceLists.Sort((a, b) => a.StyleId - b.StyleId);
                            foreach (Speaker st in VoiceLists)
                            {
                                // 候補リストに項目を追加（初期設定）
                                ChatInputCombo.Items.Add($"/{st.StyleId} {st.UserName} にボイス変更");
                            }
                            ChatInputCombo.Items.Add("/p0.4 最小スピード");
                            ChatInputCombo.Items.Add("/p1.0 標準スピード");
                            ChatInputCombo.Items.Add("/p1.8 最大スピード");
                            ChatInputCombo.Items.Add("/s スピーチのトグル");
                        }
                    }
                }
            }

            //デモモードの取得
            string[] args = Environment.GetCommandLineArgs();
            foreach (var arg in args)
            {
                if (arg != null)
                {
                    if (arg == "/demo")
                    {
                        DemoMode = true;
                        break;
                    }
                }
            }

            ContentRendered += MainWindow_ContentRendered;
            Closing += MainWindow_Closing;
            try
            {
                ToastNotificationManagerCompat.OnActivated += ToastNotificationManagerCompat_OnActivated;
            }
            catch (Exception)
            {
                //無理に登録しなくてもいいよね。
            }

            MainVM.Info.SysInfo = "起動中…";
            //MainVM.Info.ChatLog = "";
            DataContext = MainVM;
        }

        private void ToastNotificationManagerCompat_OnActivated(ToastNotificationActivatedEventArgsCompat e)
        {
            var arg = e.Argument;
            if (!string.IsNullOrEmpty(arg))
            {
                if (arg == "cancel") { return; }

                Uri u = new(arg);

                if (u.IsAbsoluteUri)
                {
                    Tools.OpenUrl(arg);
                }
            }
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

            if (widthA == 0)
            {
                widthA = 200;
            }
            if (widthB == 0)
            {
                widthB = Width - widthA;
            }

            SplitGrid.ColumnDefinitions[0].Width = new GridLength(widthA, GridUnitType.Star);
            SplitGrid.ColumnDefinitions[2].Width = new GridLength(widthB, GridUnitType.Star);


            var fullname = typeof(App).Assembly.Location;
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(fullname);
            var ver = info.FileVersion;
            Title = $"SyncRoom読み上げちゃん V2 ver {ver}";

            _ = VoiceVoxWarmUp(); // 画面描画後にウォームアップ

            _ = GetChat();
        }

        async Task GetChat()
        {
            AutomationElement? rootElement = null;

            string msg = "読み上げちゃん起動中…";
            bool firstFlg = true;

            //MainVM.Info.ChatLog = "";
            MainVM.Chats.Clear();

            //外のループ。プロセス確認用。
            while (true)
            {
                MainVM.Info.IsEntered = false;

                TargetProcess targetProc = new("SYNCROOM2");

                MainVM.Info.SysInfo = msg;

                await Task.Delay(2000);

                if (targetProc.IsAlive == false)
                {
                    msg = $"No SyncRoom Process. {DateTime.Now}";
                    continue;
                }

                msg = "SyncRoomが起動されています。";

                //タイトル検索なので、他のプロセスでも"SYNCROOM"が入ってると…
                Process[] procs = Tools.GetProcessesByWindowTitle("SYNCROOM");
                if (procs.Length == 0)
                {
                    msg = $"No 'SYNCROOM' Title Window. {DateTime.Now}";
                    continue;
                }

                foreach (Process proc in procs)
                {
                    if (proc.MainWindowTitle == "SYNCROOM")
                    {
                        //MainWindowTitle が "SYNCROOM"なプロセス＝ターゲットのプロセスは、SYNCROOM2.exeが中で作った別プロセスのようで
                        //こんな面倒なやり方をしてみている。
                        rootElement = AutomationElement.FromHandle(proc.MainWindowHandle);
                        break;
                    }
                }

                if (rootElement is null)
                {
                    msg = "RootElement Is Null.";
                    continue;
                }

                rootWebArea = rootElement.FindFirst(TreeScope.Children | TreeScope.Descendants,
                                                                    new PropertyCondition(AutomationElement.AutomationIdProperty, "RootWebArea"));
                if (rootWebArea is null)
                {
                    msg = "RootWebArea Is Null.";
                    continue;
                }

                //狙いの要素のちょい上の要素に、"studio"ってのがある。ここを起点にする。
                studio = rootWebArea.FindFirst(TreeScope.Element | TreeScope.Descendants,
                                                                    new PropertyCondition(AutomationElement.AutomationIdProperty, "studio"));

                MainVM.Members?.Clear();

                if (studio is null)
                {
                    msg = "studio is null.";
                    continue;
                }

                string oldMessage = "";

                TreeWalker twName = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "name"));
                TreeWalker twTime = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "time"));
                TreeWalker twMessage = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "message"));
                TreeWalker twPart = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "part"));
                TreeWalker twRack = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "rack"));
                TreeWalker twDivision = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "division"));
                TreeWalker twAvatar = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "avatar"));

                TreeWalker twControl = new(new PropertyCondition(AutomationElement.IsControlElementProperty, true));
                TreeWalker twImage = new(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Image));

                msg = "studio exist.";

                //自分自身をセットする。
                AutomationElement rack = twRack.GetFirstChild(studio);
                AutomationElement? yourSelf = null;
                if (rack is not null)
                {
                    yourSelf = twDivision.GetFirstChild(rack);
                    if (yourSelf is not null)
                    {
                        //名前と演奏パートの取得
                        var tempName = twName.GetFirstChild(yourSelf);
                        if (tempName is null) { break; }
                        var tempNameText = twControl.GetFirstChild(tempName);
                        yourName = tempNameText.Current.Name;

                        var tempPart = twPart.GetFirstChild(yourSelf);
                        if (tempPart is null) { break; }
                        var tempPartText = twControl.GetFirstChild(tempPart);

                        var tempAvatar = twAvatar.GetFirstChild(yourSelf);
                        if (tempAvatar is null) { break; }
                        var tempAvatarImage = twImage.GetFirstChild(tempAvatar);
                        //何とかキャプチャしてアイコン取った。
                        BitmapSource? bitmapSource = CaptureAndConvert(tempAvatarImage);

                        Member item = new();
                        if (tempNameText.Current.Name != null)
                        {
                            item.MemberName = tempNameText.Current.Name;
                        }
                        if (tempPartText.Current.Name != null)
                        {
                            item.MemberPart = tempPartText.Current.Name;
                        }

                        if (bitmapSource is not null)
                        {
                            item.MemberImage = new()
                            {
                                Source = bitmapSource
                            };
                        }

                        if (MainVM.Members is null) { break; }
                        MainVM.Members.Add(item);

                    }
                }

                TreeWalker twChat = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "chat"));
                AutomationElement chat = twChat.GetFirstChild(studio);
                if (chat is null)
                {
                    twChat = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "docked-chat"));
                    chat = twChat.GetFirstChild(rootWebArea);
                    if (chat is null) { continue; }
                }

                //非常にダサいがメインループの外で一回チャットの最終行を取得し、oldMessageにぶっ込む。
                AutomationElement chatList1 = chat.FindFirst(TreeScope.Element | TreeScope.Descendants,
                                                                    new PropertyCondition(AutomationElement.AutomationIdProperty, "chatList"));
                AutomationElement elMessage1 = twMessage.GetLastChild(chatList1);
                if (elMessage1 is null)
                {
                    firstFlg = true;
                }
                else
                {
                    elMessage1 = twControl.GetLastChild(elMessage1);
                    if (elMessage1 is not null) { oldMessage = elMessage1.Current.Name; }
                    firstFlg = string.IsNullOrEmpty(oldMessage);
                }

                //連結申請のフラグ。
                bool invitationFlg = false;

                //メインのループ。チャット取得用。
                while (true)
                {
                    MainVM.Info.SysInfo = msg;
                    MainVM.Info.IsEntered = true;

                    await Task.Delay((int)Settings.Default.WaitValue);

                    try
                    {
                        //連結チェック
                        if (rootWebArea is null) { continue; }

                        TreeWalker twApp = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "app"));
                        AutomationElement app = twApp.GetFirstChild(rootWebArea);
                        if (app is null) { continue; }

                        TreeWalker twInvitations = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "room-invitations-length-back"));
                        AutomationElement elInvitation = twInvitations.GetFirstChild(app);
                        if (elInvitation is null) { invitationFlg = false; }

                        if (invitationFlg == false)
                        {
                            if (elInvitation != null)
                            {
                                new ToastContentBuilder()
                                    .AddText($"連結申請が届いてます。")
                                    .Show();

                                var item = new Chat
                                {
                                    ChatTime = DateTime.Now.ToString("G"),
                                    UserName = "System",
                                    Message = "連結申請が届いています。",
                                    IsYourSelf = false,
                                    Link = "",
                                    IsLink = false
                                };
                                MainVM.Chats.Add(item);
                                if (Settings.Default.CanSpeech)
                                {
                                    if (Settings.Default.SpeechWhenInvited)
                                    {
                                        //_ = Task.Run(() => SpeechMessageAsync(item.UserName, item.Message));
                                        await SpeechMessageAsync(item.UserName, item.Message);
                                    }
                                }
                                invitationFlg = true;
                            }
                        }

                        //メンバーの削除＆追加（毎回やる割には問題なさそう）
                        if (yourSelf is not null)
                        {
#nullable disable warnings
                            var roomMember = twDivision.GetNextSibling(yourSelf);

                            for (int i = (MainVM.Members.Count) - (1); i >= 1; i--)
                            {
                                MainVM.Members.RemoveAt(i);
                            }

                            while (roomMember is not null)
                            {
                                var tempName = twName.GetFirstChild(roomMember);
                                if (tempName is null) { break; }
                                var tempPart = twPart.GetFirstChild(roomMember);
                                if (tempPart is null) { break; }
                                var tempNameText = twControl.GetFirstChild(tempName);
                                if (tempNameText is null) { break; }
                                var tempPartText = twControl.GetFirstChild(tempPart);
                                if (tempPartText is null) { break; }
                                var tempAvatar = twAvatar.GetFirstChild(roomMember);
                                if (tempAvatar is null) { break; }
                                var tempAvatarImage = twImage.GetFirstChild(tempAvatar);
                                BitmapSource bitmapSource = CaptureAndConvert(tempAvatarImage);

                                Member item = new();
                                if (tempNameText.Current.Name != null)
                                {
                                    item.MemberName = tempNameText.Current.Name;
                                }
                                if (tempPartText.Current.Name != null)
                                {
                                    item.MemberPart = tempPartText.Current.Name;
                                }
                                item.MemberImage = new()
                                {
                                    Source = bitmapSource
                                };

                                MainVM.Members.Add(item);
#nullable restore
                                roomMember = twDivision.GetNextSibling(roomMember);
                            }
                        }
                        else
                        {
                            break;
                        }

                        //chatListのAutomationIdを持つ要素の下に、divisionってAutomationIdを持つ要素群＝チャットの各行っつうか名前と時間とチャット内容が入っとる。
                        AutomationElement chatList = chat.FindFirst(TreeScope.Element | TreeScope.Descendants,
                                                                            new PropertyCondition(AutomationElement.AutomationIdProperty, "chatList"));

                        if (chatList is null)
                        {
                            msg = "chatList is null.";
                            break;
                        }

                        msg = "チャット入力待ち";
                        AutomationElement elName = twName.GetLastChild(chatList);
                        if (elName is null)
                        {
                            continue;
                        }

                        elName = twControl.GetLastChild(elName);
                        if (elName is null) { continue; }

                        AutomationElement elTime = twTime.GetLastChild(chatList);
                        if (elTime is null) { continue; }
                        elTime = twControl.GetLastChild(elTime);
                        if (elTime is null) { continue; }


                        AutomationElement elMessage = twMessage.GetLastChild(chatList);
                        if (elMessage is null) { continue; }
                        elMessage = twControl.GetLastChild(elMessage);
                        if (elMessage is null) { continue; }


                        //string chatLine = $"{elName.Current.Name} {elTime.Current.Name} {elMessage.Current.Name}";
                        string chatLine = $"[{elTime.Current.Name}] {elMessage.Current.Name}";

                        if ((elMessage.Current.Name != oldMessage) || (firstFlg))
                        {
                            firstFlg = false;
                            //MainVM.Info.ChatLog += Environment.NewLine + chatLine;

                            string Message = elMessage.Current.Name;

                            //リンク自動オープン時の処理。
                            bool IsLink = false;
                            string url = "";
                            Match match;
                            match = httpReg().Match(Message);
                            if (match.Success)
                            {
                                IsLink = true;
                                string UriString = Message[match.Index..];
                                Uri u = new(UriString);

                                Message = "リンクが張られました";

                                if (Settings.Default.OpenLink)
                                {
                                    if (UriString != LastURL)
                                    {
                                        if (u.IsAbsoluteUri)
                                        {
                                            Tools.OpenUrl(UriString);
                                        }
                                    }
                                }
                                LastURL = UriString;
                                url = UriString;
                            }

                            //チャット風表示
                            bool IsYourSelf = false;
                            if (elName.Current.Name == yourName)
                            {
                                IsYourSelf = true;
                            }

                            bool RandChat = IsYourSelf;

                            if (DemoMode)
                            {
                                var random = new Random();

                                RandChat = random.Next(2) == 1;
                            }

                            var item = new Chat
                            {
                                ChatTime = elTime.Current.Name,
                                UserName = elName.Current.Name,
                                Message = elMessage.Current.Name,
                                IsYourSelf = RandChat,
                                Link = url,
                                IsLink = IsLink
                            };

                            Application.Current.Dispatcher.Invoke(() => MainVM.Chats.Add(item));


                            if (Settings.Default.CanSpeech)
                            {
                                if (IsLink)
                                {
                                    oldMessage = elMessage.Current.Name;
                                    msg = "リンクが張られました。";

                                    //リンク固定ファイル再生時
                                    if (!string.IsNullOrEmpty(Settings.Default.LinkWaveFilePath))
                                    {
                                        if (Path.Exists(Settings.Default.LinkWaveFilePath))
                                        {
                                            string wavFile = Path.Combine(Path.GetTempPath(), "chat.wav");
                                            File.Copy(Settings.Default.LinkWaveFilePath, wavFile, true);

                                            /*
                                            using var waveReader = new WaveFileReader(Settings.Default.LinkWaveFilePath);
                                            using var waveOut = new WaveOut();
                                            waveOut.Init(waveReader);
                                            waveOut.Play();
                                            while (waveOut.PlaybackState == PlaybackState.Playing)
                                            {
                                                await Task.Delay(50);
                                            }
                                            */
                                            //非同期で再生する。
                                            await PlayWavAsync(Settings.Default.LinkWaveFilePath);
                                            continue;
                                        }
                                    }
                                }
                                if (!string.IsNullOrEmpty(Message))
                                {
                                    //_ = Task.Run(() => SpeechMessageAsync(elName.Current.Name, Message));
                                    await SpeechMessageAsync(elName.Current.Name, Message);
                                }
                            }
                        }
                        oldMessage = elMessage.Current.Name;

                        msg = $"監視中… {DateTime.Now}";
                    }
                    catch (Exception e)
                    {
                        msg = $"何かエラーです。[{e.Message}] {DateTime.Now}";
                        break;
                    }
                }
            }
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow();
            settingsWindow.ShowDialog();
        }

        private void ChatInputCombo_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (studio is null) { return; }

            TreeWalker twChat = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "chat"));
            AutomationElement chat = twChat.GetFirstChild(studio);
            if (chat is null)
            {
                twChat = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "docked-chat"));
                chat = twChat.GetFirstChild(rootWebArea);
                if (chat is null) { return; }
            }

            //編集エリアとボタンのグループを探す。
            TreeWalker twChatInput = new(new PropertyCondition(AutomationElement.ClassNameProperty, "chat-input-backgraund d-flex floatable"));
            if (twChatInput is null) { return; }
            AutomationElement chatInputBackground = twChatInput.GetFirstChild(chat);
            if (chatInputBackground is null) { return; }

            //編集エリア内から、テキストボックスの実体を探す。
            TreeWalker twEdit = new(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));

            //2.1.1でコメントアウト
            //編集エリアの上の要素
            //AutomationElement EditBoxParent = twEdit.GetFirstChild(chatInputBackground);
            //if (EditBoxParent is null) { return; }
            //編集エリアのテキストボックスの要素（ここに文字はぶっ込む）
            //AutomationElement EditBox = twEdit.GetFirstChild(EditBoxParent);

            //編集エリアのテキストボックスの要素（ここに文字はぶっ込む）
            AutomationElement EditBox = twEdit.GetLastChild(chatInputBackground);
            if (EditBox is null) { return; }

            //2.1.1だとここは要らない
            //（構造が変わって「編集（上位）」→「編集（実体）」ではなく、いきなり「編集（実体のテキストボックス）」なので。

            //2.1.0以前の対応になるかな？
            int cnt = 0;
            //実体のテキストボックスのNameは「チャット」なのでそこでチェック。チョイダサい。
            while (EditBox.Current.Name is not "チャット")
            {
                cnt++;
                EditBox = twEdit.GetLastChild(EditBox);
                //一応3回トライして見つからなかったら抜ける。
                if (cnt >= 3)
                {
                    break;
                }
                //抜けちゃって実体のテキストボックス要素が取れなくても、入力出来ないだけで落ちはしない模様。
            }

            //ボタンの実体を探す（このボタンをInvokeする）
            TreeWalker twButton = new(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            AutomationElement EditButton = twButton.GetLastChild(chatInputBackground);

            if (e.Key == Key.Return)
            {
                if (EditBox.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePattern))
                {
                    ((ValuePattern)valuePattern).SetValue(ChatInputCombo.Text);

                    if (EditButton.GetCurrentPattern(InvokePattern.Pattern) is InvokePattern btn)
                    {
                        btn.Invoke();
                    }

                    //なんでか2.0.4から文字が残る気がする。前からだっけ？空文字はダメの模様。半角スペースをぶっ込む。
                    ((ValuePattern)valuePattern).SetValue(" ");
                }

                bool existFlg = false;
                foreach (var item in ChatInputCombo.Items)
                {
                    if (item.ToString() == ChatInputCombo.Text)
                    {
                        existFlg = true;
                        break;
                    }
                }

                if (!existFlg)
                {
                    ChatInputCombo.Items.Add(ChatInputCombo.Text);
                }

                ChatInputCombo.Text = "";
                this.Activate();
            }
        }

        private async void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            if (rootWebArea is null) { return; }

            TreeWalker twApp = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "app"));
            AutomationElement app = twApp.GetFirstChild(rootWebArea);
            if (app is null) { return; }

            TreeWalker twExit = new(new PropertyCondition(AutomationElement.ClassNameProperty, "exit-button"));
            AutomationElement elExtBtn = twExit.GetFirstChild(app);
            if (elExtBtn is null) { return; }

            TreeWalker twButton = new(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            AutomationElement exitBtn = twButton.GetFirstChild(elExtBtn);
            if (exitBtn is null) { return; }

            if (exitBtn.GetCurrentPattern(ExpandCollapsePattern.Pattern) is ExpandCollapsePattern btn)
            {
                if (btn.Current.ExpandCollapseState == ExpandCollapseState.Collapsed)
                {
                    btn.Expand();
                }
                await Task.Delay(500);
                TreeWalker twFirst = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "first-area"));
                AutomationElement FirstArea = twFirst.GetFirstChild(rootWebArea);
                if (FirstArea is null) { return; }

                AutomationElement exitBtn2 = twButton.GetFirstChild(FirstArea);

                if (exitBtn2.GetCurrentPattern(InvokePattern.Pattern) is InvokePattern btn2)
                {
                    btn2.Invoke();
                }
            }
        }

        private void ChatViewYourSelf_TargetUpdated(object sender, System.Windows.Data.DataTransferEventArgs e)
        {
            if (ChatViewYourSelf is not null)
            {
#nullable disable warnings
                (ChatViewYourSelf.ItemsSource as INotifyCollectionChanged).CollectionChanged += new NotifyCollectionChangedEventHandler(ChatViewYourSelf_CollectionChanged);
#nullable restore
            }
        }

        private void ChatViewYourSelf_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (ChatViewYourSelf is not null)
            {
                if (ChatViewYourSelf.Items.Count > 0)
                {
                    ChatViewYourSelf?.ScrollIntoView(ChatViewYourSelf.Items[^1]);
                }
            }
        }

        protected override void OnPreviewMouseWheel(MouseWheelEventArgs args)
        {
            base.OnPreviewMouseWheel(args);
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                uiScaleSlider.Value += (args.Delta > 0) ? 0.1 : -0.1;
            }
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            MenuToggleButton.IsChecked = false;
        }

        private void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            var fullname = typeof(App).Assembly.Location;
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(fullname);
            var ver = info.FileVersion;

            //クエリー作成
            string url = "https://github.com/XiAce-Lite/SyncRoomChatToolV2/releases/latest";

            var client = new ServiceHttpClient(url, ServiceHttpClient.RequestType.none);
            var ret = client.Get();
            if (ret is null) { return; }

            var document = JsonConvert.DeserializeObject<System.Dynamic.ExpandoObject>(ret);
            if (document is null) { return; }

            foreach (var item in document)
            {
                if (item.Key == "tag_name")
                {
                    if (item.Value is null)
                    {
                        break;
                    }

                    if (item.Value.ToString() != $"v{ver}")
                    {
                        try
                        {
                            new ToastContentBuilder()
                                .AddText("読み上げちゃんに更新があります。")
                                .AddButton("Githubを開く", ToastActivationType.Foreground, url)
                                .AddButton(new ToastButton("Cancel", "cancel"))
                                .Show();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Failed to show Windows notification: {Message}", ex.Message);
                            Application.Current.Shutdown();
                        }
                    }
                }
            }

            MenuToggleButton.IsChecked = false;
        }
    }
}