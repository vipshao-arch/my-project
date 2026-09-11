#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System;
using System.IO;
using System.Collections.Generic;
using Game.SkillSystem;

namespace Game.SkillSystem.EditorTools
{
    public partial class SkillBuilderWizard : EditorWindow
    {
        // ═══════════════════════════════════════════════════════════════
        // 左:Preview 预览窗口
        // ═══════════════════════════════════════════════════════════════
        void DrawPreviewPanel(Rect area)
        {
            EditorGUI.DrawRect(area, new Color(0.13f, 0.13f, 0.15f));
            // 标题条
            var titleRect = new Rect(area.x, area.y, area.width, 20f);
            EditorGUI.DrawRect(titleRect, UI.BgHeader);
            GUI.Label(new Rect(titleRect.x + 6, titleRect.y + 2, 100, 16), "预览", UI.TitleStyle());

            // 刷新按钮:完整重建预览实例 + 运行时 + 自动播放
            var refreshRect = new Rect(area.xMax - 48, titleRect.y + 1, 44, 17);
            var c0 = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.35f, 0.55f, 0.8f, 0.7f);
            if (GUI.Button(refreshRect, "刷新", EditorStyles.miniButton))
            {
                RefreshPreview();
            }
            GUI.backgroundColor = c0;

            // 控件区
            float y = area.y + 22f;
            float L = area.x + 4;   // 左内边距
            float W = area.width - 8; // 可用宽度

            // ── V3 P1:独立预览 SkillData 槽(双轨:可与 _editingTarget 不同)──
            GUI.Label(new Rect(L, y, 70, 18), "预览数据:", EditorStyles.miniLabel);
            var newTarget = (SkillData)EditorGUI.ObjectField(
                new Rect(L + 72, y, W - 72 - 20, 18),
                _previewTarget, typeof(SkillData), false);
            // 🔄 重新从 .asset 拷贝(丢弃 working)
            if (GUI.Button(new Rect(area.xMax - 20, y, 18, 18), "🔄", EditorStyles.miniButton))
            {
                if (_previewTarget != null)
                {
                    _workingCopy?.Dispose();
                    _workingCopy = new WorkingCopySkillData(_previewTarget);
                    _previewRuntime?.Init(_previewInstance, _workingCopy);
                    _previewRuntime?.Reset();
                    _lastKnownMtime = TryReadMtime(_previewTargetPath);
                    _sourceMtimeDirty = false;
                }
            }
            if (newTarget != _previewTarget) ChangePreviewTarget(newTarget);
            EnsurePreviewPrefabMatchesSkill();
            y += 20f;

            // 状态指示:Working vs Source(✓同步 / ✗ 有改动)
            if (_previewTarget != null)
            {
                string state = _workingCopy != null && _workingCopy.IsDirty ? "✗ Working 有未确认改动" : "✓ Working = Source";
                var c = GUI.color;
                GUI.color = _workingCopy != null && _workingCopy.IsDirty ? new Color(1f, 0.7f, 0.3f) : new Color(0.6f, 1f, 0.6f);
                GUI.Label(new Rect(L, y, W, 16), state, EditorStyles.miniLabel);
                GUI.color = c;
                y += 16f;
            }

            // mtime 警告条
            if (_sourceMtimeDirty)
            {
                var c = GUI.color;
                GUI.color = new Color(1f, 0.85f, 0.3f);
                var warnRect = new Rect(L, y, W, 18);
                EditorGUI.DrawRect(warnRect, new Color(0.4f, 0.35f, 0.1f, 0.6f));
                GUI.Label(warnRect, " ⚠ 源 .asset 已被外部修改,点 🔄 重新加载", EditorStyles.miniLabel);
                GUI.color = c;
                y += 20f;
            }

            // P1-2:预览异常提示(Animator Play/SetTrigger 失败等)
            if (!string.IsNullOrEmpty(_previewLastError))
            {
                var c = GUI.color;
                GUI.color = new Color(1f, 0.45f, 0.35f);
                var errRect = new Rect(L, y, W, 18);
                EditorGUI.DrawRect(errRect, new Color(0.4f, 0.15f, 0.1f, 0.6f));
                GUI.Label(errRect, $" ⚠ {_previewLastError}", EditorStyles.miniLabel);
                GUI.color = c;
                y += 20f;
            }

            // 角色 Prefab 槽
            DrawPreviewObjectRow(area, L, W, ref y, ref _previewPrefab, "角色 Prefab:", () =>
            {
                RebuildPreviewInstance();
                _previewRuntime?.Init(_previewInstance, _workingCopy);
            });
            if (_previewPrefab != null)
            {
                string previewPath = AssetDatabase.GetAssetPath(_previewPrefab);
                string clipPath = _previewClip != null ? AssetDatabase.GetAssetPath(_previewClip) : "(未选中 Clip)";
                GUI.Label(new Rect(L + 72, y - 2f, W - 72, 16),
                    $"角色: {previewPath} | Clip: {clipPath}", EditorStyles.miniLabel);
                y += 16f;
            }

            // V3:武器挂载(主手 / 副手)—— 拖入 WeaponData 后实时挂到预览实例上
            DrawPreviewObjectRow(area, L, W, ref y, ref _previewMainHandWeapon, "主手武器:", ReapplyPreviewWeapons);
            DrawPreviewObjectRow(area, L, W, ref y, ref _previewOffHandWeapon, "副手武器:", ReapplyPreviewWeapons);

            // ── 修复 6:联动 Timeline 播放头开关 ─────────────────────
            _previewLinkTimeline = GUI.Toggle(new Rect(L, y, W, 16),
                _previewLinkTimeline, " 🔗 联动 Timeline 播放头(按当前技能演出预览)", EditorStyles.miniLabel);
            y += 18f;

            if (_previewLinkTimeline)
            {
                AutoSelectClipForPlayhead();
                _previewTime = _previewClip != null ? Mathf.Clamp(_playheadT - _previewClipStartT, 0f, _previewClip.length) : 0f;
                // P2-2 修复:联动模式下仅非循环时才在 clip 末尾自动停止。
                // 旧版无条件 _previewPlaying=false 在循环模式下形成死锁:
                //   playhead 走到 clip.length → _previewPlaying=false → 卡住
                //   → 再按 Play → playhead 仍在 clip.length → 立即再停 → 永不可播。
                // 循环模式让 AdvancePreviewTime 统一管理 loop/stop 逻辑。
                if (_previewPlaying && !_previewLoopMode && _previewClip != null && _previewTime >= _previewClip.length - 0.001f)
                {
                    _previewPlaying = false;
                }
                GUI.Label(new Rect(L, y, W, 18),
                    _previewClip != null
                        ? $"▶ 播放头 {_playheadT:0.00}s → {_previewClip.name} @ {_previewTime:0.00}s"
                        : $"▶ 播放头 {_playheadT:0.00}s（当前无匹配动画片段）",
                    EditorStyles.miniLabel);
                y += 20f;
                if (!string.IsNullOrEmpty(_previewAnimStateMissing))
                {
                    string resolvedPart = string.IsNullOrEmpty(_previewAnimResolvedState) ? "(回退失败)" : $"→ 已回退到 `{_previewAnimResolvedState}`";
                    GUI.Label(new Rect(L, y, W, 18),
                        $"⚠ Animator 状态 `{_previewAnimStateMissing}` 在 controller 中未找到 {resolvedPart}",
                        s_warnStyle);
                    y += 18f;
                }
            }
            else
            {
                GUI.Label(new Rect(L, y, 60, 18), "动画:", EditorStyles.miniLabel);
                RefreshPreviewClipCache();
                string[] clipNames = new string[_previewClipCache.Count + 1];
                clipNames[0] = "(无)";
                for (int i = 0; i < _previewClipCache.Count; i++) clipNames[i + 1] = _previewClipCache[i] != null ? _previewClipCache[i].name : "<null>";
                int curIdx = _previewClip == null ? 0 : _previewClipCache.IndexOf(_previewClip) + 1;
                int newIdx = EditorGUI.Popup(new Rect(L + 60, y, W - 60, 18), curIdx, clipNames);
                if (newIdx != curIdx)
                {
                    _previewClip = newIdx <= 0 ? null : _previewClipCache[newIdx - 1];
                    _previewTime = 0f;
                    _previewPlaying = false;
                }
                y += 22f;
            }

