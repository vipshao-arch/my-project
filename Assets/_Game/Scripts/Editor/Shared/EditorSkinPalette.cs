using UnityEngine;
using UnityEditor;

namespace Game.EditorTools.Shared
{
    /// <summary>
    /// 跨工具共享的 Editor UI 调色板（Character Kit / Level Kit / Combat Sandbox 共用）。
    ///
    /// 抽取自 SkillBuilderWizard.UI 的色值方案（Phase 0 基础设施）。
    /// SkillBuilderWizard 本体未做改动（保持已验证稳定的行为不变），
    /// 本类仅供 Game Dev Suite 下的新建工具（GameDevSuiteWindow 及后续 Level Kit / Combat Sandbox）复用，
    /// 避免每个新工具各写一份 Light/Dark Skin 判断逻辑。
    /// </summary>
    public static class EditorSkinPalette
    {
        static bool IsDark => EditorGUIUtility.isProSkin;

        /// <summary>Dark Skin 下直接用给定 RGB；Light Skin 下自动提亮，保持两套 Skin 视觉一致</summary>
        static Color DC(float r, float g, float b) => IsDark
            ? new Color(r, g, b)
            : new Color(Mathf.Clamp01(r + 0.62f), Mathf.Clamp01(g + 0.62f), Mathf.Clamp01(b + 0.62f));

        static Color LC(float r, float g, float b, float a = 1f) => new Color(r, g, b, a);

        // —— 背景层 ———————————————————————————
        public static Color BgPanel     => DC(0.18f, 0.18f, 0.20f);
        public static Color BgHeader    => DC(0.18f, 0.18f, 0.20f);
        public static Color BgRowAlt1   => DC(0.18f, 0.18f, 0.20f);
        public static Color BgRowAlt2   => DC(0.14f, 0.14f, 0.16f);
        public static Color BgSplit     => DC(0.08f, 0.08f, 0.08f);
        public static Color Divider     => DC(0.05f, 0.05f, 0.05f);
        public static Color DividerSoft => DC(0.10f, 0.10f, 0.10f);

        // —— 文字色阶 ———————————————————————————
        public static Color TextPrimary   => IsDark ? LC(0.85f, 0.85f, 0.85f) : LC(0.15f, 0.15f, 0.15f);
        public static Color TextSecondary => IsDark ? LC(0.65f, 0.65f, 0.65f) : LC(0.30f, 0.30f, 0.30f);
        public static Color TextTertiary  => IsDark ? LC(0.45f, 0.45f, 0.45f) : LC(0.50f, 0.50f, 0.50f);

        // —— 高亮 / 交互(两套 Skin 共用) ————————————
        public static Color Highlight => LC(0.24f, 0.49f, 0.91f);
        public static Color Success   => LC(0.30f, 0.70f, 0.40f);
        public static Color Warning   => LC(0.90f, 0.65f, 0.20f);
        public static Color Danger    => LC(0.85f, 0.30f, 0.30f);
        public static Color SplitLine => LC(0.50f, 0.50f, 0.50f);

        // —— 常用 style 工厂 ——————————————————
        public static GUIStyle TitleStyle() => new GUIStyle(EditorStyles.boldLabel)
        {
            normal = { textColor = TextPrimary }
        };
        public static GUIStyle SubTitleStyle() => new GUIStyle(EditorStyles.miniBoldLabel)
        {
            normal = { textColor = TextSecondary },
            alignment = TextAnchor.MiddleLeft
        };
        public static GUIStyle TipStyle() => new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            wordWrap = true,
            normal = { textColor = TextTertiary }
        };
    }
}
