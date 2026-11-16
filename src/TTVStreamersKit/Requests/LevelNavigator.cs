using BeatSaberMarkupLanguage;
using HMUI;
using IPA.Utilities;
using SongCore;
using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using TTVStreamersKit.Requests.UI;
using UnityEngine;

namespace TTVStreamersKit.Requests
{
    /// <summary>
    /// 今開いている選曲画面（ソロ、パーティー、マルチプレイ）のカスタム曲の一覧で、その曲を選ぶ。
    /// 開く前に BetterSongList のフィルターがかかっていれば解除する。
    /// <para>キューの画面は選曲画面の上に開くため、キューの画面を閉じたあと、開いている選曲画面をそのまま操作する
    /// （開き直して、開くときの状態として曲を渡す方法では、曲が選ばれない）。
    /// 曲の一覧の画面はどのモードでも共通のため、同じ手順でどの選曲画面でも選べる。
    /// 曲の一覧の部品を借りて独自の画面に表示する MOD（BeatSaberPlus の Multiplayer+ など）でも、同じ部品を操作して選ぶ。</para>
    /// <para>選曲画面が開いておらず、メインメニューにいるときだけ、ソロの選曲画面を開くときの状態として曲を渡す。
    /// 他の画面（マルチプレイのロビーなど）が開いているときにソロの選曲画面を重ねると、画面の流れが壊れて操作できなくなるため、何もしない。</para>
    /// <para>開いている画面は次の順に操作する:
    /// カテゴリーのセグメントをカスタム曲にする → カスタム曲の一覧を作り直す（<c>UpdateCustomSongs</c>）→
    /// パックの一覧でパックを選ぶ → 曲の表で曲を選ぶ。
    /// パックを選ぶゲームの処理（<c>SelectAnnotatedBeatmapLevelCollection</c>）だけでは、今のカテゴリーの外のパックに切り替わらない。</para>
    /// </summary>
    internal static class LevelNavigator
    {
        // 一覧の作り直しとパックの切り替えを待つ上限
        private const float WaitSeconds = 5f;

        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>
        /// 今の画面から曲を選べるか。選曲画面が開いているか、メインメニューにいれば選べる。
        /// それ以外（マルチプレイのロビーなど）でソロの選曲画面を重ねると、画面の流れが壊れて操作できなくなる。
        /// </summary>
        public static bool CanShow()
        {
            var main = BeatSaberUI.MainFlowCoordinator;
            if (main == null) {
                return false;
            }
            if (FindSongList(main) != null) {
                return true;
            }
            // キューの画面を閉じた後に見える画面で判定する
            var youngest = main.YoungestChildFlowCoordinatorOrSelf();
            var below = youngest is QueueFlowCoordinator queue ? queue.Parent : youngest;
            if (below != main) {
                Plugin.Log.Warn($"Requests: no song selection screen is open ({below?.GetType().Name ?? "nothing"}).");
                return false;
            }
            return true;
        }

        /// <summary>曲を選ぶ。先に <see cref="CanShow"/> で選べることを確かめる。</summary>
        public static void Show(BeatmapLevel level, bool wip)
        {
            ClearBetterSongListFilter();

            var main = BeatSaberUI.MainFlowCoordinator;
            var songList = FindSongList(main);
            Action select = () => {
                if (songList != null && songList.isActivated) {
                    CoroutineHost.Run(SelectInOpenList(songList, level, wip));
                }
                else if (main.YoungestChildFlowCoordinatorOrSelf() == main) {
                    var solo = main.GetField<SoloFreePlayFlowCoordinator, MainFlowCoordinator>("_soloFreePlayFlowCoordinator");
                    Present(main, solo, level, wip);
                }
                else {
                    Plugin.Log.Warn("Requests: the song selection screen was closed before the song was selected.");
                }
            };

            var youngest = main.YoungestChildFlowCoordinatorOrSelf();
            if (youngest is QueueFlowCoordinator queue && queue.Parent != null) {
                queue.Parent.DismissFlowCoordinator(queue, select, ViewController.AnimationDirection.Horizontal, true);
            }
            else {
                select();
            }
        }

        /// <summary>
        /// 開いている曲の一覧の部品。メインから子の画面の流れをたどり、一番奥のものを返す。
        /// <list type="bullet">
        /// <item>ゲームの選曲画面（ソロ、パーティー、マルチプレイ）は、その画面が持つ部品。</item>
        /// <item>部品を借りて中央に表示している MOD の画面（Multiplayer+ など）は、中央に表示している部品。</item>
        /// </list>
        /// 上に別の画面（キューの画面など）が重なっていても見つける。
        /// 子の画面を出している間は親の画面は非アクティブになるため、アクティブかどうかは見ない（子の画面の流れにあれば開いている）。
        /// </summary>
        private static LevelSelectionNavigationController FindSongList(MainFlowCoordinator main)
        {
            LevelSelectionNavigationController found = null;
            for (FlowCoordinator flow = main; flow != null; flow = flow.childFlowCoordinator) {
                if (flow is LevelSelectionFlowCoordinator selection) {
                    found = selection.GetField<LevelSelectionNavigationController, LevelSelectionFlowCoordinator>("levelSelectionNavigationController");
                }
                else if (flow.topViewController is LevelSelectionNavigationController borrowed) {
                    found = borrowed;
                }
            }
            return found;
        }

