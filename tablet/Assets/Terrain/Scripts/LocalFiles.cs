using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace MartianMap.Terrain
{
    /// <summary>
    /// Reads local files from a plain path or from StreamingAssets on Android, where the folder is
    /// inside the APK and only <see cref="UnityWebRequest"/> can read it.
    /// </summary>
    public static class LocalFiles
    {
        /// <summary>Joins path parts with forward slashes, valid for file paths and jar:file:// URLs.</summary>
        public static string Combine(params string[] parts) => string.Join("/", parts).Replace('\\', '/');

        /// <exception cref="FileNotFoundException">The file does not exist.</exception>
        public static async Task<byte[]> ReadBytesAsync(string path)
        {
            if (!path.Contains("://"))
            {
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException($"missing {path}", path);
                }

                return await Task.Run(() => File.ReadAllBytes(path));
            }

            using var request = UnityWebRequest.Get(path);
            var completion = new TaskCompletionSource<bool>();
            request.SendWebRequest().completed += _ => completion.TrySetResult(true);
            await completion.Task;

            if (request.result != UnityWebRequest.Result.Success)
            {
                if (request.responseCode == 404 || (request.error ?? string.Empty).Contains("not found", StringComparison.OrdinalIgnoreCase))
                {
                    throw new FileNotFoundException($"missing {path}: {request.error}", path);
                }

                throw new IOException($"cannot read {path}: {request.error}");
            }

            return request.downloadHandler.data;
        }

        /// <summary>The file's bytes, or null if it does not exist.</summary>
        public static async Task<byte[]> TryReadBytesAsync(string path)
        {
            try
            {
                return await ReadBytesAsync(path);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }
    }
}
