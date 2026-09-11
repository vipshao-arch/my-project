using UnityEngine;
using System.Collections.Generic;
using Game.Character;



namespace Game.SkillSystem
{
    /// <summary>
    /// Bridges the skill system to the Unity Animator.
    /// Uses CrossFadeInFixedTime for direct animation control —no AnyState transitions needed.
    ///
    /// Two-layer system:
    ///   SkillFullBody (no mask) —idle: full body
    ///   SkillUpperBody (UpperBody mask) —moving: upper body only
    ///
    /// Front/back swing:
    ///   SkillData.frontSwing/backSwing: 归一化比例(0~1),相对 AnimationClip.length。
    ///   本类在 PlayAnimation 入口做唯一一次 clipLength * ratio → 秒转换,
    ///   此后 _frontSwing / _backSwingStart / _animDuration 全为秒量纲。
    ///   frontSwing: 前摇段,不可被新技能打断。
    ///   backSwing: 后摇段,动画可被提前打断(有效时长 = clipLength * (1-backSwing))。
    /// </summary>
    public class SkillAnimPlayer : MonoBehaviour
    {
        [Header("Transition Settings")]
        [Tooltip("CrossFade duration when starting a skill/attack animation")]
        public float fadeInDuration = 0.1f;
        [Tooltip("Blend duration when returning to idle after animation ends")]
        public float fadeOutDuration = 0.35f;

        // ── 节点执行器：所有 VFX/判定节点的统一入口 ────────────────
        private SkillRunner _runner = new SkillRunner();
        public SkillRunner Runner => _runner;

        private Animator _animator;
        private ICharacterMotor _motor;
        private int _upperBodyLayerIndex = -1;
        private int _fullBodyLayerIndex = -1;
        private int _activeLayerIndex = -1; // which layer is currently active

        // Current animation state
        private bool _isCasting = false;
        private bool _isFadingOut = false; // fading back to idle

        // ── 普攻姿态(攻击朝向锁定/保持系统,2026-07-30 原生实现) ──
        private bool  _isBasicAttackSlot = false;   // 当前 cast 是否为普攻(BasicAttack)
        private float _basicAttackHoldUntil = 0f;   // 普攻姿态保持窗截止时刻(Time.time)
        private const float BasicAttackHoldSeconds = 1f; // 动画主体结束后的姿态保持时长
        private float _animTimer = 0f;
        private float _animDuration = 0f;
        private float _frontSwing = 0f;
        private float _backSwingStart = 0f; // time after which new input can interrupt
        private float _fadeOutTimer = 0f;
        private float _currentFadeOut = 0f; // per-skill fadeOut duration for current animation
        private SkillData _currentData = null;

        // Animator parameter hashes (kept for compatibility, but CrossFade does the heavy lifting)
        // 参数名统一走 AnimatorParams 契约(2026-07-30)
        private static readonly int InputMagnitudeHash = AnimatorParams.InputMagnitude;
        private static readonly int MoveAttackHash = AnimatorParams.MoveAttack;
        private static readonly int IsGroundedHash = AnimatorParams.IsGrounded;

                // Hit frame state
        private bool _hitFired = false;

        // 节点启动标记：PlayAnimation 时置 true,HitDetector.SetupAttack 注入完整参数后启动 runner
        private bool _pendingStart = false;
        public bool IsPendingStart => _pendingStart;
        public void ClearPendingStart() { _pendingStart = false; }

        // 远端表现模式标记(S2-5b):PlayRemoteCast 起,Finish/Reset 清;期间弹道 VFX 不伤
        private bool _visualOnlyCast;

        /// <summary>
        /// 远端表现施法(S2-5b,联机):NetworkCastRelay 收到广播后调用。
        /// 播放动画+跑技能图——VFX/弹道真实生成,伤害经 ctx.visualOnly 全抑制。
        /// 本端 HitDetector 禁用不走 SetupAttack,直接驱动 runner。
        /// </summary>
        public void PlayRemoteCast(SkillData data)
        {
            if (data == null || IsCasting) return;
            _visualOnlyCast = true;
            PlayAnimation(data, false);
            if (!IsCasting) { _visualOnlyCast = false; return; }
            if (_pendingStart)
            {
                ClearPendingStart();
                var hitDet = GetComponent<HitDetector>();
                LayerMask mask = hitDet != null ? hitDet.hitMask : (LayerMask)(~0);
                _runner.StartCast(data, gameObject, 0f, 0f, mask, false, AnimDuration, visualOnly: true);
            }
        }

        /// <summary>
        /// 在技能前摇结束时触发（伤害判定帧）。
        /// 参数为当前 SkillData，由 HitDetector 监听。
        /// </summary>
        public event System.Action<SkillData> OnHitFrame;

        // Clip 长度缓存：避免每次播放时遍历 animationClips 数组
        private Dictionary<string, float> _clipLengthCache = new Dictionary<string, float>();

