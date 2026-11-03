using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;

namespace TTVStreamersKit.Requests
{
    public enum RequestStatus
    {
        Queued,
        Played,
        Skipped,
        Blocked,
        WrongSong,
        /// <summary>キューの画面の検索で入れたもの。リクエスト数に数えず、履歴にも残さない。</summary>
        SearchResult,
    }

    /// <summary>リクエストした人。</summary>
    public sealed class Requester
    {
        [JsonProperty("id")] public string Id { get; set; } = "";
        [JsonProperty("login")] public string Login { get; set; } = "";
        [JsonProperty("displayName")] public string DisplayName { get; set; } = "";

        [JsonIgnore] public string Name => string.IsNullOrEmpty(this.DisplayName) ? this.Login : this.DisplayName;
    }

    /// <summary>キューと履歴の1件。</summary>
    public sealed class SongRequest
    {
        [JsonProperty("map")] public MapInfo Map { get; set; }
        /// <summary>履歴では、ゲームの終了時に消す（null になる）。</summary>
        [JsonProperty("requester")] public Requester Requester { get; set; }
        [JsonProperty("requestedAt")] public DateTimeOffset RequestedAt { get; set; }
        [JsonProperty("status"), JsonConverter(typeof(StringEnumConverter))] public RequestStatus Status { get; set; }
        /// <summary>指定された難易度（BeatSaver の表記）。未指定なら null。</summary>
        [JsonProperty("difficulty")] public string Difficulty { get; set; }
        [JsonProperty("characteristic")] public string Characteristic { get; set; }
        [JsonProperty("difficultyLabel")] public string DifficultyLabel { get; set; }

        /// <summary>難易度の指定を待っていて、追加の通知をまだ送っていない。</summary>
        [JsonProperty("announcementPending")] public bool AnnouncementPending { get; set; }

        [JsonIgnore] public bool HasDifficulty => !string.IsNullOrEmpty(this.Difficulty);

        [JsonIgnore] public string DifficultyDisplayName =>
            !this.HasDifficulty ? "" : new DifficultyResolver.ResolvedDifficulty {
                Difficulty = this.Difficulty, Characteristic = this.Characteristic, Label = this.DifficultyLabel,
            }.FullDisplayName;

        public void SetDifficulty(DifficultyResolver.ResolvedDifficulty d)
        {
            this.Difficulty = d?.Difficulty;
            this.Characteristic = d?.Characteristic;
            this.DifficultyLabel = d?.Label;
        }
    }
}
