#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using Game.SkillSystem;

namespace Game.SkillSystem.EditorTools
{
    /// <summary>
    /// SkillBuilderWizard — SceneView Gizmo 绘制部分。
    /// 包含 OnSceneGui 入口和 5 类判定形状 + VFX offset Handle 的绘制逻辑。
    /// </summary>
    public partial class SkillBuilderWizard : EditorWindow
    {
    // ═══════════════════════════════════════════════════════════════
    // Phase 5.4: SceneView Gizmo 5类判定形状
    // ═══════════════════════════════════════════════════════════════
    void OnSceneGui(SceneView sceneView)
    {
        // V3 P3:命中框 Gizmo(基于 _previewRuntime 在 PreviewInstance 上画的全局事件)
        _previewRuntime?.DrawHitGizmos();

        // 2026-07-28(test07 对齐):SceneView 语义 = 编辑期只看"当前展开节点"的形状;
        // Play 模式触发时的范围显示由 RuntimeSkillEffectSink.EmitHitGizmo(Debug.DrawLine)承担。
        // 旧代码被 Handles.BeginGUI()/EndGUI() 包裹导致 3D 形状画到屏幕外,已移除。
        // 锚点:场景中选中的角色(带 Animator)优先,否则预览实例。
        GameObject targetGO = null;
        var sel = Selection.activeGameObject;
        if (sel != null && sel.GetComponentInChildren<Animator>(true) != null)
            targetGO = sel;
        else if (_previewInstance != null)
            targetGO = _previewInstance;

        if (_editingTarget == null || _expandedNodeIndex < 0 || _expandedNodeIndex >= _editingTarget.graphData.Count)
            return;

        var node = _editingTarget.graphData[_expandedNodeIndex];
        if (node == null || targetGO == null) return;

        // 展开节点的判定形状(编辑期调试)
        switch (node)
        {
            case MeleeSwingData melee:        DrawMeleeSwingGizmo(targetGO, melee); break;
            case RectShotData rect:           DrawRectShotGizmo(targetGO, rect); break;
            case AOECircularData aoe:         DrawAOECircularGizmo(targetGO, aoe); break;
            case BeamData beam:               DrawBeamGizmo(targetGO, beam); break;
            case CurvedProjectileData curved: DrawCurvedProjectileGizmo(targetGO, curved); break;
            case ChainBounceData chain:       DrawChainBounceGizmo(targetGO, chain); break;
        }

        // P1-3:VFX 节点 positionOffset 拖拽 Gizmo —— 在骨骼/角色根位置画球形 Handle,
        // 拖拽后将世界坐标差值回写到 positionOffset 字段并标 dirty。
        // 支持 CastVFXData / MidVFXData / HitVFXData(后者 spawnBone 通常为空=命中目标,
        // 预览时用 targetGO 本地坐标系,方便调试偏移量)。
        DrawVFXOffsetHandle(targetGO, node, _expandedNodeIndex);
    }

    // ═══════════════════════════════════════════════════════════════
    // 2026-07-28(test07 对齐):预览窗 3D overlay —— 伤害范围 + 参考坐标轴
    // v2: Handles.SetCamera 与 PreviewRenderUtility 的 pixelRect 坐标系存在偏移,
    //     改为手动 WorldToViewportPoint 投影到 GUI 坐标绘制,位置严格对齐。
    //     所有形状采样为线段/三角扇,用 2D Handles 画在预览纹理之上。
    // ═══════════════════════════════════════════════════════════════
    Camera _overlayCam;
    Rect   _overlayRect;

    void DrawPreviewOverlay(Rect rect)
    {
        if (_previewRtu == null || _previewRtu.camera == null) return;
        _overlayCam = _previewRtu.camera;
        // v3: GUI.BeginClip 裁剪,overlay 不超出预览窗口;
        //     投影坐标改为 rect 本地坐标(0,0 起点)。
        _overlayRect = new Rect(0f, 0f, rect.width, rect.height);
        GUI.BeginClip(rect);
        if (_showRefAxes) DrawReferenceAxes();
        if (_showHitRange) DrawPreviewHitRanges();
        Handles.color = Color.white;
        GUI.EndClip();
    }

