using System;
using System.Collections.Generic;
using System.Linq;

namespace TTVStreamersKit.Requests
{
    internal sealed partial class RequestManager
    {
        /// <summary>
        /// キューから外す。取り消しと検索の結果以外は履歴の先頭に加え、上限を超えた古いものを消す。
        /// リクエスト数はキューから数えるため、外せば自然に1減る。
        /// </summary>
        public void RemoveFromQueue(SongRequest request, RequestStatus status)
        {
            if (request == null || !_store.Queue.Remove(request)) {
                return;
            }
            var wasSearchResult = request.Status == RequestStatus.SearchResult;
            request.Status = status;
            if (!wasSearchResult && status != RequestStatus.WrongSong) {
                this.AddToHistory(request);
            }
            if (ReferenceEquals(this.CurrentSong, request) && status == RequestStatus.WrongSong) {
                this.CurrentSong = null;
            }
            _store.SaveQueue();
            this.RaiseQueueChanged();
        }

        private void AddToHistory(SongRequest request)
        {
            _store.History.Insert(0, request);
            var limit = Math.Max(0, _config.RequestHistoryLimit);
            if (_store.History.Count > limit) {
                _store.History.RemoveRange(limit, _store.History.Count - limit);
            }
            _store.SaveHistory();
            this.WriteHistoryPlaylist();
        }

        /// <summary>キューを空にする。外した曲の数を返す。元には戻せない。</summary>
        public int ClearQueue()
        {
            var targets = _store.Queue.ToList();
            var count = targets.Count(r => r.Status == RequestStatus.Queued);
            foreach (var r in targets) {
                _store.Queue.Remove(r);
                if (r.Status == RequestStatus.Queued) {
                    r.Status = RequestStatus.Skipped;
                    _store.History.Insert(0, r);
                }
            }
            var limit = Math.Max(0, _config.RequestHistoryLimit);
            if (_store.History.Count > limit) {
                _store.History.RemoveRange(limit, _store.History.Count - limit);
            }
            _store.SaveQueue();
            _store.SaveHistory();
            this.WriteHistoryPlaylist();
            this.RaiseQueueChanged();
            return count;
        }

        /// <summary>再生する曲をキューから外し、再生済みに記録する。履歴から選んだ曲はそのまま。</summary>
        public void MarkPlayed(SongRequest request)
        {
            this.CurrentSong = request;
            if (_store.Queue.Contains(request)) {
                _store.AddPlayed(request.Map);
                this.RemoveFromQueue(request, RequestStatus.Played);
            }
        }

        /// <summary>選曲画面で曲を選んだあと、次の曲としてチャットに知らせる。</summary>
        public void AnnounceNextSong(SongRequest request)
        {
            this.Reply("NextSong", this.SongVars(request.Map, null, request));
        }

        public void ReplyDownloadFailed(SongRequest request)
        {
            this.Reply("DownloadFailed", this.SongVars(request.Map, null, request));
        }

        /// <summary>選んだ曲をブロックする（キューの画面のブロックボタン）。履歴から選んだ場合はキューに触らず状態だけ変える。</summary>
        public void Block(SongRequest request)
        {
            if (request?.Map == null) {
                return;
            }
            _lists.SongBlockList.Add(request.Map.Key);
            if (_store.Queue.Contains(request)) {
                this.RemoveFromQueue(request, RequestStatus.Blocked);
            }
            else {
                request.Status = RequestStatus.Blocked;
                _store.SaveHistory();
                this.RaiseQueueChanged();
            }
        }

        /// <summary>キューの中で1つ上（-1）か1つ下（+1）に動かす。</summary>
        public void Move(SongRequest request, int offset)
        {
            var index = _store.Queue.IndexOf(request);
            var target = index + offset;
            if (index < 0 || target < 0 || target >= _store.Queue.Count) {
                return;
            }
            _store.Queue.RemoveAt(index);
            _store.Queue.Insert(target, request);
            _store.SaveQueue();
            this.RaiseQueueChanged();
        }

        public void SetQueueOpen(bool open)
        {
            _config.RequestQueueOpen = open;
            this.Reply(open ? "QueueOpened" : "QueueClosed");
            this.RaiseQueueChanged();
        }

        /// <summary>
        /// キューの中の曲を探す。末尾側から見て最初に一致したもの。
        /// ID とみなせれば ID の一致、そうでなければ 3 文字以上の語が曲名などに含まれるもの。
        /// </summary>
        private SongRequest FindInQueue(string query)
        {
            var candidates = _store.Queue.Where(r => r.Status == RequestStatus.Queued).Reverse().ToList();
            if (TryParseKey(query, out var key)) {
                var byKey = candidates.FirstOrDefault(r => string.Equals(r.Map.Key, key, StringComparison.OrdinalIgnoreCase));
                if (byKey != null) {
                    return byKey;
                }
            }
            var words = query.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 3).ToList();
            if (words.Count == 0) {
                return null;
            }
            return candidates.FirstOrDefault(r => words.Any(w => Matches(r, w)));
        }

        private static bool Matches(SongRequest r, string word)
        {
            var fields = new[] {
                r.Map.SongName, r.Map.SongSubName, r.Map.SongAuthorName, r.Map.LevelAuthorName, r.Map.Key,
                r.Requester?.DisplayName, r.Requester?.Login,
            };
            return fields.Any(f => !string.IsNullOrEmpty(f) && f.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
