using System;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace MartianMap.Terrain
{
    /// <summary>
    /// App configuration (C5, "Configuración"): the two habitat URLs and this tablet's author.
    /// Stored in <c>persistentDataPath/config.json</c>; the first access falls back to
    /// <c>StreamingAssets/config.default.json</c>.
    /// </summary>
    public static class AppConfig
    {
        private const int SchemaVersion = 1;
        private const string FileName = "config.json";
        private const string DefaultFileName = "config.default.json";

        private static readonly Regex UrlPattern = new Regex(@"^http://[A-Za-z0-9.\-]+:[0-9]{1,5}$");
        private static readonly Regex AuthorPattern = new Regex(@"^[a-z0-9-]{2,16}$");

        private static bool _loaded;
        private static string _habitatUrl = string.Empty;
        private static string _filesUrl = string.Empty;
        private static string _author = string.Empty;

        public static string HabitatUrl
        {
            get
            {
                EnsureLoaded();
                return _habitatUrl;
            }
        }

        public static string FilesUrl
        {
            get
            {
                EnsureLoaded();
                return _filesUrl;
            }
        }

        public static string Author
        {
            get
            {
                EnsureLoaded();
                return _author;
            }
        }

        private static string ConfigPath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>Validates, saves and emits <see cref="MapEvents.ConfigChanged"/>.</summary>
        /// <exception cref="ArgumentException">A URL is not <c>http://host:port</c>, or the author is invalid or differs from the saved one.</exception>
        public static void Save(string habitatUrl, string filesUrl, string author)
        {
            EnsureLoaded();
            habitatUrl ??= string.Empty;
            filesUrl ??= string.Empty;
            author ??= string.Empty;

            ValidateUrl(nameof(habitatUrl), habitatUrl);
            ValidateUrl(nameof(filesUrl), filesUrl);
            if (author.Length > 0 && !AuthorPattern.IsMatch(author))
            {
                throw new ArgumentException($"author '{author}' does not match ^[a-z0-9-]{{2,16}}$", nameof(author));
            }

            if (_author.Length > 0 && author != _author)
            {
                throw new ArgumentException($"author is already set to '{_author}' and cannot change", nameof(author));
            }

            var file = new ConfigFile
            {
                SchemaVersion = SchemaVersion,
                HabitatUrl = habitatUrl,
                FilesUrl = filesUrl,
                Author = author,
            };
            File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(file, Formatting.Indented));

            _habitatUrl = habitatUrl;
            _filesUrl = filesUrl;
            _author = author;
            Debug.Log($"[Terrain] config saved habitat_url={habitatUrl} files_url={filesUrl} author={author}");
            MapEvents.Emit(new ConfigChanged(habitatUrl, filesUrl, author));
        }

        private static void ValidateUrl(string name, string url)
        {
            if (url.Length > 0 && !UrlPattern.IsMatch(url))
            {
                throw new ArgumentException($"{name} '{url}' is not of the form http://host:port", name);
            }
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            var json = File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath) : ReadDefault();
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogWarning("[Terrain] no config found; working from cache only");
                return;
            }

            var file = JsonConvert.DeserializeObject<ConfigFile>(json);
            if (file == null)
            {
                Debug.LogWarning("[Terrain] config is empty; working from cache only");
                return;
            }

            _habitatUrl = file.HabitatUrl ?? string.Empty;
            _filesUrl = file.FilesUrl ?? string.Empty;
            _author = file.Author ?? string.Empty;
        }

        private static string ReadDefault()
        {
            var path = Path.Combine(Application.streamingAssetsPath, DefaultFileName);
            if (!path.Contains("://"))
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }

            // Android: StreamingAssets lives inside the APK and is only readable through UnityWebRequest.
            using var request = UnityWebRequest.Get(path);
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[Terrain] default config unreadable path={path} error={request.error}");
                return null;
            }

            return request.downloadHandler.text;
        }

        private sealed class ConfigFile
        {
            [JsonProperty("schema_version")] public int SchemaVersion { get; set; }
            [JsonProperty("habitat_url")] public string HabitatUrl { get; set; }
            [JsonProperty("files_url")] public string FilesUrl { get; set; }
            [JsonProperty("author")] public string Author { get; set; }
        }
    }
}