        // 多段动画调度：标记 graphData 中已触发过的动画层节点索引
        private readonly HashSet<int> _animLayerTriggered = new HashSet<int>();

        // P1 安全校验：Animator 参数名缓存（避免每帧遍历 parameters[]）
        private HashSet<string> _paramNameCache;

        void Awake()
        {
            _animator = GetComponent<Animator>();
            _motor = GetComponent<ICharacterMotor>();
        }

        void Start()
        {
            _upperBodyLayerIndex = FindLayerByName("SkillUpperBody");
            _fullBodyLayerIndex = FindLayerByName("SkillFullBody");

            // Both layers start at weight 0
            if (_upperBodyLayerIndex >= 0)
                _animator.SetLayerWeight(_upperBodyLayerIndex, 0f);
            if (_fullBodyLayerIndex >= 0)
                _animator.SetLayerWeight(_fullBodyLayerIndex, 0f);

            // Build parameter name cache for safety checks
            BuildParamNameCache();

            // Force reset parameters to prevent AnyState looping on start
            SafeSetInteger(AnimatorParams.SkillState, 0);
            SafeSetInteger(AnimatorParams.AttackIndex, 0);
            SafeResetTrigger("SkillQ");
            SafeResetTrigger("SkillW");
            SafeResetTrigger("SkillE");
            SafeResetTrigger("SkillR");
            SafeResetTrigger(AnimatorParams.Attack);

            BuildClipLengthCache();
        }

        private void BuildParamNameCache()
        {
            _paramNameCache = new HashSet<string>();
            if (_animator == null || _animator.runtimeAnimatorController == null) return;
            foreach (var p in _animator.parameters)
                _paramNameCache.Add(p.name);
        }

        private void BuildClipLengthCache()
        {
            _clipLengthCache.Clear();
            if (_animator == null || _animator.runtimeAnimatorController == null) return;
            foreach (var clip in _animator.runtimeAnimatorController.animationClips)
            {
                if (clip != null && !_clipLengthCache.ContainsKey(clip.name))
                    _clipLengthCache[clip.name] = clip.length;
            }
        }

        void LateUpdate()
        {
            if (_animator == null) return;

            // Layer weight fade-in: from current weight up to 1 at skill start
            if (_layerFadeInTimer > 0f && _activeLayerIndex >= 0)
            {
                _layerFadeInTimer -= Time.deltaTime;
                float t = 1f - Mathf.Clamp01(_layerFadeInTimer / _layerFadeInDuration);
                _animator.SetLayerWeight(_activeLayerIndex, Mathf.Lerp(0f, 1f, t));
            }

            // Handle fade-out: smoothly lerp layer weight to 0
            if (_isFadingOut)
            {
                _fadeOutTimer -= Time.deltaTime;
                float fadeDur = _currentFadeOut > 0f ? _currentFadeOut : fadeOutDuration;

                if (_fadeOutTimer <= 0f)
                {
                    // Ensure final weight is exactly 0 before disabling —no last-frame pop
                    if (_activeLayerIndex >= 0)
                        _animator.SetLayerWeight(_activeLayerIndex, 0f);
                    DisableAllLayers();
                    _isFadingOut = false;
                    _runner.End();
                }
                else if (_activeLayerIndex >= 0)
                {
                    // Linear fade instead of SmoothStep to avoid the ease-in plateau
                    // that makes the final jump-to-zero more noticeable.
                    // Smooth ease at the START (blend in), linear at the END (blend out).
                    float t = Mathf.Clamp01(_fadeOutTimer / fadeDur);
                    float weight = t * t;  // quadratic: fast early drop, gentle tail
                    _animator.SetLayerWeight(_activeLayerIndex, weight);
                }
                return;
            }

            // Tick animation timer
            if (_isCasting)
            {
                _animTimer += Time.deltaTime;

                // 节点驱动：每帧 Tick（Cast 阶段的 MidVFX / 通道 tick 走这里）
                if (_currentData != null)
                {
                    _runner.Tick(_animTimer);
                }

                // 多段动画调度：遍历 graphData 中 AnimClipLayerData / MultiStageLayerData，
                // 按 triggerTime 切换动画片段
                ScheduleGraphAnimationLayers();

                // Hit frame: fire once when animation reaches frontSwing
                if (!_hitFired && _animTimer >= _frontSwing)
                {
                    OnHitFrame?.Invoke(_currentData);
                    _hitFired = true;
                }

                if (_animTimer >= _animDuration)
                {
                    FinishAnimation();
                }
            }
        }

        #region Play Animation