        /// <summary>開いている曲の一覧で、カスタム曲（WIP なら WIP）のパックを開き、曲を選ぶ。</summary>
        private static IEnumerator SelectInOpenList(LevelSelectionNavigationController navigation, BeatmapLevel level, bool wip)
        {
            var filtering = navigation.GetField<LevelFilteringNavigationController, LevelSelectionNavigationController>("_levelFilteringNavigationController");
            var collection = navigation.GetField<LevelCollectionNavigationController, LevelSelectionNavigationController>("_levelCollectionNavigationController");
            var pack = wip ? Loader.WIPLevelsPack : Loader.CustomLevelsPack;
            if (pack == null) {
                Plugin.Log.Warn("Requests: the custom song pack is not available.");
                yield break;
            }

            // すでにカスタム曲のパックを開いていても、毎回この手順を通す。
            // 未所持曲をダウンロードしたあとは、開いている一覧が古い中身のままで、新しい曲が入っていないため
            // （マルチプレイでカスタム曲のカテゴリーがない場合は、ここで警告を出し、以降のパックの切り替えで止まる）
            SelectCategoryCell(filtering, SelectLevelCategoryViewController.LevelCategory.CustomSongs);

            // カスタム曲の一覧を作り直す。作り直しが終わるまで待つ
            filtering.GetType().GetMethod("UpdateCustomSongs", AnyInstance, null, Type.EmptyTypes, null)?.Invoke(filtering, null);
            var until = Time.realtimeSinceStartup + WaitSeconds;
            while (filtering.GetField<CancellationTokenSource, LevelFilteringNavigationController>("_cancellationTokenSource") != null
                && Time.realtimeSinceStartup < until) {
                yield return null;
            }

            SelectPackInGrid(filtering, pack);

            var packUntil = Time.realtimeSinceStartup + WaitSeconds;
            while (CurrentPackId(collection) != pack.packID && Time.realtimeSinceStartup < packUntil) {
                yield return null;
            }
            if (CurrentPackId(collection) != pack.packID) {
                Plugin.Log.Warn($"Requests: the song list did not switch to {pack.packID} (it shows {CurrentPackId(collection) ?? "nothing"}).");
                yield break;
            }

            // 一覧の中身が入るのを1フレーム待つ
            yield return null;
            SelectLevelInTable(collection, level);
            yield return null;
            var selected = collection.beatmapLevel;
            if (selected?.levelID != level.levelID) {
                Plugin.Log.Warn($"Requests: {level.levelID} could not be selected in the song list (selected: {selected?.levelID ?? "nothing"}).");
            }
        }

        /// <summary>カテゴリー（上のアイコンの並び）のセグメントを選ぶ。番号はカテゴリーの一覧から探す。</summary>
        private static void SelectCategoryCell(LevelFilteringNavigationController filtering, SelectLevelCategoryViewController.LevelCategory category)
        {
            try {
                var selector = filtering.GetField<SelectLevelCategoryViewController, LevelFilteringNavigationController>("_selectLevelCategoryViewController");
                var selectorType = typeof(SelectLevelCategoryViewController);
                var control = selectorType.GetField("_levelFilterCategoryIconSegmentedControl", AnyInstance)?.GetValue(selector);
                var infos = selectorType.GetField("_levelCategoryInfos", AnyInstance)?.GetValue(selector) as IEnumerable;
                if (control == null || infos == null) {
                    Plugin.Log.Warn("Requests: the song category selector was not found.");
                    return;
                }
                var index = IndexOfCategory(infos, category);
                if (index < 0) {
                    Plugin.Log.Warn($"Requests: the song category {category} is not available.");
                    return;
                }
                control.GetType().GetMethod("SelectCellWithNumber", AnyInstance, null, new[] { typeof(int) }, null)?.Invoke(control, new object[] { index });
            }
            catch (Exception e) {
                Plugin.Log.Warn($"Requests: could not select the song category: {e.Message}");
            }
        }

