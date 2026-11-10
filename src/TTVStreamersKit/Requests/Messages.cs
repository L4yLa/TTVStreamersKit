using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TTVStreamersKit.Requests
{
    /// <summary>
    /// チャットへの返信の文言。%songName% のような変数を埋め込める。
    /// 言語ごとに messages-en.json / messages-ja.json を作り、そこに書いた文言を既定の文言より優先する。
    /// </summary>
    internal sealed class Messages
    {
        private readonly Dictionary<string, string> _texts;

        private Messages(Dictionary<string, string> texts)
        {
            _texts = texts;
        }

        public static Messages Load(string dir, bool japanese)
        {
            var defaults = japanese ? Japanese() : English();
            var path = Path.Combine(dir, japanese ? "messages-ja.json" : "messages-en.json");
            var texts = new Dictionary<string, string>(defaults, StringComparer.Ordinal);
            try {
                Directory.CreateDirectory(dir);
                if (File.Exists(path)) {
                    var custom = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path, Encoding.UTF8));
                    if (custom != null) {
                        foreach (var kv in custom) {
                            // 以前の版の既定の文言のままなら、今の既定に置き換える（書き戻したファイルに古い既定が残るため）
                            if (kv.Value != null && defaults.ContainsKey(kv.Key) && !IsOldDefault(kv.Key, kv.Value)) {
                                texts[kv.Key] = kv.Value;
                            }
                        }
                    }
                }
                // 既定の文言を足したものを書き戻し、新しい項目もファイルで見えるようにする
                File.WriteAllText(path, JsonConvert.SerializeObject(texts, Formatting.Indented), new UTF8Encoding(false));
            }
            catch (Exception e) {
                Plugin.Log.Error($"Requests: failed to read {path}: {e.Message}");
            }
            return new Messages(texts);
        }

        /// <summary>
        /// 以前の版の既定の文言。返信が「!名前」で始まっていたもの（ほかのボットやこの MOD 自身がコマンドとして受け取ることがある）と、
        /// 開発中の版で使い方からも ! を外していたもの。
        /// </summary>
        private static readonly Dictionary<string, string[]> s_oldDefaults = new Dictionary<string, string[]>(StringComparer.Ordinal) {
            ["DifficultyPrompt"] = new[] {
                "%user%, please choose a difficulty for %songName% (%key%) with !difficulty: %available%",
                "%user% さん、%songName% (%key%) の難易度を !difficulty で指定してください: %available%",
                "%user%, please choose a difficulty for %songName% (%key%) with the difficulty command: %available%",
                "%user% さん、%songName% (%key%) の難易度を difficulty コマンドで指定してください: %available%",
            },
            ["HelpList"] = new[] {
                "Commands: %list%. Add a command name to the help command for details.",
                "コマンド: %list% 詳しくは help コマンドにコマンド名を付けてください",
            },
            ["HelpCommand"] = new[] {
                "%command%: %description% Usage: %usage%",
                "%command%: %description% 使い方: %usage%",
                "%command% command: %description% Usage: %command% command%usage%",
                "%command% コマンド: %description% 使い方: %command% コマンド%usage%",
            },
            ["Usage"] = new[] {
                "Usage: %command% command%usage%",
                "使い方: %command% コマンド%usage%",
            },
            ["PermissionDenied"] = new[] {
                "%command% can be used by: %roles%.",
                "%command% を使えるのは %roles% です",
            },
        };

        private static bool IsOldDefault(string key, string value) =>
            s_oldDefaults.TryGetValue(key, out var olds) && Array.IndexOf(olds, value) >= 0;

        public string Get(string key) => _texts.TryGetValue(key, out var s) ? s : key;

        public bool Has(string key) => _texts.ContainsKey(key);

        /// <summary>%name% を値に置き換える。知らない変数はそのまま残す。</summary>
        public string Format(string key, IDictionary<string, string> vars) => Apply(this.Get(key), vars);

        public static string Apply(string template, IDictionary<string, string> vars)
        {
            if (string.IsNullOrEmpty(template) || vars == null) {
                return template ?? "";
            }
            var sb = new StringBuilder(template.Length + 32);
            var i = 0;
            while (i < template.Length) {
                var start = template.IndexOf('%', i);
                if (start < 0) {
                    sb.Append(template, i, template.Length - i);
                    break;
                }
                var end = template.IndexOf('%', start + 1);
                if (end < 0) {
                    sb.Append(template, i, template.Length - i);
                    break;
                }
                var name = template.Substring(start + 1, end - start - 1);
                if (name.Length > 0 && vars.TryGetValue(name, out var value)) {
                    sb.Append(template, i, start - i).Append(value);
                    i = end + 1;
                }
                else {
                    // 変数でなければ % をそのまま残し、次の % から探し直す
                    sb.Append(template, i, end - i);
                    i = end;
                }
            }
            return sb.ToString();
        }

        private static Dictionary<string, string> English() => new Dictionary<string, string> {
            ["RequestsClosed"] = "Song requests are closed.",
            ["LimitReached"] = "%user%, you have reached your request limit (%limit%).",
            ["TooShort"] = "Please enter at least 3 characters to search.",
            ["BeatSaverUnavailable"] = "BeatSaver is not responding. Please try again later.",
            ["NotFound"] = "No songs found for \"%query%\".",
            ["InvalidKey"] = "\"%query%\" is not a valid BeatSaver key.",
            ["Candidates"] = "Several songs found: %list%. Please request with the key.",
            ["CandidateItem"] = "%songName% / %levelAuthorName% (%key%)",
            ["TooMany"] = "Too many results for \"%query%\". Please add words or use the key.",
            ["FilterInQueue"] = "%songName% (%key%) is already in the queue.",
            ["FilterBlocked"] = "%songName% (%key%) is blocked.",
            ["FilterMapper"] = "Songs by %levelAuthorName% are not accepted.",
            ["FilterDuplicate"] = "%songName% (%key%) has already been requested this session.",
            ["FilterLength"] = "%songName% (%key%) is too long (%length%, limit %limit% min).",
            ["FilterNjs"] = "%songName% (%key%) is below the minimum NJS (%njs%, limit %limit%).",
            ["FilterRating"] = "%songName% (%key%) is below the minimum rating (%rating%, limit %limit%).",
            ["Added"] = "Request %songName% %songSubName% / %levelAuthorName% %rating% (%key%) added to queue.",
            ["AddedDifficulty"] = " Difficulty: %difficulty%",
            ["DifficultyPrompt"] = "%user%, please choose a difficulty for %songName% (%key%) with !difficulty (!d): %available%",
            ["DifficultySet"] = "%user%, the difficulty of %songName% (%key%) is set to %difficulty%.",
            ["DifficultyUnknown"] = "%user%, \"%input%\" is not available for %songName% (%key%). Available: %available%",
            ["DifficultyPending"] = "%user%, \"%input%\" will be used for your next request (within 30 seconds).",
            ["WrongSongDone"] = "%songName% (%key%) was removed from the queue.",
            ["NoRequestsInQueue"] = "%user%, you have no requests in the queue.",
            ["QueueEmpty"] = "The queue is empty.",
            ["QueueList"] = "Queue: %list%",
            ["QueueItem"] = "%songName% (%key%)",
            ["QueueMore"] = " and %count% more",
            ["Link"] = "%songName% %songSubName% / %songAuthorName% (%key%) %beatsaverLink%",
            ["LinkNone"] = "No song is selected.",
            ["LookupResult"] = "%count% songs found: %list%",
            ["LookupItem"] = "%songName% / %levelAuthorName% %rating% (%key%)",
            ["QueueStatus"] = "Requests are %state%. %count% songs in the queue, total %length%.",
            ["StateOpen"] = "open",
            ["StateClosed"] = "closed",
            ["HelpList"] = "Commands: %list%. Use !help <command> for details.",
            ["HelpCommand"] = "%command% command: %description% Usage: %usage%",
            ["Usage"] = "Usage: %usage%",
            ["PermissionDenied"] = "The %command% command can be used by: %roles%.",
            ["About"] = "TTVStreamersKit %version%. Designed after EnhancedStreamChat and SongRequestManagerV2 by denpadokei and angturil.",
            ["QueueOpened"] = "Song requests are now open.",
            ["QueueClosed"] = "Song requests are now closed.",
            ["RemoveDone"] = "%songName% (%key%) was removed from the queue.",
            ["RemoveNotFound"] = "No song matching \"%query%\" is in the queue.",
            ["ClearQueueDone"] = "The queue was cleared (%count% songs).",
            ["BlockAdded"] = "%songName% (%key%) is now blocked.",
            ["BlockAddedKeyOnly"] = "%key% is now blocked.",
            ["BlockExists"] = "%key% is already blocked.",
            ["UnblockDone"] = "%key% is no longer blocked.",
            ["UnblockNotFound"] = "%key% is not blocked.",
            ["NextSong"] = "Next song: %songName% %songSubName% / %levelAuthorName% (%key%) requested by %user%",
            ["DownloadFailed"] = "Failed to download %songName% (%key%).",
            ["ObsQueueLine"] = "%songName% / %user%",
            ["ObsQueueOpen"] = "Requests are open",
            ["ObsQueueClosed"] = "Requests are closed",
            ["RoleEveryone"] = "everyone",
            ["RoleSubscriber"] = "subscribers",
            ["RoleVip"] = "VIPs",
            ["RoleModerator"] = "moderators",
            ["RoleBroadcaster"] = "the broadcaster",
            ["Help.bsr"] = "Request a song by BeatSaver key or by search words.",
            ["Help.bsrd"] = "Request a song by key with a difficulty.",
            ["Help.difficulty"] = "Set the difficulty of your latest request.",
            ["Help.wrongsong"] = "Remove your latest request from the queue.",
            ["Help.queue"] = "Show the songs in the queue.",
            ["Help.link"] = "Show the current song and its BeatSaver link.",
            ["Help.help"] = "Show the commands you can use, or the details of a command.",
            ["Help.lookup"] = "Search BeatSaver without adding to the queue.",
            ["Help.open"] = "Open song requests.",
            ["Help.close"] = "Close song requests.",
            ["Help.queuestatus"] = "Show whether requests are open, the number of songs and the total length.",
            ["Help.modadd"] = "Add a song to the end of the queue, ignoring the filters.",
            ["Help.att"] = "Add a song to the top of the queue, ignoring the filters.",
            ["Help.remove"] = "Remove a song from the queue.",
            ["Help.clearqueue"] = "Remove all songs from the queue.",
            ["Help.block"] = "Block a song.",
            ["Help.unblock"] = "Unblock a song.",
            ["Help.about"] = "Show the version and credits.",
        };

        private static Dictionary<string, string> Japanese() => new Dictionary<string, string> {
            ["RequestsClosed"] = "リクエストの受付は停止中です",
            ["LimitReached"] = "%user% さんはリクエストの上限（%limit% 曲）に達しています",
            ["TooShort"] = "検索するには 3 文字以上を入力してください",
            ["BeatSaverUnavailable"] = "BeatSaver が応答しません。しばらくしてからもう一度お試しください",
            ["NotFound"] = "「%query%」に一致する曲は見つかりませんでした",
            ["InvalidKey"] = "「%query%」は有効な BeatSaver のキーではありません",
            ["Candidates"] = "複数の曲が見つかりました: %list% キーでリクエストしてください",
            ["CandidateItem"] = "%songName% / %levelAuthorName% (%key%)",
            ["TooMany"] = "「%query%」に一致する曲が多すぎます。語を足すか、キーでリクエストしてください",
            ["FilterInQueue"] = "%songName% (%key%) はすでにキューにあります",
            ["FilterBlocked"] = "%songName% (%key%) はブロックされています",
            ["FilterMapper"] = "%levelAuthorName% さんの譜面は受け付けていません",
            ["FilterDuplicate"] = "%songName% (%key%) はこのセッションですでにリクエストされています",
            ["FilterLength"] = "%songName% (%key%) は長すぎます（%length%、上限 %limit% 分）",
            ["FilterNjs"] = "%songName% (%key%) の NJS が下限に届きません（%njs%、下限 %limit%）",
            ["FilterRating"] = "%songName% (%key%) の評価が下限に届きません（%rating%、下限 %limit%）",
            ["Added"] = "%songName% %songSubName% / %levelAuthorName% %rating% (%key%) をキューに追加しました",
            ["AddedDifficulty"] = "（難易度: %difficulty%）",
            ["DifficultyPrompt"] = "%user% さん、%songName% (%key%) の難易度を !difficulty (!d) で指定してください: %available%",
            ["DifficultySet"] = "%user% さん、%songName% (%key%) の難易度を %difficulty% にしました",
            ["DifficultyUnknown"] = "%user% さん、%songName% (%key%) に「%input%」はありません。指定できるもの: %available%",
            ["DifficultyPending"] = "%user% さん、次のリクエスト（30 秒以内）に「%input%」を使います",
            ["WrongSongDone"] = "%songName% (%key%) をキューから外しました",
            ["NoRequestsInQueue"] = "%user% さんのリクエストはキューにありません",
            ["QueueEmpty"] = "キューは空です",
            ["QueueList"] = "キュー: %list%",
            ["QueueItem"] = "%songName% (%key%)",
            ["QueueMore"] = " ほか %count% 曲",
            ["Link"] = "%songName% %songSubName% / %songAuthorName% (%key%) %beatsaverLink%",
            ["LinkNone"] = "選んでいる曲はありません",
            ["LookupResult"] = "%count% 曲見つかりました: %list%",
            ["LookupItem"] = "%songName% / %levelAuthorName% %rating% (%key%)",
            ["QueueStatus"] = "リクエストは%state%です。キューに %count% 曲、合計 %length%",
            ["StateOpen"] = "受付中",
            ["StateClosed"] = "停止中",
            ["HelpList"] = "コマンド: %list% 詳しくは !help <コマンド名>",
            ["HelpCommand"] = "%command% コマンド: %description% 使い方: %usage%",
            ["Usage"] = "使い方: %usage%",
            ["PermissionDenied"] = "%command% コマンドを使えるのは %roles% です",
            ["About"] = "TTVStreamersKit %version% 設計の元になった MOD: EnhancedStreamChat、SongRequestManagerV2（denpadokei 氏、angturil 氏）",
            ["QueueOpened"] = "リクエストの受付を始めました",
            ["QueueClosed"] = "リクエストの受付を停止しました",
            ["RemoveDone"] = "%songName% (%key%) をキューから外しました",
            ["RemoveNotFound"] = "「%query%」に一致する曲はキューにありません",
            ["ClearQueueDone"] = "キューを空にしました（%count% 曲）",
            ["BlockAdded"] = "%songName% (%key%) をブロックしました",
            ["BlockAddedKeyOnly"] = "%key% をブロックしました",
            ["BlockExists"] = "%key% はすでにブロックしています",
            ["UnblockDone"] = "%key% のブロックを解除しました",
            ["UnblockNotFound"] = "%key% はブロックしていません",
            ["NextSong"] = "次の曲: %songName% %songSubName% / %levelAuthorName% (%key%) リクエスト: %user%",
            ["DownloadFailed"] = "%songName% (%key%) のダウンロードに失敗しました",
            ["ObsQueueLine"] = "%songName% / %user%",
            ["ObsQueueOpen"] = "リクエスト受付中",
            ["ObsQueueClosed"] = "リクエスト停止中",
            ["RoleEveryone"] = "全員",
            ["RoleSubscriber"] = "サブスク",
            ["RoleVip"] = "VIP",
            ["RoleModerator"] = "モデレーター",
            ["RoleBroadcaster"] = "配信者",
            ["Help.bsr"] = "BeatSaver のキーか検索の語で曲をリクエストします",
            ["Help.bsrd"] = "キーと難易度を指定して曲をリクエストします",
            ["Help.difficulty"] = "自分の最新のリクエストの難易度を指定します",
            ["Help.wrongsong"] = "自分の最新のリクエストを取り消します",
            ["Help.queue"] = "キューの曲を表示します",
            ["Help.link"] = "今の曲と BeatSaver のリンクを表示します",
            ["Help.help"] = "使えるコマンドの一覧か、コマンドの説明を表示します",
            ["Help.lookup"] = "キューに追加せずに BeatSaver を検索します",
            ["Help.open"] = "リクエストの受付を始めます",
            ["Help.close"] = "リクエストの受付を停止します",
            ["Help.queuestatus"] = "受付の状態、曲数、合計の長さを表示します",
            ["Help.modadd"] = "フィルターを無視してキューの末尾に追加します",
            ["Help.att"] = "フィルターを無視してキューの先頭に追加します",
            ["Help.remove"] = "キューの曲を外します",
            ["Help.clearqueue"] = "キューを空にします",
            ["Help.block"] = "曲をブロックします",
            ["Help.unblock"] = "曲のブロックを解除します",
            ["Help.about"] = "バージョンとクレジットを表示します",
        };
    }
}