        /// <summary>
        /// Play any SkillData animation (skill or basic attack).
        /// Uses CrossFadeInFixedTime for smooth transitions.
        /// </summary>
        /// <param name="isMovingAttackOverride">
        /// 普攻专用:由调用方在清空输入(如 CancelClickToMove)之前捕捉的"是否移动攻击"。
        /// null=维持旧行为(读 _motor.input 当前值)。
        /// </param>
        public void PlayAnimation(SkillData data, bool? isMovingAttackOverride = null)
        {
            if (_animator == null || data == null) return;

            // If currently casting, check if we can interrupt
            if (_isCasting && !CanBeInterrupted())
                return;

            // Stop any fade-out in progress
            _isFadingOut = false;

            _currentData = data;

            // Determine target state name
            string stateName = GetAnimatorStateName(data);

            // Calculate duration and interrupt windows
            // P1 时间单位收敛：SkillData.frontSwing/backSwing 为归一化 0~1,
            // 此处做唯一一次 clipLength * ratio → 秒转换,之后全用秒量纲。
            float clipLength = GetClipLength(data);
            _frontSwing = clipLength * Mathf.Clamp01(data.frontSwing);

            // backSwingStart = the time point after which new input CAN interrupt
            // Before this point: animation must play fully (front swing + core)
            // After this point: interruptible (back swing / recovery phase)
            float backSwingSec = clipLength * Mathf.Clamp01(data.backSwing);
            _backSwingStart = Mathf.Max(clipLength - backSwingSec, _frontSwing + 0.05f);

            // Determine whether the character is "in motion" for duration selection:
            //   Moving (InputMagnitude > 0) OR Airborne (IsGrounded = false)
            //   —use backSwingStart (shorter, responsive / interruptible)
            // Standing on ground with no input
            //   —use full clipLength (let the full animation play)
            //
            // Key fix: in-air attacks must use backSwingStart, NOT clipLength.
            // When jumping, InputMagnitude briefly drops to 0, which previously caused
            // the full 2.77s clip to play, making the character appear "stuck" mid-air.
            float inputMag = _animator.GetFloat(InputMagnitudeHash);
            bool isGrounded = _animator.GetBool(IsGroundedHash);
            bool isMoving = inputMag > 0.1f || !isGrounded;

            _animDuration = isMoving ? _backSwingStart : clipLength;
            _animTimer = 0f;
            _isCasting = true;
            _hitFired = false;        // reset so hit frame fires once per cast

            // 重置多段动画调度状态
            _animLayerTriggered.Clear();

            // 通知老 runner 结束上一个技能
            if (_runner.IsRunning) _runner.End();
            // 注意：StartCast 在第一次 Tick 之前由 HitDetector.SetupAttack 注入完整参数
            // 这里仅做"待启动"标记：用一个 _pendingStart 标记，到 HitDetector 真正注入数据时启动
            _pendingStart = true;

            // Resolve per-skill fade durations (-1 = use global default)
            float useFadeIn = data.fadeInDuration >= 0f ? data.fadeInDuration : fadeInDuration;
            float useFadeOut = data.fadeOutDuration >= 0f ? data.fadeOutDuration : fadeOutDuration;
            _currentFadeOut = useFadeOut;

            // 站立普攻必须走 SkillFullBody；移动普攻才走 SkillUpperBody。
            // 之前无论是否移动都固定上半身，导致原地攻击只抬手、腿部仍停留在待机。
            int targetLayer = GetTargetLayer();
            if (data.category == SkillCategory.BasicAttack)
            {
                bool movingAttack = isMovingAttackOverride
                    ?? (_motor != null && _motor.input.sqrMagnitude > 0.01f);
                targetLayer = movingAttack && _upperBodyLayerIndex >= 0
                    ? _upperBodyLayerIndex
                    : _fullBodyLayerIndex >= 0 ? _fullBodyLayerIndex : targetLayer;
            }
            ActivateLayer(targetLayer);

            // 联机表现广播记录(S2-5):SkillController 取走广播给远端
            CurrentStateName = stateName;
            CurrentLayerIndex = targetLayer;

            // 普攻槽位标记(供 IsBasicAttackPose 姿态保持窗判定)
            _isBasicAttackSlot = data.category == SkillCategory.BasicAttack;

            // Sync MoveAttack param: only set for BasicAttack, skills clear it
            // Any directional input triggers move-attack (consistent with GetTargetLayer)
            if (data.category == SkillCategory.BasicAttack)
            {
                bool isMovingAttack = isMovingAttackOverride
                    ?? (_motor != null && _motor.input.sqrMagnitude > 0.01f);
                _animator.SetBool(MoveAttackHash, isMovingAttack);
            }

            // CrossFade directly to the target animation state on the active layer.
            // Multi-tier fallback: try alternative state name formats and layers.
            // This handles naming mismatches between SkillData and various Controller conventions
            // (e.g. "Attack_1" vs "Attack01", "SkillUpperBody" vs "Base Layer").
            if (!TryCrossFadeWithFallbacks(data, stateName, useFadeIn, targetLayer))
            {
                Debug.LogWarning(
                    $"[SkillAnim] State '{stateName}' 在 Layer {targetLayer} 中未找到。\n" +
                    $"  SkillData.animClipName='{data.animClipName}'，" +
                    "请确认 Controller 中该 State 名称完全一致（含大小写、下划线、空格）。");
            }

            // ── SkillTrace: 统一诊断日志 ──
            string clipNameForTrace = null;
            float clipLenForTrace = clipLength;
            if (!string.IsNullOrWhiteSpace(data.animClipName))
                clipNameForTrace = data.animClipName;
            else if (data.animClips != null && data.animClips.Length > 0 && data.animClips[0] != null)
                clipNameForTrace = data.animClips[0].name;

            SkillTrace.LogRuntime(
                _animator, data, stateName, targetLayer,
                clipNameForTrace ?? "n/a", clipLenForTrace,
                data.frontSwing, data.backSwing,
                _frontSwing, _backSwingStart);
        }

