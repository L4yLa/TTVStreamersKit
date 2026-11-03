using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TTVStreamersKit.Requests
{
    /// <summary>
    /// 1行に1件のテキストの一覧。「#」で始まる行はコメント、空行は無視、前後の空白は取り除く。
    /// 書き換えるときは該当する行の追加と削除だけを行い、コメントや他の行はそのまま残す。
    /// </summary>
    internal sealed class ListFile
    {
        private readonly string _path;
        private readonly string[] _header;
        private readonly HashSet<string> _items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new object();

        public ListFile(string path, string[] header)
        {
            _path = path;
            _header = header;
        }

        public string Path => _path;

        public int Count
        {
            get {
                lock (_lock) {
                    return _items.Count;
                }
            }
        }

        /// <summary>読み込む。ファイルがなければ、書き方のコメントだけを書いたファイルを作る。</summary>
        public void Load()
        {
            lock (_lock) {
                _items.Clear();
                try {
                    if (!File.Exists(_path)) {
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path));
                        File.WriteAllLines(_path, _header, new UTF8Encoding(false));
                        return;
                    }
                    foreach (var line in File.ReadAllLines(_path, Encoding.UTF8)) {
                        var item = Normalize(line);
                        if (item != null) {
                            _items.Add(item);
                        }
                    }
                }
                catch (Exception e) {
                    Plugin.Log.Error($"Requests: failed to read {_path}: {e.Message}");
                }
            }
        }

        /// <summary>大文字小文字を区別しない完全一致。</summary>
        public bool Contains(string value)
        {
            if (string.IsNullOrEmpty(value)) {
                return false;
            }
            lock (_lock) {
                return _items.Contains(value.Trim());
            }
        }

        /// <summary>いずれかの項目を、大文字小文字を区別せずに含むか（部分一致）。</summary>
        public bool AnyContainedIn(string text)
        {
            if (string.IsNullOrEmpty(text)) {
                return false;
            }
            lock (_lock) {
                return _items.Any(i => text.IndexOf(i, StringComparison.OrdinalIgnoreCase) >= 0);
            }
        }

        /// <summary>行を加える。すでにあれば false。</summary>
        public bool Add(string value)
        {
            value = value?.Trim();
            if (string.IsNullOrEmpty(value)) {
                return false;
            }
            lock (_lock) {
                if (!_items.Add(value)) {
                    return false;
                }
                try {
                    var lines = File.Exists(_path) ? File.ReadAllLines(_path, Encoding.UTF8).ToList() : _header.ToList();
                    lines.Add(value);
                    File.WriteAllLines(_path, lines, new UTF8Encoding(false));
                }
                catch (Exception e) {
                    Plugin.Log.Error($"Requests: failed to write {_path}: {e.Message}");
                }
                return true;
            }
        }

        /// <summary>行を外す。なければ false。</summary>
        public bool Remove(string value)
        {
            value = value?.Trim();
            if (string.IsNullOrEmpty(value)) {
                return false;
            }
            lock (_lock) {
                if (!_items.Remove(value)) {
                    return false;
                }
                try {
                    if (File.Exists(_path)) {
                        var lines = File.ReadAllLines(_path, Encoding.UTF8)
                            .Where(l => !string.Equals(Normalize(l), value, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        File.WriteAllLines(_path, lines, new UTF8Encoding(false));
                    }
                }
                catch (Exception e) {
                    Plugin.Log.Error($"Requests: failed to write {_path}: {e.Message}");
                }
                return true;
            }
        }

        private static string Normalize(string line)
        {
            var t = line?.Trim();
            if (string.IsNullOrEmpty(t) || t.StartsWith("#", StringComparison.Ordinal)) {
                return null;
            }
            return t;
        }
    }

    /// <summary>4種の一覧と、その書き方のコメント。</summary>
    internal sealed class RequestLists
    {
        public ListFile BlockedUsers { get; }
        public ListFile SongAllowList { get; }
        public ListFile MapperBanList { get; }
        public ListFile SongBlockList { get; }

        public RequestLists(string dir)
        {
            this.BlockedUsers = new ListFile(System.IO.Path.Combine(dir, "blocked-users.txt"), new[] {
                "# ブロックした利用者の一覧 / Blocked users",
                "# この一覧に入っている人のメッセージは、TTVStreamersKit のコマンドとして扱いません。",
                "# Messages from these users are not handled as TTVStreamersKit commands.",
                "# 書き方: 1行に1人、Twitch のログイン名（英数字。表示名ではありません）を書きます。大文字と小文字は区別しません。",
                "# Format: one Twitch login name per line (not the display name). Case-insensitive.",
                "# 「#」で始まる行はコメントです。変更はゲームを再起動すると反映されます。",
                "# Lines starting with # are comments. Restart the game to apply changes.",
            });
            this.SongAllowList = new ListFile(System.IO.Path.Combine(dir, "song-allowlist.txt"), new[] {
                "# 曲の許可一覧 / Song allow list",
                "# この一覧の曲は、長さ・NJS・評価のフィルターを通ります。",
                "# Songs in this list skip the length, NJS and rating filters.",
                "# 書き方: 1行に1曲、BeatSaver のキー（例: 25f）を書きます。",
                "# Format: one BeatSaver key per line (e.g. 25f).",
                "# 「#」で始まる行はコメントです。変更はゲームを再起動すると反映されます。",
                "# Lines starting with # are comments. Restart the game to apply changes.",
            });
            this.MapperBanList = new ListFile(System.IO.Path.Combine(dir, "mapper-banlist.txt"), new[] {
                "# 譜面作者の禁止一覧 / Mapper ban list",
                "# 譜面作者名にこの一覧のどれかを含む曲は、リクエストを受け付けません。",
                "# Songs whose mapper name contains any of these entries are rejected.",
                "# 書き方: 1行に1件、譜面作者名の一部を書きます。大文字と小文字は区別しません。",
                "# Format: one (partial) mapper name per line. Case-insensitive.",
                "# 「#」で始まる行はコメントです。変更はゲームを再起動すると反映されます。",
                "# Lines starting with # are comments. Restart the game to apply changes.",
            });
            this.SongBlockList = new ListFile(System.IO.Path.Combine(dir, "song-blocklist.txt"), new[] {
                "# 曲のブロック一覧 / Song block list",
                "# この一覧の曲は、リクエストを受け付けません。!block / !unblock とキューの画面のブロックでも書き換わります。",
                "# Songs in this list are rejected. !block, !unblock and the Block button also update this file.",
                "# 書き方: 1行に1曲、BeatSaver のキー（例: 25f）を書きます。",
                "# Format: one BeatSaver key per line (e.g. 25f).",
                "# 「#」で始まる行はコメントです。手で編集した変更はゲームを再起動すると反映されます。",
                "# Lines starting with # are comments. Restart the game to apply manual changes.",
            });
        }

        public void LoadAll()
        {
            this.BlockedUsers.Load();
            this.SongAllowList.Load();
            this.MapperBanList.Load();
            this.SongBlockList.Load();
        }
    }
}
