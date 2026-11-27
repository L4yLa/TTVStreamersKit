using System;
using System.Collections.Generic;
using System.Linq;
using TTVCore.Api.V1;
using TTVStreamersKit.Configuration;

namespace TTVStreamersKit.Utilities
{
    /// <summary>
    /// 設定ファイルの IgnoredUsers（ログイン名、大文字と小文字は区別しない）に書いたユーザーを無視する。
    /// チャットの表示、エモートの演出、リクエストのコマンド、通知のすべてで使う。
    /// 通知は送った人（サブスクした人、ギフトを贈った人、レイドした人など）で判断し、受け取った人では判断しない。
    /// </summary>
    internal static class IgnoredUsers
    {
        private static PluginConfig s_config;
        // 設定の変更は別のスレッドから届くことがあるため、作り直すときは参照ごと差し替える
        private static volatile HashSet<string> s_set;
        private static volatile string s_signature = "";

        public static void Initialize(PluginConfig config)
        {
            s_config = config;
            s_set = null;
            config.OnConfigChanged += () => s_set = null;
        }

        /// <summary>指定の内容を表す文字列。変わったかどうかの判断に使う。</summary>
        public static string Signature
        {
            get {
                _ = Set;
                return s_signature;
            }
        }

        private static HashSet<string> Set
        {
            get {
                var set = s_set;
                if (set != null) {
                    return set;
                }
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try {
                    var list = s_config?.IgnoredUsers;
                    if (list != null) {
                        foreach (var name in list.ToArray()) {
                            var login = Normalize(name);
                            if (login.Length > 0) {
                                set.Add(login);
                            }
                        }
                    }
                }
                catch (Exception e) {
                    // 設定の読み込み中に一覧が変わった場合など。次に参照したときに作り直す
                    Plugin.Log.Warn($"Failed to read IgnoredUsers: {e.Message}");
                    return set;
                }
                s_signature = string.Join(",", set.Select(x => x.ToLowerInvariant()).OrderBy(x => x, StringComparer.Ordinal));
                s_set = set;
                return set;
            }
        }

        // 前後の空白と、先頭の @ を除く
        private static string Normalize(string name) => (name ?? "").Trim().TrimStart('@').Trim();

        public static bool Contains(string login) => !string.IsNullOrEmpty(login) && Set.Contains(login);

        public static bool Contains(ChatUser user) => user != null && Contains(user.Login);

        public static bool Contains(UserRef user) => user != null && Contains(user.Login);

        public static bool IsIgnored(ChatMessage message) => message != null && Contains(message.Sender);

        /// <summary>表示用の流れの1件が、無視するユーザーのものか。</summary>
        public static bool IsIgnored(TimelineItem item)
        {
            switch (item) {
                case TimelineMessage m:
                    return IsIgnored(m.Message);
                case TimelineNotification n:
                    return Contains(SenderOf(n));
                default:
                    return false;
            }
        }

        /// <summary>通知を送った人のログイン名。分からない（ハイプトレインなど）ときは null。</summary>
        private static string SenderOf(TimelineNotification n)
        {
            if (!string.IsNullOrEmpty(n.Notice?.Chatter?.Login)) {
                return n.Notice.Chatter.Login;
            }
            if (!string.IsNullOrEmpty(n.Message?.Sender?.Login)) {
                return n.Message.Sender.Login;
            }
            switch (n.Notification) {
                case SubscribeNotification s:
                    return s.User?.Login;
                case ResubscribeNotification r:
                    return r.User?.Login;
                case SubscriptionGiftNotification g:
                    return g.Gifter?.Login;
                case RaidNotification r:
                    return r.FromBroadcaster?.Login;
                case BitsUseNotification b:
                    return b.User?.Login;
                case ChannelPointsRedemptionNotification c:
                    return c.User?.Login;
                default:
                    return null;
            }
        }
    }
}
