using IPA.Config.Stores.Attributes;
using IPA.Config.Stores.Converters;

namespace TTVStreamersKit.Requests
{
    public enum LinkType
    {
        OnlyRequest,
        All,
    }

    /// <summary>リクエスト機能の設定。</summary>
    public class RequestConfig
    {
        // ===== 設定画面に出すもの =====
        public virtual float LowestAllowedRating { get; set; } = 0f;
        public virtual int MaximumSongLength { get; set; } = 180;
        public virtual float MinimumNJS { get; set; } = 0f;
        public virtual bool BotPrefix { get; set; } = false;
        public virtual int UserRequestLimit { get; set; } = 2;
        public virtual int SubRequestLimit { get; set; } = 5;
        public virtual int ModRequestLimit { get; set; } = 10;
        public virtual int VipBonusRequests { get; set; } = 1;
        public virtual bool ModFullRights { get; set; } = false;
        public virtual bool LimitUserRequestsToSession { get; set; } = false;
        public virtual int SessionResetAfterXHours { get; set; } = 6;
        public virtual bool NotifySound { get; set; } = false;
        public virtual int SoundVolume { get; set; } = 50;
        public virtual bool PerformanceMode { get; set; } = false;
        public virtual bool RequireDifficulty { get; set; } = false;
        [UseConverter(typeof(EnumConverter<LinkType>))]
        public virtual LinkType LinkType { get; set; } = LinkType.All;

        // ===== 設定ファイルだけにあるもの =====
        public virtual bool RequestQueueOpen { get; set; } = true;
        public virtual bool AutoOpenRequestQueue { get; set; } = true;
        public virtual int RequestHistoryLimit { get; set; } = 40;
        public virtual bool UpdateQueueStatusFiles { get; set; } = true;
        public virtual int MaximumQueueTextEntries { get; set; } = 8;
        public virtual int MaximumQueueMessages { get; set; } = 1;
        /// <summary>リクエスト（!bsr など）の検索結果の並べ替えの順。</summary>
        public virtual string AddSortOrder { get; set; } = SortOrder.Default;
        /// <summary>!lookup の結果の並べ替えの順。</summary>
        public virtual string LookupSortOrder { get; set; } = SortOrder.Default;
        /// <summary>キューの画面の検索の結果の並べ替えの順。</summary>
        public virtual string SearchSortOrder { get; set; } = SortOrder.Default;
        /// <summary>共有チャットで他のチャンネルから届いたコマンドを受け付けるか。</summary>
        public virtual bool AcceptSharedChatCommands { get; set; } = false;
        /// <summary>選曲画面のリクエストボタンの位置（曲の一覧の画面の中心からの距離）。</summary>
        /// 他の MOD の表示と重なるとポインターが当たらないため、重ならない位置にする。
        public virtual float RequestButtonX { get; set; } = 70f;
        public virtual float RequestButtonY { get; set; } = 80f;
    }
}
