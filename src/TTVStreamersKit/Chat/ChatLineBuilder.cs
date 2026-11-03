// Copyright (c) 2020 brian91292, (c) 2026 L4yLa. MIT License.
// 名前の色の扱いは EnhancedStreamChat の ChatMessageBuilder を元にしている。
using System;
using System.Collections.Generic;
using System.Globalization;
using TTVCore.Api.V1;
using UnityEngine;
using TTVStreamersKit.Utilities;

namespace TTVStreamersKit.Chat
{
    /// <summary>TTVCore の表示用の流れ（Timeline）の1件から、チャット表示の内容を作る。</summary>
    internal static class ChatLineBuilder
    {
        private const string SystemColor = "#bbbbbbff";
        // 巨大化エモートの倍率（Twitch と同じく通常の 4 倍）
        private const float GigantifiedScale = 4f;

        private static readonly Dictionary<string, Color> s_randomColors = new Dictionary<string, Color>();
        private static readonly System.Random s_random = new System.Random();

        public static ChatLine Build(TimelineItem item)
        {
            if (item is TimelineMessage tm) {
                return tm.Message != null ? FromMessage(tm.Message) : null;
            }
            if (item is TimelineNotification tn) {
                return FromNotification(tn);
            }
            return null;
        }

        // ================= 通常のメッセージ =================

        private static ChatLine FromMessage(ChatMessage m)
        {
            var line = MessageLine(m);
            line.Main = b => AppendMessage(b, m);
            switch (m.MessageType) {
                case ChatMessageType.ChannelPointsHighlighted:
                case ChatMessageType.PowerUpsMessageEffect:
                    line.Highlight = true;
                    line.Accent = true;
                    break;
            }
            line.Ping = m.MentionsBroadcaster && !line.Highlight;
            return line;
        }

        private static ChatLine MessageLine(ChatMessage m) => new ChatLine {
            MessageId = m.MessageId,
            SenderId = m.Sender?.Id,
            Header = b => AppendHeader(b, m),
        };

        /// <summary>バッジ、名前、本文。/me は本文も名前の色にする。</summary>
        private static void AppendMessage(TmpContentBuilder b, ChatMessage m)
        {
            var color = NameColor(m.Sender);
            AppendBadges(b, m);
            if (m.IsAction) {
                b.AppendRichText($"<color={color}><b>").AppendText(DisplayName(m.Sender)).AppendRichText("</b> ");
                AppendBody(b, m);
                b.AppendRichText("</color>");
                return;
            }
            b.AppendRichText($"<color={color}><b>").AppendText(DisplayName(m.Sender)).AppendRichText("</b></color>: ");
            AppendBody(b, m);
        }

        private static void AppendHeader(TmpContentBuilder b, ChatMessage m)
        {
            AppendBadges(b, m);
            b.AppendRichText($"<color={NameColor(m.Sender)}><b>").AppendText(DisplayName(m.Sender)).AppendRichText("</b></color>");
        }

        private static void AppendBody(TmpContentBuilder b, ChatMessage m)
        {
            var fragments = m.Fragments;
            var gigantified = -1;
            if (m.MessageType == ChatMessageType.PowerUpsGigantifiedEmote) {
                // 巨大化するのは最後のエモート
                for (var i = fragments.Count - 1; i >= 0; i--) {
                    if (fragments[i] is EmoteFragment) {
                        gigantified = i;
                        break;
                    }
                }
            }
            for (var i = 0; i < fragments.Count; i++) {
                var f = fragments[i];
                if (f is CheermoteFragment c) {
                    b.AppendFragment(c);
                    b.AppendRichText($" <color={CheerColor(c.Tier)}><size=77%><b>{c.Bits.ToString(CultureInfo.InvariantCulture)} </b></size></color>");
                    continue;
                }
                b.AppendFragment(f, i == gigantified ? GigantifiedScale : 1f);
            }
        }