    Vector2 WorldToOverlayGUI(Vector3 w)
    {
        Vector3 vp = _overlayCam.WorldToViewportPoint(w);
        return new Vector2(vp.x * _overlayRect.width,
                           (1f - vp.y) * _overlayRect.height);
    }

    bool OverlayVisible(Vector3 w) => _overlayCam.WorldToViewportPoint(w).z > 0f;

    void OverlayLine(Vector3 a, Vector3 b)
    {
        if (!OverlayVisible(a) && !OverlayVisible(b)) return;   // 两端都在相机后,跳过
        Handles.DrawLine(WorldToOverlayGUI(a), WorldToOverlayGUI(b));
    }

    // 3D 圆环 → 线段逼近(normal 为圆面法线)
    void OverlayCircle(Vector3 c, Vector3 normal, float r, int seg = 40)
    {
        normal = normal.normalized;
        Vector3 t = Vector3.Cross(normal,
            Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.99f ? Vector3.right : Vector3.up).normalized;
        Vector3 b = Vector3.Cross(normal, t);
        Vector3 prev = c + t * r;
        for (int i = 1; i <= seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            Vector3 p = c + (t * Mathf.Cos(a) + b * Mathf.Sin(a)) * r;
            OverlayLine(prev, p);
            prev = p;
        }
    }

    // 扇形:三角扇填充 + 径向线 + 外弧(360° 即整圆盘)
    void OverlaySector(Vector3 c, Vector3 up, Vector3 fromDir, float angleDeg, float r,
                       Color fill, Color wire, int seg = 24)
    {
        Vector2 pc = WorldToOverlayGUI(c);
        Vector3 prevW = c + fromDir * r;
        Vector2 prev = WorldToOverlayGUI(prevW);
        for (int i = 1; i <= seg; i++)
        {
            float a = i / (float)seg * angleDeg;
            Vector3 endW = c + Quaternion.AngleAxis(a, up) * fromDir * r;
            Vector2 p = WorldToOverlayGUI(endW);
            Handles.color = fill;
            Handles.DrawAAConvexPolygon(pc, prev, p);
            Handles.color = wire;
            OverlayLine(prevW, endW);
            prevW = endW;
            prev = p;
        }
        Handles.color = wire;
        Handles.DrawLine(pc, WorldToOverlayGUI(c + fromDir * r));
        Handles.DrawLine(pc, prev);
    }

    void OverlayLabel(Vector3 w, string text)
    {
        Vector2 p = WorldToOverlayGUI(w);
        GUI.Label(new Rect(p.x + 6, p.y - 8, 180, 16), text, EditorStyles.whiteMiniLabel);
    }

    void DrawPreviewHitRanges()
    {
        var data = GetActivePreviewData();
        if (data == null || data.graphData == null || _previewInstance == null) return;
        for (int i = 0; i < data.graphData.Count; i++)
        {
            switch (data.graphData[i])
            {
                case MeleeSwingData melee:        OverlayMelee(melee); break;
                case AOECircularData aoe:         OverlayAOE(aoe); break;
                case RectShotData rect:           OverlayRectShot(rect); break;
                case BeamData beam:               OverlayBeam(beam); break;
                case CurvedProjectileData curved: OverlayCurved(curved); break;
                case ChainBounceData chain:       OverlayChain(chain); break;
            }
        }
    }

    // 链式弹射:内圈=首跳搜索半径,外圈=最大弹射范围 + 弹射方向标记
    void OverlayChain(ChainBounceData data)
    {
        var t = _previewInstance.transform;
        Vector3 center = t.position;
        Handles.color = new Color(0.6f, 0.4f, 0.95f, 0.9f);
        OverlayCircle(center, Vector3.up, data.searchRadius);
        if (data.maxBounceRange > data.searchRadius + 0.1f)
        {
            Handles.color = new Color(0.5f, 0.3f, 0.9f, 0.45f);
            OverlayCircle(center, Vector3.up, data.maxBounceRange);
        }
        int marks = Mathf.Min(data.bounceCount, 6);
        Handles.color = new Color(0.6f, 0.4f, 0.95f, 0.5f);
        for (int b = 0; b < marks; b++)
        {
            float ang = b * 360f / Mathf.Max(data.bounceCount, 1);
            var dir = Quaternion.Euler(0f, ang, 0f) * t.forward;
            OverlayCircle(center + dir * data.searchRadius, Vector3.up, 0.3f, 16);
        }
        OverlayLabel(center + t.up * 0.5f, $"链式 首跳 {data.searchRadius}m / 弹射 {data.bounceCount} 次");
    }

