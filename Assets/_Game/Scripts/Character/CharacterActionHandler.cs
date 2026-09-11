using UnityEngine;
using System.Collections;
using Game.SkillSystem;

namespace Game.Character
{
    [AddComponentMenu("Game/Character System/Character Action Handler")]
    public class CharacterActionHandler : MonoBehaviour
    {
        [Header("Input")]
        public KeyCode actionKey = KeyCode.E;

        [Header("Settings")]
        public string actionTag = "Action";

        [Header("Debug - Read Only")]
        public CharacterActionTrigger currentTrigger;
        public bool canTriggerAction;
        public bool isPlayingAnimation;

        private Animator _anim;
        private CharacterMotor _motor;
        private Rigidbody _rb;
        private CapsuleCollider _col;
        private SkillAnimPlayer _animPlayer;
        // 是否存在输入组件(玩家)。无输入组件(AI)时忽略按键,仅 autoAction/TryTriggerAction API 生效(2026-07-30 组件化)
        private bool _hasInputHandler;

        private AnimatorStateInfo _baseLayerInfo;
        private int _baseLayerIdx = 0;
        private bool _settingsApplied = false;
        private bool _resetting = false;
        private bool _matchTargetApplied = false;

        // 每次进入 trigger 区域 = false，触发一次动画后 = true，OnTriggerExit 才重置
        private bool _playedThisContact = false;
        // 动画结束后标记，下一帧起才处理 OnTriggerExit/Stay
        private bool _justFinished = false;
        // 动画结束后冷却：防止角色仍停留在 trigger 区域内时，OnTriggerStay 立即二次触发
        // （step-up / climb 动画结束后角色往往还压在触发器上 → 会重复播放第二次向上爬）
        private float _retriggerCooldown = 0f;
        private const float RetriggerCooldownTime = 0.6f;

        // CrossFadeInFixedTime 鍦?AnimatePhysics 模式下等下€涓墿鐞嗘才生?        // 用状态机：triggered=刚触发未进过渡，inAnim=过渡垨鎾斁涓紝done=鎾畬
        private enum TrackState { Idle, WaitTransitionStart, WaitTransitionEnd, Playing }
        private TrackState _trackState = TrackState.Idle;
        private float _trackWaitTimer = 0f;          // WaitTransition 超时计时器
        private const float TrackWaitTimeout = 0.5f; // 超过 0.5s 仍未进入过渡则强制推进

        private ICharacterInputSource _inputSource;   // 输入源(S2-0 收口)

        void Awake()
        {
            _anim       = GetComponent<Animator>();
            _motor      = GetComponent<CharacterMotor>();
            _rb         = GetComponent<Rigidbody>();
            _col        = GetComponent<CapsuleCollider>();
            _animPlayer = GetComponent<SkillAnimPlayer>();
            _hasInputHandler = GetComponent<CharacterInputHandler>() != null;
            _inputSource = CharacterInputSourceResolver.Resolve(this);
        }

        void LateUpdate()
        {
            RefreshBaseLayer();
            TriggerActionInput();
            TrackAnimationState();
            if (isPlayingAnimation && _trackState == TrackState.Playing) AnimationBehaviour();
            if (_justFinished) _justFinished = false;  // 下一帧起恢 trigger 鐩戝惉
            if (_retriggerCooldown > 0f) _retriggerCooldown -= Time.deltaTime;
        }

        private void RefreshBaseLayer()
        {
            if (_anim == null) return;
            _baseLayerIdx = 0;
            for (int i = 0; i < _anim.layerCount; i++)
                if (_anim.GetLayerName(i) == "NormalState") { _baseLayerIdx = i; break; }
            _baseLayerInfo = _anim.GetCurrentAnimatorStateInfo(_baseLayerIdx);
        }