        private static void AppendBadges(TmpContentBuilder b, ChatMessage m)
        {
            // 表示しない設定なら、どのバッジも付けない
            if (m.Sender == null || Plugin.Config?.ShowBadges == false) {
                return;
            }
            foreach (var badge in m.Sender.Badges) {
                var info = TTVCoreApi.Catalog.GetBadge(m.BroadcasterId, badge);
                if (info == null && m.Source?.Broadcaster != null) {
                    info = TTVCoreApi.Catalog.GetBadge(m.Source.Broadcaster.Id, badge);
                }
                if (info == null || !info.Image.IsValid) {
                    continue;
                }
                b.AppendImage(info.Image, "").AppendRichText(" ");
            }
        }

        // ================= 通知 =================

        private static ChatLine FromNotification(TimelineNotification n)
        {
            switch (n.Kind) {
                case TimelineNotificationKind.Cheer:
                    return Cheer(n);
                case TimelineNotificationKind.PowerUp:
                    return PowerUp(n);
                case TimelineNotificationKind.ChannelPointsRedemption:
                    return Redemption(n);
                case TimelineNotificationKind.HypeTrain:
                    return HypeTrain(n);
                case TimelineNotificationKind.AdBreak:
                    return AdBreak(n);
                case TimelineNotificationKind.Sub:
                case TimelineNotificationKind.Resub:
                case TimelineNotificationKind.SubGift:
                case TimelineNotificationKind.CommunitySubGift:
                case TimelineNotificationKind.Raid:
                case TimelineNotificationKind.ChatNoticeOnly:
                    // 知らない種類のチャット上の通知は表示しない
                    return n.Notice?.NoticeType == ChatNoticeType.Unknown ? null : Notice(n);
                default:
                    // 知らない種類の通知（Twitch 側で増えたものなど）は表示しない
                    return null;
            }
        }

        /// <summary>Cheer は1行（Twitch と同じ）。</summary>
        private static ChatLine Cheer(TimelineNotification n)
        {
            if (n.Message != null) {
                var line = MessageLine(n.Message);
                line.Main = b => AppendMessage(b, n.Message);
                return line;
            }
            var bits = n.Notification as BitsUseNotification;
            var text = Localization.T(
                $"{Name(bits?.User, "Anonymous")} cheered {bits?.Bits ?? 0} bits",
                $"{Name(bits?.User, "匿名")} が {bits?.Bits ?? 0} ビッツを Cheer しました");
            if (!string.IsNullOrEmpty(bits?.MessageText)) {
                text += "  " + bits.MessageText;
            }
            return SystemLine(text, highlight: false);
        }

        private static ChatLine PowerUp(TimelineNotification n)
        {
            var bits = n.Notification as BitsUseNotification;
            var m = n.Message;
            var type = bits?.PowerUp;
            var custom = bits?.Type == BitsUseType.CustomPowerUp;
            if (type == null && !custom && m != null) {
                if (m.MessageType == ChatMessageType.PowerUpsMessageEffect) {
                    type = PowerUpType.MessageEffect;
                }
                else if (m.MessageType == ChatMessageType.PowerUpsGigantifiedEmote) {
                    type = PowerUpType.GigantifyAnEmote;
                }
            }

            // メッセージエフェクトと巨大化エモートは、通知の行を出さずにメッセージの1行だけにする
            // （巨大化はエモートを4倍で、メッセージエフェクトは強調の背景色で表示する）。メッセージが届かなければ何も出さない
            if (!custom && (type == PowerUpType.MessageEffect || type == PowerUpType.GigantifyAnEmote)) {
                if (m == null) {
                    return null;
                }
                var line = FromMessage(m);
                if (type == PowerUpType.MessageEffect) {
                    line.Highlight = true;
                    line.Accent = true;
                    line.Ping = false;
                }
                return line;
            }

            // セレブレーションとカスタム Power-up はチャンネルポイントと同じ「引き換え」の表示
            string title;
            if (custom) {
                title = string.IsNullOrEmpty(bits.CustomPowerUpTitle) ? Localization.T("a custom Power-up", "カスタムパワーアップ") : bits.CustomPowerUpTitle;
            }
            else if (type == PowerUpType.Celebration) {
                title = Localization.T("an On-Screen Celebration", "全画面セレブレーション");
            }
            else {
                // 知らない種類の Power-up は表示しない
                return null;
            }
            var user = bits?.User != null ? Name(bits.User, "") : DisplayName(m?.Sender);
            var text = Localization.T($"{user} redeemed {title}", $"{user} が {title} を引き換えました");
            return RedemptionLine(text, m);
        }