    void OverlayMelee(MeleeSwingData data)
    {
        var t = _previewInstance.transform;
        Vector3 origin = t.position + t.up * data.originHeight + t.forward * data.originForwardOffset;
        OverlaySector(origin, t.up,
            Quaternion.Euler(0, -data.hitAngle * 0.5f, 0) * t.forward,
            data.hitAngle, data.range,
            new Color(0.8f, 0.2f, 0.2f, 0.05f), new Color(0.9f, 0.3f, 0.3f, 0.9f));
        Handles.color = new Color(0.9f, 0.3f, 0.3f, 0.5f);
        OverlayCircle(origin, t.up, data.hitRadius);
        // 纵向高度可视化(2026-07-29):扇形两侧 + 弧中点画竖线,高度=verticalRange
        float vHalf = data.verticalRange > 0.01f ? data.verticalRange * 0.5f : data.hitRadius;
        Handles.color = new Color(0.9f, 0.5f, 0.3f, 0.7f);
        for (int i = -1; i <= 1; i++)
        {
            var dir = Quaternion.Euler(0, i * data.hitAngle * 0.5f, 0) * t.forward;
            Vector3 end = origin + dir * data.range;
            OverlayLine(end - Vector3.up * vHalf, end + Vector3.up * vHalf);
        }
        OverlayLabel(origin + t.up * (vHalf + 0.2f), $"近战 {data.range}m / {data.hitAngle}° / 高 {vHalf * 2f:F1}m");
    }

    void OverlayAOE(AOECircularData data)
    {
        var t = _previewInstance.transform;
        Vector3 center = data.centerIsTargetPoint
            ? t.position + t.forward * data.radius
            : t.position;
        OverlaySector(center, Vector3.up, t.forward, 360f, data.radius,
            new Color(0.8f, 0.6f, 0.2f, 0.04f), new Color(0.9f, 0.7f, 0.25f, 0.9f));
        if (data.height > 0f)
        {
            // 2026-07-31:圆柱判定 —— 顶环 + 竖线
            Handles.color = new Color(0.9f, 0.7f, 0.25f, 0.8f);
            OverlayCircle(center + Vector3.up * data.height, Vector3.up, data.radius);
            Handles.color = new Color(0.9f, 0.7f, 0.25f, 0.5f);
            for (int i = 0; i < 4; i++)
            {
                var dir = Quaternion.Euler(0f, i * 90f, 0f) * t.forward;
                OverlayLine(center + dir * data.radius, center + dir * data.radius + Vector3.up * data.height);
            }
            OverlayLabel(center + Vector3.up * (data.height + 0.3f), $"AOE 半径 {data.radius}m × 高 {data.height}m");
        }
        else
        {
            Handles.color = new Color(0.9f, 0.7f, 0.25f, 0.8f);
            OverlayCircle(center, t.forward, data.radius);   // 垂直环 1(球)
            OverlayCircle(center, t.right, data.radius);     // 垂直环 2(球)
            Handles.color = new Color(0.9f, 0.7f, 0.25f, 0.4f);
            OverlayCircle(center, Vector3.up, data.radius * 0.5f);
            OverlayLabel(center + Vector3.up * 0.5f, $"AOE 半径 {data.radius}m(球)");
        }
    }

    void OverlayRectShot(RectShotData data)
    {
        var t = _previewInstance.transform;
        Vector3 f = t.forward, u = t.up, r = t.right;
        Vector3 c0 = t.position + u * data.spawnHeight + f * data.forwardOffset;
        Vector3 c1 = c0 + f * data.maxRange;
        float hw = data.width * 0.5f, hh = data.height * 0.5f;
        Vector3[] s = { c0 - r*hw - u*hh, c0 + r*hw - u*hh, c0 + r*hw + u*hh, c0 - r*hw + u*hh };
        Vector3[] e = { c1 - r*hw - u*hh, c1 + r*hw - u*hh, c1 + r*hw + u*hh, c1 - r*hw + u*hh };
        Handles.color = new Color(0.3f, 0.7f, 0.95f, 0.9f);
        for (int i = 0; i < 4; i++)
        {
            OverlayLine(s[i], s[(i + 1) % 4]);
            OverlayLine(e[i], e[(i + 1) % 4]);
            OverlayLine(s[i], e[i]);
        }
        OverlayLabel(c0 + u * 0.5f, $"弹幕 {data.maxRange}m × {data.width}m");
    }

