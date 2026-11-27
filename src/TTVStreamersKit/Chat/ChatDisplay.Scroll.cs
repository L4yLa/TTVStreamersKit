using BeatSaberMarkupLanguage;
using HMUI;
using TTVStreamersKit.Utilities;
using UnityEngine;
using VRUIControls;

namespace TTVStreamersKit.Chat
{
    /// <summary>
    /// チャットのスクロール。メニューでだけ、チャットに当てたポインタのスティックの上下と、右外のスクロールバーで操作する。
    /// ゲームに入るときに最新に戻す。新しいメッセージが来ても、スクロールしている間は見ている位置を保つ。
    /// </summary>
    internal sealed partial class ChatDisplay
    {
        // スティックを倒しきったときの速さ（1 秒あたりの単位）
        private const float ScrollSpeed = 60f;
        // スクロールバーの幅、チャットの右端からの隙間、つまみの最小の長さ
        private const float ScrollbarWidth = 2f;
        private const float ScrollbarHitWidth = 6f;
        private const float ScrollbarGap = 1.5f;
        private const float MinKnobLength = 4f;

        // 最新を表示しているか。最新のときは、新しいメッセージに合わせて表示を進める
        private bool _atLatest = true;
        // 最新でないときの、古い側の端から、表示している範囲の古い側の端までの距離
        private float _fromOldest;
        // 直前に並べたときの、全体の高さと、スクロールできる量
        private float _contentHeight;
        private float _scrollRange;
        private float _scrollOffset;

        private ChatGrabWatcher _grabWatcher;
        private GameObject _scrollbar;
        private RectTransform _scrollbarRect;
        private RectTransform _knobRect;

        private bool CanScroll => !_isInGame && _inChatScene;

        /// <summary>スクロールの操作を受け取る部品と、スクロールバーを作る。</summary>
        private void SetupScroll()
        {
            var receiver = _chatScreen.gameObject.AddComponent<ChatScrollReceiver>();
            receiver.Scrolled = this.OnScrolled;
            if (_chatScreen.Handle != null) {
                _grabWatcher = _chatScreen.Handle.AddComponent<ChatGrabWatcher>();
            }
            this.CreateScrollbar();
            this.UpdateScrollTarget();
        }

        /// <summary>
        /// 掴む部分がないときだけ、背景にポインタが当たるようにする（スティックの操作を受け取るため）。
        /// 掴む部分があるときはポインタがそちらに当たるため、背景には当てず、掴む操作とぶつからないようにする。
        /// </summary>
        private void UpdateScrollTarget()
        {
            if (_bg != null) {
                _bg.raycastTarget = !_allowMovement;
            }
        }

        /// <summary>
        /// スクロールバーをチャットの右外に作る。ゲームの VRGraphicRaycaster は、ポインタの当たった点がキャンバスの範囲の中にあるときだけ
        /// その中の部品に当てるため、チャットのキャンバスの外には置けない。別のキャンバスと VRGraphicRaycaster を持たせる。
        /// </summary>
        private void CreateScrollbar()
        {
            _scrollbar = new GameObject("TTVStreamersKit ChatScrollbar", typeof(RectTransform));
            _scrollbarRect = (RectTransform)_scrollbar.transform;
            _scrollbarRect.SetParent(_chatScreen.transform, false);
            _scrollbarRect.anchorMin = _scrollbarRect.anchorMax = new Vector2(0.5f, 0.5f);
            _scrollbarRect.pivot = new Vector2(0.5f, 0.5f);
            _scrollbar.AddComponent<Canvas>();
            BeatSaberUI.DiContainer.InstantiateComponent<VRGraphicRaycaster>(_scrollbar);

            // 当たり判定は見た目より広くする（ほぼ透明）
            var hit = CreateImage("Hit", _scrollbarRect, new Color(1f, 1f, 1f, 0.01f));
            hit.raycastTarget = true;
            var hitRect = hit.rectTransform;
            hitRect.anchorMin = new Vector2(0.5f, 0f);
            hitRect.anchorMax = new Vector2(0.5f, 1f);
            hitRect.sizeDelta = new Vector2(ScrollbarHitWidth, 0f);
            hit.gameObject.AddComponent<ChatScrollbarInput>().Pointed = this.OnScrollbarPointed;

            var track = CreateImage("Track", _scrollbarRect, new Color(1f, 1f, 1f, 0.15f));
            var trackRect = track.rectTransform;
            trackRect.anchorMin = new Vector2(0f, 0f);
            trackRect.anchorMax = new Vector2(1f, 1f);
            trackRect.sizeDelta = Vector2.zero;

            var knob = CreateImage("Knob", _scrollbarRect, new Color(1f, 1f, 1f, 0.6f));
            _knobRect = knob.rectTransform;
            _knobRect.anchorMin = new Vector2(0f, 1f);
            _knobRect.anchorMax = new Vector2(1f, 1f);
            _knobRect.pivot = new Vector2(0.5f, 1f);

            _scrollbar.SetActive(false);
        }

