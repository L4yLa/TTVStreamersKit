using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using System.Collections.Generic;
using TTVStreamersKit.Requests;
using TTVStreamersKit.Utilities;

namespace TTVStreamersKit.UI
{
    /// <summary>リクエスト機能の設定。値は変えたときにすぐ設定ファイルへ反映する。</summary>
    internal sealed class RequestSettingsViewController : BSMLResourceViewController
    {
        // 画面は言語を変えると作り直すため、作ったときの言語で決める
        private readonly string _linkRequestOnly = Localization.T("Requested song", "リクエストのみ");
        private readonly string _linkAll = Localization.T("Selected song", "選んでいる曲");

        public override string ResourceName => "TTVStreamersKit.UI.RequestSettingsView.bsml";

        public override string Content => Localization.Bsml(base.Content);

        private static RequestConfig C => Plugin.Config.Request;

        [UIValue("lowest-rating")]
        private float LowestRating { get => C.LowestAllowedRating; set => C.LowestAllowedRating = value; }

        [UIValue("max-length")]
        private int MaxLength { get => C.MaximumSongLength; set => C.MaximumSongLength = value; }

        [UIValue("min-njs")]
        private float MinNjs { get => C.MinimumNJS; set => C.MinimumNJS = value; }

        [UIValue("user-limit")]
        private int UserLimit { get => C.UserRequestLimit; set => C.UserRequestLimit = value; }

        [UIValue("sub-limit")]
        private int SubLimit { get => C.SubRequestLimit; set => C.SubRequestLimit = value; }

        [UIValue("mod-limit")]
        private int ModLimit { get => C.ModRequestLimit; set => C.ModRequestLimit = value; }

        [UIValue("vip-bonus")]
        private int VipBonus { get => C.VipBonusRequests; set => C.VipBonusRequests = value; }

        [UIValue("mod-full-rights")]
        private bool ModFullRights { get => C.ModFullRights; set => C.ModFullRights = value; }

        [UIValue("limit-to-session")]
        private bool LimitToSession { get => C.LimitUserRequestsToSession; set => C.LimitUserRequestsToSession = value; }

        [UIValue("session-hours")]
        private int SessionHours { get => C.SessionResetAfterXHours; set => C.SessionResetAfterXHours = value; }

        [UIValue("notify-sound")]
        private bool NotifySound { get => C.NotifySound; set => C.NotifySound = value; }

        [UIValue("sound-volume")]
        private int SoundVolume { get => C.SoundVolume; set => C.SoundVolume = value; }

        [UIValue("performance-mode")]
        private bool PerformanceMode { get => C.PerformanceMode; set => C.PerformanceMode = value; }

        [UIValue("require-difficulty")]
        private bool RequireDifficulty { get => C.RequireDifficulty; set => C.RequireDifficulty = value; }

        [UIValue("link-types")]
        private List<object> LinkTypes => new List<object> { _linkRequestOnly, _linkAll };

        [UIValue("link-type")]
        private string LinkTypeText
        {
            get => C.LinkType == LinkType.OnlyRequest ? _linkRequestOnly : _linkAll;
            set => C.LinkType = value == _linkRequestOnly ? LinkType.OnlyRequest : LinkType.All;
        }

        [UIValue("shared-chat-commands")]
        private bool SharedChatCommands { get => C.AcceptSharedChatCommands; set => C.AcceptSharedChatCommands = value; }

        [UIValue("bot-prefix")]
        private bool BotPrefix { get => C.BotPrefix; set => C.BotPrefix = value; }
    }
}
