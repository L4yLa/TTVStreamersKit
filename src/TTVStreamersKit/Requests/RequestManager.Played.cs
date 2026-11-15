using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TTVStreamersKit.Requests
{
    internal sealed partial class RequestManager
    {
        private const string CustomLevelPrefix = "custom_level_";
        private const string WipSuffix = " WIP";
        private const int HashLength = 40;

        /// <summary>
        /// セッション中に BeatSaver に問い合わせたハッシュとキーの対応。値が null のものは BeatSaver にない譜面。
        /// ファイルには保存せず、ゲームの終了で消える。
        /// </summary>
        private readonly Dictionary<string, string> _keyByHash = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _lookingUp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private int _startedCount;
        private int _recordedStart;

        /// <summary>最後にプレイした譜面（!link の「プレイした曲」で使う）。公式の曲、WIP、BeatSaver にない譜面は含めない。</summary>
        public MapInfo LastPlayedMap { get; private set; }

        /// <summary>曲が始まる直前（LevelStartPatch）。手元でキーが分かればすぐ記録し、なければ BeatSaver に問い合わせる。</summary>
        private void OnLevelStarting(BeatmapLevel level)
        {
            // 「プレイした曲」を使わないときは問い合わせない
            if (!_pluginConfig.RequestEnabled || _config.LinkType != LinkType.All) {
                return;
            }
            var hash = HashOf(level);
            if (hash == null) {
                // 公式の曲と WIP は対象外。前に記録した曲を残す
                return;
            }
            var start = ++_startedCount;
            if (_keyByHash.TryGetValue(hash, out var cached)) {
                if (cached != null) {
                    this.RecordPlayed(level, hash, cached, start);
                }
                return;
            }
            var local = this.FindLocalKey(hash);
            if (local != null) {
                this.RecordPlayed(level, hash, local, start);
                return;
            }
            _ = this.LookUpKeyAsync(level, hash, start);
        }

        /// <summary>曲の ID からハッシュを取り出す。カスタム曲でないか WIP なら null。</summary>
        private static string HashOf(BeatmapLevel level)
        {
            var id = level?.levelID;
            if (string.IsNullOrEmpty(id) || !id.StartsWith(CustomLevelPrefix, StringComparison.Ordinal)
                || id.EndsWith(WipSuffix, StringComparison.Ordinal)) {
                return null;
            }
            var rest = id.Substring(CustomLevelPrefix.Length);
            if (rest.Length < HashLength) {
                return null;
            }
            var hash = rest.Substring(0, HashLength);
            return hash.All(Uri.IsHexDigit) ? hash.ToUpperInvariant() : null;
        }

        /// <summary>キューと履歴（リクエストで BeatSaver から取った情報）からキーを探す。</summary>
        private string FindLocalKey(string hash)
        {
            var map = this.Queue.Concat(this.History)
                .Select(r => r.Map)
                .FirstOrDefault(m => m != null && !string.IsNullOrEmpty(m.Key) && string.Equals(m.Hash, hash, StringComparison.OrdinalIgnoreCase));
            return map?.Key;
        }

        private async Task LookUpKeyAsync(BeatmapLevel level, string hash, int start)
        {
            // 重ね定義が内部で呼び合うと同じ曲で続けて届くため、問い合わせ中なら重ねない
            if (!_lookingUp.Add(hash)) {
                return;
            }
            try {
                var (status, map) = await BeatSaverClient.GetByHashAsync(hash);
                if (status == BeatSaverStatus.Unavailable) {
                    // 覚えずに、次にプレイしたときに問い合わせ直す
                    return;
                }
                var key = status == BeatSaverStatus.Ok && !string.IsNullOrEmpty(map?.Key) ? map.Key : null;
                _keyByHash[hash] = key;
                if (key != null) {
                    this.RecordPlayed(level, hash, key, start);
                }
            }
            catch (Exception e) {
                Plugin.Log.Warn($"Requests: failed to look up {hash}: {e.Message}");
            }
            finally {
                _lookingUp.Remove(hash);
            }
        }

        /// <summary>後から始まった曲がすでに記録されていれば、問い合わせの遅れた前の曲で上書きしない。</summary>
        private void RecordPlayed(BeatmapLevel level, string hash, string key, int start)
        {
            if (start < _recordedStart) {
                return;
            }
            _recordedStart = start;
            this.LastPlayedMap = new MapInfo {
                Key = key,
                Hash = hash,
                SongName = level.songName ?? "",
                SongSubName = level.songSubName ?? "",
                SongAuthorName = level.songAuthorName ?? "",
                LevelAuthorName = string.Join(", ", level.allMappers ?? Array.Empty<string>()),
                Duration = (int)level.songDuration,
            };
        }
    }
}
