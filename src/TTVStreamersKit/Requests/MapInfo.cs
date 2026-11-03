using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TTVStreamersKit.Requests
{
    /// <summary>譜面の1つの難易度。</summary>
    public sealed class MapDifficulty
    {
        [JsonProperty("characteristic")] public string Characteristic { get; set; } = "";
        [JsonProperty("difficulty")] public string Difficulty { get; set; } = "";
        [JsonProperty("label")] public string Label { get; set; } = "";
        [JsonProperty("njs")] public float Njs { get; set; }
        [JsonProperty("stars")] public float? Stars { get; set; }
    }

    /// <summary>BeatSaver の譜面の情報のうち、リクエストで使うもの。キューのファイルにもこの形で保存する。</summary>
    public sealed class MapInfo
    {
        [JsonProperty("key")] public string Key { get; set; } = "";
        [JsonProperty("hash")] public string Hash { get; set; } = "";
        [JsonProperty("songName")] public string SongName { get; set; } = "";
        [JsonProperty("songSubName")] public string SongSubName { get; set; } = "";
        [JsonProperty("songAuthorName")] public string SongAuthorName { get; set; } = "";
        [JsonProperty("levelAuthorName")] public string LevelAuthorName { get; set; } = "";
        [JsonProperty("uploaderName")] public string UploaderName { get; set; } = "";
        /// <summary>秒。</summary>
        [JsonProperty("duration")] public int Duration { get; set; }
        /// <summary>BeatSaver の評価（0〜1）。</summary>
        [JsonProperty("rating")] public float Rating { get; set; }
        [JsonProperty("upvotes")] public int Upvotes { get; set; }
        [JsonProperty("downvotes")] public int Downvotes { get; set; }
        [JsonProperty("uploaded")] public DateTimeOffset? Uploaded { get; set; }
        [JsonProperty("downloadUrl")] public string DownloadUrl { get; set; } = "";
        [JsonProperty("coverUrl")] public string CoverUrl { get; set; } = "";
        /// <summary>公開済みでない（WIP）版。</summary>
        [JsonProperty("wip")] public bool IsWip { get; set; }
        [JsonProperty("diffs")] public List<MapDifficulty> Difficulties { get; set; } = new List<MapDifficulty>();

        [JsonIgnore] public float MaxNjs => this.Difficulties.Count == 0 ? 0f : this.Difficulties.Max(d => d.Njs);

        [JsonIgnore] public float? MaxStars => this.Difficulties.Where(d => d.Stars.HasValue).Select(d => d.Stars).DefaultIfEmpty(null).Max();

        [JsonIgnore] public string BeatSaverUrl => "https://beatsaver.com/maps/" + this.Key;

        /// <summary>
        /// BeatSaver の譜面の JSON から作る。公開済みの版（state = Published）を使い、なければ最も新しい版を使う。
        /// </summary>
        public static MapInfo FromJson(JObject map)
        {
            if (map == null) {
                return null;
            }
            var versions = map["versions"] as JArray;
            var version = versions?.OfType<JObject>().FirstOrDefault(v => (string)v["state"] == "Published")
                          ?? versions?.OfType<JObject>().OrderByDescending(v => (string)v["createdAt"], StringComparer.Ordinal).FirstOrDefault();
            if (version == null) {
                return null;
            }
            var meta = map["metadata"] as JObject;
            var stats = map["stats"] as JObject;
            var info = new MapInfo {
                Key = (string)map["id"] ?? "",
                Hash = ((string)version["hash"] ?? "").ToUpperInvariant(),
                SongName = (string)meta?["songName"] ?? (string)map["name"] ?? "",
                SongSubName = (string)meta?["songSubName"] ?? "",
                SongAuthorName = (string)meta?["songAuthorName"] ?? "",
                LevelAuthorName = (string)meta?["levelAuthorName"] ?? "",
                UploaderName = (string)map["uploader"]?["name"] ?? "",
                Duration = (int?)meta?["duration"] ?? 0,
                Rating = (float?)stats?["score"] ?? 0f,
                Upvotes = (int?)stats?["upvotes"] ?? 0,
                Downvotes = (int?)stats?["downvotes"] ?? 0,
                DownloadUrl = (string)version["downloadURL"] ?? "",
                CoverUrl = (string)version["coverURL"] ?? "",
                IsWip = (string)version["state"] != "Published",
            };
            if (DateTimeOffset.TryParse((string)map["uploaded"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var uploaded)) {
                info.Uploaded = uploaded;
            }
            if (version["diffs"] is JArray diffs) {
                foreach (var d in diffs.OfType<JObject>()) {
                    info.Difficulties.Add(new MapDifficulty {
                        Characteristic = (string)d["characteristic"] ?? "",
                        Difficulty = (string)d["difficulty"] ?? "",
                        Label = (string)d["label"] ?? "",
                        Njs = (float?)d["njs"] ?? 0f,
                        Stars = (float?)d["stars"],
                    });
                }
            }
            return info;
        }
    }
}
