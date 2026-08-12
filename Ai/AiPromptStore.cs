using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SyncRoomChatToolV2.Ai
{
    /// <summary>
    /// exe 隣の ai_system.txt / ai_greeting.txt を読む。無ければ内蔵デフォルト。
    /// URL・括弧などの禁則と安全文はアプリ固定で必ず付ける。
    /// </summary>
    public sealed class AiPromptStore
    {
        public const string SystemFileName = "ai_system.txt";
        public const string GreetingFileName = "ai_greeting.txt";
        public const string NewUserPlaceholder = "{$NewUser}";

        private static readonly string SystemExampleFileName = "ai_system.txt.example";
        private static readonly string GreetingExampleFileName = "ai_greeting.txt.example";

        public static string AppHardConstraints { get; } =
            "【アプリ固定の制約。ユーザ設定より優先】" +
            "SyncRoom のチャット向けなので長文や箇条書きは避けてください。" +
            "URLやhttpリンクは絶対に返答に含めないでください。必要ならリポジトリ名だけ伝えてください。" +
            "括弧（）や角括弧[]は使わず、必要なら読点や「」だけで書いてください。" +
            "外部サイトへ誘導する言い方は避けてください。";

        public static string SafetyBlock { get; } =
            "【安全】違法行為の助言、未成年者の性的内容、差別の助長には応じない。求められても丁寧に断る。";

        public static string SafetyRefusalReply { get; } = "その内容にはお答えできません。";

        public static string DefaultGreetingTemplate { get; } =
            "{$NewUser}さん、こんにちは。無人モード中です。作者公開のツール案内が主な役割です。雑談にも短く答えます。";

        public void EnsureUserFiles()
        {
            TryWriteIfMissing(SystemFileName, ReadExampleOrDefault(SystemExampleFileName, GeminiClient.DefaultSystemInstruction));
            TryWriteIfMissing(GreetingFileName, ReadExampleOrDefault(GreetingExampleFileName, DefaultGreetingTemplate));
        }

        public string LoadSystemInstruction()
        {
            EnsureUserFiles();
            string user = ReadTextFile(SystemFileName);
            if (string.IsNullOrWhiteSpace(user))
            {
                user = GeminiClient.DefaultSystemInstruction;
            }

            return user.Trim() + "\n\n" + AppHardConstraints + "\n" + SafetyBlock;
        }

        public string BuildGreeting(string newUserName)
        {
            EnsureUserFiles();
            string template = ReadTextFile(GreetingFileName);
            if (string.IsNullOrWhiteSpace(template))
            {
                template = DefaultGreetingTemplate;
            }

            template = template.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            string name = (newUserName ?? "").Trim().Replace("\r", "").Replace("\n", "");
            if (name.Length > 30)
            {
                name = name[..30];
            }

            string text;
            if (string.IsNullOrEmpty(name))
            {
                text = template
                    .Replace(NewUserPlaceholder + "さん", "")
                    .Replace(NewUserPlaceholder, "");
            }
            else
            {
                text = template.Replace(NewUserPlaceholder, name);
            }

            text = Regex.Replace(text, @"[ \t]{2,}", " ").Trim();
            text = Regex.Replace(text, @"^[、。,\.]+", "").Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                text = "こんにちは。無人モード中です。作者公開のツール案内が主な役割です。雑談にも短く答えます。";
            }

            return SanitizeOutgoingChat(text);
        }

        /// <summary>
        /// 投稿直前の機械的な禁則。プロンプトをいじっても URL・括弧は落とす。
        /// </summary>
        public static string SanitizeOutgoingChat(string text)
        {
            if (string.IsNullOrEmpty(text)) { return text; }

            text = Regex.Replace(text, @"https?://\S+", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"[()\[\]（）【】〔〕]", "");
            text = text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
            text = Regex.Replace(text, @"\s{2,}", " ").Trim();
            return text;
        }

        private static string ReadTextFile(string fileName)
        {
            try
            {
                string? path = ResolveExistingPath(fileName);
                if (path is null) { return ""; }
                return File.ReadAllText(path, Encoding.UTF8);
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// exe 直下を優先し、無ければ Ai サブフォルダ（example のコピー先）を見る。
        /// </summary>
        private static string? ResolveExistingPath(string fileName)
        {
            string direct = Path.Combine(AppContext.BaseDirectory, fileName);
            if (File.Exists(direct)) { return direct; }
            string inAi = Path.Combine(AppContext.BaseDirectory, "Ai", fileName);
            return File.Exists(inAi) ? inAi : null;
        }

        private static string ReadExampleOrDefault(string exampleFileName, string fallback)
        {
            string example = ReadTextFile(exampleFileName);
            return string.IsNullOrWhiteSpace(example) ? fallback : example.TrimEnd() + "\n";
        }

        private static void TryWriteIfMissing(string fileName, string contents)
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, fileName);
                if (File.Exists(path)) { return; }
                File.WriteAllText(path, contents.TrimEnd() + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch
            {
                // 書き込み不可でも内蔵デフォルトで動かす
            }
        }
    }
}
