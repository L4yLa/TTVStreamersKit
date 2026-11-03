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
    /// 選曲画面（ソロのフリープレイ）のカスタム曲の一覧で、その曲を選ぶ。
    /// 開く前に BetterSongList のフィルターがかかっていれば解除する。
    /// <para>キューの画面は選曲画面の上に開くため、キューの画面を閉じたあと、開いている選曲画面をそのまま操作する
    /// （開き直して、開くときの状態として曲を渡す方法では、曲が選ばれない）。
    /// 選曲画面が開いていなければ、開くときの状態として曲を渡す。</para>
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

        public static void Show(BeatmapLevel level, bool wip)
        {
            ClearBetterSongListFilter();

            var main = BeatSaberUI.MainFlowCoordinator;
            var solo = main.GetField<SoloFreePlayFlowCoordinator, MainFlowCoordinator>("_soloFreePlayFlowCoordinator");
            Action select = () => {
                if (solo.isActivated) {
                    CoroutineHost.Run(SelectInOpenList(solo, level, wip));
                }
                else {
                    Present(main, solo, level, wip);
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

        /// <summary>開いている選曲画面で、カスタム曲（WIP なら WIP）のパックを開き、曲を選ぶ。</summary>
        private static IEnumerator SelectInOpenList(SoloFreePlayFlowCoordinator solo, BeatmapLevel level, bool wip)
        {
            var navigation = solo.GetField<LevelSelectionNavigationController, LevelSelectionFlowCoordinator>("levelSelectionNavigationController");
            var filtering = navigation.GetField<LevelFilteringNavigationController, LevelSelectionNavigationController>("_levelFilteringNavigationController");
            var collection = navigation.GetField<LevelCollectionNavigationController, LevelSelectionNavigationController>("_levelCollectionNavigationController");
            var pack = wip ? Loader.WIPLevelsPack : Loader.CustomLevelsPack;
            if (pack == null) {
                Plugin.Log.Warn("Requests: the custom song pack is not available.");
                yield break;
            }

            // すでにカスタム曲のパックを開いていても、毎回この手順を通す。
            // 未所持曲をダウンロードしたあとは、開いている一覧が古い中身のままで、新しい曲が入っていないため
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

        /// <summary>選曲画面が開いていないとき。開くときの状態として、カテゴリー・パック・曲を渡す。</summary>
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