    void OverlayBeam(BeamData data)
    {
        var t = _previewInstance.transform;
        Vector3 f = t.forward;
        Vector3 origin = t.position + Vector3.up * data.originHeight;
        Vector3 end = origin + f * data.range;
        float hw = data.beamWidth * 0.5f;
        Handles.color = new Color(0.95f, 0.9f, 0.3f, 0.95f);
        OverlayLine(origin, end);
        Handles.color = new Color(0.95f, 0.9f, 0.3f, 0.6f);
        OverlayLine(origin + t.right * hw, origin - t.right * hw);
        OverlayLine(origin + t.up * hw, origin - t.up * hw);
        OverlayLine(end + t.right * hw, end - t.right * hw);
        OverlayLine(end + t.up * hw, end - t.up * hw);
        OverlayLabel(origin + t.up * 0.5f, $"光束 {data.range}m / 宽 {data.beamWidth}m");
    }

    void OverlayCurved(CurvedProjectileData data)
    {
        var t = _previewInstance.transform;
        Vector3 f = t.forward;
        Vector3 origin = t.position + t.up * data.spawnHeight + f * data.forwardOffset;
        Vector3 velocity = (Quaternion.Euler(-data.launchPitch, 0, 0) * f) * data.speed;
        Handles.color = new Color(0.3f, 0.9f, 0.5f, 0.9f);
        const int seg = 20;
        Vector3 prev = origin;
        for (int i = 1; i < seg; i++)
        {
            float tt = i / (float)(seg - 1) * data.maxRange / Mathf.Max(0.01f, data.speed);
            Vector3 p = origin + velocity * tt + Vector3.down * (0.5f * data.gravity * tt * tt);
            if (p.y < data.groundSnapY) p.y = data.groundSnapY;
            OverlayLine(prev, p);
            prev = p;
        }
        OverlayLabel(origin + t.up * 0.5f, $"抛物线 {data.maxRange}m");
    }

    // 参考坐标轴:X 红 / Y 绿 / Z 蓝,锚在预览角色脚下,便于辨认朝向
    void DrawReferenceAxes()
    {
        Vector3 o = _previewInstance != null ? _previewInstance.transform.position : Vector3.zero;
        const float len = 1f;
        Handles.color = new Color(0.9f, 0.25f, 0.25f, 0.95f);   // X 红
        OverlayLine(o, o + Vector3.right * len);
        OverlayLabel(o + Vector3.right * len, "X");
        Handles.color = new Color(0.3f, 0.85f, 0.3f, 0.95f);    // Y 绿
        OverlayLine(o, o + Vector3.up * len);
        OverlayLabel(o + Vector3.up * len, "Y");
        Handles.color = new Color(0.3f, 0.5f, 0.95f, 0.95f);    // Z 蓝
        OverlayLine(o, o + Vector3.forward * len);
        OverlayLabel(o + Vector3.forward * len, "Z");
    }

