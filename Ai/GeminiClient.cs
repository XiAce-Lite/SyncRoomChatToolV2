using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Text;

namespace SyncRoomChatToolV2.Ai
{
    /// <summary>
    /// Google Gemini generateContent の薄いクライアント。
    /// </summary>
    public sealed class GeminiClient
    {
        private const string Model = "gemini-3.1-flash-lite";
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
            return client;
        }

        public static string DefaultSystemInstruction { get; } =
            "あなたは YAMAHA SyncRoom の部屋にいる、気さくな話し相手兼案内役です。" +
            "丁寧語で返答してください。" +
            "雑談・あいづち・天気・季節・体調・楽器・音楽・部屋の雰囲気などは、原則1文だけ・できるだけ短く答えてください。目安は40〜70文字です。" +
            "同じ意味の言い直しや長い前置きは避け、会話が続く一言にしてください。" +
            "天気の話題では、可能なら検索結果を踏まえつつ短く触れ、感想や気遣いを一言添えてください。" +
            "「気象庁を見てください」「調べてください」だけで終わらせないでください。分からなくても会話として乗ってください。" +
            "XiAce-Lite の公開 GitHub リポジトリや SyncRoomChatToolV2 など、公開リポジトリや本ツールの説明・使い方・機能を聞かれたときだけは例外です。" +
            "その場合は与えられたリポジトリ情報を優先し、説明が主になるので必要なら2文程度まで詳しく答えて構いません。" +
            "ツールやリポジトリの事実は推測せず、分からないときは分からないと答えてください。";

        public async Task<(bool Ok, string Error)> PingAsync(string apiKey, CancellationToken cancellationToken = default)
        {
            var (ok, _, error) = await GenerateAsync(
                apiKey,
                DefaultSystemInstruction,
                [],
                "疎通確認です。「OK」とだけ返してください。",
                cancellationToken).ConfigureAwait(false);
            return (ok, error);
        }

        public async Task<(bool Ok, string Text, string Error)> GenerateAsync(
            string apiKey,
            string systemInstruction,
            IReadOnlyList<(string Role, string Text)> history,
            string userMessage,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return (false, "", "Gemini APIキーが未設定です。設定画面で入力してください。");
            }

            bool allowDetail = LooksLikeRepoOrToolQuestion(userMessage);

            // 雑談・天気向けに Google 検索を試し、使えなければ通常生成へフォールバック
            var withSearch = await GenerateCoreAsync(apiKey, systemInstruction, history, userMessage, useGoogleSearch: true, allowDetail, cancellationToken).ConfigureAwait(false);
            if (withSearch.Ok)
            {
                return withSearch;
            }

            return await GenerateCoreAsync(apiKey, systemInstruction, history, userMessage, useGoogleSearch: false, allowDetail, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 公開リポジトリ／本ツールの説明を求めているかどうか。
        /// </summary>
        public static bool LooksLikeRepoOrToolQuestion(string userMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage)) { return false; }
            string t = userMessage;
            // ユーザー名付き "Name: text" の場合は本文側も見る
            int colon = t.IndexOf(':');
            if (colon >= 0 && colon < 40)
            {
                t = t[(colon + 1)..];
            }

