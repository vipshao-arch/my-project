#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Game.SkillSystem;

namespace Game.SkillSystem.EditorTools
{
    /// <summary>
    /// 技能构建向导 — 三段式时间线编辑器(参考 Unity Timeline / UE Sequencer 布局)
    ///
    /// 布局契约:
    ///   ┌────────────┬─────────────────────────────────────────────┐
    ///   │            │  顶部工具条 / Mode 切换                       │
    ///   │  Preview   ├─────────────────────────────────────────────┤
    ///   │  预览窗口   │                                             │
    ///   │            │  参数面板 / 节点编辑 / Step1/2/3              │
    ///   │  (可拖拽)  │                                             │
    ///   ├────────────┤                                             │
    ///   │  Layers    │                                             │
    ///   │  层管理     │                                             │
    ///   └────────────┴─────────────────────────────────────────────┤
    ///   │  Timeline 时间轴(轨道 + 标尺 + 节点 bar,可拖拽)             │
    ///   └──────────────────────────────────────────────────────────┘
    ///   垂直分隔条:Preview | Params    (左/右 边界)
    ///   水平分隔条:上半(Preview+Params) | 底部 Timeline  (上/下 边界)
    ///   窗口尺寸持久化到 EditorPrefs。
    ///
    /// 2026-07-02 重构:
    ///   - 三段式布局 + 拖拽分隔条
    ///   - 左侧 PreviewPanel:角色 Prefab + 动画列表 + PreviewRenderUtility 离屏渲染
    ///   - 底部 Timeline 单独成窗口,左侧 Layers 列(眼/锁/重命名/上下移)
    ///   - 保留原节点拖动 / 区间时长调整 / Alt+Click 删除 / 选中详情条 / Hover tooltip
    /// </summary>
    public partial class SkillBuilderWizard : EditorWindow
    {
        // ═══════════════════════════════════════════════════════════════
        //  UI 调色板(与 Unity Timeline Editor / Pro Skin 对齐)
        //  所有面板色值统一在这里改一处即可
        // ═══════════════════════════════════════════════════════════════
        static class UI
        {
            // Phase 10.6: Light/Dark Skin 自适应 — 每次访问动态检测 isProSkin
            static bool IsDark => EditorGUIUtility.isProSkin;
            static Color DC(float r, float g, float b) => IsDark
                ? new Color(r, g, b)
                : new Color(Mathf.Clamp01(r + 0.62f), Mathf.Clamp01(g + 0.62f), Mathf.Clamp01(b + 0.62f));
            static Color LC(float r, float g, float b, float a = 1f) => new Color(r, g, b, a);

            // —— 背景层(由内到外,色值递减)———————————————
            public static Color BgPanel      => DC(0.18f, 0.18f, 0.20f);
            public static Color BgTimeline   => DC(0.20f, 0.20f, 0.22f);
            public static Color BgHeader     => DC(0.18f, 0.18f, 0.20f);
            public static Color BgLayerRow   => DC(0.12f, 0.12f, 0.14f);
            public static Color BgTrack      => DC(0.14f, 0.14f, 0.16f);
            public static Color BgTrackHead  => DC(0.16f, 0.16f, 0.18f);
            public static Color BgSplit      => DC(0.08f, 0.08f, 0.08f);
            public static Color BgPreview    => DC(0.13f, 0.13f, 0.15f);
            public static Color BgRowAlt1    => DC(0.18f, 0.18f, 0.20f);
            public static Color BgRowAlt2    => DC(0.14f, 0.14f, 0.16f);
            public static Color Divider      => DC(0.05f, 0.05f, 0.05f);
            public static Color DividerSoft  => DC(0.10f, 0.10f, 0.10f);

            // —— 文字色阶(Light Skin 下自动取深色)———————————
            public static Color TextPrimary   => IsDark ? LC(0.85f, 0.85f, 0.85f) : LC(0.15f, 0.15f, 0.15f);
            public static Color TextSecondary => IsDark ? LC(0.65f, 0.65f, 0.65f) : LC(0.30f, 0.30f, 0.30f);
            public static Color TextTertiary  => IsDark ? LC(0.45f, 0.45f, 0.45f) : LC(0.50f, 0.50f, 0.50f);

            // —— 高亮 / 交互(两套 Skin 共用)————————————————
            public static Color Highlight => LC(0.24f, 0.49f, 0.91f);
            public static Color Play      => LC(0.30f, 0.70f, 0.40f);
            public static Color Snap      => LC(0.30f, 0.60f, 0.90f);
            public static Color SplitLine => LC(0.50f, 0.50f, 0.50f);

            // —— 布局常量(像素)—————————————————
            public const float HdrH        = 28f;   // Timeline Header 高度
            public const float TrackHdrH   = 16f;   // 轨道头(眼/锁那一行)高度
            public const float RowH        = 22f;   // 工具条按钮高 / 通用行高
            public const float LayersW     = 110f;  // Layers 列宽
            public const float SplitterThk = 6f;    // 分隔条厚度
            public const float TitleH      = 20f;   // 面板标题条高度

            // ── P2-4: 常用 style 缓存(lazy-init,避免 OnGUI 热路径分配)──
            private static GUIStyle s_titleStyle;
            private static GUIStyle s_subTitleStyle;
            private static GUIStyle s_tipStyle;