    // P1-3:VFX positionOffset 球形拖拽 Handle
    void DrawVFXOffsetHandle(GameObject go, SkillNodeData node, int nodeIndex)
    {
        // 反射读 spawnBone / positionOffset 字段(兼容 CastVFXData / MidVFXData / HitVFXData)
        var type = node.GetType();
        var boneField   = type.GetField("spawnBone",       System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        var offsetField = type.GetField("positionOffset",  System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        if (offsetField == null || offsetField.FieldType != typeof(Vector3)) return;

        // 计算 Gizmo 的世界坐标基点(骨骼 > 角色根)
        Transform anchor = go.transform;
        if (boneField != null)
        {
            string bone = boneField.GetValue(node) as string;
            if (!string.IsNullOrEmpty(bone))
            {
                var animator = go.GetComponentInChildren<Animator>(true);
                if (animator != null)
                {
                    // 尝试找骨骼 transform(HumanBodyBones 或普通 Find)
                    foreach (HumanBodyBones hbb in System.Enum.GetValues(typeof(HumanBodyBones)))
                    {
                        if (hbb == HumanBodyBones.LastBone) continue;
                        try
                        {
                            var t = animator.GetBoneTransform(hbb);
                            if (t != null && string.Equals(t.name, bone, StringComparison.OrdinalIgnoreCase))
                            { anchor = t; break; }
                        }
                        catch { }
                    }
                    if (anchor == go.transform)
                    {
                        // fallback: 递归 Find
                        var found = FindChildByName(go.transform, bone);
                        if (found != null) anchor = found;
                    }
                }
            }
        }

        Vector3 offset = (Vector3)offsetField.GetValue(node);
        Vector3 worldPos = anchor.TransformPoint(offset);

        // 画球形 Handle + 拖拽
        Handles.color = new Color(0.3f, 0.85f, 0.3f, 0.85f);
        float size = HandleUtility.GetHandleSize(worldPos) * 0.12f;
        EditorGUI.BeginChangeCheck();
        Vector3 newWorld = Handles.FreeMoveHandle(worldPos, size, Vector3.zero, Handles.SphereHandleCap);
        if (EditorGUI.EndChangeCheck())
        {
            Vector3 newLocal = anchor.InverseTransformPoint(newWorld);
            Undo.RecordObject(_editingTarget, "调整 VFX 偏移");
            offsetField.SetValue(node, newLocal);
            EditorUtility.SetDirty(_editingTarget);
            MarkPreviewParamsDirty();
        }

        // 标注线(从骨骼锚点到 VFX 生成点)
        Handles.color = new Color(0.3f, 0.85f, 0.3f, 0.4f);
        Handles.DrawDottedLine(anchor.position, worldPos, 4f);
        Handles.Label(worldPos + Vector3.up * size * 1.5f,
            $"VFX offset\n({offset.x:0.00}, {offset.y:0.00}, {offset.z:0.00})",
            EditorStyles.miniLabel);
    }

    // 递归按名称查找子 Transform(骨骼名匹配用,大小写不敏感)
    static Transform FindChildByName(Transform root, string name)
    {
        if (string.Equals(root.name, name, StringComparison.OrdinalIgnoreCase)) return root;
        foreach (Transform c in root)
        {
            var r = FindChildByName(c, name);
            if (r != null) return r;
        }
        return null;
    }

    // 2026-07-28(test07 对齐):链式弹射 Gizmo(参考近战配色,以紫色调区分弹射特性)
    void DrawChainBounceGizmo(GameObject go, ChainBounceData data)
    {
        Vector3 center = go.transform.position;
        Vector3 forward = go.transform.forward;

        // 首跳搜索半径(紫色实圈)
        Handles.color = new Color(0.6f, 0.4f, 0.95f, 0.9f);
        Handles.DrawWireDisc(center, Vector3.up, data.searchRadius);

        // 最大弹射范围(虚线外圈)
        if (data.maxBounceRange > data.searchRadius + 0.1f)
        {
            Handles.color = new Color(0.5f, 0.3f, 0.9f, 0.45f);
            Handles.DrawWireDisc(center, Vector3.up, data.maxBounceRange);
        }

        // 弹射方向标记(沿朝向画 bounceCount 个小菱形标记)
        int marks = Mathf.Min(data.bounceCount, 6);
        Handles.color = new Color(0.6f, 0.4f, 0.95f, 0.6f);
        for (int b = 0; b < marks; b++)
        {
            float ang = b * 360f / Mathf.Max(data.bounceCount, 1);
            var dir = Quaternion.Euler(0f, ang, 0f) * forward;
            Handles.DrawWireDisc(center + dir * data.searchRadius, Vector3.up, 0.3f);
        }

        Handles.Label(center + Vector3.up * 0.5f,
            $"链式弹射\n首跳: {data.searchRadius}m\n弹射: {data.bounceCount} 次 / {data.maxBounceRange}m\n伤害: {data.damage}");
    }

    void DrawMeleeSwingGizmo(GameObject go, MeleeSwingData data)
    {
        Vector3 origin = go.transform.position + go.transform.up * data.originHeight
                        + go.transform.forward * data.originForwardOffset;
        float range = data.range;
        float hitAngle = data.hitAngle;
        Vector3 forward = go.transform.forward;
        Vector3 up = go.transform.up;

        Handles.color = new Color(0.8f, 0.2f, 0.2f, 0.4f); // 红色半透明
        // 绘制扇形判定范围（左右两半）
        Handles.DrawSolidArc(origin, up, Quaternion.Euler(0, -hitAngle * 0.5f, 0) * forward, hitAngle, range);
        Handles.color = new Color(0.8f, 0.2f, 0.2f, 0.6f); // 边框
        Handles.DrawWireArc(origin, up, Quaternion.Euler(0, -hitAngle * 0.5f, 0) * forward, hitAngle, range);
        
        // 绘制命中半径
        Handles.color = new Color(0.8f, 0.2f, 0.2f, 0.3f);
        Handles.DrawSolidDisc(origin, up, data.hitRadius);
        Handles.color = new Color(0.8f, 0.2f, 0.2f, 0.5f);
        Handles.DrawWireDisc(origin, up, data.hitRadius);
        
        // 绘制原点标记
        Handles.color = Color.white;
        Handles.SphereHandleCap(0, origin, Quaternion.identity, 0.1f, EventType.Repaint);

        // 2026-07-29(test07 对齐):纵向高度可视化 —— 扇形两侧+弧中点画竖线,高度 = verticalRange
        {
            float vHalf = data.verticalRange > 0.01f ? data.verticalRange * 0.5f : data.hitRadius;
            Handles.color = new Color(0.9f, 0.5f, 0.3f, 0.7f);
            for (int i = -1; i <= 1; i++)
            {
                var dir = Quaternion.Euler(0, i * hitAngle * 0.5f, 0) * forward;
                Vector3 end = origin + dir * range;
                Handles.DrawLine(end - Vector3.up * vHalf, end + Vector3.up * vHalf);
            }
        }

        // 显示信息
        Handles.Label(origin + Vector3.up * 0.5f, $"近战挥击\n范围: {range}m\n角度: {hitAngle}°\n高度: {data.verticalRange:F1}m\n伤害: {data.damage}");
    }

    void DrawRectShotGizmo(GameObject go, RectShotData data)
    {
        Vector3 forward = go.transform.forward;
        Vector3 up = go.transform.up;
        Vector3 right = go.transform.right;
        float spawnHeight = data.spawnHeight;
        float forwardOffset = data.forwardOffset;
        
        Vector3 center = go.transform.position + up * spawnHeight + forward * forwardOffset;
        float maxRange = data.maxRange;
        
        // 绘制矩形判定区域（从发射点延伸到最大射程）
        Vector3[] corners = new Vector3[4];
        corners[0] = center + right * (-data.width * 0.5f);
        corners[1] = center + right * (data.width * 0.5f);
        corners[2] = center + forward * maxRange + right * (data.width * 0.5f);
        corners[3] = center + forward * maxRange + right * (-data.width * 0.5f);
        corners[0].y = center.y - data.height * 0.5f;
        corners[1].y = center.y - data.height * 0.5f;
        corners[2].y = center.y + data.height * 0.5f;
        corners[3].y = center.y + data.height * 0.5f;
        
        Handles.color = new Color(0.2f, 0.6f, 0.8f, 0.3f); // 蓝色半透明
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                if (i != j)
                    Handles.DrawLine(corners[i], corners[j]);
            }
        }
        
        Handles.color = new Color(0.2f, 0.6f, 0.8f, 0.6f); // 边框
        Handles.DrawWireCube(center + forward * maxRange * 0.5f, new Vector3(maxRange, data.height, data.width));
        
        // 绘制弹体散布锥（如果有散布）
        if (data.spreadAngle > 0 && data.count > 1)
        {
            Handles.color = new Color(0.2f, 0.6f, 0.8f, 0.2f);
            float coneLength = maxRange;
            float coneRadius = Mathf.Tan(data.spreadAngle * Mathf.Deg2Rad) * coneLength;
            Vector3 coneBase = center + forward * coneLength;
            // 绘制圆锥底面圆
            Handles.DrawWireDisc(coneBase, forward, coneRadius);
            // 绘制圆锥轮廓线
            Handles.DrawLine(center, coneBase + Vector3.right * coneRadius);
            Handles.DrawLine(center, coneBase - Vector3.right * coneRadius);
            Handles.DrawLine(center, coneBase + go.transform.right * coneRadius);
            Handles.DrawLine(center, coneBase - go.transform.right * coneRadius);
        }
        
        // 显示信息
        Handles.color = Color.white;
        Handles.Label(center + Vector3.up * 0.5f, $"矩形弹幕\n射程: {maxRange}m\n尺寸: {data.width}x{data.height}\n数量: {data.count}");
    }

