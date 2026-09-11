#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Collections.Generic;
using Game.SkillSystem;

namespace Game.SkillSystem.EditorTools
{
    public partial class SkillBuilderWizard : EditorWindow
    {

        // 拖拽中读临时值(Resize 单选 / Move 单选或多选),否则读节点正式值
        void GetDisplayTimeAndDuration(SkillNodeData data, int index, out float tt, out float dur)
        {
            if (_isDraggingClip && index == _dragNodeIndex && _dragKind != TimelineHitKind.None
                && _dragGroupNodes.Count <= 1)
            {
                tt  = _dragKind == TimelineHitKind.ResizeRight ? GetNodeTriggerTime(data) : _tempTriggerTime;
                dur = _dragKind == TimelineHitKind.ResizeLeft || _dragKind == TimelineHitKind.ResizeRight
                      ? _tempDuration : GetNodeDuration(data);
                return;
            }
            // 多选整体移动:每个节点用自己在 _dragGroupNodes 中的临时值
            if (_isDraggingClip && _dragGroupNodes.Count > 1)
            {
                int gi = _dragGroupNodes.IndexOf(data);
                if (gi >= 0)
                {
                    tt = _dragGroupTempTrigger[gi];
                    dur = GetNodeDuration(data);
                    return;
                }
            }
            tt = GetNodeTriggerTime(data);
            dur = GetNodeDuration(data);
        }