            public static GUIStyle TitleStyle()
            {
                if (s_titleStyle == null)
                    s_titleStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                    {
                        normal = { textColor = TextPrimary },
                        alignment = TextAnchor.MiddleLeft
                    };
                return s_titleStyle;
            }
            public static GUIStyle SubTitleStyle()
            {
                if (s_subTitleStyle == null)
                    s_subTitleStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                    {
                        normal = { textColor = TextSecondary },
                        alignment = TextAnchor.MiddleLeft
                    };
                return s_subTitleStyle;
            }
            public static GUIStyle TipStyle()
            {
                if (s_tipStyle == null)
                    s_tipStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = TextTertiary }
                    };
                return s_tipStyle;
            }
        }
        // ═══════════════════════════════════════════════════════════════
        // 状态
        // ═══════════════════════════════════════════════════════════════
        private SkillRecipe _recipe = new SkillRecipe();
        private Vector2 _scroll;
        private int _step = 1;

        private enum Mode { NewSkill, EditExisting, CharacterLibrary }
        private Mode _mode = Mode.NewSkill;
        private SkillData _editingTarget;
        private SerializedObject _editingSO;
        private Vector2 _nodeEditScroll;

        // 验证结果缓存（ChangePreviewTarget 后自动刷新，底栏显示摘要）
        private IReadOnlyList<SkillDataValidator.Issue> _lastValidationIssues;

        // ── 缓存 GUIStyle 避免每帧分配（延迟初始化，避免 TypeInitializationException）──
        private static GUIStyle _s_timelineTooltipStyle;
        private static GUIStyle s_timelineTooltipStyle
        {
            get
            {
                if (_s_timelineTooltipStyle == null)
                    _s_timelineTooltipStyle = new GUIStyle(EditorStyles.helpBox)
                    {
                        fontSize = 11,
                        alignment = TextAnchor.UpperLeft,
                        padding = new RectOffset(6, 6, 4, 4),
                        normal = { textColor = Color.white }
                    };
                return _s_timelineTooltipStyle;
            }
        }
        private static GUIStyle _s_rulerLabelStyle;
        private static GUIStyle s_rulerLabelStyle
        {
            get
            {
                if (_s_rulerLabelStyle == null)
                    _s_rulerLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        normal = { textColor = new Color(0.82f, 0.82f, 0.82f) },
                        fontSize = 9
                    };
                return _s_rulerLabelStyle;
            }
        }
        private static GUIStyle _s_frameInfoStyle;
        private static GUIStyle s_frameInfoStyle
        {
            get
            {
                if (_s_frameInfoStyle == null)
                    _s_frameInfoStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleRight
                    };
                return _s_frameInfoStyle;
            }
        }
        private static GUIStyle _s_miniBoldCenterStyle;
        private static GUIStyle s_miniBoldCenterStyle
        {
            get
            {
                if (_s_miniBoldCenterStyle == null)
                    _s_miniBoldCenterStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = new Color(0.9f, 0.9f, 0.9f) },
                        fontSize = 10
                    };
                return _s_miniBoldCenterStyle;
            }
        }

        // ── P5: OnGUI 热路径 GUIStyle 缓存，避免每帧 new GUIStyle 产生 GC ──
        private static GUIStyle _s_warnStyle;
        private static GUIStyle s_warnStyle => _s_warnStyle ?? (_s_warnStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1f, 0.55f, 0.2f) } });

        private static GUIStyle _s_previewTipStyle;
        private static GUIStyle s_previewTipStyle => _s_previewTipStyle ?? (_s_previewTipStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.55f, 0.55f, 0.55f) }, alignment = TextAnchor.MiddleCenter });

        private static GUIStyle _s_placeholderTipStyle;
        private static GUIStyle s_placeholderTipStyle => _s_placeholderTipStyle ?? (_s_placeholderTipStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.5f, 0.5f, 0.5f) } });

        private static GUIStyle _s_timelineTitleStyle;
        private static GUIStyle s_timelineTitleStyle => _s_timelineTitleStyle ?? (_s_timelineTitleStyle = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.9f, 0.9f, 0.9f) }, clipping = TextClipping.Clip });

        private static GUIStyle _s_gripStyle;
        private static GUIStyle s_gripStyle => _s_gripStyle ?? (_s_gripStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.5f, 0.5f, 0.5f) }, fontSize = 9 });

        private static GUIStyle _s_castTimeLabelStyle;
        private static GUIStyle s_castTimeLabelStyle => _s_castTimeLabelStyle ?? (_s_castTimeLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = new Color(0.4f, 1f, 0.5f) } });

        private static GUIStyle _s_clipNameStyle;
        private static GUIStyle s_clipNameStyle => _s_clipNameStyle ?? (_s_clipNameStyle = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = Color.white }, alignment = TextAnchor.MiddleLeft, fontSize = 10, clipping = TextClipping.Clip });

        private static GUIStyle _s_clipTimeStyle;
        private static GUIStyle s_clipTimeStyle => _s_clipTimeStyle ?? (_s_clipTimeStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1f, 0.92f, 0.5f, 0.9f) }, alignment = TextAnchor.MiddleRight, fontSize = 9, clipping = TextClipping.Clip });

        private static GUIStyle _s_selectedBoldWhiteStyle;
        private static GUIStyle s_selectedBoldWhiteStyle => _s_selectedBoldWhiteStyle ?? (_s_selectedBoldWhiteStyle = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = Color.white }, fontSize = 10 });

        private static GUIStyle _s_selectedSmallGrayStyle;
        private static GUIStyle s_selectedSmallGrayStyle => _s_selectedSmallGrayStyle ?? (_s_selectedSmallGrayStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }, fontSize = 9 });

        private static GUIStyle _s_selectedSmallYellowStyle;
        private static GUIStyle s_selectedSmallYellowStyle => _s_selectedSmallYellowStyle ?? (_s_selectedSmallYellowStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1f, 0.92f, 0.45f) }, fontSize = 9 });

        // 动态颜色样式（基样式复用，normal.textColor 按需设置）
        private static GUIStyle _s_groupArrowStyleBase;
        private static GUIStyle s_groupArrowStyleBase => _s_groupArrowStyleBase ?? (_s_groupArrowStyleBase = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter });
        private static GUIStyle _s_groupNameStyleBase;
        private static GUIStyle s_groupNameStyleBase => _s_groupNameStyleBase ?? (_s_groupNameStyleBase = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleLeft });
        private static GUIStyle _s_layerNameStyleBase;
        private static GUIStyle s_layerNameStyleBase => _s_layerNameStyleBase ?? (_s_layerNameStyleBase = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleLeft });
        private static GUIStyle _s_trackGroupLabelStyleBase;
        private static GUIStyle s_trackGroupLabelStyleBase => _s_trackGroupLabelStyleBase ?? (_s_trackGroupLabelStyleBase = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft });

        private int _expandedNodeIndex = -1;
        // P0 延迟 graphData 操作 — 避免 Layout/Repaint 控件数不一致导致 BeginArea 报错
        private int _pendingNodeDeletionIndex = -1;  // 待删除节点索引（-1 = 无）
        private int _pendingNodeMoveFrom = -1;        // 待上移/下移来源索引

        // ── 方案 A(2026-07-06):角色技能库总览 ──────────────────────────
        // 扫描 Assets/_Game/character/*(排除 Common)得到角色目录列表,
        // 选中角色后列出其 SkillData/ 下全部技能资产,支持一键加载编辑/复制到其他角色/绑定 SkillSlots。
        private string[] _charLibNames = new string[0];   // 角色显示名(目录名)
        private string[] _charLibPaths = new string[0];   // 角色目录完整路径(Assets/...)
        private int _charLibSelectedIndex = -1;
        private List<SkillData> _charLibSkills = new List<SkillData>();
        private Vector2 _charLibScroll;
        private bool _charLibScanned = false;
        private const string CHARACTER_ROOT = "Assets/_Game/character";

        // ── 窗口尺寸(像素)──────────────────────────────────────────────
        private const string PREF_PREVIEW_W = "SBW.PreviewWidth";    // 左预览列宽
        private const string PREF_TIMELINE_H = "SBW.TimelineHeight"; // 底部时间轴高
        private const string PREF_LAYERS_W = "SBW.LayersWidth";      // Layers 列宽
        // Phase 10.3: 缩放/帧率/Snap 持久化
        private const string PREF_PPS = "SBW.PixelsPerSecond";       // 缩放级别
        private const string PREF_FPS = "SBW.FrameRate";             // 帧率
        private const string PREF_SNAP = "SBW.SnapToFrame";          // Snap 开关
        private float _previewW = 320f;     // 默认左预览窗宽(可拖)
        private float _timelineH = 240f;    // 默认底部时间轴高(可拖)
        private float _layersW = 110f;      // Layers 列宽(可拖,替代原硬编码常量)
        private const float SPLIT_THICKNESS = 4f;  // 分隔条视觉厚度
        private const float SPLIT_MIN = 160f;       // 各面板最小尺寸
        private const float LAYERS_W_MIN = 70f;     // Layers 列最小宽度
        private const float TRACKS_W_MIN = 120f;    // 轨道区最小宽度(夹紧 Layers 列上限用)

        // ── 拖拽状态 ──
        private bool _draggingVSplit;       // 正在拖垂直分隔(Preview | Params)
        private bool _draggingHSplit;       // 正在拖水平分隔(上半 | Timeline)
        private bool _draggingLayersSplit;  // 正在拖 Layers | 轨道区分隔条
        private float _splitDragStartX;      // 分隔条拖拽起始鼠标 X(防抖动)
        private float _splitDragStartY;      // 分隔条拖拽起始鼠标 Y
        private float _splitDragStartVal;    // 分隔条拖拽起始面板宽度值
        private static readonly int _vSplitCtrlId = "SBW.VSplit".GetHashCode();
        private static readonly int _hSplitCtrlId = "SBW.HSplit".GetHashCode();
        private static readonly int _layersSplitCtrlId = "SBW.LayersSplit".GetHashCode();

        // ── Layers 行拖拽排序(按住行把手拖到新位置)────────────────────
        private bool _draggingLayerRow;
        private int _dragLayerFromIndex = -1;
        private int _dragLayerHoverIndex = -1;

        // ── Phase 2:Category 分组折叠状态(按 SkillNodeCategory 索引)──
        // true=折叠(隐藏该组轨道),false=展开(默认)
        private bool[] _categoryCollapsed = new bool[9]; // 0~8 对应 SkillNodeCategory 枚举
        private const float GROUP_HEADER_H = 18f; // 分组行高

        // ── 时间轴坐标系(独立 pps + scrollX,对齐 Unity Timeline 架构)───
        // 时间→像素: x = originX + t * _pixelsPerSecond - _scrollX
        // 像素→时间: t = (x - originX + _scrollX) / _pixelsPerSecond
        private float _pixelsPerSecond = 200f;   // 缩放核心:每秒对应的像素数,范围 [10, 4000]
        private float _scrollX = 0f;             // 横向滚动偏移(像素)
        private float _timelineMaxT = 2f;        // 内容逻辑总时长(秒)——仅用于 Fit/Ruler 兜底,不再是缩放驱动量
        private float _lastTracksAreaW = 400f;   // 上一帧轨道可视区宽度缓存(供 Fit/缩放按钮使用)

        private int _selectedTimelineIndex = -1;
        private int _dragNodeIndex = -1;
        private SkillNodeData _dragNodeRef;
        private TimelineHitKind _dragKind = TimelineHitKind.Move;
        private float _dragStartMouseX;
        private float _dragStartValue;       // 拖拽起点:triggerTime(Move/ResizeLeft)或 duration(ResizeRight)
        private float _dragStartDuration;    // 拖拽起点原始 duration(ResizeLeft 需要保持右端不动)
        private int _dragStartRow = -1;
        private bool _dragDirty;
        private int _timelineControlId = -1;
        private float _dragLastRecordX;
        private float _timelineContentH;    // 给 Layers 同步:实际内容高度
        private bool _timelineScrolling;     // 时间轴内容是否在滚动

        // ── 拖拽临时值(MouseDrag 期间持有,MouseUp 才写入节点)──────────
        private float _tempTriggerTime;      // 拖拽中的临时 triggerTime(Resize 用)
        private float _tempDuration;         // 拖拽中的临时 editorDuration(Resize 用)
        private bool  _isDraggingClip;       // true 时参数区/绘制读临时值

        // ── 多选 + 框选(Ctrl/Shift 点选,或空白区拖拽框选)────────────
        private List<int> _multiSelectedIndices = new List<int>();
        private List<SkillNodeData> _dragGroupNodes = new List<SkillNodeData>();
        private List<float> _dragGroupStartTrigger = new List<float>();
        private List<float> _dragGroupTempTrigger = new List<float>();
        private bool _boxSelecting;
        private Vector2 _boxSelectStart;
        private Vector2 _boxSelectCur;

        // ── 中键平移 / 水平滚动条 ─────────────────────────────────────
        private bool _panningTimeline;
        private Vector2 _panStartMouse;
        private float _panStartScrollX;

        // ── 参数区滚动目标(选中 Clip 时自动定位)──────────────────────
        private float _paramScrollTarget = -1f;
        private bool  _paramScrollAnimating;

        // ── Timeline Header 状态(Unity Timeline 风格)──────────
        private float _playheadT = 0f;            // 当前时间游标(秒)
        // V3.1 修复:_timelinePlaying / _timelineLastTick 已删除 — 播放控制权全部交给底行 5 键
        // 详见 DrawTimelineHeader 1658 注释 / OnGUI tick 路径 1793 / Space 键 3972
        private float _frameRate = 30f;           // 帧率(可改)
        private bool _snapToFrame = true;         // 吸附到帧
        private bool _showRulerGrid = true;       // 标尺大网格开关
        private string _timelineTitle = "Skill Timeline";  // 顶部标题(可改)

        // ── Layers 层管理(Unity Timeline 风格:每个节点添加一层)──────────
        // 行号 = 节点在 graphData 中的索引
        [System.Serializable]
        private class LayerState
        {
            public bool visible = true;
            public bool locked = false;
            public string customName = null;  // null = 用节点 DisplayName
        }
        private LayerState[] _layerStates;
        // Layers 列搜索过滤
        private string _layerSearchFilter = "";
        // Layers 列 + Timeline 轨道共享的纵向滚动
        private Vector2 _layersScroll;
        // 行高(Unity Timeline 默认 16px,实际稍大以便看清)
        private const float LAYER_ROW_H = 18f;

        // ── 预览窗口状态 ──────────────────────────────────────────────
        // [NonSerialized] 防止 domain reload 后残留已销毁的 GO/PRU 引用
        [System.NonSerialized] private PreviewRenderUtility _previewRtu;
        [System.NonSerialized] private GameObject _previewInstance;
        private GameObject _previewPrefab;

        // ── 受击目标(2026-08-03):预览窗内摆放可替换靶子,观察命中/受击效果 ──
        // prefab 为空时生成内置胶囊假人;数量/距离/扇形角可调,EditorPrefs 持久化。
        private GameObject _previewTargetPrefab;                                  // null=内置胶囊假人
        private int _previewTargetCount = 1;
        private float _previewTargetDistance = 3f;
        private float _previewTargetArc = 60f;                                    // 扇形排布角(度,居中于角色前方)
        [System.NonSerialized] private System.Collections.Generic.List<GameObject> _previewTargets
            = new System.Collections.Generic.List<GameObject>();
        [System.NonSerialized] private System.Collections.Generic.List<Animator> _previewTargetAnims
            = new System.Collections.Generic.List<Animator>();   // 缓存,渲染循环免查找
        [System.NonSerialized] private Animator _previewActor;                 // V3 修复:Animator 状态机路径专用缓存
        [System.NonSerialized] private SkinnedMeshRenderer[] _previewSmrs;     // P0:预览角色蒙皮缓存 — 离屏渲染需 updateWhenOffscreen
        private AnimationClip _previewClip;
        private List<AnimationClip> _previewClipCache = new List<AnimationClip>();
        private float _previewTime;
        // P1-8.2: AnimatorOverrideController 用于 Clip 路径的 Humanoid Avatar 重定向采样。
        // SampleAnimation 不经过 Avatar retargeting,肌肉空间曲线直接作用到骨骼 Transform 上位置错误。
        // OverrideController 让 Animator 在已知 state 上播放 _previewClip,获得与运行时一致的骨骼位姿。
        [System.NonSerialized] private AnimatorOverrideController _previewOverrideCtrl;
        [System.NonSerialized] private bool _previewOverrideIsTemporary;
        private string _previewOverrideStateName;  // Animator state 名（不是 OverrideController 的 Clip key）
        private int    _previewOverrideLayer;       // override state 所在的 layer 索引
        [System.NonSerialized] private AnimationClip _previewOverrideAnchorClip;
        private bool _previewPlaying;
        private double _previewLastEditorTime;
        // 播放速度倍率(0.25x ~ 2x)。StepAnimatorPlayback / TickPreviewRuntime 都按此倍率推 dt。
        // 编辑器下 EditorApplication.timeSinceStartup 不能改,只能在 Tick 时乘倍率。
        private float _previewPlaySpeed = 1.0f;
        // P4 固定步长预览:墙钟 dt 累积器,按 60fps 固定步长消费,保证 VFX 触发间距与运行时一致。
        private float _previewFixedAccum = 0f;
        // V3 修复:Animator 状态机走完后用 normalizedTime 重置;记录当前状态在 Animator 中的"已播秒数"
        private float _previewAnimElapsed;
        private float _previewAnimLength;               // 当前 Animator 状态总时长(秒),<=0 表示尚未启动
        private bool _previewAnimTriggered;             // 是否已发送过 SetTrigger(只发一次)
        private string _previewAnimResolvedState;       // 三级匹配后真正播放的 state 名
        private int    _previewResolvedLayer;           // state 所在的 layer 索引(由 ResolveAnimatorStateName 填入)
        private string _previewAnimStateMissing;        // 用户填的 animClipName 在 controller 中找不到(null = 正常)
        private float _previewFadeIn = 0.1f;            // P4:预览 CrossFade 时长,对齐运行时 fadeInDuration(启动时同步自 SkillData)
        private string _previewLastError;               // 最近一次预览异常信息(P1-2: 在预览面板顶部小字显示)
        private Vector2 _previewDir = new Vector2(120f, -25f);  // 相机方位角/俯角
        private float _previewZoom = 1.0f;
        private Bounds _previewBounds;
        private bool _previewBoundsValid;
        private static readonly int _previewCtrlId = "SBW.Preview".GetHashCode();
        private int _previewDragMode;       // 0=none 1=orbit 2=pan
        private Vector2 _previewDragStart;
        private Vector2 _previewDragStartDir;
        private float _previewDragStartZoom;

        // ── V3 武器挂载(预览用)──────────────────────────────────────
        // 编辑器拖入 WeaponData,RebuildPreviewInstance 时挂到预览实例上。
        // 直接走 WeaponSlot + WeaponMountPoint 的解析逻辑,不依赖 WeaponHolder 组件的 Awake/Start
        // (HideAndDontSave 克隆下 Awake/Start 不一定会跑,且运行时也走这层)。
        private WeaponData _previewMainHandWeapon;
        private WeaponData _previewOffHandWeapon;
        // 武器实例(预览实例的子物体),Dispose 时一并 DestroyImmediate
        private GameObject _previewMainHandInstance;
        private GameObject _previewOffHandInstance;

        // ── 修复 6:预览区联动 Timeline 播放头(默认开启)────────────────
        private bool _previewLinkTimeline = true;
        private float _previewClipStartT;   // 联动模式下,当前选中片段对应的 graphData 节点 triggerTime

        // ── V3 双轨 SkillData(预览独立于编辑)────────────────────────────
        private SkillData _previewTarget;            // 用户选的"要预览哪个 .asset"
        private WorkingCopySkillData _workingCopy;   // 预览用的深拷贝,所有 Edit 落这里
        private string _previewTargetPath;           // EditorPrefs 持久化 + mtime 检查用

        // ── V3 预览运行时 ───────────────────────────────────────────────
        private SkillPreviewRuntime _previewRuntime;
        private double _previewLastTickTime;
        // V3.1.11 修复:SimulateVFX 的 dt 基线,跟 _previewLastEditorTime(给 AdvancePreviewTime
        //  推进 _playheadT 用)解耦。VFX 推进 dt = "自上次 OnGUI 渲染以来真实过去时间",而不是
        //  "自上次播放模式 tick 以来"。这能保证:①ruler 拖动期间 VFX 不会一次性被 Simulate
        //  一个巨大 dt("突然出现") ②ruler 拖完一停,OnGUI 进来用真实 dt 平滑推进 VFX
        private double _previewLastSimulateVFXTime;
        // P1-6 ruler-drag: ruler 拖拽期间 VFX 需要继续用墙钟 dt 推进(否则粒子创建后冻结在 t=0)。
        // 与 _previewPlaying 解耦——暂停中拖拽 ruler 并非"播放",但 VFX 仍需渲染当前时间点的粒子状态。
        private bool _previewRulerDragging;
        private bool _previewSFXMute = true;         // 默认静音 SFX,Preview 面板可切换
        private int _previewFrameRate = 30;          // P3 5 跳转按钮用的帧率(独立于 _frameRate)

        // ── V3 源文件 mtime 弱提示 ──────────────────────────────────────
        private double _lastMtimeCheck;
        private string _lastKnownMtime;
        private bool _sourceMtimeDirty;

        // V3.1.6:参数区修改标记 — 检测到修改后在 OnEditorUpdate 重建 workingCopy
        private bool _paramsDirty;

        // P2-1:播放循环模式(true=循环,false=播完自停)
        private bool _previewLoopMode = true;

        // 2026-07-28(test07 对齐):显示开关 + 灯光参数(EditorPrefs 持久化,见 OnEnable/SavePreviewDisplayPrefs)
        private bool  _showHitRange = true;         // 预览窗 overlay:伤害范围
        private bool  _showRefAxes  = false;        // 预览窗 overlay:参考坐标轴(默认关,避免常规遮挡)
        private float _previewLightIntensity = 1.0f;
        private float _previewLightAzimuth   = 35f;   // 水平角(度)
        private float _previewLightElevation = 55f;   // 俯仰角(度)

        // P1-2:参数面板折叠分组 — key = "SBW.Section.{nodeIndex}.{sectionName}", value = bool expanded
        // 使用字典缓存,避免每帧 EditorPrefs 读写
        private Dictionary<string, bool> _nodeSectionFoldouts = new Dictionary<string, bool>();

        bool GetSectionFoldout(int nodeIndex, string section, bool defaultExpanded = true)
        {
            string key = $"{nodeIndex}:{section}";
            if (!_nodeSectionFoldouts.TryGetValue(key, out bool v))
            {
                v = EditorPrefs.GetBool($"SBW.Section.{nodeIndex}.{section}", defaultExpanded);
                _nodeSectionFoldouts[key] = v;
            }
            return v;
        }

        void SetSectionFoldout(int nodeIndex, string section, bool v)
        {
            string key = $"{nodeIndex}:{section}";
            _nodeSectionFoldouts[key] = v;
            EditorPrefs.SetBool($"SBW.Section.{nodeIndex}.{section}", v);
        }

        // ── 2026-07-28(test07 对齐):预览 display prefs(显示开关+灯光) ──
        void LoadPreviewDisplayPrefs()
        {
            _showHitRange          = EditorPrefs.GetBool ("SBW.Display.HitRange",       true);
            _showRefAxes           = EditorPrefs.GetBool ("SBW.Display.RefAxes",        false);
            _previewLightIntensity = EditorPrefs.GetFloat("SBW.Display.LightIntensity", 1.0f);
            _previewLightAzimuth   = EditorPrefs.GetFloat("SBW.Display.LightAzimuth",   35f);
            _previewLightElevation = EditorPrefs.GetFloat("SBW.Display.LightElevation", 55f);
            // 受击目标(2026-08-03)
            _previewTargetCount    = EditorPrefs.GetInt  ("SBW.Display.TargetCount",    1);
            _previewTargetDistance = EditorPrefs.GetFloat("SBW.Display.TargetDistance", 3f);
            _previewTargetArc      = EditorPrefs.GetFloat("SBW.Display.TargetArc",      60f);
            string targetGuid      = EditorPrefs.GetString("SBW.Display.TargetPrefab",  "");
            if (!string.IsNullOrEmpty(targetGuid))
                _previewTargetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    AssetDatabase.GUIDToAssetPath(targetGuid));
        }

        void SavePreviewDisplayPrefs()
        {
            EditorPrefs.SetBool ("SBW.Display.HitRange",       _showHitRange);
            EditorPrefs.SetBool ("SBW.Display.RefAxes",        _showRefAxes);
            EditorPrefs.SetFloat("SBW.Display.LightIntensity", _previewLightIntensity);
            EditorPrefs.SetFloat("SBW.Display.LightAzimuth",   _previewLightAzimuth);
            EditorPrefs.SetFloat("SBW.Display.LightElevation", _previewLightElevation);
            // 受击目标(prefab 存 GUID)
            EditorPrefs.SetInt  ("SBW.Display.TargetCount",    _previewTargetCount);
            EditorPrefs.SetFloat("SBW.Display.TargetDistance", _previewTargetDistance);
            EditorPrefs.SetFloat("SBW.Display.TargetArc",      _previewTargetArc);
            string targetPath = _previewTargetPrefab != null
                ? AssetDatabase.GetAssetPath(_previewTargetPrefab) : "";
            EditorPrefs.SetString("SBW.Display.TargetPrefab",
                string.IsNullOrEmpty(targetPath) ? "" : AssetDatabase.AssetPathToGUID(targetPath));
        }

        // 字段 → 所属分组(返回 null = 不分组,直接按原流程渲染)
        static string GetFieldSection(string fieldName)
        {
            switch (fieldName)
            {
                // 时序
                case "triggerTime":
                case "editorDuration":
                    return "⏱ 时序";
                // 空间/骨骼
                case "spawnBone":
                case "worldSpace":
                case "positionOffset":
                case "rotationOffset":
                case "scale":
                case "destroyAfterSeconds":
                    return "📍 位置/骨骼";
                // 伤害/战斗核心数值
                case "damage":
                case "range":
                case "hitRadius":
                case "hitAngle":
                case "originHeight":
                case "originForwardOffset":
                case "beamWidth":
                case "bounceCount":
                case "searchRadius":
                case "damageFalloff":
                case "maxBounceRange":
                case "radius":
                case "aoeRadius":
                case "centerIsTargetPoint":
                    return "⚔ 伤害/范围";
                // VFX/SFX
                case "prefab":
                case "sfx":
                case "sfxPitchRandomPercent":
                case "vfxOnCast":
                case "sfxOnCast":
                case "vfxOnHit":
                case "sfxOnHit":
                    return "✨ VFX/SFX";
                default:
                    return "⚙ 其他参数";
            }
        }

        // 方案 C(2026-07-06):统一的"标记预览需要重建"入口 —— 任何直接改 graphData 结构
        // (增删/重排/复制节点)的路径都调这个,替代分散重复的三行判断逻辑。
        //
        // P2 保存闭环:分两条路径:
        //   • _editingTarget 直改路径(Timeline/Gizmo 拖拽等) → 走 _paramsDirty → RebuildFromSource
        //   • Working 直改路径(ProcessPendingGraphEdits/AddLayerToGraph) → 走 MarkWorkingDirty
        void MarkPreviewParamsDirty()
        {
            if (_editingTarget == _previewTarget && _workingCopy != null)
            {
                _workingCopy.SnapshotForUndo();
                _paramsDirty = true;
            }
        }

        // P2 保存闭环:直接修改 WorkingCopy.Working 后调用,刷新预览 + 记录 Undo 快照。
        // 不走 _paramsDirty/RebuildFromSource(那会从 Source 覆盖掉我们对 Working 的改动)。
        void MarkWorkingDirty()
        {
            if (_workingCopy == null) return;
            _workingCopy.SnapshotForUndo();
            _previewRuntime?.Init(_previewInstance, _workingCopy);
            if (_previewPlaying && _previewRuntime != null)
            {
                double curT = _previewRuntime.CurrentT;
                _previewRuntime.Seek(curT);
            }
        }

        // P3:时间轴操作前调用 —— 把面板编辑(SerializedObject 修改)先写入 Source,
        //    这样时间轴操作对 _editingTarget 的改动不会在后续 RebuildFromSource 时
        //    覆盖掉面板编辑。只刷新不脏标记(会紧接着触发 _paramsDirty→RebuildFromSource)。
        void FlushWorkingToSource()
        {
            if (_workingCopy == null) return;
            _editingSO?.ApplyModifiedPropertiesWithoutUndo();
            _workingCopy.ConfirmWithoutSave();
        }

        // ── V3 帧显示 ───────────────────────────────────────────────────
        private int _previewCurFrame => Mathf.RoundToInt((float)_previewRuntime?.CurrentT * _previewFrameRate);

        [MenuItem("Tools/Skill Builder Wizard", false, 200)]
        static void Open()
        {
            var win = GetWindow<SkillBuilderWizard>("Skill Builder");
            win.minSize = new Vector2(800, 520);
            win.LoadLayoutPrefs();
        }

        // P1-4:从 CharacterKitPanel Skill Tab 传入 SkillData 直接定位编辑
        public static void EditSkill(SkillData target)
        {
            if (target == null) return;
            var win = GetWindow<SkillBuilderWizard>("Skill Builder");
            win.minSize = new Vector2(800, 520);
            win.LoadLayoutPrefs();
            // 切到"编辑现有"模式并定位到指定技能
            win._mode = Mode.EditExisting;
            win.ChangePreviewTarget(target);
            win._editingTarget = target;
            win._scrollX = 0f;
            win._multiSelectedIndices.Clear();
            win.RefreshEditingSO();
            EditorApplication.delayCall += win.FitTimelineToContent;
            win.Repaint();
            win.Focus();
        }

        void OnEnable()
        {
            LoadLayoutPrefs();
            LoadPreviewDisplayPrefs();   // 2026-07-28(test07 对齐):显示开关+灯光
            EnsureLayerStates();
            // Phase 5.4: 注册 SceneView Gizmo 绘制回调
            SceneView.duringSceneGui += OnSceneGui;
            // V3 修复:订阅 EditorApplication.update 驱动持续重绘,
            // 否则 _previewPlaying=true 时画面只在鼠标移动瞬间更新一次就冻住,
            // 速度倍率 / 暂停 / 上下帧全都看不出变化。update 回调里按需 Repaint 省 CPU。
            EditorApplication.update += OnEditorUpdate;
            // V3.1.11 修复:Unity 实际编译报 CS0103 — `wantsConstantRepaint` 在当前 Unity 版本
            //  (编译目标 API Level)找不到该符号。回退到"在 OnEditorUpdate 末尾主动 Repaint +
            //  QueuePlayerLoopUpdate"路线,经实测功能等价(EditorWindow 重绘时机仍受
            //  EditorApplication.update 频率驱动,约 10Hz,实测画面已能跟动)。

            // ── V3: 预览运行时 + 从 EditorPrefs 恢复 _previewTarget ──
            if (_previewRuntime == null) _previewRuntime = new SkillPreviewRuntime();
            _previewRuntime.SFXMute = _previewSFXMute;
            if (_previewInstance != null) _previewRuntime.Init(_previewInstance, _workingCopy);
            // V3.1.6 修复:订阅 VFX/SFX/Hit 事件(VFX 实例加入离屏场景,SFX/Hit 仅日志)
            _previewRuntime.OnVFXTriggered += OnPreviewVFX;
            _previewRuntime.OnSFXTriggered += OnPreviewSFX;
            _previewRuntime.OnHitTriggered += OnPreviewHit;

            var savedPath = EditorPrefs.GetString("SBW.PreviewTargetPath", "");
            if (!string.IsNullOrEmpty(savedPath))
            {
                var sd = AssetDatabase.LoadAssetAtPath<SkillData>(savedPath);
                if (sd != null)
                {
                    _previewTarget = sd;
                    _previewTargetPath = savedPath;
                    var inferredPrefab = AutoFindCharacterPrefab(savedPath);
                    if (inferredPrefab != null)
                    {
                        _previewPrefab = inferredPrefab;
                    }
                    else if (!IsPreviewPrefabForSkill(_previewPrefab, savedPath))
                    {
                        _previewPrefab = null;
                    }
                    _workingCopy = new WorkingCopySkillData(sd);
                    _previewRuntime.Init(_previewInstance, _workingCopy);
                    _previewRuntime.Reset();
                    _lastKnownMtime = TryReadMtime(savedPath);
                    if (_previewPrefab != null)
                    {
                        RebuildPreviewInstance();
                        _previewRuntime.Init(_previewInstance, _workingCopy);
                        RefreshPreviewClipCache();
                        AutoSelectClipForPlayhead();
                    }
                }
            }
        }

        void OnDisable()
        {
            // ── P2 保存闭环:窗口关闭前自动落盘 WorkingCopy 改动,防止数据丢失 ──
            // P2-3 修复:EnemySetup/外部工具可能在 wizard 打开期间修改了 Source 磁盘文件。
            //   此时 _lastKnownMtime < 当前磁盘 mtime,强制 Confirm 会用旧 WorkingCopy 覆盖
            //   掉外部刚写入的新数据(domain reload 触发 OnDisable → Confirm 踩数据)。
            //   检测到外部修改时跳过 Confirm,仅丢弃 WorkingCopy,保护外部生成结果。
            if (_workingCopy != null)
            {
                // Character Kit 批量生成 SkillData 时会主动关闭本窗口；此时 WorkingCopy
                // 可能是旧快照，禁止 OnDisable 自动回写，避免把新生成的 animClips/graphData 清空。
                bool suppressExternalAutoSave = EditorPrefs.GetBool("Test09.CharacterKit.SuppressSkillBuilderAutoSave", false);
                bool externalChanged = false;
                if (!string.IsNullOrEmpty(_previewTargetPath))
                {
                    var currentMtime = TryReadMtime(_previewTargetPath);
                    if (_lastKnownMtime != null && currentMtime != null
                        && string.CompareOrdinal(currentMtime, _lastKnownMtime) != 0)
                    {
                        externalChanged = true;
                    }
                }

                if (suppressExternalAutoSave)
                {
                    Debug.Log("[SkillBuilder] Character Kit 外部批量生成期间跳过 WorkingCopy 自动保存。");
                }
                else if (externalChanged)
                {
                    Debug.Log($"[SkillBuilder] 检测到外部工具已修改 {_previewTargetPath}，跳过自动保存以保护新数据。");
                    // 不 Confirm,不覆盖外部修改;直接丢弃 WorkingCopy。
                }
                else
                {
                    // 若 Timeline 直改过 _editingTarget 但 _paramsDirty 尚未处理,
                    // 先强制 RebuildFromSource 把改动同步到 Working,再 Confirm 落盘。
                    if (_paramsDirty)
                        _workingCopy.RebuildFromSource();
                    _workingCopy.Confirm();
                }
            }

            SaveLayoutPrefs();

            // ── 预览运行时:先取消事件订阅,再 Dispose + 置空 ──
            if (_previewRuntime != null)
            {
                _previewRuntime.OnVFXTriggered -= OnPreviewVFX;
                _previewRuntime.OnSFXTriggered -= OnPreviewSFX;
                _previewRuntime.OnHitTriggered -= OnPreviewHit;
                _previewRuntime.Dispose();
                _previewRuntime = null;
            }
            if (_workingCopy != null)
            {
                _workingCopy.Dispose();
                _workingCopy = null;
            }

            _editingSO?.Dispose();
            _editingSO = null;
            DisposePreview();
            // Phase 5.4: 取消 SceneView Gizmo 回调
            SceneView.duringSceneGui -= OnSceneGui;
            // V3 修复:注销持续重绘驱动
            EditorApplication.update -= OnEditorUpdate;

            // ── V3: 持久化 ──
            if (!string.IsNullOrEmpty(_previewTargetPath))
                EditorPrefs.SetString("SBW.PreviewTargetPath", _previewTargetPath);
            EditorPrefs.SetBool("SBW.PreviewSFXMute", _previewSFXMute);
        }

        /// <summary>安全替换 _editingSO,先释放旧实例避免内存泄漏。</summary>
        private void RefreshEditingSO()
        {
            _editingSO?.Dispose();
            _editingSO = new SerializedObject(_editingTarget);
            _editingSO.Update();
        }

        // V3 修复:Editor 持续 tick,仅在播放中 / 调速度时驱动 Repaint,
        // 暂停且无交互时静默,不浪费 CPU。Repaint 走 window.Repaint() 标记 dirty,
        // 下一帧 OnGUI 链路会重跑(底行按钮 / 渲染 / TickPreviewRuntime 一气呵成)。
        // V3.1.2 修复:
        //  1) 暂停时不调 EditorApplication.QueuePlayerLoopUpdate() — 这会让 Editor 引擎整体
        //     PlayerLoop 跑一次(Animator/物理/UI 全部 tick),跟手动 StepAnimatorPlayback 路径
        //     冲突/叠加,导致暂停后画面仍会"抽搐"一帧。
        //  2) 把 Runtime.Tick 也搬到 OnEditorUpdate(原来在 OnGUI 入口),跟 AdvancePreviewTime
        //     同步 — 之前 OnGUI 入口的 TickPreviewRuntime 在 MouseMove/MouseDrag 事件下
        //     被 Unity 多派发,会重复推 Tick 一次,导致"鼠标晃一下画面抖一下"。
        //  3) Repaint() 始终调(否则画面冻结),跟 _previewPlaying 解耦。
        void OnEditorUpdate()
        {
            // V3.1.6:参数区修改后实时同步到预览(重建 workingCopy + 重新触发当前帧 VFX)
            // P1-3 修复:旧版 Reset()+Tick(curT, 0.05) 会把区间 [0,curT] 内全部 VFX 一帧刷出
            // (因 Reset 后 lastT=0, Tick 判定 nodeT > 0 && nodeT <= curT 全部命中),
            // 表现为"拖拽参数后全部特效同时出现(不是逐帧触发)"。
            // 改为 Seek(curT):只更新 LastTickT=curT 不触发 VFX,正在播放的粒子继续存活,
            // 正常 Tick 循环会按区间递推自然触发之后的新 VFX。
            if (_paramsDirty)
            {
                _paramsDirty = false;
                if (_workingCopy != null)
                {
                    // P3 修复:时间轴操作(Drag/Delete/Duplicate/Reorder)直接改 _editingTarget(Source),
                    //   会触发 _paramsDirty → RebuildFromSource() 从 Source 重建 Working,
                    //   此时若 Working 有未提交的面板编辑就会被覆盖丢失。
                    //   修复策略:在每个时间轴操作开始前(而不是这里)调用 FlushWorkingToSource(),
                    //   把面板编辑先写入 Source,这样 RebuildFromSource 重建的 Working 包含完整数据。
                    //   这里只需单纯重建。
                    _workingCopy.RebuildFromSource();
                    _previewRuntime?.Init(_previewInstance, _workingCopy);
                    if (_previewPlaying && _previewRuntime != null)
                    {
                        double curT = _previewRuntime.CurrentT;
                        _previewRuntime.Seek(curT);
                    }
                }
            }

            if (_previewPlaying)
            {
                // P4:固定步长推进(动画+VFX 在同一 60fps 步长内联动,消除墙钟抖动)
                AdvancePreviewTime();
            }
            // V3.1.11 修复:`wantsConstantRepaint` 在当前 Unity 编译报 CS0103,改回
            //  "无条件 Repaint() + QueuePlayerLoopUpdate()" 路线。Repaint 标 dirty,
            //  QueuePlayerLoopUpdate 强制主循环跑一次并派发 Repaint 事件,保证鼠标停下
            //  / ruler 拖动期间画面实时跟动。两者都是 EditorWindow/EditorApplication 公开
            //  API,跨版本稳定。VFX 推进 dt 的解耦(改用 _previewLastSimulateVFXTime)保留。
            Repaint();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        float GetPreviewTimelineEnd()
        {
            var data = GetActivePreviewData();
            float end = _timelineMaxT;
            if (data?.graphData != null)
            {
                for (int i = 0; i < data.graphData.Count; i++)
                {
                    var node = data.graphData[i];
                    if (node == null) continue;
                    end = Mathf.Max(end, GetNodeTriggerTime(node) + Mathf.Max(0f, node.editorDuration));
                }
            }
            return Mathf.Max(0.01f, end);
        }

        // V3.1.1 修复:统一的时间推进入口(从 DrawPreviewPanel 底行 1010-1035 搬出来)
        // P4 固定步长:墙钟 dt → 累积器 → 按 60fps 固定步长消费。每个步长内:
        //   1) 推进动画时间  2) 同步骨骼位姿  3) 触发 VFX/SFX/Hit
        // 优点:VFX 触发间距严格 = 0.016s(与运行时一致),不受 Editor 帧率/卡顿影响。
        const float PREVIEW_FIXED_DT = 0.016f;  // 60fps ≈ 16.7ms
        const int   PREVIEW_MAX_STEPS = 6;      // 单帧最多推 6 步(支撑 2x 速 @30fps Editor)
        void AdvancePreviewTime()
        {
            double now = EditorApplication.timeSinceStartup;
            if (_previewLastEditorTime <= 0d) _previewLastEditorTime = now;
            float realDt = (float)(now - _previewLastEditorTime);
            _previewLastEditorTime = now;
            if (realDt < 0f) return;

            // ── 一次性守卫(原 TickPreviewRuntime 入口) ──
            if (_previewRuntime == null) return;
            if (_previewInstance != _previewRuntime.PreviewInstance)
                _previewRuntime.PreviewInstance = _previewInstance;
            if (_previewRuntime.Working != _workingCopy)
                _previewRuntime.Init(_previewInstance, _workingCopy);
            _previewRuntime.SFXMute = _previewSFXMute;

            // ── 固定步长消费 ──
            _previewFixedAccum += realDt * _previewPlaySpeed;
            int steps = 0;

            while (_previewFixedAccum >= PREVIEW_FIXED_DT && steps < PREVIEW_MAX_STEPS)
            {
                _previewFixedAccum -= PREVIEW_FIXED_DT;
                steps++;

                // ① 推进动画时间
                if (_previewLinkTimeline)
                {
                    _playheadT += PREVIEW_FIXED_DT;
                    float loopMax = GetPreviewTimelineEnd();
                    if (loopMax > 0f)
                    {
                        if (_previewLoopMode)
                        {
                            float beforeRepeat = _playheadT;
                            _playheadT = Mathf.Repeat(_playheadT, loopMax);
                            if (beforeRepeat >= loopMax) _previewRuntime.Reset();
                        }
                        else if (_playheadT >= loopMax)
                        {
                            _playheadT = loopMax;
                            PausePreviewPlayback();
                        }
                    }
                }
                else if (_previewClip != null)
                {
                    _previewTime += PREVIEW_FIXED_DT;
                    if (_previewTime >= _previewClip.length)
                    {
                        if (_previewLoopMode) { _previewTime = 0f; _previewRuntime.Reset(); }
                        else { _previewTime = _previewClip.length; PausePreviewPlayback(); }
                    }
                }
                else if (_previewActor != null)
                {
                    if (_previewActor != null) _previewActor.speed = _previewPlaySpeed;
                    StepAnimatorPlayback(PREVIEW_FIXED_DT);
                }

                // ② 同步骨骼位姿(VFX 挂接需要当前帧骨骼)
                SyncPreviewPoseForTick();

                // ③ 触发 VFX/SFX/Hit(固定步长 dt)
                if (_previewClip != null)
                {
                    if (_previewLinkTimeline)
                    {
                        double tAbs = _playheadT;
                        double dtt = tAbs - _previewRuntime.LastTickT;
                        if (dtt < 0) _previewRuntime.Seek(tAbs);
                        else if (dtt > 0) _previewRuntime.Tick(tAbs, dtt);
                    }
                    else
                    {
                        // 非联动 Clip:从 LastTickT 固定前进一步
                        double tAbs = _previewRuntime.LastTickT + PREVIEW_FIXED_DT;
                        if (tAbs > _previewClip.length) tAbs = _previewClip.length;
                        double dtt = tAbs - _previewRuntime.LastTickT;
                        if (dtt > 0) _previewRuntime.Tick(tAbs, dtt);
                    }
                }
                else if (_previewActor != null)
                {
                    double lastT = _previewRuntime.LastTickT;
                    double dtt = _previewAnimElapsed - lastT;
                    if (dtt > 0.5) dtt = 0.5;
                    if (dtt >= 0d) _previewRuntime.Tick(_previewAnimElapsed, dtt);
                }

                // ④ 逐帧推进粒子系统(Editor 不会自动驱动 ParticleSystem,须手动 Simulate)
                _previewRuntime.SimulateVFX(PREVIEW_FIXED_DT);
            }

            // 防止溢出:长期暂停后恢复时 accumulator 可能累积巨大值
            if (_previewFixedAccum > PREVIEW_FIXED_DT * PREVIEW_MAX_STEPS * 2f)
                _previewFixedAccum = PREVIEW_FIXED_DT * PREVIEW_MAX_STEPS;
        }

        // P1-8:在 Tick 生成 VFX 之前把预览实例骨骼位姿更新到当前时间。
        //
        // 核心问题:人形角色(Humanoid)的 AnimationClip.SampleAnimation() 不经过 Animator 的
        //   Avatar 重定向,返回的是原始骨骼位姿(与运行时 Animator.GetBoneTransform 不一致)。
        //   这导致 spawnBone="RightHand" 等骨骼挂点上的 VFX 位置/朝向在编辑器和运行时完全不同。
        //
        // 修复:Clip 路径如果存在 Animator(有人形控制器),改用 Animator.Play+Update(0) 采样,
        //       通过 Avatar 重定向得到与运行时一致的骨骼位姿。
        //       无 Animator(Generic 骨架等)回退到 SampleAnimation。
        //
        // Animator 路径:AdvancePreviewTime 已通过 StepAnimatorPlayback 推进 Animator,
        //   此处不做额外推进(原联动路径的打补丁 Update 会导致 Animator 被双推一份 dt,
        //   _previewAnimElapsed 以 2x 速增长,VFX 触发时间错位)。
        void SyncPreviewPoseForTick()
        {
            if (_previewInstance == null) return;

            if (_previewClip != null)
            {
                float sampleT = _previewLinkTimeline
                    ? (_playheadT - _previewClipStartT)
                    : _previewTime;
                sampleT = Mathf.Clamp(sampleT, 0f, _previewClip.length);

                // P1-8.2: Humanoid 角色通过 AnimatorOverrideController 采样,
                // 获得 Avatar 重定向后的正确骨骼位姿。无 Animator 回退 SampleAnimation。
                // P1-8.5: 自愈——HideAndDontSave 实例的 Animator 可能未走 OnEnable,controller 仍为 null
                if (_previewActor != null && _previewActor.runtimeAnimatorController == null)
                    TryAssignControllerFromPrefab();
                if (_previewActor != null && _previewActor.runtimeAnimatorController != null)
                {
                    EnsurePreviewOverrideController();
                    if (_previewOverrideCtrl != null && _previewOverrideAnchorClip != null)
                    {
                        _previewOverrideCtrl[_previewOverrideAnchorClip] = _previewClip;
                        float normT = _previewClip.length > 0f ? sampleT / _previewClip.length : 0f;
                        try
                        {
                            if (_previewActor.runtimeAnimatorController != null)
                            {
                                // P1-8.7 修复(test07 对齐):多 controller 场景层权重残留 → 0 权重层播放不可见(T-pose 假"没动")
                                if (_previewOverrideLayer > 0)
                                    _previewActor.SetLayerWeight(_previewOverrideLayer, 1f);
                                _previewActor.Play(_previewOverrideStateName, _previewOverrideLayer, normT);
                                _previewActor.Update(0f);
                            }
                            else
                            {
                                _previewClip.SampleAnimation(_previewInstance, sampleT);
                            }
                        }
                        catch
                        {
                            _previewClip.SampleAnimation(_previewInstance, sampleT);
                        }
                    }
                    else
                    {
                        _previewClip.SampleAnimation(_previewInstance, sampleT);
                    }
                }
                else
                {
                    _previewClip.SampleAnimation(_previewInstance, sampleT);
                }
            }
            // Animator 路径:骨骼已在 AdvancePreviewTime→StepAnimatorPlayback 中更新,
            // 无需在此再次推进（原本 P1-7 联动路径的额外 Update 导致双倍时间累加,已移除）。
        }

        void LoadLayoutPrefs()
        {
            _previewW = EditorPrefs.GetFloat(PREF_PREVIEW_W, 320f);
            _timelineH = EditorPrefs.GetFloat(PREF_TIMELINE_H, 240f);
            _layersW = EditorPrefs.GetFloat(PREF_LAYERS_W, 110f);
            // Phase 10.3: 加载缩放/帧率/Snap
            _pixelsPerSecond = EditorPrefs.GetFloat(PREF_PPS, 80f);
            _frameRate = EditorPrefs.GetFloat(PREF_FPS, 30f);
            _snapToFrame = EditorPrefs.GetBool(PREF_SNAP, true);
            // V3: 预览独立帧率 + SFX mute 偏好
            _previewFrameRate = EditorPrefs.GetInt("SBW.PreviewFrameRate", 30);
            _previewSFXMute = EditorPrefs.GetBool("SBW.PreviewSFXMute", true);
        }

        void SaveLayoutPrefs()
        {
            EditorPrefs.SetFloat(PREF_PREVIEW_W, _previewW);
            EditorPrefs.SetFloat(PREF_TIMELINE_H, _timelineH);
            EditorPrefs.SetFloat(PREF_LAYERS_W, _layersW);
            // Phase 10.3: 保存缩放/帧率/Snap
            EditorPrefs.SetFloat(PREF_PPS, _pixelsPerSecond);
            EditorPrefs.SetFloat(PREF_FPS, _frameRate);
            EditorPrefs.SetBool(PREF_SNAP, _snapToFrame);
            // V3: 预览独立帧率
            EditorPrefs.SetInt("SBW.PreviewFrameRate", _previewFrameRate);
        }

        // 初始化/对齐 _layerStates 到 graphData 节点数
        // 行号 = 节点在 graphData 的索引;每节点一行的可见/锁/重命名状态独立存
        void EnsureLayerStates()
        {
            int n = 0;
            if (_editingTarget != null && _editingTarget.graphData != null)
                n = _editingTarget.graphData.Count;
            if (_layerStates == null) _layerStates = new LayerState[0];
            if (_layerStates.Length == n) return;
            var old = _layerStates;
            _layerStates = new LayerState[n];
            int copy = Mathf.Min(old.Length, n);
            for (int i = 0; i < copy; i++) _layerStates[i] = old[i];
            for (int i = copy; i < n; i++) _layerStates[i] = new LayerState();
        }

        // ═══════════════════════════════════════════════════════════════
        // OnGUI — 三段式主入口
        // ═══════════════════════════════════════════════════════════════
        void OnGUI()
        {
            // V3 P4:Ctrl+Z 优先吃 WorkingCopy.Undo 栈(深度 32,JSON 快照)
            if (Event.current.type == EventType.KeyDown && Event.current.control && Event.current.keyCode == KeyCode.Z)
            {
                if (_workingCopy != null && _workingCopy.Undo())
                {
                    Event.current.Use();
                    Repaint();
                    GUIUtility.ExitGUI();
                }
            }
            // Ctrl+Y / Ctrl+Shift+Z → WorkingCopy Redo
            if (Event.current.type == EventType.KeyDown && Event.current.control
                && (Event.current.keyCode == KeyCode.Y || (Event.current.shift && Event.current.keyCode == KeyCode.Z)))
            {
                if (_workingCopy != null && _workingCopy.Redo())
                {
                    Event.current.Use();
                    Repaint();
                    GUIUtility.ExitGUI();
                }
            }

            // ── P0 延迟 graphData 操作(任意 GUILayout 调用前执行)──
            ProcessPendingGraphEdits();

            EnsureLayerStates();
            // 顶层区域(扣除顶部模式条 32px 留给 ModeBar)
            var topBarRect = new Rect(0, 0, position.width, 32f);
            DrawModeBar(topBarRect);

            var workArea = new Rect(0, topBarRect.yMax, position.width, position.height - topBarRect.height);

            // 水平分隔条 Y = workArea.yMax - _timelineH
            float splitY = workArea.yMax - _timelineH;
            // 上下夹紧(防止拖出窗口)
            _timelineH = Mathf.Clamp(_timelineH, SPLIT_MIN, workArea.height - SPLIT_MIN);
            splitY = workArea.yMax - _timelineH;

            var upperArea = new Rect(workArea.x, workArea.y, workArea.width, splitY - workArea.y);
            var timelineArea = new Rect(workArea.x, splitY + SPLIT_THICKNESS,
                                         workArea.width, workArea.yMax - (splitY + SPLIT_THICKNESS));

            // 垂直分隔条 X = upperArea.x + _previewW
            // 夹紧
            _previewW = Mathf.Clamp(_previewW, SPLIT_MIN, upperArea.width - SPLIT_MIN);
            float splitX = upperArea.x + _previewW;
            var previewArea = new Rect(upperArea.x, upperArea.y, _previewW, upperArea.height);
            var paramsArea = new Rect(splitX + SPLIT_THICKNESS, upperArea.y,
                                       upperArea.width - (splitX + SPLIT_THICKNESS) - upperArea.x, upperArea.height);

            // ── 绘制 4 个面板 ──
            DrawPreviewPanel(previewArea);
            DrawParamsPanel(paramsArea);
            DrawTimelinePanel(timelineArea);

            // ── 绘制 2 个分隔条(盖在最上层,可拖) ──
            DrawVSplitHandle(new Rect(splitX, upperArea.y, SPLIT_THICKNESS, upperArea.height));
            DrawHSplitHandle(new Rect(workArea.x, splitY, workArea.width, SPLIT_THICKNESS));

            // 拖动结束时立即重绘 + 保存
            if ((_draggingVSplit || _draggingHSplit) && Event.current.type == EventType.MouseUp)
            {
                _draggingVSplit = false;
                _draggingHSplit = false;
                GUIUtility.hotControl = 0;
                SaveLayoutPrefs();
                Repaint();
            }

            // V3 P2:每帧推进 Preview Runtime(VFX 实例化 / SFX 静音播放 / 命中框事件)
            // V3:0.5s 节流扫源文件 mtime
            CheckSourceMtime();
            // V3.1.2 修复:TickPreviewRuntime 已搬到 OnEditorUpdate(跟 AdvancePreviewTime 同步),
            //  跟 OnGUI 事件完全解耦。这里不再调,避免 MouseMove/MouseDrag 事件下重复推 Tick。
        }

        // ── P0 延迟 graphData 操作:在 OnGUI 入口、任意 GUILayout 调用前执行 ──
        // 将按钮点击触发的 graphData 变更延迟到下一帧,避免同一帧内 Layout 和
        // Repaint 遍历不同数量的节点导致 "Getting control N's position in a
        // group with only M controls" 错误。
        void ProcessPendingGraphEdits()
        {
            // P2 保存闭环:优先编辑 WorkingCopy.Working(与 Confirm 路径一致),兜底 _editingTarget
            SkillData target = (_workingCopy != null) ? _workingCopy.Working : _editingTarget;
            if (target == null) return;
            var so = new SerializedObject(target);
            var graphProp = so.FindProperty("graphData");
            if (graphProp == null) return;
            bool changed = false;

            // ── 处理待删除节点 ──
            if (_pendingNodeDeletionIndex >= 0 && _pendingNodeDeletionIndex < graphProp.arraySize)
            {
                graphProp.DeleteArrayElementAtIndex(_pendingNodeDeletionIndex);
                if (_expandedNodeIndex == _pendingNodeDeletionIndex) _expandedNodeIndex = -1;
                changed = true;
            }
            _pendingNodeDeletionIndex = -1;

            // ── 处理待移动节点(上移/下移) ──
            if (_pendingNodeMoveFrom >= 0 && _pendingNodeMoveFrom < graphProp.arraySize)
            {
                // _pendingNodeMoveFrom 的低位存 from,高位存 direction(+1=下移, -1=上移)
                int from = _pendingNodeMoveFrom & 0xFFFF;
                int dir  = (short)(_pendingNodeMoveFrom >> 16);
                int to   = from + dir;
                if (dir != 0 && to >= 0 && to < graphProp.arraySize)
                {
                    graphProp.MoveArrayElement(from, to);
                    _expandedNodeIndex = to;
                    changed = true;
                }
            }
            _pendingNodeMoveFrom = -1;

            if (changed)
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
                if (_workingCopy != null)
                    MarkWorkingDirty();  // P2:直接改 Working,不需 RebuildFromSource
                else
                    EditorUtility.SetDirty(_editingTarget);
                Repaint();
            }
        }

        // ── V3 辅助方法 ───────────────────────────────────────────────
        // 读 .asset 文件的最后写入时间(ISO 8601 字符串);文件不存在返回 null。
        // V3 修复:返回"当前预览应使用的数据源"
        //   优先 _workingCopy.Working(工作副本,可与 .asset 解耦)
        //   兜底 _editingTarget(老路径兼容)
        //   都没有返回 null
        SkillData GetActivePreviewData()
        {
            if (_workingCopy != null && _workingCopy.Working != null) return _workingCopy.Working;
            return _editingTarget;
        }

        string TryReadMtime(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            try
            {
                var fullPath = System.IO.Path.GetFullPath(assetPath);
                if (!System.IO.File.Exists(fullPath)) return null;
                var t = System.IO.File.GetLastWriteTimeUtc(fullPath);
                return t.ToString("o");
            }
            catch (Exception ex) { Debug.LogWarning($"[SkillBuilder] 读取 mtime 失败: {assetPath} — {ex.Message}"); return null; }
        }

        // 0.5s 节流,扫一次 mtime;变了就把 _sourceMtimeDirty 标 true,DrawPreviewPanel 顶部显示黄条提示
        void CheckSourceMtime()
        {
            if (_previewTarget == null) return;
            if (string.IsNullOrEmpty(_previewTargetPath)) return;
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastMtimeCheck < 0.5) return;
            _lastMtimeCheck = now;
            var m = TryReadMtime(_previewTargetPath);
            if (m == null) return;
            if (_lastKnownMtime == null) { _lastKnownMtime = m; return; }
            if (m != _lastKnownMtime) _sourceMtimeDirty = true;
        }

        // 推进预览运行时:每帧从 _playheadT 走,联动 Timeline 播放头
        // V3.1.1 修复:全路径加 _previewPlaying 闸门。
        //  旧版 Animator 路径(else if _previewPlaying)和联动模式(无 _previewPlaying 判断)
        //  会在"暂停"后继续推一帧,然后被钳 0.5s 锁住 — 用户体验是"按暂停画面会再跳一帧然后停"。
        //  显式判断 _previewPlaying=false → 直接 return,完全停。
        // V3.1.1 修复:联动模式 dt 也乘 _previewPlaySpeed —— 旧注释说"OnEditorUpdate 已经乘过",
        //  但实测 _previewPlaySpeed 改了之后 1x→0.5x 时联动模式无效果,因为 OnEditorUpdate
        //  推的 _playheadT 已经含速度倍率,这里 dt = t-lastT 拿到的是"OnGUI 间隔内的播放头增量",
        //  这一段本身已经被速度调制过,这里再乘一次 = 双乘 0.25x,跟旧版问题一致。
        //  修正:OnEditorUpdate 推 _playheadT 不再乘倍率(它推的是"实时游标",不含速度),
        //       TickPreviewRuntime 拿 dt 后按 _previewPlaySpeed 缩放 — 这样两路径都吃同一份倍率。
        void TickPreviewRuntime()
        {
            if (_previewRuntime == null) return;
            // V3.1.1 修复:暂停闸门,Animator / 非联动模式 / 联动模式都立刻停(不再推 0.5s 锁帧)
            if (!_previewPlaying) return;
            if (_previewInstance != _previewRuntime.PreviewInstance)
                _previewRuntime.PreviewInstance = _previewInstance;
            if (_previewRuntime.Working != _workingCopy)
                _previewRuntime.Init(_previewInstance, _workingCopy);
            _previewRuntime.SFXMute = _previewSFXMute;

            // ── 路径:Clip > Animator > 联动(仅当有 clip 时) ──
            if (_previewClip != null)
            {
                if (_previewLinkTimeline)
                {
                    double t = _playheadT;
                    double lastT = _previewRuntime.LastTickT;
                    double dt = t - lastT;
                    if (dt > 0.5) dt = 0.5;
                    if (dt < 0) _previewRuntime.Seek(t);
                    else if (dt > 0) _previewRuntime.Tick(t, dt);
                }
                else
                {
                    double now = EditorApplication.timeSinceStartup;
                    float dt = (float)(now - _previewLastTickTime);
                    _previewLastTickTime = now;
                    if (dt > 0.5f) dt = 0.5f;
                    if (dt > 0) _previewRuntime.Tick(_previewRuntime.CurrentT + dt, dt);
                }
            }
            else if (_previewActor != null)
            {
                // Animator 路径:用 _previewAnimElapsed 推 Tick,让 VFX/SFX/Hit 触发。
                // P1-5 修复:dt 直接用 _previewAnimElapsed - LastTickT 算,不碰墙钟。
                // 旧版 dt=(now-_previewLastTickTime)*speed 在 OnEditorUpdate 单帧内
                // EditorApplication.timeSinceStartup 可能返回同值→dt=0→Tick 跳过
                // →LastTickT 落后于 _previewAnimElapsed→下帧区间膨胀→全部 VFX 一帧刷出。
                // 直接用动画时间差做 dt 消除了 wall-clock 同值问题,同时 dt 严格等于
                // _previewAnimElapsed 的实际帧推进量,不依赖 external clock。
                double lastT = _previewRuntime.LastTickT;
                double dt = _previewAnimElapsed - lastT;
                if (dt > 0.5) dt = 0.5;
                if (dt >= 0d) _previewRuntime.Tick(_previewAnimElapsed, dt);
            }
        }

        // V3.1 修复:把"按 Play"的逻辑抽出来供 Space 键复用
        //  底行 965-983 的逻辑:toggle _previewPlaying=true + 重置 dt 基线 + (Animator)StartAnimatorPlayback
        void StartPreviewPlayback()
        {
            // P1-2:跟踪本次 StartPlayback 是否触发了 Runtime.Reset(含 atEnd 自动重启)。
            bool runtimeWasReset = false;
            // P0:非循环模式播完后 playhead 停在终点,直接点 Play 应从头开始。
            //    检测 playhead 是否已到终末并自动 Reset。
            // P2-2:在循环模式也要检测 — 若播放头在 clip 末尾被 OnGUI 自动停止,
            //    不 Reset 的话再次点 Play 会立刻再停止（死锁）。
            {
                bool atEnd = false;
                if (_previewLinkTimeline)
                {
                    float loopMax = GetPreviewTimelineEnd();
                    if (loopMax > 0f && _playheadT >= loopMax - 0.001f) atEnd = true;
                    // P2-2:联动模式下播放头可能停在 clip 末尾(非 timeline 末尾),也需要 Reset
                    if (!atEnd && _previewClip != null && _previewClipStartT + _previewClip.length > 0f
                        && _playheadT >= _previewClipStartT + _previewClip.length - 0.001f) atEnd = true;
                }
                else if (_previewClip != null && _previewTime >= _previewClip.length - 0.001f) atEnd = true;
                if (atEnd)
                {
                    if (_previewLinkTimeline) _playheadT = 0f;
                    _previewTime = 0f;
                    _previewRuntime?.Reset();
                    runtimeWasReset = true;
                }
            }
            _previewPlaying = true;
            // ── SkillTrace: 统一诊断日志（仅首帧启动时输出一次） ──
            {
                var traceData = GetActivePreviewData();
                if (traceData != null && _previewActor != null)
                {
                    string traceState = _previewAnimResolvedState;
                    if (string.IsNullOrEmpty(traceState) && !string.IsNullOrEmpty(traceData.animClipName))
                        traceState = ResolveAnimatorStateName(_previewActor, traceData.animClipName);
                    if (string.IsNullOrEmpty(traceState))
                        traceState = traceData.animClipName ?? "n/a";

                    string traceClip = _previewClip != null ? _previewClip.name
                        : (!string.IsNullOrEmpty(traceData.animClipName) ? traceData.animClipName : "n/a");
                    float traceClipLen = _previewClip != null ? _previewClip.length
                        : (_previewAnimLength > 0f ? _previewAnimLength : 1f);

                    SkillTrace.LogPreview(
                        _previewActor, traceData, traceState, 0,
                        traceClip, traceClipLen,
                        traceData.frontSwing, traceData.backSwing);

                    // P1 时间单位收敛：将 clipLength 传给 Runtime，
                    // 使 GetPreviewTriggerTime 能将归一化 frontSwing 转为秒。
                    if (_previewRuntime != null)
                        _previewRuntime.PreviewClipLength = traceClipLen;
                }
            }
            _previewLastSimulateVFXTime = 0d;  // 重置墙钟基线,避免 resume 后含暂停期
            // 重新播放:从当前位置继续(已暂停)或从头开始(已停止)
            // 用 _previewLastEditorTime 重置,避免按 Resume 时 dt 跳到巨大值
            _previewLastEditorTime = EditorApplication.timeSinceStartup;
            _previewLastTickTime = EditorApplication.timeSinceStartup;
            // V3.1 修复:联动模式 Runtime.LastTickT 是 private set,无法直接改。
            //  Resume 时 Runtime 会算 dt = _playheadT - LastTickT,
            //  暂停期间 LastTickT 不动,Resume 后第一次 Tick 拿到巨大 dt 一次刷一堆 VFX。
            //  兜底:在 TickPreviewRuntime 入口钳 dt <= 0.5s,即使上面误算也不影响。
            //  跨长时间暂停(切回 Play 模式)走 StepPreviewFrame 调 Runtime.Seek 显式重置,正常路径 OK。
            // P1-2:Animator 路径仅在新启动 / atEnd 重启时调 StartAnimatorPlayback,
            //  暂停→继续时不再重启动画,避免 _previewAnimElapsed 回 0 与 Runtime.LastTickT 错位
            //  导致 Tick 时间倒退→节点不触发→LastTickT 污染(偶现"暂停/重新播放后特效不触发")。
            // 重新确认当前 SkillData 的动画候选，避免首次加载时 _previewClip 仍为空而走待机状态。
            RefreshPreviewClipCache();
            AutoSelectClipForPlayhead();
            if (_previewActor != null)
            {
                if (_previewClip == null && (runtimeWasReset || _previewRuntime.LastTickT < 0.0001))
                    StartAnimatorPlayback();
                else if (_previewClip != null && runtimeWasReset)
                {
                    ResetAnimatorPlayState();
                    EnsurePreviewOverrideController();
                }
            }
            // V3.1.2 修复:立刻同步 Animator.speed,跟 _previewPlaySpeed 一致
            //  (StartAnimatorPlayback 内部 delayCall 推一帧才会发 SetTrigger,这里先设 speed)
            if (_previewActor != null) _previewActor.speed = _previewPlaySpeed;
        }

        // V3 修复:停止播放 — 重置 _previewPlaying + 时间游标到 0 + 清空 Runtime 池(销毁已生成的 VFX/SFX/HitGizmo)
        // 按下后画面立刻定格到 t=0(若 _previewClip 有则 sample 到头,若 Animator 路径则停在 default state),
        // 下次再按 Play 从头开始(StartAnimatorPlayback 内部已带 _previewAnimElapsed = 0)。
        void StopPreviewPlayback()
        {
            _previewPlaying = false;
            _previewLastSimulateVFXTime = 0d;
            if (_previewLinkTimeline) _playheadT = 0f;
            _previewTime = 0f;
            // P0:Reset()= Dispose 销毁所有 VFX/SFX/HitGizmo + 清空 _topLevelXxxFired
            //    确保 Stop 后旧特效不留存,下次 Play 时 vfxOnCast 可重新触发。
            _previewRuntime?.Reset();
            // Animator 状态机路径:把状态拉回 default state(从 SkillIdle 之类回到 start),但不重新触发 SetTrigger
            if (_previewActor != null && _previewActor.runtimeAnimatorController != null
                && !string.IsNullOrEmpty(_previewAnimResolvedState))
            {
                try { _previewActor.Play(_previewAnimResolvedState, _previewResolvedLayer, 0f); _previewActor.Update(0f); } catch (Exception ex) { _previewLastError = $"停止预览 Animator.Play({_previewAnimResolvedState}) 失败: {ex.Message}"; }
            }
            // P1-8.7: 重置 skill layer weight 到 0 (SkillFullBody/SkillUpperBody)
            if (_previewActor != null && _previewResolvedLayer > 0)
                _previewActor.SetLayerWeight(_previewResolvedLayer, 0f);
            ResetAnimatorPlayState();
            Repaint();
        }

        // V3 修复:挂主手/副手武器到预览实例。
        // 直接走 WeaponMountPoint.Resolve()(和运行时 WeaponHolder 共用一套骨骼解析,行为一致),
        // 不依赖 WeaponHolder.Awake — HideAndDontSave 克隆下 Awake/Start 不一定跑。
        // 调用时机:每次 RebuildPreviewInstance 末尾;UI 改 WeaponData 时再单独调 Reapply。
        void MountPreviewWeapons()
        {
            if (_previewInstance == null) return;
            MountOnePreviewWeapon(_previewMainHandWeapon, WeaponSlotType.MainHand, ref _previewMainHandInstance);
            MountOnePreviewWeapon(_previewOffHandWeapon, WeaponSlotType.OffHand, ref _previewOffHandInstance);
        }

        void MountOnePreviewWeapon(WeaponData data, WeaponSlotType slot, ref GameObject instanceRef)
        {
            // 先清掉旧的
            DestroyPreviewWeapon(ref instanceRef);
            if (data == null || data.prefab == null) return;
            // 必须读取角色 Prefab 上 WeaponHolder 的真实 mountPoints。运行时会用这里的
            // 自定义骨骼/槽位配置；此前固定使用左右手，导致采用自定义挂点的角色中武器及
            // 武器子级 VFX 挂点整体旋转偏移。
            Transform bone = null;
            var holder = _previewInstance.GetComponent<WeaponHolder>();
            if (holder != null && holder.mountPoints != null)
            {
                for (int i = 0; i < holder.mountPoints.Length; i++)
                {
                    if (holder.mountPoints[i] != null && holder.mountPoints[i].slotType == slot)
                    {
                        bone = holder.mountPoints[i].Resolve(_previewActor, _previewInstance.transform);
                        break;
                    }
                }
            }
            if (bone == null)
            {
                var fallback = new WeaponMountPoint
                {
                    slotType = slot,
                    bone = slot == WeaponSlotType.MainHand ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand
                };
                bone = fallback.Resolve(_previewActor, _previewInstance.transform);
            }
            if (bone == null) bone = _previewInstance.transform;
            // 实例化 + 设偏移(走 WeaponData 战斗位字段,holsterOffset 给运行时切武器用,这里不用)
            var go = (GameObject)UnityEngine.Object.Instantiate(data.prefab, bone);
            go.name = $"PreviewWeapon_{slot}_{data.weaponName}";
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.localPosition = data.positionOffset;
            go.transform.localRotation = Quaternion.Euler(data.rotationOffset);
            go.transform.localScale = data.scale;
            instanceRef = go;
        }

        // 公开给 UI 用:用户改了 WeaponData 字段后重新挂(不重建整个预览实例)
        void ReapplyPreviewWeapons()
        {
            MountPreviewWeapons();
            Repaint();
        }

        // V3.1.5 修复:逐帧变化 + 特效/音效播放
        //  1) Animator 路径:同步 resolve(不等 delayCall),直接 Play+Update 更新 pose
        //  2) Runtime 区间:不再 Seek→Tick(Seek 把 LastTickT 设到 nextT 导致区间为空),
        //     改用先 Tick(curT→nextT) 扫过 graphData 节点,再 Seek 重置池
        void StepPreviewFrame(int deltaFrames)
        {
            if (_previewRuntime == null) return;
            float curT = (float)_previewRuntime.CurrentT;
            float fps = Mathf.Max(1, _previewFrameRate);
            float step = deltaFrames / fps;
            float nextT = Mathf.Max(0f, curT + step);

            // ── 路径判断:Clip优先,其次Animator,最后联动 ──
            bool isClipPath = _previewClip != null;
            bool isAnimatorPath = !isClipPath && _previewActor != null;

            // Animator 路径:若尚未初始化,同步 resolve
            if (isAnimatorPath && string.IsNullOrEmpty(_previewAnimResolvedState))
            {
                if (_previewActor.runtimeAnimatorController == null)
                    TryAssignControllerFromPrefab();
                _previewActor.Rebind();
                _previewActor.Update(0f);
                var data = GetActivePreviewData();
                string clipName = data != null ? data.animClipName : "";
                // P1-8.7: 当 animClipName 为空时按 trigger/stateId 推导 state 名
                if (data != null && string.IsNullOrEmpty(clipName))
                    clipName = DerivePreviewStateName(data);
                _previewAnimResolvedState = ResolveAnimatorStateName(_previewActor, clipName);
                _previewAnimLength = GetActiveAnimatorStateLength();
                if (data != null && !string.IsNullOrEmpty(data.animTrigger)
                    && HasAnimatorParameter(_previewActor, data.animTrigger))
                {
                    try { _previewActor.SetTrigger(data.animTrigger); } catch (Exception ex) { _previewLastError = $"SetTrigger({data.animTrigger}) 失败: {ex.Message}"; }
                }
                _previewAnimTriggered = true;
            }

            // ── 联动模式:同步 _playheadT(仅当有 clip 时有效) ──
            // V3.1.8 修复:无条件同步 _playheadT + Resync Runtime ——
            //  旧版「仅联动 + 有 clip 才动 ruler」会导致非联动模式按下一帧 ruler 红线不动、
            //  Animator 路径 ruler 红线永远卡在 0,跟用户预期「5 键移动播放头」不符。
            ResyncPreviewToPlayhead(Mathf.Min(nextT, _timelineMaxT));

            // ── Runtime:先 Tick 扫区间触发 VFX/SFX/Hit,再 Seek 清池 ──
            if (step > 0 && _previewRuntime.Working != null)
            {
                _previewRuntime.Tick(nextT, step);
            }
            _previewRuntime.Seek(nextT);

            // ── 更新预览时间 ──
            if (isClipPath)
            {
                if (_previewLinkTimeline)
                    _previewTime = Mathf.Clamp(nextT - _previewClipStartT, 0f, _previewClip.length);
                else
                    _previewTime = Mathf.Clamp(nextT, 0f, _previewClip.length);
            }
            else if (isAnimatorPath && !string.IsNullOrEmpty(_previewAnimResolvedState))
            {
                // Animator:按 norm 跳帧
                try
                {
                    float norm = _previewAnimLength > 0f ? Mathf.Clamp01(nextT / _previewAnimLength) : 0f;
                    _previewActor.Play(_previewAnimResolvedState, _previewResolvedLayer, norm);
                    _previewActor.Update(0f);
                }
                catch (Exception ex) { _previewLastError = $"Animator 逐帧播放失败: {ex.Message}"; }
                _previewAnimElapsed = nextT;
            }

            // P1-6:暂停模式下直接推 VFX 步进量 — DrawPreviewView 在 OnGUI 中先于
            // DrawTimelineHeader 调用,走 pending 通道会有一帧延迟。后退步进时
            // step≤0 跳过(Unity PS 不支持反向 Simulate)。
            if (step > 0f)
            {
                _previewRuntime.SimulateVFX(step);
                // 重置墙钟基线,防止下一帧 OnGUI 的 SimulateVFX 重复推进(导致 2 帧偏移)
                _previewLastSimulateVFXTime = EditorApplication.timeSinceStartup;
            }

            Repaint();
        }

        // 把 _playheadT 设到 t,并联动 _previewClip 选中最贴近的动画片段
        void SyncPlayheadToTime(float t)
        {
            _playheadT = t;
            AutoSelectClipForPlayhead();
        }

        // V3.1.8 修复:统一入口 —— 改 _playheadT 后同步预览 Runtime(让 ruler 拖动 / Home/End /
        // 右键跳转 / 下一帧按钮都能即时刷新预览窗口)。
        //  前进(newT > Runtime.LastTickT):Tick(newT, newT - LastTickT)扫区间触发新 VFX/SFX/Hit
        //  倒退/跳跃(newT <= LastTickT):Seek(newT) 清空 VFX 池(回拽不应重发未来区间的特效)
        // 联动模式 + 有 clip 时:_previewTime 也同步到 clip 局部时间,跟 Runtime.CurrentT 保持一致
        // 非联动模式:_previewTime 保持用户选 clip 的局部时间(独立预览不被 ruler 影响)
        void ResyncPreviewToPlayhead(float newT)
        {
            _playheadT = newT;
            AutoSelectClipForPlayhead();

            if (_previewRuntime == null) return;

            if (_previewLinkTimeline && _previewClip != null)
            {
                // 联动模式:Runtime 直接走 playhead 全局时间
                float lastT = (float)_previewRuntime.LastTickT;
                _previewTime = Mathf.Clamp(newT - _previewClipStartT, 0f, _previewClip.length);
                if (newT > lastT + 0.0001f)
                    _previewRuntime.Tick(newT, newT - lastT);
                else if (newT < lastT - 0.0001f)
                    _previewRuntime.Seek(newT);
                // V3.1.10 修复:**不**重置 _previewLastEditorTime,只重置 _previewLastTickTime。
                // 旧版重置 _previewLastEditorTime = now 导致下一帧 OnGUI 的
                //   SimulateVFX(vfxDt) 算 vfxDt = now - _previewLastEditorTime ≈ 0,
                //  新触发的 VFX 不被推进,呈现"突然出现没播放过程"。
                // 保留 _previewLastEditorTime 让 OnGUI 拿到真实 dt,推进 VFX。
                _previewLastTickTime = EditorApplication.timeSinceStartup;
            }
            else if (_previewClip != null)
            {
                // 非联动 + 有 clip:也走 Tick/Seek 驱动 Runtime,
                // 但不更新 _previewTime(保持独立预览时间线)
                float lastT = (float)_previewRuntime.LastTickT;
                if (newT > lastT + 0.0001f)
                    _previewRuntime.Tick(newT, newT - lastT);
                else if (newT < lastT - 0.0001f)
                    _previewRuntime.Seek(newT);
                _previewLastTickTime = EditorApplication.timeSinceStartup;
            }
            else if (_previewActor != null)
            {
                // Animator 路径:_playheadT 推到全局,Runtime.Tick 一次以触发区间 VFX
                float lastT = (float)_previewRuntime.LastTickT;
                if (newT > lastT + 0.0001f)
                    _previewRuntime.Tick(newT, newT - lastT);
                else if (newT < lastT - 0.0001f)
                    _previewRuntime.Seek(newT);
                // P1-3:同步 _previewAnimElapsed 到播放头位置。
                // 旧版只更新了 Runtime.LastTickT 但未更新 _previewAnimElapsed,
                // 导致 ruler 拖动/键盘跳帧后 _previewAnimElapsed 与 LastTickT 错位。
                // resume 后 Tick(_previewAnimElapsed, dt) 因 _previewAnimElapsed < LastTickT
                // 走 time倒退 Seek,首帧不触发 VFX 且 LastTickT 被覆写(事后恢复但瞬态丢失)。
                // 同时把 Animator 跳到对应 norm 以免 visual 与时间线脱节。
                _previewAnimElapsed = newT;
                if (!string.IsNullOrEmpty(_previewAnimResolvedState) && _previewAnimLength > 0f)
                {
                    try
                    {
                        float norm = Mathf.Clamp01(newT / _previewAnimLength);
                        _previewActor.Play(_previewAnimResolvedState, _previewResolvedLayer, norm);
                        _previewActor.Update(0f);
                    }
                    catch (Exception ex) { _previewLastError = $"Animator Resync 失败: {ex.Message}"; }
                }
            }
        }

        // V3 事件订阅(在 OnEnable 末尾已 Init;这里只是空实现,主 wizard 不需要 GUI 响应,
        // VFX/SFX 在 Runtime 内部直接生成 GO,HIT gizmo 在 DrawPreviewView 调 runtime.DrawHitGizmos)
        // V3.1.6 修复:VFX 实例化后加入离屏渲染场景,否则预览窗口看不到
        // P1-6 v3: AddSingleGO 要求根节点(GO 不能有 parent),所以 Runtime 中先按世界坐标
        //  生成 VFX —— AddSingleGO 将其加入离屏场景 —— 然后对 worldSpace=false 的 VFX
        //  挂回骨骼,使其跟随动画。流程:"世界坐标定位 → 加入场景 → 挂骨骼跟随"。
        //  与游戏侧 SkillAnimPlayer.SpawnVFXEntry 一致: worldSpace=false 时 SetParent(spawnPoint)。
        void OnPreviewVFX(SkillVFXEvent e)
        {
            if (e.VFXInstance != null && _previewRtu != null)
            {
                try
                {
                    // AddSingleGO 要求 GO 为根节点(无 parent),所以先加入场景再挂骨骼。
                    // 防御:若生成方提前挂了父(会抛 "Gameobject is not a root in a scene"),先脱父。
                    if (e.VFXInstance.transform.parent != null)
                        e.VFXInstance.transform.SetParent(null, worldPositionStays: true);
                    _previewRtu.AddSingleGO(e.VFXInstance);
                    // 离屏场景粒子系统完全靠 SimulateVFX 逐帧推进,不调用 Play().
                    // 原因: Play() 在 Unity Editor 下仍可能被内部循环驱动(即使在
                    // PreviewRenderUtility 中),导致第一帧 PS 已被推进大量时间.
                    // 改为 Stop+Clear+time=0 后直接 Simulate(0,restart) 硬重置,
                    // 后续全部由 OnGUI 的 SimulateVFX(vfxDt) 累进取 PVU dt 推进.
                    var pss = e.VFXInstance.GetComponentsInChildren<ParticleSystem>(true);
                    foreach (var ps in pss)
                    {
                        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        ps.Clear();
                        ps.time = 0f;
                        ps.Simulate(0f, true, true);  // restart=true,t=0: 硬重置到第 0 帧
                    }
                    // P1-6 v3: worldSpace=false → 挂回骨骼使其跟随动画(对齐游戏)
                    if (!e.WorldSpace && e.SpawnBoneTransform != null)
                    {
                        e.VFXInstance.transform.SetParent(e.SpawnBoneTransform, worldPositionStays: true);
                    }
                }
                catch (System.Exception ex) { Debug.LogError($"[OnPreviewVFX] {ex}"); }
            }
        }
        void OnPreviewSFX(SkillSFXEvent e) { /* SFX 走 AudioSource.Play,不需要加入离屏场景 */ }
        void OnPreviewHit(SkillHitEvent e) { /* 同上 */ }

        /// <summary>
        /// 保存 WorkingCopy 前的外部修改守卫。
        /// 若源 .asset 在向导外被修改（如 EnemySetup 重新生成），直接 Confirm 会用
        /// 旧 WorkingCopy 覆盖新数据（已发生过的踩数据 bug）。检测到外部修改时
        /// 弹窗让用户显式选择：覆盖保存 / 重新加载 / 取消。
        /// </summary>
        void TrySaveWorkingCopyWithGuard()
        {
            if (_workingCopy == null) return;

            bool externalChanged = false;
            if (!string.IsNullOrEmpty(_previewTargetPath))
            {
                var currentMtime = TryReadMtime(_previewTargetPath);
                if (_lastKnownMtime != null && currentMtime != null
                    && string.CompareOrdinal(currentMtime, _lastKnownMtime) != 0)
                {
                    externalChanged = true;
                }
            }

            if (externalChanged)
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    "源文件已被外部修改",
                    $"{_previewTargetPath} 已在向导外被修改（如 EnemySetup 重新生成）。\n\n" +
                    "• 覆盖保存：用当前 WorkingCopy 覆盖磁盘（外部修改将丢失）\n" +
                    "• 重新加载：丢弃 WorkingCopy，从磁盘重新加载外部修改\n" +
                    "• 取消：不保存",
                    "覆盖保存", "取消", "重新加载");
                if (choice == 2)
                {
                    // 重新加载：丢弃 WorkingCopy，从磁盘重建
                    _workingCopy.RebuildFromSource();
                    _previewRuntime?.Init(_previewInstance, _workingCopy);
                    _lastKnownMtime = TryReadMtime(_previewTargetPath);
                    _sourceMtimeDirty = false;
                    ShowNotification(new GUIContent("已从磁盘重新加载"));
                    Repaint();
                    return;
                }
                if (choice != 0) return; // 取消
                // choice == 0：用户明确选择覆盖，继续 Confirm
            }

            if (_workingCopy.Confirm())
            {
                _sourceMtimeDirty = false;
                _lastKnownMtime = TryReadMtime(_previewTargetPath);
                ShowNotification(new GUIContent("✓ 已保存到 .asset"));
                RefreshPreview();
            }
            else
            {
                ShowNotification(new GUIContent("✗ 保存失败：源资产为空"));
                Debug.LogWarning("[SkillBuilder] Confirm 失败：WorkingCopy.Source 为 null");
            }
        }

        /// <summary>
        /// 完整重建预览:销毁旧的 PreviewRenderUtility + 实例,重新 Instantiate 角色 Prefab,
        /// 挂武器,重连 Runtime,重置时间线,并自动播放。—— 替代"关窗重开"的刷新操作。
        /// </summary>
        void RefreshPreview()
        {
            if (_previewTarget == null) return;

            // ① 重建预览实例(销毁旧的 PreviewRenderUtility + 角色 GO,重新创建)
            RebuildPreviewInstance();

            // ② 重挂武器
            MountPreviewWeapons();

            // ③ Runtime 重连(带新的 NodeContext/VFX Sink 初始化)
            _previewRuntime?.Init(_previewInstance, _workingCopy);
            _previewRuntime?.Reset();

            // ④ 重载动画候选 + 自动选 Clip
            RefreshPreviewClipCache();
            AutoSelectClipForPlayhead();

            // ⑤ 同步骨骼位姿(让 VFX 挂点正确)
            SyncPreviewPoseForTick();

            // ⑥ 自动开始播放
            _previewTime = 0f;
            _previewPlaying = true;

            // ⑦ 验证
            _lastValidationIssues = SkillDataValidator.Validate(_previewTarget);
            foreach (var issue in _lastValidationIssues)
                if (issue.Level != SkillDataValidator.Issue.Severity.Info)
                    Debug.LogWarning($"[SkillBuilder] 验证 {issue.Level}: {issue.Message}");

            Repaint();
        }

        // 切换预览目标:卸旧 WorkingCopy + 装新(走深拷贝,不直接持有 .asset)
        void ChangePreviewTarget(SkillData newTarget)
        {
            if (newTarget == _previewTarget) return;
            _workingCopy?.Dispose();
            _workingCopy = null;
            _previewTarget = newTarget;
            _previewTargetPath = newTarget != null ? AssetDatabase.GetAssetPath(newTarget) : null;
            if (newTarget != null)
            {
                _workingCopy = new WorkingCopySkillData(newTarget);
                // 旧逻辑把缺失动画层直接注入源 .asset，留下编辑残留并污染技能资源。
                // 现在只补到 Working Copy，关闭窗口或切换技能会自动丢弃，不改变源资产。
                var working = _workingCopy.Working;
                if (working != null && working.animClips != null && working.animClips.Length > 0)
                {
                    bool hasAnimLayer = working.graphData != null
                        && working.graphData.Any(n => n is AnimClipLayerData || n is MultiStageLayerData);
                    if (!hasAnimLayer)
                    {
                        if (working.graphData == null)
                            working.graphData = new System.Collections.Generic.List<SkillNodeData>();
                        working.graphData.Insert(0, new AnimClipLayerData
                        {
                            animClip = working.animClips[0],
                            triggerTime = 0f,
                            visualOnly = true
                        });
                        Debug.Log($"[SkillBuilder] 已在 Working Copy 注入动画层: clip={working.animClips[0]?.name}，未修改源资产");
                    }
                }
                // 2026-07-22:编辑现有技能时同步 Step1/Step2 配方视图。
                // Recipe 是生成器视图,graphData 是实际行为源；这里反向读取而不改写资产。
                _recipe = SkillRecipeBuilder.FromSkillData(newTarget);
                _lastKnownMtime = TryReadMtime(_previewTargetPath);
                _sourceMtimeDirty = false;
            }
            _previewRuntime?.Init(_previewInstance, _workingCopy);
            _previewRuntime?.Reset();

            // 自动验证 graphData 节点配置
            _lastValidationIssues = SkillDataValidator.Validate(newTarget);
            // 仅 Error/Warning 打印到 Console;Info 静默
            foreach (var issue in _lastValidationIssues)
                if (issue.Level != SkillDataValidator.Issue.Severity.Info)
                    Debug.LogWarning($"[SkillBuilder] 验证 {issue.Level}: {issue.Message}");

            // 切换现有 SkillData 后立即刷新动画候选和播放状态，避免首次加载仍沿用
            // 上一个技能的 null/旧 Clip，必须重复执行或重复点击才出现动画。
            RefreshPreviewClipCache();
            AutoSelectClipForPlayhead();
            _previewTime = 0f;
            _previewAnimElapsed = 0f;
            _previewAnimTriggered = false;
            // 切换 SkillData 时重新按资产目录推断角色 Prefab。
            // 旧逻辑只在 _previewPrefab 为空时推断，导致先打开 graves 后再编辑 h_ezreal
            // 时一直沿用旧角色的 Controller/Avatar，表现为预览动画错误。
            var inferredPrefab = !string.IsNullOrEmpty(_previewTargetPath)
                ? AutoFindCharacterPrefab(_previewTargetPath)
                : null;
            // SkillData 与预览角色必须一一对应；不再允许旧 graves Prefab 通过手动标记残留。
            if (inferredPrefab != null)
            {
                _previewPrefab = inferredPrefab;
            }
            else if (!IsPreviewPrefabForSkill(_previewPrefab, _previewTargetPath))
            {
                _previewPrefab = null;
            }
            // V3.1.7 修复:切换技能时自动同步角色默认武器(用户未手动拖入时才填充,不覆盖已拖入的)
            if (_previewPrefab != null)
            {
                AutoSyncWeaponsFromPrefab(_previewPrefab);
                RebuildPreviewInstance();
            }
            EditorPrefs.SetString("SBW.PreviewTargetPath", _previewTargetPath ?? "");
            Repaint();
        }

        // ── 顶部模式条 ───────────────────────────────────────────────
        // V3.1.2 修复:加 💾 保存按钮到最左(原版只有 2 个模式按钮,保存要走预览窗口底行,不方便)。
        //           撤销 / SFX toggle 也挪到这里(原预览区底行)。
        //           居中显示当前编辑目标名字。右侧 3 个 FPS 选项(原预览区底行)。
        void DrawModeBar(Rect r)
        {
            EditorGUI.DrawRect(r, UI.BgHeader);
            GUILayout.BeginArea(new Rect(r.x + 6, r.y + 3, r.width - 12, r.height - 6));
            try
            {
            EditorGUILayout.BeginHorizontal();
            try
            {
            var bg = GUI.backgroundColor;

            // ── 左侧:保存 / 撤销 / SFX toggle ──
            GUI.backgroundColor = new Color(0.4f, 0.7f, 0.4f);
            using (new EditorGUI.DisabledScope(_workingCopy == null))
            {
                if (GUILayout.Button(new GUIContent("💾 保存", "保存到 .asset (Ctrl+S)"),
                    GUILayout.Height(22), GUILayout.Width(80)))
                {
                    TrySaveWorkingCopyWithGuard();
                }
            }
            GUI.backgroundColor = bg;

            GUI.backgroundColor = _workingCopy != null && _workingCopy.IsDirty
                ? new Color(0.9f, 0.7f, 0.3f) : Color.gray;
            using (new EditorGUI.DisabledScope(_workingCopy == null || !_workingCopy.IsDirty))
            {
                if (GUILayout.Button(new GUIContent("↶",
                        "Ctrl+Z = 撤销预览编辑"),
                    GUILayout.Height(22), GUILayout.Width(28)))
                {
                    _workingCopy?.Undo();
                    Repaint();
                }
            }
            GUI.backgroundColor = _workingCopy != null && _workingCopy.HasRedo
                ? new Color(0.9f, 0.7f, 0.3f) : Color.gray;
            using (new EditorGUI.DisabledScope(_workingCopy == null || !_workingCopy.HasRedo))
            {
                if (GUILayout.Button(new GUIContent("↷",
                        "Ctrl+Y = 重做预览编辑"),
                    GUILayout.Height(22), GUILayout.Width(28)))
                {
                    _workingCopy?.Redo();
                    Repaint();
                }
            }
            GUI.backgroundColor = bg;

            // SFX toggle(图标按钮,放 ModeBar 不占预览区空间)
            bool oldMute = _previewSFXMute;
            GUI.backgroundColor = _previewSFXMute ? Color.gray : new Color(0.4f, 0.6f, 0.9f);
            _previewSFXMute = GUILayout.Toggle(_previewSFXMute,
                _previewSFXMute ? "🔇 SFX" : "🔊 SFX",
                EditorStyles.miniButton, GUILayout.Height(22), GUILayout.Width(70));
            if (oldMute != _previewSFXMute && _previewRuntime != null) _previewRuntime.SFXMute = _previewSFXMute;
            GUI.backgroundColor = bg;

            // SkillTrace 诊断日志开关
            GUI.backgroundColor = SkillTrace.Enabled ? new Color(0.9f, 0.5f, 0.3f) : Color.gray;
            SkillTrace.Enabled = GUILayout.Toggle(SkillTrace.Enabled,
                SkillTrace.Enabled ? "📋 Trace" : "📋 Trace",
                EditorStyles.miniButton, GUILayout.Height(22), GUILayout.Width(70));
            GUI.backgroundColor = bg;

            GUILayout.Space(12);

            // ── 中部:3 个模式按钮 + 编辑目标名 ──
            GUI.backgroundColor = _mode == Mode.NewSkill ? new Color(0.4f, 0.7f, 0.4f) : Color.gray;
            if (GUILayout.Button("🆕 新建技能", GUILayout.Height(22), GUILayout.Width(90))) _mode = Mode.NewSkill;
            GUI.backgroundColor = _mode == Mode.EditExisting ? new Color(0.4f, 0.6f, 0.9f) : Color.gray;
            if (GUILayout.Button("✏️ 编辑现有", GUILayout.Height(22), GUILayout.Width(90))) _mode = Mode.EditExisting;
            GUI.backgroundColor = _mode == Mode.CharacterLibrary ? new Color(0.8f, 0.6f, 0.3f) : Color.gray;
            if (GUILayout.Button("🧑 角色技能库", GUILayout.Height(22), GUILayout.Width(100)))
            {
                _mode = Mode.CharacterLibrary;
                if (!_charLibScanned) ScanCharacterLibrary();
            }
            GUI.backgroundColor = bg;

            GUILayout.FlexibleSpace();
            GUILayout.Label(_mode == Mode.EditExisting && _editingTarget != null
                ? $"▶ {_editingTarget.name}" : "", EditorStyles.miniBoldLabel);

            // ── 右侧:预览独立 FPS(原在预览区底行,挪到这里后预览区就能干净)──
            GUILayout.FlexibleSpace();
            GUILayout.Label("预览 FPS:", EditorStyles.miniLabel, GUILayout.Height(22));
            int fpsChoice = EditorGUILayout.IntPopup(_previewFrameRate,
                new[] { "24", "30", "60" }, new[] { 24, 30, 60 },
                EditorStyles.popup, GUILayout.Width(50), GUILayout.Height(20));
            if (fpsChoice != _previewFrameRate)
            {
                _previewFrameRate = fpsChoice;
                EditorPrefs.SetInt("SBW.PreviewFrameRate", _previewFrameRate);
            }


            }
            finally { EditorGUILayout.EndHorizontal(); }
            }
            finally { GUILayout.EndArea(); }
        }

        // ── 拖拽分隔条(垂直) ─────────────────────────────────────────
        void DrawVSplitHandle(Rect r)
        {
            EditorGUI.DrawRect(r, UI.BgSplit);
            // 中线高亮
            EditorGUI.DrawRect(new Rect(r.x + r.width * 0.5f - 0.5f, r.y, 1f, r.height),
                new Color(UI.SplitLine.r, UI.SplitLine.g, UI.SplitLine.b, _draggingVSplit ? 0.9f : 0.35f));
            EditorGUIUtility.AddCursorRect(r, MouseCursor.SplitResizeLeftRight);

            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
            {
                _draggingVSplit = true;
                _splitDragStartX = e.mousePosition.x;
                _splitDragStartVal = _previewW;
                GUIUtility.hotControl = _vSplitCtrlId;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == _vSplitCtrlId && _draggingVSplit)
            {
                float delta = e.mousePosition.x - _splitDragStartX;
                _previewW = _splitDragStartVal + delta;
                e.Use();
                Repaint();
            }
        }

        // ── 拖拽分隔条(水平) ─────────────────────────────────────────
        void DrawHSplitHandle(Rect r)
        {
            EditorGUI.DrawRect(r, UI.BgSplit);
            EditorGUI.DrawRect(new Rect(r.x, r.y + r.height * 0.5f - 0.5f, r.width, 1f),
                new Color(UI.SplitLine.r, UI.SplitLine.g, UI.SplitLine.b, _draggingHSplit ? 0.9f : 0.35f));
            EditorGUIUtility.AddCursorRect(r, MouseCursor.SplitResizeUpDown);

            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
            {
                _draggingHSplit = true;
                _splitDragStartY = e.mousePosition.y;
                _splitDragStartVal = _timelineH;
                GUIUtility.hotControl = _hSplitCtrlId;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == _hSplitCtrlId && _draggingHSplit)
            {
                float delta = e.mousePosition.y - _splitDragStartY;
                _timelineH = _splitDragStartVal - delta;
                e.Use();
                Repaint();
            }
        }


        // 把新层追加到 graphData 末尾 + 同步 _layerStates + 选中 + 展开
        // 走与 Step 面板 AddNodeButton<T> 完全一致的数据流([SerializeReference] 多态)
        // 方案 D(2026-07-06):新节点插入位置感知播放头(V2 §9.2 智能默认值)——
        // 若节点类型有 triggerTime 字段,默认写入当前 _playheadT,而不是固定 0。
        void AddLayerToGraph<T>() where T : SkillNodeData, new()
        {
            // P2 保存闭环:优先编辑 WorkingCopy.Working,兜底 _editingTarget
            SkillData target = (_workingCopy != null) ? _workingCopy.Working : _editingTarget;
            if (target == null) return;
            Undo.RecordObject(target, "新增层");
            var so = new SerializedObject(target);
            so.Update();
            var graphProp = so.FindProperty("graphData");
            int newIndex = graphProp.arraySize;
            graphProp.InsertArrayElementAtIndex(newIndex);
            var newElem = graphProp.GetArrayElementAtIndex(newIndex);
            var newNode = new T();
            var ttField = typeof(T).GetField("triggerTime",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (ttField != null && ttField.FieldType == typeof(float))
                ttField.SetValue(newNode, Mathf.Max(0f, _playheadT));
            newElem.managedReferenceValue = newNode;
            so.ApplyModifiedProperties();
            // 选中 + 展开右侧参数面板
            EnsureLayerStates();
            _selectedTimelineIndex = newIndex;
            _expandedNodeIndex = newIndex;
            // 滚到可视区
            ScrollLayersToRow(newIndex);
            EditorUtility.SetDirty(target);
            if (_workingCopy != null)
            {
                MarkWorkingDirty();
                _previewRuntime?.Init(_previewInstance, _workingCopy);
                RefreshPreviewClipCache();
                AutoSelectClipForPlayhead();
            }
            Repaint();
        }

        // ═══════════════════════════════════════════════════════════════
        // 时间轴交互(行号 = 节点索引;trackRect 内部坐标系 y 从 0 开始)
        // ═══════════════════════════════════════════════════════════════
        enum TimelineHitKind { None, Move, ResizeLeft, ResizeRight }
        struct TimelineHit
        {
            public int nodeIndex;
            public Rect rect;
            public int trackRow;
            public TimelineHitKind kind;
            public SkillNodeData data;
        }

        void HandleTimelineInteraction(Rect rect, Rect trackRect, float rowH, System.Collections.Generic.List<TimelineHit> hits, int rowCount)
        {
            var e = Event.current;

            // ── 右键菜单(方案 §3.4):重命名/复制/删除/跳转/锁定/隐藏 ──
            if (e.type == EventType.MouseDown && e.button == 1
                && rect.Contains(e.mousePosition) && GUIUtility.hotControl == 0)
            {
                TimelineHit? rHit = null;
                for (int i = 0; i < hits.Count; i++)
                    if (hits[i].kind == TimelineHitKind.Move && hits[i].rect.Contains(e.mousePosition)) { rHit = hits[i]; break; }
                if (rHit.HasValue)
                {
                    int idx = rHit.Value.nodeIndex;
                    _selectedTimelineIndex = idx;
                    ShowClipContextMenu(idx);
                    e.Use();
                    return;
                }
            }

            if (e.type == EventType.MouseDown && e.button == 0
                && rect.Contains(e.mousePosition) && GUIUtility.hotControl == 0)
            {
                if (e.alt)
                {
                    var delHit = FindClosestHit(hits, e.mousePosition, true);
                    if (delHit.HasValue)
                    {
                        int delIdx = delHit.Value.nodeIndex;
                        DeleteNode(delIdx);
                        GUIUtility.hotControl = _timelineControlId;
                        e.Use();
                        GUI.changed = true;
                        Repaint();
                        return;
                    }
                }

                // 优先精确命中(左/右 resize 区优先于 move 区,因为 resize 区更窄)
                TimelineHit? hit = null;
                foreach (var kindPref in new[] { TimelineHitKind.ResizeLeft, TimelineHitKind.ResizeRight, TimelineHitKind.Move })
                {
                    for (int i = 0; i < hits.Count; i++)
                        if (hits[i].kind == kindPref && hits[i].rect.Contains(e.mousePosition)) { hit = hits[i]; break; }
                    if (hit.HasValue) break;
                }

                if (hit.HasValue)
                {
                    bool ctrlOrCmd = e.control || e.command;
                    // ── Ctrl/Cmd 点选:切换多选集合 ─────────────────────
                    if (ctrlOrCmd && hit.Value.kind == TimelineHitKind.Move)
                    {
                        if (_multiSelectedIndices.Contains(hit.Value.nodeIndex))
                            _multiSelectedIndices.Remove(hit.Value.nodeIndex);
                        else
                            _multiSelectedIndices.Add(hit.Value.nodeIndex);
                        _selectedTimelineIndex = hit.Value.nodeIndex;
                        e.Use(); Repaint();
                        return;
                    }

                    // 普通点击:若命中节点已在多选集合中,则整组一起拖(Move only);否则清空多选,单选拖拽
                    bool dragAsGroup = hit.Value.kind == TimelineHitKind.Move && _multiSelectedIndices.Contains(hit.Value.nodeIndex) && _multiSelectedIndices.Count > 1;
                    if (!dragAsGroup) _multiSelectedIndices.Clear();

                    _dragNodeIndex = hit.Value.nodeIndex;
                    _dragNodeRef = hit.Value.data;
                    _dragKind = hit.Value.kind;
                    _dragStartMouseX = e.mousePosition.x;
                    float curTT  = GetNodeTriggerTime(hit.Value.data);
                    float curDur = GetNodeDuration(hit.Value.data);
                    _dragStartValue    = curTT;
                    _dragStartDuration = curDur;
                    _tempTriggerTime = curTT;
                    _tempDuration    = curDur;

                    // P3:拖拽前把面板编辑写入 Source,否则 RebuildFromSource 会丢失面板数据
                    FlushWorkingToSource();

                    _isDraggingClip  = true;
                    _dragStartRow = hit.Value.nodeIndex;
                    _dragDirty = false;
                    _dragLastRecordX = e.mousePosition.x;
                    _selectedTimelineIndex = hit.Value.nodeIndex;
                    _expandedNodeIndex = hit.Value.nodeIndex;

                    // 多选整体拖拽:记录组内所有节点的起始 triggerTime
                    _dragGroupNodes.Clear();
                    _dragGroupStartTrigger.Clear();
                    _dragGroupTempTrigger.Clear();
                    if (dragAsGroup)
                    {
                        foreach (int idx in _multiSelectedIndices)
                        {
                            if (idx < 0 || idx >= _editingTarget.graphData.Count) continue;
                            var gd = _editingTarget.graphData[idx];
                            if (gd == null) continue;
                            _dragGroupNodes.Add(gd);
                            float gt = GetNodeTriggerTime(gd);
                            _dragGroupStartTrigger.Add(gt);
                            _dragGroupTempTrigger.Add(gt);
                        }
                    }

                    _paramScrollTarget = hit.Value.nodeIndex * 140f;
                    _paramScrollAnimating = true;
                    ScrollLayersToRow(hit.Value.nodeIndex);
                    GUIUtility.hotControl = _timelineControlId;
                    e.Use();
                    GUI.changed = true;
                    Repaint();
                    return;
                }

                // ── 空白区点击:开始框选(不清空,等 MouseUp 时判断是否有拖动)──
                if (!e.shift && !(e.control || e.command))
                {
                    _selectedTimelineIndex = -1;
                    _multiSelectedIndices.Clear();
                }
                _boxSelecting = true;
                _boxSelectStart = e.mousePosition;
                _boxSelectCur = e.mousePosition;
                GUIUtility.hotControl = _timelineControlId + 4;
                e.Use();
                return;
            }

            // ── 框选拖拽中 ───────────────────────────────────────────
            if (e.type == EventType.MouseDrag && GUIUtility.hotControl == _timelineControlId + 4 && _boxSelecting)
            {
                _boxSelectCur = e.mousePosition;
                e.Use();
                Repaint();
                return;
            }
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == _timelineControlId + 4 && _boxSelecting)
            {
                var boxRect = Rect.MinMaxRect(
                    Mathf.Min(_boxSelectStart.x, _boxSelectCur.x), Mathf.Min(_boxSelectStart.y, _boxSelectCur.y),
                    Mathf.Max(_boxSelectStart.x, _boxSelectCur.x), Mathf.Max(_boxSelectStart.y, _boxSelectCur.y));
                if (boxRect.width > 3f || boxRect.height > 3f)
                {
                    var newSel = new List<int>();
                    for (int i = 0; i < hits.Count; i++)
                    {
                        if (hits[i].kind != TimelineHitKind.Move) continue;
                        if (boxRect.Overlaps(hits[i].rect) && !newSel.Contains(hits[i].nodeIndex))
                            newSel.Add(hits[i].nodeIndex);
                    }
                    if (e.control || e.command || e.shift)
                    {
                        foreach (var idx in newSel) if (!_multiSelectedIndices.Contains(idx)) _multiSelectedIndices.Add(idx);
                    }
                    else
                    {
                        _multiSelectedIndices = newSel;
                    }
                    if (_multiSelectedIndices.Count > 0) _selectedTimelineIndex = _multiSelectedIndices[0];
                }
                _boxSelecting = false;
                GUIUtility.hotControl = 0;
                e.Use();
                Repaint();
                return;
            }

            if (e.type == EventType.MouseDrag && GUIUtility.hotControl == _timelineControlId && _dragNodeRef != null)
            {
                // ── 多选整体移动(Move only)──────────────────────────
                if (_dragGroupNodes.Count > 1)
                {
                    float dxAll = e.mousePosition.x - _dragStartMouseX;
                    float dtAll = dxAll / Mathf.Max(1f, _pixelsPerSecond);
                    for (int g = 0; g < _dragGroupNodes.Count; g++)
                    {
                        float nv = Mathf.Max(0f, _dragGroupStartTrigger[g] + dtAll);
                        if (_snapToFrame) nv = Mathf.Round(nv * _frameRate) / _frameRate;
                        _dragGroupTempTrigger[g] = nv;
                    }
                    // 主拖拽节点的临时值也同步(供选中详情条/参数区读取)
                    int mainGi = _dragGroupNodes.IndexOf(_dragNodeRef);
                    if (mainGi >= 0) _tempTriggerTime = _dragGroupTempTrigger[mainGi];
                    _dragDirty = true;
                    e.Use();
                    Repaint();
                    return;
                }

                var node = _dragNodeRef;
                int curIdx = _editingTarget.graphData.IndexOf(node);
                if (curIdx < 0)
                {
                    GUIUtility.hotControl = 0;
                    _dragNodeIndex = -1;
                    _dragNodeRef = null;
                    _isDraggingClip = false;
                    e.Use();
                    return;
                }
                _dragNodeIndex = curIdx;

                float dx = e.mousePosition.x - _dragStartMouseX;
                float dt = dx / Mathf.Max(1f, _pixelsPerSecond);

                // ── 更新临时值(不写节点，参数区读临时值实时跟随)─────────
                switch (_dragKind)
                {
                    case TimelineHitKind.Move:
                        _tempTriggerTime = Mathf.Max(0f, _dragStartValue + dt);
                        _tempDuration = _dragStartDuration;
                        if (_snapToFrame)
                            _tempTriggerTime = Mathf.Round(_tempTriggerTime * _frameRate) / _frameRate;
                        TrySnapTrigger(ref _tempTriggerTime, curIdx, _dragStartDuration);
                        break;
                    case TimelineHitKind.ResizeRight:
                        // 右边缘:只改 duration,起点不动
                        _tempTriggerTime = _dragStartValue;
                        _tempDuration = Mathf.Max(0.05f, _dragStartDuration + dt);
                        if (_snapToFrame)
                            _tempDuration = Mathf.Round(_tempDuration * _frameRate) / _frameRate;
                        break;
                    case TimelineHitKind.ResizeLeft:
                        // 左边缘:triggerTime 随鼠标移动,duration 反向变化,右端(= 原 start+dur)保持不动
                        {
                            float origEnd = _dragStartValue + _dragStartDuration;
                            float newStart = Mathf.Max(0f, _dragStartValue + dt);
                            newStart = Mathf.Min(newStart, origEnd - 0.05f);   // 至少保留 0.05s
                            if (_snapToFrame)
                                newStart = Mathf.Round(newStart * _frameRate) / _frameRate;
                            _tempTriggerTime = newStart;
                            _tempDuration = Mathf.Max(0.05f, origEnd - newStart);
                        }
                        break;
                }

                // Shift 拖动行:重排节点(仅 Move 时有效)
                if (e.shift && _dragKind == TimelineHitKind.Move)
                {
                    float yRel = e.mousePosition.y - trackRect.y;
                    int newRow = Mathf.Clamp(Mathf.FloorToInt(yRel / rowH), 0, rowCount - 1);
                    if (newRow != _dragStartRow && newRow < _layerStates.Length
                        && !_layerStates[newRow].locked && _layerStates[newRow].visible)
                    {
                        Undo.RecordObject(_editingTarget, "重排节点");
                        int from = _editingTarget.graphData.IndexOf(node);
                        if (from >= 0 && from != newRow)
                        {
                            _editingTarget.graphData.RemoveAt(from);
                            int insertAt = (newRow > from) ? newRow - 1 : newRow;
                            insertAt = Mathf.Clamp(insertAt, 0, _editingTarget.graphData.Count);
                            _editingTarget.graphData.Insert(insertAt, node);
                            _selectedTimelineIndex = insertAt;
                            _expandedNodeIndex = insertAt;
                            _dragStartRow = insertAt;
                        }
                        _dragStartMouseX = e.mousePosition.x;
                        _dragStartValue = _tempTriggerTime;
                    }
                }

                _dragDirty = true;
                e.Use();
                Repaint();   // 参数区本帧读临时值，Clip 位置用临时值绘制
                return;
            }

            if (e.type == EventType.MouseUp && GUIUtility.hotControl == _timelineControlId)
            {
                if (_dragDirty)
                {
                    Undo.RecordObject(_editingTarget, "拖动时间轴节点");
                    if (_dragGroupNodes.Count > 1)
                    {
                        for (int g = 0; g < _dragGroupNodes.Count; g++)
                            SetNodeTriggerTime(_dragGroupNodes[g], _dragGroupTempTrigger[g]);
                    }
                    else if (_dragNodeRef != null)
                    {
                        switch (_dragKind)
                        {
                            case TimelineHitKind.Move:
                                SetNodeTriggerTime(_dragNodeRef, _tempTriggerTime);
                                break;
                            case TimelineHitKind.ResizeRight:
                                _dragNodeRef.editorDuration = _tempDuration;
                                break;
                            case TimelineHitKind.ResizeLeft:
                                SetNodeTriggerTime(_dragNodeRef, _tempTriggerTime);
                                _dragNodeRef.editorDuration = _tempDuration;
                                break;
                        }
                    }
                    EditorUtility.SetDirty(_editingTarget);
                    RefreshEditingSO();
                    // 方案 C(2026-07-06):时间轴拖拽(triggerTime/editorDuration)结束写入后,
                    // 同样标脏触发预览自动重建,不再要求手动点 🔄
                    if (_editingTarget == _previewTarget && _workingCopy != null)
                    {
                        _workingCopy.SnapshotForUndo();
                        _paramsDirty = true;
                    }
                }
                _isDraggingClip  = false;
                _dragNodeIndex = -1;
                _dragNodeRef = null;
                _dragGroupNodes.Clear();
                _dragGroupStartTrigger.Clear();
                _dragGroupTempTrigger.Clear();
                GUIUtility.hotControl = 0;
                e.Use();
                Repaint();
                return;
            }
        }

        // 吸附:靠近其他 Clip 的 startTime/endTime 或播放头时自动对齐(Alt 键临时禁用)
        void TrySnapTrigger(ref float tt, int selfIdx, float dur)
        {
            var e = Event.current;
            if (e.alt) return;   // Alt 临时禁用吸附
            if (_editingTarget == null || _editingTarget.graphData == null) return;
            const float SNAP_TIME_PX = 8f;
            float snapThreshold = SNAP_TIME_PX / Mathf.Max(1f, _pixelsPerSecond);
            float bestDelta = snapThreshold;
            float curTT = tt;         // 局部拷贝,供 local function 捕获(ref 参数不能被闭包捕获)
            float snappedTT = curTT;
            bool found = false;

            void TryTarget(float targetTime)
            {
                float d0 = Mathf.Abs(curTT - targetTime);
                float d1 = Mathf.Abs((curTT + dur) - targetTime);
                if (d0 < bestDelta) { bestDelta = d0; snappedTT = targetTime; found = true; }
                if (dur > 0f && d1 < bestDelta) { bestDelta = d1; snappedTT = targetTime - dur; found = true; }
            }

            for (int j = 0; j < _editingTarget.graphData.Count; j++)
            {
                if (j == selfIdx) continue;
                var other = _editingTarget.graphData[j];
                if (other == null) continue;
                float ott = GetNodeTriggerTime(other);
                float odur = GetNodeDuration(other);
                TryTarget(ott);
                if (odur > 0f) TryTarget(ott + odur);
            }
            TryTarget(_playheadT);

            if (found) tt = Mathf.Max(0f, snappedTT);
        }

        System.Nullable<TimelineHit> FindClosestHit(System.Collections.Generic.List<TimelineHit> hits, Vector2 mouse, bool exactOnly = false)
        {
            for (int i = 0; i < hits.Count; i++)
                if (hits[i].kind == TimelineHitKind.Move && hits[i].rect.Contains(mouse)) return hits[i];
            for (int i = 0; i < hits.Count; i++)
                if (hits[i].rect.Contains(mouse)) return hits[i];
            if (exactOnly) return null;
            return null;
        }

        void SetNodeTriggerTime(SkillNodeData d, float v)
        {
            if (d == null) return;
            var f = d.GetType().GetField("triggerTime",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.IgnoreCase);
            if (f != null && f.FieldType == typeof(float))
            {
                f.SetValue(d, v);
            }
        }

        // Phase 8.2: 设置节点 editorDuration(直接写公共字段)
        void SetNodeDuration(SkillNodeData d, float v)
        {
            if (d == null) return;
            d.editorDuration = Mathf.Max(0f, v);
        }

        // ── 对齐辅助线:拖拽中高亮与其他 Clip 左/右端重合的竖线(局部坐标系,基于 pps)──
        void DrawAlignmentGuides(Rect dragBar, int dragIdx, float areaW, float areaH)
        {
            if (_editingTarget == null || _editingTarget.graphData == null) return;
            const float SNAP_PX = 6f;
            float dragL = dragBar.x;
            float dragR = dragBar.xMax;
            for (int j = 0; j < _editingTarget.graphData.Count; j++)
            {
                if (j == dragIdx) continue;
                var other = _editingTarget.graphData[j];
                if (other == null) continue;
                float ott  = GetNodeTriggerTime(other);
                float odur = GetNodeDuration(other);
                float oL   = TimeToLocalX(ott);
                float oR   = odur > 0f ? TimeToLocalX(ott + odur) : oL;

                foreach (float edge in new[] { oL, oR })
                {
                    foreach (float myEdge in new[] { dragL, dragR })
                    {
                        if (Mathf.Abs(myEdge - edge) < SNAP_PX)
                        {
                            Handles.color = new Color(1f, 1f, 0f, 0.65f);
                            Handles.DrawLine(
                                new Vector3(edge, 0f),
                                new Vector3(edge, areaH), 4f);
                        }
                    }
                }
            }
        }


        void DrawRectOutline(Rect r, Color c)
        {
            Handles.color = c;
            Handles.DrawLine(new Vector3(r.x, r.y),        new Vector3(r.xMax, r.y));
            Handles.DrawLine(new Vector3(r.xMax, r.y),     new Vector3(r.xMax, r.yMax));
            Handles.DrawLine(new Vector3(r.xMax, r.yMax),  new Vector3(r.x, r.yMax));
            Handles.DrawLine(new Vector3(r.x, r.yMax),     new Vector3(r.x, r.y));
        }

        // ── Phase 8.1: Ease In/Out 渐变装饰(仅视觉)──────────────────────
        void DrawEaseDecoration(Rect bar, Color col)
        {
            float triW = Mathf.Min(bar.width * 0.2f, 12f);
            if (triW < 4f) return;
            // Ease In(左侧渐变三角:背景色→背景色×0.5)
            var easeInPts = new Vector3[] {
                new Vector3(bar.x,             bar.y),
                new Vector3(bar.x + triW,      bar.y),
                new Vector3(bar.x,             bar.yMax),
            };
            var easeInCol = new Color(col.r * 0.24f, col.g * 0.24f, col.b * 0.24f, 0.5f);
            Handles.color = easeInCol;
            Handles.DrawAAConvexPolygon(easeInPts);
            // Ease Out(右侧)
            var easeOutPts = new Vector3[] {
                new Vector3(bar.xMax - triW,  bar.y),
                new Vector3(bar.xMax,         bar.y),
                new Vector3(bar.xMax,         bar.yMax),
            };
            Handles.color = easeInCol;
            Handles.DrawAAConvexPolygon(easeOutPts);
        }

        // ── Phase 8.2: Split 分割 Clip(在播放头位置)─────────────────────
        void SplitClipAtPlayhead(int nodeIndex)
        {
            if (_editingTarget == null || _editingTarget.graphData == null) return;
            if (nodeIndex < 0 || nodeIndex >= _editingTarget.graphData.Count) return;
            var src = _editingTarget.graphData[nodeIndex];
            if (src == null) return;

            float tt = GetNodeTriggerTime(src);
            float dur = GetNodeDuration(src);
            if (dur <= 0f)
            {
                ShowNotification("点事件节点无法分割");
                return;
            }
            float splitT = _playheadT;
            if (splitT <= tt || splitT >= tt + dur)
            {
                ShowNotification("播放头不在 Clip 范围内");
                return;
            }

            Undo.RecordObject(_editingTarget, "分割节点");
            var so = _editingSO ?? new SerializedObject(_editingTarget);
            so.Update();
            var graphProp = so.FindProperty("graphData");

            // 原节点: triggerTime ~ splitT
            float newDurA = splitT - tt;
            SetNodeDuration(src, newDurA);

            // 新节点: splitT ~ endTime(复制参数)
            int newIdx = nodeIndex + 1;
            graphProp.InsertArrayElementAtIndex(newIdx);
            var newElem = graphProp.GetArrayElementAtIndex(newIdx);
            // 深拷贝原节点
            var clone = System.Activator.CreateInstance(src.GetType());
            var fields = src.GetType().GetFields(System.Reflection.BindingFlags.Instance |
                                                  System.Reflection.BindingFlags.Public |
                                                  System.Reflection.BindingFlags.NonPublic);
            foreach (var f in fields)
            {
                if (f.IsInitOnly) continue;
                f.SetValue(clone, f.GetValue(src));
            }
            newElem.managedReferenceValue = clone;
            so.ApplyModifiedProperties();
            _editingSO = so;

            // 设置新节点的 triggerTime 和 duration
            var newNode = _editingTarget.graphData[newIdx];
            SetNodeTriggerTime(newNode, splitT);
            SetNodeDuration(newNode, dur - newDurA);

            // 同步 _layerStates
            EnsureLayerStates();
            _selectedTimelineIndex = newIdx;
            _expandedNodeIndex = newIdx;
            EditorUtility.SetDirty(_editingTarget);
            Repaint();
        }

        // ── Phase 8.3: 批量对齐(多选节点起点对齐到播放头)────────────────
        void AlignSelectedToPlayhead()
        {
            if (_editingTarget == null || _editingTarget.graphData == null) return;
            if (_multiSelectedIndices.Count < 2) return;
            Undo.RecordObject(_editingTarget, "对齐到播放头");
            foreach (int idx in _multiSelectedIndices)
            {
                if (idx < 0 || idx >= _editingTarget.graphData.Count) continue;
                var d = _editingTarget.graphData[idx];
                if (d == null) continue;
                SetNodeTriggerTime(d, _playheadT);
            }
            EditorUtility.SetDirty(_editingTarget);
            Repaint();
        }

        // ── Phase 8.3: 等间距排列(按选中节点当前顺序等间距排列)──────────
        void DistributeSelectedEvenly()
        {
            if (_editingTarget == null || _editingTarget.graphData == null) return;
            if (_multiSelectedIndices.Count < 3) return;
            // 按 triggerTime 排序
            var sorted = new List<int>(_multiSelectedIndices);
            sorted.Sort((a, b) =>
                GetNodeTriggerTime(_editingTarget.graphData[a])
                .CompareTo(GetNodeTriggerTime(_editingTarget.graphData[b])));
            float firstT = GetNodeTriggerTime(_editingTarget.graphData[sorted[0]]);
            float lastT = GetNodeTriggerTime(_editingTarget.graphData[sorted[sorted.Count - 1]]);
            float interval = (lastT - firstT) / (sorted.Count - 1);
            Undo.RecordObject(_editingTarget, "等间距排列");
            for (int i = 0; i < sorted.Count; i++)
            {
                var d = _editingTarget.graphData[sorted[i]];
                if (d == null) continue;
                SetNodeTriggerTime(d, firstT + interval * i);
            }
            EditorUtility.SetDirty(_editingTarget);
            Repaint();
        }

        void ShowNotification(string msg)
        {
            Debug.Log($"[SkillBuilder] {msg}");
            _timelineTitle = msg;  // 临时借标题栏显示
        }

        // ── 键盘导航 ─────────────────────────────────────────────────────
        // ← → :播放头步进 1 帧  |  Delete/Backspace:删除选中节点
        // Ctrl+A:全选节点 → 批量跳到 triggerTime 最小值(以备后续扩展)
        // 仅当 trackRect 聚焦(鼠标悬停)时响应,避免与输入框冲突
        void HandleTimelineKeyboard(Rect trackRect)
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;
            if (!trackRect.Contains(e.mousePosition) && GUIUtility.hotControl != _timelineControlId) return;

            switch (e.keyCode)
            {
                case KeyCode.LeftArrow:
                    SeekPlayhead(-1);
                    e.Use(); Repaint();
                    break;
                case KeyCode.RightArrow:
                    SeekPlayhead(1);
                    e.Use(); Repaint();
                    break;
                case KeyCode.Home:
                    ResyncPreviewToPlayhead(0f);  // V3.1.8:同步驱动 Runtime
                    e.Use(); Repaint();
                    break;
                case KeyCode.End:
                    ResyncPreviewToPlayhead(_timelineMaxT);  // V3.1.8
                    e.Use(); Repaint();
                    break;
                case KeyCode.Space:
                    // V3.1.1 修复:Space 键走底行新播放逻辑,翻 _previewPlaying
                    // (不再 toggle 已删除的 _timelinePlaying)。按 Pause 而不是 Stop —
                    // Stop 会把时间游标归零,Space 是播放/暂停切换,语义是 Pause(保留当前位置)。
                    if (_previewClip != null || _previewActor != null)
                    {
                        if (_previewPlaying) PausePreviewPlayback();
                        else StartPreviewPlayback();
                        e.Use(); Repaint();
                    }
                    break;
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    if (_editingTarget != null && _editingTarget.graphData != null)
                    {
                        if (_multiSelectedIndices.Count > 1)
                        {
                            if (EditorUtility.DisplayDialog("删除节点",
                                $"确定删除选中的 {_multiSelectedIndices.Count} 个节点?", "删除", "取消"))
                            {
                                var sorted = new List<int>(_multiSelectedIndices);
                                sorted.Sort((a, b) => b.CompareTo(a));   // 从大到小删,避免索引错位
                                foreach (int idx in sorted) DeleteNode(idx);
                                _multiSelectedIndices.Clear();
                            }
                            e.Use();
                        }
                        else if (_selectedTimelineIndex >= 0 && _selectedTimelineIndex < _editingTarget.graphData.Count)
                        {
                            if (EditorUtility.DisplayDialog("删除节点",
                                $"确定删除节点 #{_selectedTimelineIndex} ?", "删除", "取消"))
                            {
                                DeleteNode(_selectedTimelineIndex);
                            }
                            e.Use();
                        }
                    }
                    break;
                case KeyCode.F:
                    FitTimelineToContent();   // Unity Timeline 语义:Fit All(方案 §4)
                    e.Use(); Repaint();
                    break;
                case KeyCode.A:
                    if (e.control || e.command)
                    {
                        if (_editingTarget != null && _editingTarget.graphData != null)
                        {
                            _multiSelectedIndices.Clear();
                            for (int i = 0; i < _editingTarget.graphData.Count; i++) _multiSelectedIndices.Add(i);
                        }
                        e.Use(); Repaint();
                    }
                    break;
                case KeyCode.D:
                    if ((e.control || e.command) && _selectedTimelineIndex >= 0)
                    {
                        DuplicateNode(_selectedTimelineIndex);
                        e.Use();
                    }
                    break;
                case KeyCode.Escape:
                    _multiSelectedIndices.Clear();
                    _selectedTimelineIndex = -1;
                    e.Use(); Repaint();
                    break;
            }
        }


        /// <summary>
        /// 统一的 Clip 标签绘制: 文字直接显示在时间条上(参考 Unity Timeline)
        /// </summary>
        void DrawClipLabel(Rect bar, SkillNodeData data, float tt, float dur, Color col, bool isSelected)
        {
            if (bar.width < 16f) return;

            string name = data.DisplayName ?? data.GetType().Name;
            bool isPoint = dur <= 0f;

            // 文字颜色: 深底用白, 浅底用深
            var nameStyle = s_clipNameStyle;
            var timeStyle = s_clipTimeStyle;

            // 名称: 左对齐, 留出左侧手柄空间
            float padLeft = dur > 0f ? 6f : 4f;
            float padRight = dur > 0f && bar.width >= 60f ? 42f : 4f;
            float nameW = Mathf.Max(0f, bar.width - padLeft - padRight);

            if (bar.width >= 22f && nameW > 4f)
            {
                var nameRect = new Rect(bar.x + padLeft, bar.y + 1f, nameW, bar.height - 2f);
                string displayName = name;
                // 宽度不够时逐字截断
                var sizeTest = nameStyle.CalcSize(new GUIContent(displayName));
                if (sizeTest.x > nameW && displayName.Length > 2)
                {
                    while (displayName.Length > 1 && nameStyle.CalcSize(new GUIContent(displayName + "…")).x > nameW)
                        displayName = displayName.Substring(0, displayName.Length - 1);
                    displayName += "…";
                }
                GUI.Label(nameRect, displayName, nameStyle);
            }

            // 时长/触发时间: 右对齐
            if (bar.width >= 60f)
            {
                string timeText = isPoint ? $"@{tt:0.00}s" : $"{dur:0.00}s";
                float tw = 38f;
                var timeRect = new Rect(bar.xMax - tw - 3f, bar.y + 1f, tw, bar.height - 2f);
                GUI.Label(timeRect, timeText, timeStyle);
            }
        }

        void DrawTimelineSelectionBar(Rect trackRect)
        {
            if (_editingTarget == null || _editingTarget.graphData == null) return;
            if (_selectedTimelineIndex < 0 || _selectedTimelineIndex >= _editingTarget.graphData.Count) return;
            var sel = _editingTarget.graphData[_selectedTimelineIndex];
            if (sel == null) return;

            GetDisplayTimeAndDuration(sel, _selectedTimelineIndex, out float tt, out float dur);
            int cat = (int)sel.Category;
            var col = CategoryColor(sel);  // P2-3:按节点实例取色
            string typeName = sel.GetType().Name;
            bool isPoint = dur <= 0f;
            bool multi = _multiSelectedIndices.Count > 1;

            // 选中详情条: 绝对定位在 trackRect 顶部
            float barH = 20f;
            var barRect = new Rect(trackRect.x, trackRect.y, trackRect.width, barH);
            EditorGUI.DrawRect(barRect, new Color(col.r * 0.15f, col.g * 0.15f, col.b * 0.15f, 0.95f));
            EditorGUI.DrawRect(new Rect(barRect.x, barRect.y, 3f, barH), col);
            // 底部分隔线
            EditorGUI.DrawRect(new Rect(barRect.x, barRect.yMax, barRect.width, 1f), new Color(col.r, col.g, col.b, 0.3f));

            var boldWhite   = s_selectedBoldWhiteStyle;
            var smallGray   = s_selectedSmallGrayStyle;
            var smallYellow = s_selectedSmallYellowStyle;

            float lx = barRect.x + 7f;
            float ly = barRect.y + 3f;
            float lh = barH - 6f;

            if (multi)
            {
                GUI.Label(new Rect(lx, ly, 200, lh), $"已选中 {_multiSelectedIndices.Count} 个节点", boldWhite);
            }
            else
            {
                // 精简信息: 名称 | 类型 | 触发时间 | 时长
                GUI.Label(new Rect(lx, ly, 120, lh), sel.DisplayName, boldWhite); lx += 120;
                GUI.Label(new Rect(lx, ly, 100, lh), typeName, smallGray); lx += 100;

                var ttStyle = _isDraggingClip && _dragKind == TimelineHitKind.Move ? smallYellow : smallGray;
                GUI.Label(new Rect(lx, ly, 80, lh), $"@{tt:0.00}s", ttStyle); lx += 80;

                if (!isPoint)
                {
                    var durStyle = _isDraggingClip && (_dragKind == TimelineHitKind.ResizeLeft || _dragKind == TimelineHitKind.ResizeRight) ? smallYellow : smallGray;
                    GUI.Label(new Rect(lx, ly, 70, lh), $"{dur:0.00}s", durStyle);
                }
            }

            // 右侧: 定位按钮 + 索引
            float rx = barRect.xMax - 30 - 4 - 44 - 4;
            if (GUI.Button(new Rect(rx, ly, 44, lh), "定位", EditorStyles.miniButton))
                GotoSelectedNodeTrigger();
            rx += 44 + 4;
            GUI.Label(new Rect(rx, ly, 30, lh), multi ? $"#{_multiSelectedIndices.Count}选" : $"#{_selectedTimelineIndex}", smallGray);
        }

        void DrawTimelineHoverTooltip(Rect rect, Rect trackRect, float rowH)
        {
            if (_editingTarget == null || _editingTarget.graphData == null) return;
            if (_editingTarget.graphData.Count == 0) return;
            var e = Event.current;
            if (!rect.Contains(e.mousePosition)) return;
            if (GUIUtility.hotControl == _timelineControlId) return;

            // Phase 2: 使用分组布局查找鼠标悬停的节点
            int mouseRow = -1;
            float mouseY = e.mousePosition.y - trackRect.y - _layersScroll.y;
            var groups = BuildCategoryGroups();
            float curY = 0f;
            foreach (var g in groups)
            {
                curY += GROUP_HEADER_H;
                bool collapsed = g.category >= 0 && g.category < _categoryCollapsed.Length && _categoryCollapsed[g.category];
                if (collapsed) continue;
                foreach (int i in g.nodeIndices)
                {
                    if (mouseY >= curY && mouseY < curY + rowH)
                    {
                        mouseRow = i;
                        break;
                    }
                    curY += rowH;
                }
                if (mouseRow >= 0) break;
            }
            if (mouseRow < 0) return;

            SkillNodeData best = null;
            float bestDx = float.MaxValue;
            for (int i = 0; i < _editingTarget.graphData.Count; i++)
            {
                var d = _editingTarget.graphData[i];
                if (d == null) continue;
                if (i != mouseRow) continue;
                float tt = GetNodeTriggerTime(d);
                float dur = d.editorDuration;
                float nodeX = rect.x + TimeToLocalX(tt);
                float nodeEndX = rect.x + TimeToLocalX(tt + dur);
                float cx = dur > 0f ? (nodeX + nodeEndX) * 0.5f : nodeX;
                float dx = Mathf.Abs(cx - e.mousePosition.x);
                if (dx < bestDx) { bestDx = dx; best = d; }
            }
            if (best == null) return;

            float tt2 = GetNodeTriggerTime(best);
            float dur2 = best.editorDuration;
            int cat = (int)best.Category;
            string durTip = dur2 > 0f ? $"{dur2:0.00}s" : "点事件";
            string tip = $"{best.DisplayName}  [{best.GetType().Name}]\n" +
                         $"类目: {(SkillNodeCategory)cat}  |  节点 #{_editingTarget.graphData.IndexOf(best)}\n" +
                         $"触发时刻: {tt2:0.00}s  |  持续: {durTip}";

            var size = s_timelineTooltipStyle.CalcSize(new GUIContent(tip));
            float pad = 6f;
            var tipRect = new Rect(e.mousePosition.x + 14f,
                                   e.mousePosition.y - size.y - 14f,
                                   size.x, size.y);
            if (tipRect.xMax > rect.xMax - pad) tipRect.x = e.mousePosition.x - size.x - 14f;
            if (tipRect.y < rect.y) tipRect.y = e.mousePosition.y + 14f;

            EditorGUI.DrawRect(tipRect, new Color(0.1f, 0.1f, 0.1f, 0.92f));
            DrawRectOutline(tipRect, CategoryColor(best));  // P2-3:hover tooltip 也用精细色
            GUI.Label(tipRect, tip, s_timelineTooltipStyle);
        }

        float GetNodeTriggerTime(SkillNodeData d)
        {
            if (d == null) return 0f;
            var f = d.GetType().GetField("triggerTime",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.IgnoreCase);
            if (f == null) return 0f;
            if (f.FieldType != typeof(float)) return 0f;
            return (float)f.GetValue(d);
        }

        // 智能可视时长:editorDuration=0 时按节点类型给一个 fallback
        //   - AnimClipLayerData(animClip != null) → animClip.length
        //   - 判定/生成/一次性 VFX 类节点 → 给一个短条默认时长,呈现为"条形"而非纯点(对齐 Unity Timeline Clip 视觉)
        //   - 真正的瞬时起点(Movement/Channeled 等状态类节点) → 保留 0,画点事件
        // 编辑器编辑 editorDuration > 0 时优先生效(用户可手动覆盖)
        float GetNodeDuration(SkillNodeData d)
        {
            if (d == null) return 0f;
            if (d.editorDuration > 0f) return d.editorDuration;
            // 反射读 animClip 字段(只对 AnimClipLayerData / MultiStageLayerData 有意义)
            var acf = d.GetType().GetField("animClip",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.IgnoreCase);
            if (acf != null && typeof(AnimationClip).IsAssignableFrom(acf.FieldType))
            {
                var clip = acf.GetValue(d) as AnimationClip;
                if (clip != null) return clip.length;
            }
            return GetDefaultVisualDuration(d);
        }

        // 按类目给默认可视时长(仅影响显示,不写回数据):
        //   判定类(Melee/Shot/Spawn) → 0.15s;一次性 VFX → 0.1s;其余状态类保留点形态(0)
        static float GetDefaultVisualDuration(SkillNodeData d)
        {
            if (IsPointStyleNode(d)) return 0f;
            switch (d.Category)
            {
                case SkillNodeCategory.Melee:
                case SkillNodeCategory.Shot:
                case SkillNodeCategory.Spawn:
                    return 0.15f;
                case SkillNodeCategory.VFX:
                    return 0.1f;
                default:
                    return 0f;
            }
        }

        // 白名单:哪些节点类型继续用"点"形态(瞬时状态类,不适合表现为条)
        static bool IsPointStyleNode(SkillNodeData d)
        {
            return d is MovementData || d is ChanneledData || d is StatusEffectData;
        }

        // P2-3:VFX 同 Category(=0)内三种节点细分颜色重载
        Color CategoryColor(SkillNodeData node)
        {
            if (node == null) return Color.white;
            // VFX 三色细分(同属 Category.VFX=0,用具体类型区分)
            if (node is CastVFXData)  return new Color(0.35f, 0.65f, 1.00f);  // 蓝 — 起手 VFX
            if (node is MidVFXData)   return new Color(0.70f, 0.40f, 1.00f);  // 紫 — 中段 VFX
            if (node is HitVFXData)   return new Color(1.00f, 0.55f, 0.20f);  // 橙 — 命中 VFX
            return CategoryColor((int)node.Category);
        }

        Color CategoryColor(int row)
        {
            switch (row)
            {
                case 0: return new Color(1f, 0.4f, 0.4f);    // VFX — 红(兜底,正常走 CategoryColor(node) 三色)
                case 1: return new Color(1f, 0.8f, 0.2f);    // Melee — 金
                case 2: return new Color(0.3f, 0.8f, 1f);    // Shot — 青蓝
                case 3: return new Color(0.6f, 1f, 0.3f);    // Spawn — 黄绿
                case 4: return new Color(0.9f, 0.5f, 1f);    // Channeled — 紫
                case 5: return new Color(0.7f, 0.7f, 0.7f);  // Movement — 灰
                case 6: return new Color(0.4f, 1f, 0.7f);    // Buff — 薄荷
                case 7: return new Color(0.5f, 0.85f, 1f);   // AnimClip — 天蓝(动画)
                case 8: return new Color(1f, 0.6f, 0.85f);   // MultiStage — 粉(多段)
                default: return Color.white;
            }
        }

        // ── Phase 2:Category 分组名称 ──────────────────────────────────
        static readonly string[] CategoryNames =
        {
            "VFX (视效)",      // 0
            "Melee (近战)",    // 1
            "Shot (弹体)",     // 2
            "Spawn (生成)",    // 3
            "Channeled (持续)",// 4
            "Movement (移动)", // 5
            "Buff (状态)",     // 6
            "AnimClip (动画)", // 7
            "MultiStage (多段)"// 8
        };

        // 构建分组信息:返回按 Category 分组的 (categoryIndex, nodeIndices[]) 列表,只包含有节点的组
        struct CategoryGroup
        {
            public int category;          // SkillNodeCategory 枚举值
            public List<int> nodeIndices; // 属于该组的节点索引(原始 graphData 索引)
        }

        List<CategoryGroup> BuildCategoryGroups()
        {
            var result = new List<CategoryGroup>();
            if (_editingTarget == null || _editingTarget.graphData == null) return result;
            var dict = new Dictionary<int, List<int>>();
            for (int i = 0; i < _editingTarget.graphData.Count; i++)
            {
                var d = _editingTarget.graphData[i];
                if (d == null) continue;
                int cat = (int)d.Category;
                if (!dict.TryGetValue(cat, out var list))
                {
                    list = new List<int>();
                    dict[cat] = list;
                }
                list.Add(i);
            }
            // 按 Category 枚举顺序排序(保持稳定)
            foreach (var kv in dict)
                result.Add(new CategoryGroup { category = kv.Key, nodeIndices = kv.Value });
            result.Sort((a, b) => a.category.CompareTo(b.category));
            return result;
        }

        // 计算分组布局后的总内容高度(分组头 + 节点行,折叠组只算头高)
        float GetGroupedContentHeight(int nodeCount)
        {
            var groups = BuildCategoryGroups();
            float h = 0f;
            foreach (var g in groups)
            {
                h += GROUP_HEADER_H;
                if (g.category < 0 || g.category >= _categoryCollapsed.Length) continue;
                if (!_categoryCollapsed[g.category])
                    h += g.nodeIndices.Count * LAYER_ROW_H;
            }
            return h;
        }

        // 给定原始节点索引 → 分组布局中的 Y 偏移(考虑分组头高度 + 折叠)
        // 返回 false 表示该节点被折叠隐藏
        bool TryGetGroupedY(int nodeIndex, out float y, out int category)
        {
            y = 0f;
            category = -1;
            if (_editingTarget == null || _editingTarget.graphData == null || nodeIndex < 0 || nodeIndex >= _editingTarget.graphData.Count)
                return false;
            var groups = BuildCategoryGroups();
            float curY = 0f;
            foreach (var g in groups)
            {
                curY += GROUP_HEADER_H;
                bool collapsed = g.category >= 0 && g.category < _categoryCollapsed.Length && _categoryCollapsed[g.category];
                if (g.nodeIndices.Contains(nodeIndex))
                {
                    category = g.category;
                    if (collapsed) return false;
                    int localIdx = g.nodeIndices.IndexOf(nodeIndex);
                    y = curY + localIdx * LAYER_ROW_H;
                    return true;
                }
                if (!collapsed)
                    curY += g.nodeIndices.Count * LAYER_ROW_H;
            }
            return false;
        }

        // ═══════════════════════════════════════════════════════════
        // 原生图标兜底(修复 4:图标样式对齐 Unity 内置 Timeline / Animation 窗口)
        // IconContent 名称在不同 Unity 版本可能失效,一律 try/catch + null 检查,
        // 取不到时退回原 Emoji/文本,保证任何版本下都不会出现空按钮或异常。
        // ═══════════════════════════════════════════════════════════
        static GUIContent SafeIcon(string iconName, string fallbackText, string tooltip = null)
        {
            GUIContent c = null;
            try { c = EditorGUIUtility.IconContent(iconName); } catch { c = null; }
            if (c == null || c.image == null) return new GUIContent(fallbackText, tooltip);
            var out2 = new GUIContent(c.image, tooltip ?? c.tooltip);
            return out2;
        }

        private GUIContent _iconVisibleOn, _iconVisibleOff, _iconLockOn, _iconLockOff;
        private GUIContent _iconFirstKey, _iconLastKey, _iconPrevKey, _iconNextKey, _iconSave, _iconCamera;
        private bool _iconsInit;

        void EnsureIcons()
        {
            if (_iconsInit) return;
            _iconsInit = true;
            _iconVisibleOn  = SafeIcon("scenevis_visible_hover", "👁", "隐藏该层");
            _iconVisibleOff = SafeIcon("scenevis_hidden_hover", "—", "显示该层");
            _iconLockOn     = SafeIcon("IN LockButton on", "🔒", "解锁该层");
            _iconLockOff    = SafeIcon("IN LockButton", "—", "锁定该层");
            _iconFirstKey   = SafeIcon("Animation.FirstKey", "⏮", "首帧");
            _iconLastKey    = SafeIcon("Animation.LastKey", "⏭", "末帧");
            _iconPrevKey    = SafeIcon("Animation.PrevKey", "⏪", "上一帧");
            _iconNextKey    = SafeIcon("Animation.NextKey", "⏩", "下一帧");
            _iconSave       = SafeIcon("SaveAs", "💾", "保存");
            _iconCamera     = SafeIcon("Camera Icon", "🎬", "预览");
        }

        GUIContent IconVisibleOn  { get { EnsureIcons(); return _iconVisibleOn; } }
        GUIContent IconVisibleOff { get { EnsureIcons(); return _iconVisibleOff; } }
        GUIContent IconLockOn     { get { EnsureIcons(); return _iconLockOn; } }
        GUIContent IconLockOff    { get { EnsureIcons(); return _iconLockOff; } }
        GUIContent IconFirstKey   { get { EnsureIcons(); return _iconFirstKey; } }
        GUIContent IconLastKey    { get { EnsureIcons(); return _iconLastKey; } }
        GUIContent IconPrevKey    { get { EnsureIcons(); return _iconPrevKey; } }
        GUIContent IconNextKey    { get { EnsureIcons(); return _iconNextKey; } }
        GUIContent IconSave       { get { EnsureIcons(); return _iconSave; } }
        GUIContent IconCamera     { get { EnsureIcons(); return _iconCamera; } }

        void DrawRecipePreview()
        {
            string desc = $"射程: {_recipe.range}  |  范围: {_recipe.targeting}  |  " +
                          $"弹体: {_recipe.projectile}  |  多段: {_recipe.multiHit}";
            if (_recipe.buff != SkillBuffType.None)
                desc += $"  |  Buff: {_recipe.buff}";
            EditorGUILayout.HelpBox(desc, MessageType.None);
        }

        string GetTargetingDesc(SkillTargeting t) => t switch
        {
            SkillTargeting.Single => "单体：只命中一个目标",
            SkillTargeting.AOE => "范围：圆形 AOE 命中范围内所有目标",
            SkillTargeting.Cone => "扇形：前方扇形角度内所有目标",
            SkillTargeting.Line => "直线：前方窄条/矩形内所有目标",
            _ => "",
        };

        string GetProjectileDesc(SkillProjectile p) => p switch
        {
            SkillProjectile.Instant => "即时：无飞行体，前摇结束瞬间判定（近战挥击/AOE爆炸）",
            SkillProjectile.Projectile => "直线飞行：投射物沿直线飞出（子弹/弓箭/魔法弹）",
            SkillProjectile.Curved => "抛物线：受重力影响的弧线弹体（手雷/榴弹）",
            SkillProjectile.Beam => "光束：瞬时射线扫掠（激光/火焰吐息）",
            SkillProjectile.Chain => "链式：命中后弹射到下一个目标(闪电链)",
            _ => "",
        };

    // Gizmo 绘制方法已移至 SkillBuilderWizard.Gizmo.cs
    }


    // ═══════════════════════════════════════════════════════════════
    // EditorInputDialog —— IMGUI 没有原生输入弹窗,自己做一个
    // 用法:EditorInputDialog.Show("标题", "默认文本") → 返回输入或 null(取消)
    // ═══════════════════════════════════════════════════════════════
    internal class EditorInputDialog : EditorWindow
    {
        string _text;
        string _prompt;
        bool _enterPressed;
        bool _cancelPressed;
        const float W = 320f;
        const float H = 90f;

        public static string Show(string title, string prompt, string defaultText = "")
        {
            var win = CreateInstance<EditorInputDialog>();
            win.titleContent = new GUIContent(title);
            win._prompt = prompt;
            win._text = defaultText ?? "";
            win._enterPressed = false;
            win._cancelPressed = false;
            win.position = new Rect(Screen.width * 0.5f - W * 0.5f, Screen.height * 0.5f - H * 0.5f, W, H);
            win.minSize = new Vector2(W, H);
            win.maxSize = new Vector2(W, H);
            win.ShowUtility();
            win.Focus();
            return win.WaitForResult();
        }

        string WaitForResult()
        {
            // 简单同步等待(主线程)
            while (!_enterPressed && !_cancelPressed)
            {
                System.Threading.Thread.Sleep(20);
            }
            string r = _enterPressed ? _text : null;
            Close();
            DestroyImmediate(this);
            return r;
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField(_prompt, EditorStyles.boldLabel);
            GUI.SetNextControlName("InputField");
            _text = EditorGUILayout.TextField(_text ?? "");
            GUI.FocusControl("InputField");

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            bool enterNow = Event.current.keyCode == KeyCode.Return && Event.current.type == EventType.KeyDown;
            bool escNow = Event.current.keyCode == KeyCode.Escape && Event.current.type == EventType.KeyDown;
            if (GUILayout.Button("确定", GUILayout.Width(80)) || enterNow)
            {
                _enterPressed = true;
                Event.current.Use();
                Repaint();
            }
            if (GUILayout.Button("取消", GUILayout.Width(80)) || escNow)
            {
                _cancelPressed = true;
                Event.current.Use();
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif
