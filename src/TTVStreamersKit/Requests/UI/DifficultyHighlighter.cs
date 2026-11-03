// Copyright (c) 2026 L4yLa. MIT License.
using HMUI;
using IPA.Utilities;
using SiraUtil.Affinity;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Zenject;

namespace TTVStreamersKit.Requests.UI
{
    /// <summary>
    /// キューの画面から再生した曲に限り、指定された難易度を選曲画面で自動で選び、別の難易度に移ったときはその難易度を赤で示す。
    /// <list type="bullet">
    /// <item>難易度のセルは SetData をまたいで使い回される。色を変えたセルは、手放す前に必ず元の色に戻す。</item>
    /// <item>自動で選ぶのは SetData の直後だけ。セルの選択のイベントから選ぶと、利用者が選び直した難易度を引き戻してしまう。</item>
    /// <item>SegmentedControl.SelectCellWithNumber は選択のイベントを発生させないため、利用者の操作と自動の選択を区別できる。</item>
    /// </list>
    /// </summary>
    internal sealed class DifficultyHighlighter : IInitializable, IDisposable, IAffinity
    {
        // 暗い背景でも目立つ明るい赤。PlaylistManager が同じセルに黄色を使うため、別の色にする
        private static readonly Color32 HighlightColor = new Color32(255, 60, 60, 255);
        // 難易度のセルの、選んでいないときの文字の色
        private static readonly Color32 DefaultTextColor = new Color32(255, 255, 255, 255);
        private const string CustomLevelIdPrefix = "custom_level_";

        private readonly SongPlayer _player;
        private readonly StandardLevelDetailView _detailView;
        private readonly BeatmapCharacteristicSegmentedControlController _characteristicController;
        private readonly BeatmapDifficultySegmentedControlController _difficultyController;
        private readonly SegmentedControl _difficultyControl;
        private readonly SegmentedControl _characteristicControl;

        private CurvedTextMeshPro _highlightedText;
        private Color32 _originalColor;
        private SongRequest _target;
        private bool _userSelected;

        public DifficultyHighlighter(StandardLevelDetailViewController detailViewController, SongPlayer player)
        {
            _player = player;
            _detailView = detailViewController.GetField<StandardLevelDetailView, StandardLevelDetailViewController>("_standardLevelDetailView");
            _characteristicController = _detailView.GetField<BeatmapCharacteristicSegmentedControlController, StandardLevelDetailView>("_beatmapCharacteristicSegmentedControlController");
            _difficultyController = _detailView.GetField<BeatmapDifficultySegmentedControlController, StandardLevelDetailView>("_beatmapDifficultySegmentedControlController");
            _characteristicControl = _characteristicController.GetField<IconSegmentedControl, BeatmapCharacteristicSegmentedControlController>("_segmentedControl");
            _difficultyControl = _difficultyController.GetField<TextSegmentedControl, BeatmapDifficultySegmentedControlController>("_difficultySegmentedControl");
        }

        public void Initialize()
        {
            if (_characteristicControl != null) {
                _characteristicControl.didSelectCellEvent += this.OnCharacteristicSelected;
            }
            if (_difficultyControl != null) {
                _difficultyControl.didSelectCellEvent += this.OnDifficultySelected;
            }
            _player.PlayStarted += this.OnPlayStarted;
        }

        public void Dispose()
        {
            if (_characteristicControl != null) {
                _characteristicControl.didSelectCellEvent -= this.OnCharacteristicSelected;
            }
            if (_difficultyControl != null) {
                _difficultyControl.didSelectCellEvent -= this.OnDifficultySelected;
            }
            _player.PlayStarted -= this.OnPlayStarted;
            _target = null;
            this.Restore();
        }

        /// <summary>キューの画面から再生した。ここでだけ対象を決める（履歴からの再生でも同じ曲を対象にできる）。</summary>
        private void OnPlayStarted(SongRequest request)
        {
            _target = request != null && request.HasDifficulty ? request : null;
            _userSelected = false;
        }

        [AffinityPostfix]
        [AffinityPatch(typeof(BeatmapDifficultySegmentedControlController), nameof(BeatmapDifficultySegmentedControlController.SetData))]
        private void SetDataPostfix()
        {
            // SetData の中でセルが差し替わるため、1フレーム待つ
            CoroutineHost.Run(this.ApplyDelayed(true));
        }

        private void OnCharacteristicSelected(SegmentedControl control, int index)
        {
            CoroutineHost.Run(this.ApplyDelayed(false));
        }

        /// <summary>利用者が難易度を選んだ。以降は自動で選ばない。</summary>
        private void OnDifficultySelected(SegmentedControl control, int index)
        {
            _userSelected = true;
            CoroutineHost.Run(this.ApplyDelayed(false));
        }

        private IEnumerator ApplyDelayed(bool allowAutoSelect)
        {
            yield return null;
            this.Apply(allowAutoSelect);
        }

