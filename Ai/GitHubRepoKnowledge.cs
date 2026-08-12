using Newtonsoft.Json.Linq;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace SyncRoomChatToolV2.Ai
{
    /// <summary>
    /// XiAce-Lite 公開リポジトリの一覧と README をキャッシュする。
    /// 任意で exe 隣の github_pat.txt に PAT を置くと認証付きで取得する（UIなし）。
    /// </summary>
    public sealed class GitHubRepoKnowledge
    {
        private const string Owner = "XiAce-Lite";
        private const string PatFileName = "github_pat.txt";
        private const int MaxReadmeCharsPerRepo = 1500;
        private const int MaxTotalChars = 24000;
        private const int MaxReadmeFetchCount = 8;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

        private static readonly HttpClient Http = CreateClient();
        private static readonly string CacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SyncRoomChatToolV2");
        private static readonly string CacheFilePath = Path.Combine(CacheDirectory, "github_xiace_lite_cache.txt");

        private string _context = "";
        private bool _loaded;
        private string _lastError = "";

        public bool IsLoaded => _loaded && !string.IsNullOrWhiteSpace(_context);
        public string Context => _context;
        public string LastError => _lastError;
        public bool HasPatConfigured => !string.IsNullOrWhiteSpace(TryReadPatToken());

        private static HttpClient CreateClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SyncRoomChatToolV2/2.0 (+https://github.com/XiAce-Lite/SyncRoomChatToolV2)");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            return client;
        }

        /// <summary>
        /// exe と同じフォルダの github_pat.txt から token を読む。
        /// # 行と空行は無視。最初の有効行を PAT とする。
        /// </summary>
        public static string? TryReadPatToken()
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, PatFileName);
                if (!File.Exists(path)) { return null; }

                foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) { continue; }
                    if (trimmed.StartsWith('#')) { continue; }
                    return trimmed;
                }
            }
            catch
            {
                return null;
            }
            return null;
        }

        private static async Task<HttpResponseMessage> GetGitHubAsync(string url, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var pat = TryReadPatToken();
            if (!string.IsNullOrEmpty(pat))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pat);
            }
            return await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        public async Task<(bool Ok, string Error)> EnsureLoadedAsync(CancellationToken cancellationToken = default)
        {
            if (IsLoaded)
            {
                return (true, "");
            }

            // 新鮮なキャッシュがあれば API を叩かない
            if (TryLoadCache(requireFresh: true, out var cached))
            {
                _context = cached;
                _loaded = true;
                _lastError = "";
                return (true, "");
            }

            try
            {
                var reposUrl = $"https://api.github.com/users/{Owner}/repos?per_page=100&type=public&sort=updated";
                using var reposResponse = await GetGitHubAsync(reposUrl, cancellationToken).ConfigureAwait(false);
                var reposJson = await reposResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!reposResponse.IsSuccessStatusCode)
                {
                    // レート制限等: 古いキャッシュがあればそれで続行
                    if (TryLoadCache(requireFresh: false, out cached))
                    {
                        _context = cached;
                        _loaded = true;
                        _lastError = $"GitHub API {(int)reposResponse.StatusCode}。キャッシュを利用します。";
                        return (true, "");
                    }

                    _lastError = FormatGitHubError((int)reposResponse.StatusCode, HasPatConfigured);
                    return (false, _lastError);
                }

                var repos = JArray.Parse(reposJson);
                if (repos.Count == 0)
                {
                    if (TryLoadCache(requireFresh: false, out cached))
                    {
                        _context = cached;
                        _loaded = true;
                        return (true, "");
                    }
                    _lastError = "公開リポジトリが見つかりませんでした。";
                    return (false, _lastError);
                }

                var sb = new StringBuilder();
                sb.AppendLine($"GitHub ユーザ {Owner} の公開リポジトリ情報:");
                var successCount = 0;
                var readmeFetched = 0;

                foreach (var repo in repos)
                {
                    if (sb.Length >= MaxTotalChars) { break; }

                    var name = repo["name"]?.ToString() ?? "";
                    if (string.IsNullOrEmpty(name)) { continue; }
                    var description = repo["description"]?.ToString() ?? "";
                    var htmlUrl = repo["html_url"]?.ToString() ?? "";
                    var language = repo["language"]?.ToString() ?? "";

                    sb.AppendLine("---");
                    sb.AppendLine($"名前: {name}");
                    sb.AppendLine($"URL: {htmlUrl}");
                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        sb.AppendLine($"説明: {description}");
                    }
                    if (!string.IsNullOrWhiteSpace(language))
                    {
                        sb.AppendLine($"言語: {language}");
                    }

                    // README は件数制限（未認証レート制限対策）
                    if (readmeFetched < MaxReadmeFetchCount)
                    {
                        var readme = await TryGetReadmeAsync(name, cancellationToken).ConfigureAwait(false);
                        readmeFetched++;
                        if (!string.IsNullOrWhiteSpace(readme))
                        {
                            if (readme.Length > MaxReadmeCharsPerRepo)
                            {
                                readme = readme[..MaxReadmeCharsPerRepo] + "…";
                            }
                            sb.AppendLine("README:");
                            sb.AppendLine(readme);
                        }
                    }

                    successCount++;
                }

                if (successCount == 0)
                {
                    if (TryLoadCache(requireFresh: false, out cached))
                    {
                        _context = cached;
                        _loaded = true;
                        return (true, "");
                    }
                    _lastError = "公開リポジトリ情報を1件も取得できませんでした。";
                    return (false, _lastError);
                }

                _context = sb.ToString();
                _loaded = true;
                _lastError = HasPatConfigured ? "GitHub PAT を使用して取得しました。" : "";
                TrySaveCache(_context);
                return (true, "");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (TryLoadCache(requireFresh: false, out cached))
                {
                    _context = cached;
                    _loaded = true;
                    _lastError = $"GitHub 取得例外のためキャッシュを利用: {ex.Message}";
                    return (true, "");
                }
                _lastError = $"GitHub 情報の取得に失敗しました: {ex.Message}";
                return (false, _lastError);
            }
        }

        private static string FormatGitHubError(int statusCode, bool usedPat)
        {
            if (statusCode == 401)
            {
                return "GitHub PAT が無効です。github_pat.txt を確認してください。";
            }
            if (statusCode == 403 || statusCode == 429)
            {
                return usedPat
                    ? $"GitHub API が拒否されました ({statusCode})。PAT の権限またはレート制限を確認してください。"
                    : $"GitHub API が拒否されました ({statusCode})。未認証の制限の可能性が高いです。exe 隣の github_pat.txt に PAT を置くと緩和できます。";
            }
            return $"GitHub リポジトリ一覧の取得に失敗しました: {statusCode}";
        }

        private static bool TryLoadCache(bool requireFresh, out string content)
        {
            content = "";
            try
            {
                if (!File.Exists(CacheFilePath)) { return false; }
                var info = new FileInfo(CacheFilePath);
                if (requireFresh && DateTime.UtcNow - info.LastWriteTimeUtc > CacheTtl)
                {
                    return false;
                }
                content = File.ReadAllText(CacheFilePath, Encoding.UTF8);
                return !string.IsNullOrWhiteSpace(content);
            }
            catch
            {
                return false;
            }
        }

        private static void TrySaveCache(string content)
        {
            try
            {
                Directory.CreateDirectory(CacheDirectory);
                File.WriteAllText(CacheFilePath, content, Encoding.UTF8);
            }
            catch
            {
                // キャッシュ保存失敗は無視
            }
        }

        private static async Task<string> TryGetReadmeAsync(string repoName, CancellationToken cancellationToken)
        {
            try
            {
                var url = $"https://api.github.com/repos/{Owner}/{repoName}/readme";
                using var response = await GetGitHubAsync(url, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return "";
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var root = JObject.Parse(json);
                var content = root["content"]?.ToString();
                if (string.IsNullOrWhiteSpace(content))
                {
                    return "";
                }

                var compact = content.Replace("\n", "").Replace("\r", "");
                var bytes = Convert.FromBase64String(compact);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return "";
            }
        }

        public string BuildSystemInstruction(string baseInstruction)
        {
            if (!IsLoaded)
            {
                return baseInstruction + "\n\n現在 GitHub リポジトリ情報は未取得です。雑談は続けつつ、リポジトリ詳細は控えめで答えてください。";
            }

            return baseInstruction + "\n\n以下は参照用の公開リポジトリ情報です。質問に関係する範囲だけ使ってください。\n" + _context;
        }
    }
}
