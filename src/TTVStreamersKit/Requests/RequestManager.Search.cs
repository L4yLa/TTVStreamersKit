using System.Linq;
using System.Threading.Tasks;

namespace TTVStreamersKit.Requests
{
    internal sealed partial class RequestManager
    {
        /// <summary>
        /// キューの画面の検索。BeatSaver を関連度順で検索し、フィルターを通った曲を「検索の結果」としてキューの先頭に入れる。
        /// 前の検索の結果は外してから入れる。リクエスト数には数えず、外しても履歴には残さない。
        /// </summary>
        public void SearchToQueue(string query)
        {
            this.EnqueueWork(() => this.SearchToQueueAsync(query));
        }

        private async Task SearchToQueueAsync(string query)
        {
            var (status, maps) = await BeatSaverClient.SearchAsync(query, "Relevance");
            if (status != BeatSaverStatus.Ok) {
                Plugin.Log.Warn("Requests: BeatSaver search failed.");
                return;
            }
            _store.Queue.RemoveAll(r => r.Status == RequestStatus.SearchResult);
            var results = SortOrder.Sort(maps.Where(m => this.CheckFilters(m) == null), _config.SearchSortOrder)
                .Select(m => new SongRequest { Map = m, RequestedAt = System.DateTimeOffset.UtcNow, Status = RequestStatus.SearchResult })
                .ToList();
            _store.Queue.InsertRange(0, results);
            _store.SaveQueue();
            this.RaiseQueueChanged();
        }
    }
}
