#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Game.Character.EditorTools.CharacterKit
{
    public partial class CharacterKitPanel
    {
        // ═══════════════════════════════════════════════════════════════
        // P2.3：GUIStyle 懒加载缓存
        // 避免 OnGUI 每帧 new GUIStyle(...) 产生 GC 分配。
        // 使用实例字段 lazy-init（非 static readonly），避免类型初始化时机问题。
        // ═══════════════════════════════════════════════════════════════

        // ── 标题样式 ─────────────────────────────────────────────────
        GUIStyle _sTitle14;
        GUIStyle StyleTitle14 => _sTitle14 ?? (_sTitle14 = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 });

        GUIStyle _sTitle12;
        GUIStyle StyleTitle12 => _sTitle12 ?? (_sTitle12 = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 });

        GUIStyle _sBoldLabel;
        GUIStyle StyleBoldLabel => _sBoldLabel ?? (_sBoldLabel = new GUIStyle(EditorStyles.boldLabel));

        // ── 帮助框 ───────────────────────────────────────────────────
        GUIStyle _sHelpBox;
        GUIStyle StyleHelpBox => _sHelpBox ?? (_sHelpBox = new GUIStyle(EditorStyles.helpBox));

        // ── MiniLabel（模板，使用时可能覆写颜色）────────────────────
        GUIStyle _sMiniLabelTemplate;
        GUIStyle StyleMiniLabel => _sMiniLabelTemplate ?? (_sMiniLabelTemplate = new GUIStyle(EditorStyles.miniLabel));

        // ── 按钮样式 ─────────────────────────────────────────────────
        GUIStyle _sBoldButton13;
        GUIStyle StyleBoldButton13 => _sBoldButton13 ?? (_sBoldButton13 = new GUIStyle(GUI.skin.button)
            { fontStyle = FontStyle.Bold, fontSize = 13 });

        GUIStyle _sBoldButton12;
        GUIStyle StyleBoldButton12 => _sBoldButton12 ?? (_sBoldButton12 = new GUIStyle(GUI.skin.button)
            { fontStyle = FontStyle.Bold, fontSize = 12 });

        GUIStyle _sButton;
        GUIStyle StyleButton => _sButton ?? (_sButton = new GUIStyle(GUI.skin.button));

        // ── 流水线步骤按钮标签 ──────────────────────────────────────
        GUIStyle _sStepLabel;
        GUIStyle StyleStepLabel => _sStepLabel ?? (_sStepLabel = new GUIStyle(EditorStyles.label));

        GUIStyle _sStepLabelSelected;
        GUIStyle StyleStepLabelSelected => _sStepLabelSelected ?? (_sStepLabelSelected = new GUIStyle(EditorStyles.label)
            { normal = { textColor = new Color(0.3f, 0.7f, 1f) }, fontStyle = FontStyle.Bold });

        // ── 获取带颜色覆写的 miniLabel（不创建新实例，仅覆写颜色后返回）─
        GUIStyle GetMiniLabelColor(Color color)
        {
            var s = StyleMiniLabel;
            s.normal.textColor = color;
            return s;
        }

        // ── Diagnostic 专用样式 ──────────────────────────────────────
        GUIStyle _sDiagResultBox;
        GUIStyle StyleDiagResultBox => _sDiagResultBox ?? (_sDiagResultBox = new GUIStyle(EditorStyles.helpBox));

        GUIStyle _sDiagErrorLabel;
        GUIStyle StyleDiagErrorLabel => _sDiagErrorLabel ?? (_sDiagErrorLabel = new GUIStyle(EditorStyles.miniLabel)
            { normal = { textColor = Color.red } });

        GUIStyle _sDiagFixHeader;
        GUIStyle StyleDiagFixHeader => _sDiagFixHeader ?? (_sDiagFixHeader = new GUIStyle(EditorStyles.miniLabel)
            { normal = { textColor = new Color(0.85f, 0.3f, 0.2f) }, fontStyle = FontStyle.Bold });

        GUIStyle _sDiagFixItem;
        GUIStyle StyleDiagFixItem => _sDiagFixItem ?? (_sDiagFixItem = new GUIStyle(EditorStyles.miniLabel)
            { normal = { textColor = new Color(0.9f, 0.25f, 0.15f) } });

        GUIStyle _sDiagBoldButton;
        GUIStyle StyleDiagBoldButton => _sDiagBoldButton ?? (_sDiagBoldButton = new GUIStyle(GUI.skin.button)
            { fontStyle = FontStyle.Bold, fontSize = 13 });

        // ── SpringBone / Ragdoll 按钮 ────────────────────────────────
        GUIStyle _sApplyButton;
        GUIStyle StyleApplyButton => _sApplyButton ?? (_sApplyButton = new GUIStyle(GUI.skin.button)
            { fontStyle = FontStyle.Bold });

        GUIStyle _sRemoveButton;
        GUIStyle StyleRemoveButton => _sRemoveButton ?? (_sRemoveButton = new GUIStyle(GUI.skin.button)
            { normal = { textColor = new Color(0.8f, 0.2f, 0.2f) } });

        // ── Skill 徽章 ───────────────────────────────────────────────
        GUIStyle _sSkillBadge;
        GUIStyle StyleSkillBadge => _sSkillBadge ?? (_sSkillBadge = new GUIStyle(EditorStyles.miniButton)
            { normal = { textColor = Color.white } });

        // ── 流水线独立模式徽章 ──────────────────────────────────────
        GUIStyle _sStandaloneBadge;
        GUIStyle StyleStandaloneBadge => _sStandaloneBadge ?? (_sStandaloneBadge = new GUIStyle(EditorStyles.miniLabel)
            { normal = { textColor = new Color(0.3f, 0.7f, 1f) }, fontStyle = FontStyle.Bold });

        // ── 动画配置提示标签 ─────────────────────────────────────────
        GUIStyle _sAnimHint;
        GUIStyle StyleAnimHint => _sAnimHint ?? (_sAnimHint = new GUIStyle(EditorStyles.miniLabel));

        // ── Part 1 完成盒 ────────────────────────────────────────────
        GUIStyle _sPart1CompleteBox;
        GUIStyle StylePart1CompleteBox => _sPart1CompleteBox ?? (_sPart1CompleteBox = new GUIStyle(EditorStyles.helpBox));

        // ── 确认按钮（已修改状态，绿色文字） ──────────────────────────
        GUIStyle _sConfirmDirtyButton13;
        GUIStyle StyleConfirmDirtyButton13 => _sConfirmDirtyButton13 ?? (_sConfirmDirtyButton13 = new GUIStyle(GUI.skin.button)
            { fontStyle = FontStyle.Bold, fontSize = 13, normal = { textColor = new Color(0.4f, 1f, 0.4f) } });
    }
}
#endif