        private static ChatLine Redemption(TimelineNotification n)
        {
            var c = n.Notification as ChannelPointsRedemptionNotification;
            string text;
            if (c != null) {
                text = Localization.T($"{Name(c.User, "")} redeemed {c.RewardTitle}", $"{Name(c.User, "")} が「{c.RewardTitle}」を引き換えました");
            }
            else {
                text = Localization.T(
                    $"{DisplayName(n.Message?.Sender)} redeemed a channel point reward",
                    $"{DisplayName(n.Message?.Sender)} がチャンネルポイントの報酬を引き換えました");
            }
            if (n.Message == null && !string.IsNullOrEmpty(c?.UserInput)) {
                // 入力はあるがチャットのメッセージが届かなかった
                var line = SystemLine(text, highlight: true);
                var input = c.UserInput;
                line.Sub = b => b.AppendText(input);
                return line;
            }
            return RedemptionLine(text, n.Message);
        }

        /// <summary>ハイライト背景の、通知の行とメッセージの行のまとまり。</summary>
        private static ChatLine RedemptionLine(string systemText, ChatMessage m)
        {
            var line = SystemLine(systemText, highlight: true);
            if (m != null) {
                AttachMessage(line, m);
            }
            return line;
        }

        private static ChatLine HypeTrain(TimelineNotification n)
        {
            if (n.Notification is HypeTrainEndNotification end) {
                return SystemLine(Localization.T($"The Hype Train reached level {end.Level}!", $"ハイプトレインはレベル {end.Level} でした！"), highlight: true);
            }
            return SystemLine(Localization.T("The Hype Train has departed!", "ハイプトレインが発車しました！"), highlight: true);
        }

        private static ChatLine AdBreak(TimelineNotification n)
        {
            var ad = n.Notification as AdBreakNotification;
            if (ad == null) {
                return null;
            }
            // 開始時と終了の目安の時刻に、別々の通知として届く。表示し直しても開始の行は開始のまま
            var adText = ad.IsEstimatedEnd
                ? Localization.T("Ad break ended", "広告が終わりました")
                : Localization.T($"Ad break started ({ad.DurationSeconds} s)", $"広告が始まりました（{ad.DurationSeconds} 秒）");
            return SystemLine(adText, highlight: false);
        }

        /// <summary>サブスク、ギフト、レイド、アナウンスなど。チャット上の通知の文言を優先する。</summary>
        private static ChatLine Notice(TimelineNotification n)
        {
            var text = n.Notice?.SystemMessage;
            if (string.IsNullOrEmpty(text)) {
                text = ComposeNoticeText(n);
            }
            if (string.IsNullOrEmpty(text)) {
                return null;
            }
            var line = SystemLine(text, highlight: true);
            if (n.Message != null) {
                AttachMessage(line, n.Message);
            }
            else if (n.Notice != null && n.Notice.Fragments.Count > 0) {
                var notice = n.Notice;
                line.Sub = b => {
                    if (notice.Chatter != null) {
                        b.AppendRichText($"<color={NameColor(notice.Chatter)}><b>").AppendText(DisplayName(notice.Chatter)).AppendRichText("</b></color>: ");
                    }
                    b.AppendFragments(notice.Fragments);
                };
            }
            else if (n.Notification is ResubscribeNotification r && !string.IsNullOrEmpty(r.MessageText)) {
                var message = r.MessageText;
                line.Sub = b => b.AppendText(message);
            }
            return line;
        }

