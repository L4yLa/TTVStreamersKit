using System;
using System.Globalization;
using System.Reflection;
using TTVCore.Api.V1;
using TTVStreamersKit.Utilities;

namespace TTVStreamersKit.Chat
{
    /// <summary>
    /// チャット上の通知（サブスク、ギフト、レイドなど）の文言を、設定の言語で作る。
    /// Twitch の文言（SystemMessage）は英語でしか届かないため使わない。
    /// </summary>
    internal static class NoticeText
    {
        /// <summary>通知の文言。作れない種類（Unknown）は null。</summary>
        public static string Compose(ChatNotice notice)
        {
            if (notice == null) {
                return null;
            }
            var user = notice.IsAnonymous || notice.Chatter == null ? Anonymous : DisplayName(notice.Chatter);
            var d = notice.Detail;
            switch (notice.NoticeType) {
                case ChatNoticeType.Sub:
                    return Sub(user, d as SubNoticeDetail);
                case ChatNoticeType.Resub:
                    return Resub(user, d as ResubNoticeDetail);
                case ChatNoticeType.SubGift:
                    return SubGift(user, d as SubGiftNoticeDetail);
                case ChatNoticeType.CommunitySubGift: {
                    var g = d as CommunitySubGiftNoticeDetail;
                    var tier = TierText(g?.Tier ?? SubTier.Unknown, false);
                    var total = g?.Total ?? 0;
                    return Localization.T(
                        $"{user} is gifting {total} {tier} subs to the community",
                        $"{user} がコミュニティに {tier} のサブスクを {total} 件ギフトしています");
                }
                case ChatNoticeType.GiftPaidUpgrade: {
                    var gifter = GifterName(d as GiftPaidUpgradeNoticeDetail);
                    return Localization.T(
                        $"{user} is continuing the gift sub from {gifter}",
                        $"{user} が {gifter} から贈られたサブスクを継続しました");
                }
                case ChatNoticeType.PayItForward: {
                    var gifter = GifterName(d as GiftPaidUpgradeNoticeDetail);
                    return Localization.T(
                        $"{user} is paying forward the gift from {gifter}",
                        $"{user} が {gifter} から受け取ったギフトを恩送りしています");
                }
                case ChatNoticeType.PrimePaidUpgrade: {
                    var tier = TierText((d as PrimePaidUpgradeNoticeDetail)?.Tier ?? SubTier.Unknown, false);
                    return Localization.T($"{user} upgraded from Prime to {tier}", $"{user} が Prime から {tier} に切り替えました");
                }
                case ChatNoticeType.Raid: {
                    var raid = d as RaidNoticeDetail;
                    var raider = raid?.Raider != null ? Name(raid.Raider) : user;
                    var viewers = raid?.ViewerCount ?? 0;
                    return Localization.T($"{raider} is raiding with {viewers} viewers", $"{raider} が {viewers} 人でレイドしています");
                }
                case ChatNoticeType.Unraid:
                    return Localization.T("The raid was canceled", "レイドが取り消されました");
                case ChatNoticeType.Announcement:
                    // 本文は2行目に出す
                    return Localization.T("Announcement", "アナウンス");
                case ChatNoticeType.BitsBadgeTier: {
                    var tier = (d as BitsBadgeTierNoticeDetail)?.Tier ?? 0;
                    return Localization.T($"{user} earned a {tier} Bits badge", $"{user} が {tier} の Bits バッジを獲得しました");
                }
                case ChatNoticeType.CharityDonation: {
                    var c = d as CharityDonationNoticeDetail;
                    var amount = c != null ? Amount(c) : "";
                    var charity = c?.CharityName ?? "";
                    return Localization.T($"{user} donated {amount} to {charity}", $"{user} が {charity} に {amount} を寄付しました");
                }
                case ChatNoticeType.WatchStreak:
                    return WatchStreak(user, d);
                case ChatNoticeType.Modiversary: {
                    var months = IntOf(d, "ModiversaryNoticeDetail", "Months");
                    return months.HasValue
                        ? Localization.T($"{user} has been a moderator for {months} months", $"{user} がモデレーターになって {months} か月になりました")
                        : Localization.T($"{user} is celebrating a modiversary", $"{user} がモデレーターの記念日を迎えました");
                }
                case ChatNoticeType.GiftedDropsSummary: {
                    var count = IntOf(d, "GiftedDropsSummaryNoticeDetail", "RecipientCount");
                    return count.HasValue
                        ? Localization.T($"{user} gifted Drops to {count} viewers", $"{user} が {count} 人にドロップをギフトしました")
                        : Localization.T($"{user} gifted Drops", $"{user} がドロップをギフトしました");
                }
                default:
                    return null;
            }
        }

        private static string Anonymous => Localization.T("Anonymous", "匿名");