        public void PlaySkillAnimation(SkillData skill)
        {
            PlayAnimation(skill);
        }

        public void PlayRandomAttack(SkillData[] attackSlots)
        {
            PlayRandomAttack(attackSlots, null);
        }

        /// <summary>
        /// 随机播放一个普攻。<paramref name="isMovingAttack"/> 为 null 时维持旧行为(读 _motor.input)。
        /// </summary>
        public void PlayRandomAttack(SkillData[] attackSlots, bool? isMovingAttack)
        {
            if (attackSlots == null || attackSlots.Length == 0) return;

            int count = 0;
            for (int i = 0; i < attackSlots.Length; i++)
                if (attackSlots[i] != null) count++;
            if (count == 0) return;

            int pick = Random.Range(0, count);
            int idx = 0;
            for (int i = 0; i < attackSlots.Length; i++)
            {
                if (attackSlots[i] != null)
                {
                    if (idx == pick)
                    {
                        PlayAnimation(attackSlots[i], isMovingAttack);
                        return;
                    }
                    idx++;
                }
            }
        }

        public void PlayRandomAttack()
        {
            Debug.LogWarning("[SkillAnim] PlayRandomAttack() called without SkillData array.");
        }

        #endregion

        #region Interrupt Logic

        /// <summary>
        /// Can the current animation be interrupted?
        ///   - Not casting —true
        ///   - In front swing (0 ~ frontSwing) —false
        ///   - In core animation (frontSwing ~ backSwingStart) —false
        ///   - In back swing (backSwingStart ~ end) —true (interruptible)
        /// </summary>
        public bool CanBeInterrupted()
        {
            if (!_isCasting) return true;

            // Only interruptible after reaching the back swing window
            return _animTimer >= _backSwingStart;
        }

        /// <summary>
        /// Can the animation be interrupted by a high-priority action (jump, roll, etc.)?
        /// More lenient than CanBeInterrupted —allows interrupt after front swing.
        ///   - Not casting —true
        ///   - In front swing (0 ~ frontSwing) —false  
        ///   - Past front swing —true (jump/roll can cancel)
        /// </summary>
        public bool CanBeInterruptedByAction()
        {
            if (!_isCasting) return true;
            return _animTimer >= _frontSwing;
        }

        public bool CanStartNewAction()
        {
            if (_isFadingOut) return true; // fading out = effectively done
            return !_isCasting || CanBeInterrupted();
        }

        #endregion

        #region Layer Management

        private int GetTargetLayer()
        {
            // Use motor.input raw value, not damped InputMagnitude Animator param.
            bool hasInput = _motor != null && _motor.input.sqrMagnitude > 0.01f;
            bool isGrounded = _animator.GetBool(IsGroundedHash);
            // Airborne also counts as moving - legs stay free for fall/land anim
            bool isMoving = hasInput || !isGrounded;
            // Fallback: if preferred layer missing, use the other; both missing → Base Layer 0
            if (isMoving)
                return _upperBodyLayerIndex >= 0 ? _upperBodyLayerIndex
                     : _fullBodyLayerIndex  >= 0 ? _fullBodyLayerIndex : 0;
            else
                return _fullBodyLayerIndex >= 0 ? _fullBodyLayerIndex
                     : _upperBodyLayerIndex >= 0 ? _upperBodyLayerIndex : 0;
        }

        // Layer weight fade-in state
        private float _layerFadeInTimer = 0f;
        private float _layerFadeInDuration = 0f;

        private void ActivateLayer(int layerIndex)
        {
            // Disable other layer, then fade in target layer
            if (layerIndex < 0) { _activeLayerIndex = -1; return; }
            int otherLayer = (layerIndex == _fullBodyLayerIndex) ? _upperBodyLayerIndex : _fullBodyLayerIndex;
            if (otherLayer >= 0)
                _animator.SetLayerWeight(otherLayer, 0f);

            _activeLayerIndex = layerIndex;

            // Start layer weight fade-in from current weight up to 1
            float currentWeight = _animator.GetLayerWeight(layerIndex);
            _layerFadeInDuration = Mathf.Max((1f - currentWeight) * fadeInDuration, 0.001f);
            _layerFadeInTimer    = _layerFadeInDuration;
        }