    void DrawAOECircularGizmo(GameObject go, AOECircularData data)
    {
        Vector3 center;
        if (data.centerIsTargetPoint)
            center = go.transform.position + go.transform.forward * data.radius;
        else
            center = go.transform.position;
        
        float radius = data.radius;
        
        Handles.color = new Color(0.8f, 0.6f, 0.2f, 0.4f); // 橙色半透明
        Handles.DrawSolidDisc(center, Vector3.up, radius);
        
        Handles.color = new Color(0.8f, 0.6f, 0.2f, 0.6f); // 边框
        Handles.DrawWireDisc(center, Vector3.up, radius);
        
        // 绘制同心圆（内圈 50% 半径）
        Handles.color = new Color(0.8f, 0.6f, 0.2f, 0.3f);
        Handles.DrawWireDisc(center, Vector3.up, radius * 0.5f);

        if (data.height > 0f)
        {
            // 2026-07-31:圆柱判定(height>0)—— 顶环 + 四条竖线表现立体范围
            Handles.color = new Color(0.8f, 0.6f, 0.2f, 0.7f);
            Handles.DrawWireDisc(center + Vector3.up * data.height, Vector3.up, radius);
            Handles.color = new Color(0.8f, 0.6f, 0.2f, 0.5f);
            for (int i = 0; i < 4; i++)
            {
                var dir = Quaternion.Euler(0f, i * 90f, 0f) * go.transform.forward;
                Handles.DrawLine(center + dir * radius, center + dir * radius + Vector3.up * data.height);
            }
        }
        else
        {
            // 2026-07-29(test07 对齐):球体判定(height=0)—— 两个垂直方向的圆环,直观表现球形覆盖
            Handles.color = new Color(0.8f, 0.6f, 0.2f, 0.5f);
            Handles.DrawWireDisc(center, go.transform.forward, radius);
            Handles.DrawWireDisc(center, go.transform.right, radius);
        }

        // 绘制中心标记
        Handles.color = Color.white;
        Handles.SphereHandleCap(0, center, Quaternion.identity, 0.1f, EventType.Repaint);
        
        // 显示信息
        Handles.Label(center + Vector3.up * 0.5f, $"圆形 AOE\n半径: {radius}m\n伤害: {data.damage}");
    }

