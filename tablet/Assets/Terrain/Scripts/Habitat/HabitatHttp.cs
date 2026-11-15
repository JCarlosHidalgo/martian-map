using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace MartianMap.Terrain
{
    /// <summary>
    /// The HTTP calls to the habitat and its file server (C3). UnityWebRequest awaited on the main
    /// thread; a timeout, a network error or a non-2xx status throws <see cref="HabitatHttpException"/>.
    /// Timeouts (Convenciones, "Ritmo del cliente"): 5 s for JSON, 30 s for a tile file.
    /// </summary>
    public static class HabitatHttp
    {
        public const int JsonTimeoutS = 5;
        public const int FileTimeoutS = 30;

        public static async Task<string> GetTextAsync(string url)
        {
            return Encoding.UTF8.GetString(await GetBytesAsync(url, JsonTimeoutS));
        }

        public static async Task<byte[]> GetBytesAsync(string url, int timeoutS = FileTimeoutS)
        {
            using var request = UnityWebRequest.Get(url);
            request.timeout = timeoutS;
            await SendAsync(request);
            return request.downloadHandler.data ?? Array.Empty<byte>();
        }

        public static async Task<string> PostJsonAsync(string url, string json)
        {
            using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = JsonTimeoutS,
            };
            request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            await SendAsync(request);
            return request.downloadHandler.text;
        }

        private static async Task SendAsync(UnityWebRequest request)
        {
            var completion = new TaskCompletionSource<bool>();
            request.SendWebRequest().completed += _ => completion.TrySetResult(true);
            await completion.Task;

            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new HabitatHttpException(request.url, request.responseCode, request.error,
                    request.downloadHandler?.text);
            }
        }
    }

    /// <summary>A failed call to the habitat: network error, timeout or a non-2xx status.</summary>
    public sealed class HabitatHttpException : Exception
    {
        public HabitatHttpException(string url, long status, string error, string body)
            : base($"{url} -> {(status > 0 ? status.ToString() : "no response")} {error}")
        {
            Url = url;
            Status = status;
            Body = body;
        }

        public string Url { get; }

        /// <summary>The HTTP status, or 0 when there was no response.</summary>
        public long Status { get; }

        /// <summary>The response body (RFC 9457 problem+json on errors), if any.</summary>
        public string Body { get; }
    }
}