        private void Apply(bool allowAutoSelect)
        {
            try {
                // セルは使い回されるため、まず元の色に戻す
                this.Restore();
                var song = _target;
                if (song == null) {
                    return;
                }
                if (!this.IsCurrentLevel(song)) {
                    _target = null;
                    return;
                }
                var characteristic = _characteristicController.selectedBeatmapCharacteristic;
                var wanted = string.IsNullOrEmpty(song.Characteristic) ? DifficultyResolver.StandardCharacteristic : song.Characteristic;
                if (characteristic == null || !string.Equals(characteristic.serializedName, wanted, StringComparison.OrdinalIgnoreCase)) {
                    return;
                }
                // BeatSaver の難易度の表記はゲームの BeatmapDifficulty と同じ名前
                if (!Enum.TryParse<BeatmapDifficulty>(song.Difficulty, true, out var difficulty)) {
                    return;
                }
                // GetClosestDifficultyIndex は存在しない難易度でも近いものを返すため、先に確かめる
                var available = _difficultyController.GetField<List<BeatmapDifficulty>, BeatmapDifficultySegmentedControlController>("_difficulties");
                if (available == null || !available.Contains(difficulty)) {
                    return;
                }
                var cells = _difficultyControl.cells;
                var index = _difficultyController.InvokeMethod<int, BeatmapDifficultySegmentedControlController>("GetClosestDifficultyIndex", difficulty);
                if (cells == null || index < 0 || index >= cells.Count) {
                    return;
                }
                if (_difficultyControl.selectedCellNumber != index && allowAutoSelect && !_userSelected && this.TrySelect(index)) {
                    // 選ぶとデータが作り直されるため、色はそのときに付ける
                    return;
                }
                var text = cells[index].GetComponentInChildren<CurvedTextMeshPro>();
                if (text == null) {
                    return;
                }
                var current = text.faceColor;
                // 前の色が残っていたら、それを元の色として覚えない
                _originalColor = IsHighlight(current) ? DefaultTextColor : current;
                _highlightedText = text;
                text.faceColor = HighlightColor;
            }
            catch (Exception e) {
                Plugin.Log.Error($"Requests: difficulty highlight failed: {e}");
            }
        }

        /// <summary>難易度のセルを選び、ゲームに反映させる（コントローラーのセル選択の処理を直接呼ぶ）。</summary>
        private bool TrySelect(int index)
        {
            try {
                var handler = typeof(BeatmapDifficultySegmentedControlController)
                    .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .FirstOrDefault(m => {
                        var p = m.GetParameters();
                        return m.Name.IndexOf("DidSelectCell", StringComparison.OrdinalIgnoreCase) >= 0 && p.Length == 2
                               && typeof(SegmentedControl).IsAssignableFrom(p[0].ParameterType) && p[1].ParameterType == typeof(int);
                    });
                _difficultyControl.SelectCellWithNumber(index);
                if (handler == null) {
                    Plugin.Log.Error("Requests: the difficulty selection handler was not found.");
                    return false;
                }
                handler.Invoke(_difficultyController, new object[] { _difficultyControl, index });
                return true;
            }
            catch (Exception e) {
                Plugin.Log.Error($"Requests: difficulty selection failed: {e}");
                return false;
            }
        }

        private static bool IsHighlight(Color32 c) =>
            c.r == HighlightColor.r && c.g == HighlightColor.g && c.b == HighlightColor.b && c.a == HighlightColor.a;

        /// <summary>変えた色を元に戻す。選択の状態にかかわらず必ず戻す（戻さずに手放すと解除できなくなる）。</summary>
        private void Restore()
        {
            try {
                if (_highlightedText != null) {
                    _highlightedText.faceColor = _originalColor;
                }
            }
            catch (Exception e) {
                Plugin.Log.Error($"Requests: {e}");
            }
            _highlightedText = null;
        }

        /// <summary>詳細の画面に出ている曲が対象のリクエストか。判定できなければ false。</summary>
        private bool IsCurrentLevel(SongRequest song)
        {
            var hash = song.Map?.Hash;
            var levelId = _detailView.beatmapKey.levelId;
            if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(levelId)
                || !levelId.StartsWith(CustomLevelIdPrefix, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
            // WIP の曲の levelId は "custom_level_<HASH> WIP" の形
            var current = levelId.Substring(CustomLevelIdPrefix.Length).Split(' ')[0];
            return string.Equals(current, hash, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>MonoBehaviour でないクラスからコルーチンを動かす。</summary>
    internal sealed class CoroutineHost : MonoBehaviour
    {
        private static CoroutineHost s_instance;

        public static void Run(IEnumerator routine)
        {
            if (s_instance == null) {
                var go = new GameObject("TTVStreamersKit CoroutineHost");
                DontDestroyOnLoad(go);
                s_instance = go.AddComponent<CoroutineHost>();
            }
            s_instance.StartCoroutine(routine);
        }
    }
}