    void DrawBeamGizmo(GameObject go, BeamData data)
    {
        Vector3 forward = go.transform.forward;
        Vector3 origin = go.transform.position + Vector3.up * data.originHeight;
        float range = data.range;
        float width = data.beamWidth;
        
        Vector3 end = origin + forward * range;
        
        // 绘制光束主体
        Handles.color = new Color(0.8f, 0.8f, 0.2f, 0.5f); // 黄色半透明
        Handles.DrawLine(origin, end);
        
        // 绘制光束宽度（矩形截面）
        Vector3 right = go.transform.right;
        Vector3 up = go.transform.up;
        float halfWidth = width * 0.5f;
        
        Handles.color = new Color(0.8f, 0.8f, 0.2f, 0.3f);
        Vector3[] beamCorners = new Vector3[4];
        beamCorners[0] = origin + right * -halfWidth + up * -halfWidth;
        beamCorners[1] = origin + right * halfWidth + up * -halfWidth;
        beamCorners[2] = end + right * halfWidth + up * -halfWidth;
        beamCorners[3] = end + right * -halfWidth + up * -halfWidth;
        
        for (int i = 0; i < 3; i++)
            Handles.DrawLine(beamCorners[i], beamCorners[i + 1]);
        Handles.DrawLine(beamCorners[3], beamCorners[0]);
        
        // 绘制起点和终点标记
        Handles.color = Color.white;
        Handles.SphereHandleCap(0, origin, Quaternion.identity, 0.1f, EventType.Repaint);
        Handles.SphereHandleCap(0, end, Quaternion.identity, 0.1f, EventType.Repaint);
        
        // 显示信息
        Handles.Label(origin + Vector3.up * 0.5f, $"光束\n射程: {range}m\n宽度: {width}m\n伤害: {data.damage}");
    }

