using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using HMUI;
using System;
using System.Linq;
using TMPro;
using TTVStreamersKit.Configuration;
using TTVStreamersKit.Utilities;
using UnityEngine;
using UnityEngine.UI;
using VRUIControls;
using Zenject;

namespace TTVStreamersKit.Requests.UI
{
    /// <summary>
    /// 選曲画面に置く、キューの画面を開くボタン。
    /// 表記（Open / Close）で受付の状態を、下線の色でキューに曲があるか（緑）ないか（赤）を示す。
    /// </summary>
    internal sealed class RequestButton : IInitializable, ITickable, IDisposable
    {
        private const string ButtonBsml =
            "<button id='request-button' text='Open' on-click='open-queue' pref-width='22' pref-height='8' " +
            "hover-hint='{{Open the song request queue|リクエストのキューを開きます}}' />";

        private static readonly Vector2 ButtonAreaSize = new Vector2(24f, 10f);

        private static readonly Color HasSongs = new Color(0.2f, 0.9f, 0.3f);
        private static readonly Color NoSongs = new Color(0.95f, 0.25f, 0.25f);

        private readonly PluginConfig _config;
        private readonly RequestManager _manager;
        private readonly SongPlayer _player;
        // ボタンを画面の面より少しだけ手前に置く距離（画面の単位）
        private const float SurfaceMargin = 1f;

        private GameObject _root;
        private RectTransform _rect;
        // 曲の一覧の画面の曲面（曲面の設定を持つ一番近い親）。奥行きの基準にする
        private CurvedCanvasSettings _screenCurve;
        private bool _curveMissingLogged;
        private QueueFlowCoordinator _flow;
        private int _flowLanguageVersion;

        [UIComponent("request-button")]
        private Button _button = null;

        public RequestButton(PluginConfig config, RequestManager manager, SongPlayer player)
        {
            _config = config;
            _manager = manager;
            _player = player;
        }

        public void Initialize()
        {
            try {
                this.CreateButton();
            }
            catch (Exception e) {
                Plugin.Log.Error($"Requests: failed to add the request button: {e}");
            }
            _manager.QueueChanged += this.UpdateButton;
            this.UpdateButton();
        }

        private static string PathOf(GameObject go)
        {
            if (go == null) {
                return "(nothing)";
            }
            var path = go.name;
            for (var t = go.transform.parent; t != null; t = t.parent) {
                path = t.name + "/" + path;
            }
            return path;
        }

        /// <summary>
        /// ボタンは自前のキャンバスに置き、曲の一覧の画面の子にする。曲の一覧の画面の外に置くため、自前の Raycaster を持たせる。
        /// <para>ゲームのポインターは、当たった物のうち<b>距離が最も近いもの</b>を選ぶ（並び順はほぼ同じ距離のときだけ比べる）。
        /// 画面は曲面で、端ほど手前に出ているため、平面のままのボタンは画面の奥になり、同じ位置の画面の要素に負ける。
        /// そこでボタンのキャンバスは平面（曲げない）にし、画面の曲面に接する向きで、面より少しだけ手前に置く（<see cref="PlaceOnScreenSurface"/>）。</para>
        /// </summary>
        private void CreateButton()
        {
            var parent = Resources.FindObjectsOfTypeAll<LevelCollectionNavigationController>().FirstOrDefault();
            if (parent == null) {
                Plugin.Log.Warn("Requests: the song list screen was not found. The request button is not shown.");
                return;
            }
            _root = new GameObject("TTVStreamersKit RequestButton",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CurvedCanvasSettings), typeof(VRGraphicRaycaster));
            // BSML の FloatingScreen と同じく、Raycaster の依存はコンテナから入れる
            BeatSaberUI.DiContainer.InjectGameObject(_root);
            var rect = (RectTransform)_root.transform;
            _rect = rect;
            rect.SetParent(parent.transform, false);
            _root.layer = parent.gameObject.layer;
            // 並び順を親の画面より1つ上にする。上書きしないと、同じ位置にある親の画面の表示にポインターを取られる
            var canvas = _root.GetComponent<Canvas>();
            var parentCanvas = parent.GetComponentInParent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = (parentCanvas != null ? parentCanvas.sortingOrder : 0) + 1;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            // 平面にする。半径 0 なら、当たりの判定も描画も、このキャンバスの面で行われる
            _root.GetComponent<CurvedCanvasSettings>().SetRadius(0f);
            // 奥行きの基準（画面の曲面）は、ここでは決めない。
            // 作る時点では曲の一覧はまだ画面に表示されておらず、画面の子になっていないため（ResolveScreenCurve で表示中に探す）
            rect.sizeDelta = ButtonAreaSize;
            // 既定は曲の一覧の右上。設定ファイルで調整できる
            rect.anchoredPosition = new Vector2(_config.Request.RequestButtonX, _config.Request.RequestButtonY);
            var layout = _root.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            BSMLParser.Instance.Parse(Localization.Bsml(ButtonBsml), _root, this);
        }