        // 兜底：动画播完（不在 clip 且不在过渡中）时繚 reset
        // 兜底：动画播完（不在目标 clip 且不在过渡中）时确保 reset
        private void TrackAnimationState()
        {
            if (!isPlayingAnimation || currentTrigger == null) return;

            bool inTransition = _anim.IsInTransition(_baseLayerIdx);

            switch (_trackState)
            {
                case TrackState.WaitTransitionStart:
                    // 等过渡开始；超时则强制推进，避免 Animator 条件冲突导致永久卡死
                    _trackWaitTimer += Time.deltaTime;
                    if (inTransition)
                    {
                        _trackState     = TrackState.WaitTransitionEnd;
                        _trackWaitTimer = 0f;
                    }
                    else if (_trackWaitTimer >= TrackWaitTimeout)
                    {
                        if (_motor != null && _motor.debugMode)
                            Debug.LogWarning($"[ActionHandler] WaitTransitionStart 超时（{TrackWaitTimeout}s），强制切到 Playing。检查 Animator 是否有冲突条件。");
                        _trackState     = TrackState.Playing;
                        _trackWaitTimer = 0f;
                    }
                    return;

                case TrackState.WaitTransitionEnd:
                    _trackWaitTimer += Time.deltaTime;
                    if (!inTransition)
                    {
                        _trackState     = TrackState.Playing;
                        _trackWaitTimer = 0f;
                    }
                    else if (_trackWaitTimer >= TrackWaitTimeout)
                    {
                        if (_motor != null && _motor.debugMode)
                            Debug.LogWarning($"[ActionHandler] WaitTransitionEnd 超时（{TrackWaitTimeout}s），强制切到 Playing。");
                        _trackState     = TrackState.Playing;
                        _trackWaitTimer = 0f;
                    }
                    return;

                case TrackState.Playing:
                    if (inTransition) return;
                    bool inClip = _baseLayerInfo.IsName(currentTrigger.playAnimation);
                    if (!inClip)
                        ResetPlayerSettings();
                    return;
            }
        }

        private void TriggerActionInput()
        {
            if (currentTrigger == null || !canTriggerAction) return;
            if (_playedThisContact) return;      // 鏈接触已经触发过，不重?
            bool conditionsOk = ActionConditions();
            bool shouldTrigger = (currentTrigger.autoAction && conditionsOk)
                              || (_hasInputHandler && _inputSource != null && _inputSource.GetKeyDown(actionKey) && conditionsOk);

            if (shouldTrigger)
            {
                _playedThisContact = true;
                currentTrigger.OnDoAction.Invoke();
                TriggerAnimation();
            }
        }

        /// <summary>
        /// 程序化触发 API(AI/外部驱动,2026-07-30 组件化)。
        /// 走与按键完全相同的条件检查;成功触发返回 true。
        /// </summary>
        public bool TryTriggerAction()
        {
            if (currentTrigger == null || !canTriggerAction) return false;
            if (_playedThisContact || !ActionConditions()) return false;
            _playedThisContact = true;
            currentTrigger.OnDoAction.Invoke();
            TriggerAnimation();
            return true;
        }

        private bool ActionConditions()
        {
            if (isPlayingAnimation)                                       return false;
            if (_motor != null && _motor.isJumping)                      return false;
            if (_motor != null && _motor.actions)                        return false;
            if (_anim.IsInTransition(_baseLayerIdx))                     return false;
            if (_animPlayer != null && !_animPlayer.CanStartNewAction()) return false;
            return true;
        }