        private void DisableAllLayers()
        {
            if (_upperBodyLayerIndex >= 0)
                _animator.SetLayerWeight(_upperBodyLayerIndex, 0f);
            if (_fullBodyLayerIndex >= 0)
                _animator.SetLayerWeight(_fullBodyLayerIndex, 0f);
            _activeLayerIndex = -1;
        }

        #endregion

        #region Finish / Reset

        private void FinishAnimation()
        {
            _isCasting = false;
            _visualOnlyCast = false;   // S2-5b:远端表现模式结束
            _currentData = null;
            _animTimer = 0f;
            _animDuration = 0f;
            _frontSwing = 0f;
            _backSwingStart = 0f;
            // 普攻动画主体结束:开启 1 秒姿态保持窗(供攻击朝向锁定/下半身 strafe 融合)
            if (_isBasicAttackSlot)
                _basicAttackHoldUntil = Time.time + BasicAttackHoldSeconds;
            // 动画结束时清除 MoveAttack，防止残留 true 影响下次静止攻击的层选择
            if (_animator != null)
                _animator.SetBool(MoveAttackHash, false);
            _layerFadeInTimer = 0f;   // 停止淡入，淡出接管权重
            float useFadeOut = _currentFadeOut > 0f ? _currentFadeOut : fadeOutDuration;

            // Crossfade to SkillIdle if it has a real clip, otherwise freeze on last frame.
            // Either way the layer weight then fades 1— over useFadeOut seconds.
            if (_activeLayerIndex >= 0)
            {
                if (HasSkillIdleClip())
                    SafeCrossFade("SkillIdle", useFadeOut * 0.4f, _activeLayerIndex);
                // else: keep last attack frame frozen; quadratic weight-fade handles the blend
            }

            _isFadingOut = true;
            _fadeOutTimer = useFadeOut;
        }

