using IPA.Utilities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TTVStreamersKit.Requests
{
    internal sealed partial class RequestManager
    {
        private const string PlaylistFileName = "TTVStreamersKit History.bplist";

        /// <summary>OBS 用のファイル。キューの内容と受付状態。</summary>
        private void WriteObsFiles()
        {
            if (!_config.UpdateQueueStatusFiles || _store == null) {
                return;
            }
            try {
                var lines = _store.Queue.Where(r => r.Status == RequestStatus.Queued)
                    .Take(Math.Max(0, _config.MaximumQueueTextEntries))
                    .Select(r => _messages.Format("ObsQueueLine", this.SongVars(r.Map, null, r)));
                File.WriteAllText(Path.Combine(_dir, "queue-list.txt"), string.Join(Environment.NewLine, lines), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(_dir, "queue-status.txt"),
                    _messages.Get(_config.RequestQueueOpen ? "ObsQueueOpen" : "ObsQueueClosed"), new UTF8Encoding(false));
            }
            catch (Exception e) {
                Plugin.Log.Warn($"Requests: failed to write the OBS files: {e.Message}");
            }
        }

        private sealed class PlaylistSong
        {
            [JsonProperty("key")] public string Key { get; set; }
            [JsonProperty("hash")] public string Hash { get; set; }
            [JsonProperty("songName")] public string SongName { get; set; }
            [JsonProperty("levelAuthorName")] public string LevelAuthorName { get; set; }
            [JsonProperty("dateAdded")] public DateTimeOffset DateAdded { get; set; }
        }

        private sealed class Playlist
        {
            [JsonProperty("playlistTitle")] public string Title { get; set; }
            [JsonProperty("playlistAuthor")] public string Author { get; set; }
            [JsonProperty("image", NullValueHandling = NullValueHandling.Ignore)] public string Image { get; set; }
            [JsonProperty("songs")] public List<PlaylistSong> Songs { get; set; }
        }

        /// <summary>
        /// 履歴をプレイリストに書き出す。同じハッシュは1回だけ、追加日時の新しい順。
        /// WIP の曲、取り消した曲、検索の結果、リクエストした人の情報は入れない。
        /// </summary>
        private void WriteHistoryPlaylist()
        {
            try {
                var songs = _store.History
                    .Where(r => r.Map != null && !r.Map.IsWip && !string.IsNullOrEmpty(r.Map.Hash)
                                && r.Status != RequestStatus.WrongSong && r.Status != RequestStatus.SearchResult)
                    .OrderByDescending(r => r.RequestedAt)
                    .GroupBy(r => r.Map.Hash, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .Select(r => new PlaylistSong {
                        Key = r.Map.Key,
                        Hash = r.Map.Hash,
                        SongName = r.Map.SongName,
                        LevelAuthorName = r.Map.LevelAuthorName,
                        DateAdded = r.RequestedAt,
                    })
                    .ToList();
                var playlist = new Playlist { Title = "TTVStreamersKit History", Author = "TTVStreamersKit", Image = HistoryCover.Value, Songs = songs };
                var dir = Path.Combine(UnityGame.InstallPath, "Playlists");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, PlaylistFileName), JsonConvert.SerializeObject(playlist, Formatting.Indented), new UTF8Encoding(false));
            }
            catch (Exception e) {
                Plugin.Log.Warn($"Requests: failed to write the history playlist: {e.Message}");
            }
        }

        /// <summary>プレイリストのカバー画像（埋め込みの PNG を data URI にしたもの）。読めなければ null。</summary>
        private static readonly Lazy<string> HistoryCover = new Lazy<string>(() => {
            try {
                using (var stream = typeof(RequestManager).Assembly.GetManifestResourceStream("TTVStreamersKit.Resources.history-cover.png")) {
                    if (stream == null) {
                        return null;
                    }
                    using (var memory = new MemoryStream()) {
                        stream.CopyTo(memory);
                        return "data:image/png;base64," + Convert.ToBase64String(memory.ToArray());
                    }
                }
            }
            catch (Exception e) {
                Plugin.Log.Warn($"Requests: failed to read the playlist cover: {e.Message}");
                return null;
            }
        });

        private void PlayNotifySound()
        {
            if (_config.NotifySound) {
                NotifySound.Play(_config.SoundVolume / 100f);
            }
        }
    }
}