        private void TriggerAnimation()
        {
            if (currentTrigger == null) return;

            // 防止重复触发：已经在播放同一动画时不再次触发
            if (isPlayingAnimation && _baseLayerInfo.IsName(currentTrigger.playAnimation))
            {
                if (_motor != null && _motor.debugMode)
                    Debug.LogWarning($"[ActionHandler] Animation '{currentTrigger.playAnimation}' already playing, skip trigger");
                return;
            }

            if (_motor != null && _motor.debugMode)
                Debug.Log($"[ActionHandler] Trigger animation '{currentTrigger.playAnimation}' for '{currentTrigger.name}'");
            if (string.IsNullOrEmpty(currentTrigger.playAnimation)) return;

            isPlayingAnimation = true;
            _matchTargetApplied = false;
            _trackState        = TrackState.WaitTransitionStart;
            _trackWaitTimer    = 0f;

            // ── 入场对齐（在 ApplyPlayerSettings 之前，重力/速度归零后再移动）──
            // 朝向不再在此处瞬间对齐：瞬转会表现为"顿一下"。交给 AnimationBehaviour
            // 的 RotateTowards 平滑旋转，翻越过程朝向过渡自然。

            // 2. 位置对齐：若 trigger 上配置了 snapPosition（可选子 Transform），
            //    且没有 matchTarget 负责精确对位，则立即 snap 到该点 XZ，保留 Y。
            //    这样 CrossFade 的起始姿态不会因为站位偏差而产生滑步。
            if (currentTrigger.snapPosition != null && currentTrigger.matchTarget == null)
            {
                Vector3 sp = currentTrigger.snapPosition.position;
                Vector3 pos = transform.position;
                pos.x = sp.x;
                pos.z = sp.z;
                transform.position = pos;
                if (_rb != null) _rb.MovePosition(pos);
            }

            // 同步 Animator 根基准：applyRootMotion 从 false→true 切换瞬间，
            // 根基准停在旧位置会产生"陈旧 delta"（首帧巨大位移），被异常保护丢弃后
            // 首帧不动 → 翻越开始"顿一下"。先同步根基准 + skipNextRootDelta 消除。
            if (_motor != null)
                _motor.PrepareAnimatorRootMotion(transform.position, transform.rotation);

            ApplyPlayerSettings();

            // CrossFade 时长：根据角色当前水平速度动态调整
            //   静止接近 0 → 用短过渡（0.08f）快速切入
            //   全速跑进来 → 用长一些的过渡（0.18f）消除滑步残像
            float crossFadeDuration = CalcCrossFadeDuration();
            _anim.CrossFadeInFixedTime(currentTrigger.playAnimation, crossFadeDuration);
            StartCoroutine(currentTrigger.OnDoActionDelay(gameObject));

            if (currentTrigger.destroyAfter)
                StartCoroutine(DestroyDelay(currentTrigger));
        }

        /// <summary>
        /// 根据角色水平速度线性插值 CrossFade 时长。
        /// 静止 → 0.08s（快速切入），全速（≥3m/s）→ 0.18s（足够时间消融运动残像）。
        /// </summary>
        private float CalcCrossFadeDuration()
        {
            if (_rb == null) return 0.1f;
            float horizSpeed = new Vector3(_rb.velocity.x, 0f, _rb.velocity.z).magnitude;
            return Mathf.Lerp(0.08f, 0.18f, Mathf.Clamp01(horizSpeed / 3f));
        }

        private void AnimationBehaviour()
        {
            if (currentTrigger == null) return;

            if (currentTrigger.matchTarget != null)
            {
                float normTime = Mathf.Repeat(_baseLayerInfo.normalizedTime, 1f);
                // 固定窗口：start/end 用配置值，已过窗口由 CharacterMotor.MatchTarget 的
                // normalizedTime>end 保护跳过，避免动态缩短导致匹配不连续。
                float start = currentTrigger.startMatchTarget;
                float end   = currentTrigger.endMatchTarget;

                // 窗口保护：
                //   1. start 必须 < end，否则窗口已过，跳过（避免角色弹飞）
                //   2. 目标距离过远（>4m）说明 matchTarget 设置有误，跳过并 Warning
                if (start < end)
                {
                    Vector3 targetPos = currentTrigger.matchTarget.position;
                    float dist = Vector3.Distance(transform.position, targetPos);
                    if (dist > 4f)
                    {
                        if (_motor != null && _motor.debugMode)
                            Debug.LogWarning($"[ActionHandler] MatchTarget '{currentTrigger.name}' 距离 {dist:F2}m 过远，已跳过。请确认 matchTarget 子物体位置。");
                    }
                    else
                    {
                        // 使用触发器资产明确配置的 AvatarTarget/Mask；不同动作的目标点语义不同，
                        // 强制 Root + 全轴会把 JumpOver/ClimbUp 吸到错误高度并产生回落。
                        _matchTargetApplied = _motor != null && _motor.MatchTarget(
                            targetPos,
                            currentTrigger.matchTarget.rotation,
                            currentTrigger.avatarTarget,
                            new MatchTargetWeightMask(currentTrigger.matchTargetMask, 0f),
                            start,
                            end);
                    }
                }
            }

            if (currentTrigger.useTriggerRotation)
            {
                // 用 Slerp 替代 Lerp：球面插值在大角度时曲线更自然，
                // 并改为基于固定速度（而非 normalizedTime）驱动，防止动画慢时旋转也慢
                float rotSpeed = 360f * Time.deltaTime;   // 最大 360°/s
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    currentTrigger.transform.rotation,
                    rotSpeed);
            }

