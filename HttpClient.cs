using SyncRoomChatToolV2.Properties;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;

namespace SyncRoomChatToolV2
{
    /// <summary>
    /// Serviceとの通信を担当する、HTTPクライアントのサンプル。
    /// https://iwasiman.hatenablog.com/entry/20210622-CSharp-HttpClient ほぼ丸パクリ。不要部分は削除。VOICEVOXはPOST出来りゃいい。んだが、やっぱりGETも使うのであった。
    /// </summary>
    public class ServiceHttpClient
    {
#nullable disable warnings
        /// <summary>
        /// 通信先のアドレス
        /// </summary>
        private readonly string requestAddress;

        private readonly RequestType reqType;

        /// <summary>
        /// C#側のHttpクライアント
        /// </summary>
        private readonly HttpClient httpClient;
        private readonly HttpClient httpClient2;
        private readonly HttpClient httpClient3;

        /// <summary>
        /// デフォルトコンストラクタ。外部からは呼び出せません。
        /// </summary>
        private ServiceHttpClient()
        {
        }

        public enum RequestType
        {
            none = 0,
            userInfo = 1,
            commentInfo = 2
        }

        /// <summary>
        /// 引数付きのコンストラクタ。こちらを使用します。
        /// 引数には正しいURLが入っていることが前提です。
        /// </summary>
        /// <param Name="requestAddress">リクエストURL</param>
        /// <param name="reqType">通常（VOICEVOXリクエスト）かツイキャス他か</param>
        public ServiceHttpClient(string requestAddress, RequestType reqType = RequestType.none)
        {
            this.requestAddress = requestAddress;
            this.reqType = reqType;

            // 通信するメソッドでその都度HttpClientをnewすると毎回ソケットを開いてリソースを消費するため、
            // メンバ変数で使い回す手法を取っています。
            switch (reqType)
            {
                case RequestType.none:
                    this.httpClient = new HttpClient();
                    break;
                case RequestType.userInfo:
                    this.httpClient2 = new HttpClient();
                    break;
                case RequestType.commentInfo:
                    this.httpClient3 = new HttpClient();
                    break;
                default:
                    this.httpClient = new HttpClient();
                    break;
            }
        }

        /// <summary>
        /// 情報がURLに載ったGETリクエストを送受信するサンプル。
        /// </summary>
        /// <param Name="someId">何かのID</param>
        /// <returns>正常：レスポンスのボディ / 異常：null</returns>
        public string Get()
        {
            String requestEndPoint = this.requestAddress;
            HttpRequestMessage request = this.CreateRequest(HttpMethod.Get, requestEndPoint);

            string resBodyStr;
            HttpStatusCode resStatusCode = HttpStatusCode.NotFound;
            Task<HttpResponseMessage> response;
            // 通信実行。メンバ変数でhttpClientを持っているので、using(～)で囲いません。囲うと通信後にオブジェクトが破棄されます。
            // 引数にrequestを取る場合はGetAsyncやPostAsyncでなくSendAsyncメソッドになります。
            // 戻り値はTask<HttpResponseMessage>で、変数名.ResultとするとSystem.Net.Http.HttpResponseMessageクラスが取れます。
            try
            {
                response = reqType switch
                {
                    RequestType.none => httpClient.SendAsync(request),
                    RequestType.userInfo => httpClient2.SendAsync(request),
                    RequestType.commentInfo => httpClient3.SendAsync(request),
                    _ => httpClient.SendAsync(request),
                };
                resBodyStr = response.Result.Content.ReadAsStringAsync().Result;

                resStatusCode = response.Result.StatusCode;
                if (reqType == RequestType.commentInfo)
                {
#if DEBUG
                    Debug.WriteLine(response.Result.Headers);
#endif
                }
            }
            catch (SocketException)
            {
                //UNDONE: 暫定処置だな。サーバがレスポンスを返さなかった時。再現難しいなぁ。
                return null;
            }

            catch (HttpRequestException)
            {
                // UNDONE: 通信失敗のエラー処理
                return null;
            }
            catch (System.AggregateException Aggre)
            {
#if DEBUG
                Debug.WriteLine(Aggre.Message);
#endif
                return null;
            }


            if (!resStatusCode.Equals(HttpStatusCode.OK))
            {
                // UNDONE: レスポンスが200 OK以外の場合のエラー処理
                return null;
            }
            if (String.IsNullOrEmpty(resBodyStr))
            {
                // UNDONE: レスポンスのボディが空の場合のエラー処理
                return null;
            }
            // 中身のチェックなどを経て終了。
            return resBodyStr;
        }

