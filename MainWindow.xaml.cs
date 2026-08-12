using Microsoft.Toolkit.Uwp.Notifications;
using NAudio.Wave;
using Newtonsoft.Json;
using SyncRoomChatToolV2.Ai;
using SyncRoomChatToolV2.ModelView;
using SyncRoomChatToolV2.Properties;
using SyncRoomChatToolV2.View;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Speech.Synthesis;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
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

        // 無人AIモード（起動時OFF・永続化しない）
        private bool aiUnmannedMode = false;
        private readonly GeminiClient geminiClient = new();
        private readonly GitHubRepoKnowledge repoKnowledge = new();
        private readonly AiPromptStore aiPromptStore = new();
        private readonly List<(string Role, string Text)> aiHistory = [];
        private readonly HashSet<string> aiSentMessages = new(StringComparer.Ordinal);
        private readonly Queue<string> pendingHumanEchoes = new();
        private readonly HashSet<string> knownRoomMemberNames = new(StringComparer.Ordinal);
        private bool roomMemberBaselineReady = false;
        private Channel<(string UserName, string Message)>? aiQueue;
        private CancellationTokenSource? aiWorkerCts;
        private Task? aiWorkerTask;
        private readonly object aiLock = new();
        private bool aiToggleBusy = false;

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

        // /ai, /ai on, /ai off, /ai 無人モードのトグル など
        [GeneratedRegex(@"^/ai(?:\s+(on|off))?(?:\s|$)", RegexOptions.IgnoreCase)]
        private static partial Regex aiModeReg();

        [GeneratedRegex("ツイキャスユーザ")]
        private static partial Regex twiCasUserReg();

        [GeneratedRegex(@"(ω)|((８|8){2,})|((８|8){1,})|((ｗ|w){2,})|((ｗ|w){1,}$)", RegexOptions.IgnoreCase)]
        private static partial Regex MultiChatReg();
        #endregion

        private static string LastURL = "";
        private static string LastLinkWaveUrl = "";
        private static DateTime LastLinkWaveAtUtc = DateTime.MinValue;
        /// <summary>アプリ入力欄から即時表示した本文。UIAエコーで二重追加しない。</summary>
        private readonly Queue<string> optimisticLocalChats = new();

        static readonly List<Speaker> VoiceLists = [];
        private static readonly string VoiceVoxDefaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs\\VOICEVOX\\vv-engine\\run.exe");
        private static readonly string VoiceVoxDefaultOldPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs\\VOICEVOX\\run.exe");

        private static readonly Dictionary<string, Speaker> UserTable = [];
        private static readonly List<Speaker> StyleDef = [];
        private static readonly int[] RandTable = [0, 1, 2, 3, 6, 7, 8, 9, 10, 14, 20, 23, 29];
        private static readonly SemaphoreSlim SpeechGate = new(1, 1);

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

        private static Task VoiceVoxWarmUp()
        {
            return Task.Run(() =>
            {
                string testMessage = "テストです";
                string baseUrl = Settings.Default.VoiceVoxAddress;
                if (string.IsNullOrEmpty(baseUrl))
                {
                    baseUrl = "http://127.0.0.1:50021";
                }
                if (baseUrl.Substring(baseUrl.Length - 1, 1) != "/")
                {
                    baseUrl += "/";
                }

                foreach (int styleId in RandTable)
                {
                    Debug.WriteLine("WarmUp Start: " + styleId.ToString());
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

                        string wavFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".wav");
                        ret = client.Post(ref queryResponse, wavFile);
                        if (ret.StatusCode.Equals(HttpStatusCode.OK))
                        {
                            try
                            {
                                Debug.WriteLine("WarmUp: " + styleId.ToString());
                                if (File.Exists(wavFile))
                                {
                                    File.Delete(wavFile);
                                }
                            }
                            catch
                            {
                                // 削除失敗時は無視
                            }
                        }
                    }
                }
            });
        }

        private static async Task SpeechMessageAsync(string UserName, string Message, bool isAiReply = false)
        {
            // AI返答は合成をゲート待ちと並行し、前発言の読み上げ終了後すぐ再生できるようにする
            Task<string?>? prepareTask = null;
            if (isAiReply && Settings.Default.UseVoiceVox && Settings.Default.CanSpeech)
            {
                string prepareText = NormalizeSpeechText(Message);
                if (!string.IsNullOrEmpty(prepareText))
                {
                    prepareTask = Task.Run(() => TrySynthesizeVoiceVoxToTempFile(prepareText, styleId: 2, speedScale: 1.0));
                }
            }

            await SpeechGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (prepareTask is not null)
                {
                    string? wavFile = null;
                    try
                    {
                        wavFile = await prepareTask.ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"AI VOICEVOX prepare failed: {ex}");
                    }

                    if (!string.IsNullOrEmpty(wavFile))
                    {
                        try
                        {
                            await PlayWavAsync(wavFile).ConfigureAwait(false);
                        }
                        finally
                        {
                            try
                            {
                                if (File.Exists(wavFile))
                                {
                                    File.Delete(wavFile);
                                }
                            }
                            catch
                            {
                                // 削除失敗は無視
                            }
                        }
                        return;
                    }
                }

                await SpeechMessageCoreAsync(UserName, Message, isAiReply).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 読み上げ失敗で監視ループやAIワーカーを落とさない
                Debug.WriteLine($"SpeechMessageAsync failed: {ex}");
            }
            finally
            {
                SpeechGate.Release();
            }
        }

        /// <summary>
        /// 読み上げ前の絵文字・制御文字除去（合成先行用にも使う）
        /// </summary>
        private static string NormalizeSpeechText(string message)
        {
            if (string.IsNullOrEmpty(message)) { return ""; }

            var newCommentChar = message.ToCharArray();
            for (int i = 0; i < newCommentChar.Length; i++)
            {
                switch (char.GetUnicodeCategory(newCommentChar[i]))
                {
                    case System.Globalization.UnicodeCategory.Surrogate:
                    case System.Globalization.UnicodeCategory.OtherSymbol:
                    case System.Globalization.UnicodeCategory.PrivateUse:
                        newCommentChar[i] = ' ';
                        break;
                }
            }

            return new string(newCommentChar).Replace("ω", "").Trim();
        }

        /// <summary>
        /// VOICEVOX で wav を一時ファイルへ合成する。失敗時は null。
        /// </summary>
        private static string? TrySynthesizeVoiceVoxToTempFile(string message, int styleId, double speedScale)
        {
            if (string.IsNullOrWhiteSpace(message)) { return null; }

            string baseUrl = Settings.Default.VoiceVoxAddress;
            if (string.IsNullOrEmpty(baseUrl))
            {
                baseUrl = "http://127.0.0.1:50021";
            }
            if (baseUrl.Substring(baseUrl.Length - 1, 1) != "/")
            {
                baseUrl += "/";
            }

            string url = baseUrl + $"audio_query?text='{message}'&speaker={styleId}";
            var client = new ServiceHttpClient(url, ServiceHttpClient.RequestType.none);
            string queryResponse = "";
            var ret = client.Post(ref queryResponse, "");
            if (ret is null || !ret.StatusCode.Equals(HttpStatusCode.OK)) { return null; }

            var queryJson = JsonConvert.DeserializeObject<AccentPhrasesRoot>(queryResponse);
            if (queryJson is null) { return null; }
            queryJson.VolumeScale = Settings.Default.Volume;
            queryJson.SpeedScale = speedScale;
            queryResponse = JsonConvert.SerializeObject(queryJson);

            url = baseUrl + $"synthesis?speaker={styleId}";
            client = new ServiceHttpClient(url, ServiceHttpClient.RequestType.none);
            string wavFile = Path.Combine(Path.GetTempPath(), $"srct_chat_{Guid.NewGuid():N}.wav");
            ret = client.Post(ref queryResponse, wavFile);
            if (ret is null || !ret.StatusCode.Equals(HttpStatusCode.OK) || !File.Exists(wavFile))
            {
                try
                {
                    if (File.Exists(wavFile)) { File.Delete(wavFile); }
                }
                catch { /* ignore */ }
                return null;
            }
            return wavFile;
        }

        private static async Task SpeechMessageCoreAsync(string UserName, string Message, bool isAiReply)
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
            Message = NormalizeSpeechText(Message);

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

            //スピーチフラグチェック。スピーチしない＝抜ける（AI返信は必ず読む）
            if (!isAiReply && SpeechFlg == false) { return; }

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

            // AI返信は VOICEVOX StyleId=2 固定、CutLength 無視
            if (isAiReply)
            {
                StyleId = 2;
            }
            else if (Message.Length > (int)Settings.Default.CutLength)
            {
                //文字数制限
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
            string? wavFile = TrySynthesizeVoiceVoxToTempFile(Message, StyleId, SpeedScale);
            if (string.IsNullOrEmpty(wavFile)) { return; }
            try
            {
                await PlayWavAsync(wavFile).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (File.Exists(wavFile))
                    {
                        File.Delete(wavFile);
                    }
                }
                catch
                {
                    // 削除失敗は無視
                }
            }
        }

        private static async Task PlayWavAsync(string wavFile)
        {
            // 通常の VOICEVOX 一時WAVなど：従前どおり軽量再生（出音開始を遅らせない）
            using var waveReader = new WaveFileReader(wavFile);
            using var waveOut = new WaveOut();
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            waveOut.PlaybackStopped += (s, e) => tcs.TrySetResult();
            waveOut.Init(waveReader);
            waveOut.Play();

            var finished = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(90))).ConfigureAwait(false);
            if (finished != tcs.Task)
            {
                try { waveOut.Stop(); } catch { /* ignore */ }
                Debug.WriteLine($"PlayWavAsync timeout: {wavFile}");
            }
            else
            {
                await tcs.Task.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// リンク固定WAV専用。メモリ展開 + WaveOutEvent（大きめバッファ）で途切れを抑える。
        /// </summary>
        private static async Task PlayLinkWavBufferedAsync(string wavFile)
        {
            byte[] bytes = await File.ReadAllBytesAsync(wavFile).ConfigureAwait(false);
            using var ms = new MemoryStream(bytes, writable: false);
            using var waveReader = new WaveFileReader(ms);
            using var waveOut = new WaveOutEvent
            {
                DesiredLatency = 500,
                NumberOfBuffers = 4
            };
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnStopped(object? s, StoppedEventArgs e) => tcs.TrySetResult();
            waveOut.PlaybackStopped += OnStopped;
            try
            {
                waveOut.Init(waveReader);
                waveOut.Play();

                var finished = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(90))).ConfigureAwait(false);
                if (finished != tcs.Task)
                {
                    try { waveOut.Stop(); } catch { /* ignore */ }
                    Debug.WriteLine($"PlayLinkWavBufferedAsync timeout: {wavFile}");
                }
                else
                {
                    await tcs.Task.ConfigureAwait(false);
                }
            }
            finally
            {
                waveOut.PlaybackStopped -= OnStopped;
            }
        }

        /// <summary>
        /// 通常読み上げと排他でリンク固定 WAV を再生する。
        /// </summary>
        private static async Task PlayWavGatedAsync(string wavFile)
        {
            await SpeechGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await PlayLinkWavBufferedAsync(wavFile).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PlayWavGatedAsync failed: {ex}");
            }
            finally
            {
                SpeechGate.Release();
            }
        }

        /// <summary>
        /// 同一URL・短時間の再トリガー（UIAのリンク分割更新）を弾く。
        /// </summary>
        private static bool TryClaimLinkWavePlay(string url)
        {
            if (string.IsNullOrEmpty(url)) { return false; }
            var now = DateTime.UtcNow;
            if (string.Equals(url, LastLinkWaveUrl, StringComparison.Ordinal)
                && (now - LastLinkWaveAtUtc).TotalSeconds < 3.0)
            {
                return false;
            }
            // 直前URLの前方一致（途中キャプチャ→全文）も同一リンクとみなす
            if (!string.IsNullOrEmpty(LastLinkWaveUrl)
                && (now - LastLinkWaveAtUtc).TotalSeconds < 3.0
                && (url.StartsWith(LastLinkWaveUrl, StringComparison.Ordinal)
                    || LastLinkWaveUrl.StartsWith(url, StringComparison.Ordinal)))
            {
                LastLinkWaveUrl = url.Length >= LastLinkWaveUrl.Length ? url : LastLinkWaveUrl;
                return false;
            }

            LastLinkWaveUrl = url;
            LastLinkWaveAtUtc = now;
            return true;
        }

        public MainWindow()
        {
            //前のバージョンのプロパティを引き継ぐぜ。
            Settings.Default.Upgrade();

            InitializeComponent();

            aiPromptStore.EnsureUserFiles();

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

            ChatInputCombo.Items.Add("/ai 無人モードのトグル");

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
            DisableAiMode();

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

            // VoiceVox が使える場合のみウォームアップ
            if (Settings.Default.UseVoiceVox
                && !string.IsNullOrEmpty(Settings.Default.VoiceVoxPath)
                && File.Exists(Settings.Default.VoiceVoxPath))
            {
                TargetProcess tp = new("run");
                if (tp.IsAlive)
                {
                    _ = VoiceVoxWarmUp();
                }
            }

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
                if (chatList1 is null || !TryReadLastChatRow(chatList1, twDivision, twName, twTime, twMessage, twControl, out _, out _, out oldMessage))
                {
                    oldMessage = "";
                    firstFlg = true;
                }
                else
                {
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
                        AutomationElement? elInvitation = twInvitations.GetFirstChild(app);
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
                                        _ = SpeechMessageAsync(item.UserName, item.Message);
                                    }
                                }
                                invitationFlg = true;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        msg = $"何かエラーです。[{e.Message}] {DateTime.Now}";
                        invitationFlg = false;
                        await Task.Delay(500);
                    }

                    try
                    {
                        //メンバーの削除＆追加（毎回やる割には問題なさそう）
                        if (yourSelf is not null)
                        {
#nullable disable warnings
                            var roomMember = twDivision.GetNextSibling(yourSelf);

                            for (int i = (MainVM.Members.Count) - (1); i >= 1; i--)
                            {
                                MainVM.Members.RemoveAt(i);
                            }

                            var seenThisPoll = new HashSet<string>(StringComparer.Ordinal);
                            if (!string.IsNullOrEmpty(yourName))
                            {
                                seenThisPoll.Add(yourName);
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
                                if (tempNameText != null)
                                {
                                    try
                                    {
                                        if (tempNameText.Current.Name != null)
                                        {
                                            item.MemberName = tempNameText.Current.Name;
                                        }
                                    }
                                    catch (ElementNotAvailableException)
                                    {
                                        // 要素が無効化されている場合の処理
                                        new ToastContentBuilder()
                                            .AddText($"メンバー情報取得エラーです。")
                                            .Show();
                                        break;
                                    }
                                }
 
                                if (tempPartText.Current.Name != null)
                                {
                                    item.MemberPart = tempPartText.Current.Name;
                                }
                                item.MemberImage = new()
                                {
                                    Source = bitmapSource
                                };

                                if (!string.IsNullOrEmpty(item.MemberName))
                                {
                                    seenThisPoll.Add(item.MemberName);
                                }

                                MainVM.Members.Add(item);
#nullable restore
                                roomMember = twDivision.GetNextSibling(roomMember);
                            }

                            NoticeNewRoomMembersIfNeeded(seenThisPoll);
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
                        if (!TryReadLastChatRow(chatList, twDivision, twName, twTime, twMessage, twControl,
                                out string rowUserName, out string rowTime, out string capturedMessage))
                        {
                            continue;
                        }
                        if (string.IsNullOrEmpty(capturedMessage)) { continue; }

                        // 混入っぽいときだけサニタイズ（毎周の正規表現コストを避ける）
                        if (LooksLikePollutedCapture(capturedMessage, rowUserName, rowTime, oldMessage))
                        {
                            capturedMessage = SanitizeCapturedMessage(capturedMessage, rowUserName, rowTime, oldMessage);
                        }

                        //string chatLine = $"{rowUserName} {rowTime} {capturedMessage}";
                        string chatLine = $"[{rowTime}] {capturedMessage}";

                        if ((capturedMessage != oldMessage) || (firstFlg))
                        {
                            firstFlg = false;
                            //MainVM.Info.ChatLog += Environment.NewLine + chatLine;

                            string Message = capturedMessage;

                            //リンク自動オープン時の処理。
                            bool IsLink = false;
                            string url = "";
                            string extractedUrl = ExtractHttpUrl(Message);
                            if (!string.IsNullOrEmpty(extractedUrl))
                            {
                                IsLink = true;
                                Message = "リンクが張られました";
                                url = extractedUrl;
                                // 自動オープン（同一URLの再オープン防止は TryOpenChatLink 内）
                                TryOpenChatLink(extractedUrl, delayMs: 500);
                            }

                            //チャット風表示
                            bool IsYourSelf = false;
                            if (rowUserName == yourName)
                            {
                                IsYourSelf = true;
                            }

                            bool RandChat = IsYourSelf;

                            if (DemoMode)
                            {
                                var random = new Random();

                                RandChat = random.Next(2) == 1;
                            }

                            string rawMessage = capturedMessage;
                            string speakerName = rowUserName ?? "";
                            string displayMessage = rawMessage;
                            bool fromAiBot = false;
                            string? humanEchoFull = null;
                            bool skipChatLogAdd = false;

                            // UIA 欠落時の補完（送信時に控えた全文）
                            if (IsYourSelf)
                            {
                                if (TryConsumeHumanEcho(rawMessage, out humanEchoFull))
                                {
                                    displayMessage = humanEchoFull;
                                    // アプリ入力欄で既に一覧へ出している分は二重追加しない
                                    skipChatLogAdd = TryConsumeOptimisticLocalChat(humanEchoFull);
                                }
                                else if (TryConsumeAiSentMessage(rawMessage, out var aiFull))
                                {
                                    displayMessage = aiFull;
                                    fromAiBot = true;
                                }
                                else if (aiUnmannedMode)
                                {
                                    // 無人中の自分発言で手打ち控が無い＝AI側エコーとみなす（誤再応答防止）
                                    fromAiBot = true;
                                }
                            }

                            // 表示用にリンク情報を全文から再判定
                            if (!IsLink)
                            {
                                extractedUrl = ExtractHttpUrl(displayMessage);
                                if (!string.IsNullOrEmpty(extractedUrl))
                                {
                                    IsLink = true;
                                    url = extractedUrl;
                                }
                            }

                            if (!skipChatLogAdd)
                            {
                                var item = new Chat
                                {
                                    ChatTime = rowTime,
                                    UserName = rowUserName,
                                    Message = displayMessage,
                                    IsYourSelf = RandChat,
                                    Link = url,
                                    IsLink = IsLink
                                };

                                Application.Current.Dispatcher.BeginInvoke(() => MainVM.Chats.Add(item));
                            }

                            // 起動者専用 /ai コマンド（読み上げ対象外・会話対象外）
                            if (IsYourSelf && aiModeReg().IsMatch(displayMessage.Trim()))
                            {
                                var aiMatch = aiModeReg().Match(displayMessage.Trim());
                                string? force = aiMatch.Groups[1].Success ? aiMatch.Groups[1].Value.ToLowerInvariant() : null;
                                // モード切替で監視を長時間止めない
                                _ = HandleAiModeCommandAsync(force);
                                oldMessage = rawMessage;
                                continue;
                            }

                            // 無人AI: 他人の通常チャット、またはアプリからの手打ちエコーのみ対象
                            // http/https を含む・httpで始まる投稿は返答対象外
                            bool skipAiForHttp = IsHttpRelatedChat(displayMessage);
                            if (aiUnmannedMode && !fromAiBot && !displayMessage.TrimStart().StartsWith('/') && !skipAiForHttp)
                            {
                                if (!IsYourSelf || humanEchoFull is not null)
                                {
                                    EnqueueAiMessage(speakerName, displayMessage);
                                }
                            }

                            // AIが送った文はワーカー側で読むので二重読み上げしない
                            bool skipSpeech = fromAiBot;

                            // 先に oldMessage を進め、読み上げ待ちで新規チャットを取りこぼさない
                            oldMessage = rawMessage;
                            msg = aiUnmannedMode
                                ? $"監視中…（無人AI） {DateTime.Now}"
                                : $"監視中… {DateTime.Now}";

                            if (Settings.Default.CanSpeech && !skipSpeech)
                            {
                                // 読み上げは表示用の全文を使う（欠落した UIA 文字列で切らない）
                                string speechSource = IsLink ? Message : displayMessage;
                                if (IsLink)
                                {
                                    msg = "リンクが張られました。";

                                    //リンク固定ファイル再生時
                                    if (!string.IsNullOrEmpty(Settings.Default.LinkWaveFilePath))
                                    {
                                        if (Path.Exists(Settings.Default.LinkWaveFilePath))
                                        {
                                            // UIAの分割更新で多重再生しない。再生は SpeechGate 経由で排他。
                                            if (TryClaimLinkWavePlay(url))
                                            {
                                                _ = PlayWavGatedAsync(Settings.Default.LinkWaveFilePath);
                                            }
                                            continue;
                                        }
                                    }
                                }
                                if (!string.IsNullOrEmpty(speechSource))
                                {
                                    // 監視ループは待たない（読み上げ中でも次のチャットをキャプチャする）
                                    string speechName = speakerName;
                                    string speechMsg = speechSource;
                                    _ = SpeechMessageAsync(speechName, speechMsg);
                                }
                            }
                        }
                        else
                        {
                            oldMessage = capturedMessage;
                            msg = aiUnmannedMode
                                ? $"監視中…（無人AI） {DateTime.Now}"
                                : $"監視中… {DateTime.Now}";
                        }
                    }
                    catch (Exception e)
                    {
                        msg = $"何かエラーです。[{e.Message}] {DateTime.Now}";
                        // break すると部屋監視ごと止まるので継続する
                        await Task.Delay(500);
                    }
                }
            }
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
        }

        /// <summary>
        /// message 要素配下のテキスト／リンクを結合して全文を取得する。
        /// ハイパーリンクがあると末尾断片だけが Name になることがある。
        /// </summary>
        private static string ReadChatMessageText(AutomationElement messageElement)
        {
            try
            {
                var parts = new List<string>();
                CollectLeafTexts(messageElement, parts, 0);

                if (parts.Count == 0)
                {
                    return messageElement.Current.Name?.Trim() ?? "";
                }

                // 隣接重複を除去して連結
                var sb = new StringBuilder();
                string? last = null;
                foreach (var part in parts)
                {
                    if (string.IsNullOrWhiteSpace(part)) { continue; }
                    if (last is not null && (part == last || last.EndsWith(part, StringComparison.Ordinal) || part.StartsWith(last, StringComparison.Ordinal)))
                    {
                        if (part.Length > last.Length)
                        {
                            sb.Length = Math.Max(0, sb.Length - last.Length);
                            sb.Append(part);
                            last = part;
                        }
                        continue;
                    }
                    sb.Append(part);
                    last = part;
                }

                string joined = sb.ToString().Trim();
                return string.IsNullOrEmpty(joined) ? (messageElement.Current.Name?.Trim() ?? "") : joined;
            }
            catch
            {
                return messageElement.Current.Name?.Trim() ?? "";
            }
        }

        /// <summary>
        /// chatList 直下の最後の division（1チャット行）から name/time/message を揃えて読む。
        /// 行を跨いだ last-child 取り違えや UIA 混入を減らす。
        /// </summary>
        private static bool TryReadLastChatRow(
            AutomationElement chatList,
            TreeWalker twDivision,
            TreeWalker twName,
            TreeWalker twTime,
            TreeWalker twMessage,
            TreeWalker twControl,
            out string userName,
            out string chatTime,
            out string message)
        {
            userName = "";
            chatTime = "";
            message = "";
            if (chatList is null) { return false; }

            try
            {
                AutomationElement? row = twDivision.GetLastChild(chatList);
                AutomationElement? elName;
                AutomationElement? elTime;
                AutomationElement? elMessage;

                if (row is not null)
                {
                    elName = twName.GetFirstChild(row) ?? twName.GetLastChild(row);
                    elTime = twTime.GetFirstChild(row) ?? twTime.GetLastChild(row);
                    elMessage = twMessage.GetFirstChild(row) ?? twMessage.GetLastChild(row);
                }
                else
                {
                    // 行ラッパーが無いレイアウト向けフォールバック
                    elName = twName.GetLastChild(chatList);
                    elTime = twTime.GetLastChild(chatList);
                    elMessage = twMessage.GetLastChild(chatList);
                }

                if (elMessage is null) { return false; }

                if (elName is not null)
                {
                    var nameText = twControl.GetLastChild(elName) ?? elName;
                    try { userName = nameText.Current.Name?.Trim() ?? ""; }
                    catch { userName = ""; }
                }

                if (elTime is not null)
                {
                    var timeText = twControl.GetLastChild(elTime) ?? elTime;
                    try { chatTime = timeText.Current.Name?.Trim() ?? ""; }
                    catch { chatTime = ""; }
                }

                message = ReadChatMessageText(elMessage);
                return !string.IsNullOrEmpty(message);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// SyncRoom UIA が前発言・「ユーザーアイコン」・名前・時刻を本文先頭に連結することがある。
        /// </summary>
        private static bool LooksLikePollutedCapture(string captured, string userName, string chatTime, string previousMessage)
        {
            if (string.IsNullOrEmpty(captured)) { return false; }
            if (captured.Contains("ユーザーアイコン", StringComparison.Ordinal)) { return true; }
            if (!string.IsNullOrEmpty(previousMessage)
                && captured.StartsWith(previousMessage, StringComparison.Ordinal)
                && captured.Length > previousMessage.Length + 3)
            {
                return true;
            }
            if (!string.IsNullOrEmpty(userName)
                && !string.IsNullOrEmpty(chatTime)
                && captured.Contains(userName + chatTime, StringComparison.Ordinal))
            {
                return true;
            }
            return false;
        }

        private static string SanitizeCapturedMessage(string captured, string userName, string chatTime, string previousMessage)
        {
            if (string.IsNullOrEmpty(captured)) { return captured; }

            string text = captured;

            if (!string.IsNullOrEmpty(previousMessage)
                && text.StartsWith(previousMessage, StringComparison.Ordinal)
                && text.Length > previousMessage.Length)
            {
                text = text[previousMessage.Length..];
            }

            text = text.Replace("ユーザーアイコン", "", StringComparison.Ordinal);

            if (!string.IsNullOrEmpty(userName))
            {
                // "Bangdoll20:00:58" / "Bangdoll 20:00:58"（行の time とずれていても名前+時刻パターンで切る）
                var nameTime = Regex.Match(
                    text,
                    Regex.Escape(userName) + @"\s*\d{1,2}:\d{2}(?::\d{2})?");
                if (nameTime.Success)
                {
                    text = text[(nameTime.Index + nameTime.Length)..];
                }
                else if (!string.IsNullOrEmpty(chatTime))
                {
                    string marker = userName + chatTime;
                    string markerSp = userName + " " + chatTime;
                    int idx = text.LastIndexOf(marker, StringComparison.Ordinal);
                    int markerLen = marker.Length;
                    if (idx < 0)
                    {
                        idx = text.LastIndexOf(markerSp, StringComparison.Ordinal);
                        markerLen = markerSp.Length;
                    }
                    if (idx >= 0)
                    {
                        text = text[(idx + markerLen)..];
                    }
                }
                else if (text.StartsWith(userName, StringComparison.Ordinal))
                {
                    text = text[userName.Length..];
                }
            }

            // 先頭の時刻 HH:mm:ss
            if (!string.IsNullOrEmpty(chatTime) && text.StartsWith(chatTime, StringComparison.Ordinal))
            {
                text = text[chatTime.Length..];
            }
            else
            {
                var timePrefix = Regex.Match(text, @"^\s*\d{1,2}:\d{2}(?::\d{2})?\s*");
                if (timePrefix.Success)
                {
                    text = text[timePrefix.Length..];
                }
            }

            return text.Trim();
        }

        private static void CollectLeafTexts(AutomationElement node, List<string> parts, int depth)
        {
            if (node is null || depth > 10) { return; }

            var walker = TreeWalker.ControlViewWalker;
            AutomationElement? child = null;
            try
            {
                child = walker.GetFirstChild(node);
            }
            catch
            {
                child = null;
            }

            if (child is null)
            {
                try
                {
                    string? name = node.Current.Name;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        parts.Add(name);
                    }
                    else if (node.TryGetCurrentPattern(ValuePattern.Pattern, out object vpObj)
                             && vpObj is ValuePattern vp
                             && !string.IsNullOrWhiteSpace(vp.Current.Value))
                    {
                        parts.Add(vp.Current.Value);
                    }
                }
                catch
                {
                    // ignore single node
                }
                return;
            }

            for (AutomationElement? c = child; c is not null; c = walker.GetNextSibling(c))
            {
                CollectLeafTexts(c, parts, depth + 1);
            }
        }

        private static bool IsHttpRelatedChat(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) { return false; }
            string t = text.TrimStart();
            if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return httpReg().IsMatch(text);
        }

        private static string StripHttpUrls(string text)
        {
            if (string.IsNullOrEmpty(text)) { return text; }
            string stripped = Regex.Replace(text, @"https?://\S+", "", RegexOptions.IgnoreCase);
            return Regex.Replace(stripped, @"\s{2,}", " ").Trim();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow();
            settingsWindow.ShowDialog();
        }

        private void ChatInputCombo_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Return) { return; }

            string text = ChatInputCombo.Text;
            if (string.IsNullOrWhiteSpace(text)) { return; }

            if (SendChatMessage(text, fromHuman: true))
            {
                bool existFlg = false;
                foreach (var item in ChatInputCombo.Items)
                {
                    if (item.ToString() == text)
                    {
                        existFlg = true;
                        break;
                    }
                }

                if (!existFlg)
                {
                    ChatInputCombo.Items.Add(text);
                }

                ChatInputCombo.Text = "";
                this.Activate();
            }
        }

        /// <summary>
        /// SyncRoom のチャット入力へ投稿する（手動入力・AI共用）。
        /// UI Automation はバックグラウンドからも呼ぶ（Dispatcher.Invoke すると読み上げ中にデッドロックする）。
        /// fromHuman: アプリ入力欄からの手打ち。エコーを AI 会話対象にするための控える。
        /// </summary>
        private bool SendChatMessage(string text, bool fromHuman = false)
        {
            if (studio is null || string.IsNullOrWhiteSpace(text)) { return false; }

            try
            {
                TreeWalker twChat = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "chat"));
                AutomationElement chat = twChat.GetFirstChild(studio);
                if (chat is null)
                {
                    twChat = new(new PropertyCondition(AutomationElement.AutomationIdProperty, "docked-chat"));
                    chat = twChat.GetFirstChild(rootWebArea);
                    if (chat is null) { return false; }
                }

                TreeWalker twChatInput = new(new PropertyCondition(AutomationElement.ClassNameProperty, "chat-input-backgraund d-flex floatable"));
                AutomationElement chatInputBackground = twChatInput.GetFirstChild(chat);
                if (chatInputBackground is null) { return false; }

                TreeWalker twEdit = new(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
                AutomationElement EditBox = twEdit.GetLastChild(chatInputBackground);
                if (EditBox is null) { return false; }

                int cnt = 0;
                while (EditBox.Current.Name is not "チャット")
                {
                    cnt++;
                    EditBox = twEdit.GetLastChild(EditBox);
                    if (cnt >= 3)
                    {
                        break;
                    }
                }

                TreeWalker twButton = new(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
                AutomationElement EditButton = twButton.GetLastChild(chatInputBackground);
                if (EditButton is null) { return false; }

                if (!EditBox.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePattern))
                {
                    return false;
                }

                ((ValuePattern)valuePattern).SetValue(text);

                if (EditButton.GetCurrentPattern(InvokePattern.Pattern) is InvokePattern btn)
                {
                    btn.Invoke();
                }

                ((ValuePattern)valuePattern).SetValue(" ");

                if (fromHuman)
                {
                    RememberHumanEcho(text);
                    RememberOptimisticLocalChat(text);
                    AddLocalChatLog(text, isYourSelf: true);
                    // アプリ入力からのリンクは UIA 待ちにせずここで開く（AIモードでも漏れない）
                    TryOpenChatLink(ExtractHttpUrl(text), delayMs: 500);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SendChatMessage failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// チャット本文から先頭の http(s) URL を抜き出す。無ければ空文字。
        /// </summary>
        private static string ExtractHttpUrl(string text)
        {
            if (string.IsNullOrEmpty(text)) { return ""; }
            var match = httpReg().Match(text);
            if (!match.Success) { return ""; }
            string uriString = text[match.Index..];
            int sp = uriString.IndexOfAny([' ', '　', '\n', '\r', '。']);
            if (sp > 0)
            {
                uriString = uriString[..sp];
            }
            return uriString.Trim();
        }

        /// <summary>
        /// リンク自動オープン。成功時のみ LastURL を更新する。
        /// UI スレッド経由で開き、再生とぶつからないよう任意で遅延する。
        /// </summary>
        private void TryOpenChatLink(string? uriString, int delayMs = 0)
        {
            if (!Settings.Default.OpenLink) { return; }
            if (string.IsNullOrWhiteSpace(uriString)) { return; }

            if (!Uri.TryCreate(uriString, UriKind.Absolute, out var uri)) { return; }
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) { return; }

            string normalized = uri.AbsoluteUri;
            if (string.Equals(normalized, LastURL, StringComparison.Ordinal)
                || string.Equals(uriString, LastURL, StringComparison.Ordinal))
            {
                return;
            }

            // オープンを予約した時点で記録し、UIA の二重オープンを防ぐ
            LastURL = normalized;

            string openUrl = normalized;
            async Task OpenAsync()
            {
                try
                {
                    if (delayMs > 0)
                    {
                        await Task.Delay(delayMs).ConfigureAwait(true);
                    }
                    Tools.OpenUrl(openUrl);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"TryOpenChatLink failed: {ex.Message}");
                    // 失敗したら再試行できるよう戻す
                    if (string.Equals(LastURL, openUrl, StringComparison.Ordinal))
                    {
                        LastURL = "";
                    }
                }
            }

            if (Dispatcher.CheckAccess())
            {
                _ = OpenAsync();
            }
            else
            {
                _ = Dispatcher.InvokeAsync(async () => await OpenAsync());
            }
        }

        private void NoticeNewRoomMembersIfNeeded(HashSet<string> seenThisPoll)
        {
            foreach (var name in seenThisPoll)
            {
                if (string.IsNullOrWhiteSpace(name)) { continue; }
                bool isNew = knownRoomMemberNames.Add(name);
                if (!isNew) { continue; }

                // 初回スキャンは在室者の基準にするだけ。無人中の「増員」だけ挨拶する
                if (!roomMemberBaselineReady) { continue; }
                if (!aiUnmannedMode) { continue; }
                if (string.Equals(name, yourName, StringComparison.Ordinal)) { continue; }

                AnnounceAiNewMemberGreeting(name);
            }

            if (!roomMemberBaselineReady && knownRoomMemberNames.Count > 0)
            {
                roomMemberBaselineReady = true;
            }
        }

        /// <summary>
        /// 無人モード中にメンバーが増えたときの短い自己紹介。
        /// </summary>
        private void AnnounceAiNewMemberGreeting(string memberName)
        {
            string text = aiPromptStore.BuildGreeting(memberName);
            if (string.IsNullOrWhiteSpace(text)) { return; }

            RememberAiSentMessage(text);
            SendChatMessage(text);
        }

        private void EnqueueAiMessage(string userName, string message)
        {
            var queue = aiQueue;
            if (queue is null || !aiUnmannedMode) { return; }
            queue.Writer.TryWrite((userName, message));
        }

        private void RememberHumanEcho(string text)
        {
            if (string.IsNullOrEmpty(text)) { return; }
            lock (pendingHumanEchoes)
            {
                pendingHumanEchoes.Enqueue(text);
                while (pendingHumanEchoes.Count > 20)
                {
                    pendingHumanEchoes.Dequeue();
                }
            }
        }

        private void RememberOptimisticLocalChat(string text)
        {
            if (string.IsNullOrEmpty(text)) { return; }
            lock (optimisticLocalChats)
            {
                optimisticLocalChats.Enqueue(text);
                while (optimisticLocalChats.Count > 20)
                {
                    optimisticLocalChats.Dequeue();
                }
            }
        }

        private bool TryConsumeOptimisticLocalChat(string text)
        {
            if (string.IsNullOrEmpty(text)) { return false; }
            lock (optimisticLocalChats)
            {
                if (optimisticLocalChats.Count == 0) { return false; }
                var list = optimisticLocalChats.ToList();
                int idx = list.FindIndex(p =>
                    string.Equals(p, text, StringComparison.Ordinal)
                    || HumanEchoMatches(text, p));
                if (idx < 0) { return false; }
                list.RemoveAt(idx);
                optimisticLocalChats.Clear();
                foreach (var item in list)
                {
                    optimisticLocalChats.Enqueue(item);
                }
                return true;
            }
        }

        private void AddLocalChatLog(string message, bool isYourSelf)
        {
            string url = ExtractHttpUrl(message);
            bool isLink = !string.IsNullOrEmpty(url);

            var item = new Chat
            {
                ChatTime = DateTime.Now.ToString("HH:mm:ss"),
                UserName = string.IsNullOrEmpty(yourName) ? "You" : yourName,
                Message = isLink ? "リンクが張られました" : message,
                IsYourSelf = isYourSelf,
                Link = url,
                IsLink = isLink
            };

            void Add()
            {
                MainVM.Chats.Add(item);
            }

            if (Dispatcher.CheckAccess())
            {
                Add();
            }
            else
            {
                Dispatcher.BeginInvoke(Add);
            }
        }

        private bool TryConsumeHumanEcho(string capturedText, out string fullText)
        {
            lock (pendingHumanEchoes)
            {
                if (pendingHumanEchoes.Count == 0)
                {
                    fullText = "";
                    return false;
                }

                var list = pendingHumanEchoes.ToList();
                int idx = list.FindIndex(p => HumanEchoMatches(capturedText, p));
                if (idx < 0)
                {
                    fullText = "";
                    return false;
                }

                fullText = list[idx];
                list.RemoveAt(idx);
                pendingHumanEchoes.Clear();
                foreach (var item in list)
                {
                    pendingHumanEchoes.Enqueue(item);
                }
                return true;
            }
        }

        private static bool HumanEchoMatches(string captured, string pending)
        {
            if (string.IsNullOrEmpty(pending)) { return false; }
            string c = (captured ?? "").Trim();
            string p = pending.Trim();
            if (c.Length == 0) { return false; }
            if (string.Equals(c, p, StringComparison.Ordinal)) { return true; }
            // UIA 欠落: 控全文の先頭 or 末尾がキャプチャと一致
            if (c.Length >= 4 && (p.StartsWith(c, StringComparison.Ordinal) || p.EndsWith(c, StringComparison.Ordinal)))
            {
                return true;
            }
            // 逆にキャプチャが長い（混入）場合は控が末尾に含まれる
            if (p.Length >= 8 && c.EndsWith(p, StringComparison.Ordinal))
            {
                return true;
            }
            int take = Math.Min(12, Math.Min(c.Length, p.Length));
            return take >= 6 && string.Equals(c[..take], p[..take], StringComparison.Ordinal);
        }

        private void RememberAiSentMessage(string text)
        {
            if (string.IsNullOrEmpty(text)) { return; }
            lock (aiSentMessages)
            {
                aiSentMessages.Add(text);
                // 古いものを捨てて肥大化防止
                if (aiSentMessages.Count > 30)
                {
                    aiSentMessages.Clear();
                    aiSentMessages.Add(text);
                }
            }
        }

        private bool TryConsumeAiSentMessage(string capturedText, out string fullText)
        {
            fullText = "";
            if (string.IsNullOrEmpty(capturedText)) { return false; }
            string normalized = capturedText.Trim();
            lock (aiSentMessages)
            {
                if (aiSentMessages.Remove(capturedText))
                {
                    fullText = capturedText;
                    return true;
                }
                if (aiSentMessages.Remove(normalized))
                {
                    fullText = normalized;
                    return true;
                }

                // UIA 欠落（()[]・httpリンク分割等）向けに部分一致で全文を返す
                // 混入キャプチャ向け: 控えた AI 全文が末尾にある場合もヒット
                string? hit = null;
                foreach (var sent in aiSentMessages)
                {
                    if (normalized.Length >= 4 && sent.EndsWith(normalized, StringComparison.Ordinal))
                    {
                        hit = sent;
                        break;
                    }
                    if (normalized.Length >= 8 && sent.StartsWith(normalized, StringComparison.Ordinal))
                    {
                        hit = sent;
                        break;
                    }
                    if (sent.Length >= 8 && normalized.StartsWith(sent, StringComparison.Ordinal))
                    {
                        hit = sent;
                        break;
                    }
                    if (sent.Length >= 8 && normalized.EndsWith(sent, StringComparison.Ordinal))
                    {
                        hit = sent;
                        break;
                    }
                    // 先頭が一致していれば欠落キャプチャとみなす
                    int take = Math.Min(12, Math.Min(normalized.Length, sent.Length));
                    if (take >= 6 && string.Equals(normalized[..take], sent[..take], StringComparison.Ordinal))
                    {
                        hit = sent;
                        break;
                    }
                }
                if (hit is not null)
                {
                    aiSentMessages.Remove(hit);
                    fullText = hit;
                    return true;
                }
            }
            return false;
        }

        private bool ConsumeAiSentMessage(string text)
        {
            return TryConsumeAiSentMessage(text, out _);
        }

        /// <summary>
        /// force: null=トグル, "on", "off"
        /// </summary>
        private async Task HandleAiModeCommandAsync(string? force)
        {
            lock (aiLock)
            {
                if (aiToggleBusy) { return; }
                aiToggleBusy = true;
            }

            try
            {
                bool turnOn = force switch
                {
                    "on" => true,
                    "off" => false,
                    _ => !aiUnmannedMode
                };

                if (turnOn == aiUnmannedMode)
                {
                    // 既に同じ状態なら何もしない（再アナウンスしない）
                    return;
                }

                if (turnOn)
                {
                    await TryEnableAiModeAsync();
                }
                else
                {
                    DisableAiMode();
                    RememberAiSentMessage("無人モードを終了しました");
                    SendChatMessage("無人モードを終了しました");
                }
            }
            finally
            {
                lock (aiLock) { aiToggleBusy = false; }
            }
        }

        private async Task TryEnableAiModeAsync()
        {
            string apiKey = Settings.Default.GeminiApiKey?.Trim() ?? "";
            if (string.IsNullOrEmpty(apiKey))
            {
                ShowAiError("Gemini APIキーが未設定です。設定画面で入力してから /ai を実行してください。");
                return;
            }

            var (repoOk, repoError) = await repoKnowledge.EnsureLoadedAsync().ConfigureAwait(true);
            if (!repoOk)
            {
                // GitHub レート制限でも無人モード自体は開始する（雑談は可能）
                ShowAiWarning(
                    "GitHub リポジトリ情報を取得できませんでした。\n" +
                    "無人モードは開始しますが、リポジトリ案内は弱くなります。\n\n" +
                    repoError);
            }
            else if (!string.IsNullOrEmpty(repoKnowledge.LastError))
            {
                SetAiStatus($"無人AI: {repoKnowledge.LastError}");
            }

            aiPromptStore.EnsureUserFiles();

            var (pingOk, pingError) = await geminiClient.PingAsync(apiKey).ConfigureAwait(true);
            if (!pingOk)
            {
                ShowAiError(pingError);
                return;
            }

            StartAiWorker();
            aiUnmannedMode = true;
            RememberAiSentMessage("無人モードを開始しました");
            SendChatMessage("無人モードを開始しました");
        }

        private void DisableAiMode()
        {
            aiUnmannedMode = false;
            StopAiWorker();
            lock (aiHistory)
            {
                aiHistory.Clear();
            }
            lock (aiSentMessages)
            {
                aiSentMessages.Clear();
            }
            lock (pendingHumanEchoes)
            {
                pendingHumanEchoes.Clear();
            }
            lock (optimisticLocalChats)
            {
                optimisticLocalChats.Clear();
            }
        }

        private void StartAiWorker()
        {
            StopAiWorker();
            aiQueue = Channel.CreateUnbounded<(string UserName, string Message)>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
            aiWorkerCts = new CancellationTokenSource();
            var token = aiWorkerCts.Token;
            var queue = aiQueue;
            aiWorkerTask = Task.Run(() => AiWorkerLoopAsync(queue, token), token);
        }

        private void StopAiWorker()
        {
            try
            {
                aiWorkerCts?.Cancel();
                aiQueue?.Writer.TryComplete();
            }
            catch
            {
                // ignore
            }

            aiWorkerCts?.Dispose();
            aiWorkerCts = null;
            aiQueue = null;
            aiWorkerTask = null;
        }

        private async Task AiWorkerLoopAsync(Channel<(string UserName, string Message)> queue, CancellationToken token)
        {
            try
            {
                await foreach (var (userName, message) in queue.Reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    if (!aiUnmannedMode) { break; }

                    string apiKey = Settings.Default.GeminiApiKey?.Trim() ?? "";
                    if (string.IsNullOrEmpty(apiKey))
                    {
                        SetAiStatus("無人AI: APIキーが空です");
                        continue;
                    }

                    SetAiStatus($"無人AI: 応答生成中… ({userName})");

                    string system = repoKnowledge.BuildSystemInstruction(aiPromptStore.LoadSystemInstruction());
                    List<(string Role, string Text)> historySnapshot;
                    lock (aiHistory)
                    {
                        historySnapshot = [.. aiHistory];
                    }

                    string userPrompt = $"{userName}: {message}";
                    var (ok, reply, error) = await geminiClient.GenerateAsync(apiKey, system, historySnapshot, userPrompt, token).ConfigureAwait(false);
                    if (!ok || string.IsNullOrWhiteSpace(reply))
                    {
                        SetAiStatus(string.IsNullOrEmpty(error) ? "無人AI: 応答生成に失敗" : $"無人AI: {error}");
                        Debug.WriteLine($"AI generate failed: {error}");
                        continue;
                    }

                    // UIA 分割・誤応答を避けるため URL・括弧はアプリ側で落とす
                    reply = AiPromptStore.SanitizeOutgoingChat(reply);
                    if (string.IsNullOrWhiteSpace(reply))
                    {
                        reply = "詳しい情報は GitHub の該当リポジトリを見てください。";
                    }

                    lock (aiHistory)
                    {
                        aiHistory.Add(("user", userPrompt));
                        aiHistory.Add(("model", reply));
                        // 直近のみ保持
                        while (aiHistory.Count > 12)
                        {
                            aiHistory.RemoveAt(0);
                        }
                    }

                    if (!aiUnmannedMode) { break; }

                    RememberAiSentMessage(reply);
                    bool sent = SendChatMessage(reply);
                    if (!sent)
                    {
                        ConsumeAiSentMessage(reply);
                        SetAiStatus("無人AI: チャット送信に失敗しました");
                        continue;
                    }

                    SetAiStatus($"監視中…（無人AI） {DateTime.Now}");

                    if (Settings.Default.CanSpeech)
                    {
                        // 読み上げ完了を待たず次のキューを処理する
                        string speakAs = yourName;
                        string speakText = reply;
                        _ = SpeechMessageAsync(speakAs, speakText, isAiReply: true);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常終了
            }
            catch (Exception ex)
            {
                SetAiStatus($"無人AI: ワーカー例外 {ex.Message}");
                Debug.WriteLine(ex);
            }
        }

        private void SetAiStatus(string message)
        {
            void Set()
            {
                MainVM.Info.SysInfo = message;
            }

            if (Dispatcher.CheckAccess())
            {
                Set();
            }
            else
            {
                _ = Dispatcher.BeginInvoke(Set);
            }
        }

        private void ShowAiError(string message)
        {
            SetAiStatus($"無人AI: {message}");
            void Show()
            {
                MessageBox.Show(this, message, "無人AIモード", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            if (Dispatcher.CheckAccess())
            {
                Show();
            }
            else
            {
                Dispatcher.Invoke(Show);
            }
        }

        private void ShowAiWarning(string message)
        {
            // モード開始は続行するが、ユーザーには知らせる
            ShowAiError(message);
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