            if (currentTrigger.resetPlayerSettings
                && _baseLayerInfo.normalizedTime >= currentTrigger.endExitTimeAnimation)
            {
                ResetPlayerSettings();
            }
        }

        private void ApplyPlayerSettings()
        {
            if (_settingsApplied || currentTrigger == null) return;
            _settingsApplied = true;

            if (currentTrigger.disableGravity)
            {
                _rb.useGravity = false;
                _rb.velocity   = Vector3.zero;
                _anim.SetBool(AnimatorParams.IsGrounded, true);
            }

            // disableCollision锛氱鐢?trigger 鑷韩鐨?collider锛岃€不色的
            // 避免角色碰撞体变 trigger 后重复触?OnTriggerEnter/Exit
            if (currentTrigger.disableCollision)
            {
                var tc = currentTrigger.GetComponent<Collider>();
                if (tc != null) tc.enabled = false;
                if (_col != null) _col.enabled = false;
            }

            // 保持 AnimatePhysics：Animator 与 Rigidbody 在同一物理步采样，
            // Root Motion(deltaPosition) 与物理同步。切 Normal 后 deltaPosition 在渲染帧
            // 采样、MovePosition 却走 FixedUpdate，位移与物理不同步 → 翻越过程跳帧卡顿。
            _anim.updateMode = AnimatorUpdateMode.AnimatePhysics;
            // MatchTarget 只有在 Root Motion 开启时才会把动作根位移应用到角色；
            // 新创建角色此前仅播放 ClimbUp/JumpOver 视觉动画，位置保持不动。
            _anim.applyRootMotion = true;

            if (_motor != null)
                _motor.customAction = true;
        }

        private void ResetPlayerSettings()
        {
            if (_resetting) return;
            _resetting = true;

            isPlayingAnimation = false;
            _settingsApplied   = false;
            canTriggerAction   = false;
            _trackState        = TrackState.Idle;

            if (currentTrigger != null && currentTrigger.disableGravity)
            {
                // MatchTarget 已经通过 Root Motion 消费；不再在结束帧硬写 matchTarget.position。
                // 硬吸附会把 Y/XZ 误差放大成“升高后回落/横跳/闪切固定点”。
                _rb.useGravity = true;
                // 退出速度继承：给角色沿 trigger 前方的小初速，防止动画结束后瞬间弹停
                float exitSpeed = currentTrigger != null ? currentTrigger.exitSpeed : 0f;
                if (exitSpeed > 0f)
                {
                    Vector3 fwd = currentTrigger.transform.forward;
                    fwd.y = 0f;
                    _rb.velocity = (fwd.sqrMagnitude > 0.001f ? fwd.normalized : transform.forward) * exitSpeed;
                }
                else
                {
                    _rb.velocity = Vector3.zero;
                }
            }
            // 鎭㈠ trigger collider
            if (currentTrigger != null && currentTrigger.disableCollision)
            {
                var tc = currentTrigger.GetComponent<Collider>();
                if (tc != null) tc.enabled = true;
                if (_col != null) _col.enabled = true;
            }

            if (_motor != null)
                _motor.customAction = false;

            // 普通移动不消费交互 Root Motion；恢复 AnimatePhysics 模式，与物理步同步
            _anim.applyRootMotion = false;
            _anim.updateMode = AnimatorUpdateMode.AnimatePhysics;

            _resetting    = false;
            _justFinished = true;
            _retriggerCooldown = RetriggerCooldownTime;
            if (_motor != null && _motor.debugMode)
                Debug.Log($"[ActionHandler] Animation finished, start cooldown {RetriggerCooldownTime}s");
        }