        private static string Sub(string user, SubNoticeDetail s)
        {
            var text = s != null && s.IsPrime
                ? Localization.T($"{user} subscribed with Prime", $"{user} が Prime でサブスクしました")
                : Localization.T($"{user} subscribed with {TierText(s?.Tier ?? SubTier.Unknown, false)}",
                                 $"{user} が {TierText(s?.Tier ?? SubTier.Unknown, false)} でサブスクしました");
            if (s != null && s.DurationMonths > 1) {
                text += Localization.T($" ({s.DurationMonths} months)", $"（{s.DurationMonths} か月分）");
            }
            return text;
        }

        private static string Resub(string user, ResubNoticeDetail r)
        {
            var tier = TierText(r?.Tier ?? SubTier.Unknown, r != null && r.IsPrime);
            var months = r?.CumulativeMonths ?? 0;
            var streak = r?.StreakMonths;
            // 連続の月数は非公開なら届かない。累計と同じなら省く
            return streak.HasValue && streak.Value > 0 && streak.Value != months
                ? Localization.T(
                    $"{user} resubscribed with {tier} ({months} months, {streak.Value}-month streak)",
                    $"{user} が {tier} でサブスクを継続しました（累計 {months} か月、連続 {streak.Value} か月）")
                : Localization.T(
                    $"{user} resubscribed with {tier} ({months} months)",
                    $"{user} が {tier} でサブスクを継続しました（累計 {months} か月）");
        }

        private static string SubGift(string user, SubGiftNoticeDetail g)
        {
            var tier = TierText(g?.Tier ?? SubTier.Unknown, false);
            var recipient = g?.Recipient != null ? Name(g.Recipient) : "";
            var text = Localization.T($"{user} gifted a {tier} sub to {recipient}", $"{user} が {recipient} に {tier} のサブスクをギフトしました");
            // 累計は匿名のギフトでは届かない
            if (g?.CumulativeTotal is int total && total > 0) {
                text += Localization.T($" ({total} gifts in total)", $"（累計 {total} 件）");
            }
            return text;
        }

        /// <summary>
        /// 連続視聴。詳細（WatchStreakNoticeDetail）は TTVCore 0.2.2 で加わったため、型を直接参照せずに読む。
        /// 0.2.1 以下では詳細が届かないので、回数のない文言にする。
        /// </summary>
        private static string WatchStreak(string user, ChatNoticeDetail d)
        {
            var count = IntOf(d, "WatchStreakNoticeDetail", "StreakCount");
            if (!count.HasValue) {
                return Localization.T($"{user} shared a watch streak", $"{user} が連続視聴を共有しました");
            }
            var text = Localization.T($"{user} watched {count} streams in a row", $"{user} が {count} 回連続で視聴しました");
            var points = IntOf(d, "WatchStreakNoticeDetail", "ChannelPointsAwarded");
            if (points.HasValue && points.Value > 0) {
                text += Localization.T($" (+{points} points)", $"（{points} ポイント獲得）");
            }
            return text;
        }

        /// <summary>
        /// TTVCore 0.2.2 で加わった詳細の値を、名前で読む。型を直接参照すると、0.2.1 以下では型が見つからず
        /// このクラスを使う処理ごと失敗するため。型が違う、または値が読めないときは null。
        /// </summary>
        private static int? IntOf(ChatNoticeDetail d, string typeName, string property)
        {
            if (d == null || d.GetType().Name != typeName) {
                return null;
            }
            try {
                var p = d.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public);
                return p?.GetValue(d) is int value ? value : (int?)null;
            }
            catch (Exception e) {
                Plugin.Log.Debug($"Chat: failed to read {typeName}.{property}: {e.Message}");
                return null;
            }
        }

        private static string GifterName(GiftPaidUpgradeNoticeDetail g) =>
            g == null || g.GifterIsAnonymous || g.Gifter == null ? Anonymous : Name(g.Gifter);

        /// <summary>金額を小数点以下の桁数に合わせ、通貨のコードを付ける（例: 5.00 USD、500 JPY）。</summary>
        private static string Amount(CharityDonationNoticeDetail c)
        {
            var places = Math.Max(0, Math.Min(c.AmountDecimalPlaces, 8));
            var value = c.AmountValue / (decimal)Math.Pow(10, places);
            var number = value.ToString("F" + places.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(c.AmountCurrency) ? number : $"{number} {c.AmountCurrency}";
        }

        /// <summary>"Tier 1" から "Tier 3"、Prime なら "Prime"。</summary>
        private static string TierText(SubTier tier, bool prime)
        {
            if (prime) {
                return "Prime";
            }
            switch (tier) {
                case SubTier.Tier1: return "Tier 1";
                case SubTier.Tier2: return "Tier 2";
                case SubTier.Tier3: return "Tier 3";
                default: return "Tier ?";
            }
        }

        private static string DisplayName(ChatUser user) =>
            !string.IsNullOrEmpty(user.DisplayName) ? user.DisplayName : user.Login ?? "";

        private static string Name(UserRef user) =>
            !string.IsNullOrEmpty(user.DisplayName) ? user.DisplayName : user.Login ?? "";
    }
}
