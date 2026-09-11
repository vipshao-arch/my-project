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

        // ═══════════════════════════════════════════════════════════════
        // Timeline Header —— 顶行(标题 / FPS / Snap / 缩放)+ 底行(5 键 / 速度 / Frame)
        // V3.1 修复:把原 DrawPreviewPanel 底行 5 键搬到 Header 底行,让播放控件紧贴轨道上方,
        // 符合 Unity Timeline Editor 习惯;同时删除原 DrawPreviewPanel 残留的 IconPlay/IconPause
        // + Stop + Scrub slider 第三套控制源,统一控制权。
        // ═══════════════════════════════════════════════════════════════
        // V3.1.3:两行合并成一行 — 左侧 4 键+速度,居中标题,右侧 Frame/FPS/Snap/F/Fit/缩放
        //  布局(从左到右,单行 28px):
        //   [⏮][▶/⏸][⏭][■][×▾]  ...标题居中...  [F:xx T:xx][FPS▾][Sn●/Sn○][F][↔][━][+][−]
        void DrawTimelineHeader(Rect r)
        {
            EditorGUI.DrawRect(r, UI.BgTimeline);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), UI.Divider);

            float pad = 3f;
            float h = 22f;
            float y = r.y + (r.height - h) * 0.5f;
            float x = r.x + pad;

            // ── 左侧:4 键播放 + 速度下拉 ──
            float btnW = 36f;
            float speedLabelW = 14f;
            float speedDropW = 40f;
            float leftBlockW = btnW * 4f + 2f + speedLabelW + speedDropW;

            // |< 上一帧 (播放中先暂停再步进,确保逐帧可控)
            if (GUI.Button(new Rect(x, y, btnW, h), "|<"))
            {
                if (_previewPlaying) PausePreviewPlayback();
                StepPreviewFrame(-1);
            }
            // > / ||
            string playLabel = _previewPlaying ? "||" : ">";
            if (GUI.Button(new Rect(x + btnW, y, btnW, h), playLabel))
            {
                if (_previewPlaying) PausePreviewPlayback();
                else StartPreviewPlayback();
                Repaint();
            }
            // >| (播放中先暂停再步进,确保逐帧可控)
            if (GUI.Button(new Rect(x + btnW * 2f, y, btnW, h), ">|"))
            {
                if (_previewPlaying) PausePreviewPlayback();
                StepPreviewFrame(+1);
            }
            // []
            if (GUI.Button(new Rect(x + btnW * 3f, y, btnW, h), "[]")) { StopPreviewPlayback(); Repaint(); }
            // x速度
            float spX = x + btnW * 4f + 2f;
            GUI.Label(new Rect(spX, y + 4, speedLabelW, 14), "x", EditorStyles.miniLabel);
            string[] speedLabels = { "0.25", "0.5", "1", "1.5", "2" };
            float[] speedValues = { 0.25f, 0.5f, 1f, 1.5f, 2f };
            int curSpeedIdx = 2;
            for (int i = 0; i < speedValues.Length; i++)
                if (Mathf.Approximately(speedValues[i], _previewPlaySpeed)) { curSpeedIdx = i; break; }
            int newSpeedIdx = EditorGUI.Popup(new Rect(spX + speedLabelW, y, speedDropW, h), curSpeedIdx, speedLabels);
            if (newSpeedIdx != curSpeedIdx)
            {
                _previewPlaySpeed = speedValues[newSpeedIdx];
                if (_previewActor != null) _previewActor.speed = _previewPlaySpeed;
                Repaint();
            }

            // P2-1:循环/单次 切换按钮(紧接速度下拉右侧)
            float loopBtnX = spX + speedLabelW + speedDropW + 3f;
            float loopBtnW = 36f;
            var loopBg = GUI.backgroundColor;
            GUI.backgroundColor = _previewLoopMode ? new Color(0.4f, 0.6f, 0.9f) : new Color(0.55f, 0.55f, 0.55f);
            if (GUI.Button(new Rect(loopBtnX, y, loopBtnW, h),
                new GUIContent(_previewLoopMode ? "↺" : "→|", _previewLoopMode ? "当前:循环播放(点击切换为单次)" : "当前:单次播放(点击切换为循环)"),
                EditorStyles.miniButton))
            {
                _previewLoopMode = !_previewLoopMode;
                Repaint();
            }
            GUI.backgroundColor = loopBg;
            leftBlockW += loopBtnW + 3f;

            // ── 右侧控件总宽 ──
            float frameW = 58f, fpsW = 38f, snapW = 30f, fitW = 22f, animFitW = 38f, zoomSliderW = 42f, zoomBtnW = 16f;
            // 右侧宽度必须包含 Anim 按钮，否则 rx 会继续向右溢出，导致按钮文字被裁切。
            float rightW = frameW + 2 + fpsW + 2 + snapW + 2 + 18 + 2 + fitW + 2 + animFitW + 2 + zoomSliderW + 2 + zoomBtnW + 1 + zoomBtnW + pad;
            float rx = r.xMax - rightW;

            // ── 标题(居中) ──
            float titleX = x + leftBlockW + 4;
            float titleMaxW = rx - titleX - 6;
            float titleW = Mathf.Clamp(titleMaxW, 0f, 180f);
            if (titleW > 30f)
            {
                var titleRect = new Rect(titleX + Mathf.Max(0, (titleMaxW - titleW) * 0.5f), y, titleW, h);
                string title = $"{_timelineTitle} ({(_editingTarget != null ? _editingTarget.name : "<none>")})";
                if (GUI.Button(titleRect, title, s_timelineTitleStyle))
                    ShowTimelineTitleMenu();
                if (titleRect.xMax + 20 < rx && GUI.Button(new Rect(titleRect.xMax + 1, y, 18, h), "⋮", EditorStyles.miniButton))
                    ShowTimelineTitleMenu();
            }

            // ── 右侧:Frame / FPS / Snap / F / Fit / 缩放 ──
            int curF = _previewRuntime != null ? _previewCurFrame : 0;
            float curT = _previewLinkTimeline ? _playheadT : _previewTime;
            GUI.Label(new Rect(rx, y + 4, frameW, 14), $"F:{curF} T:{curT:0.00}", s_frameInfoStyle);
            rx += frameW + 2;

            // FPS 下拉
            int fpsIndex = _frameRate == 12f ? 0 : _frameRate == 24f ? 1 : _frameRate == 30f ? 2 : _frameRate == 60f ? 3 : 4;
            string[] fpsLabels = { "12", "24", "30", "60", ".." };
            var newFpsIdx = EditorGUI.Popup(new Rect(rx, y, fpsW, h), fpsIndex, fpsLabels, EditorStyles.popup);
            if (newFpsIdx != fpsIndex)
            {
                _frameRate = newFpsIdx switch { 0 => 12f, 1 => 24f, 2 => 30f, 3 => 60f, _ => _frameRate };
                if (newFpsIdx == 4)
                {
                    string input = EditorInputDialog.Show("自定义帧率", "输入帧率 (fps):", _frameRate.ToString("F0"));
                    if (float.TryParse(input, out float parsed) && parsed >= 1f && parsed <= 120f)
                        _frameRate = parsed;
                }
                Repaint();
            }
            rx += fpsW + 2;

            // Snap
            var snapBg = GUI.backgroundColor;
            GUI.backgroundColor = _snapToFrame ? UI.Snap : Color.gray;
            if (GUI.Button(new Rect(rx, y, snapW, h), new GUIContent(_snapToFrame ? "Sn" : "Sn", _snapToFrame ? "吸附到帧: 开" : "吸附到帧: 关"), EditorStyles.miniButton))
            {
                _snapToFrame = !_snapToFrame;
                Repaint();
            }
            GUI.backgroundColor = snapBg;
            rx += snapW + 2;

            // F(跳到选中节点)
            if (GUI.Button(new Rect(rx, y, 18, h), "F", EditorStyles.miniButton))
                GotoSelectedNodeTrigger();
            rx += 20;

            // Fit All：适配当前时间轴所有节点内容，不额外添加人为边距。
            if (GUI.Button(new Rect(rx, y, fitW, h), new GUIContent("↔", "适配全部节点内容"), EditorStyles.miniButton))
                FitTimelineToContent();
            rx += fitW + 2;

            // Anim：以当前技能动画片段长度作为时间轴范围
            if (GUI.Button(new Rect(rx, y, animFitW, h), new GUIContent("Anim", "按当前 SkillData 的动画片段长度适配时间轴"), EditorStyles.miniButton))
                FitTimelineToAnimation();
            rx += animFitW + 2;

            // 缩放条
            float ppsNorm = Mathf.InverseLerp(PPS_MIN, PPS_MAX, _pixelsPerSecond);
            var sliderRect = new Rect(rx, y, zoomSliderW, h);
            EditorGUIUtility.AddCursorRect(sliderRect, MouseCursor.SlideArrow);
            var newPpsNorm = GUI.HorizontalSlider(sliderRect, ppsNorm, 0f, 1f);
            if (!Mathf.Approximately(newPpsNorm, ppsNorm))
            {
                float newPps = Mathf.Lerp(PPS_MIN, PPS_MAX, newPpsNorm);
                float anchorT = LocalXToTime(_lastTracksAreaW * 0.5f);
                _pixelsPerSecond = newPps;
                _scrollX = anchorT * _pixelsPerSecond - _lastTracksAreaW * 0.5f;
                ClampScrollX(_lastTracksAreaW);
                Repaint();
            }
            rx += zoomSliderW + 2;
            if (GUI.Button(new Rect(rx, y, zoomBtnW, h), "+", EditorStyles.miniButton))
                ZoomAtAnchor(_lastTracksAreaW * 0.5f, 1.25f);
            rx += zoomBtnW + 1;
            if (GUI.Button(new Rect(rx, y, zoomBtnW, h), "−", EditorStyles.miniButton))
                ZoomAtAnchor(_lastTracksAreaW * 0.5f, 0.8f);
        }

        // V3.1.1 新增:从播放状态切到暂停 — 不动时间游标(Resume 时从当前位置继续)
        //  跟 StopPreviewPlayback 区别:Stop 重置 t=0 + 清空 Runtime 池;Pause 只翻 _previewPlaying=false。
        //  TickPreviewRuntime 已用 _previewPlaying 闸门守护,这样 Animator 路径 / 联动模式 都立刻停。
        void PausePreviewPlayback()
        {
            _previewPlaying = false;
            // 重置 dt 基线,避免 Resume 时第一帧 dt 跳巨大值
            _previewLastEditorTime = EditorApplication.timeSinceStartup;
            _previewLastTickTime = EditorApplication.timeSinceStartup;
        }

        // V3.1 修复:StepPlayhead 改名 SeekPlayhead — 语义只改 _playheadT,不动播放状态。
        // 仍被键盘 ←/→ 用来跳帧做 timeline 节点精确定位(底行 5 键的"上一帧/下一帧"是
        // StepPreviewFrame,改的是 _previewTime / Runtime 池,跟 _playheadT 不直接关联)。
        // V3.1.8 修复:写完 _playheadT 后 ResyncPreviewToPlayhead —— 拖动 ruler / 键盘跳帧
        //  也要即时驱动 Runtime 触发/清理区间节点(否则预览窗口永远停在原地)。
        void SeekPlayhead(int dir)
        {
            float newT;
            if (_snapToFrame)
            {
                int curFrame = Mathf.RoundToInt(_playheadT * _frameRate);
                curFrame += dir;
                newT = Mathf.Clamp(curFrame / _frameRate, 0f, _timelineMaxT);
            }
            else
            {
                newT = Mathf.Clamp(_playheadT + dir * (1f / _frameRate), 0f, _timelineMaxT);
            }
            ResyncPreviewToPlayhead(newT);
        }

        // ═══════════════════════════════════════════════════════════
        // 标尺自适应粒度算法
        // ═══════════════════════════════════════════════════════════
        static float CalcRulerInterval(float pps, float minSpacePx)
        {
            float[] candidates = { 0.001f, 0.005f, 0.01f, 0.025f, 0.05f, 0.1f,
                                    0.25f, 0.5f, 1f, 2f, 5f, 10f, 30f };
            foreach (var c in candidates)
                if (c * pps >= minSpacePx) return c;
            return 30f;
        }

        // ═══════════════════════════════════════════════════════════
        // TimelineCoord —— 坐标转换(独立 pps + scrollX,对齐 Unity Timeline)
        // 时间→像素相对 tracksRect.x 的偏移;像素→时间同理
        // ═══════════════════════════════════════════════════════════
        const float PPS_MIN = 10f;
        const float PPS_MAX = 4000f;

        float TimeToLocalX(float t) => t * _pixelsPerSecond - _scrollX;
        float LocalXToTime(float x) => Mathf.Max(0f, (x + _scrollX) / _pixelsPerSecond);

        // 内容总宽度(像素)——用于横向滚动条范围
        float GetContentWidthPx()
        {
            float maxT = Mathf.Max(_timelineMaxT, 0.5f);
            return maxT * _pixelsPerSecond;
        }

        // 夹紧 scrollX,防止滚出内容范围(左侧不能滚到负,右侧最多滚到内容末尾)
        void ClampScrollX(float viewportW)
        {
            float contentW = GetContentWidthPx();
            float maxScroll = Mathf.Max(0f, contentW - viewportW + 40f);
            _scrollX = Mathf.Clamp(_scrollX, 0f, maxScroll);
        }

        // 以锚点像素位置为中心缩放(锚点对应的时间在缩放前后保持不变,Unity Timeline 标准缩放行为)
        void ZoomAtAnchor(float anchorLocalX, float zoomFactor)
        {
            float anchorTime = LocalXToTime(anchorLocalX);
            _pixelsPerSecond = Mathf.Clamp(_pixelsPerSecond * zoomFactor, PPS_MIN, PPS_MAX);
            _scrollX = anchorTime * _pixelsPerSecond - anchorLocalX;
            if (_scrollX < 0f) _scrollX = 0f;
        }

        // ═══════════════════════════════════════════════════════════
        //  P1-1 工具条配套:自适应 + 跳到选中节点
        // ═══════════════════════════════════════════════════════════

        // Fit All(方案 §4):把全部节点内容适配到当前轨道可视宽度,调整 pps + 归零 scrollX
        // 没节点时退化到 castTime * 1.5 或 2s 的默认缩放
        void FitTimelineToContent()
        {
            float viewportW = Mathf.Max(50f, _lastTracksAreaW);
            if (_editingTarget == null || _editingTarget.graphData == null || _editingTarget.graphData.Count == 0)
            {
                _timelineMaxT = Mathf.Max(0.5f, _editingTarget != null && _editingTarget.castTime > 0
                    ? _editingTarget.castTime * 1.5f : 2f);
                _pixelsPerSecond = Mathf.Clamp(viewportW / _timelineMaxT, PPS_MIN, PPS_MAX);
                _scrollX = 0f;
                Repaint();
                return;
            }
            float rightMost = 0f;
            for (int i = 0; i < _editingTarget.graphData.Count; i++)
            {
                var d = _editingTarget.graphData[i];
                if (d == null) continue;
                float tt = GetNodeTriggerTime(d);
                float dur = GetNodeDuration(d);
                float end = tt + Mathf.Max(0f, dur);
                if (end > rightMost) rightMost = end;
            }
            // Fit All 的时间范围严格截止到最后一个节点内容结束时间。
            // 视口若需要留白，只通过缩放/视图绘制处理，不修改时间轴语义时长。
            _timelineMaxT = Mathf.Clamp(rightMost, 0.5f, 3000f);
            _pixelsPerSecond = Mathf.Clamp(viewportW / _timelineMaxT, PPS_MIN, PPS_MAX);
            _scrollX = 0f;
            Repaint();
        }

        // 按当前 SkillData 的动画片段长度适配时间轴。
        // 与 FitTimelineToContent 的区别：Fit 只看 graphData 节点，空节点/节点时长为 0
        // 时会把时间轴压到默认 0.5s；Anim 直接读取 animClips、动画层节点和多段节点。
        void FitTimelineToAnimation()
        {
            var data = GetActivePreviewData();
            if (data == null)
            {
                ShowNotification(new GUIContent("没有当前 SkillData"));
                return;
            }

            float animationLength = 0f;
            if (data.animClips != null)
            {
                foreach (var clip in data.animClips)
                    if (clip != null) animationLength = Mathf.Max(animationLength, clip.length);
            }
            if (data.graphData != null)
            {
                foreach (var node in data.graphData)
                {
                    if (node == null) continue;
                    var field = node.GetType().GetField("animClip",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.IgnoreCase);
                    var clip = field != null ? field.GetValue(node) as AnimationClip : null;
                    if (clip != null)
                        animationLength = Mathf.Max(animationLength, GetNodeTriggerTime(node) + clip.length);
                }
            }

            if (animationLength <= 0f)
            {
                ShowNotification(new GUIContent("当前 SkillData 没有有效 AnimationClip"));
                return;
            }

            // 动画适配严格使用 AnimationClip.length，不额外添加时间边距。
            _timelineMaxT = Mathf.Clamp(animationLength, 0.5f, 3000f);
            _pixelsPerSecond = Mathf.Clamp(_lastTracksAreaW / _timelineMaxT, PPS_MIN, PPS_MAX);
            _scrollX = 0f;
            _playheadT = Mathf.Clamp(_playheadT, 0f, animationLength);
            AutoSelectClipForPlayhead();
            Repaint();
        }

        // 跳到第一个被选中节点的 triggerTime,并保持在可视区(不改变缩放,仅平移 scrollX)
        void GotoSelectedNodeTrigger()
        {
            if (_editingTarget == null || _editingTarget.graphData == null) return;
            if (_selectedTimelineIndex < 0 || _selectedTimelineIndex >= _editingTarget.graphData.Count) return;
            var d = _editingTarget.graphData[_selectedTimelineIndex];
            if (d == null) return;
            float tt = GetNodeTriggerTime(d);
            _playheadT = Mathf.Max(0f, tt);
            // 保证选中节点在可视区居中
            float viewportW = Mathf.Max(50f, _lastTracksAreaW);
            _scrollX = Mathf.Max(0f, tt * _pixelsPerSecond - viewportW * 0.5f);
            Repaint();
        }

        // 标题菜单(Unity Timeline 风格:可改名 / 帧率 / 吸附)
        void ShowTimelineTitleMenu()
        {
            var menu = new GenericMenu();
            string oldName = _timelineTitle;
            menu.AddItem(new GUIContent("重命名 Timeline…"), false, () => {
                // 简易:在 Inspector 改更靠谱,这里先弹 DisplayDialog
                string input = EditorInputDialog.Show("重命名 Timeline", "新名称:", oldName);
                if (!string.IsNullOrEmpty(input)) { _timelineTitle = input; Repaint(); }
            });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("吸附到 Frame"), _snapToFrame, () => { _snapToFrame = !_snapToFrame; Repaint(); });
            menu.AddItem(new GUIContent("显示标尺网格"), _showRulerGrid, () => { _showRulerGrid = !_showRulerGrid; Repaint(); });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("帧率 30"), false, () => { _frameRate = 30f; Repaint(); });
            menu.AddItem(new GUIContent("帧率 60"), false, () => { _frameRate = 60f; Repaint(); });
            menu.AddItem(new GUIContent("帧率 24"), false, () => { _frameRate = 24f; Repaint(); });
            menu.ShowAsContext();
        }

        // ── Layers 列(眼/锁/重命名/上下移)────────────────────────────
        // Layers 列 —— Unity Timeline 风格:每个节点添加一层(行号 = 节点索引)
        // 行点击 -> 选中节点 + 展开右侧参数面板
        void DrawLayersColumn(Rect rect)
        {
            EnsureLayerStates();
            EditorGUI.DrawRect(rect, UI.BgLayerRow);

            int n = (_editingTarget != null && _editingTarget.graphData != null) ? _editingTarget.graphData.Count : 0;
            int safeN = Mathf.Min(n, _layerStates != null ? _layerStates.Length : 0);
            float rowH = LAYER_ROW_H;
            float contentH = Mathf.Max(rect.height, GetGroupedContentHeight(n));
            float innerW = rect.width - 14f;

            _layersScroll = GUI.BeginScrollView(rect, _layersScroll,
                new Rect(0, 0, innerW, contentH), false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            try
            {
                var groups = BuildCategoryGroups();
                float curY = 0f;

                foreach (var g in groups)
                {
                    // ── Phase 2:分组头(可折叠)──────────────────────────
                    var grpRect = new Rect(0, curY, innerW, GROUP_HEADER_H);
                    var catCol = CategoryColor(g.category);
                    bool collapsed = g.category >= 0 && g.category < _categoryCollapsed.Length && _categoryCollapsed[g.category];

                    // 分组头背景(比行底深 10%)
                    EditorGUI.DrawRect(grpRect, new Color(UI.BgTrackHead.r * 0.85f, UI.BgTrackHead.g * 0.85f, UI.BgTrackHead.b * 0.85f, 1f));
                    // 左侧 3px 类目色条
                    EditorGUI.DrawRect(new Rect(grpRect.x, grpRect.y, 3f, grpRect.height), catCol);

                    // 折叠箭头
                    var arrowRect = new Rect(grpRect.x + 6f, grpRect.y, 14f, GROUP_HEADER_H);
                    s_groupArrowStyleBase.normal.textColor = catCol;
                    GUI.Label(arrowRect, collapsed ? "▶" : "▼", s_groupArrowStyleBase);

                    // 分组名 + 计数
                    string grpName = g.category < CategoryNames.Length ? CategoryNames[g.category] : $"Category {g.category}";
                    var nameRect = new Rect(grpRect.x + 22f, grpRect.y, innerW - 22f - 4f, GROUP_HEADER_H);
                    s_groupNameStyleBase.normal.textColor = new Color(catCol.r * 0.8f, catCol.g * 0.8f, catCol.b * 0.8f);
                    GUI.Label(nameRect, $"{grpName}  ({g.nodeIndices.Count})", s_groupNameStyleBase);

                    // 点击分组头 → 切换折叠/展开
                    var eGrp = Event.current;
                    if (eGrp.type == EventType.MouseDown && eGrp.button == 0 && grpRect.Contains(eGrp.mousePosition))
                    {
                        if (g.category >= 0 && g.category < _categoryCollapsed.Length)
                        {
                            _categoryCollapsed[g.category] = !_categoryCollapsed[g.category];
                            eGrp.Use();
                            Repaint();
                        }
                    }
                    EditorGUIUtility.AddCursorRect(grpRect, MouseCursor.Arrow);

                    curY += GROUP_HEADER_H;

                    if (collapsed) continue;

                    // ── 该组下的节点行 ──────────────────────────────────
                    foreach (int i in g.nodeIndices)
                    {
                        if (i >= safeN) break;
                        var data = _editingTarget.graphData[i];
                        var state = _layerStates[i];

                        // 搜索过滤：空格分词，多关键词 AND 匹配，不匹配则跳过（不占行高）
                        if (!string.IsNullOrEmpty(_layerSearchFilter))
                        {
                            string nodeName = (state?.customName ?? data?.DisplayName ?? "Unnamed");
                            bool match = true;
                            var keywords = _layerSearchFilter.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                            for (int k = 0; k < keywords.Length; k++)
                            {
                                if (nodeName.IndexOf(keywords[k], System.StringComparison.OrdinalIgnoreCase) < 0)
                                { match = false; break; }
                            }
                            if (!match) continue;
                        }

                        var col = catCol;
                        var rowRect = new Rect(0, curY, innerW, rowH);

                        bool isDragSource = _draggingLayerRow && i == _dragLayerFromIndex;
                        var bg = (i % 2 == 0) ? UI.BgRowAlt1 : UI.BgRowAlt2;
                        bool rowSelected = i == _selectedTimelineIndex;
                        if (rowSelected)
                            bg = new Color(col.r * 0.32f, col.g * 0.32f, col.b * 0.32f, 1f);
                        if (isDragSource) bg = new Color(bg.r, bg.g, bg.b, bg.a * 0.4f);
                        EditorGUI.DrawRect(rowRect, bg);
                        EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, rowRect.width, 1f),
                            new Color(col.r, col.g, col.b, 0.7f));
                        if (rowSelected)
                            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y + 1f, 3f, rowRect.height - 2f), col);

                        // 拖拽把手
                        var gripRect = new Rect(rowRect.x, rowRect.y, 10f, rowH);
                        GUI.Label(gripRect, "≡", s_gripStyle);
                        EditorGUIUtility.AddCursorRect(gripRect, MouseCursor.MoveArrow);

                        float bx = rowRect.x + 12;
                        float by = rowRect.y + 1f;
                        float btnH = rowH - 2f;

                        var dotRect = new Rect(bx, by, 12, btnH);
                        EditorGUI.DrawRect(new Rect(dotRect.x + 2, dotRect.y + 2, 8, btnH - 4), col);
                        bx += 14;

                        string typeName = data.GetType().Name;
                        string display = data.DisplayName;
                        if (string.IsNullOrEmpty(display) || display == typeName) display = typeName;
                        string nm = state.customName ?? display;
                        s_layerNameStyleBase.normal.textColor = state.visible ? col : new Color(0.4f, 0.4f, 0.4f);
                        var nameStyle = s_layerNameStyleBase;
                        float ebW = 18f;
                        var nmRect = new Rect(bx, rowRect.y, rowRect.width - 12 - 14 - ebW * 3 - 6, rowH);
                        GUI.Label(nmRect, nm, nameStyle);

                        float ebX = rowRect.xMax - ebW * 3 - 2;
                        if (GUI.Button(new Rect(ebX, by, ebW, btnH), state.visible ? IconVisibleOn : IconVisibleOff, EditorStyles.miniButton))
                        { state.visible = !state.visible; Repaint(); }
                        if (GUI.Button(new Rect(ebX + ebW, by, ebW, btnH), state.locked ? IconLockOn : IconLockOff, EditorStyles.miniButton))
                        { state.locked = !state.locked; Repaint(); }
                        if (GUI.Button(new Rect(ebX + ebW * 2, by, ebW, btnH), "⋮", EditorStyles.miniButton))
                        {
                            int captured = i;
                            var menu = new GenericMenu();
                            menu.AddItem(new GUIContent("重命名…"), false, () => {
                                string input = EditorInputDialog.Show("重命名节点层", "新名称:",
                                    _layerStates[captured].customName ?? _editingTarget.graphData[captured].DisplayName ?? _editingTarget.graphData[captured].GetType().Name);
                                if (!string.IsNullOrEmpty(input)) { _layerStates[captured].customName = input; Repaint(); }
                            });
                            menu.AddItem(new GUIContent("重置为默认名"), false, () => { _layerStates[captured].customName = null; Repaint(); });
                            menu.AddSeparator("");
                            menu.AddItem(new GUIContent("上移"), false, () => MoveNode(captured, -1));
                            menu.AddItem(new GUIContent("下移"), false, () => MoveNode(captured, +1));
                            menu.AddSeparator("");
                            menu.AddItem(new GUIContent("删除"), false, () => DeleteNode(captured));
                            menu.ShowAsContext();
                        }

                        var eGrip = Event.current;
                        if (eGrip.type == EventType.MouseDown && eGrip.button == 0 && gripRect.Contains(eGrip.mousePosition))
                        {
                            _draggingLayerRow = true;
                            _dragLayerFromIndex = i;
                            _dragLayerHoverIndex = i;
                            GUIUtility.hotControl = _layersSplitCtrlId + 1;
                            eGrip.Use();
                        }

                        var eRow = Event.current;
                        if (!_draggingLayerRow && eRow.type == EventType.MouseDown && eRow.button == 0
                            && rowRect.Contains(eRow.mousePosition) && !gripRect.Contains(eRow.mousePosition))
                        {
                            if (eRow.control || eRow.command)
                            {
                                if (_multiSelectedIndices.Contains(i)) _multiSelectedIndices.Remove(i);
                                else _multiSelectedIndices.Add(i);
                            }
                            else
                            {
                                _multiSelectedIndices.Clear();
                            }
                            _selectedTimelineIndex = i;
                            _expandedNodeIndex = i;
                            _paramScrollTarget    = i * 140f;
                            _paramScrollAnimating = true;
                            ScrollLayersToRow(i);
                            eRow.Use();
                            Repaint();
                        }

                        if (_draggingLayerRow && GUIUtility.hotControl == _layersSplitCtrlId + 1)
                        {
                            if (eRow.type == EventType.MouseDrag)
                            {
                                _dragLayerHoverIndex = Mathf.Clamp(Mathf.FloorToInt(eRow.mousePosition.y / rowH), 0, Mathf.Max(0, safeN - 1));
                                eRow.Use();
                                Repaint();
                            }
                            else if (eRow.type == EventType.MouseUp)
                            {
                                if (_dragLayerFromIndex >= 0 && _dragLayerHoverIndex >= 0 && _dragLayerFromIndex != _dragLayerHoverIndex)
                                    ReorderNode(_dragLayerFromIndex, _dragLayerHoverIndex);
                                _draggingLayerRow = false;
                                _dragLayerFromIndex = -1;
                                _dragLayerHoverIndex = -1;
                                GUIUtility.hotControl = 0;
                                eRow.Use();
                                Repaint();
                            }
                        }

                        curY += rowH;
                    }
                }

                // 插入指示线
                if (_draggingLayerRow && _dragLayerHoverIndex >= 0)
                {
                    float lineY = _dragLayerHoverIndex * rowH + (_dragLayerHoverIndex > _dragLayerFromIndex ? rowH : 0f);
                    EditorGUI.DrawRect(new Rect(0, lineY - 1f, innerW, 2f), UI.Highlight);
                }
            }
            finally { GUI.EndScrollView(); }

            if (n == 0)
            {
                GUI.Label(rect, "(无节点 — 在右侧添加,或拖入 AnimationClip / Prefab)", s_placeholderTipStyle);
            }

            HandleLayersDragAndDrop(rect);
        }

        // 拖拽资源到 Layers 列自动建轨道(方案 §9):
        //   AnimationClip → AnimClipLayerData;含 VFX/ParticleSystem 的 Prefab → CastVFXData
        void HandleLayersDragAndDrop(Rect rect)
        {
            if (_editingTarget == null) return;
            var e = Event.current;
            if (!rect.Contains(e.mousePosition)) return;

            if (e.type == EventType.DragUpdated)
            {
                bool accept = false;
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    if (obj is AnimationClip || obj is GameObject) { accept = true; break; }
                }
                DragAndDrop.visualMode = accept ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                e.Use();
            }
            else if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    if (obj is AnimationClip clip)
                    {
                        AddLayerWithClip(clip);
                    }
                    else if (obj is GameObject go)
                    {
                        AddLayerWithPrefab(go);
                    }
                }
                e.Use();
                GUI.changed = true;
                Repaint();
            }
        }

        // 拖入 AnimationClip → 新建 AnimClipLayerData,triggerTime=0,duration=clip.length
        void AddLayerWithClip(AnimationClip clip)
        {
            if (_editingTarget == null || clip == null) return;
            Undo.RecordObject(_editingTarget, "拖入动画片段建层");
            var so = _editingSO ?? new SerializedObject(_editingTarget);
            so.Update();
            var graphProp = so.FindProperty("graphData");
            int newIndex = graphProp.arraySize;
            graphProp.InsertArrayElementAtIndex(newIndex);
            var newElem = graphProp.GetArrayElementAtIndex(newIndex);
            var layer = new AnimClipLayerData { animClip = clip, triggerTime = 0f };
            newElem.managedReferenceValue = layer;
            so.ApplyModifiedProperties();
            _editingSO = so;
            _selectedTimelineIndex = newIndex;
            _expandedNodeIndex = newIndex;
            ScrollLayersToRow(newIndex);
            EditorUtility.SetDirty(_editingTarget);
        }

        // 拖入 Prefab → 按是否含粒子系统/VFX 组件智能判定为起手 VFX 节点(方案 §9.1)
        void AddLayerWithPrefab(GameObject prefab)
        {
            if (_editingTarget == null || prefab == null) return;
            bool looksLikeVfx = prefab.GetComponentInChildren<ParticleSystem>(true) != null;
            Undo.RecordObject(_editingTarget, "拖入 Prefab 建层");
            var so = _editingSO ?? new SerializedObject(_editingTarget);
            so.Update();
            var graphProp = so.FindProperty("graphData");
            int newIndex = graphProp.arraySize;
            graphProp.InsertArrayElementAtIndex(newIndex);
            var newElem = graphProp.GetArrayElementAtIndex(newIndex);
            if (looksLikeVfx)
            {
                newElem.managedReferenceValue = new CastVFXData { prefab = prefab, editorDuration = 0.3f };
            }
            else
            {
                // 非 VFX 类 Prefab:默认当作召唤物节点(Summon),用户可后续在参数区改类型
                newElem.managedReferenceValue = new SummonData { summonPrefab = prefab };
            }
            so.ApplyModifiedProperties();
            _editingSO = so;
            _selectedTimelineIndex = newIndex;
            _expandedNodeIndex = newIndex;
            ScrollLayersToRow(newIndex);
            EditorUtility.SetDirty(_editingTarget);
        }

        // Layers 行整体上移 / 下移(直接重排 graphData 数组,语义最直观)
        void MoveNode(int index, int dir)
        {
            if (_editingTarget == null || _editingTarget.graphData == null) return;
            int n = _editingTarget.graphData.Count;
            int newIdx = index + dir;
            if (index < 0 || index >= n) return;
            if (newIdx < 0 || newIdx >= n) return;
            Undo.RecordObject(_editingTarget, "重排节点");
            var node = _editingTarget.graphData[index];
            _editingTarget.graphData.RemoveAt(index);
            _editingTarget.graphData.Insert(newIdx, node);
            _selectedTimelineIndex = newIdx;
            _expandedNodeIndex = newIdx;
            EditorUtility.SetDirty(_editingTarget);
            MarkPreviewParamsDirty();
            Repaint();
        }

        // Layers 行拖拽排序:把 from 位置的节点移动到 to 位置(任意跨行插入,不限于 ±1)
        void ReorderNode(int from, int to)
        {
            if (_editingTarget == null || _editingTarget.graphData == null) return;
            FlushWorkingToSource();  // P3:操作前把面板编辑写入 Source
            int n = _editingTarget.graphData.Count;
            if (from < 0 || from >= n || to < 0 || to >= n || from == to) return;
            Undo.RecordObject(_editingTarget, "拖拽重排节点");
            var node = _editingTarget.graphData[from];
            _editingTarget.graphData.RemoveAt(from);
            // 移除 from 后,若目标下标原本在 from 之后,所有下标整体前移 1,需要 -1 才能落在期望的可视行
            int insertAt = to > from ? to - 1 : to;
            insertAt = Mathf.Clamp(insertAt, 0, _editingTarget.graphData.Count);
            _editingTarget.graphData.Insert(insertAt, node);
            _selectedTimelineIndex = insertAt;
            _expandedNodeIndex = insertAt;
            EditorUtility.SetDirty(_editingTarget);
            MarkPreviewParamsDirty();
            Repaint();
        }

        // 复制节点(方案 §3.4):同类型深拷贝所有 public 字段,插入到原节点之后
        void DuplicateNode(int index)
        {
            if (_editingTarget == null || _editingTarget.graphData == null) return;
            FlushWorkingToSource();  // P3:操作前把面板编辑写入 Source
            if (index < 0 || index >= _editingTarget.graphData.Count) return;
            var src = _editingTarget.graphData[index];
            if (src == null) return;
            Undo.RecordObject(_editingTarget, "复制节点");
            var so = _editingSO ?? new SerializedObject(_editingTarget);
            so.Update();
            var graphProp = so.FindProperty("graphData");
            int newIndex = index + 1;
            graphProp.InsertArrayElementAtIndex(newIndex);
            var clone = (SkillNodeData)Activator.CreateInstance(src.GetType());
            foreach (var f in src.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
                f.SetValue(clone, f.GetValue(src));
            graphProp.GetArrayElementAtIndex(newIndex).managedReferenceValue = clone;
            so.ApplyModifiedProperties();
            _editingSO = so;
            _selectedTimelineIndex = newIndex;
            _expandedNodeIndex = newIndex;
            ScrollLayersToRow(newIndex);
            EditorUtility.SetDirty(_editingTarget);
            MarkPreviewParamsDirty();
            Repaint();
        }

        // Clip 右键菜单(方案 §3.4)
        void ShowClipContextMenu(int index)
        {
            if (_editingTarget == null || _editingTarget.graphData == null) return;
            if (index < 0 || index >= _editingTarget.graphData.Count) return;
            var data = _editingTarget.graphData[index];
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("重命名…"), false, () => {
                string input = EditorInputDialog.Show("重命名节点层", "新名称:",
                    (index < _layerStates.Length ? _layerStates[index].customName : null) ?? data.DisplayName ?? data.GetType().Name);
                if (!string.IsNullOrEmpty(input) && index < _layerStates.Length) { _layerStates[index].customName = input; Repaint(); }
            });
            menu.AddItem(new GUIContent("复制节点 (Ctrl+D)"), false, () => DuplicateNode(index));
            menu.AddItem(new GUIContent("删除节点 (Delete)"), false, () => {
                if (EditorUtility.DisplayDialog("删除节点", $"确定删除节点 #{index} ?", "删除", "取消"))
                    DeleteNode(index);
            });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("跳到此 Clip 开始"), false, () => {
                ResyncPreviewToPlayhead(GetNodeTriggerTime(data));  // V3.1.8:同步驱动 Runtime
                Repaint();
            });
            menu.AddItem(new GUIContent("跳到此 Clip 结束"), false, () => {
                ResyncPreviewToPlayhead(GetNodeTriggerTime(data) + GetNodeDuration(data));  // V3.1.8
                Repaint();
            });
            // ── Phase 8.2: 在播放头处分割 ──
            menu.AddSeparator("");
            float tt = GetNodeTriggerTime(data);
            float dur = GetNodeDuration(data);
            bool canSplit = dur > 0f && _playheadT > tt && _playheadT < tt + dur;
            if (canSplit)
                menu.AddItem(new GUIContent("在播放头处分割"), false, () => SplitClipAtPlayhead(index));
            else
                menu.AddDisabledItem(new GUIContent("在播放头处分割 (播放头需在 Clip 内)"));

            // ── Phase 8.3: 批量操作(仅在多选时可用) ──
            if (_multiSelectedIndices.Count >= 2)
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("多选/对齐到播放头"), false, () => AlignSelectedToPlayhead());
                if (_multiSelectedIndices.Count >= 3)
                    menu.AddItem(new GUIContent("多选/等间距排列"), false, () => DistributeSelectedEvenly());
            }
            menu.AddSeparator("");
            bool locked = index < _layerStates.Length && _layerStates[index].locked;
            bool visible = index >= _layerStates.Length || _layerStates[index].visible;
            menu.AddItem(new GUIContent("锁定该轨道"), locked, () => {
                if (index < _layerStates.Length) { _layerStates[index].locked = !_layerStates[index].locked; Repaint(); }
            });
            menu.AddItem(new GUIContent("隐藏该轨道"), !visible, () => {
                if (index < _layerStates.Length) { _layerStates[index].visible = !_layerStates[index].visible; Repaint(); }
            });
            menu.ShowAsContext();
        }

        void DeleteNode(int index)
        {
            if (_editingTarget == null || _editingTarget.graphData == null) return;
            int n = _editingTarget.graphData.Count;
            if (index < 0 || index >= n) return;
            Undo.RecordObject(_editingTarget, "删除节点");
            _editingTarget.graphData.RemoveAt(index);
            // 修 _layerStates(EnsureLayerStates 会在下帧同步)
            var old = _layerStates;
            _layerStates = new LayerState[Mathf.Max(0, n - 1)];
            int copy = Mathf.Min(old.Length, _layerStates.Length);
            for (int i = 0; i < copy; i++) _layerStates[i] = old[i];
            for (int i = copy; i < _layerStates.Length; i++) _layerStates[i] = new LayerState();
            if (_selectedTimelineIndex == index) _selectedTimelineIndex = -1;
            else if (_selectedTimelineIndex > index) _selectedTimelineIndex--;
            if (_expandedNodeIndex == index) _expandedNodeIndex = -1;
            else if (_expandedNodeIndex > index) _expandedNodeIndex--;
            // 同步多选索引集合(删除的索引移除,大于它的整体减一)
            for (int m = _multiSelectedIndices.Count - 1; m >= 0; m--)
            {
                if (_multiSelectedIndices[m] == index) _multiSelectedIndices.RemoveAt(m);
                else if (_multiSelectedIndices[m] > index) _multiSelectedIndices[m]--;
            }
            RefreshEditingSO();
            EditorUtility.SetDirty(_editingTarget);
            MarkPreviewParamsDirty();
            Repaint();
        }

        // 把指定节点行滚到 Layers 列可视区(行号 = 节点索引)
        void ScrollLayersToRow(int i)
        {
            float rowH = LAYER_ROW_H;
            // Phase 2: 使用分组 Y 坐标
            if (!TryGetGroupedY(i, out float yTop, out _)) return;
            float yBot = yTop + rowH;
            float view = _layersScroll.y;
            float viewH = (_editingTarget != null && _editingTarget.graphData != null)
                ? GetGroupedContentHeight(_editingTarget.graphData.Count)
                : 200f;
            if (yTop < view) _layersScroll.y = Mathf.Max(0f, yTop - 4f);
            else if (yBot > view + viewH) _layersScroll.y = yBot + 4f - viewH;
        }

        // ── 时间轴主体(标尺 + 轨道 + 节点 bar)─────────────────────────
        // headerRect: 16px 标尺条(Layers 头那一行对齐)
        // trackRect:  主体轨道区(headerRect 下方)
        // 内部逻辑:行号 = 节点在 graphData 的索引;Layers 与 trackRect 共享 _layersScroll 纵滚;
        // 横向滚动由 _timelineHRange(0..1)控制
        void DrawTimelineTracks(Rect headerRect, Rect trackRect)
        {
            if (_timelineControlId < 0) _timelineControlId = "SkillBuilderWizard.Timeline".GetHashCode();
            int nodeCount = (_editingTarget != null && _editingTarget.graphData != null) ? _editingTarget.graphData.Count : 0;
            float rowH = LAYER_ROW_H;

            // ── 横向滚动条占用底部 14px(与 Layers 列纵向滚动条宽度对齐)──
            const float HSCROLL_H = 14f;
            var hScrollRect = new Rect(trackRect.x, trackRect.yMax - HSCROLL_H, trackRect.width, HSCROLL_H);
            var bodyRect = new Rect(trackRect.x, trackRect.y, trackRect.width, trackRect.height - HSCROLL_H);
            float viewportW = bodyRect.width - 14f;   // 减去纵向滚动条宽度
            _lastTracksAreaW = viewportW;

            float contentH = Mathf.Max(bodyRect.height, GetGroupedContentHeight(nodeCount));
            ClampScrollX(viewportW);
            float pps = _pixelsPerSecond;

            // ═══════════════════════════════════════════════════════════
            // 标尺(大数字 + 细步线 + 贯穿到 trackRect 底)—— 基于 pps + scrollX
            // ═══════════════════════════════════════════════════════════
            EditorGUI.DrawRect(headerRect, UI.BgTrackHead);
            Handles.color = new Color(UI.DividerSoft.r + 0.25f, UI.DividerSoft.g + 0.25f, UI.DividerSoft.b + 0.25f);
            Handles.DrawLine(new Vector3(headerRect.x, headerRect.yMax),
                             new Vector3(headerRect.xMax, headerRect.yMax));

            float bigStep = CalcRulerInterval(pps, 40f);   // 主刻度间距 ≥ 40px
            float smallStep = bigStep / 5f;                 // 次刻度 = 主刻度 / 5

            // 可视时间范围(只画视口内的刻度,避免长内容卡顿)
            float visStartT = Mathf.Max(0f, LocalXToTime(0f) - smallStep);
            float visEndT   = LocalXToTime(viewportW) + smallStep;

            // 次刻度(先画,再被主刻度覆盖)
            for (float t = Mathf.Floor(visStartT / smallStep) * smallStep; t <= visEndT; t += smallStep)
            {
                float nx = headerRect.x + TimeToLocalX(t);
                if (nx < headerRect.x - 2f || nx > headerRect.xMax + 2f) continue;
                bool isBig = Mathf.Abs(t - Mathf.Round(t / bigStep) * bigStep) < smallStep * 0.01f;
                if (!isBig)
                {
                    if (_showRulerGrid)
                    {
                        Handles.color = new Color(0.28f, 0.28f, 0.30f, 0.6f);
                        Handles.DrawLine(new Vector3(nx, headerRect.yMax), new Vector3(nx, bodyRect.yMax));
                    }
                    Handles.color = new Color(0.5f, 0.5f, 0.5f, 0.8f);
                    Handles.DrawLine(new Vector3(nx, headerRect.yMax - 4f), new Vector3(nx, headerRect.yMax));
                }
            }
            // 主刻度(长线 + 数字标签)
            for (float t = Mathf.Floor(visStartT / bigStep) * bigStep; t <= visEndT; t += bigStep)
            {
                float nx = headerRect.x + TimeToLocalX(t);
                if (nx < headerRect.x - 2f || nx > headerRect.xMax + 2f) continue;
                Handles.color = new Color(0.85f, 0.85f, 0.85f, 0.9f);
                Handles.DrawLine(new Vector3(nx, headerRect.y + headerRect.height * 0.35f),
                                 new Vector3(nx, headerRect.yMax));
                if (_showRulerGrid)
                {
                    Handles.color = new Color(0.35f, 0.35f, 0.38f, 0.5f);
                    Handles.DrawLine(new Vector3(nx, headerRect.yMax), new Vector3(nx, bodyRect.yMax));
                }
                // 标签文字:智能单位
                string text;
                if (bigStep < 0.001f)       text = $"{t * 1000f:0.#}ms";
                else if (bigStep < 1f)      text = t < 1f ? $"{t * 1000f:0}ms" : $"{t:0.##}s";
                else                        text = $"{t:0.##}s";
                EditorGUI.LabelField(new Rect(nx + 2, headerRect.y + 1, 44, 11), text, s_rulerLabelStyle);
            }

            // ── 标尺点击/拖拽 → 直接跳转播放头 ─────────────────────────
            {
                var re = Event.current;
                bool rulerSeekStart  = re.type == EventType.MouseDown && re.button == 0
                                       && headerRect.Contains(re.mousePosition) && GUIUtility.hotControl == 0;
                bool rulerSeekDrag   = re.type == EventType.MouseDrag && GUIUtility.hotControl == _timelineControlId + 2;
                bool rulerSeekEnd    = re.type == EventType.MouseUp   && GUIUtility.hotControl == _timelineControlId + 2;
                if (rulerSeekStart || rulerSeekDrag)
                {
                    if (re.type == EventType.MouseDown) GUIUtility.hotControl = _timelineControlId + 2;
                    // P1-6:标尺拖拽中解除 VFX 冻结,让 DrawPreviewView 用墙钟 dt 推进粒子
                    _previewRulerDragging = true;
                    float newT = LocalXToTime(re.mousePosition.x - headerRect.x);
                    if (_snapToFrame)
                        newT = Mathf.Round(newT * _frameRate) / _frameRate;
                    ResyncPreviewToPlayhead(newT);  // V3.1.8:同步 _playheadT + 驱动 Runtime
                    re.Use(); Repaint();
                }
                else if (rulerSeekEnd)
                {
                    GUIUtility.hotControl = 0;
                    _previewRulerDragging = false;  // P1-6:拖拽结束恢复 VFX 冻结
                    re.Use();
                }
                EditorGUIUtility.AddCursorRect(headerRect, MouseCursor.ArrowPlus);
            }

            // castTime 标线(绿色)
            if (_editingTarget != null && _editingTarget.castTime > 0)
            {
                float castX = headerRect.x + TimeToLocalX(_editingTarget.castTime);
                if (castX >= headerRect.x - 1f && castX <= headerRect.xMax + 1f)
                {
                    Handles.color = new Color(0.3f, 1f, 0.5f, 0.7f);
                    Handles.DrawLine(new Vector3(castX, headerRect.yMax), new Vector3(castX, bodyRect.yMax), 4f);
                    EditorGUI.LabelField(new Rect(castX - 26, headerRect.y + 1, 52, 12),
                        $"cast {_editingTarget.castTime:0.00}s", s_castTimeLabelStyle);
                }
            }

            if (_editingTarget == null || _editingTarget.graphData == null) return;

            // ═══════════════════════════════════════════════════════════
            // 轨道主体(纵向滚动视口 + 行底 + 节点 bar;横向由 _scrollX 手动管理)
            // ═══════════════════════════════════════════════════════════
            _layersScroll = GUI.BeginScrollView(bodyRect, _layersScroll,
                new Rect(0, 0, viewportW, contentH), false, false, GUIStyle.none, GUIStyle.none);
            try
            {
                var clipRect = new Rect(0, 0, viewportW, contentH);
                GUI.BeginClip(clipRect);
                try
                {
                    DrawTimelineTracksBody(new Rect(0, 0, viewportW, bodyRect.height), viewportW, contentH, nodeCount, rowH);
                }
                finally { GUI.EndClip(); }
            }
            finally { GUI.EndScrollView(); }

            // ── 横向滚动条 ─────────────────────────────────────────────
            {
                float contentW = GetContentWidthPx();
                float newScroll = GUI.HorizontalScrollbar(hScrollRect, _scrollX, viewportW,
                    0f, Mathf.Max(viewportW, contentW));
                if (!Mathf.Approximately(newScroll, _scrollX)) { _scrollX = newScroll; Repaint(); }
            }

            // ── Ctrl+滚轮缩放(锚点 = 鼠标位置) / Shift+滚轮或中键拖拽平移 ──
            {
                var scrollEvent = Event.current;
                if (scrollEvent.type == EventType.ScrollWheel && bodyRect.Contains(scrollEvent.mousePosition))
                {
                    if (scrollEvent.control)
                    {
                        float anchorLocalX = scrollEvent.mousePosition.x - bodyRect.x;
                        float zoomFactor = Mathf.Pow(1.0015f, -scrollEvent.delta.y * 20f);
                        ZoomAtAnchor(anchorLocalX, zoomFactor);
                        scrollEvent.Use();
                        Repaint();
                    }
                    else if (scrollEvent.shift)
                    {
                        _scrollX += scrollEvent.delta.y * 40f;
                        ClampScrollX(viewportW);
                        scrollEvent.Use();
                        Repaint();
                    }
                }
                // 中键拖拽平移(整个 trackRect 范围内响应)
                if (scrollEvent.type == EventType.MouseDown && scrollEvent.button == 2 && trackRect.Contains(scrollEvent.mousePosition))
                {
                    _panningTimeline = true;
                    _panStartMouse = scrollEvent.mousePosition;
                    _panStartScrollX = _scrollX;
                    GUIUtility.hotControl = _timelineControlId + 3;
                    scrollEvent.Use();
                }
                else if (scrollEvent.type == EventType.MouseDrag && _panningTimeline && GUIUtility.hotControl == _timelineControlId + 3)
                {
                    float dx = scrollEvent.mousePosition.x - _panStartMouse.x;
                    _scrollX = _panStartScrollX - dx;
                    ClampScrollX(viewportW);
                    scrollEvent.Use();
                    Repaint();
                }
                else if (scrollEvent.type == EventType.MouseUp && _panningTimeline)
                {
                    _panningTimeline = false;
                    GUIUtility.hotControl = 0;
                    scrollEvent.Use();
                }
                if (_panningTimeline) EditorGUIUtility.AddCursorRect(trackRect, MouseCursor.Pan);
                // 滚轮缩放(Ctrl+滚轮 以鼠标位置为锚点)
                if (scrollEvent.type == EventType.ScrollWheel && trackRect.Contains(scrollEvent.mousePosition))
                {
                    float zoomFactor = 1f - scrollEvent.delta.y * 0.05f;
                    float anchorX = scrollEvent.mousePosition.x - trackRect.x;
                    ZoomAtAnchor(anchorX, zoomFactor);
                    ClampScrollX(viewportW);
                    scrollEvent.Use();
                    Repaint();
                }
            }

            // ── 键盘导航:方向键移动 playhead,Delete/退格删除选中节点 ──
            HandleTimelineKeyboard(bodyRect);

            // ═══════════════════════════════════════════════════════════
            // Playhead(不滚动,叠在视口上)
            // ═══════════════════════════════════════════════════════════
            DrawPlayhead(bodyRect, headerRect);

            // 选中详情条(压在 trackRect 顶部)
            DrawTimelineSelectionBar(bodyRect);
            // hover tooltip
            DrawTimelineHoverTooltip(bodyRect, new Rect(0, 0, viewportW, contentH), rowH);

            _timelineContentH = GetGroupedContentHeight(nodeCount) + headerRect.height;
        }

        // 轨道主体绘制(在 BeginScrollView+BeginClip 内部调用,viewRect 是相对滚动组的局部坐标)
        void DrawTimelineTracksBody(Rect viewRect, float viewportW, float contentH, int nodeCount, float rowH)
        {
            // 防御:节点数与 _layerStates 长度错位时只画到 min
            int safeNodeCount = Mathf.Min(nodeCount, _layerStates != null ? _layerStates.Length : 0);

            // ── Phase 2:分组布局 — 行底色使用分组 Y 坐标,折叠组跳过 ──
            var groups = BuildCategoryGroups();
            float curY = 0f;
            foreach (var g in groups)
            {
                bool collapsed = g.category >= 0 && g.category < _categoryCollapsed.Length && _categoryCollapsed[g.category];

                // 分组头背景条(轨道区侧也画一条对应高度的占位条,保持与 Layers 列对齐)
                EditorGUI.DrawRect(new Rect(0, curY, viewportW, GROUP_HEADER_H),
                    new Color(UI.BgTrackHead.r * 0.85f, UI.BgTrackHead.g * 0.85f, UI.BgTrackHead.b * 0.85f, 1f));
                var catCol = CategoryColor(g.category);
                // 分组头左侧色条(与 Layers 列呼应)
                EditorGUI.DrawRect(new Rect(0, curY, 3f, GROUP_HEADER_H), catCol);
                // 分组名标签(轨道区侧淡显)
                if (collapsed)
                {
                    s_trackGroupLabelStyleBase.normal.textColor = new Color(catCol.r * 0.5f, catCol.g * 0.5f, catCol.b * 0.5f, 0.7f);
                    string grpName = g.category < CategoryNames.Length ? CategoryNames[g.category] : $"Category {g.category}";
                    GUI.Label(new Rect(8f, curY, viewportW - 12f, GROUP_HEADER_H), $"▶ {grpName} ({g.nodeIndices.Count})", s_trackGroupLabelStyleBase);
                }
                curY += GROUP_HEADER_H;

                if (collapsed) continue;

                // 行底色
                foreach (int r in g.nodeIndices)
                {
                    if (r >= safeNodeCount) break;
                    var state = _layerStates[r];
                    if (!state.visible) { curY += rowH; continue; }
                    bool trackSelected = r == _selectedTimelineIndex || _multiSelectedIndices.Contains(r);
                    var rowRect = new Rect(0, curY, viewportW, rowH);
                    var rowCatCol = catCol;
                    var rowBg = trackSelected
                        ? new Color(rowCatCol.r * 0.20f, rowCatCol.g * 0.20f, rowCatCol.b * 0.20f, 1f)
                        : ((r % 2 == 0) ? UI.BgRowAlt1 : UI.BgRowAlt2);
                    EditorGUI.DrawRect(rowRect, rowBg);
                    EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, rowRect.width, 1f),
                        new Color(rowCatCol.r, rowCatCol.g, rowCatCol.b, 0.65f));
                    if (trackSelected)
                        EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y + 1f, 3f, rowRect.height - 2f), rowCatCol);
                    Handles.color = new Color(0.05f, 0.05f, 0.05f, 0.6f);
                    Handles.DrawLine(new Vector3(rowRect.x, rowRect.yMax), new Vector3(rowRect.xMax, rowRect.yMax));
                    curY += rowH;
                }
            }

            // 播放头竖线延伸到轨道内
            {
                float phx = TimeToLocalX(_playheadT);
                if (phx >= -1f && phx <= viewportW + 1f)
                {
                    Handles.color = new Color(0.9f, 0.25f, 0.25f, 0.35f);
                    Handles.DrawLine(new Vector3(phx, 0), new Vector3(phx, contentH));
                }
            }

            // ── 统一时间条渲染(参考 Unity Timeline Clip 样式) ──────────
            var hits = new List<TimelineHit>();
            for (int i = 0; i < nodeCount; i++)
            {
                var data = _editingTarget.graphData[i];
                if (data == null) continue;
                if (i >= _layerStates.Length) continue;
                if (!_layerStates[i].visible) continue;
                if (_layerStates[i].locked) continue;

                if (!TryGetGroupedY(i, out float groupedY, out int _))
                    continue;

                bool isActiveDrag = _isDraggingClip && (i == _dragNodeIndex || _dragGroupNodes.Contains(data));
                float tt, dur;
                GetDisplayTimeAndDuration(data, i, out tt, out dur);
                float x = TimeToLocalX(tt);

                // 视口裁剪
                float clipX2 = dur > 0f ? TimeToLocalX(tt + dur) : x;
                float scrollY = _layersScroll.y;
                float viewBot = scrollY + viewRect.height;
                if (groupedY + rowH < scrollY || groupedY > viewBot) continue;
                if (Mathf.Max(x, clipX2) < -20f || Mathf.Min(x, clipX2) > viewportW + 20f) continue;

                float yc = groupedY + rowH * 0.5f;
                var col = CategoryColor(data);  // P2-3:按节点实例取色,VFX 三色细分
                bool isSelected = i == _selectedTimelineIndex || _multiSelectedIndices.Contains(i);

                // ── 统一:所有节点都渲染为时间条 ──
                // 持续事件: bar 横跨 [tt, tt+dur]; 点事件: 最小宽度 12px 居中于 tt
                float barX, barW;
                if (dur > 0f)
                {
                    float x2 = TimeToLocalX(tt + dur);
                    barX = Mathf.Min(x, x2);
                    barW = Mathf.Max(12f, Mathf.Abs(x2 - x));
                }
                else
                {
                    barW = 12f;
                    barX = x - barW * 0.5f;
                }
                float barH = Mathf.Max(14f, rowH - 4f);
                var bar = new Rect(barX, yc - barH * 0.5f, barW, barH);

                // 1) 底部阴影
                EditorGUI.DrawRect(new Rect(bar.x + 1f, bar.yMax, bar.width, 2f), new Color(0f, 0f, 0f, 0.35f));

                // 2) 主体填充: 实色 + 顶部高光带(Unity Timeline 风格)
                float fillAlpha = isSelected ? 0.90f : 0.75f;
                EditorGUI.DrawRect(bar, new Color(col.r * 0.55f, col.g * 0.55f, col.b * 0.55f, fillAlpha));
                // 顶部高光
                EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width, 2f), new Color(col.r, col.g, col.b, 1f));
                // 底部暗带
                EditorGUI.DrawRect(new Rect(bar.x, bar.yMax - 1f, bar.width, 1f), new Color(0f, 0f, 0f, 0.4f));

                // 3) 描边
                DrawRectOutline(bar, isSelected ? Color.white : new Color(col.r, col.g, col.b, 0.6f));

                // 4) 左右拖拽手柄(仅持续事件)
                if (dur > 0f && bar.width >= 10f)
                {
                    float handleW = 3f;
                    var leftHandle = new Rect(bar.x, bar.y + 1f, handleW, bar.height - 2f);
                    var rightHandle = new Rect(bar.xMax - handleW, bar.y + 1f, handleW, bar.height - 2f);
                    EditorGUI.DrawRect(leftHandle, new Color(1f, 1f, 1f, 0.3f));
                    EditorGUI.DrawRect(rightHandle, new Color(1f, 1f, 1f, 0.3f));
                }

                // 5) Ease 装饰
                if (dur > 0f && Event.current.type == EventType.Repaint && bar.width > 30f)
                    DrawEaseDecoration(bar, col);

                // 6) 文字标签直接画在条上
                DrawClipLabel(bar, data, tt, dur, col, isSelected);

                // 7) 光标提示
                if (dur > 0f && bar.width >= 10f)
                {
                    EditorGUIUtility.AddCursorRect(new Rect(bar.x - 2f, bar.y, 7f, bar.height), MouseCursor.SplitResizeLeftRight);
                    EditorGUIUtility.AddCursorRect(new Rect(bar.xMax - 5f, bar.y, 7f, bar.height), MouseCursor.SplitResizeLeftRight);
                }
                EditorGUIUtility.AddCursorRect(new Rect(bar.x + 4f, bar.y, Mathf.Max(1f, bar.width - 8f), bar.height), MouseCursor.MoveArrow);

                // 8) 对齐辅助线
                if (isActiveDrag && Event.current.type == EventType.Repaint)
                    DrawAlignmentGuides(bar, i, viewportW, contentH);

                // 9) 命中区
                if (dur > 0f && bar.width >= 10f)
                {
                    hits.Add(new TimelineHit { nodeIndex = i, rect = new Rect(bar.x - 3f, bar.y, 8f, bar.height), kind = TimelineHitKind.ResizeLeft,  data = data, trackRow = i });
                    hits.Add(new TimelineHit { nodeIndex = i, rect = new Rect(bar.xMax - 5f, bar.y, 8f, bar.height), kind = TimelineHitKind.ResizeRight, data = data, trackRow = i });
                }
                hits.Add(new TimelineHit { nodeIndex = i, rect = new Rect(bar.x + 2f, bar.y, Mathf.Max(1f, bar.width - 4f), bar.height), kind = TimelineHitKind.Move, data = data, trackRow = i });
            }

            // 交互
            var trackInner = new Rect(0, 0, viewportW, contentH);
            HandleTimelineInteraction(viewRect, trackInner, rowH, hits, nodeCount);

            // 框选矩形绘制
            if (_boxSelecting)
            {
                var boxRect = Rect.MinMaxRect(
                    Mathf.Min(_boxSelectStart.x, _boxSelectCur.x), Mathf.Min(_boxSelectStart.y, _boxSelectCur.y),
                    Mathf.Max(_boxSelectStart.x, _boxSelectCur.x), Mathf.Max(_boxSelectStart.y, _boxSelectCur.y));
                EditorGUI.DrawRect(boxRect, new Color(0.3f, 0.6f, 0.9f, 0.18f));
                DrawRectOutline(boxRect, new Color(0.4f, 0.7f, 1f, 0.9f));
            }

            // 选中高亮(统一用白色描边条)
            if (_selectedTimelineIndex >= 0 && _selectedTimelineIndex < nodeCount)
            {
                var sel = _editingTarget.graphData[_selectedTimelineIndex];
                if (sel != null)
                {
                    GetDisplayTimeAndDuration(sel, _selectedTimelineIndex, out float tt, out float dur);
                    if (TryGetGroupedY(_selectedTimelineIndex, out float selY, out int _))
                    {
                        float selYc = selY + rowH * 0.5f;
                        float selBarH = Mathf.Max(14f, rowH - 4f);
                        float sx, sw;
                        if (dur > 0f)
                        {
                            float sx2 = TimeToLocalX(tt + dur);
                            sx = Mathf.Min(TimeToLocalX(tt), sx2);
                            sw = Mathf.Max(12f, Mathf.Abs(sx2 - TimeToLocalX(tt)));
                        }
                        else
                        {
                            sw = 12f;
                            sx = TimeToLocalX(tt) - sw * 0.5f;
                        }
                        var selBar = new Rect(sx, selYc - selBarH * 0.5f, sw, selBarH);
                        DrawRectOutline(selBar, Color.white);
                        DrawRectOutline(new Rect(selBar.x - 1f, selBar.y - 1f, selBar.width + 2f, selBar.height + 2f),
                            new Color(1f, 1f, 1f, 0.4f));
                    }
                }
            }
        }

    }
}
#endif