        // ═══════════════════════════════════════════════════════════════
        // Playhead —— 红色竖线 + 顶部三角旗(跟 Unity Timeline 一致)
        // ═══════════════════════════════════════════════════════════════
        void DrawPlayhead(Rect trackRect, Rect headerRect)
        {
            float x = headerRect.x + TimeToLocalX(_playheadT);
            if (x < headerRect.x - 10f || x > headerRect.xMax + 10f) return;   // 视口外不绘制

            // 顶端三角旗(在标尺上方,Unity 风格红色)
            Handles.color = new Color(0.9f, 0.25f, 0.25f);
            var flag = new Vector3[] {
                new Vector3(x, headerRect.y),
                new Vector3(x + 9f, headerRect.y),
                new Vector3(x, headerRect.y + 9f),
            };
            Handles.DrawAAConvexPolygon(flag);
            // 竖线
            Handles.color = new Color(0.9f, 0.25f, 0.25f, 0.85f);
            Handles.DrawLine(new Vector3(x, headerRect.yMax), new Vector3(x, trackRect.yMax));

            // playhead 拖动(点中三角旗或线可拖)
            var e = Event.current;
            var hitRect = new Rect(x - 4, headerRect.y, 12, trackRect.yMax - headerRect.y);
            if (e.type == EventType.MouseDown && e.button == 0 && hitRect.Contains(e.mousePosition))
            {
                GUIUtility.hotControl = _timelineControlId + 1;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == _timelineControlId + 1)
            {
                float newT = Mathf.Max(0f, LocalXToTime(e.mousePosition.x - headerRect.x));
                if (_snapToFrame)
                    newT = Mathf.Round(newT * _frameRate) / _frameRate;
                ResyncPreviewToPlayhead(newT);  // V3.1.8:playhead 拖动 → 同步驱动 Runtime
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseUp && GUIUtility.hotControl == _timelineControlId + 1)
            {
                GUIUtility.hotControl = 0;
                e.Use();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Step 1 / 2 / 3(原内容,完全保留)
        // ═══════════════════════════════════════════════════════════════
        void DrawStep1()
        {
            EditorGUILayout.LabelField("预设库（一键加载，内置配方）", EditorStyles.boldLabel);
            for (int i = 0; i < SkillPresets.PresetNames.Length; i++)
            {
                if (GUILayout.Button(new GUIContent(SkillPresets.PresetNames[i], "点击加载此预设配方"), GUILayout.Height(26)))
                {
                    _recipe = SkillPresets.GetPreset(i);
                    GUI.changed = true;
                    Debug.Log($"[SkillBuilder] 加载预设: {SkillPresets.PresetNames[i]}");
                }
            }
            EditorGUILayout.Space(10);

            // 方案 B(2026-07-06):自定义模板 —— 扫描 Common/SkillTemplates/ 下用户另存的完整技能,
            // 与上面的内置 Recipe 预设区分开(模板是完整 SkillData,不走 Recipe→graphData 生成流程,
            // 而是深拷贝整份 .asset 直接落到本次 Step2 填写的输出目录)。
            EditorGUILayout.LabelField("自定义模板（另存为的完整技能，一键复制）", EditorStyles.boldLabel);
            var userTemplates = ScanUserTemplates();
            if (userTemplates.Count == 0)
            {
                EditorGUILayout.HelpBox("暂无自定义模板。可在『编辑现有』或『角色技能库』里点『💾 另存为模板』沉淀一份。", MessageType.None);
            }
            else
            {
                foreach (var tpl in userTemplates)
                {
                    if (GUILayout.Button(new GUIContent($"📦 {tpl.name}", "点击复制此模板到当前输出目录"), GUILayout.Height(24)))
                    {
                        GenerateSkillDataFromTemplate(tpl);
                    }
                }
            }
            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("0 技能分类(决定走 Q/W/E/R 槽还是普攻槽)", EditorStyles.boldLabel);
            _recipe.category = (SkillCategory)EditorGUILayout.EnumPopup("category", _recipe.category);
            EditorGUILayout.HelpBox(
                _recipe.category == SkillCategory.BasicAttack
                    ? "普攻: 左键/自动攻击,常驻可用,一般不占冷却槽。"
                    : "主动技能: Q/W/E/R 槽,受冷却约束,可被替换/装备。",
                MessageType.None);
            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("① 射程类型", EditorStyles.boldLabel);
            _recipe.range = (SkillRange)GUILayout.SelectionGrid(
                (int)_recipe.range, new[] { "近程 (Melee)", "远程 (Ranged)" }, 2, "Button", GUILayout.Height(30));
            EditorGUILayout.HelpBox(
                _recipe.range == SkillRange.Melee ? "近程：角色前方近距离判定（0~3m）" : "远程：发射弹体/光束到达远处（3m+）",
                MessageType.None);
            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("② 范围类型", EditorStyles.boldLabel);
            _recipe.targeting = (SkillTargeting)GUILayout.SelectionGrid(
                (int)_recipe.targeting, new[] { "单体", "范围 AOE", "扇形", "直线" }, 4, "Button", GUILayout.Height(30));
            EditorGUILayout.HelpBox(GetTargetingDesc(_recipe.targeting), MessageType.None);
            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("③ 弹体类型", EditorStyles.boldLabel);
            string[] projLabels = { "即时", "直线飞行", "抛物线", "光束", "链式" };
            _recipe.projectile = (SkillProjectile)GUILayout.SelectionGrid(
                (int)_recipe.projectile, projLabels, 5, "Button", GUILayout.Height(30));
            // D1: Melee + 非 Instant 弹体 → 判定远程 + 移动策略近战，自相矛盾，自动纠正为 Instant
            if (_recipe.range == SkillRange.Melee && _recipe.projectile != SkillProjectile.Instant)
            {
                _recipe.projectile = SkillProjectile.Instant;
                Debug.Log("[SkillBuilder] Melee 射程不支持弹体类型，已自动切换为「即时」。");
            }
            EditorGUILayout.HelpBox(GetProjectileDesc(_recipe.projectile), MessageType.None);
            // D2: Ranged + Instant → 判定恒为 AOE，targeting 选择无效
            if (_recipe.range == SkillRange.Ranged && _recipe.projectile == SkillProjectile.Instant)
                EditorGUILayout.HelpBox("远程 + 即时：判定固定为 AOE 圆形范围，上方「范围类型」选择不影响结果。", MessageType.Info);
            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("④ 多段释放", EditorStyles.boldLabel);
            _recipe.multiHit = (SkillMultiHit)GUILayout.SelectionGrid(
                (int)_recipe.multiHit, new[] { "单次", "多段", "持续" }, 3, "Button", GUILayout.Height(30));
            EditorGUILayout.HelpBox(
                _recipe.multiHit == SkillMultiHit.Single ? "单次：一次命中结算" :
                _recipe.multiHit == SkillMultiHit.MultiHit ? "多段：连续 N 次命中，每次有间隔" :
                "持续：按住持续施法，按周期 tick 伤害", MessageType.None);
            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("⑤ Buff 效果", EditorStyles.boldLabel);
            _recipe.buff = (SkillBuffType)GUILayout.SelectionGrid(
                (int)_recipe.buff, new[] { "无", "自身增益", "目标减益" }, 3, "Button", GUILayout.Height(30));
            if (_recipe.buff != SkillBuffType.None)
                EditorGUILayout.HelpBox(
                    _recipe.buff == SkillBuffType.SelfBuff
                        ? "自身增益：命中后为施法者附加 Buff。"
                        : "目标减益：命中后为目标附加 Debuff。",
                    MessageType.None);
            EditorGUILayout.Space(12);

            EditorGUILayout.LabelField("当前技能配方", EditorStyles.boldLabel);
            DrawRecipePreview();

            if (GUILayout.Button("下一步 → 填写数值参数", GUILayout.Height(32)))
                _step = 2;
        }

        // ── 标准值下拉(2026-07-28,test07 对齐):值在列表中 → Popup;不在 → TextField + 「⇩」按钮切回标准值 ──
        // 命名规范来源: Animator Controller 规范(07-17) + graves 现有资产归纳
        static readonly string[] StdTriggers        = { "Attack", "SkillQ", "SkillW", "SkillE", "SkillR" };
        static readonly string[] StdTriggerLabels   = { "Attack (普攻)", "SkillQ", "SkillW", "SkillE", "SkillR" };
        // 与 StdTriggers 平行:选中 Trigger 时联动的 State 名称 / State ID
        static readonly string[] StdTriggerStates   = { "Attack_1", "Skill_Q", "Skill_W", "Skill_E", "Skill_R" };
        static readonly int[]    StdTriggerStateIds = { 1, 1, 2, 3, 4 };
        static readonly string[] StdStates          = { "", "Attack_1", "Attack_2", "Attack_3", "Skill_Q", "Skill_W", "Skill_E", "Skill_R", "SkillIdle" };
        static readonly string[] StdStateLabels     = { "(留空自动推导)", "Attack_1", "Attack_2", "Attack_3", "Skill_Q", "Skill_W", "Skill_E", "Skill_R", "SkillIdle" };
        // 与 SkillDataValidator 可识别骨点列表保持一致
        static readonly string[] StdVfxBones        = { "VFX_BladeTip", "VFX_Muzzle", "VFX_Handle", "VFX_Grip", "VFX_WeaponRoot", "VFX_SlashTop", "VFX_SlashMid" };
        // 段/槽位下拉标签(按分类):索引 = State ID - 1,Skill ID = (普攻?101:201) + 索引
        static readonly string[] AttackSlotLabels   = { "普攻 第1段", "普攻 第2段", "普攻 第3段", "普攻 第4段" };
        static readonly string[] SkillSlotLabels    = { "技能 Q", "技能 W", "技能 E", "技能 R" };

        // 段/槽位一次选择 → 同时驱动 State ID + Skill ID + Trigger + State 名称
        void ApplySlot(int idx)
        {
            bool isAttack = _recipe.category == SkillCategory.BasicAttack;
            _recipe.animStateId  = idx + 1;
            _recipe.skillId      = (isAttack ? 101 : 201) + idx;
            _recipe.animTrigger  = isAttack ? "Attack" : StdTriggers[idx + 1];
            _recipe.animClipName = isAttack ? $"Attack_{idx + 1}" : StdTriggerStates[idx + 1];
        }

        static string DrawStandardDropdown(string label, string value, string[] options, string[] displayLabels = null)
        {
            int idx = System.Array.IndexOf(options, value);
            if (idx < 0)
            {
                // 自定义值:文本框 + 一键切回标准下拉
                EditorGUILayout.BeginHorizontal();
                string v = EditorGUILayout.TextField(label, value);
                if (GUILayout.Button("⇩ 列表", EditorStyles.miniButton, GUILayout.Width(52)))
                    v = options[0];
                EditorGUILayout.EndHorizontal();
                return v;
            }
            int sel = EditorGUILayout.Popup(label, idx, displayLabels ?? options);
            return options[Mathf.Clamp(sel, 0, options.Length - 1)];
        }

        void DrawStep2()
        {
            bool isMelee = _recipe.range == SkillRange.Melee;
            bool isInstant = _recipe.projectile == SkillProjectile.Instant;
            bool isLine = _recipe.targeting == SkillTargeting.Line;

            // 分组与 SkillData Inspector(SkillDataEditor) ①~⑥ 一一对应,方便对照
            // ── ① 基础信息 ──
            EditorGUILayout.LabelField("① 基础信息", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            _recipe.skillName = EditorGUILayout.TextField("技能名称", _recipe.skillName);
            // Skill ID 由「分类 + ⑤ 段/槽位」自动派生(1xx=普攻段, 2xx=技能槽),不手动编辑
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.IntField("Skill ID (自动)", _recipe.skillId);
            EditorGUILayout.HelpBox("ID 规则: 1xx=普攻连段(101/102/...), 2xx=技能槽(201=Q, 202=W, 203=E, 204=R), 敌人技能=0。由⑤「段/槽位」选择自动匹配。", MessageType.None);
            _recipe.icon = (Sprite)EditorGUILayout.ObjectField("图标", _recipe.icon, typeof(Sprite), false);
            // 分类切换 → 自动重置为该分类第一段/槽(普攻→Attack_1, 技能→Skill_Q),两个 ID 同步匹配
            var newCategory = (SkillCategory)EditorGUILayout.EnumPopup("分类", _recipe.category);
            if (newCategory != _recipe.category)
            {
                _recipe.category = newCategory;
                ApplySlot(0);
            }
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(8);

            // ── ② 时间参数(秒) ──
            EditorGUILayout.LabelField("② 时间参数(秒)", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            _recipe.cooldown = EditorGUILayout.FloatField("冷却 (秒)", _recipe.cooldown);
            _recipe.castTime = EditorGUILayout.FloatField("施法时间 (秒)", _recipe.castTime);
            if (_recipe.multiHit == SkillMultiHit.Channel)
            {
                // channelDuration 由 tick 次数 × 间隔自动推导,与 ApplyToSkillData 一致
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.FloatField("持续时长 (自动)", Mathf.Max(0f, _recipe.hitCount * _recipe.hitInterval));
            }
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(8);

            // ── ③ 动画窗口(归一化时间 0~1) ──
            EditorGUILayout.LabelField("③ 动画窗口(归一化 0~1)", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            _recipe.frontSwing = EditorGUILayout.Slider("前摇比例", _recipe.frontSwing, 0f, 1f);
            _recipe.backSwing = EditorGUILayout.Slider("后摇比例", _recipe.backSwing, 0f, 1f);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(8);

            // ── ④ 动画融合(秒) ──
            EditorGUILayout.LabelField("④ 动画融合(秒)", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            _recipe.fadeInDuration = EditorGUILayout.FloatField("Fade In (秒)", _recipe.fadeInDuration);
            _recipe.fadeOutDuration = EditorGUILayout.FloatField("Fade Out (秒)", _recipe.fadeOutDuration);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(8);

            // ── ⑤ 动画身份 ──
            EditorGUILayout.LabelField("⑤ 动画身份", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            // 段/槽位统一下拉:一次选择同时驱动 State ID + Skill ID + Trigger + State 名称
            bool isAttackCat = _recipe.category == SkillCategory.BasicAttack;
            int slotIdx = Mathf.Clamp(_recipe.animStateId - 1, 0, 3);
            int newSlot = EditorGUILayout.Popup("段/槽位", slotIdx, isAttackCat ? AttackSlotLabels : SkillSlotLabels);
            if (newSlot != slotIdx) ApplySlot(newSlot);
            // Trigger 下拉(可手动覆盖):选择后联动 State 名称 / State ID / Skill ID
            string prevTrigger = _recipe.animTrigger;
            _recipe.animTrigger = DrawStandardDropdown("Animator Trigger", _recipe.animTrigger, StdTriggers, StdTriggerLabels);
            if (_recipe.animTrigger != prevTrigger)
            {
                int ti = System.Array.IndexOf(StdTriggers, _recipe.animTrigger);
                if (ti >= 0)
                {
                    if (string.IsNullOrEmpty(_recipe.animClipName) || System.Array.IndexOf(StdStates, _recipe.animClipName) >= 0)
                        _recipe.animClipName = StdTriggerStates[ti];
                    _recipe.animStateId = StdTriggerStateIds[ti];
                    _recipe.skillId = (isAttackCat ? 100 : 200) + StdTriggerStateIds[ti];
                }
            }
            _recipe.animClipName = DrawStandardDropdown("State 名称", _recipe.animClipName, StdStates, StdStateLabels);
            // 多段开启时：全局 AnimationClip 合并到第一段，不单独显示
            if (_recipe.multiHit == SkillMultiHit.MultiHit)
            {
                // 自动将全局 animClip 合并到第一段（若第一段未配置）
                if (_recipe.animClip != null && _recipe.stages != null && _recipe.stages.Count > 0
                    && _recipe.stages[0].animClip == null)
                {
                    _recipe.stages[0].animClip = _recipe.animClip;
                    _recipe.animClip = null; // 已合并，清空全局
                }
                EditorGUILayout.HelpBox("多段模式：动画由各段「动画片段」字段配置（见下方「多段动画与表现」）。", MessageType.Info);
            }
            else
            {
                _recipe.animClip = (AnimationClip)EditorGUILayout.ObjectField("AnimationClip", _recipe.animClip, typeof(AnimationClip), false);
                if (_recipe.animClip == null)
                    EditorGUILayout.HelpBox("必须指定 AnimationClip；否则生成的 SkillData 无 animClips，预览无法进入 Clip 播放路径。", MessageType.Warning);
            }
            // State ID 由「段/槽位」自动派生(普攻=连段序号, 技能 Q/W/E/R=1/2/3/4),不手动编辑
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.IntField("State ID (自动)", _recipe.animStateId);
            EditorGUILayout.HelpBox("命名规范: Trigger=Attack/SkillQ~R(无下划线); State=Attack_N/Skill_Q~R(留空自动推导); 段/槽位选择自动匹配 State ID 与 Skill ID。", MessageType.None);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(8);

            // ── ⑥ 行为参数(写入 graphData 节点) ──
            EditorGUILayout.LabelField("⑥ 行为参数(生成 graphData 节点)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("以下数值不直接存 SkillData 顶层,而是写入 ⑥ graphData 的判定/VFX 节点。", MessageType.None);
            EditorGUI.indentLevel++;
            _recipe.damage = EditorGUILayout.FloatField("伤害", _recipe.damage);

            // P1-C1: rangeMeters 所有组合都需要（Melee 射程 / 弹体 maxRange / Chain searchRadius）
            //   唯一例外：Ranged+Instant（恒 AOE，不读 rangeMeters）
            bool showRange = isMelee || !isInstant;
            if (showRange) _recipe.rangeMeters = EditorGUILayout.FloatField("射程 (米)", _recipe.rangeMeters);

            // P1-C2~C4: hitRadius 用作 Melee 胶囊半径 / Projectile width / Curved width / Beam beamWidth / AOE radius
            //   唯一例外：Chain（不读 hitRadius）和 Line targeting（用固定 0.3/0.15）
            bool showRadius = !isLine && _recipe.projectile != SkillProjectile.Chain;
            if (showRadius) _recipe.hitRadius = EditorGUILayout.FloatField("命中半径 (米)", _recipe.hitRadius);

            bool usesAngle = _recipe.targeting == SkillTargeting.Cone && (isMelee || _recipe.projectile == SkillProjectile.Projectile);
            if (usesAngle) _recipe.hitAngle = EditorGUILayout.FloatField("扇形角度 (度)", _recipe.hitAngle);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(8);

            // P2-B2~B5: 弹体参数按子类型过滤显示
            if (_recipe.projectile != SkillProjectile.Instant)
            {
                EditorGUILayout.LabelField("弹体参数", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                // projectileSpeed: Projectile + Curved 使用；Beam/Chain 不读
                if (_recipe.projectile == SkillProjectile.Projectile || _recipe.projectile == SkillProjectile.Curved)
                    _recipe.projectileSpeed = EditorGUILayout.FloatField("飞行速度 (m/s)", _recipe.projectileSpeed);
                // projectileCount: Projectile + Chain 使用；Curved/Beam 不读
                if (_recipe.projectile == SkillProjectile.Projectile || _recipe.projectile == SkillProjectile.Chain)
                    _recipe.projectileCount = EditorGUILayout.IntField(
                        _recipe.projectile == SkillProjectile.Chain ? "弹射次数" : "弹丸数量",
                        _recipe.projectileCount);
                // projectileSpread: 仅 Projectile 使用
                if (_recipe.projectile == SkillProjectile.Projectile)
                    _recipe.projectileSpread = EditorGUILayout.FloatField("扩散角度 (度)", _recipe.projectileSpread);
                // projectileLifetime + Prefab: Projectile + Curved 使用；Beam/Chain 不读
                if (_recipe.projectile == SkillProjectile.Projectile || _recipe.projectile == SkillProjectile.Curved)
                {
                    _recipe.projectileLifetime = EditorGUILayout.FloatField("存活时间 (秒)", _recipe.projectileLifetime);
                    _recipe.projectilePrefab = (GameObject)EditorGUILayout.ObjectField("弹体 Prefab", _recipe.projectilePrefab, typeof(GameObject), false);
                }
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(8);
            }

            if (_recipe.multiHit != SkillMultiHit.Single)
            {
                EditorGUILayout.LabelField("多段参数", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                _recipe.hitCount = Mathf.Clamp(EditorGUILayout.IntField(_recipe.multiHit == SkillMultiHit.Channel ? "Tick 次数" : "段数", _recipe.hitCount), 1, 16);
                _recipe.hitInterval = Mathf.Max(0f, EditorGUILayout.FloatField(_recipe.multiHit == SkillMultiHit.Channel ? "Tick 间隔 (秒)" : "每段间隔 (秒)", _recipe.hitInterval));
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(8);

                // C6: Channel 模式也开放段编辑器（生成器同样读取 stages）
                if (_recipe.multiHit == SkillMultiHit.MultiHit || _recipe.multiHit == SkillMultiHit.Channel)
                    DrawRecipeStageEditor();
            }

            if (_recipe.buff != SkillBuffType.None)
            {
                EditorGUILayout.LabelField("Buff 参数", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                _recipe.buffEffect = (StatusEffect)EditorGUILayout.ObjectField("Buff 效果资产", _recipe.buffEffect, typeof(StatusEffect), false);
                if (_recipe.buffEffect == null)
                    EditorGUILayout.HelpBox("未指定 StatusEffect 资产:生成的 Buff 节点 effect=null,运行时不会生效。", MessageType.Warning);
                // B6: buffDuration/buffValue 为展示参考值，实际由 StatusEffect 资产控制，设为只读
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.FloatField("Buff 持续 (秒)", _recipe.buffDuration);
                    EditorGUILayout.FloatField("Buff 数值", _recipe.buffValue);
                }
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(8);
            }

            // 多段开启时，全局 VFX/SFX 由各段配置接管，隐藏避免重复
            if (_recipe.multiHit != SkillMultiHit.MultiHit)
            {
                EditorGUILayout.LabelField("VFX / SFX", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                _recipe.vfxOnCast = (GameObject)EditorGUILayout.ObjectField("起手 VFX", _recipe.vfxOnCast, typeof(GameObject), false);
                _recipe.vfxOnHit = (GameObject)EditorGUILayout.ObjectField("命中 VFX", _recipe.vfxOnHit, typeof(GameObject), false);
                _recipe.vfxCastBone = DrawStandardDropdown("VFX 挂载点", _recipe.vfxCastBone, StdVfxBones);
                _recipe.sfxOnCast = (AudioClip)EditorGUILayout.ObjectField("起手 SFX", _recipe.sfxOnCast, typeof(AudioClip), false);
                _recipe.sfxOnHit = (AudioClip)EditorGUILayout.ObjectField("命中 SFX", _recipe.sfxOnHit, typeof(AudioClip), false);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(8);
            }
            else
            {
                EditorGUILayout.HelpBox("多段模式：VFX/SFX 由各段独立配置（见上方「多段动画与表现」）。", MessageType.Info);
                EditorGUILayout.Space(4);
            }

            EditorGUILayout.LabelField("动画", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            _recipe.animTrigger = EditorGUILayout.TextField("Animator Trigger", _recipe.animTrigger);
            _recipe.animClipName = EditorGUILayout.TextField("State 名称", _recipe.animClipName);
            // 多段开启时：全局 AnimationClip 合并到第一段，不单独显示
            if (_recipe.multiHit == SkillMultiHit.MultiHit)
            {
                // 自动将全局 animClip 合并到第一段（若第一段未配置）
                if (_recipe.animClip != null && _recipe.stages != null && _recipe.stages.Count > 0
                    && _recipe.stages[0].animClip == null)
                {
                    _recipe.stages[0].animClip = _recipe.animClip;
                    _recipe.animClip = null; // 已合并，清空全局
                }
                EditorGUILayout.HelpBox("多段模式：动画由各段「动画片段」字段配置（见上方「多段动画与表现」）。", MessageType.Info);
            }
            else
            {
                _recipe.animClip = (AnimationClip)EditorGUILayout.ObjectField("AnimationClip", _recipe.animClip, typeof(AnimationClip), false);
                if (_recipe.animClip == null)
                    EditorGUILayout.HelpBox("必须指定 AnimationClip；否则生成的 SkillData 无 animClips，预览无法进入 Clip 播放路径。", MessageType.Warning);
            }
            _recipe.animStateId = EditorGUILayout.IntField("State ID", _recipe.animStateId);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("输出配置", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            _recipe.skillName = EditorGUILayout.TextField("技能名称", _recipe.skillName);
            _recipe.outputFolder = EditorGUILayout.TextField("输出目录 (Assets/...)", _recipe.outputFolder);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(12);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("← 上一步", GUILayout.Height(32))) _step = 1;
            if (GUILayout.Button("下一步 → 预览生成", GUILayout.Height(32))) _step = 3;
            EditorGUILayout.EndHorizontal();
        }

        void DrawRecipeStageEditor()
        {
            if (_recipe.stages == null) _recipe.stages = new List<SkillMultiStageSegment>();
            while (_recipe.stages.Count < _recipe.hitCount)
                _recipe.stages.Add(new SkillMultiStageSegment { stageName = $"第{_recipe.stages.Count + 1}段" });
            while (_recipe.stages.Count > _recipe.hitCount)
                _recipe.stages.RemoveAt(_recipe.stages.Count - 1);

            EditorGUILayout.LabelField("多段动画与表现", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("每段可绑定独立动画、起手/命中特效和音效；生成时会写入 MultiStageLayerData。", MessageType.Info);
            for (int i = 0; i < _recipe.stages.Count; i++)
            {
                var stage = _recipe.stages[i];
                EditorGUILayout.BeginVertical("box");
                stage.stageName = EditorGUILayout.TextField("段名称", stage.stageName);
                stage.animClip = (AnimationClip)EditorGUILayout.ObjectField("动画片段", stage.animClip, typeof(AnimationClip), false);
                stage.animSpeed = EditorGUILayout.Slider("播放速度", stage.animSpeed, 0.1f, 4f);
                stage.vfxOnCast = (GameObject)EditorGUILayout.ObjectField("起手 VFX", stage.vfxOnCast, typeof(GameObject), false);
                stage.sfxOnCast = (AudioClip)EditorGUILayout.ObjectField("起手 SFX", stage.sfxOnCast, typeof(AudioClip), false);
                stage.vfxOnHit = (GameObject)EditorGUILayout.ObjectField("命中 VFX", stage.vfxOnHit, typeof(GameObject), false);
                stage.sfxOnHit = (AudioClip)EditorGUILayout.ObjectField("命中 SFX", stage.sfxOnHit, typeof(AudioClip), false);
                stage.hitAtNormalized = EditorGUILayout.Slider("段内命中比例", stage.hitAtNormalized, 0f, 1f);
                stage.dealsDamage = EditorGUILayout.Toggle("本段出伤", stage.dealsDamage);
                stage.damageMultiplier = EditorGUILayout.FloatField("伤害倍率", stage.damageMultiplier);
                EditorGUILayout.EndVertical();
            }
        }

        void DrawStep3()
        {
            EditorGUILayout.LabelField("配方预览", EditorStyles.boldLabel);
            DrawRecipePreview();
            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("将生成的 graphData 节点", EditorStyles.boldLabel);
            var previewNodes = SkillRecipeBuilder.BuildDataList(_recipe);
            foreach (var n in previewNodes)
            {
                string type = n.GetType().Name;
                EditorGUILayout.LabelField($"  • {type}", EditorStyles.miniLabel);
            }
            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField($"节点数: {previewNodes.Count}", EditorStyles.miniLabel);
            EditorGUILayout.Space(12);

            if (GUILayout.Button("生成 SkillData .asset", GUILayout.Height(40)))
            {
                GenerateSkillData(previewNodes);
            }

            EditorGUILayout.Space(8);
            if (GUILayout.Button("← 上一步", GUILayout.Height(28))) _step = 2;
        }

        // ═══════════════════════════════════════════════════════════════
        // 生成 .asset
        // ═══════════════════════════════════════════════════════════════
        void GenerateSkillData(List<SkillNodeData> nodes)
        {
            string folder = $"Assets/{_recipe.outputFolder}";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                if (!EditorUtility.DisplayDialog("创建目录", $"目录 {folder} 不存在，是否创建？", "创建", "取消"))
                    return;
                Directory.CreateDirectory(folder.Replace("Assets/", Application.dataPath + "/"));
                AssetDatabase.Refresh();
            }
            string assetPath = $"{folder}/{_recipe.skillName}.asset";
            bool isMultiHit = _recipe.multiHit == SkillMultiHit.MultiHit;
            // P0-D4: MultiHit 时 animClip 已合并到 stages[0]，检查首段而非全局
            AnimationClip effectiveClip = isMultiHit
                ? (_recipe.stages != null && _recipe.stages.Count > 0 ? _recipe.stages[0].animClip : null)
                : _recipe.animClip;
            if (effectiveClip == null)
            {
                EditorUtility.DisplayDialog("无法生成技能",
                    isMultiHit
                        ? "多段模式：请在「多段动画与表现」第 1 段指定 AnimationClip。"
                        : "请在第二步指定 AnimationClip。生成无动画引用的 SkillData 会导致预览无法播放。",
                    "OK");
                return;
            }

            var data = ScriptableObject.CreateInstance<SkillData>();
            SkillRecipeBuilder.ApplyToSkillData(data, _recipe);
            // P0-D5: MultiHit 时 animClips 已由 ApplyToSkillData 清空，不再覆写
            if (!isMultiHit && _recipe.animClip != null)
                data.animClips = new[] { _recipe.animClip };
            data.graphData = new System.Collections.Generic.List<SkillNodeData>(nodes);

            // 2026-07-21 修复:判定节点 triggerTime 此前恒为 0(动画首帧即结算)。
            // 统一回写为"前摇结束时刻 + 段内偏移":
            //   单段节点 triggerTime=0 → frontSwingSec
            //   多段第 i 段 triggerTime=i*hitInterval → frontSwingSec + i*hitInterval
            float clipLen = effectiveClip != null ? effectiveClip.length : 1f;
            float frontSwingSec = clipLen * Mathf.Clamp01(_recipe.frontSwing);
            foreach (var n in data.graphData)
            {
                switch (n)
                {
                    case MeleeSwingData m:       m.triggerTime += frontSwingSec; break;
                    case RectShotData s:         s.triggerTime += frontSwingSec; break;
                    case BeamData b:             b.triggerTime += frontSwingSec; break;
                    case ChainBounceData c:      c.triggerTime += frontSwingSec; break;
                    case CurvedProjectileData v: v.triggerTime += frontSwingSec; break;
                    case AOECircularData a:      a.triggerTime += frontSwingSec; break;
                }
            }

            AssetDatabase.CreateAsset(data, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("技能构建完成",
                $"已生成(全 graphData):\n{assetPath}\n节点数: {nodes.Count} (POCO 内嵌,无子资产)\n\n可在 ✏️ 编辑现有 模式打开微调。",
                "OK");
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SkillData>(assetPath);
            EditorGUIUtility.PingObject(data);
        }

        // 方案 B(2026-07-06):从用户自定义模板一键复制生成新技能到当前 Step2 填写的输出目录/名称,
        // 与 GenerateSkillData(Recipe→graphData 组装)是两条独立路径 —— 模板是完整 SkillData 直接深拷贝。
        void GenerateSkillDataFromTemplate(SkillData tpl)
        {
            if (tpl == null) return;
            string folder = $"Assets/{_recipe.outputFolder}";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                if (!EditorUtility.DisplayDialog("创建目录", $"目录 {folder} 不存在，是否创建？", "创建", "取消"))
                    return;
                Directory.CreateDirectory(folder.Replace("Assets/", Application.dataPath + "/"));
                AssetDatabase.Refresh();
            }
            string skillName = string.IsNullOrEmpty(_recipe.skillName) || _recipe.skillName == "New Skill"
                ? tpl.name : _recipe.skillName;
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{skillName}.asset");
            var copy = ScriptableObject.CreateInstance<SkillData>();
            var json = EditorJsonUtility.ToJson(tpl);
            EditorJsonUtility.FromJsonOverwrite(json, copy);
            AssetDatabase.CreateAsset(copy, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("模板已应用",
                $"已从模板『{tpl.name}』生成:\n{assetPath}\n\n可在 ✏️ 编辑现有 模式打开微调。", "OK");
            _editingTarget = AssetDatabase.LoadAssetAtPath<SkillData>(assetPath);
            _mode = Mode.EditExisting;
            Selection.activeObject = _editingTarget;
            EditorGUIUtility.PingObject(copy);
        }

        // ═══════════════════════════════════════════════════════════════
        // 方案 A(2026-07-06):角色技能库总览 —— 选角色 → 浏览/编辑/复制/绑定该角色技能
        // ═══════════════════════════════════════════════════════════════

        // 扫描 Assets/_Game/character/* 下的角色目录(排除 Common,Common 只放模板)
        void ScanCharacterLibrary()
        {
            _charLibScanned = true;
            var names = new List<string>();
            var paths = new List<string>();
            if (AssetDatabase.IsValidFolder(CHARACTER_ROOT))
            {
                var subFolders = AssetDatabase.GetSubFolders(CHARACTER_ROOT);
                foreach (var f in subFolders)
                {
                    string folderName = System.IO.Path.GetFileName(f);
                    if (string.Equals(folderName, "Common", StringComparison.OrdinalIgnoreCase)) continue;
                    names.Add(folderName);
                    paths.Add(f);
                }
            }
            _charLibNames = names.ToArray();
            _charLibPaths = paths.ToArray();
            if (_charLibSelectedIndex >= _charLibNames.Length) _charLibSelectedIndex = -1;
            if (_charLibSelectedIndex >= 0) LoadCharacterSkills(_charLibSelectedIndex);
        }

        // 加载指定角色目录下 SkillData/ 子目录的全部技能资产
        void LoadCharacterSkills(int index)
        {
            _charLibSkills.Clear();
            if (index < 0 || index >= _charLibPaths.Length) return;
            string skillFolder = $"{_charLibPaths[index]}/SkillData";
            if (!AssetDatabase.IsValidFolder(skillFolder)) return;
            var guids = AssetDatabase.FindAssets("t:SkillData", new[] { skillFolder });
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var sd = AssetDatabase.LoadAssetAtPath<SkillData>(p);
                if (sd != null) _charLibSkills.Add(sd);
            }
            _charLibSkills.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        }

        // 在该角色目录下查找第一个含 Animator 的 Prefab(供预览面板自动绑定)
        GameObject FindCharacterPrefabInFolder(string charFolder)
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { charFolder });
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go != null && go.GetComponentInChildren<Animator>(true) != null)
                    return go;
            }
            return null;
        }

        void DrawCharacterLibrary()
        {
            EditorGUILayout.LabelField("🧑 角色技能库", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                $"扫描 {CHARACTER_ROOT}/* 目录(排除 Common),选中角色后列出其 SkillData/ 下全部技能。\n" +
                "点技能名直接进入编辑;右键可复制到其他角色 / 绑定到该角色 Prefab 的 SkillSlots。",
                MessageType.None);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("🔄 重新扫描", GUILayout.Width(100), GUILayout.Height(22)))
                ScanCharacterLibrary();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6);

            if (_charLibNames.Length == 0)
            {
                EditorGUILayout.HelpBox($"未在 {CHARACTER_ROOT} 下找到任何角色目录。", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("选择角色", EditorStyles.miniBoldLabel);
            int newSel = GUILayout.SelectionGrid(_charLibSelectedIndex, _charLibNames,
                Mathf.Max(1, Mathf.Min(4, _charLibNames.Length)), "Button", GUILayout.Height(26));
            if (newSel != _charLibSelectedIndex)
            {
                _charLibSelectedIndex = newSel;
                LoadCharacterSkills(newSel);
                // 自动把预览角色切到当前选中角色(复用 AutoFindCharacterPrefab 同一套查找逻辑)
                var found = FindCharacterPrefabInFolder(_charLibPaths[newSel]);
                if (found != null)
                {
                    _previewPrefab = found;
                    // V3.1.7:切换角色时重置武器槽,让 AutoSyncWeaponsFromPrefab 重新从新角色读默认武器
                    _previewMainHandWeapon = null;
                    _previewOffHandWeapon  = null;
                    AutoSyncWeaponsFromPrefab(_previewPrefab);
                    RebuildPreviewInstance();
                }
            }
            EditorGUILayout.Space(8);

            if (_charLibSelectedIndex < 0) return;

            EditorGUILayout.LabelField($"『{_charLibNames[_charLibSelectedIndex]}』的技能库 ({_charLibSkills.Count})", EditorStyles.boldLabel);
            _charLibScroll = EditorGUILayout.BeginScrollView(_charLibScroll, GUILayout.MaxHeight(320));
            try
            {
            foreach (var sd in _charLibSkills)
            {
                if (sd == null) continue;
                EditorGUILayout.BeginHorizontal("box");
                string catIcon = sd.category == SkillCategory.BasicAttack ? "⚔️" : "✨";
                if (GUILayout.Button($"{catIcon} {sd.name}", EditorStyles.label, GUILayout.Height(22)))
                {
                    _editingTarget = sd;
                    _mode = Mode.EditExisting;
                    _scrollX = 0f;
                    _multiSelectedIndices.Clear();
                    EditorApplication.delayCall += FitTimelineToContent;
                    ChangePreviewTarget(sd);
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("⎘ 复制到...", GUILayout.Width(90), GUILayout.Height(20)))
                    ShowCopySkillToCharacterMenu(sd);
                if (GUILayout.Button("🔗 绑定槽位", GUILayout.Width(90), GUILayout.Height(20)))
                    BindSkillToSlots(sd, _charLibPaths[_charLibSelectedIndex]);
                EditorGUILayout.EndHorizontal();
            }
            }
            finally { EditorGUILayout.EndScrollView(); }

            if (_charLibSkills.Count == 0)
            {
                EditorGUILayout.HelpBox("该角色暂无技能资产,可在『新建技能』模式生成后输出到该角色 SkillData/ 目录。", MessageType.Info);
            }
        }

        // 右键菜单:把技能复制(深拷贝 + 另存为)到其他角色目录
        void ShowCopySkillToCharacterMenu(SkillData src)
        {
            var menu = new GenericMenu();
            for (int i = 0; i < _charLibNames.Length; i++)
            {
                int idx = i;
                if (idx == _charLibSelectedIndex)
                {
                    menu.AddDisabledItem(new GUIContent(_charLibNames[idx] + " (当前角色)"));
                    continue;
                }
                menu.AddItem(new GUIContent(_charLibNames[idx]), false, () => CopySkillToCharacter(src, _charLibPaths[idx]));
            }
            if (_charLibNames.Length == 0) menu.AddDisabledItem(new GUIContent("(无其他角色)"));
            menu.ShowAsContext();
        }

        // 深拷贝技能资产到目标角色的 SkillData/ 目录(用 EditorJsonUtility 保留 [SerializeReference] graphData)
        void CopySkillToCharacter(SkillData src, string targetCharFolder)
        {
            if (src == null) return;
            string skillFolder = $"{targetCharFolder}/SkillData";
            if (!AssetDatabase.IsValidFolder(skillFolder))
                AssetDatabase.CreateFolder(targetCharFolder, "SkillData");

            string baseName = src.name;
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{skillFolder}/{baseName}.asset");
            var copy = ScriptableObject.CreateInstance<SkillData>();
            var json = EditorJsonUtility.ToJson(src);
            EditorJsonUtility.FromJsonOverwrite(json, copy);
            AssetDatabase.CreateAsset(copy, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("复制完成", $"已复制到:\n{assetPath}", "OK");
            if (_charLibSelectedIndex >= 0) LoadCharacterSkills(_charLibSelectedIndex);
            EditorGUIUtility.PingObject(copy);
        }

        // 把技能绑定到目标角色 Prefab 的 SkillController.skillSlots / attackSlots
        // (追加到第一个空槽;已存在同名技能则跳过,避免重复)
        void BindSkillToSlots(SkillData sd, string charFolder)
        {
            if (sd == null) return;
            var prefab = FindCharacterPrefabInFolder(charFolder);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("绑定失败", $"在 {charFolder} 下未找到含 Animator 的 Prefab。", "OK");
                return;
            }
            var prefabPath = AssetDatabase.GetAssetPath(prefab);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var sc = instance.GetComponent<SkillController>();
            if (sc == null)
            {
                UnityEngine.Object.DestroyImmediate(instance);
                EditorUtility.DisplayDialog("绑定失败", "该 Prefab 没有 SkillController 组件。", "OK");
                return;
            }

            bool isAttack = sd.category == SkillCategory.BasicAttack;
            var slots = isAttack ? sc.attackSlots : sc.skillSlots;
            if (slots == null) slots = new SkillData[8];

            bool already = false;
            int emptyIdx = -1;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == sd) { already = true; break; }
                if (slots[i] == null && emptyIdx < 0) emptyIdx = i;
            }

            if (already)
            {
                UnityEngine.Object.DestroyImmediate(instance);
                EditorUtility.DisplayDialog("已绑定", $"『{sd.name}』已在 {(isAttack ? "attackSlots" : "skillSlots")} 中。", "OK");
                return;
            }
            if (emptyIdx < 0)
            {
                UnityEngine.Object.DestroyImmediate(instance);
                EditorUtility.DisplayDialog("绑定失败", $"{(isAttack ? "attackSlots" : "skillSlots")} 已满({slots.Length}/{slots.Length}),请先在 Inspector 里腾出空位。", "OK");
                return;
            }

            slots[emptyIdx] = sd;
            if (isAttack) sc.attackSlots = slots; else sc.skillSlots = slots;

            PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            UnityEngine.Object.DestroyImmediate(instance);
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("绑定完成",
                $"『{sd.name}』已绑定到 {(isAttack ? "attackSlots" : "skillSlots")}[{emptyIdx}]。", "OK");
        }

        // ═══════════════════════════════════════════════════════════════
        // 方案 B(2026-07-06):模板体系落地 —— 另存为模板 / 另存为副本
        // ═══════════════════════════════════════════════════════════════
        const string TEMPLATE_FOLDER = "Assets/_Game/character/Common/SkillTemplates";

        // 另存为模板:深拷贝当前技能写入 Common/SkillTemplates/,供 Step1 预设库扫描复用给任意角色
        void SaveAsTemplate(SkillData src)
        {
            if (src == null) return;
            string input = EditorInputDialog.Show("另存为模板", "模板名称:", src.name + "_Template");
            if (string.IsNullOrEmpty(input)) return;

            if (!AssetDatabase.IsValidFolder(TEMPLATE_FOLDER))
            {
                // 逐级创建(Common 目录已存在,只需建 SkillTemplates)
                if (!AssetDatabase.IsValidFolder("Assets/_Game/character/Common"))
                    AssetDatabase.CreateFolder("Assets/_Game/character", "Common");
                AssetDatabase.CreateFolder("Assets/_Game/character/Common", "SkillTemplates");
            }

            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{TEMPLATE_FOLDER}/{input}.asset");
            var copy = ScriptableObject.CreateInstance<SkillData>();
            var json = EditorJsonUtility.ToJson(src);
            EditorJsonUtility.FromJsonOverwrite(json, copy);
            AssetDatabase.CreateAsset(copy, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("模板已保存",
                $"已保存为模板:\n{assetPath}\n\n可在『新建技能』Step1 的预设库中看到(自定义模板分组)。", "OK");
            EditorGUIUtility.PingObject(copy);
        }

        // 另存为副本:深拷贝到同目录(不覆盖当前 .asset),避免团队协作误改共享技能资产
        void SaveAsCopy(SkillData src)
        {
            if (src == null) return;
            string srcPath = AssetDatabase.GetAssetPath(src);
            string dir = string.IsNullOrEmpty(srcPath) ? "Assets" : System.IO.Path.GetDirectoryName(srcPath).Replace('\\', '/');
            string input = EditorInputDialog.Show("另存为副本", "副本名称:", src.name + "_Copy");
            if (string.IsNullOrEmpty(input)) return;

            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{input}.asset");
            var copy = ScriptableObject.CreateInstance<SkillData>();
            var json = EditorJsonUtility.ToJson(src);
            EditorJsonUtility.FromJsonOverwrite(json, copy);
            AssetDatabase.CreateAsset(copy, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("副本已保存", $"已保存副本:\n{assetPath}", "OK");
            _editingTarget = AssetDatabase.LoadAssetAtPath<SkillData>(assetPath);
            EditorGUIUtility.PingObject(copy);
        }

        // 扫描 Common/SkillTemplates/ 下的用户自定义模板(供 Step1 展示,区别于内置 SkillPresets)
        static List<SkillData> ScanUserTemplates()
        {
            var result = new List<SkillData>();
            if (!AssetDatabase.IsValidFolder(TEMPLATE_FOLDER)) return result;
            var guids = AssetDatabase.FindAssets("t:SkillData", new[] { TEMPLATE_FOLDER });
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var sd = AssetDatabase.LoadAssetAtPath<SkillData>(p);
                if (sd != null) result.Add(sd);
            }
            result.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        // ═══════════════════════════════════════════════════════════════
        // 编辑现有(原 DrawEditExisting)
        // ═══════════════════════════════════════════════════════════════
        void DrawEditExisting()
        {
            EditorGUILayout.BeginHorizontal();
            var prevTarget = _editingTarget;
            var displayedTarget = _editingTarget;
            displayedTarget = (SkillData)EditorGUILayout.ObjectField("目标 SkillData", displayedTarget, typeof(SkillData), false);
            _editingTarget = displayedTarget;
            if (GUILayout.Button("Load Selected", GUILayout.Width(110)))
            {
                if (Selection.activeObject is SkillData sel) _editingTarget = sel;
            }
            EditorGUILayout.EndHorizontal();

            if (_editingTarget == null) return;
            if (_workingCopy != null && _workingCopy.Working != null && _workingCopy.Source != _editingTarget)
                _workingCopy = new WorkingCopySkillData(_editingTarget);

            // 方案 B(2026-07-06):另存为模板 / 另存为副本 —— 避免直接改共享技能资产,
            // 也让技能配方能沉淀到 Common/SkillTemplates/ 供 Step1 预设库扫描复用。
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("💾 另存为模板", GUILayout.Height(22)))
                SaveAsTemplate(_editingTarget);
            if (GUILayout.Button("📄 另存为副本", GUILayout.Height(22)))
                SaveAsCopy(_editingTarget);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);

            // 切换技能时自动 Fit All,避免沿用上一个技能的缩放/滚动
            if (_editingTarget != prevTarget)
            {
                _scrollX = 0f;
                _multiSelectedIndices.Clear();
                EditorApplication.delayCall += FitTimelineToContent;
                // V3.1.3 修复:编辑现有模式切换目标时,自动同步预览目标
                // 这样预览窗口的 _previewTarget / _workingCopy 会跟着走,
                // Timeline 节点和预览数据一致
                ChangePreviewTarget(_editingTarget);
            }
            var parameterTarget = _workingCopy != null && _workingCopy.Working != null
                ? _workingCopy.Working : _editingTarget;
            if (parameterTarget == null) return;
            if (_editingSO == null || _editingSO.targetObject != parameterTarget)
            {
                _editingSO?.Dispose();
                _editingSO = new SerializedObject(parameterTarget);
            }

            _editingSO.Update();

            EditorGUI.BeginChangeCheck();
            // ── ① 基础信息 ──
            EditorGUILayout.LabelField("① 基础信息", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_editingSO.FindProperty("skillName"));
            EditorGUILayout.PropertyField(_editingSO.FindProperty("skillId"));
            EditorGUILayout.PropertyField(_editingSO.FindProperty("icon"));
            var newCategory = (SkillCategory)EditorGUILayout.EnumPopup("category", parameterTarget.category);
            if (newCategory != parameterTarget.category)
            {
                parameterTarget.category = newCategory;
                EditorUtility.SetDirty(parameterTarget);
                RefreshEditingSO();
            }
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);

            // ── ② 时间参数 ──
            EditorGUILayout.LabelField("② 时间参数(秒)", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_editingSO.FindProperty("cooldown"));
            EditorGUILayout.PropertyField(_editingSO.FindProperty("castTime"));
            EditorGUILayout.PropertyField(_editingSO.FindProperty("channelDuration"));
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);

            // ── ③ 动画窗口 ──
            EditorGUILayout.LabelField("③ 动画窗口(归一化 0~1)", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_editingSO.FindProperty("frontSwing"));
            EditorGUILayout.PropertyField(_editingSO.FindProperty("backSwing"));
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);

            // ── ④ 动画融合 ──
            EditorGUILayout.LabelField("④ 动画融合(秒)", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_editingSO.FindProperty("fadeInDuration"));
            EditorGUILayout.PropertyField(_editingSO.FindProperty("fadeOutDuration"));
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);

            // ── ⑤ 动画身份 ──
            EditorGUILayout.LabelField("⑤ 动画身份", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_editingSO.FindProperty("animTrigger"));
            EditorGUILayout.PropertyField(_editingSO.FindProperty("animStateId"));
            EditorGUILayout.PropertyField(_editingSO.FindProperty("animClipName"));
            EditorGUILayout.PropertyField(_editingSO.FindProperty("animClips"), true);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);

            // ── ⑤.1 多段配置 ──
            EditorGUILayout.PropertyField(_editingSO.FindProperty("multiStage"), true);

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(_editingTarget);
                // V3.1.6:参数修改后标记脏,下一帧 OnEditorUpdate 同步到预览
                if (_editingTarget == _previewTarget && _workingCopy != null)
                {
                    _workingCopy.SnapshotForUndo();
                    _paramsDirty = true;
                }
            }

            EditorGUILayout.Space(6);
            DrawGraphDataEditor();

            EditorGUILayout.Space(8);
            if (GUILayout.Button(new GUIContent(" 保存 (Ctrl+S)", IconSave.image), GUILayout.Height(28)))
            {
                _editingSO.ApplyModifiedProperties();
                // P3 修复:参数面板编辑落在 Working,必须 Confirm→Source 才能真正落盘。
                //   旧代码直接 SaveAssetIfDirty(_editingTarget),但 Source 没有 Working 的改动。
                _workingCopy?.ConfirmWithoutSave();
                EditorUtility.SetDirty(_editingTarget);
                AssetDatabase.SaveAssetIfDirty(_editingTarget);
                Debug.Log($"[SkillBuilder] 已保存 {_editingTarget.name}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // graphData 节点编辑面板(原 DrawGraphDataEditor)
        // ═══════════════════════════════════════════════════════════════
        void DrawGraphDataEditor()
        {
            var parameterTarget = _workingCopy != null && _workingCopy.Working != null
                ? _workingCopy.Working : _editingTarget;
            if (parameterTarget == null) return;
            if (_editingSO == null || _editingSO.targetObject != parameterTarget)
            {
                _editingSO?.Dispose();
                _editingSO = new SerializedObject(parameterTarget);
            }
            _editingSO.Update();
            var graphProp = _editingSO.FindProperty("graphData");
            if (graphProp == null) return;
            EditorGUILayout.LabelField($"graphData 节点 ({graphProp.arraySize}) — 触发时刻=动画进度 0~1", EditorStyles.boldLabel);

            // 嵌套 ScrollView 会导致滚动异常（外层已有 ScrollView），直接在外层容器中展开
            // (2026-07-28,test07 对齐:修"graphData 节点面板显示不全")
            try
            {

            for (int i = 0; i < graphProp.arraySize; i++)
            {
                var elem = graphProp.GetArrayElementAtIndex(i);
                var typeName = elem.managedReferenceFullTypename;
                string display = string.IsNullOrEmpty(typeName) ? "<未指定类型>" :
                    typeName.Substring(typeName.LastIndexOf('.') + 1);
                bool expanded = (_expandedNodeIndex == i);

                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal();
                bool newExpanded = EditorGUILayout.Foldout(expanded,
                    $"#{i}  {display}",
                    true);
                if (newExpanded != expanded) _expandedNodeIndex = newExpanded ? i : -1;

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("↑", GUILayout.Width(24)) && i > 0)
                {
                    // P0 延迟:MoveArrayElement 等到下帧 OnGUI 入口再执行
                    _pendingNodeMoveFrom = i | (-1 << 16);  // direction = -1 (上移)
                }
                if (GUILayout.Button("↓", GUILayout.Width(24)) && i < graphProp.arraySize - 1)
                {
                    _pendingNodeMoveFrom = i | (1 << 16);   // direction = +1 (下移)
                }
                if (GUILayout.Button("✕", GUILayout.Width(24)))
                {
                    // P0 延迟:DeleteArrayElementAtIndex 等到下帧 OnGUI 入口再执行
                    _pendingNodeDeletionIndex = i;
                }
                EditorGUILayout.EndHorizontal();

                if (expanded)
                {
                    EditorGUI.indentLevel++;

                    // P1-2:先遍历收集各 Section 字段列表(路径拷贝),再按组渲染 Foldout
                    // 注意:SerializedProperty 是 class,Copy() 产生独立路径,不影响原 elem
                    var endProp   = elem.GetEndProperty();
                    var sectionFields = new Dictionary<string, List<SerializedProperty>>();
                    var sectionOrder  = new List<string>(); // 保持首次出现顺序
                    {
                        var scan = elem.Copy();
                        bool first = true;
                        while (scan.NextVisible(first) && !SerializedProperty.EqualContents(scan, endProp))
                        {
                            first = false;
                            if (scan.name == "editorTrackRow") continue;
                            string sec = GetFieldSection(scan.name);
                            if (!sectionFields.ContainsKey(sec))
                            {
                                sectionFields[sec] = new List<SerializedProperty>();
                                sectionOrder.Add(sec);
                            }
                            sectionFields[sec].Add(scan.Copy());
                        }
                    }

                    // ── 参数区→时间轴同步:从 SerializedProperty 显示,修改后自动触发 Repaint ──
                    EditorGUI.BeginChangeCheck();
                    foreach (var sec in sectionOrder)
                    {
                        var fields = sectionFields[sec];
                        bool secOpen = GetSectionFoldout(i, sec, true);
                        bool newSecOpen = EditorGUILayout.Foldout(secOpen, sec, true, EditorStyles.foldoutHeader);
                        if (newSecOpen != secOpen) SetSectionFoldout(i, sec, newSecOpen);
                        if (!newSecOpen) continue;

                        EditorGUI.indentLevel++;
                        foreach (var child in fields)
                        {
                            if (child.name == "editorDuration")
                            {
                                bool isDragTarget = _isDraggingClip && i == _dragNodeIndex
                                                    && (_dragKind == TimelineHitKind.ResizeLeft || _dragKind == TimelineHitKind.ResizeRight);
                                float displayDur = isDragTarget ? _tempDuration : child.floatValue;
                                EditorGUI.BeginDisabledGroup(isDragTarget);
                                float newDur = EditorGUILayout.FloatField("持续时长(s)", displayDur);
                                EditorGUI.EndDisabledGroup();
                                if (!isDragTarget && !Mathf.Approximately(newDur, child.floatValue))
                                {
                                    Undo.RecordObject(parameterTarget, "修改节点时长");
                                    child.floatValue = newDur;
                                    _tempDuration    = newDur;
                                }
                                continue;
                            }
                            if (child.name == "triggerTime")
                            {
                                bool isDragTrigger = _isDraggingClip && i == _dragNodeIndex
                                                     && (_dragKind == TimelineHitKind.Move || _dragKind == TimelineHitKind.ResizeLeft);
                                float displayTT = isDragTrigger ? _tempTriggerTime : child.floatValue;
                                EditorGUI.BeginDisabledGroup(isDragTrigger);
                                float newTT = EditorGUILayout.FloatField("触发时刻(s)", displayTT);
                                EditorGUI.EndDisabledGroup();
                                if (!isDragTrigger && !Mathf.Approximately(newTT, child.floatValue))
                                {
                                    Undo.RecordObject(parameterTarget, "修改触发时刻");
                                    child.floatValue = newTT;
                                    _tempTriggerTime = newTT;
                                }
                                continue;
                            }
                            EditorGUILayout.PropertyField(child, true);
                        }
                        EditorGUI.indentLevel--;
                    }
                    if (EditorGUI.EndChangeCheck())
                    {
                        _editingSO?.ApplyModifiedProperties();
                        EditorUtility.SetDirty(parameterTarget);
                        // 参数面板始终编辑当前数据源；编辑现有模式下当前数据源是 WorkingCopy，
                        // 不能再用 _editingTarget == _previewTarget 判断，否则添加节点后 _editingTarget
                        // 已切到 Working、条件恒 false，参数修改不会同步预览/撤销链。
                        if (_workingCopy != null)
                        {
                            MarkWorkingDirty();
                            _previewRuntime?.Init(_previewInstance, _workingCopy);
                        }
                        Repaint();
                    }

                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndVertical();
            }

            }
            finally { }

            // 方案 D(2026-07-06):统一节点添加入口 —— 与 Layers 头 "+" 按钮共用同一个
            // ShowAddLayerMenu(行为节点/动画片段层/多段层 三级菜单),不再维护并行的
            // AddNodeButton 矩阵(旧矩阵只有 12 个行为节点类型,漏了状态效果/动画片段/多段)。
            if (GUILayout.Button("+ 添加节点 ▾", GUILayout.Height(26)))
            {
                ShowAddLayerMenu();
            }
        }

        // ── graphData 顺序/删除操作统一提交(P0-1 修复) ──
        // ↑/↓/✕ 按钮修改 graphProp 后未 Apply,下一帧 Update() 覆盖导致改动丢失。
        void ApplyGraphOrderChange()
        {
            _editingSO?.ApplyModifiedProperties();
            EditorUtility.SetDirty(_editingTarget);
            if (_editingTarget == _previewTarget && _workingCopy != null)
            {
                _workingCopy.SnapshotForUndo();
                _paramsDirty = true;
            }
            Repaint();
        }

        // ── Layers 列顶部 + 按钮弹的菜单(三选一:节点 / 动画片段 / 多段)──
        // 与 graphData 走同一条数据流,只是把 14 个 SkillNodeData 子类重新组织成子菜单
        void ShowAddLayerMenu()
        {
            if (_editingTarget == null) return;
            var menu = new GenericMenu();

            // ── ① 行为节点(14 种) — 拆成类目子菜单,跟 Step 面板保持一致
            menu.AddItem(new GUIContent("行为节点/起手 VFX"),     false, () => AddLayerToGraph<CastVFXData>());
            menu.AddItem(new GUIContent("行为节点/中段 VFX"),     false, () => AddLayerToGraph<MidVFXData>());
            menu.AddItem(new GUIContent("行为节点/命中 VFX"),     false, () => AddLayerToGraph<HitVFXData>());
            menu.AddSeparator("行为节点/");
            menu.AddItem(new GUIContent("行为节点/近战挥击"),     false, () => AddLayerToGraph<MeleeSwingData>());
            menu.AddSeparator("行为节点/");
            menu.AddItem(new GUIContent("行为节点/矩形弹幕"),     false, () => AddLayerToGraph<RectShotData>());
            menu.AddItem(new GUIContent("行为节点/光束"),         false, () => AddLayerToGraph<BeamData>());
            menu.AddItem(new GUIContent("行为节点/链式弹射"),     false, () => AddLayerToGraph<ChainBounceData>());
            menu.AddItem(new GUIContent("行为节点/抛物线弹体"),   false, () => AddLayerToGraph<CurvedProjectileData>());
            menu.AddSeparator("行为节点/");
            menu.AddItem(new GUIContent("行为节点/圆形 AOE"),     false, () => AddLayerToGraph<AOECircularData>());
            menu.AddItem(new GUIContent("行为节点/陷阱"),         false, () => AddLayerToGraph<TrapData>());
            menu.AddItem(new GUIContent("行为节点/墙体"),         false, () => AddLayerToGraph<WallData>());
            menu.AddItem(new GUIContent("行为节点/召唤"),         false, () => AddLayerToGraph<SummonData>());
            menu.AddSeparator("行为节点/");
            menu.AddItem(new GUIContent("行为节点/持续施法"),     false, () => AddLayerToGraph<ChanneledData>());
            menu.AddSeparator("行为节点/");
            menu.AddItem(new GUIContent("行为节点/移动策略"),     false, () => AddLayerToGraph<MovementData>());
            menu.AddSeparator("行为节点/");
            menu.AddItem(new GUIContent("行为节点/状态效果"),     false, () => AddLayerToGraph<StatusEffectData>());

            // ── ② 动画片段层(对应 SkillData.animClips[i])─────────────
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("动画片段层/动画片段"),  false, () => AddLayerToGraph<AnimClipLayerData>());

            // ── ③ 多段层(对应 SkillData.multiStage.segments[i])────────
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("多段层/多段配置"),      false, () => AddLayerToGraph<MultiStageLayerData>());

            menu.ShowAsContext();
        }

    }
}
#endif