        private static ImageView CreateImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<ImageView>();
            image.raycastTarget = false;
            if (BeatSaberUtils.UINoGlowMaterial != null) {
                image.material = BeatSaberUtils.UINoGlowMaterial;
            }
            image.color = color;
            return image;
        }

        /// <summary>最新の表示に戻す。</summary>
        private void ScrollToLatest()
        {
            _atLatest = true;
            _fromOldest = 0f;
            _updateMessagePositions = true;
        }

        private void OnScrolled(Vector2 delta)
        {
            if (!this.CanScroll || _scrollRange <= 0f || (_grabWatcher != null && _grabWatcher.Grabbing)) {
                return;
            }
            // 上に倒すと表示している範囲が上に動く。新しいものが下のときは古い側、上のときは新しい側になる
            var amount = delta.y * ScrollSpeed * Time.deltaTime;
            this.SetScrollOffset(_scrollOffset + (this.ReverseChatOrder ? -amount : amount));
        }

        private void OnScrollbarPointed(Vector3 worldPosition)
        {
            if (!this.CanScroll || _scrollRange <= 0f || _scrollbarRect == null) {
                return;
            }
            // 指した位置がつまみの中央になるようにする
            var height = this.ChatHeight;
            var knob = this.KnobLength(height);
            var travel = height - knob;
            if (travel <= 0f) {
                return;
            }
            var y = _scrollbarRect.InverseTransformPoint(worldPosition).y;
            var fromTop = Mathf.Clamp01((height / 2f - y - knob / 2f) / travel);
            this.SetScrollOffset(this.ReverseChatOrder ? fromTop * _scrollRange : (1f - fromTop) * _scrollRange);
        }

        /// <summary>最新の側の端からの距離で、表示する範囲を決める。</summary>
        private void SetScrollOffset(float offset)
        {
            offset = Mathf.Clamp(offset, 0f, _scrollRange);
            if (offset <= 0f) {
                _atLatest = true;
                _fromOldest = 0f;
            }
            else {
                _atLatest = false;
                _fromOldest = _scrollRange - offset;
            }
            _updateMessagePositions = true;
        }

        /// <summary>古いものを消したとき、見ている位置がずれないようにする。</summary>
        private void OnOldestRemoved(float height)
        {
            if (!_atLatest) {
                _fromOldest = Mathf.Max(0f, _fromOldest - height);
            }
        }

        /// <summary>全体の高さから、今の表示の位置（最新の側の端からの距離）を決める。</summary>
        private float ResolveScrollOffset(float contentHeight)
        {
            var height = this.ChatHeight;
            _contentHeight = contentHeight;
            _scrollRange = Mathf.Max(0f, contentHeight - height);
            if (_atLatest || !this.CanScroll) {
                _scrollOffset = 0f;
            }
            else {
                _scrollOffset = Mathf.Clamp(_scrollRange - _fromOldest, 0f, _scrollRange);
                if (_scrollOffset <= 0f) {
                    _atLatest = true;
                    _fromOldest = 0f;
                }
            }
            return _scrollOffset;
        }

        private float KnobLength(float height) =>
            _contentHeight <= 0f ? height : Mathf.Clamp(height * height / _contentHeight, MinKnobLength, height);

        /// <summary>スクロールバーの位置、大きさ、つまみを今の状態に合わせる。</summary>
        private void UpdateScrollbar()
        {
            if (_scrollbar == null) {
                return;
            }
            var visible = this.CanScroll && _scrollRange > 0f;
            if (_scrollbar.activeSelf != visible) {
                _scrollbar.SetActive(visible);
            }
            this.ScrollDiagOnUpdate(visible);
            if (!visible) {
                return;
            }
            var height = this.ChatHeight;
            _scrollbarRect.sizeDelta = new Vector2(ScrollbarWidth, height);
            _scrollbarRect.anchoredPosition = new Vector2(this.ChatWidth / 2f + ScrollbarGap + ScrollbarWidth / 2f, 0f);
            var knob = this.KnobLength(height);
            var travel = height - knob;
            // 上の端からの割合。新しいものが下のときは古い側が上になる
            var fromTop = _scrollRange <= 0f ? 0f
                : this.ReverseChatOrder ? _scrollOffset / _scrollRange : 1f - _scrollOffset / _scrollRange;
            _knobRect.sizeDelta = new Vector2(0f, knob);
            _knobRect.anchoredPosition = new Vector2(0f, -fromTop * travel);
        }
    }
}