        /// <summary>
        /// Returns true if a clip whose name matches "SkillIdle" or "skill_idle" exists
        /// in the controller —meaning SkillIdle state has a real pose to CrossFade to.
        /// </summary>
        private bool HasSkillIdleClip()
        {
            if (_animator == null) return false;
            // Check cache first
            foreach (var key in _clipLengthCache.Keys)
            {
                if (key.IndexOf("SkillIdle", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    key.IndexOf("skill_idle", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        public void ResetSkillAnimation()
        {
            _isCasting = false;
            _visualOnlyCast = false;   // S2-5b:远端表现模式结束
            _isFadingOut = false;
            _isBasicAttackSlot = false;
            _basicAttackHoldUntil = 0f;
            _layerFadeInTimer = 0f;
            _layerFadeInDuration = 0f;
            // P2 VFX 收敛:vfxEntries.fired 重置已移除——运行时 VFX 全部走 graphData 节点,
            // legacy vfxEntries 仅 FromLegacy 迁移工具读取,不再被运行时消费。
            _currentData = null;
            _animTimer = 0f;
            _animDuration = 0f;
            _frontSwing = 0f;
            _backSwingStart = 0f;
            _hitFired = false;
            _animLayerTriggered.Clear();
            DisableAllLayers();
        }

        #endregion

        #region Public State

        public bool IsCasting => _isCasting;
        public bool IsFadingOut => _isFadingOut;
        public bool IsMoveAttack => _isCasting && _animator != null && _animator.GetBool(MoveAttackHash);
        /// <summary>
        /// 普攻姿态激活:攻击动画播放中,或动画主体结束后 1 秒姿态保持窗内。
        /// 供攻击朝向锁定(根节点钉住)与下半身 strafe 融合保持使用。
        /// </summary>
        public bool IsBasicAttackPose => _isBasicAttackSlot && (_isCasting || Time.time < _basicAttackHoldUntil);
        public float AnimTimer => _animTimer;
        public float AnimDuration => _animDuration;
        /// <summary>前摇秒数（已从归一化 frontSwing 转换为秒）。</summary>
        public float FrontSwingSeconds => _frontSwing;
        /// <summary>后摇开始秒数（已从归一化 backSwing 转换为秒）。</summary>
        public float BackSwingStartSeconds => _backSwingStart;
        public SkillData CurrentData => _currentData;

        /// <summary>最近一次施法的动画状态名/目标层(联机表现广播用,S2-5)。</summary>
        public string CurrentStateName { get; private set; }
        public int CurrentLayerIndex { get; private set; } = -1;
        public bool IsPlayingSkillAnimation() => _isCasting;

        public float GetSkillAnimNormalizedTime()
        {
            if (_animDuration <= 0f) return 0f;
            return Mathf.Clamp01(_animTimer / _animDuration);
        }

        #endregion

        #region Helpers

        // ── P1 安全校验：Animator 参数/State 存在性检查 ──

        /// <summary>HasParameter 检查后再 SetInteger。参数不存在时仅警告，不阻断。</summary>
        private void SafeSetInteger(int hash, int value)
        {
            if (_animator == null) return;
            var name = GetParamNameByHash(hash);
            if (name == null || !_paramNameCache.Contains(name))
            {
                Debug.LogWarning($"[SkillAnim] Animator 缺少参数 '{name ?? $"Hash {hash}"}'，" +
                    "SetInteger 将被忽略。请检查 Controller Parameters 页。");
                return;
            }
            _animator.SetInteger(hash, value);
        }

        /// <summary>HasParameter 检查后再 ResetTrigger。参数不存在时仅警告。</summary>
        private void SafeResetTrigger(string name)
        {
            if (_animator == null) return;
            if (!_paramNameCache.Contains(name))
            {
                Debug.LogWarning($"[SkillAnim] Animator 缺少 Trigger '{name}'，" +
                    "ResetTrigger 将被忽略。请检查 Controller Parameters 页。");
                return;
            }
            _animator.ResetTrigger(name);
        }

        /// <summary>HasParameter 检查后再 ResetTrigger(int)。</summary>
        private void SafeResetTrigger(int hash)
        {
            if (_animator == null) return;
            var name = GetParamNameByHash(hash);
            if (name == null || !_paramNameCache.Contains(name))
            {
                Debug.LogWarning($"[SkillAnim] Animator 缺少 Trigger '{name ?? $"Hash {hash}"}'，" +
                    "ResetTrigger 将被忽略。请检查 Controller Parameters 页。");
                return;
            }
            _animator.ResetTrigger(hash);
        }

        /// <summary>HasState 检查后再 CrossFadeInFixedTime。State 不存在时回退到日志+阻断。</summary>
        private bool SafeCrossFade(string stateName, float duration, int layerIndex, float speed = 0f)
        {
            if (_animator == null || layerIndex < 0 || layerIndex >= _animator.layerCount)
                return false;

            int stateHash = Animator.StringToHash(stateName);
            if (!_animator.HasState(layerIndex, stateHash))
            {
                Debug.LogWarning(
                    $"[SkillAnim] Animator Layer '{_animator.GetLayerName(layerIndex)}' " +
                    $"不存在 State '{stateName}'。CrossFade 已跳过。\n" +
                    "  请检查 Controller 中该 Layer 下是否有同名 State，" +
                    "或检查 SkillData.animClipName 是否写对了 State 名称。");
                return false;
            }

            if (speed > 0f)
                _animator.CrossFadeInFixedTime(stateName, duration, layerIndex, speed);
            else
                _animator.CrossFadeInFixedTime(stateName, duration, layerIndex);
            return true;
        }

        /// <summary>
        /// Multi-tier CrossFade fallback. Tries canonical name → legacy name → Base Layer.
        /// Canonical format: Attack_1, Attack_2 (per AnimStateNaming convention).
        /// Legacy format: Attack01, Attack02 (old single-layer controllers).
        /// Returns true if any candidate succeeds.
        /// </summary>
        private bool TryCrossFadeWithFallbacks(SkillData data, string stateName, float duration, int primaryLayer)
        {
            // Tier 1: original stateName on primary layer
            if (TrySilentCrossFade(stateName, duration, primaryLayer))
                return true;

            // Tier 2: alternative naming conventions (legacy backward compat)
            if (data.category == SkillCategory.BasicAttack)
            {
                string canonical = "Attack_" + data.animStateId;
                string legacy    = "Attack0" + data.animStateId;

                // canonical is the standard format — try it first
                if (stateName != canonical && TrySilentCrossFade(canonical, duration, primaryLayer))
                {
                    LogFallbackRecovery(data.animClipName, primaryLayer, canonical);
                    return true;
                }
                // legacy format (Attack01) for old controllers
                if (stateName != legacy && TrySilentCrossFade(legacy, duration, primaryLayer))
                {
                    LogFallbackRecovery(data.animClipName, primaryLayer, legacy);
                    return true;
                }
            }

            // Tier 3: try Base Layer (0) for simple single-layer controllers
            if (primaryLayer != 0 && _animator != null && _animator.layerCount > 0)
            {
                if (TrySilentCrossFade(stateName, duration, 0))
                {
                    ActivateLayer(0);
                    Debug.LogWarning(
                        $"[SkillAnim] State '{stateName}' 在 Layer {primaryLayer} 中未找到，" +
                        "已回退到 Base Layer (0)。");
                    return true;
                }
            }

            return false;
        }

        private void LogFallbackRecovery(string animClipName, int layerIndex, string recoveredName)
        {
            string layerName = _animator != null && layerIndex >= 0
                ? _animator.GetLayerName(layerIndex) : "?";
            Debug.LogWarning(
                $"[SkillAnim] animClipName='{animClipName}' 在 Layer '{layerName}' " +
                $"中未匹配 State，已自动回退到 '{recoveredName}'。\n" +
                "  建议将 SkillData.animClipName 清空，让系统自动推导 State 名称。");
        }

        /// <summary>HasState + CrossFade without logging on failure (used by fallback chain).</summary>
        private bool TrySilentCrossFade(string stateName, float duration, int layerIndex)
        {
            if (_animator == null || layerIndex < 0 || layerIndex >= _animator.layerCount)
                return false;
            int hash = Animator.StringToHash(stateName);
            if (!_animator.HasState(layerIndex, hash))
                return false;
            _animator.CrossFadeInFixedTime(stateName, duration, layerIndex);
            return true;
        }

        /// <summary>用 Animator.StringToHash 结果反查参数名（仅遍历一次，可能返回 null）。</summary>
        private string GetParamNameByHash(int hash)
        {
            if (_animator == null) return null;
            foreach (var p in _animator.parameters)
            {
                if (p.nameHash == hash) return p.name;
                // Fallback for dynamic parameters where nameHash might differ
                if (Animator.StringToHash(p.name) == hash) return p.name;
            }
            return null;
        }

        private int FindLayerByName(string layerName)
        {
            if (_animator == null) return -1;
            for (int i = 0; i < _animator.layerCount; i++)
            {
                if (_animator.GetLayerName(i) == layerName)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Get the Animator state name for a SkillData.
        /// Skills: "Skill_Q", "Skill_W", "Skill_E", "Skill_R"
        /// Attacks: "Attack_1", "Attack_2", "Attack_3", "Attack_4"
        /// </summary>
        private string GetAnimatorStateName(SkillData data)
        {
            // Skill Builder 的 animClipName 实际保存的是 Animator State 名称；
            // 运行时必须优先使用它，否则编辑器预览播放该状态，而游戏按 SkillQ
            // 推导 Skill_Q，二者会落到不同状态/不同动画。
            if (!string.IsNullOrWhiteSpace(data.animClipName))
                return data.animClipName;

            if (data.category == SkillCategory.BasicAttack)
                return "Attack_" + data.animStateId;

            // Skill: derive from trigger name "SkillQ" → "Skill_Q"
            if (!string.IsNullOrEmpty(data.animTrigger) && data.animTrigger.StartsWith("Skill") && data.animTrigger.Length > 5)
            {
                string letter = data.animTrigger.Substring(5);
                return "Skill_" + letter;
            }

            return "Skill_Q";
        }

        private float GetClipLength(SkillData data)
        {
            if (!string.IsNullOrEmpty(data.animClipName))
            {
                float len = FindClipByName(data.animClipName);
                if (len > 0f) return len;
            }

            string clipName;
            if (data.category == SkillCategory.BasicAttack)
                clipName = "attack0" + data.animStateId;
            else
                clipName = "spell0" + data.animStateId;

            float result = FindClipByName(clipName);
            return result > 0f ? result : 0.5f;
        }

        private float FindClipByName(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return -1f;
            if (_clipLengthCache.TryGetValue(clipName, out float len))
                return len;
            // 缓存未命中时回退遍历（运行时动态加载的控制器）
            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                foreach (var clip in _animator.runtimeAnimatorController.animationClips)
                {
                    if (clip != null && clip.name == clipName)
                    {
                        _clipLengthCache[clipName] = clip.length;
                        return clip.length;
                    }
                }
            }
            return -1f;
        }

        private static Transform FindChildByName(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                Transform found = FindChildByName(child, name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// Spawns a single VFXEntry, resolving the spawn bone and worldSpace flag.
        /// For projectile-type effects (World Space = true), uses the character's
        /// forward direction as rotation so the prefab flies in the attack direction.
        /// </summary>
        private void SpawnVFXEntry(VFXEntry entry)
        {
            if (entry.prefab == null) return;
            Transform spawnPoint = ResolveSpawnBone(entry.spawnBone);

            Vector3 spawnPos = spawnPoint.position + spawnPoint.TransformDirection(entry.positionOffset);
            Quaternion spawnRot;

            if (entry.worldSpace)
            {
                // Use character root forward so projectile flies in attack direction
                // Apply rotation offset on top of facing direction
                Quaternion facingRot = Quaternion.LookRotation(transform.forward, Vector3.up);
                spawnRot = facingRot * Quaternion.Euler(entry.rotationOffset);
                GameObject projObj = GameObject.Instantiate(entry.prefab, spawnPos, spawnRot);
                if (entry.scale != 1f) projObj.transform.localScale = projObj.transform.localScale * entry.scale;
                // Initialize projectile with damage and source info
                var projectile = projObj.GetComponent<VFXProjectile>();
                if (projectile != null && _currentData != null)
                {
                    var hitDet = GetComponent<HitDetector>();
                    LayerMask mask = hitDet != null ? hitDet.hitMask : ~0;
                    float dmg = SkillData.GetDamageFromGraph(_currentData);
                    // Try to get modified damage from SkillController
                    var sc = GetComponent<SkillController>();
                    if (sc != null) dmg = sc.GetModifiedDamage(_currentData);
                    projectile.Initialize(dmg, gameObject, mask);
                    if (_visualOnlyCast) projectile.SetVisualOnly(true);   // S2-5b:远端表现不伤
                }
            }
            else
            {
                spawnRot = spawnPoint.rotation * Quaternion.Euler(entry.rotationOffset);
                GameObject go = GameObject.Instantiate(entry.prefab, spawnPos, spawnRot);
                if (entry.scale != 1f) go.transform.localScale = go.transform.localScale * entry.scale;
                go.transform.SetParent(spawnPoint, worldPositionStays: true);
            }
        }

        private Transform ResolveSpawnBone(string boneName)
        {
            if (string.IsNullOrEmpty(boneName)) return transform;
            if (_animator != null && _animator.isHuman &&
                System.Enum.TryParse<HumanBodyBones>(boneName, true, out HumanBodyBones bone))
            {
                Transform t = _animator.GetBoneTransform(bone);
                if (t != null) return t;
            }
            Transform found = FindChildByName(transform, boneName);
            return found != null ? found : transform;
        }

        /// <summary>
        /// 在指定位置播放带音调随机化的 AudioClip。挂临时 AudioSource，播放完毕后销毁。
        /// 3D 音效，5~30m 线性衰减。多次调用同一 clip 自动避免听感重复。
        /// </summary>
        public static void PlaySfxAtPoint(AudioClip clip, Vector3 worldPos, float pitchRandomPercent)
        {
            if (clip == null) return;
            var go = new GameObject($"[SFX]_{clip.name}");
            go.transform.position = worldPos;
            var src = go.AddComponent<AudioSource>();
            src.clip         = clip;
            src.spatialBlend = 1f;
            src.rolloffMode  = AudioRolloffMode.Linear;
            src.minDistance  = 5f;
            src.maxDistance  = 30f;
            if (pitchRandomPercent > 0f)
            {
                float r = pitchRandomPercent / 100f;
                src.pitch = 1f + Random.Range(-r, r);
            }
            src.Play();
            Object.Destroy(go, clip.length + 0.1f);
        }

        // ── 多段动画运行时调度 ─────────────────────────────────

        /// <summary>
        /// 遍历 graphData 中的 AnimClipLayerData / MultiStageLayerData 节点，
        /// 按各自的 triggerTime 在当前活跃动画层上 CrossFadeInFixedTime 播放新的动画片段。
        /// 
        /// 每个节点只触发一次（通过 _animLayerTriggered 标记索引），
        /// 每次 PlayAnimation/ResetSkillAnimation 重置标记。
        /// </summary>
        private void ScheduleGraphAnimationLayers()
        {
            if (_currentData == null || _currentData.graphData == null || _currentData.graphData.Count == 0)
                return;

            var graphData = _currentData.graphData;
            for (int i = 0; i < graphData.Count; i++)
            {
                var node = graphData[i];
                if (node == null || _animLayerTriggered.Contains(i))
                    continue;

                AnimationClip clip = null;
                float speed = 1f;
                float triggerAt = 0f;

                if (node is AnimClipLayerData clipLayer)
                {
                    if (clipLayer.visualOnly) continue;   // 编辑器可视化节点(Timeline 轨道),不调度
                    clip = clipLayer.animClip;
                    speed = clipLayer.animSpeed > 0f ? clipLayer.animSpeed : 1f;
                    triggerAt = clipLayer.triggerTime;
                }
                else if (node is MultiStageLayerData multiStage)
                {
                    clip = multiStage.animClip;
                    speed = multiStage.animSpeed > 0f ? multiStage.animSpeed : 1f;
                    triggerAt = multiStage.triggerTime;

                    // 段起手 VFX（可覆盖全局 vfxOnCast）
                    // P3:使用节点 VFX spawn 配置字段(对齐 CastVFXData),不再硬编码零值
                    if (multiStage.vfxOnCast != null)
                        SpawnVFXEntry(new VFXEntry
                        {
                            prefab = multiStage.vfxOnCast,
                            spawnBone = multiStage.spawnBone,
                            positionOffset = multiStage.positionOffset,
                            rotationOffset = multiStage.rotationOffset,
                            scale = multiStage.scale > 0f ? multiStage.scale : 1f,
                            worldSpace = multiStage.worldSpace,
                            destroyAfterSeconds = multiStage.destroyAfterSeconds,
                        });

                    // P7:段起手 SFX — 读 MultiStageLayerData 自身的 pitch(不再读 SkillData legacy 字段)
                    if (multiStage.sfxOnCast != null)
                        PlaySfxAtPoint(multiStage.sfxOnCast, transform.position,
                            multiStage.sfxPitchRandomPercent);
                }
                else
                {
                    continue;
                }

                if (_animTimer < triggerAt)
                    continue;

                _animLayerTriggered.Add(i);

                if (clip == null)
                    continue;

                int layer = _activeLayerIndex >= 0 ? _activeLayerIndex : _fullBodyLayerIndex;
                if (layer >= 0)
                {
                    SafeCrossFade(clip.name, fadeInDuration, layer, speed);
                }
            }
        }

        #endregion
    }
}