    void DrawCurvedProjectileGizmo(GameObject go, CurvedProjectileData data)
    {
        Vector3 forward = go.transform.forward;
        Vector3 up = go.transform.up;
        Vector3 origin = go.transform.position + up * data.spawnHeight + forward * data.forwardOffset;
        float maxRange = data.maxRange;
        float gravity = data.gravity;
        float launchPitch = data.launchPitch;
        float speed = data.speed;
        
        // 计算抛物线轨迹（采样 20 个点）
        int sampleCount = 20;
        Vector3[] trajectoryPoints = new Vector3[sampleCount];
        
        // 初始速度方向（根据发射角度）
        Vector3 launchDir = Quaternion.Euler(-launchPitch, 0, 0) * forward;
        Vector3 velocity = launchDir * speed;
        
        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / (sampleCount - 1) * maxRange / speed;
            trajectoryPoints[i] = origin + velocity * t + Vector3.down * 0.5f * gravity * t * t;
            
            // 地面吸附
            if (trajectoryPoints[i].y < data.groundSnapY)
                trajectoryPoints[i].y = data.groundSnapY;
        }
        
        // 绘制抛物线
        Handles.color = new Color(0.2f, 0.8f, 0.4f, 0.6f); // 绿色
        for (int i = 0; i < sampleCount - 1; i++)
            Handles.DrawLine(trajectoryPoints[i], trajectoryPoints[i + 1]);
        
        // 绘制弹体宽度矩形（沿轨迹的几个关键点）
        float halfWidth = data.width * 0.5f;
        float halfHeight = data.height * 0.5f;
        
        Handles.color = new Color(0.2f, 0.8f, 0.4f, 0.3f);
        int keyPoints = 4;
        for (int i = 0; i < keyPoints; i++)
        {
            int idx = i * (sampleCount - 1) / (keyPoints - 1);
            Vector3 pos = trajectoryPoints[idx];
            Vector3 localForward = (idx < sampleCount - 1 ? trajectoryPoints[idx + 1] : trajectoryPoints[idx]) - pos;
            if (localForward.sqrMagnitude < 0.001f) continue;
            Vector3 localRight = Vector3.Cross(localForward.normalized, up).normalized;
            Vector3 localUp = Vector3.Cross(localRight, localForward.normalized).normalized;
            
            Vector3[] rectCorners = new Vector3[4];
            rectCorners[0] = pos + localRight * -halfWidth + localUp * -halfHeight;
            rectCorners[1] = pos + localRight * halfWidth + localUp * -halfHeight;
            rectCorners[2] = pos + localRight * halfWidth + localUp * halfHeight;
            rectCorners[3] = pos + localRight * -halfWidth + localUp * halfHeight;
            
            for (int j = 0; j < 4; j++)
                Handles.DrawLine(rectCorners[j], rectCorners[(j + 1) % 4]);
        }
        
        // 绘制起点和终点标记
        Handles.color = Color.white;
        Handles.SphereHandleCap(0, origin, Quaternion.identity, 0.1f, EventType.Repaint);
        if (trajectoryPoints.Length > 0)
            Handles.SphereHandleCap(0, trajectoryPoints[sampleCount - 1], Quaternion.identity, 0.1f, EventType.Repaint);
        
        // 显示信息
        Handles.Label(origin + Vector3.up * 0.5f, $"抛物线弹体\n射程: {maxRange}m\n速度: {speed}m/s\n发射角: {launchPitch}°\n重力: {gravity}");
    }
    }
}
#endif
