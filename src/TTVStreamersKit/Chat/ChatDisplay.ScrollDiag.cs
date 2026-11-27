using System.Text;
using UnityEngine;

namespace TTVStreamersKit.Chat
{
    /// <summary>
    /// 調査用（一時的）。スクロールバーが崩れて表示される原因を調べるため、表示された後のフレームで実際の値をログに出す。
    /// 原因が分かったら、このファイルと呼び出し（UpdateScrollbar と LateUpdate）を削除する。
    /// </summary>
    internal sealed partial class ChatDisplay
    {
        private const int ScrollDiagLimit = 5;
        private bool _scrollDiagPending;
        private int _scrollDiagCount;
        private bool _scrollDiagWasVisible;

        /// <summary>UpdateScrollbar から呼ぶ。表示に切り替わったら、次の LateUpdate で値を出す。</summary>
        private void ScrollDiagOnUpdate(bool visible)
        {
            if (visible && !_scrollDiagWasVisible && _scrollDiagCount < ScrollDiagLimit) {
                _scrollDiagPending = true;
            }
            _scrollDiagWasVisible = visible;
        }

        /// <summary>LateUpdate から呼ぶ。描画の前（レイアウトの後）の値を出す。</summary>
        private void ScrollDiagTick()
        {
            if (!_scrollDiagPending || _scrollbar == null) {
                return;
            }
            _scrollDiagPending = false;
            _scrollDiagCount++;
            var sb = new StringBuilder();
            sb.AppendLine($"[ScrollDiag] #{_scrollDiagCount} frame={Time.frameCount} ChatWidth={this.ChatWidth} ChatHeight={this.ChatHeight} "
                          + $"range={_scrollRange} offset={_scrollOffset} content={_contentHeight} inGame={_isInGame}");
            AppendRect(sb, "screen", (RectTransform)_chatScreen.transform);
            AppendRect(sb, "scrollbar", _scrollbarRect);
            foreach (Transform child in _scrollbarRect) {
                AppendRect(sb, "  " + child.name, child as RectTransform);
            }
            var canvas = _scrollbar.GetComponent<Canvas>();
            if (canvas != null) {
                sb.AppendLine($"  canvas: enabled={canvas.enabled} isRoot={canvas.isRootCanvas} root={canvas.rootCanvas?.name} renderMode={canvas.renderMode} "
                              + $"overrideSorting={canvas.overrideSorting} sortingOrder={canvas.sortingOrder} scaleFactor={canvas.scaleFactor} "
                              + $"layer={_scrollbar.layer}");
            }
            var parentCanvas = _chatScreen.GetComponent<Canvas>();
            if (parentCanvas != null) {
                sb.AppendLine($"  parent canvas: renderMode={parentCanvas.renderMode} scaleFactor={parentCanvas.scaleFactor} layer={_chatScreen.gameObject.layer}");
            }
            Plugin.Log.Info(sb.ToString());
        }

        private static void AppendRect(StringBuilder sb, string name, RectTransform rt)
        {
            if (rt == null) {
                sb.AppendLine($"  {name}: (not a RectTransform)");
                return;
            }
            sb.AppendLine($"  {name}: active={rt.gameObject.activeInHierarchy} anchorMin={rt.anchorMin} anchorMax={rt.anchorMax} pivot={rt.pivot} "
                          + $"sizeDelta={rt.sizeDelta} anchoredPos={rt.anchoredPosition} rect={rt.rect} localPos={rt.localPosition} "
                          + $"localScale={rt.localScale} lossyScale={rt.lossyScale} localRot={rt.localRotation.eulerAngles}");
        }
    }
}
