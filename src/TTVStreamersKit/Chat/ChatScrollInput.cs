using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TTVStreamersKit.Chat
{
    /// <summary>
    /// チャットに当てたポインタのスティックの操作を受け取る。
    /// ゲームの VRInputModule は、スティックの傾きを scrollDelta にして、ポインタが当たっている物とその親に送る。
    /// チャットの画面（FloatingScreen）に付け、背景、歯車、移動用の掴む部分のどれに当たっていても受け取る。
    /// </summary>
    internal sealed class ChatScrollReceiver : MonoBehaviour, IScrollHandler
    {
        public Action<Vector2> Scrolled;

        public void OnScroll(PointerEventData eventData)
        {
            this.Scrolled?.Invoke(eventData.scrollDelta);
        }
    }

    /// <summary>
    /// 移動用の掴む部分（BSML の FloatingScreenHandle）を掴んでいるか。
    /// BSML は掴んでいる間、スティックの上下でウィンドウを手前と奥に動かすため、その間はスクロールしない。
    /// 押した物の部品すべてに届くため、FloatingScreenHandle と同じ物に付ける。
    /// </summary>
    internal sealed class ChatGrabWatcher : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public bool Grabbing { get; private set; }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.pointerPressRaycast.gameObject == this.gameObject) {
                this.Grabbing = true;
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            this.Grabbing = false;
        }

        private void OnDisable()
        {
            this.Grabbing = false;
        }
    }

    /// <summary>スクロールバーへのトリガーの操作（押した位置とドラッグ中の位置）を受け取る。</summary>
    internal sealed class ChatScrollbarInput : MonoBehaviour, IPointerDownHandler, IInitializePotentialDragHandler, IDragHandler
    {
        /// <summary>ポインタが指しているワールド座標。</summary>
        public Action<Vector3> Pointed;

        public void OnPointerDown(PointerEventData eventData) => this.Point(eventData);

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            // 押した直後から動きに付いてくるようにする
            eventData.useDragThreshold = false;
        }

        public void OnDrag(PointerEventData eventData) => this.Point(eventData);

        private void Point(PointerEventData eventData)
        {
            // ドラッグ中にスクロールバーから外れても、何かに当たっていれば高さだけを使う
            var hit = eventData.pointerCurrentRaycast;
            if (hit.gameObject != null) {
                this.Pointed?.Invoke(hit.worldPosition);
            }
        }
    }
}