            string[] strongKeys =
            [
                "github", "リポジトリ", "レポジトリ", "repo", "README",
                "XiAce", "SyncRoomChatTool", "読み上げちゃん",
                "どういうツール", "何ができる",
                "インストール", "セットアップ", "設定方法"
            ];
            foreach (var key in strongKeys)
            {
                if (t.Contains(key, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // 「使い方」「説明」系はツール／リポジトリ文脈があるときだけ
            bool askExplain = t.Contains("使い方", StringComparison.Ordinal)
                || t.Contains("説明", StringComparison.Ordinal)
                || t.Contains("教えて", StringComparison.Ordinal)
                || t.Contains("機能", StringComparison.Ordinal);
            if (askExplain
                && (t.Contains("ツール", StringComparison.Ordinal)
                    || t.Contains("アプリ", StringComparison.Ordinal)
                    || t.Contains("ソフト", StringComparison.Ordinal)
                    || t.Contains("読み上げ", StringComparison.Ordinal)
                    || t.Contains("XiAce", StringComparison.OrdinalIgnoreCase)
                    || t.Contains("github", StringComparison.OrdinalIgnoreCase)
                    || t.Contains("リポジトリ", StringComparison.OrdinalIgnoreCase)
                    || t.Contains("レポジトリ", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
            return false;
        }

        private static async Task<(bool Ok, string Text, string Error)> GenerateCoreAsync(
            string apiKey,
            string systemInstruction,
            IReadOnlyList<(string Role, string Text)> history,
            string userMessage,
            bool useGoogleSearch,
            bool allowDetail,
            CancellationToken cancellationToken)
        {
            var contents = new JArray();
            foreach (var (role, text) in history)
            {
                if (string.IsNullOrWhiteSpace(text)) { continue; }
                contents.Add(new JObject
                {
                    ["role"] = role == "model" ? "model" : "user",
                    ["parts"] = new JArray(new JObject { ["text"] = text })
                });
            }

            contents.Add(new JObject
            {
                ["role"] = "user",
                ["parts"] = new JArray(new JObject { ["text"] = userMessage })
            });

            var body = new JObject
            {
                ["systemInstruction"] = new JObject
                {
                    ["parts"] = new JArray(new JObject { ["text"] = systemInstruction })
                },
                ["contents"] = contents,
                ["generationConfig"] = new JObject
                {
                    ["temperature"] = allowDetail ? 0.7 : 0.9,
                    ["maxOutputTokens"] = allowDetail ? 256 : 96
                }
            };

            if (useGoogleSearch)
            {
                body["tools"] = new JArray(
                    new JObject
                    {
                        ["google_search"] = new JObject()
                    });
            }

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent";
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey.Trim());
            request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");

            try
            {
                using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var detail = TryExtractError(json);
                    return (false, "", string.IsNullOrEmpty(detail)
                        ? $"Gemini APIエラー: {(int)response.StatusCode} {response.ReasonPhrase}"
                        : $"Gemini APIエラー: {detail}");
                }

                var root = JObject.Parse(json);
                if (IsSafetyBlocked(root))
                {
                    return (true, AiPromptStore.SafetyRefusalReply, "");
                }

                var text = ExtractCandidateText(root);
                if (string.IsNullOrWhiteSpace(text))
                {
                    return (false, "", "Gemini から空の応答が返りました。");
                }

                return (true, CollapseToShortReply(text, allowDetail), "");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (false, "", $"Gemini 通信に失敗しました: {ex.Message}");
            }
        }

        private static bool IsSafetyBlocked(JObject root)
        {
            string? finish = root["candidates"]?[0]?["finishReason"]?.ToString();
            if (!string.IsNullOrEmpty(finish)
                && (string.Equals(finish, "SAFETY", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(finish, "BLOCKLIST", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(finish, "PROHIBITED_CONTENT", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            string? block = root["promptFeedback"]?["blockReason"]?.ToString();
            return !string.IsNullOrEmpty(block)
                && !string.Equals(block, "BLOCK_REASON_UNSPECIFIED", StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractCandidateText(JObject root)
        {
            var parts = root["candidates"]?[0]?["content"]?["parts"] as JArray;
            if (parts is null || parts.Count == 0)
            {
                return "";
            }

            var sb = new StringBuilder();
            foreach (var part in parts)
            {
                var t = part?["text"]?.ToString();
                if (!string.IsNullOrWhiteSpace(t))
                {
                    if (sb.Length > 0) { sb.Append(' '); }
                    sb.Append(t.Trim());
                }
            }
            return sb.ToString().Trim();
        }

        private static string TryExtractError(string json)
        {
            try
            {
                var root = JObject.Parse(json);
                return root["error"]?["message"]?.ToString() ?? "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// 長すぎる場合は文数・文字数を抑える。リポジトリ説明は従来どおり緩め。
        /// </summary>
        private static string CollapseToShortReply(string text, bool allowDetail)
        {
            text = text.Replace("\r\n", "\n").Trim();
            var parts = text.Split(['\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) { return text; }

            var joined = string.Join(" ", parts);
            int maxSentences = allowDetail ? 2 : 1;
            int maxChars = allowDetail ? 200 : 80;

            var sentences = new List<string>();
            var buffer = new StringBuilder();
            foreach (var ch in joined)
            {
                buffer.Append(ch);
                if (ch is '。' or '！' or '？' or '!' or '?')
                {
                    sentences.Add(buffer.ToString().Trim());
                    buffer.Clear();
                    if (sentences.Count >= maxSentences) { break; }
                }
            }
            if (buffer.Length > 0 && sentences.Count < maxSentences)
            {
                sentences.Add(buffer.ToString().Trim());
            }

            var result = string.Join("", sentences);
            if (result.Length > maxChars)
            {
                result = result[..maxChars].TrimEnd() + "…";
            }
            return result;
        }
    }
}