        /// <summary>
        /// VOICEVOX用に改修。元はキーをJsonにまでしてくれてたけど、それは要らんので。喚び元でやれと。
        /// </summary>
        /// <param Name="QueryResponce"></param>
        /// <returns>正常：レスポンスのボディ / 異常：null</returns>
        public HttpResponseMessage Post(ref string QueryResponce, string BinaryFile)
        {
            String requestEndPoint = this.requestAddress;
            var request = this.CreateRequest(HttpMethod.Post, requestEndPoint);

            var content = new StringContent(QueryResponce, Encoding.UTF8, @"application/json");

            request.Content = content;

            string resBodyStr = "";
            Task<HttpResponseMessage> response;
            HttpStatusCode resStatusCoode;
            try
            {
                response = reqType switch
                {
                    RequestType.none => httpClient.SendAsync(request),
                    RequestType.userInfo => httpClient2.SendAsync(request),
                    RequestType.commentInfo => httpClient3.SendAsync(request),
                    _ => httpClient.SendAsync(request),
                };
                if (!string.IsNullOrEmpty(QueryResponce))
                {
                    var stream = response.Result.Content.ReadAsStreamAsync().Result;

                    using var fileStream = File.Create(BinaryFile);
                    using var httpStream = response.Result.Content.ReadAsStreamAsync();
                    stream.CopyTo(fileStream);
                    fileStream.Flush();
                    resBodyStr = "ファイル出力OK";
                }
                else
                {
                    resBodyStr = response.Result.Content.ReadAsStringAsync().Result;
                }
                resStatusCoode = response.Result.StatusCode;
            }
            catch (HttpRequestException)
            {
                // UNDONE: 通信失敗のエラー処理
                return null;
            }

            if (!resStatusCoode.Equals(HttpStatusCode.OK))
            {
                // UNDONE: レスポンスが200 OK以外の場合のエラー処理
                return null;
            }
            if (String.IsNullOrEmpty(resBodyStr))
            {
                // UNDONE: レスポンスのボディが空の場合のエラー処理
                return null;
            }
            // 中身を取り出したりして終了
            QueryResponce = resBodyStr;
            return response.Result;
        }

        /*
        /// <summary>
        /// オプションを設定します。内部メソッドです。
        /// </summary>
        /// <returns>JsonSerializerOptions型のオプション</returns>
        private JsonSerializerOptions GetJsonOption()
        {
            // ユニコードのレンジ指定で日本語も正しく表示、インデントされるように指定
            var options = new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                WriteIndented = true,
            };
            return options;
        }
        */

        /// <summary>
        /// HTTPリクエストメッセージを生成する内部メソッドです。
        /// </summary>
        /// <param Name="httpMethod">HTTPメソッドのオブジェクト</param>
        /// <param Name="requestEndPoint">通信先のURL</param>
        /// <returns>HttpRequestMessage</returns>
        private HttpRequestMessage CreateRequest(HttpMethod httpMethod, string requestEndPoint)
        {
            var request = new HttpRequestMessage(httpMethod, requestEndPoint);
            return this.AddHeaders(request);
        }

        /// <summary>
        /// HTTPリクエストにヘッダーを追加する内部メソッドです。
        /// </summary>
        /// <param Name="request">リクエスト</param>
        /// <returns>HttpRequestMessage</returns>
        private HttpRequestMessage AddHeaders(HttpRequestMessage request)
        {
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("Accept-Charset", "utf-8");
            // 同じようにして、例えば認証通過後のトークンが "Authorization: Bearer {トークンの文字列}"
            // のように必要なら適宜追加していきます。

            // 対応サービス増やすかは分からんのにEnumにしたが、ここはSwitchにしてねぇと言う。
            if (this.reqType != RequestType.none)
            {
                request.Headers.Add("X-Api-Version", "2.0");
                request.Headers.Add("Authorization", $"Bearer {Settings.Default.AccessToken}");
            }
            return request;
        }
#nullable restore
    }
}