            // ── 显示开关 + 灯光调整(2026-07-28,test07 对齐) ─────────────
            EditorGUI.BeginChangeCheck();
            _showHitRange = GUI.Toggle(new Rect(L, y, W * 0.5f, 16),
                _showHitRange, " ◎ 伤害范围", EditorStyles.miniLabel);
            _showRefAxes = GUI.Toggle(new Rect(L + W * 0.5f, y, W * 0.5f, 16),
                _showRefAxes, " ✛ 坐标轴", EditorStyles.miniLabel);
            y += 18f;
            GUI.Label(new Rect(L, y, 30, 16), "灯光", EditorStyles.miniLabel);
            _previewLightIntensity = EditorGUI.Slider(new Rect(L + 30, y, W - 30, 14), _previewLightIntensity, 0f, 2f);
            y += 16f;
            GUI.Label(new Rect(L, y, 30, 16), "方向", EditorStyles.miniLabel);
            float halfW = (W - 36) * 0.5f;
            _previewLightAzimuth = EditorGUI.Slider(new Rect(L + 30, y, halfW, 14), _previewLightAzimuth, -180f, 180f);
            _previewLightElevation = EditorGUI.Slider(new Rect(L + 36 + halfW, y, halfW, 14), _previewLightElevation, 0f, 90f);
            y += 20f;
            if (EditorGUI.EndChangeCheck()) SavePreviewDisplayPrefs();

            // ── 受击目标(2026-08-03):可替换靶子 prefab + 数量/距离/扇形角 ──
            EditorGUI.BeginChangeCheck();
            GUI.Label(new Rect(L, y, 56, 16), "受击目标", EditorStyles.miniLabel);
            _previewTargetPrefab = (GameObject)EditorGUI.ObjectField(
                new Rect(L + 58, y, W - 58, 16), _previewTargetPrefab, typeof(GameObject), false);
            y += 18f;
            GUI.Label(new Rect(L, y, 30, 16), "数量", EditorStyles.miniLabel);
            _previewTargetCount = EditorGUI.IntSlider(new Rect(L + 30, y, halfW, 14), _previewTargetCount, 1, 6);
            GUI.Label(new Rect(L + 36 + halfW, y, 30, 16), "距离", EditorStyles.miniLabel);
            _previewTargetDistance = EditorGUI.Slider(new Rect(L + 66 + halfW, y, halfW - 36, 14), _previewTargetDistance, 1f, 10f);
            y += 16f;
            GUI.Label(new Rect(L, y, 56, 16), "扇形角", EditorStyles.miniLabel);
            _previewTargetArc = EditorGUI.Slider(new Rect(L + 58, y, W - 58, 14), _previewTargetArc, 0f, 180f);
            y += 20f;
            if (EditorGUI.EndChangeCheck())
            {
                SavePreviewDisplayPrefs();
                RespawnPreviewTargets();
            }

            var viewRect = new Rect(L, y, W, area.height - (y - area.y) - 4);
            DrawPreviewView(viewRect);
        }

        /// <summary>封装的预览行：标签 + ObjectField + 值变更回调，消除硬编码偏移量。</summary>
        void DrawPreviewObjectRow<T>(Rect area, float L, float W, ref float y, ref T current, string label, System.Action onChanged)
            where T : UnityEngine.Object
        {
            GUI.Label(new Rect(L, y, 80, 18), label, EditorStyles.miniLabel);
            int numRef = (typeof(T) == typeof(SkillData)) ? 72 : 80;
            var next = (T)EditorGUI.ObjectField(new Rect(L + numRef, y, W - numRef, 18), current, typeof(T), false);
            if (next != current)
            {
                current = next;
                onChanged?.Invoke();
            }
            y += 20f;
        }