        public void Tick()
        {
            // 設定画面でリクエスト機能を切り替えたとき、ボタンの表示を合わせる
            if (_root != null && _root.activeSelf != _config.RequestEnabled) {
                this.UpdateButton();
            }
            if (_root != null && _root.activeInHierarchy) {
                this.PlaceOnScreenSurface();
            }
        }

        /// <summary>
        /// 画面の曲面（中心は見る人の側、半径は画面の <see cref="CurvedCanvasSettings"/>）に接する向きにし、
        /// 面より <see cref="SurfaceMargin"/> だけ手前に置く。横と縦の位置は設定の値のまま変えない。
        /// </summary>
        private void PlaceOnScreenSurface()
        {
            if (_rect == null || !this.ResolveScreenCurve()) {
                return;
            }
            var screen = _screenCurve.transform;
            var radius = _screenCurve.radius;
            var local = screen.InverseTransformPoint(_rect.position);
            var z = -SurfaceMargin;
            var angle = 0f;
            if (radius >= 0.1f) {
                var x = Mathf.Clamp(local.x, -radius, radius);
                var theta = Mathf.Asin(x / radius);
                z += -radius + radius * Mathf.Cos(theta);
                angle = theta * Mathf.Rad2Deg;
            }
            var rotation = screen.rotation * Quaternion.Euler(0f, angle, 0f);
            if (Mathf.Abs(local.z - z) > 0.01f || Quaternion.Angle(_rect.rotation, rotation) > 0.05f) {
                _rect.position = screen.TransformPoint(new Vector3(local.x, local.y, z));
                _rect.rotation = rotation;
            }
        }

        /// <summary>
        /// 奥行きの基準にする画面の曲面を探す。曲の一覧が画面に表示されてから、曲面の設定を持つ一番近い親を使う
        /// （rootCanvas は画面よりさらに上のキャンバスで、曲面の設定を持たず、座標の単位も違う）。
        /// 別の画面に移ったときは探し直す。
        /// </summary>
        private bool ResolveScreenCurve()
        {
            if (_screenCurve != null && _rect.IsChildOf(_screenCurve.transform)) {
                return true;
            }
            _screenCurve = _rect.parent != null
                ? _rect.parent.GetComponentsInParent<CurvedCanvasSettings>(true).FirstOrDefault()
                : null;
            if (_screenCurve == null) {
                if (!_curveMissingLogged) {
                    _curveMissingLogged = true;
                    Plugin.Log.Warn($"Requests: no curved screen was found above {PathOf(_rect.gameObject)}. The request button is not moved to the screen surface.");
                }
                return false;
            }
            return true;
        }

        [UIAction("open-queue")]
        private void OpenQueue()
        {
            try {
                // 言語を変えたあとは、画面を作り直す
                if (_flow != null && _flowLanguageVersion != Localization.Version && !_flow.isActivated) {
                    UnityEngine.Object.Destroy(_flow.gameObject);
                    _flow = null;
                }
                if (_flow == null) {
                    _flow = BeatSaberUI.CreateFlowCoordinator<QueueFlowCoordinator>();
                    _flow.Setup(_manager, _player);
                    _flowLanguageVersion = Localization.Version;
                }
                _flow.Open();
            }
            catch (Exception e) {
                Plugin.Log.Error($"Requests: failed to open the queue: {e}");
            }
        }

        private void UpdateButton()
        {
            if (_root == null) {
                return;
            }
            _root.SetActive(_config.RequestEnabled);
            if (_button == null) {
                return;
            }
            // 受付の状態は言語によらず同じ表記にする
            var text = _button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text != null) {
                text.text = _manager.IsOpen ? "Open" : "Close";
            }
            var color = _manager.Queue.Any(r => r.Status == RequestStatus.Queued) ? HasSongs : NoSongs;
            var underline = _button.GetComponentsInChildren<Image>(true)
                .FirstOrDefault(i => i.name.IndexOf("Underline", StringComparison.OrdinalIgnoreCase) >= 0);
            if (underline != null) {
                underline.color = color;
            }
        }

        public void Dispose()
        {
            _manager.QueueChanged -= this.UpdateButton;
            if (_flow != null) {
                UnityEngine.Object.Destroy(_flow.gameObject);
                _flow = null;
            }
            if (_root != null) {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
        }
    }
}
