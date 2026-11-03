using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TTVStreamersKit.Requests
{
    internal sealed partial class RequestManager
    {
        private enum AddMode
        {
            /// <summary>!bsr。すべてのフィルターを通す。</summary>
            Normal,
            /// <summary>!bsrd。キー指定専用で、ID で見つからなくても文字列検索に切り替えない。</summary>
            KeyOnly,
            /// <summary>!modadd。キューの重複だけを判定し、末尾に追加する。</summary>
            ModAdd,
            /// <summary>!att。キューの重複だけを判定し、先頭に追加する。</summary>
            AddToTop,
        }

        // ================= 受付 =================

        private void BeginRequest(CommandContext ctx, string query, AddMode mode)
        {
            var bypassClosed = ctx.IsLocal || mode == AddMode.ModAdd || mode == AddMode.AddToTop;
            if (!_config.RequestQueueOpen && !bypassClosed) {
                this.Reply("RequestsClosed");
                this.DiscardPendingDifficulty(ctx.User.Id);
                return;
            }
            if (mode == AddMode.Normal || mode == AddMode.KeyOnly) {
                var limit = this.RequestLimit(ctx);
                if (limit.HasValue && this.RequestCount(ctx.User.Id) >= limit.Value) {
                    this.Reply("LimitReached", new Dictionary<string, string> { ["user"] = ctx.User.Name, ["limit"] = limit.Value.ToString(CultureInfo.InvariantCulture) });
                    this.DiscardPendingDifficulty(ctx.User.Id);
                    return;
                }
            }
            this.EnqueueWork(() => this.ProcessRequest(ctx, query, mode));
        }

        /// <summary>立場ごとの上限。配信者は上限なし（null）。</summary>
        private int? RequestLimit(CommandContext ctx)
        {
            if (ctx.IsBroadcaster) {
                return null;
            }
            var limit = _config.UserRequestLimit;
            if (ctx.IsSubscriber) {
                limit = Math.Max(limit, _config.SubRequestLimit);
            }
            if (ctx.IsModerator) {
                limit = Math.Max(limit, _config.ModRequestLimit);
            }
            if (ctx.IsVip) {
                limit += _config.VipBonusRequests;
            }
            return limit;
        }

        /// <summary>
        /// 本人のリクエスト数。LimitUserRequestsToSession が無効ならキューに残っている数（キューは保存されるため再起動後も正しい）、
        /// 有効ならセッション中に受け付けた数。
        /// </summary>
        private int RequestCount(string userId)
        {
            if (_config.LimitUserRequestsToSession) {
                return _store.GetAcceptedCount(userId);
            }
            return _store.Queue.Count(r => r.Status == RequestStatus.Queued && r.Requester?.Id == userId);
        }

        // ================= 曲の特定 =================

        /// <summary>記号を取り除いた文字列が BeatSaver のキーとみなせるか。</summary>
        internal static bool TryParseKey(string text, out string key)
        {
            key = null;
            var cleaned = Regex.Replace(text ?? "", @"[^\p{L}\p{N}\-]", "");
            if (Regex.IsMatch(cleaned, "^[0-9a-fA-F]+$")) {
                key = cleaned.ToLowerInvariant();
                return true;
            }
            var old = Regex.Match(cleaned, @"^\d+-(\d+)$");
            if (old.Success && long.TryParse(old.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) {
                key = n.ToString("x", CultureInfo.InvariantCulture);
                return true;
            }
            return false;
        }

        private async Task ProcessRequest(CommandContext ctx, string query, AddMode mode)
        {
            var map = await this.FindSingleMap(query, mode == AddMode.KeyOnly);
            if (map == null) {
                this.DiscardPendingDifficulty(ctx.User.Id);
                return;
            }
            var filterKey = mode == AddMode.ModAdd || mode == AddMode.AddToTop
                ? this.CheckQueueOnly(map)
                : this.CheckFilters(map);
            if (filterKey != null) {
                this.Reply(filterKey, this.FilterVars(map, filterKey));
                this.DiscardPendingDifficulty(ctx.User.Id);
                return;
            }
            this.AddToQueue(map, ctx.User, mode == AddMode.AddToTop);
        }

        /// <summary>1曲に絞り込む。見つからない・候補が複数のときは返信して null を返す。</summary>
        private async Task<MapInfo> FindSingleMap(string query, bool keyOnly)
        {
            string key;
            if (TryParseKey(query, out key)) {
                var (status, map) = await BeatSaverClient.GetByKeyAsync(key);
                if (status == BeatSaverStatus.Ok) {
                    return map;
                }
                if (status == BeatSaverStatus.Unavailable) {
                    this.Reply("BeatSaverUnavailable");
                    return null;
                }
                if (keyOnly) {
                    this.Reply("InvalidKey", new Dictionary<string, string> { ["query"] = query });
                    return null;
                }
                // face や cafe のような英単語もキーとみなすため、見つからなければ文字列で探し直す
            }
            else if (keyOnly) {
                this.Reply("InvalidKey", new Dictionary<string, string> { ["query"] = query });
                return null;
            }
            if (query.Trim().Length < 3) {
                this.Reply("TooShort");
                return null;
            }
            var (searchStatus, maps) = await BeatSaverClient.SearchAsync(query.Trim(), "Latest");
            if (searchStatus != BeatSaverStatus.Ok) {
                this.Reply("BeatSaverUnavailable");
                return null;
            }
            var sorted = SortOrder.Sort(maps, _config.AddSortOrder).ToList();
            if (sorted.Count == 0) {
                this.Reply("NotFound", new Dictionary<string, string> { ["query"] = query });
                return null;
            }
            if (sorted.Count == 1) {
                return sorted[0];
            }
            if (sorted.Count <= 3) {
                var list = string.Join(", ", sorted.Select(m => _messages.Format("CandidateItem", this.SongVars(m))));
                this.Reply("Candidates", new Dictionary<string, string> { ["list"] = list, ["query"] = query });
                return null;
            }
            this.Reply("TooMany", new Dictionary<string, string> { ["query"] = query });
            return null;
        }

        // ================= フィルター =================

        private string CheckQueueOnly(MapInfo map) =>
            _store.Queue.Any(r => r.Status == RequestStatus.Queued && SameSong(r.Map, map)) ? "FilterInQueue" : null;

        /// <summary>最初に引っかかったフィルターの返信の項目名。通れば null。</summary>
        internal string CheckFilters(MapInfo map)
        {
            if (this.CheckQueueOnly(map) != null) {
                return "FilterInQueue";
            }
            if (_lists.SongBlockList.Contains(map.Key)) {
                return "FilterBlocked";
            }
            if (_lists.MapperBanList.AnyContainedIn(map.LevelAuthorName) || _lists.MapperBanList.AnyContainedIn(map.UploaderName)) {
                return "FilterMapper";
            }
            if (_store.WasRequestedThisSession(map.Key)) {
                return "FilterDuplicate";
            }
            if (_lists.SongAllowList.Contains(map.Key)) {
                return null;
            }
            if (_config.MaximumSongLength > 0 && map.Duration > _config.MaximumSongLength * 60) {
                return "FilterLength";
            }
            if (map.MaxNjs < _config.MinimumNJS) {
                return "FilterNjs";
            }
            // BeatSaver の評価（0〜1）を百分率に直して比べる。評価 0 の曲は対象外
            if (map.Rating > 0f && map.Rating * 100f < _config.LowestAllowedRating) {
                return "FilterRating";
            }
            return null;
        }

        private Dictionary<string, string> FilterVars(MapInfo map, string filterKey)
        {
            var vars = this.SongVars(map);
            switch (filterKey) {
                case "FilterLength":
                    vars["limit"] = _config.MaximumSongLength.ToString(CultureInfo.InvariantCulture);
                    break;
                case "FilterNjs":
                    vars["limit"] = _config.MinimumNJS.ToString("0.#", CultureInfo.InvariantCulture);
                    break;
                case "FilterRating":
                    vars["limit"] = _config.LowestAllowedRating.ToString("0.#", CultureInfo.InvariantCulture) + "%";
                    break;
                default:
                    vars["limit"] = "";
                    break;
            }
            return vars;
        }

        private static bool SameSong(MapInfo a, MapInfo b) =>
            a != null && b != null && (string.Equals(a.Key, b.Key, StringComparison.OrdinalIgnoreCase)
                                       || (!string.IsNullOrEmpty(a.Hash) && string.Equals(a.Hash, b.Hash, StringComparison.OrdinalIgnoreCase)));

        // ================= 追加 =================

        private void AddToQueue(MapInfo map, Requester user, bool toTop)
        {
            var request = new SongRequest {
                Map = map,
                Requester = user,
                RequestedAt = DateTimeOffset.UtcNow,
                Status = RequestStatus.Queued,
            };

            // 保留していた難易度（!bsrd、または先に送った !difficulty）を反映する
            string unresolvedInput = null;
            if (this.TakePendingDifficulty(user.Id, out var input)) {
                if (DifficultyResolver.TryResolve(map, input, out var resolved)) {
                    request.SetDifficulty(resolved);
                }
                else {
                    unresolvedInput = input;
                }
            }

            if (_config.LimitUserRequestsToSession) {
                _store.IncrementAcceptedCount(user.Id);
            }
            _store.AddSessionRequest(map.Key);
            if (toTop) {
                _store.Queue.Insert(this.FirstQueuedIndex(), request);
            }
            else {
                _store.Queue.Add(request);
            }
            _store.SaveQueue();

            if (unresolvedInput != null) {
                var vars = this.SongVars(map, user, request);
                vars["input"] = unresolvedInput;
                vars["available"] = DifficultyResolver.DescribeAvailable(map);
                this.Reply("DifficultyUnknown", vars);
            }
            if (_config.RequireDifficulty && !request.HasDifficulty) {
                // 追加の通知は難易度が決まってからまとめて送る
                request.AnnouncementPending = true;
                this.ReplyDifficultyPrompt(request);
            }
            else {
                this.AnnounceAdded(request);
            }

            this.PlayNotifySound();
            try {
                this.RequestAdded?.Invoke(request);
            }
            catch (Exception e) {
                Plugin.Log.Error($"Requests: {e}");
            }
            this.RaiseQueueChanged();
        }

        /// <summary>検索の結果を除いた、キューの最初の位置。!att はここに入れる。</summary>
        private int FirstQueuedIndex()
        {
            var i = 0;
            while (i < _store.Queue.Count && _store.Queue[i].Status == RequestStatus.SearchResult) {
                i++;
            }
            return i;
        }

        private void AnnounceAdded(SongRequest request)
        {
            request.AnnouncementPending = false;
            var vars = this.SongVars(request.Map, null, request);
            var text = _messages.Format("Added", vars);
            if (request.HasDifficulty) {
                text += _messages.Format("AddedDifficulty", vars);
            }
            this.ReplyText(text);
        }

        internal void ReplyDifficultyPrompt(SongRequest request)
        {
            var vars = this.SongVars(request.Map, null, request);
            vars["available"] = DifficultyResolver.DescribeAvailable(request.Map);
            this.Reply("DifficultyPrompt", vars);
        }

        // ================= 難易度 =================

        private void SetPendingDifficulty(string userId, string input)
        {
            if (!string.IsNullOrEmpty(userId)) {
                _pendingDifficulty[userId] = (input, DateTime.UtcNow + PendingDifficultyLifetime);
            }
        }

        private bool TakePendingDifficulty(string userId, out string input)
        {
            input = null;
            if (userId == null || !_pendingDifficulty.TryGetValue(userId, out var p)) {
                return false;
            }
            _pendingDifficulty.Remove(userId);
            if (p.Expires < DateTime.UtcNow) {
                return false;
            }
            input = p.Input;
            return true;
        }

        private void DiscardPendingDifficulty(string userId)
        {
            if (userId != null) {
                _pendingDifficulty.Remove(userId);
            }
        }

        private void HandleDifficulty(CommandContext ctx)
        {
            var input = ctx.Argument;
            var request = this.LatestRequestOf(ctx.User.Id);
            if (request == null) {
                this.SetPendingDifficulty(ctx.User.Id, input);
                this.Reply("DifficultyPending", new Dictionary<string, string> { ["user"] = ctx.User.Name, ["input"] = input });
                return;
            }
            if (!DifficultyResolver.TryResolve(request.Map, input, out var resolved)) {
                var vars = this.SongVars(request.Map, ctx.User, request);
                vars["input"] = input;
                vars["available"] = DifficultyResolver.DescribeAvailable(request.Map);
                this.Reply("DifficultyUnknown", vars);
                return;
            }
            request.SetDifficulty(resolved);
            _store.SaveQueue();
            if (request.AnnouncementPending) {
                this.AnnounceAdded(request);
            }
            else {
                this.Reply("DifficultySet", this.SongVars(request.Map, ctx.User, request));
            }
            this.RaiseQueueChanged();
        }

        /// <summary>キューにある本人の最も新しいリクエスト。</summary>
        private SongRequest LatestRequestOf(string userId) =>
            _store.Queue.Where(r => r.Status == RequestStatus.Queued && r.Requester?.Id == userId)
                .OrderByDescending(r => r.RequestedAt).FirstOrDefault();
    }
}