        private static int IndexOfCategory(IEnumerable infos, SelectLevelCategoryViewController.LevelCategory category)
        {
            var index = 0;
            foreach (var info in infos) {
                if (info != null) {
                    var type = info.GetType();
                    var value = type.GetField("levelCategory", AnyInstance)?.GetValue(info)
                        ?? type.GetProperty("levelCategory", AnyInstance)?.GetValue(info);
                    if (value is SelectLevelCategoryViewController.LevelCategory c && c == category) {
                        return index;
                    }
                }
                index++;
            }
            return -1;
        }

        /// <summary>パックの一覧（グリッド）でパックを選び、ゲームがパックを押したときの処理を呼ぶ。</summary>
        private static void SelectPackInGrid(LevelFilteringNavigationController filtering, BeatmapLevelPack pack)
        {
            try {
                var packs = filtering.GetField<AnnotatedBeatmapLevelCollectionsViewController, LevelFilteringNavigationController>("_annotatedBeatmapLevelCollectionsViewController");
                var grid = packs.GetField<AnnotatedBeatmapLevelCollectionsGridView, AnnotatedBeatmapLevelCollectionsViewController>("_annotatedBeatmapLevelCollectionsGridView");
                var list = typeof(AnnotatedBeatmapLevelCollectionsGridView).GetField("_annotatedBeatmapLevelCollections", AnyInstance)?.GetValue(grid) as IEnumerable;
                if (list == null) {
                    Plugin.Log.Warn("Requests: the song pack list was not found.");
                    return;
                }
                var index = 0;
                BeatmapLevelPack target = null;
                foreach (var item in list) {
                    if (item is BeatmapLevelPack candidate && candidate.packID == pack.packID) {
                        target = candidate;
                        break;
                    }
                    index++;
                }
                if (target == null) {
                    Plugin.Log.Warn($"Requests: {pack.packID} is not in the song pack list.");
                    return;
                }
                typeof(AnnotatedBeatmapLevelCollectionsGridView)
                    .GetMethod("SelectAndScrollToCellWithIdx", AnyInstance, null, new[] { typeof(int) }, null)?
                    .Invoke(grid, new object[] { index });
                typeof(AnnotatedBeatmapLevelCollectionsViewController)
                    .GetMethod("HandleDidSelectAnnotatedBeatmapLevelCollection", AnyInstance)?
                    .Invoke(packs, new object[] { target });
            }
            catch (Exception e) {
                Plugin.Log.Warn($"Requests: could not select the song pack: {e.Message}");
            }
        }

        /// <summary>曲の表で曲を選ぶ。</summary>
        private static void SelectLevelInTable(LevelCollectionNavigationController collection, BeatmapLevel level)
        {
            try {
                var view = collection.GetField<LevelCollectionViewController, LevelCollectionNavigationController>("_levelCollectionViewController");
                var table = view.GetField<LevelCollectionTableView, LevelCollectionViewController>("_levelCollectionTableView");
                var method = typeof(LevelCollectionTableView).GetMethod("SelectLevel", AnyInstance, null, new[] { typeof(BeatmapLevel) }, null);
                if (method == null) {
                    // 表で選べない版では、ナビゲーションの処理で選ぶ
                    collection.SelectLevel(level);
                    return;
                }
                method.Invoke(table, new object[] { level });
            }
            catch (Exception e) {
                Plugin.Log.Warn($"Requests: could not select the song in the song list: {e.Message}");
            }
        }

        private static string CurrentPackId(LevelCollectionNavigationController collection) =>
            collection.GetField<BeatmapLevelPack, LevelCollectionNavigationController>("_levelPack")?.packID;

        /// <summary>選曲画面が開いておらず、メインメニューにいるとき。ソロの選曲画面を、開くときの状態としてカテゴリー・パック・曲を渡して開く。</summary>
        private static void Present(MainFlowCoordinator main, SoloFreePlayFlowCoordinator solo, BeatmapLevel level, bool wip)
        {
            var pack = wip ? Loader.WIPLevelsPack : Loader.CustomLevelsPack;
            var key = default(BeatmapKey);
            var state = new LevelSelectionFlowCoordinator.State(SelectLevelCategoryViewController.LevelCategory.CustomSongs, pack, in key, level);
            solo.Setup(state);
            main.PresentFlowCoordinator(solo);
        }

        /// <summary>BetterSongList（0.4 系）の FilterUI.ClearFilter(bool) を呼ぶ。見つからなければ何もしない。</summary>
        private static void ClearBetterSongListFilter()
        {
            try {
                var type = Type.GetType("BetterSongList.UI.FilterUI, BetterSongList", false);
                var method = type?.GetMethod("ClearFilter", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(bool) }, null);
                method?.Invoke(null, new object[] { false });
            }
            catch (Exception e) {
                Plugin.Log.Warn($"Requests: could not clear the BetterSongList filter: {e.Message}");
            }
        }
    }
}