        /// <summary>チャット上の通知が届かなかったときの文言。</summary>
        private static string ComposeNoticeText(TimelineNotification n)
        {
            var x = n.Notification;
            if (x is SubscribeNotification s) {
                return s.IsGift
                    ? Localization.T($"{Name(s.User, "")} received a gift sub ({TierText(s.Tier)})", $"{Name(s.User, "")} がギフトのサブスクを受け取りました（{TierText(s.Tier)}）")
                    : Localization.T($"{Name(s.User, "")} subscribed ({TierText(s.Tier)})", $"{Name(s.User, "")} がサブスクしました（{TierText(s.Tier)}）");
            }
            if (x is ResubscribeNotification r) {
                return Localization.T(
                    $"{Name(r.User, "")} resubscribed ({TierText(r.Tier)}, {r.CumulativeMonths} months)",
                    $"{Name(r.User, "")} がサブスクを継続しました（{TierText(r.Tier)}、{r.CumulativeMonths} か月）");
            }
            if (x is SubscriptionGiftNotification g) {
                var gifter = g.IsAnonymous ? Localization.T("Anonymous", "匿名") : Name(g.Gifter, Localization.T("Anonymous", "匿名"));
                return Localization.T(
                    $"{gifter} gifted {g.Total} subs ({TierText(g.Tier)})",
                    $"{gifter} がサブスクを {g.Total} 件ギフトしました（{TierText(g.Tier)}）");
            }
            if (x is RaidNotification raid) {
                return Localization.T(
                    $"{Name(raid.FromBroadcaster, "")} raided with {raid.Viewers} viewers",
                    $"{Name(raid.FromBroadcaster, "")} が {raid.Viewers} 人でレイドしました");
            }
            return null;
        }

        private static void AttachMessage(ChatLine line, ChatMessage m)
        {
            line.MessageId = m.MessageId;
            line.SenderId = m.Sender?.Id;
            line.MessageInSub = true;
            line.Header = b => AppendHeader(b, m);
            line.Sub = b => AppendMessage(b, m);
        }

        private static ChatLine SystemLine(string text, bool highlight) => new ChatLine {
            Main = b => b.AppendRichText($"<color={SystemColor}>").AppendText(text).AppendRichText("</color>"),
            Highlight = highlight,
            Accent = highlight,
            ShowTimestamp = false,
        };

        // ================= 共通 =================

        private static string DisplayName(ChatUser user) =>
            user == null ? "" : !string.IsNullOrEmpty(user.DisplayName) ? user.DisplayName : user.Login ?? "";

        private static string Name(UserRef user, string anonymous) =>
            user == null ? anonymous : !string.IsNullOrEmpty(user.DisplayName) ? user.DisplayName : user.Login ?? anonymous;

        private static string TierText(SubTier tier)
        {
            switch (tier) {
                case SubTier.Tier1: return "Tier 1";
                case SubTier.Tier2: return "Tier 2";
                case SubTier.Tier3: return "Tier 3";
                default: return "Tier ?";
            }
        }

        /// <summary>名前の色。未設定の利用者には利用者ごとに固定の色を割り当て、暗い色は明るくする。</summary>
        private static string NameColor(ChatUser user)
        {
            if (user == null) {
                return "#FFFFFFFF";
            }
            Color color;
            if (string.IsNullOrEmpty(user.Color) || !ColorUtility.TryParseHtmlString(user.Color, out color)) {
                var key = user.Id ?? user.Login ?? "";
                if (!s_randomColors.TryGetValue(key, out color)) {
                    color = new Color((float)s_random.NextDouble(), (float)s_random.NextDouble(), (float)s_random.NextDouble());
                    s_randomColors[key] = color;
                }
            }
            Color.RGBToHSV(color, out var h, out var s, out var v);
            if (v < 0.85f) {
                color = Color.HSVToRGB(h, s, 0.85f);
            }
            return "#" + ColorUtility.ToHtmlStringRGBA(color);
        }

        /// <summary>Cheermote の段階ごとの数字の色。</summary>
        private static string CheerColor(int tier)
        {
            if (tier >= 10000) {
                return "#F43021";
            }
            if (tier >= 5000) {
                return "#0099FE";
            }
            if (tier >= 1000) {
                return "#1DB2A5";
            }
            if (tier >= 100) {
                return "#9C3EE8";
            }
            return "#979797";
        }
    }
}