        private void FullReset()
        {
            if (_resetting) return;
            ResetPlayerSettings();
            currentTrigger     = null;
            _playedThisContact = false;   // 离开 trigger 才允许下一次进入
            // 即使 OnTriggerExit 与另一个重叠 Collider 同帧到达，也保留短冷却，
            // 防止 JumpOver/StepUp 在同一障碍物上连续横跳。
            _retriggerCooldown = RetriggerCooldownTime;
        }

        private bool TryGetActionTrigger(Collider other, out CharacterActionTrigger trigger)
        {
            trigger = null;
            if (other == null) return false;

            // 场景动作可能把 Collider 放在 TriggerAction/MatchTarget 子节点，
            // 旧逻辑只查 Collider 同级对象，导致新角色“进了区域但 currentTrigger 为空”。
            trigger = other.GetComponent<CharacterActionTrigger>()
                ?? other.GetComponentInParent<CharacterActionTrigger>();
            if (trigger == null) return false;

            // 组件是动作语义的权威来源；Tag 仅作为编辑器提示，不阻断运行时交互。
            // 旧场景中部分实例遗留 Finish/EditorOnly Tag，继续把 Tag 作为硬门控会让新角色播放动画却无法完成交互。
            return true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (!TryGetActionTrigger(other, out var trigger)) return;
            if (isPlayingAnimation) return;
            if (_justFinished) return;
            _playedThisContact = false;
            trigger.OnPlayerEnter.Invoke();
        }

        void OnTriggerStay(Collider other)
        {
            if (!TryGetActionTrigger(other, out _)) return;
            if (isPlayingAnimation) return;
            if (_justFinished) return;
            if (_retriggerCooldown > 0f)
            {
                if (_motor != null && _motor.debugMode)
                    Debug.Log($"[ActionHandler] OnTriggerStay blocked by cooldown: {_retriggerCooldown:F2}s");
                return;
            }
            if (_playedThisContact) return;
            CheckForTriggerAction(other);
        }

        void OnTriggerExit(Collider other)
        {
            if (!TryGetActionTrigger(other, out var trigger)) return;
            if (isPlayingAnimation) return;   // 动画切换产生的假 Exit，忽略
            trigger.OnPlayerExit.Invoke();
            FullReset();
        }

        private void CheckForTriggerAction(Collider other)
        {
            if (!TryGetActionTrigger(other, out var trigger)) return;

            // activeFromForward：角色朝向与触发器前方夹角 < 60° 才激活（统一使用 Vector3.Angle）
            if (!trigger.activeFromForward || Vector3.Angle(transform.forward, trigger.transform.forward) <= 60f)
            {
                currentTrigger   = trigger;
                canTriggerAction = true;
                trigger.OnPlayerEnter.Invoke();
            }
            else
            {
                if (currentTrigger != null) currentTrigger.OnPlayerExit.Invoke();
                canTriggerAction = false;
            }
        }

        private IEnumerator DestroyDelay(CharacterActionTrigger trigger)
        {
            var t = trigger;
            yield return new WaitForSeconds(t.destroyDelay);
            FullReset();
            if (t != null) Destroy(t.gameObject);
        }

        public void ForceReset() => FullReset();
    }
}

