using UnityEngine;
using UnityEngine.UI;

namespace Game.Character
{
    /// <summary>
    /// 敌人头顶血量条（Image.fillAmount）。
    /// 与玩家 CharacterHUD 共享同一套配色规范：HP 红色，运行时可改。
    /// 跟随敌人位置并面向摄像机；按比例缩短由 fillAmount 控制。
    /// </summary>
    [AddComponentMenu("Game/Character System/Enemy Health Bar")]
    [RequireComponent(typeof(EnemyMotor))]
    [DisallowMultipleComponent]
    public class EnemyHealthBar : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("头顶偏移（世界单位）")]
        public float heightOffset = 2.2f;
        [Tooltip("血条宽度（Canvas 内像素）")]
        public float barWidth = 60f;
        [Tooltip("血条高度")]
        public float barHeight = 6f;
        [Tooltip("Canvas 缩放（世界单位/像素）")]
        public float canvasScale = 0.01f;

        [Header("Colors")]
        [Tooltip("血条背景")]
        public Color backgroundColor = new Color(0.1f,  0.1f,  0.1f,  0.8f);
        [Tooltip("血条填充 - 默认（红）")]
        public Color fillColor       = new Color(0.9f,  0.15f, 0.15f, 1f);
        [Tooltip("中血量颜色（25%~50%）")]
        public Color midColor        = new Color(0.9f,  0.7f,  0.1f,  1f);
        [Tooltip("低血量颜色（<25%）")]
        public Color lowColor        = new Color(0.85f, 0.15f, 0.1f,  1f);
        [Range(0f, 1f)] public float midThreshold = 0.5f;
        [Range(0f, 1f)] public float lowThreshold = 0.25f;

        [Header("Visibility")]
        public bool showHealthBar = true;

        private GameObject _barCanvas;
        private Image _fillImage;
        private EnemyMotor _motor;
        private Camera _mainCamera;
        private static Sprite _whiteSprite;

        void Start()
        {
            _motor = GetComponent<EnemyMotor>();
            _mainCamera = Camera.main;
            if (showHealthBar) CreateHealthBar();

            if (_motor != null)
            {
                _motor.OnHealthChanged += UpdateHealth;
                UpdateHealth(_motor.currentHealth);
            }
        }

        void OnDestroy()
        {
            if (_motor != null) _motor.OnHealthChanged -= UpdateHealth;
        }

        void LateUpdate()
        {
            if (_barCanvas == null) return;
            _barCanvas.transform.position = transform.position + Vector3.up * heightOffset;
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera != null)
            {
                Vector3 dir = _barCanvas.transform.position - _mainCamera.transform.position;
                if (dir.sqrMagnitude > 0.0001f)
                    _barCanvas.transform.rotation = Quaternion.LookRotation(dir);
            }
        }

        static Sprite GetWhiteSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "EnemyBar_WhiteTex" };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 100f);
            _whiteSprite.name = "EnemyBar_WhiteSprite";
            return _whiteSprite;
        }

        void CreateHealthBar()
        {
            var whiteSprite = GetWhiteSprite();

            _barCanvas = new GameObject("EnemyHealthBar");
            _barCanvas.transform.SetParent(transform, false);
            _barCanvas.transform.localPosition = Vector3.up * heightOffset;

            var canvas = _barCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;

            var crt = _barCanvas.GetComponent<RectTransform>();
            crt.sizeDelta = new Vector2(barWidth, barHeight);
            crt.localScale = new Vector3(canvasScale, canvasScale, canvasScale);

            // 背景
            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(_barCanvas.transform, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.sprite = whiteSprite;
            bgImg.color  = backgroundColor;
            bgImg.type   = Image.Type.Simple;

            // Fill
            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(_barCanvas.transform, false);
            var fillRt = fillGo.AddComponent<RectTransform>();
            fillRt.anchorMin = new Vector2(0f, 0f);
            fillRt.anchorMax = new Vector2(1f, 1f);
            fillRt.offsetMin = Vector2.zero; fillRt.offsetMax = Vector2.zero;
            _fillImage = fillGo.AddComponent<Image>();
            _fillImage.sprite = whiteSprite;
            _fillImage.color  = fillColor;
            _fillImage.type       = Image.Type.Filled;
            _fillImage.fillMethod = Image.FillMethod.Horizontal;
            _fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            _fillImage.fillAmount = 1f;
        }

        public void UpdateHealth(float newHealth)
        {
            if (_motor == null || _fillImage == null) return;
            float ratio = _motor.maxHealth > 0f ? newHealth / _motor.maxHealth : 0f;
            ratio = Mathf.Clamp01(ratio);
            _fillImage.fillAmount = ratio;

            // 颜色阈值（默认 → 中 → 低）
            if      (ratio > midThreshold) _fillImage.color = fillColor;
            else if (ratio > lowThreshold) _fillImage.color = midColor;
            else                            _fillImage.color = lowColor;
        }
    }
}