        // 联动模式:按 _playheadT 落在哪个 AnimClipLayerData/MultiStageLayerData 的 [triggerTime, triggerTime+len] 区间
        // 选出对应片段;都没命中则 fallback 到 animClips[0](保证预览区始终有内容可看)。
        void AutoSelectClipForPlayhead()
        {
            _previewClipStartT = 0f;
            // V3 修复:优先用工作副本(双轨解耦后,_editingTarget 可能是别的 .asset)
            var data = GetActivePreviewData();
            if (data == null) { _previewClip = null; return; }
            if (data.graphData == null)
            {
                _previewClip = data.animClips != null && data.animClips.Length > 0
                    ? data.animClips[0] : null;
                return;
            }
            AnimationClip best = null;
            float bestStart = 0f;
            for (int i = 0; i < data.graphData.Count; i++)
            {
                var d = data.graphData[i];
                if (!(d is AnimClipLayerData || d is MultiStageLayerData)) continue;
                var acf = d.GetType().GetField("animClip", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                var clip = acf?.GetValue(d) as AnimationClip;
                if (clip == null) continue;
                float tt = GetNodeTriggerTime(d);
                if (_playheadT >= tt && _playheadT <= tt + clip.length)
                {
                    best = clip; bestStart = tt; break;
                }
                // 没有精确命中时,记录时间上最接近且已经开始的片段作为 fallback
                if (best == null && _playheadT >= tt) { best = clip; bestStart = tt; }
            }
            if (best == null && data.animClips != null && data.animClips.Length > 0)
                best = data.animClips[0];

            // 装配链路的主动作 Clip 是 animClips[0]；若当前播放头仍停在旧轨道时间，
            // 不允许预览沿用旧 Clip/旧角色表现。
            if (best == null || !IsClipOwnedByCurrentSkill(best, data))
            {
                best = data.animClips != null && data.animClips.Length > 0
                    ? data.animClips[0]
                    : null;
                bestStart = 0f;
            }
            _previewClip = best;
            _previewClipStartT = bestStart;
        }

        static bool IsClipOwnedByCurrentSkill(AnimationClip clip, SkillData data)
        {
            if (clip == null || data == null) return false;
            if (data.animClips != null && System.Array.IndexOf(data.animClips, clip) >= 0) return true;
            if (data.graphData == null) return false;
            foreach (var node in data.graphData)
            {
                if (node is AnimClipLayerData anim && anim.animClip == clip) return true;
                if (node is MultiStageLayerData multi && multi.animClip == clip) return true;
            }
            return false;
        }

        void RefreshPreviewClipCache()
        {
            _previewClipCache.Clear();
            // V3 修复:同 AutoSelectClipForPlayhead,优先用工作副本
            var data = GetActivePreviewData();
            if (data == null) return;
            if (data.animClips != null)
                for (int i = 0; i < data.animClips.Length; i++)
                    if (data.animClips[i] != null) _previewClipCache.Add(data.animClips[i]);
            // 与 AutoSelectClipForPlayhead 保持一致：同时收集普通动画层和多段动画层。
            if (data.graphData != null)
            {
                for (int i = 0; i < data.graphData.Count; i++)
                {
                    AnimationClip clip = null;
                    if (data.graphData[i] is AnimClipLayerData anim)
                        clip = anim.animClip;
                    else if (data.graphData[i] is MultiStageLayerData multi)
                        clip = multi.animClip;
                    if (clip != null && !_previewClipCache.Contains(clip))
                        _previewClipCache.Add(clip);
                }
            }
        }

        // V3.1.5 新增:从 SkillData 资产路径自动推断角色 Prefab
        //  例:Assets/_Game/Character/graves/SkillData/h_graves@attack01.asset
        //  → 向上找 Character/graves/ 目录,加载第一个 .prefab
        GameObject AutoFindCharacterPrefab(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            // 向上找到 Character/<name>/ 目录(大小写不敏感,prefab 在角色目录下)
            string dir = System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/');
            while (!string.IsNullOrEmpty(dir))
            {
                string parentDir = System.IO.Path.GetDirectoryName(dir)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(parentDir)
                    && string.Equals(System.IO.Path.GetFileName(parentDir), "Character", StringComparison.OrdinalIgnoreCase))
                {
                    // dir 就是 Character/<name>/ 目录。标准角色 Prefab 是唯一可靠来源；
                    // 玩家入口只作为兼容回退，避免 NetworkPlayer 变体或旧入口污染预览。
                    string folderName = System.IO.Path.GetFileName(dir);
                    string standardPath = $"{dir}/{folderName}_Character.prefab";
                    var standardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(standardPath);
                    if (standardPrefab != null && standardPrefab.GetComponentInChildren<Animator>(true) != null)
                        return standardPrefab;

                    string configuredPath = EditorPrefs.GetString("Test09.CharacterKit.PlayerPrefabPath", "");
                    if (!string.IsNullOrEmpty(configuredPath)
                        && configuredPath.StartsWith(dir + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        var configured = AssetDatabase.LoadAssetAtPath<GameObject>(configuredPath);
                        if (configured != null && configured.GetComponentInChildren<Animator>(true) != null)
                            return configured;
                    }

                    var guids = AssetDatabase.FindAssets("t:Prefab", new[] { dir });
                    GameObject fallback = null;
                    foreach (var g in guids)
                    {
                        string p = AssetDatabase.GUIDToAssetPath(g);
                        if (p.Contains("/_Backups/", StringComparison.OrdinalIgnoreCase)
                            || p.Contains("/Temp/", StringComparison.OrdinalIgnoreCase))
                            continue;
                        var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                        if (go == null || go.GetComponentInChildren<Animator>(true) == null) continue;
                        string fileName = System.IO.Path.GetFileNameWithoutExtension(p);
                        if (string.Equals(fileName, folderName + "_Character", StringComparison.OrdinalIgnoreCase))
                            return go;
                        fallback ??= go;
                    }
                    return fallback;
                }
                dir = parentDir;
            }
            return null;
        }

        void EnsurePreviewPrefabMatchesSkill()
        {
            if (_previewTarget == null || string.IsNullOrEmpty(_previewTargetPath)) return;
            var resolved = AutoFindCharacterPrefab(_previewTargetPath);
            if (resolved == null || resolved == _previewPrefab) return;

            _previewPrefab = resolved;
            _previewMainHandWeapon = null;
            _previewOffHandWeapon = null;
            _previewClip = null;
            _previewTime = 0f;
            _playheadT = 0f;
            RebuildPreviewInstance();
            _previewRuntime?.Init(_previewInstance, _workingCopy);
            RefreshPreviewClipCache();
            AutoSelectClipForPlayhead();
            SyncPreviewPoseForTick();
            Debug.Log($"[SkillBuilder] 预览角色已强制绑定：{AssetDatabase.GetAssetPath(_previewPrefab)}；技能：{_previewTargetPath}");
        }

        bool IsPreviewPrefabForSkill(GameObject prefab, string skillAssetPath)
        {
            if (prefab == null || string.IsNullOrEmpty(skillAssetPath)) return false;
            string prefabPath = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(prefabPath)) return false;

            string dir = System.IO.Path.GetDirectoryName(skillAssetPath)?.Replace('\\', '/');
            while (!string.IsNullOrEmpty(dir))
            {
                string parentDir = System.IO.Path.GetDirectoryName(dir)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(parentDir)
                    && string.Equals(System.IO.Path.GetFileName(parentDir), "Character", StringComparison.OrdinalIgnoreCase))
                {
                    return prefabPath.StartsWith(dir + "/", StringComparison.OrdinalIgnoreCase);
                }
                dir = parentDir;
            }
            return false;
        }

        // V3.1.7 新增:从角色 Prefab 读取 WeaponHolder.initialMainHandWeapon / initialOffHandWeapon,
        // 切换技能时自动同步到预览武器槽(用户不再需要每次手动拖武器)。
        // 只在 _previewMainHandWeapon == null 时尝试自动填充(用户已手动拖入的武器优先,不覆盖)。
        void AutoSyncWeaponsFromPrefab(GameObject prefab)
        {
            if (prefab == null) return;
            var holder = prefab.GetComponent<WeaponHolder>();
            if (holder == null) return;
            if (_previewMainHandWeapon == null && holder.initialMainHandWeapon != null)
            {
                _previewMainHandWeapon = holder.initialMainHandWeapon;
            }
            if (_previewOffHandWeapon == null && holder.initialOffHandWeapon != null)
            {
                _previewOffHandWeapon = holder.initialOffHandWeapon;
            }
        }

        void RebuildPreviewInstance()
        {
            DisposePreview();
            if (_previewPrefab == null) return;
            _previewRtu = new PreviewRenderUtility();
            _previewRtu.cameraFieldOfView = 30f;
            _previewRtu.camera.farClipPlane = 1000f;
            _previewRtu.camera.nearClipPlane = 0.05f;
            // 离屏实例 + 单独 GO(不用 Animator 预览模式,因为我们要直接 SampleAnimationClip)
            _previewInstance = (GameObject)UnityEngine.Object.Instantiate(_previewPrefab);
            _previewInstance.hideFlags = HideFlags.HideAndDontSave;
            _previewRtu.AddSingleGO(_previewInstance);
            _previewBoundsValid = false;
            // P0:PreviewRenderUtility 离屏渲染时 SkinnedMeshRenderer 默认不更新蒙皮
            // （updateWhenOffscreen = false）。强制设为 true 确保角色蒙皮正确显示。
            _previewSmrs = _previewInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in _previewSmrs)
            {
                smr.updateWhenOffscreen = true;
            }
            // V3 修复:绑定 Animator 缓存。若 Prefab 有 Animator 组件则走 Animator 状态机路径,
            // 否则 _previewActor 留空,后续自动降级为纯 AnimationClip 路径(原有行为不变)。
            _previewActor = _previewInstance.GetComponentInChildren<Animator>(true);
            // P1-8.5: HideAndDontSave 实例不走 OnEnable,Animator.runtimeAnimatorController 为 null。
            // Clip 路径依赖 EnsurePreviewOverrideController → AnimatorOverrideController 做 Humanoid
            // Avatar 重定向。若 controller 为 null,回退到 SampleAnimation(不经过 Avatar 重定向,
            // Humanoid 骨骼位姿错误)导致预览看起来是 T-pose 或错位"待机"而非攻击动画。
            if (_previewActor != null && _previewActor.runtimeAnimatorController == null)
            {
                var prefabActor = _previewPrefab.GetComponentInChildren<Animator>(true);
                if (prefabActor != null && prefabActor.runtimeAnimatorController != null)
                    _previewActor.runtimeAnimatorController = prefabActor.runtimeAnimatorController;
                else
                    TryAssignControllerFromPrefab();  // fallback: parse prefab YAML for controller GUID
            }
            ResetAnimatorPlayState();
            // V3:挂武器(主手/副手)
            MountPreviewWeapons();
            SpawnPreviewTargets();
        }

        // ── 受击目标(2026-08-03) ─────────────────────────────────────────

        /// <summary>按当前配置在角色前方扇形排布受击目标;prefab 为空=内置胶囊假人。</summary>
        void SpawnPreviewTargets()
        {
            ClearPreviewTargets();
            if (_previewRtu == null || _previewInstance == null) return;

            int count = Mathf.Clamp(_previewTargetCount, 1, 6);
            var origin = _previewInstance.transform;
            for (int i = 0; i < count; i++)
            {
                // 扇形均分:count==1 时正前方;否则以正前为中心左右均分 _previewTargetArc
                float angle = count == 1 ? 0f : -_previewTargetArc * 0.5f + _previewTargetArc * i / (count - 1);
                Vector3 pos = origin.position
                    + Quaternion.Euler(0f, angle, 0f) * origin.forward * _previewTargetDistance;

                GameObject t = CreatePreviewTarget(pos);
                if (t == null) continue;
                t.transform.rotation = Quaternion.LookRotation(origin.position - pos); // 面向角色
                t.hideFlags = HideFlags.HideAndDontSave;
                _previewRtu.AddSingleGO(t);
                _previewTargets.Add(t);
            }
            // 同步给预览运行时(HitVFX 预览落点)
            _previewRuntime?.SetPreviewHitTargets(_previewTargets);
        }

        /// <summary>配置变更时仅重建目标(不动角色实例/PRU)。</summary>
        void RespawnPreviewTargets() => SpawnPreviewTargets();

        void ClearPreviewTargets()
        {
            for (int i = 0; i < _previewTargets.Count; i++)
                if (_previewTargets[i] != null)
                    UnityEngine.Object.DestroyImmediate(_previewTargets[i]);
            _previewTargets.Clear();
            _previewTargetAnims.Clear();
        }

        /// <summary>实例化目标:有 prefab 用 prefab(剥离逻辑组件),否则内置胶囊假人。</summary>
        GameObject CreatePreviewTarget(Vector3 pos)
        {
            if (_previewTargetPrefab != null)
            {
                var go = UnityEngine.Object.Instantiate(_previewTargetPrefab, pos, Quaternion.identity);
                // 纯视觉摆件:禁用全部行为/AI/物理驱动,保留渲染与蒙皮
                foreach (var b in go.GetComponentsInChildren<Behaviour>(true))
                {
                    if (b is Animator) continue;
                    b.enabled = false;
                }
                var rb = go.GetComponentInChildren<Rigidbody>();
                if (rb != null) { rb.isKinematic = true; rb.detectCollisions = false; }
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    smr.updateWhenOffscreen = true;
                // 动画:HideAndDontSave 实例不走 OnEnable,controller 可能未接上——从 prefab 资产补;
                // 进 default state(待机),由渲染循环手动 Update 驱动
                var anim = go.GetComponentInChildren<Animator>();
                if (anim != null)
                {
                    if (anim.runtimeAnimatorController == null)
                    {
                        var prefabAnim = _previewTargetPrefab.GetComponentInChildren<Animator>();
                        if (prefabAnim != null)
                            anim.runtimeAnimatorController = prefabAnim.runtimeAnimatorController;
                    }
                    anim.Rebind();
                    anim.Update(0f);
                    _previewTargetAnims.Add(anim);
                }
                return go;
            }
            else
            {
                // 内置胶囊假人(1.8m,灰红色):占位观察命中覆盖
                var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                cap.name = "PreviewTarget_Dummy";
                cap.transform.position = pos + Vector3.up * 0.9f;
                cap.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
                var col = cap.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.DestroyImmediate(col);   // 离屏场景无需碰撞
                var rend = cap.GetComponent<MeshRenderer>();
                if (rend != null)
                {
                    var mat = new Material(Shader.Find("Standard"));
                    mat.color = new Color(0.75f, 0.45f, 0.45f, 1f);
                    rend.sharedMaterial = mat;
                }
                return cap;
            }
        }

        void DisposePreview()
        {
            // 先卸武器(独立 GameObject),避免 DestroyImmediate 父级时留 zombie
            DestroyPreviewWeapon(ref _previewMainHandInstance);
            DestroyPreviewWeapon(ref _previewOffHandInstance);
            ClearPreviewTargets();
            if (_previewRtu != null) { _previewRtu.Cleanup(); _previewRtu = null; }
            if (_previewInstance != null)
            {
                if (!EditorUtility.IsPersistent(_previewInstance))
                    DestroyImmediate(_previewInstance);
                _previewInstance = null;
            }
            // AnimatorOverrideController 是由预览实例持有的临时对象；释放引用即可。
            // 不调用 DestroyImmediate，避免 Unity 把其内部引用误判为持久化 Asset。
            _previewOverrideCtrl = null;
            _previewOverrideIsTemporary = false;
            _previewOverrideAnchorClip = null;
            _previewOverrideStateName = null;
            _previewActor = null;
            _previewSmrs = null;
        }

        static void DestroyPreviewWeapon(ref GameObject instance)
        {
            if (instance == null) return;
            if (!EditorUtility.IsPersistent(instance))
                DestroyImmediate(instance);
            instance = null;
        }

        // V3 修复:Animator 状态机路径的内部状态清零
        static AnimationClip GetPreviewPrimaryClip(SkillData data)
        {
            if (data == null) return null;
            if (data.animClips != null)
            {
                for (int i = 0; i < data.animClips.Length; i++)
                    if (data.animClips[i] != null) return data.animClips[i];
            }
            if (data.graphData != null)
            {
                for (int i = 0; i < data.graphData.Count; i++)
                {
                    if (data.graphData[i] is AnimClipLayerData anim && anim.animClip != null)
                        return anim.animClip;
                    if (data.graphData[i] is MultiStageLayerData multi && multi.animClip != null)
                        return multi.animClip;
                }
            }
            return null;
        }

        void ResetAnimatorPlayState()
        {
            _previewAnimElapsed = 0f;
            _previewAnimLength = 0f;
            _previewAnimTriggered = false;
            _previewLastError = null;
        }

        // V3 修复:Animator 状态机播放初始化 —— SetTrigger(animTrigger) + Play(animClipName) + Update(0)
        // 与运行时等价:先 trigger 让 AnyState 评估,再强制 Play 把当前状态跳到目标 state 头,
        // 最后用 Update(0) 让 Unity 立刻结算一次 pose,使首帧 T-pose 立刻消失。
        //
        // 编辑器预览特有陷阱:实例化后的 Animator 走 OnEnable 才把 runtimeAnimatorController wire 上去,
        // 在 HideAndDontSave 克隆下可能还没 wire 时就被调 Play/SetTrigger,Unity 会报
        // "Animator is not playing an AnimatorController"。所以这里用延迟一帧等 OnEnable 跑完。
        //
        // 项目内"State could not be found" 根因:`animClipName` 字段虽然叫 Clip 名称,
        // 但本项目里实际是 Animator 状态机 state 名(参见 SkillData.cs tooltip；
        // 实际生成的状态 `Skill_Q/Skill_W/Skill_R/SkillIdle/Attack_1` 等)。用户配置时常填错。
        // 这里做:精确名 → 忽略大小写 → 忽略下划线 三级匹配,仍找不到就 Play base layer default state
        // 并缓存提示,避免 Unity 抛异常。
        void StartAnimatorPlayback()
        {
            if (_previewActor == null) return;
            var data = GetActivePreviewData();
            if (data == null) return;
            if (string.IsNullOrEmpty(data.animTrigger) && string.IsNullOrEmpty(data.animClipName)) return;

            // P1-8.2:Animator 路径需要无污染的控制器。若 Clip 路径设置了 override,
            // 清除之,让 Animator 还原到原始 state→clip 映射。
            if (_previewOverrideCtrl != null && _previewOverrideAnchorClip != null)
                _previewOverrideCtrl[_previewOverrideAnchorClip] = null;

            // 自愈:如果 controller 还是 null(克隆路径下偶发),按 prefab 内 m_Controller 主动强绑
            if (_previewActor.runtimeAnimatorController == null)
            {
                TryAssignControllerFromPrefab();
            }

            // 第一次 Rebind + Update(0) 触发 OnEnable 走完 + 强制 wire
            _previewActor.Rebind();
            _previewActor.Update(0f);

            // P4:同步 fade 时长(对齐运行时 PlayAnimation 语义:=-1 用 SkillData 值,>=0 用自身)
            float capFade = data.fadeInDuration >= 0f ? data.fadeInDuration : 0.1f;
            _previewFadeIn = capFade;

            // 推迟一帧再发 SetTrigger / Play,等 OnEnable 真的把 controller 接上
            // 避免 "Animator is not playing an AnimatorController"
            var anim = _previewActor;
            var trigger = data.animTrigger;
            // P1-8.7: 当 animClipName 为空时,按 animTrigger/animStateId 推导 state 名,
            // 对齐运行时 SkillAnimPlayer.GetAnimatorStateName()。否则 SkillFullBody(7 层)
            // 和 SkillUpperBody(8 层) 的 state 无法被定位到。
            var clipName = data.animClipName;
            if (string.IsNullOrEmpty(clipName))
            {
                clipName = DerivePreviewStateName(data);
            }
            EditorApplication.delayCall += () =>
            {
                if (anim == null) return;
                if (anim.runtimeAnimatorController == null) return; // 仍没 controller,放弃本轮
                // 解析真正可用的 state 名(三级匹配),找不到就用 base layer default state
                string resolved = ResolveAnimatorStateName(anim, clipName);
                if (!string.IsNullOrEmpty(trigger))
                {
                    // 静默吞掉"未知 trigger"—— controller 没注册这个参数就不发
                    if (HasAnimatorParameter(anim, trigger))
                    {
                        try { anim.SetTrigger(trigger); } catch (Exception ex) { _previewLastError = $"Step 预览 SetTrigger({trigger}) 失败: {ex.Message}"; }
                    }
                }
                if (!string.IsNullOrEmpty(resolved))
                {
                    // 预览必须与 Character Kit 挂载的 Override 保持同一 Clip；
                    // 这里先清空旧 override，再把当前 SkillData Clip 写入目标状态。
                    EnsurePreviewOverrideController();
                    if (_previewOverrideCtrl != null && _previewOverrideAnchorClip != null
                        && _previewResolvedLayer == _previewOverrideLayer)
                    {
                        _previewOverrideCtrl[_previewOverrideAnchorClip] = GetPreviewPrimaryClip(data);
                    }
                    // P4:使用 CrossFadeInFixedTime 替代 Play,对齐运行时 SkillAnimPlayer 的 SafeCrossFade
                    // P1-8.7: 先设置目标 layer weight=1 (SkillFullBody/SkillUpperBody 默认为 0)
                    if (_previewResolvedLayer >= 0)
                        anim.SetLayerWeight(_previewResolvedLayer, 1f);
                    anim.CrossFadeInFixedTime(resolved, capFade, _previewResolvedLayer, 0f);
                    _previewAnimResolvedState = resolved;
                    _previewAnimStateMissing = string.IsNullOrEmpty(clipName)
                        ? null
                        : (resolved == null || !StateNamesEqual(resolved, clipName)
                            ? clipName
                            : null);
                }
                else
                {
                    // 连 default state 都拿不到 —— 什么也别做,角色保持当前 pose
                    _previewAnimResolvedState = null;
                    _previewAnimStateMissing = clipName;
                }
                anim.Update(0f);
            };

            // 取当前状态的 length,用于循环 / 收尾(用 default 1.5s 兜底,等下一帧 OnEnable 跑完再重取)
            _previewAnimLength = 1.5f;
            _previewAnimElapsed = 0f;
            _previewAnimTriggered = true;
            // V3.1.2 修复:启动播放时同步设置 Animator.speed = _previewPlaySpeed
            //  显式 Update 路径不走 Animator.speed,但 Animator 内部若有 IK / 物理 / 状态转换
            //  被 PlayerLoop 推,会乘 Animator.speed;同步设置保证两条路径一致。
            if (_previewActor != null) _previewActor.speed = _previewPlaySpeed;
        }

        // V3 修复:在 AnimatorController 中找匹配 name 的 state。三级匹配策略:
        //   ① 精确名
        //   ② 忽略大小写
        //   ③ 忽略下划线/连字符
        // 搜索全部 Layer(非仅 Layer 0),因为 SkillFullBody/SkillUpperBody 在 7/8 层。
        // 全失败回退到任意层的 default state,并把 _previewResolvedLayer 设为找到的层索引。
        string ResolveAnimatorStateName(Animator anim, string name)
        {
            _previewResolvedLayer = 0;
            if (string.IsNullOrEmpty(name)) return null;
            // P1-8.2: runtimeAnimatorController 可能是 AnimatorOverrideController,需要解包。
            var rc = anim.runtimeAnimatorController;
            if (rc is AnimatorOverrideController ovr)
                rc = ovr.runtimeAnimatorController;
            var ctrl = rc as UnityEditor.Animations.AnimatorController;
            if (ctrl == null) return null;
            if (ctrl.layers.Length == 0) return null;

            // ① 遍历所有 layer 做精确匹配；优先选择真正含有目标 Motion 的状态。
            // 仅依赖 HasState 会把同名空状态当作成功，随后 Animator 只显示待机。
            for (int li = 0; li < ctrl.layers.Length; li++)
            {
                if (!anim.HasState(li, Animator.StringToHash(name))) continue;
                var exactState = FindAnimatorState(ctrl.layers[li].stateMachine, name);
                if (exactState != null && exactState.motion != null)
                {
                    _previewResolvedLayer = li;
                    return exactState.name;
                }
            }
            // 不接受空 Motion 状态：接受它会让 CrossFade 成功但实际播放待机姿态。
            // ② 忽略大小写
            for (int li = 0; li < ctrl.layers.Length; li++)
            {
                var sm = ctrl.layers[li].stateMachine;
                foreach (var cs in sm.states)
                {
                    var s = cs.state;
                    if (s == null || string.IsNullOrEmpty(s.name)) continue;
                    if (string.Equals(s.name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        _previewResolvedLayer = li;
                        return s.name;
                    }
                }
            }
            // ③ 忽略下划线/连字符
            string nrm = NormalizeName(name);
            for (int li = 0; li < ctrl.layers.Length; li++)
            {
                var sm = ctrl.layers[li].stateMachine;
                foreach (var cs in sm.states)
                {
                    var s = cs.state;
                    if (s == null) continue;
                    if (NormalizeName(s.name) == nrm)
                    {
                        _previewResolvedLayer = li;
                        return s.name;
                    }
                }
            }
            // 未找到技能状态时必须失败，不允许静默回退到 Idle/Locomotion 的 default state；
            // 否则用户看到的是“预览播放成功”，实际却是待机或旧角色表现。
            _previewResolvedLayer = -1;
            return null;
        }

        static AnimatorState FindAnimatorState(AnimatorStateMachine sm, string name)
        {
            if (sm == null || string.IsNullOrEmpty(name)) return null;
            foreach (var child in sm.states)
            {
                if (child.state != null && string.Equals(child.state.name, name, StringComparison.Ordinal))
                    return child.state;
            }
            foreach (var child in sm.stateMachines)
            {
                var found = FindAnimatorState(child.stateMachine, name);
                if (found != null) return found;
            }
            return null;
        }

        static string NormalizeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("_", "").Replace("-", "").Replace(" ", "").ToLowerInvariant();
        }

        /// <summary>
        /// 从 SkillData 推导 Animator state 名,对齐运行时 SkillAnimPlayer.GetAnimatorStateName()。
        /// 当 animClipName 为空时使用:Attack → "Attack_N",SkillQ → "Skill_Q"。
        /// </summary>
        static string DerivePreviewStateName(SkillData data)
        {
            if (data == null) return null;
            if (!string.IsNullOrWhiteSpace(data.animClipName))
                return data.animClipName;
            if (data.category == SkillCategory.BasicAttack)
                return "Attack_" + data.animStateId;
            if (!string.IsNullOrEmpty(data.animTrigger) && data.animTrigger.StartsWith("Skill") && data.animTrigger.Length > 5)
            {
                string letter = data.animTrigger.Substring(5);
                return "Skill_" + letter;
            }
            return "Skill_Q";
        }

        static bool StateNamesEqual(string a, string b)
        {
            if (a == null || b == null) return false;
            if (a == b) return true;
            return NormalizeName(a) == NormalizeName(b);
        }

        // 检查 controller 是否注册了某参数(Trigger / Int / Float / Bool)
        bool HasAnimatorParameter(Animator anim, string name)
        {
            if (anim == null || string.IsNullOrEmpty(name)) return false;
            var ps = anim.parameters;
            for (int i = 0; i < ps.Length; i++)
                if (ps[i].name == name) return true;
            return false;
        }

        // V3 修复:Animator 没绑 controller 时,按 Prefab 内 m_Controller 的 GUID 强绑
        void TryAssignControllerFromPrefab()
        {
            if (_previewPrefab == null) return;
            if (_previewActor == null) return;
            // 读 prefab 源文件,搜 m_Controller: {fileID: ..., guid: X, type: 2} 这一行
            string path = AssetDatabase.GetAssetPath(_previewPrefab);
            if (string.IsNullOrEmpty(path)) return;
            string raw = System.IO.File.ReadAllText(System.IO.Path.GetFullPath(path));
            // 取 m_Controller 行的 guid
            int g = raw.IndexOf("m_Controller:");
            if (g < 0) return;
            int guidIdx = raw.IndexOf("guid:", g);
            if (guidIdx < 0) return;
            string guid = raw.Substring(guidIdx + 5, 32);
            string ctrlPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(ctrlPath)) return;
            var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ctrlPath);
            if (ctrl == null) return;
            _previewActor.runtimeAnimatorController = ctrl;
        }

        // 拿当前 Animator 主状态机的"当前状态"长度(秒)。若拿不到回退到 1.5s 默认
        float GetActiveAnimatorStateLength()
        {
            if (_previewActor == null || _previewActor.runtimeAnimatorController == null) return 1.5f;
            var info = _previewActor.GetCurrentAnimatorStateInfo(_previewResolvedLayer);
            if (info.length <= 0f) return 1.5f;
            return info.length;
        }

        // V3 修复:每帧推进 Animator 状态机。Editor 模式下 Animator 不会自动 Update,
        // 必须显式调用 animator.Update(dt) 才会推进 normalizedTime。
        // 到 length 时归零重新 Play(loop),保持可循环预览。
        void StepAnimatorPlayback(float dt)
        {
            if (_previewActor == null || !_previewAnimTriggered) return;
            if (_previewActor.runtimeAnimatorController == null) return;
            if (_previewAnimLength <= 0f) _previewAnimLength = GetActiveAnimatorStateLength();

            _previewActor.Update(dt);
            _previewAnimElapsed += dt;
            if (_previewAnimLength > 0f && _previewAnimElapsed >= _previewAnimLength)
            {
                // 循环:CrossFade 回到开头(用短 blend 模拟运行时循环,对齐 SkillAnimPlayer)
                if (!string.IsNullOrEmpty(_previewAnimResolvedState))
                {
                    _previewActor.CrossFadeInFixedTime(_previewAnimResolvedState,
                        _previewFadeIn * 0.5f, _previewResolvedLayer, 0f);
                }
                _previewAnimElapsed = 0f;
                // V3.1.6:循环时重置 Runtime 状态,让 VFX/SFX/graphData 节点重新触发
                _previewRuntime?.Reset();
            }
        }

        void DrawPreviewView(Rect rect)
        {
            if (rect.width < 8 || rect.height < 8) return;
            EditorGUI.DrawRect(rect, new Color(0.05f, 0.05f, 0.07f));
            // V3.1.2 修复:删除原 22px 底部浮动条(撤回/确认/SFX/FPS 挪到 ModeBar),整个 rect 都是交互区
            //  V3.1 旧版这里 botReserve = 22f 是为了不让 DrawPreviewView 的 MouseDown 吃掉底行按钮,
            //  现在底行已经没了,botReserve 设为 0。
            float botReserve = 0f;
            Rect interactiveRect = new Rect(rect.x, rect.y, rect.width, Mathf.Max(8f, rect.height - botReserve));
            // 网格(深灰)
            Handles.color = new Color(0.2f, 0.2f, 0.22f);
            int gridN = 8;
            for (int i = 1; i < gridN; i++)
            {
                float gx = rect.x + rect.width * i / (float)gridN;
                Handles.DrawLine(new Vector3(gx, rect.y, 0), new Vector3(gx, rect.yMax, 0));
                float gy = rect.y + rect.height * i / (float)gridN;
                Handles.DrawLine(new Vector3(rect.x, gy, 0), new Vector3(rect.xMax, gy, 0));
            }
            Handles.color = Color.white;

            if (_previewRtu == null || _previewInstance == null)
            {
                GUI.Label(rect, "拖入角色 Prefab\n或选择 EditExisting 加载 SkillData", s_previewTipStyle);
                return;
            }

            // 计算包围盒(只算一次)
            if (!_previewBoundsValid)
            {
                _previewBounds = CalculateBounds(_previewInstance);
                _previewBoundsValid = true;
            }

            // 相机方位
            float az = _previewDir.x * Mathf.Deg2Rad;
            float el = _previewDir.y * Mathf.Deg2Rad;
            float radius = Mathf.Max(_previewBounds.extents.magnitude, 0.5f) * 3.5f * _previewZoom;
            Vector3 center = _previewBounds.center;
            Vector3 camPos = center + new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el)) * radius;
            _previewRtu.camera.transform.position = camPos;
            _previewRtu.camera.transform.LookAt(center);

            // 拖动控制(中键平移,左键轨道)
            // V3 修复:用 interactiveRect 排除底行 44px 控制条,避免 MouseDown 把底行按钮的点击吃掉
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && interactiveRect.Contains(e.mousePosition))
            {
                _previewDragMode = 1; _previewDragStart = e.mousePosition;
                _previewDragStartDir = _previewDir; GUIUtility.hotControl = _previewCtrlId; e.Use();
            }
            else if (e.type == EventType.MouseDown && e.button == 2 && interactiveRect.Contains(e.mousePosition))
            {
                _previewDragMode = 2; _previewDragStart = e.mousePosition;
                _previewDragStartZoom = _previewZoom; GUIUtility.hotControl = _previewCtrlId; e.Use();
            }
            else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == _previewCtrlId)
            {
                if (_previewDragMode == 1)
                {
                    Vector2 d = e.mousePosition - _previewDragStart;
                    _previewDir = new Vector2(_previewDragStartDir.x + d.x * 0.5f,
                                              Mathf.Clamp(_previewDragStartDir.y + d.y * 0.5f, -89f, 89f));
                }
                else if (_previewDragMode == 2)
                {
                    float d = e.mousePosition.y - _previewDragStart.y;
                    _previewZoom = Mathf.Clamp(_previewZoom * (1f + d * 0.01f), 0.2f, 5f);
                }
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseUp && GUIUtility.hotControl == _previewCtrlId)
            {
                _previewDragMode = 0; GUIUtility.hotControl = 0; e.Use();
            }
            else if (e.type == EventType.ScrollWheel && interactiveRect.Contains(e.mousePosition))
            {
                _previewZoom = Mathf.Clamp(_previewZoom * (1f - e.delta.y * 0.05f), 0.2f, 5f);
                e.Use(); Repaint();
            }
            EditorGUIUtility.AddCursorRect(interactiveRect, MouseCursor.Orbit);

            // 直接采样到预览实例。不能在这里使用 AnimationMode：StopAnimationMode 会在
            // 绘制结束时还原骨骼 Transform，已挂在骨骼上的 local-space VFX 会随之回到错误朝向。
            // SampleAnimation 与 Tick 前的骨骼采样共用同一条路径，确保渲染、VFX 挂点和运行时一致。
            if (_previewClip != null)
            {
                float sampleT = _previewLinkTimeline
                    ? (_playheadT - _previewClipStartT)
                    : _previewTime;
                sampleT = Mathf.Clamp(sampleT, 0f, _previewClip.length);
                SamplePreviewClipPose(_previewClip, sampleT);
            }
            // V3.1.2 修复:Animator 状态机路径在渲染前再补一帧 Update,
            // 确保 Repaint() 后帧间的 pose 差被相机捕获(否则会出现"按钮按了但下一帧 T-pose"的现象)
            // V3.1.3 修复:加 _previewPlaying 守卫 — 暂停时渲染路径不应再推 Animator,
            // 否则 OnGUI 每次 Repaint 都无条件推一帧 StepAnimatorPlayback,导致暂停后画面仍会"抽搐"。
            // P1-3 修复:此处只调 _previewActor.Update(renderDt) 仅推 Animator 视觉,
            // 不再调 StepAnimatorPlayback —— 后者会 _previewAnimElapsed += dt,在 OnGUI
            // 多 Repaint 帧里额外累加时间,导致下一帧 Tick 区间 (LastTickT, _previewAnimElapsed]
            // 膨胀成多个渲染帧的跨度,表现为"全部特效一帧刷出"或"暂停恢复时间错位"。
            else if (_previewActor != null && _previewAnimTriggered && _previewPlaying)
            {
                float renderDt = Mathf.Clamp((float)(EditorApplication.timeSinceStartup - _previewLastEditorTime), 0f, 0.1f);
                if (renderDt > 0f) _previewActor.Update(renderDt);
            }

            // 受击目标动画驱动(2026-08-03):离屏场景 Animator 不自动推进,手动 Update;
            // 与 _previewPlaying 同步暂停(与主角一致)
            if (_previewTargetAnims.Count > 0 && _previewPlaying)
            {
                float targetDt = Mathf.Clamp((float)(EditorApplication.timeSinceStartup - _previewLastEditorTime), 0f, 0.1f);
                if (targetDt > 0f)
                    for (int i = 0; i < _previewTargetAnims.Count; i++)
                        if (_previewTargetAnims[i] != null && _previewTargetAnims[i].runtimeAnimatorController != null)
                            _previewTargetAnims[i].Update(targetDt);
            }

            // 离屏场景粒子系统需要手动 Simulate(Editor 下不会自动推进)
            // P1-6:三种模式推进 VFX——
            //   播放中 → 墙钟 dt
            //   ruler 拖拽中 → 墙钟 dt (标记 _previewRulerDragging; 否则粒子创建后冻结 t=0)
            //   逐帧步进 → StepPreviewFrame 内直调 SimulateVFX(step) + 重置基线防重复
            //   纯暂停 → 不模拟 (冻结)
            if (_previewRuntime != null && (_previewPlaying || _previewRulerDragging))
            {
                double now = EditorApplication.timeSinceStartup;
                if (_previewLastSimulateVFXTime <= 0d) _previewLastSimulateVFXTime = now;
                float vfxDt = Mathf.Clamp((float)(now - _previewLastSimulateVFXTime), 0f, 0.5f);
                _previewLastSimulateVFXTime = now;
                if (vfxDt > 0f) _previewRuntime.SimulateVFX(vfxDt);
            }

            // P0:动画采样后骨骼位置已更新,强制 SkinnedMeshRenderer 重新计算蒙皮网格。
            // PreviewRenderUtility 离屏渲染不会自动触发 SMR 内部状态刷新,这里用
            // enabled toggle 强制 invalidate 其缓存,确保后续 camera.Render() 捕获到正确蒙皮。
            if (_previewSmrs != null)
            {
                foreach (var smr in _previewSmrs) { if (smr != null) smr.enabled = false; }
                foreach (var smr in _previewSmrs) { if (smr != null) smr.enabled = true; }
            }

            // 灯光参数(2026-07-28,test07 对齐):渲染前应用到 PreviewRenderUtility 主光
            if (_previewRtu.lights != null && _previewRtu.lights.Length > 0 && _previewRtu.lights[0] != null)
            {
                _previewRtu.lights[0].intensity = _previewLightIntensity;
                _previewRtu.lights[0].transform.rotation = Quaternion.Euler(_previewLightElevation, _previewLightAzimuth, 0f);
            }

            // 离屏渲到 RT
            _previewRtu.BeginPreview(rect, GUIStyle.none);
            _previewRtu.camera.Render();
            var tex = _previewRtu.EndPreview();
            GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, false);

            // ── 3D overlay(2026-07-28,test07 对齐):伤害范围 / 参考坐标轴 ──
            // 此前伤害范围只在 SceneView 绘制(OnSceneGui),预览窗从未渲染 → "伤害范围不显示"。
            // v2: Handles.SetCamera 与 PreviewRenderUtility pixelRect 存在坐标偏移,
            //     改为 DrawPreviewOverlay 手动 WorldToViewport 投影,位置严格对齐。
            if (Event.current.type == EventType.Repaint && (_showHitRange || _showRefAxes))
                DrawPreviewOverlay(rect);

            // Clip 与 Animator 路径都直接把 pose 保留在预览实例上；不要调用
            // AnimationMode.StopAnimationMode，否则 local-space VFX 会跟随被还原的骨骼而偏转。
        }

            // P1-8.2: 惰性创建 AnimatorOverrideController。预览必须沿用角色 Prefab
            // 上已经挂载的 AnimatorOverrideController 主骨架，不再另外创建脱离装配状态的普通 controller。
            // 这样预览的状态、层级和 Avatar 重定向与 Character Kit 装配结果一致。
        void EnsurePreviewOverrideController()
        {
            if (_previewActor == null) return;

            var raw = _previewActor.runtimeAnimatorController;
            var installed = raw as AnimatorOverrideController;
            var baseCtrl = installed != null
                ? installed.runtimeAnimatorController as UnityEditor.Animations.AnimatorController
                : raw as UnityEditor.Animations.AnimatorController;
            if (baseCtrl == null || baseCtrl.layers.Length == 0) return;

            if (_previewOverrideCtrl == null || !_previewOverrideIsTemporary
                || _previewOverrideCtrl.runtimeAnimatorController != baseCtrl)
            {
                // 复制 Prefab 上已经装配好的 Override 映射，预览只改临时副本。
                // 旧临时对象只释放引用，不销毁 Prefab/AssetDatabase 资源。
                _previewOverrideCtrl = installed != null
                    ? new AnimatorOverrideController(installed)
                    : new AnimatorOverrideController(baseCtrl);
                _previewOverrideCtrl.hideFlags = HideFlags.HideAndDontSave;
                _previewOverrideIsTemporary = true;
            }

            string preferredName = DerivePreviewStateName(GetActivePreviewData());
            AnimatorState selectedState = null;
            int foundLayer = -1;
            if (!string.IsNullOrEmpty(preferredName))
            {
                for (int li = 0; li < baseCtrl.layers.Length; li++)
                {
                    var candidate = FindAnimatorState(baseCtrl.layers[li].stateMachine, preferredName);
                    if (candidate != null && candidate.motion != null)
                    {
                        selectedState = candidate;
                        foundLayer = li;
                        break;
                    }
                }
            }

            if (selectedState == null)
            {
                _previewOverrideStateName = null;
                _previewOverrideAnchorClip = null;
                _previewOverrideLayer = -1;
                _previewLastError = $"预览 Animator 中找不到有效技能状态：{preferredName}";
                _previewActor.runtimeAnimatorController = _previewOverrideCtrl;
                return;
            }

            var anchors = new List<AnimationClip>();
            CollectMotionClips(selectedState.motion, anchors);
            _previewOverrideStateName = selectedState.name;
            _previewOverrideLayer = foundLayer;
            _previewOverrideAnchorClip = anchors.Count > 0 ? anchors[0] : null;
            _previewActor.runtimeAnimatorController = _previewOverrideCtrl;
        }

        static void CollectMotionClips(Motion motion, List<AnimationClip> clips)
        {
            if (motion is AnimationClip clip)
            {
                if (clip != null && !clips.Contains(clip)) clips.Add(clip);
                return;
            }
            if (motion is BlendTree tree)
            {
                foreach (var child in tree.children)
                    CollectMotionClips(child.motion, clips);
            }
        }

        void SamplePreviewClipPose(AnimationClip clip, float time)
        {
            if (_previewInstance == null || clip == null) return;

            // 保存根变换：动画 clip 可能携带根位移 / 根旋转曲线，
            // 直接采样会漂移角色根位置，导致 world-space VFX 朝向不一致。
            var rootPosition = _previewInstance.transform.position;
            var rootRotation = _previewInstance.transform.rotation;

            // P1-8.2: Humanoid 角色用 AnimatorOverrideController 通过 Animator 采样,
            // 获得 Avatar 重定向后的正确骨骼位姿。无 Animator(Generic 骨架)回退到 SampleAnimation。
            // P1-8.5: 自愈——HideAndDontSave 实例的 Animator 可能未走 OnEnable,controller 仍为 null
            if (_previewActor != null && _previewActor.runtimeAnimatorController == null)
                TryAssignControllerFromPrefab();
            if (_previewActor != null && _previewActor.runtimeAnimatorController != null)
            {
                EnsurePreviewOverrideController();
                if (_previewOverrideCtrl != null && _previewOverrideAnchorClip != null)
                {
                    _previewOverrideCtrl[_previewOverrideAnchorClip] = clip;
                    float normT = clip.length > 0f ? time / clip.length : 0f;
                    try
                    {
                        if (_previewActor.runtimeAnimatorController != null)
                        {
                            _previewActor.Play(_previewOverrideStateName, _previewOverrideLayer, normT);
                            _previewActor.Update(0f);
                        }
                        else
                        {
                            clip.SampleAnimation(_previewInstance, time);
                        }
                    }
                    catch
                    {
                        clip.SampleAnimation(_previewInstance, time);
                    }
                }
                else
                {
                    clip.SampleAnimation(_previewInstance, time);
                }
            }
            else
            {
                clip.SampleAnimation(_previewInstance, time);
            }

            // 恢复根变换：ApplyRootMotion 关闭时 Animator 不会移动根,但仍做防御
            _previewInstance.transform.SetPositionAndRotation(rootPosition, rootRotation);
        }

        static Bounds CalculateBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return new Bounds(go.transform.position, Vector3.one * 2f);
            }
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        // ═══════════════════════════════════════════════════════════════
        // 中:参数面板(原 DrawEditExisting / Step 1/2/3 / DrawGraphDataEditor)
        // ═══════════════════════════════════════════════════════════════
        void DrawParamsPanel(Rect area)
        {
            EditorGUI.DrawRect(area, UI.BgPanel);

            // ── 参数区滚动动画:选中 Clip 后自动平滑定位到对应节点 ──────
            if (_paramScrollAnimating && _paramScrollTarget >= 0f)
            {
                _scroll.y = Mathf.Lerp(_scroll.y, _paramScrollTarget, 0.22f);
                if (Mathf.Abs(_scroll.y - _paramScrollTarget) < 1.5f)
                {
                    _scroll.y = _paramScrollTarget;
                    _paramScrollAnimating = false;
                }
                Repaint();
            }

            GUILayout.BeginArea(new Rect(area.x + 4, area.y + 4, area.width - 8, area.height - 8));
            try
            {
            float scrollYBefore = _scroll.y;
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            try
            {
            // 用户手动拖拽滚动条时取消动画,避免 Lerp 与手动滚动互相覆盖闪烁
            if (_paramScrollAnimating && Mathf.Abs(_scroll.y - scrollYBefore) > 0.5f)
            {
                _paramScrollAnimating = false;
            }

            switch (_mode)
            {
                case Mode.NewSkill:
                    DrawStepNav();
                    switch (_step)
                    {
                        case 1: DrawStep1(); break;
                        case 2: DrawStep2(); break;
                        case 3: DrawStep3(); break;
                    }
                    break;
                case Mode.EditExisting:
                    DrawEditExisting();
                    break;
                case Mode.CharacterLibrary:
                    DrawCharacterLibrary();
                    break;
            }
            }
            finally { EditorGUILayout.EndScrollView(); }
            }
            finally { GUILayout.EndArea(); }
        }

        void DrawStepNav()
        {
            EditorGUILayout.BeginHorizontal();
            for (int i = 1; i <= 3; i++)
            {
                if (GUILayout.Toggle(_step == i, $"Step {i}", "Button", GUILayout.Height(24)))
                    _step = i;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6);
        }

        // ═══════════════════════════════════════════════════════════════
        // 底:Timeline 时间轴窗口(独立面板,接收外部 viewport rect)
        // —— 仿 Unity 内置 Timeline 编辑器布局:
        //   ┌────────────────────────────────────────────────┐
        //   │ Preview | ⏮ ⏪ ▶ ⏩ ⏭ | [frame] 0  Skill Timeline⋮│  ← Header
        //   ├──────────┬─────────────────────────────────────┤
        //   │ Layers    │  Ruler: 0  30  60  90  120  ...      │
        //   │ (眼/锁)   │  [轨道头 + 标尺] ← 16px              │
        //   │          │  ━━━━━ 节点 bar ━━━━━━ (Playhead)    │
        //   │          │                                       │
        //   └──────────┴─────────────────────────────────────┘
        // ═══════════════════════════════════════════════════════════════
        void DrawTimelinePanel(Rect area)
        {
            // 防御:任何在 OnGUI 头调 EnsureLayerStates 之后又被修改的路径(Undo 回调 / 资产变更)
            // 都在面板入口再对齐一次,杜绝子面板读取时长度错位
            EnsureLayerStates();
            EditorGUI.DrawRect(area, UI.BgTrack);

            // ── Timeline Header / 轨道头 高度(与 Unity Timeline Editor 对齐)─
            // V3.1.3 修复:两行合并成一行 28px — 左侧 4 键+速度,居中标题,右侧 FPS/Snap/Frame/缩放
            const float HEADER_H = UI.HdrH;          // 28
            const float TRACK_HEAD_H = UI.TrackHdrH;

            var headerRect = new Rect(area.x, area.y, area.width, HEADER_H);
            DrawTimelineHeader(headerRect);

            // Layers 列宽:可拖拽(替代旧硬编码 110f),窄窗口自动夹紧不溢出
            _layersW = Mathf.Clamp(_layersW, LAYERS_W_MIN, Mathf.Max(LAYERS_W_MIN, area.width - TRACKS_W_MIN));
            float layersW = _layersW;
            // 留出 Layers 列 + 轨道头
            var bodyTop = area.y + HEADER_H;
            var bodyH = area.height - HEADER_H;

            // Layers 头(Layers 文字、列高信息)与轨道头对齐:在 Layers 列顶 16px 内画"Layers"
            var layersHeaderRect = new Rect(area.x, bodyTop, layersW, TRACK_HEAD_H);
            EditorGUI.DrawRect(layersHeaderRect, UI.BgHeader);
            var layersHdrStyle = UI.SubTitleStyle();
            GUI.Label(new Rect(layersHeaderRect.x + 4, layersHeaderRect.y + 1, layersHeaderRect.width - 30, layersHeaderRect.height - 1), "Layers", layersHdrStyle);
            // + 按钮:弹菜单三选一(节点 / 动画片段 / 多段)
            if (GUI.Button(new Rect(layersHeaderRect.xMax - 22, layersHeaderRect.y + 1, 20, layersHeaderRect.height - 2), "+", EditorStyles.miniButton))
            {
                ShowAddLayerMenu();
            }

            // ── 搜索过滤 + 批量操作行 ────────────────────────────────────────
            const float searchRowH = 22f;
            var searchRowRect = new Rect(area.x, bodyTop + TRACK_HEAD_H, layersW, searchRowH);
            EditorGUI.DrawRect(searchRowRect, new Color(UI.BgLayerRow.r * 0.95f, UI.BgLayerRow.g * 0.95f, UI.BgLayerRow.b * 0.95f));
            // 搜索框(左侧)
            var searchFieldRect = new Rect(searchRowRect.x + 2f, searchRowRect.y + 2f, searchRowRect.width - 44f, searchRowRect.height - 4f);
            var oldSearch = _layerSearchFilter;
            _layerSearchFilter = EditorGUI.TextField(searchFieldRect, _layerSearchFilter, EditorStyles.toolbarSearchField);
            if (oldSearch != _layerSearchFilter) { Repaint(); }
            // 批量选择按钮(右侧)
            int totalNodes = (_editingTarget != null && _editingTarget.graphData != null) ? _editingTarget.graphData.Count : 0;
            bool allSelected = totalNodes > 0 && _multiSelectedIndices.Count >= totalNodes;
            var selBtnRect = new Rect(searchRowRect.xMax - 42f, searchRowRect.y + 1f, 20f, searchRowRect.height - 2f);
            if (GUI.Button(selBtnRect, allSelected ? "∅" : "☐", EditorStyles.miniButton))
            {
                _multiSelectedIndices.Clear();
                if (!allSelected)
                {
                    for (int i = 0; i < totalNodes; i++) _multiSelectedIndices.Add(i);
                }
                Repaint();
            }
            if (_multiSelectedIndices.Count > 0)
            {
                var delBtnRect = new Rect(searchRowRect.xMax - 21f, searchRowRect.y + 1f, 19f, searchRowRect.height - 2f);
                if (GUI.Button(delBtnRect, "✕", EditorStyles.miniButton))
                {
                    if (EditorUtility.DisplayDialog("批量删除",
                        $"确定删除选中的 {_multiSelectedIndices.Count} 个节点?", "删除", "取消"))
                    {
                        var sorted = new List<int>(_multiSelectedIndices);
                        sorted.Sort((a, b) => b.CompareTo(a));
                        foreach (int idx in sorted) DeleteNode(idx);
                        _multiSelectedIndices.Clear();
                    }
                }
            }

            var layersRect = new Rect(area.x, bodyTop + TRACK_HEAD_H + searchRowH, layersW, bodyH - TRACK_HEAD_H - searchRowH);
            var tracksHeaderRect = new Rect(area.x + layersW, bodyTop, area.width - layersW, TRACK_HEAD_H);
            var tracksRect = new Rect(area.x + layersW, bodyTop + TRACK_HEAD_H, area.width - layersW, bodyH - TRACK_HEAD_H);

            if (_mode == Mode.EditExisting && _editingTarget != null)
            {
                DrawLayersColumn(layersRect);
                DrawTimelineTracks(tracksHeaderRect, tracksRect);
                // Layers | 轨道区 分隔条(可拖调 Layers 列宽,盖在两列交界处最上层)
                DrawLayersSplitHandle(new Rect(area.x + layersW - 2f, bodyTop, 4f, bodyH));
            }
            else
            {
                GUI.Label(area, "请先在 ✏️ 编辑现有 模式加载一个 SkillData", s_placeholderTipStyle);
            }
        }

        // ── Layers 列宽拖拽分隔条(仿 DrawVSplitHandle,窄条竖线 + hotControl 抢占)──
        void DrawLayersSplitHandle(Rect r)
        {
            EditorGUI.DrawRect(new Rect(r.x + r.width * 0.5f - 0.5f, r.y, 1f, r.height),
                new Color(UI.SplitLine.r, UI.SplitLine.g, UI.SplitLine.b, _draggingLayersSplit ? 0.9f : 0.25f));
            EditorGUIUtility.AddCursorRect(r, MouseCursor.SplitResizeLeftRight);

            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
            {
                _draggingLayersSplit = true;
                _splitDragStartX = e.mousePosition.x;
                _splitDragStartVal = _layersW;
                GUIUtility.hotControl = _layersSplitCtrlId;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == _layersSplitCtrlId && _draggingLayersSplit)
            {
                float delta = e.mousePosition.x - _splitDragStartX;
                _layersW = _splitDragStartVal + delta;
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseUp && GUIUtility.hotControl == _layersSplitCtrlId)
            {
                _draggingLayersSplit = false;
                GUIUtility.hotControl = 0;
                SaveLayoutPrefs();
                e.Use();
            }
        }

    }
}
#endif
