using UnityEngine;
using UnityEngine.UI;

namespace Game.Character
{
    /// <summary>
    /// 角色头顶 HUD：血量 / 体力 / 法力 三条 Image.fillAmount。
    /// 运行时创建 World Space Canvas（独立于场景 Canvas），跟随角色头顶并面向相机。
    /// 玩家和敌人用同一套，配色/尺寸/可见性在 Inspector 暴露。
    /// </summary>
    [AddComponentMenu("Game/Character System/Character HUD")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterMotor))]
    public class CharacterHUD : MonoBehaviour
    {
        [Header("Motor")]
        public CharacterMotor motor;

        [Header("Layout")]
        [Tooltip("头顶偏移（世界单位）")]
        public float heightOffset = 2.2f;
        [Tooltip("血条宽度（Canvas 内像素）")]
        public float barWidth = 80f;
        [Tooltip("血条高度")]
        public float barHeight = 8f;
        [Tooltip("细条相对血条的比例（0~1）")]
        [Range(0.05f, 0.5f)] public float thinBarRatio = 0.6f;
        [Tooltip("条之间间距（像素）")]
        public float barSpacing = 1f;
        [Tooltip("Canvas 缩放（世界单位/像素）")]
        public float canvasScale = 0.01f;

        [Header("Colors - 配色")]
        [Tooltip("血条背景")]
        public Color healthBgColor   = new Color(0.1f,  0.1f,  0.1f,  0.8f);
        [Tooltip("血条填充（红）")]
        public Color healthFillColor = new Color(0.9f,  0.15f, 0.15f, 1f);
        [Tooltip("体力条背景")]
        public Color staminaBgColor  = new Color(0.1f,  0.1f,  0.1f,  0.8f);
        [Tooltip("体力条填充（绿）")]
        public Color staminaFillColor= new Color(0.2f,  0.85f, 0.25f, 1f);
        [Tooltip("法力条背景")]
        public Color manaBgColor     = new Color(0.1f,  0.1f,  0.1f,  0.8f);
        [Tooltip("法力条填充（蓝）")]
        public Color manaFillColor   = new Color(0.25f, 0.5f,  0.95f, 1f);

        [Header("Visibility")]
        public bool showHealth  = true;
        public bool showStamina = true;
        public bool showMana    = true;

        // 运行时引用
        private GameObject _canvasGo;
        private Image _healthFill, _staminaFill, _manaFill;
        private Camera _mainCamera;
        private bool _eventsRegistered;
        private static Sprite _whiteSprite;

        void Start()
        {
            if (motor == null) motor = GetComponentInParent<CharacterMotor>();
            if (motor == null) motor = GetComponent<CharacterMotor>();
            if (motor == null)
            {
                Debug.LogError($"[CharacterHUD] No CharacterMotor on {name}, disabling.", this);
                enabled = false;
                return;
            }
            BuildCanvas();
            TryBindMotor();
        }

        void Update()
        {
            if (!_eventsRegistered) TryBindMotor();
        }

        void LateUpdate()
        {
            if (_canvasGo == null) return;

            // 跟随头顶
            _canvasGo.transform.position = transform.position + Vector3.up * heightOffset;

            // 面向相机
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera != null)
            {
                Vector3 dir = _canvasGo.transform.position - _mainCamera.transform.position;
                if (dir.sqrMagnitude > 0.0001f)
                    _canvasGo.transform.rotation = Quaternion.LookRotation(dir);
            }
        }

        void OnDestroy() => UnbindMotor();

        /// <summary>联机阵营着色(远端副本由 NetworkCharacterSetup 调用):改字段+已建 Image 同步。</summary>
        public void SetHealthFillColor(Color c)
        {
            healthFillColor = c;
            if (_healthFill != null) _healthFill.color = c;
        }

        /// <summary>切换头顶 HUD 整体可见性(上车隐藏 / 下车恢复)。</summary>
        public void SetVisible(bool visible)
        {
            if (_canvasGo != null)
                _canvasGo.SetActive(visible);
        }

        // ── 事件订阅 ──
        private void TryBindMotor()
        {
            if (_eventsRegistered || motor == null) return;
            motor.onHealthChanged  += UpdateHealth;
            motor.onStaminaChanged += UpdateStamina;
            motor.onManaChanged    += UpdateMana;
            _eventsRegistered = true;

            UpdateHealth(motor.currentHealth);
            UpdateStamina(motor.currentStamina);
            UpdateMana(motor.currentMana);
        }

        private void UnbindMotor()
        {
            if (!_eventsRegistered || motor == null) return;
            motor.onHealthChanged  -= UpdateHealth;
            motor.onStaminaChanged -= UpdateStamina;
            motor.onManaChanged    -= UpdateMana;
            _eventsRegistered = false;
        }

        // ── 公开更新接口（事件回调 + 外部手动调用） ──
        public void UpdateHealth(float current)
        {
            if (_healthFill == null || motor == null) return;
            _healthFill.fillAmount = motor.maxHealth > 0f
                ? Mathf.Clamp01(current / motor.maxHealth) : 0f;
        }
        public void UpdateStamina(float current)
        {
            if (_staminaFill == null || motor == null) return;
            _staminaFill.fillAmount = motor.maxStamina > 0f
                ? Mathf.Clamp01(current / motor.maxStamina) : 0f;
        }
        public void UpdateMana(float current)
        {
            if (_manaFill == null || motor == null) return;
            _manaFill.fillAmount = motor.maxMana > 0f
                ? Mathf.Clamp01(current / motor.maxMana) : 0f;
        }

        // ── 共享 1×1 白图（Image.fillAmount 生效前提） ──
        static Sprite GetWhiteSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "HUD_WhiteTex" };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 100f);
            _whiteSprite.name = "HUD_WhiteSprite";
            return _whiteSprite;
        }

        // ── 构造 WorldSpace Canvas ──
        private void BuildCanvas()
        {
            var whiteSprite = GetWhiteSprite();
            float thinH = barHeight * thinBarRatio;

            // 可见条数计算总高度
            int shown = (showHealth ? 1 : 0) + (showStamina ? 1 : 0) + (showMana ? 1 : 0);
            float totalH = 0f;
            if (showHealth)  totalH += barHeight;
            if (showStamina) totalH += thinH;
            if (showMana)    totalH += thinH;
            totalH += barSpacing * Mathf.Max(0, shown - 1);

            _canvasGo = new GameObject("HUD_Canvas");
            _canvasGo.transform.SetParent(transform, false);
            _canvasGo.transform.localPosition = Vector3.up * heightOffset;

            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;

            var crt = _canvasGo.GetComponent<RectTransform>();
            crt.sizeDelta = new Vector2(barWidth, totalH);
            crt.localScale = new Vector3(canvasScale, canvasScale, canvasScale);

            float y = 0f; // 自下而上累加
            if (showHealth)
            {
                _healthFill = CreateBar(_canvasGo.transform, "Health",
                    barHeight, healthBgColor, healthFillColor, whiteSprite, y);
                y += barHeight + barSpacing;
            }
            if (showStamina)
            {
                _staminaFill = CreateBar(_canvasGo.transform, "Stamina",
                    thinH, staminaBgColor, staminaFillColor, whiteSprite, y);
                y += thinH + barSpacing;
            }
            if (showMana)
            {
                _manaFill = CreateBar(_canvasGo.transform, "Mana",
                    thinH, manaBgColor, manaFillColor, whiteSprite, y);
            }
        }

        // 创建一条水平条（铺满宽度 / 指定高度），返回 fill Image
        private Image CreateBar(Transform parent, string name,
            float height, Color bgColor, Color fillColor, Sprite sprite, float yFromBottom)
        {
            // 容器 RectTransform
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot     = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, height);
            rt.anchoredPosition = new Vector2(0f, yFromBottom);

            // 背景（铺满）
            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(go.transform, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.sprite = sprite;
            bgImg.color  = bgColor;
            bgImg.type   = Image.Type.Simple;

            // Fill（按比例填充）
            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(go.transform, false);
            var fillRt = fillGo.AddComponent<RectTransform>();
            fillRt.anchorMin = new Vector2(0f, 0f);
            fillRt.anchorMax = new Vector2(1f, 1f);
            fillRt.offsetMin = Vector2.zero; fillRt.offsetMax = Vector2.zero;
            var fillImg = fillGo.AddComponent<Image>();
            fillImg.sprite = sprite;
            fillImg.color  = fillColor;
            fillImg.type       = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImg.fillAmount = 1f;

            return fillImg;
        }
    }
}
