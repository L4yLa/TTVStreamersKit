using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TTVStreamersKit.Requests
{
    /// <summary>キュー、履歴、再生済みの記録、セッションの情報の保存。メインスレッドから使う。</summary>
    internal sealed class RequestStore
    {
        private sealed class PlayedEntry
        {
            [JsonProperty("key")] public string Key { get; set; }
            [JsonProperty("hash")] public string Hash { get; set; }
            [JsonProperty("playedAt")] public DateTimeOffset PlayedAt { get; set; }
        }

        private sealed class SessionData
        {
            [JsonProperty("lastSaved")] public DateTimeOffset LastSaved { get; set; }
            /// <summary>セッション中にリクエストされた曲のキー。</summary>
            [JsonProperty("requestedKeys")] public List<string> RequestedKeys { get; set; } = new List<string>();
            /// <summary>セッション中に受け付けた数（LimitUserRequestsToSession が有効なときに使う）。利用者の ID ごと。</summary>
            [JsonProperty("acceptedCounts")] public Dictionary<string, int> AcceptedCounts { get; set; } = new Dictionary<string, int>();
        }

        private static readonly JsonSerializerSettings s_json = new JsonSerializerSettings {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
        };

        private readonly string _dir;
        private List<PlayedEntry> _played = new List<PlayedEntry>();
        private SessionData _session = new SessionData();

        public List<SongRequest> Queue { get; private set; } = new List<SongRequest>();
        public List<SongRequest> History { get; private set; } = new List<SongRequest>();

        public RequestStore(string dir)
        {
            _dir = dir;
        }

        private string QueuePath => Path.Combine(_dir, "queue.json");
        private string HistoryPath => Path.Combine(_dir, "history.json");
        private string PlayedPath => Path.Combine(_dir, "played.json");
        private string SessionPath => Path.Combine(_dir, "session.json");

        /// <summary>
        /// 起動時に読み込む。前回の保存から <paramref name="sessionHours"/> 時間を過ぎていたら、再生済みの記録とセッションの情報を消す。
        /// 前回の終了時に消せなかった場合に備え、履歴のリクエストした人の情報もここで消す。
        /// </summary>
        public void Load(int sessionHours)
        {
            Directory.CreateDirectory(_dir);
            this.Queue = Read<List<SongRequest>>(this.QueuePath) ?? new List<SongRequest>();
            this.Queue.RemoveAll(r => r?.Map == null);
            this.History = Read<List<SongRequest>>(this.HistoryPath) ?? new List<SongRequest>();
            this.History.RemoveAll(r => r?.Map == null);
            _played = Read<List<PlayedEntry>>(this.PlayedPath) ?? new List<PlayedEntry>();
            _session = Read<SessionData>(this.SessionPath) ?? new SessionData();

            if (DateTimeOffset.UtcNow - _session.LastSaved > TimeSpan.FromHours(Math.Max(1, sessionHours))) {
                _played.Clear();
                _session = new SessionData();
            }
            this.ForgetHistoryRequesters();
            this.SaveAll();
        }

        public bool WasRequestedThisSession(string key) => _session.RequestedKeys.Contains(key, StringComparer.OrdinalIgnoreCase);

        public void AddSessionRequest(string key)
        {
            if (!this.WasRequestedThisSession(key)) {
                _session.RequestedKeys.Add(key);
            }
            this.SaveSession();
        }

        public void RemoveSessionRequest(string key)
        {
            _session.RequestedKeys.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            this.SaveSession();
        }

        public int GetAcceptedCount(string userId) =>
            userId != null && _session.AcceptedCounts.TryGetValue(userId, out var n) ? n : 0;

        public void IncrementAcceptedCount(string userId)
        {
            if (userId == null) {
                return;
            }
            _session.AcceptedCounts[userId] = this.GetAcceptedCount(userId) + 1;
            this.SaveSession();
        }

        public void AddPlayed(MapInfo map)
        {
            _played.Add(new PlayedEntry { Key = map.Key, Hash = map.Hash, PlayedAt = DateTimeOffset.UtcNow });
            Write(this.PlayedPath, _played);
            this.SaveSession();
        }

        /// <summary>ゲームの終了時と起動時に、履歴からリクエストした人の情報を消す（曲の記録は残す）。</summary>
        public void ForgetHistoryRequesters()
        {
            foreach (var r in this.History) {
                r.Requester = null;
            }
            Write(this.HistoryPath, this.History);
        }

        public void SaveQueue() => Write(this.QueuePath, this.Queue);

        public void SaveHistory() => Write(this.HistoryPath, this.History);

        public void SaveAll()
        {
            this.SaveQueue();
            this.SaveHistory();
            Write(this.PlayedPath, _played);
            this.SaveSession();
        }

        private void SaveSession()
        {
            _session.LastSaved = DateTimeOffset.UtcNow;
            Write(this.SessionPath, _session);
        }

        private static T Read<T>(string path) where T : class
        {
            try {
                return File.Exists(path) ? JsonConvert.DeserializeObject<T>(File.ReadAllText(path, Encoding.UTF8), s_json) : null;
            }
            catch (Exception e) {
                Plugin.Log.Error($"Requests: failed to read {path}: {e.Message}");
                return null;
            }
        }

        private static void Write(string path, object value)
        {
            try {
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(value, s_json), new UTF8Encoding(false));
                if (File.Exists(path)) {
                    File.Replace(tmp, path, null);
                }
                else {
                    File.Move(tmp, path);
                }
            }
            catch (Exception e) {
                Plugin.Log.Error($"Requests: failed to write {path}: {e.Message}");
            }
        }
    }
}
