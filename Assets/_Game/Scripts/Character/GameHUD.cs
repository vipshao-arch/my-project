using UnityEngine;
using UnityEngine.UI;

namespace Game.Character
{
    /// <summary>
    /// 屏幕级玩家 HUD（Screen Space Overlay Canvas）。
    /// 显示血量/体力/法力三槽，固定在屏幕左上角，与头顶 World Space HUD 互补。
    ///
    /// 用法：放在场景根物体上，自动查找 Player；也可手动指定 motor。
    /// </summary>
    [AddComponentMenu("Game/Character System/Game HUD")]
    public class GameHUD : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("玩家的 CharacterMotor。留空 = 自动查找 Tag=Player。")]
        public CharacterMotor motor;

        [Header("Layout")]
        public Vector2 panelPosition = new Vector2(20, 20);
        public float barWidth = 240f;
        public float barHeight = 22f;
        public float barSpacing = 6f;
        public float labelWidth = 70f;

        [Header("Colors")]
        public Color healthBg   = new Color(0.08f, 0.08f, 0.08f, 0.85f);
        public Color healthFill = new Color(0.92f, 0.15f, 0.15f, 1f);
        public Color staminaBg  = new Color(0.08f, 0.08f, 0.08f, 0.85f);
        public Color staminaFill= new Color(0.2f,  0.85f, 0.25f, 1f);
        public Color manaBg     = new Color(0.08f, 0.08f, 0.08f, 0.85f);
        public Color manaFill   = new Color(0.25f, 0.5f,  0.95f, 1f);

        [Header("Visibility")]
        public bool showHealth  = true;
        public bool showStamina = true;
        public bool showMana    = true;

        // Runtime
        private Canvas _canvas;
        private Image _healthFill, _staminaFill, _manaFill;
        private Text _healthText, _staminaText, _manaText;
        private Font _dynFont;

        void Awake()
        {
            if (motor == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                    motor = player.GetComponent<CharacterMotor>();
            }
        }

        void Start()
        {
            if (motor == null)
            {
                Debug.LogWarning("[GameHUD] No CharacterMotor found. HUD disabled.");
                enabled = false;
                return;
            }

            BuildCanvas();
            BindMotor();
        }

        void OnDestroy()
        {
            UnbindMotor();
        }

        // ── 构造 Canvas ──────────────────────────────────────────────────

        private void BuildCanvas()
        {
            _dynFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var go = new GameObject("GameHUD_Canvas");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 1000;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();

            // 面板容器
            var panel = new GameObject("Panel");
            panel.transform.SetParent(go.transform, false);
            var panelRt = panel.AddComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0, 1);
            panelRt.anchorMax = new Vector2(0, 1);
            panelRt.pivot = new Vector2(0, 1);
            panelRt.anchoredPosition = panelPosition;

            var panelBg = panel.AddComponent<Image>();
            panelBg.color = new Color(0, 0, 0, 0); // transparent

            float y = 0f;
            int shown = (showHealth ? 1 : 0) + (showStamina ? 1 : 0) + (showMana ? 1 : 0);
            float totalH = shown * (barHeight + barSpacing) - (shown > 0 ? barSpacing : 0);
            panelRt.sizeDelta = new Vector2(barWidth + labelWidth + 20, totalH + 16);

            if (showHealth)
            {
                (_healthFill, _healthText) = CreateBar(panel.transform, "Health",
                    healthBg, healthFill, "HP", y);
                y -= barHeight + barSpacing;
            }
            if (showStamina)
            {
                (_staminaFill, _staminaText) = CreateBar(panel.transform, "Stamina",
                    staminaBg, staminaFill, "SP", y);
                y -= barHeight + barSpacing;
            }
            if (showMana)
            {
                (_manaFill, _manaText) = CreateBar(panel.transform, "Mana",
                    manaBg, manaFill, "MP", y);
            }
        }

        private (Image fill, Text label) CreateBar(Transform parent, string name,
            Color bg, Color fill, string label, float yPos)
        {
            // 行容器
            var row = new GameObject(name);
            row.transform.SetParent(parent, false);
            var rowRt = row.AddComponent<RectTransform>();
            rowRt.anchorMin = new Vector2(0, 1);
            rowRt.anchorMax = new Vector2(0, 1);
            rowRt.pivot = new Vector2(0, 1);
            rowRt.anchoredPosition = new Vector2(8, yPos - 8);
            rowRt.sizeDelta = new Vector2(barWidth + labelWidth, barHeight);

            // 标签
            var lblGo = new GameObject("Label");
            lblGo.transform.SetParent(row.transform, false);
            var lblRt = lblGo.AddComponent<RectTransform>();
            lblRt.anchorMin = new Vector2(0, 0.5f);
            lblRt.anchorMax = new Vector2(0, 0.5f);
            lblRt.pivot = new Vector2(0, 0.5f);
            lblRt.sizeDelta = new Vector2(labelWidth, barHeight);
            var lblText = lblGo.AddComponent<Text>();
            lblText.text = label;
            lblText.font = _dynFont;
            lblText.fontSize = 16;
            lblText.color = Color.white;
            lblText.alignment = TextAnchor.MiddleLeft;

            // 血条背景
            var barBg = new GameObject("BarBg");
            barBg.transform.SetParent(row.transform, false);
            var barBgRt = barBg.AddComponent<RectTransform>();
            barBgRt.anchorMin = new Vector2(0, 0.5f);
            barBgRt.anchorMax = new Vector2(0, 0.5f);
            barBgRt.pivot = new Vector2(0, 0.5f);
            barBgRt.anchoredPosition = new Vector2(labelWidth + 6, 0);
            barBgRt.sizeDelta = new Vector2(barWidth, barHeight);
            var barBgImg = barBg.AddComponent<Image>();
            barBgImg.color = bg;
            barBgImg.sprite = GetWhiteSprite();

            // 血条填充
            var barFill = new GameObject("BarFill");
            barFill.transform.SetParent(barBg.transform, false);
            var barFillRt = barFill.AddComponent<RectTransform>();
            barFillRt.anchorMin = Vector2.zero;
            barFillRt.anchorMax = Vector2.one;
            barFillRt.offsetMin = Vector2.zero;
            barFillRt.offsetMax = Vector2.zero;
            var barFillImg = barFill.AddComponent<Image>();
            barFillImg.sprite = GetWhiteSprite();
            barFillImg.color = fill;
            barFillImg.type = Image.Type.Filled;
            barFillImg.fillMethod = Image.FillMethod.Horizontal;
            barFillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
            barFillImg.fillAmount = 1f;

            // 数值文字（覆盖在血条上方）
            var valGo = new GameObject("Value");
            valGo.transform.SetParent(barBg.transform, false);
            var valRt = valGo.AddComponent<RectTransform>();
            valRt.anchorMin = Vector2.zero;
            valRt.anchorMax = Vector2.one;
            valRt.offsetMin = Vector2.zero;
            valRt.offsetMax = Vector2.zero;
            var valText = valGo.AddComponent<Text>();
            valText.font = _dynFont;
            valText.fontSize = 14;
            valText.color = Color.white;
            valText.alignment = TextAnchor.MiddleCenter;

            return (barFillImg, valText);
        }

        // ── 事件绑定 ──────────────────────────────────────────────────────

        private void BindMotor()
        {
            if (motor == null) return;
            motor.onHealthChanged  += UpdateHealth;
            motor.onStaminaChanged += UpdateStamina;
            motor.onManaChanged    += UpdateMana;
            UpdateHealth(motor.currentHealth);
            UpdateStamina(motor.currentStamina);
            UpdateMana(motor.currentMana);
        }

        private void UnbindMotor()
        {
            if (motor == null) return;
            motor.onHealthChanged  -= UpdateHealth;
            motor.onStaminaChanged -= UpdateStamina;
            motor.onManaChanged    -= UpdateMana;
        }

        private void UpdateHealth(float current)
        {
            if (_healthFill != null)
            {
                float ratio = motor.maxHealth > 0f ? Mathf.Clamp01(current / motor.maxHealth) : 0f;
                _healthFill.fillAmount = ratio;
            }
            if (_healthText != null)
                _healthText.text = $"{current:F0} / {motor.maxHealth:F0}";
        }

        private void UpdateStamina(float current)
        {
            if (_staminaFill != null)
            {
                float ratio = motor.maxStamina > 0f ? Mathf.Clamp01(current / motor.maxStamina) : 0f;
                _staminaFill.fillAmount = ratio;
            }
            if (_staminaText != null)
                _staminaText.text = $"{current:F0} / {motor.maxStamina:F0}";
        }

        private void UpdateMana(float current)
        {
            if (_manaFill != null)
            {
                float ratio = motor.maxMana > 0f ? Mathf.Clamp01(current / motor.maxMana) : 0f;
                _manaFill.fillAmount = ratio;
            }
            if (_manaText != null)
                _manaText.text = $"{current:F0} / {motor.maxMana:F0}";
        }

        // ── 共享白图 Sprite ───────────────────────────────────────────────

        private static Sprite _whiteSprite;

        private static Sprite GetWhiteSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "GameHUD_WhiteTex" };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 100f);
            _whiteSprite.name = "GameHUD_WhiteSprite";
            return _whiteSprite;
        }
    }
}
