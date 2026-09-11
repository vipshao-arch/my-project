using UnityEngine;
using Game.Character;

namespace Game.SkillSystem
{
    /// <summary>
    /// Skill/Attack controller with combat locomotion.
    /// Combat mode: always Strafe + input remapped to target-relative space.
    /// Base Layer locomotion driven by remapped input for correct directional animation.
    /// TurnOnSpot: when idle in combat mode and mouse direction changes significantly,
    /// play TurnOnSpot animation instead of snapping rotation.
    /// </summary>
    [AddComponentMenu("Game/Skill System/Skill Controller")]
    [DefaultExecutionOrder(-10)]
    [RequireComponent(typeof(Animator))]
    public class SkillController : MonoBehaviour
    {
        [Header("Skill Slots (1 ~ 8)")]
        public SkillData[] skillSlots = new SkillData[8];

        [Header("Basic Attack Slots")]
        public SkillData[] attackSlots = new SkillData[8];

        [Header("Input Keys")]
        public KeyCode skill1Key = KeyCode.Alpha1;
        public KeyCode skill2Key = KeyCode.Alpha2;
        public KeyCode skill3Key = KeyCode.Alpha3;
        public KeyCode skill4Key = KeyCode.Alpha4;
        public KeyCode skill5Key = KeyCode.Alpha5;
        public KeyCode skill6Key = KeyCode.Alpha6;
        public KeyCode skill7Key = KeyCode.Alpha7;
        public KeyCode skill8Key = KeyCode.Alpha8;
        public int attackMouseButton = 0;

        [Header("Combat Settings")]
        [Tooltip("How long to stay in combat mode after last attack/skill")]
        public float combatModeTimeout = 3f;

        [Header("Turn On Spot")]
        [Tooltip("Minimum angle (degrees) between current forward and mouse direction to trigger TurnOnSpot")]
        public float turnOnSpotAngleThreshold = 30f;
        [Tooltip("Rotation speed (degrees/sec) during TurnOnSpot. Higher = faster turn.")]
        public float turnOnSpotRotationSpeed = 200f;

        [Header("References (auto-assigned)")]
        public SkillAnimPlayer animPlayer;
        public SkillMovementController movementController;
        public WeaponHolder weaponHolder;
        public CharacterInputHandler inputHandler;
        public HitDetector hitDetector;

        private SkillInstance[] _instances;
        private int _activeCastSlot = -1;
        private const int ATTACK_SLOT_ID = 100;

        private Animator _animator;
        private Camera _mainCamera;  // 缓存主摄像机
        private ICharacterMotor _motor;
        // 参数名统一走 AnimatorParams 契约(2026-07-30)
        private static readonly int InputMagnitudeHash = AnimatorParams.InputMagnitude;
        private static readonly int TurnOnSpotDirectionHash = AnimatorParams.TurnOnSpotDirection;
        private static readonly int MoveAttackHash = AnimatorParams.MoveAttack;

        // CombatState layer index (resolved at runtime)
        private int _combatStateLayerIndex = -1;

        // CombatState layer fade-out
        [Header("Transition Settings")]
        [Tooltip("Duration in seconds to fade CombatState layer weight from 1— on ExitCombatMode")]
        public float combatLayerFadeOutDuration = 0.25f;
        private float _combatLayerFadeTimer = 0f;
        private bool _isCombatLayerFadingOut = false;

        // Combat state
        private bool _wasMoving   = false;
        private bool _wasGrounded = true;
        private bool _wasJumping  = false;
        private bool _wasRolling  = false;
        private bool _inCombatMode = false;
        private bool _movingAttackIntent = false;
        private float _combatTimer = 0f;
        private Vector3 _targetDirection = Vector3.forward;
        private ICharacterMotor.LocomotionType _savedLocomotionType;

        // TurnOnSpot state
        private bool _isTurningOnSpot = false;
        private bool _savedTurnOnSpotAnim = false;

        [Header("Attack Facing (攻击朝向,2026-07-30 原生实现)")]
        [Tooltip("攻击朝向总开关(默认开:静止普攻锁定起手瞄准/移动攻击与战斗移动根实时跟随鼠标,下半身+位移基于攻击朝向系映射)。关闭=最初表现:起手面向瞄准点+战斗中根跟随瞄准+投影输入映射。")]
        public bool enableAttackFacingHold = true;

        [Tooltip("诊断日志(临时):输出攻击朝向/输入映射路径,排查移动攻击表现问题。")]
        public bool debugAttackFacing = false;

        // 攻击朝向锁定/保持状态
        private Vector3 _lastAttackFacing;       // 普攻起手时锁定的朝向
        private bool    _hasLastAttackFacing;
        private bool    _postAttackFacingHeld;   // 攻击完全结束且无移动输入 → 保持当前朝向
        private bool    _wasAttackPoseActive;

        private ICharacterInputSource _inputSource;   // 输入源(S2-0 收口):本地=LocalInputSource,远端=NullInputSource

        void Awake()
        {
            if (animPlayer == null) animPlayer = GetComponent<SkillAnimPlayer>();
            if (movementController == null) movementController = GetComponent<SkillMovementController>();
            if (weaponHolder == null) weaponHolder = GetComponent<WeaponHolder>();
            if (inputHandler == null) inputHandler = GetComponent<CharacterInputHandler>();
            if (hitDetector == null) hitDetector = GetComponent<HitDetector>();
            _animator = GetComponent<Animator>();
            _motor = GetComponent<ICharacterMotor>();
            _mainCamera = Camera.main;
            _inputSource = CharacterInputSourceResolver.Resolve(this);

            // Resolve CombatState layer index
            if (_animator != null)
                _combatStateLayerIndex = GetLayerIndex(_animator, "CombatState");

            // Auto-load attack/skill data if slots are empty
            AutoLoadSkillData();

            _instances = new SkillInstance[skillSlots.Length];
            for (int i = 0; i < skillSlots.Length; i++)
            {
                if (skillSlots[i] != null)
                    _instances[i] = new SkillInstance(skillSlots[i]);
            }
        }

        /// <summary>
        /// 自动加载 SkillData 资源到空槽位。
        /// 优先从 Resources 加载；编辑器模式下也从 AssetDatabase 搜索。
        /// </summary>
        private void AutoLoadSkillData()
        {
            // Check if attackSlots are all null
            bool attacksEmpty = true;
            for (int i = 0; i < attackSlots.Length; i++)
            {
                if (attackSlots[i] != null) { attacksEmpty = false; break; }
            }

            // Check if skillSlots are all null
            bool skillsEmpty = true;
            for (int i = 0; i < skillSlots.Length; i++)
            {
                if (skillSlots[i] != null) { skillsEmpty = false; break; }
            }

            if (!attacksEmpty && !skillsEmpty) return;

            // Load all SkillData assets from Resources
            var allData = Resources.LoadAll<SkillData>("SkillData");
            if (allData == null || allData.Length == 0)
            {
                allData = Resources.LoadAll<SkillData>("");
            }

#if UNITY_EDITOR
            if (allData == null || allData.Length == 0)
            {
                // Editor-only: search via AssetDatabase (finds assets outside Resources/)
                var guids = UnityEditor.AssetDatabase.FindAssets("t:SkillData");
                var list = new System.Collections.Generic.List<SkillData>();
                foreach (var guid in guids)
                {
                    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillData>(path);
                    if (asset != null) list.Add(asset);
                }
                allData = list.ToArray();
            }
#endif

            if (allData == null || allData.Length == 0)
            {
                Debug.LogWarning("[SkillController] No SkillData assets found. Attack/skill slots will be empty.");
                return;
            }

            Debug.Log($"[SkillController] Auto-loading {allData.Length} SkillData assets");

            if (attacksEmpty)
            {
                int attackIdx = 0;
                foreach (var data in allData)
                {
                    if (data.category == SkillCategory.BasicAttack && attackIdx < attackSlots.Length)
                    {
                        attackSlots[attackIdx] = data;
                        attackIdx++;
                    }
                }
                Debug.Log($"[SkillController] Auto-loaded {attackIdx} basic attack(s)");
            }

            if (skillsEmpty)
            {
                int skillIdx = 0;
                foreach (var data in allData)
                {
                    if (data.category == SkillCategory.Skill && skillIdx < skillSlots.Length)
                    {
                        skillSlots[skillIdx] = data;
                        skillIdx++;
                    }
                }
                Debug.Log($"[SkillController] Auto-loaded {skillIdx} skill(s)");
            }
        }

        void Update()
        {
            if (_instances == null) return;
            HandleInput();
            HandleMovementInterrupt();
            UpdateCombatLocomotion();
            UpdateCombatLayerFadeOut();
            UpdateActionsSuppression();

            // IsStrafing 写入已收敛到 CharacterMotor.UpdateAnimator 单点(2026-07-30),
            // 此处不再重复写入,避免双写入点。
        }

        /// <summary>
        /// 物理步内把当帧攻击朝向同步给 Motor 的 CombatFacing,供 Strafe 物理速度方向使用。
        ///
        /// 背景(2026-09-10 修复「实际位移方向不对」):
        ///   根朝向(LateUpdate)与位移方向(FixedUpdate)都读 CombatFacing,二者必须严格同源,
        ///   否则快速甩鼠标时「身体朝一个方向、人却滑向另一方向」。
        ///   若只在 Update 写 CombatFacing,则本帧 FixedUpdate(先于 Update 执行)读到的
        ///   是上一帧值——位移方向永远比根朝向慢一帧,方向仍会错位。
        ///   故把「算攻击朝向 + 写 CombatFacing」整体移到 FixedUpdate:
        ///   SkillController 执行顺序 -10 先于 CharacterMotor(0),本物理步先写好 CombatFacing,
        ///   同一步内 CharacterMotor 用最新 CombatFacing 算速度;下一 LateUpdate 根也读同一
        ///   CombatFacing → 根与位移严格同源同帧,零相位差。
        ///   更新条件与 UpdateCombatLocomotion 对齐:移动攻击或非施法才更新;站立施法(朝向锁定)不改写。
        /// </summary>
        void FixedUpdate()
        {
            if (_instances == null) return;
            if (!_inCombatMode || _motor == null) return;

            bool isMoveAttack = enableAttackFacingHold && animPlayer != null && animPlayer.IsMoveAttack;
            bool isCasting = animPlayer != null && (animPlayer.IsCasting || animPlayer.IsFadingOut);
            if (isMoveAttack || !isCasting)
            {
                UpdateTargetDirection();
                _motor.CombatFacing = _targetDirection;
            }
        }

        void LateUpdate()
        {
            if (_instances == null) return;

            // Tick skill cooldowns
            for (int i = 0; i < _instances.Length; i++)
            {
                if (_instances[i] != null && _instances[i].State == SkillState.Cooldown)
                    _instances[i].Tick(Time.deltaTime);
            }

            // Check if animation finished naturally
            if (_activeCastSlot >= 0 && animPlayer != null && !animPlayer.IsCasting && !animPlayer.IsFadingOut)
            {
                if (_activeCastSlot != ATTACK_SLOT_ID && _activeCastSlot < _instances.Length && _instances[_activeCastSlot] != null)
                    _instances[_activeCastSlot].ForceCompleteCast();
                _activeCastSlot = -1;
            }

            // ── 攻击朝向锁定:仅「静止普攻」期间(有起手朝向记录 _hasLastAttackFacing),根节点钉在起手朝向 ──
            // 移动攻击没有 _lastAttackFacing,不会被钉住,改由下方 ApplyCombatRotation 实时跟随鼠标。
            bool attackPoseActive = enableAttackFacingHold && animPlayer != null && animPlayer.IsBasicAttackPose;
            bool pinnedToAttackFacing = attackPoseActive && _hasLastAttackFacing && _lastAttackFacing.sqrMagnitude > 0.01f;
            if (pinnedToAttackFacing)
            {
                transform.rotation = Quaternion.LookRotation(_lastAttackFacing);
                if (debugAttackFacing && attackPoseActive != _wasAttackPoseActive)
                    Debug.Log($"[AttackFacing] 锁定开始: facing={_lastAttackFacing}");
            }

            // 攻击姿态刚结束:解除朝向锁定;后续由移动/战斗旋转接管
            if (!attackPoseActive && _wasAttackPoseActive)
                _hasLastAttackFacing = false;
            _wasAttackPoseActive = attackPoseActive;

            // ── 战斗朝向:根实时面向鼠标(_targetDirection),战斗待机/战斗移动/移动攻击三者统一 ──
            // 仅两种情况不施加:①静止普攻期间(上面已钉住);②攻击后朝向保持期(_postAttackFacingHeld)。
            if (_inCombatMode && !pinnedToAttackFacing && !_postAttackFacingHeld && _targetDirection.sqrMagnitude > 0.01f)
            {
                ApplyCombatRotation();
            }
        }

        #region Input

        private void HandleInput()
        {
            if (IsInputLocked()) return;

            // WASD input cancels click-move
            if (inputHandler != null && inputHandler.IsClickMoving)
            {
                float h = _inputSource.MoveAxesRaw.x;
                float v = _inputSource.MoveAxesRaw.y;
                if (Mathf.Abs(h) > 0.05f || Mathf.Abs(v) > 0.05f)
                    inputHandler.CancelClickMove();
            }

            if (_inputSource.GetMouseButtonDown(attackMouseButton))
            {
                TryBasicAttack();
                return;
            }

            // 技能键位遍历（兼容旧 4 键 + 新 8 键）
            KeyCode[] allSkillKeys = { skill1Key, skill2Key, skill3Key, skill4Key, skill5Key, skill6Key, skill7Key, skill8Key };
            for (int i = 0; i < allSkillKeys.Length; i++)
            {
                if (_inputSource.GetKeyDown(allSkillKeys[i]))
                {
                    TryCastSkill(i);
                    return;
                }
            }
        }

        private void HandleMovementInterrupt()
        {
            if (_animator == null || animPlayer == null) return;

            float inputMag = _animator.GetFloat(InputMagnitudeHash);
            bool isMoving   = inputMag > 0.1f;
            bool isGrounded = _motor != null && _motor.isGrounded;
            bool isJumping  = _motor != null && _motor.isJumping;
            bool isRolling  = _motor != null && _motor.isRolling;

            // Movement start interrupt
            // enableAttackFacingHold 开启时:移动攻击不被移动打断,且仅后摇窗口可打断;
            // 关闭时保持旧行为(任意施法移动即断)。
            if (isMoving && !_wasMoving)
            {
                bool wasHoldingFacing = _postAttackFacingHeld;
                _postAttackFacingHeld = false;   // 移动开始即解除攻击后朝向保持

                // 攻击后保持期首次移动:恢复待机旋转,让角色自然转向移动方向(摄像机系)。
                // 战斗状态中(_inCombatMode=true)不动 lockRotation,保持战斗朝向锁定。
                if (wasHoldingFacing && _motor != null && !_inCombatMode)
                    _motor.lockRotation = false;

                bool interrupt = enableAttackFacingHold
                    ? (animPlayer.IsCasting && !animPlayer.IsMoveAttack && animPlayer.CanBeInterruptedByAction())
                    : animPlayer.IsCasting;
                if (interrupt)
                {
                    animPlayer.ResetSkillAnimation();
                    _activeCastSlot = -1;
                }
            }

            // Jump interrupt:离地边缘 OR 刚开始跳跃（移动攻击时 isGrounded 可能还未更新）
            bool jumpStarted = isJumping && !_wasJumping;
            bool liftOff     = _wasGrounded && !isGrounded;
            if (jumpStarted || liftOff)
            {
                if (animPlayer.IsCasting && animPlayer.CanBeInterruptedByAction())
                {
                    animPlayer.ResetSkillAnimation();
                    _activeCastSlot = -1;
                }
                else if (animPlayer.IsFadingOut)
                {
                    animPlayer.ResetSkillAnimation();
                    _activeCastSlot = -1;
                }
            }

            // Roll interrupt: 翻滚开始时立即打断技能（移动攻击可随时被翻滚取消）
            bool rollStarted = isRolling && !_wasRolling;
            if (rollStarted && animPlayer.IsCasting)
            {
                animPlayer.ResetSkillAnimation();
                _activeCastSlot = -1;
            }

            _wasMoving   = isMoving;
            _wasGrounded = isGrounded;
            _wasJumping  = isJumping;
            _wasRolling  = isRolling;
        }

        private bool IsInputLocked()
        {
            if (_motor != null && _motor.isDead) return true;
            return false;
        }

        #endregion

        #region Cast / Attack

        public void TryBasicAttack()
        {
            if (_motor != null && !_motor.isGrounded) return;
            if (animPlayer == null) return;
            if (!animPlayer.CanStartNewAction()) return;
            if (_motor != null && _motor.isInAction) return;   // 地形交互中(攀爬/翻越)拒绝起手

            // 移动攻击捕捉:必须在 CancelClickMove 清零输入之前读取
            bool isMovingAttack = HasCurrentMovementInput();
            _movingAttackIntent = isMovingAttack;

            if (inputHandler != null && inputHandler.IsClickMoving)
                inputHandler.CancelClickMove();

            if (weaponHolder != null && weaponHolder.HasWeapon(WeaponSlotType.MainHand))
                weaponHolder.SwitchToCombat(WeaponSlotType.MainHand);

            // 先进战斗,再播动画。
            // 开关关(最初表现):总是面向瞄准点——移动攻击=上半身攻击动画+根跟随瞄准,
            //   MoveAttack 判定回退 SkillAnimPlayer 旧行为(读 motor.input);
            // 开关开:静止攻击转向瞄准点并锁定;移动攻击根实时跟随鼠标(_targetDirection)。
            EnterCombatMode();
            if (enableAttackFacingHold && isMovingAttack)
            {
                UpdateTargetDirection();
                UnlockAttackFacing();
                // 起手当帧同步位移方向基准:与 FaceMouseDirection 一致,消除「按住位移键触发攻击」
                // 时 CombatFacing 残留旧值导致的位移方向跳变(后续每物理步由 FixedUpdate 实时跟随鼠标)。
                if (_motor != null && _targetDirection.sqrMagnitude > 0.01f)
                    _motor.CombatFacing = _targetDirection;
            }
            else
            {
                FaceMouseDirection();
            }

            // 始终传入起手前捕获的移动状态：即使关闭 AttackFacingHold，
            // 也不能回退读取 CancelClickMove 后已被清零的 motor.input，否则移动攻击会走错误层/错误镜像。
            animPlayer.PlayRandomAttack(attackSlots, isMovingAttack);
            if (!animPlayer.IsCasting)
            {
                // 无有效普攻槽位:不留脏朝向状态
                UnlockAttackFacing();
                return;
            }

            _activeCastSlot = ATTACK_SLOT_ID;

            // 联机(S2-5):广播普攻动画给远端复播(单机未 Spawn 时自动忽略)
            GetComponent<Game.Net.NetworkCastRelay>()?.BroadcastLocal(
                animPlayer.CurrentStateName, animPlayer.CurrentLayerIndex);

            if (enableAttackFacingHold && !isMovingAttack)
            {
                _lastAttackFacing     = _targetDirection;
                _hasLastAttackFacing  = true;
                _postAttackFacingHeld = false;
            }

            if (debugAttackFacing)
                Debug.Log($"[AttackFacing] 起手: targetDir={_targetDirection} isMovingAtk={isMovingAttack} mode={(isMovingAttack ? "移动攻击(根跟随鼠标)" : "静止锁定")} enableHold={enableAttackFacingHold}");
        }

        public void TryCastSkill(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _instances.Length) return;
            if (_instances[slotIndex] == null) return;
            if (animPlayer != null && !animPlayer.CanStartNewAction()) return;
            if (_motor != null && _motor.isInAction) return;   // 地形交互中(攀爬/翻越)拒绝起手
            if (!_instances[slotIndex].TryStartCast()) return;

            if (inputHandler != null && inputHandler.IsClickMoving)
                inputHandler.CancelClickMove();

            _activeCastSlot = slotIndex;
            _movingAttackIntent = false;
            var data = skillSlots[slotIndex];

            if (_animator != null)
                _animator.SetBool(MoveAttackHash, false);

            if (animPlayer != null)
                animPlayer.PlayAnimation(data);

            // 联机(S2-5b):广播技能名,远端解析后 visualOnly 图复播(单机未 Spawn 时自动忽略)
            GetComponent<Game.Net.NetworkCastRelay>()?.BroadcastSkillCast(data != null ? data.name : null);

            // 预设命中检测参数（OnHitFrame 触发时会使用这些值）
            if (hitDetector != null)
                hitDetector.SetupAttack(data);

            if (movementController != null)
                movementController.ApplySkillMovement(data);

            EnterCombatMode();
            FaceMouseDirection();
        }

        #endregion

        #region Combat Locomotion

        private void EnterCombatMode()
        {
            if (!_inCombatMode && _motor != null)
            {
                _savedLocomotionType = _motor.locomotionType;
                _savedTurnOnSpotAnim = _motor.turnOnSpotAnim;
            }

            _inCombatMode = true;
            _combatTimer = combatModeTimeout;

            if (_motor != null)
            {
                // Graves 的移动攻击入口先进入战斗 Strafe，再由输入投影到攻击朝向系；
                // 若等到下一帧 UpdateCombatLocomotion 才切换，S/A/D 与普攻同帧会先走 FreeMovement，
                // 根节点被错误旋转，无法形成边退边打。
                _motor.lockRotation = true;
                _motor.isStrafing = true;
                _motor.locomotionType = ICharacterMotor.LocomotionType.OnlyStrafe;
                (_motor as CharacterMotor)?.SetCombatMovementOverride(_movingAttackIntent);
            }

            if (_combatStateLayerIndex >= 0 && _animator != null)
            {
                _isCombatLayerFadingOut = false;
                _animator.SetLayerWeight(_combatStateLayerIndex, 1f);
            }
        }

        private void UpdateCombatLocomotion()
        {
            if (!_inCombatMode || _motor == null) return;

            // 移动攻击:下半身走 strafe 八向融合,根实时锁定瞄准点(跟随鼠标),
            // 上半身攻击动画覆盖;瞄准方向每帧更新;战斗计时暂停衰减(攻击中视为战斗中)。
            if (enableAttackFacingHold && animPlayer != null && animPlayer.IsMoveAttack)
            {
                UpdateTargetDirection();
                // 移动攻击与静止战斗统一走 strafe,根锁定瞄准点(鼠标朝向),
                // 移动方向由「攻击朝向系」投影驱动(W跟枪口),避免 Free 模式下 input 语义错位。
                _motor.locomotionType = ICharacterMotor.LocomotionType.OnlyStrafe;
                _motor.isStrafing = true;
                _motor.lockRotation = true;
                if (_isTurningOnSpot) EndTurnOnSpot();
                return;
            }

            _combatTimer -= Time.deltaTime;
            if (_combatTimer <= 0f)
            {
                // 普攻姿态保持窗:动画主体结束后仍保持 strafe 下半身(相对攻击朝向八向融合)
                bool inHoldWindow = enableAttackFacingHold && animPlayer != null && animPlayer.IsBasicAttackPose;
                if (inHoldWindow)
                {
                    _motor.locomotionType = ICharacterMotor.LocomotionType.OnlyStrafe;
                    _motor.isStrafing = true;
                    _motor.lockRotation = true;   // 移动攻击后恢复战斗旋转锁定
                    UpdateTurnOnSpot();
                    return;
                }

                ExitCombatMode();
                return;
            }

            bool isCasting = animPlayer != null && (animPlayer.IsCasting || animPlayer.IsFadingOut);
            if (!isCasting)
                UpdateTargetDirection();

            _motor.locomotionType = ICharacterMotor.LocomotionType.OnlyStrafe;
            _motor.isStrafing = true;
            _motor.lockRotation = true;   // 移动攻击后恢复战斗旋转锁定

            UpdateTurnOnSpot();
        }

        /// <summary>是否有真实移动输入(WASD 原始按键 / Motor 输入 / 点击移动)。</summary>
        private bool HasCurrentMovementInput()
        {
            bool rawWasd = _inputSource.GetKey(KeyCode.W) || _inputSource.GetKey(KeyCode.A)
                        || _inputSource.GetKey(KeyCode.S) || _inputSource.GetKey(KeyCode.D);
            bool motorInput = _motor != null && _motor.input.sqrMagnitude > 0.01f;
            bool clickMoving = inputHandler != null && inputHandler.IsClickMoving;
            return rawWasd || motorInput || clickMoving;
        }

        private void UnlockAttackFacing()
        {
            _hasLastAttackFacing  = false;
            _postAttackFacingHeld = false;
        }

        public void ExitCombatMode()
        {
            _inCombatMode = false;
            _movingAttackIntent = false;
            EndTurnOnSpot();

            // 攻击后朝向保持:攻击完全结束且无移动输入时退出战斗,角色保持当前(攻击)朝向,
            // 不被 FreeMovement/RotateWithCamera 立即拉回摄像机待机方向,直到下一次移动输入。
            // (此前该标志在 ExitCombatMode 之后才置位,而唯一的检查点又要求 _inCombatMode=true,
            //  导致它永远是死代码、从未生效;这里把判定前移到退出战斗当帧直接落地。)
            _postAttackFacingHeld = enableAttackFacingHold && !HasCurrentMovementInput();

            if (_motor != null)
            {
                _motor.locomotionType = _savedLocomotionType;
                _motor.isStrafing = false;
                _motor.lockRotation = _postAttackFacingHeld;   // 保持期仍锁定旋转,否则恢复待机旋转
                (_motor as CharacterMotor)?.SetCombatMovementOverride(false);
            }

            if (weaponHolder != null && weaponHolder.HasWeapon(WeaponSlotType.MainHand))
                weaponHolder.SwitchToHolster(WeaponSlotType.MainHand);

            if (_combatStateLayerIndex >= 0 && _animator != null)
            {
                _isCombatLayerFadingOut = true;
                _combatLayerFadeTimer = combatLayerFadeOutDuration;
            }
        }

        #endregion

        #region CombatState Layer Fade

        private void UpdateCombatLayerFadeOut()
        {
            if (!_isCombatLayerFadingOut || _combatStateLayerIndex < 0 || _animator == null)
                return;

            _combatLayerFadeTimer -= Time.deltaTime;
            if (_combatLayerFadeTimer <= 0f)
            {
                _animator.SetLayerWeight(_combatStateLayerIndex, 0f);
                _isCombatLayerFadingOut = false;
            }
            else
            {
                float t = Mathf.Clamp01(_combatLayerFadeTimer / combatLayerFadeOutDuration);
                _animator.SetLayerWeight(_combatStateLayerIndex, t * t);
            }
        }

        #endregion

        #region Actions Suppression

        /// <summary>
        /// When NormalState enters an Actions state (Roll / QuickStop / LandHigh / CustomAction),
        /// OR when SkillFullBody layer is active (idle attack —full body override),
        /// drop CombatState layer weight to 0 so the action/attack animation is not disturbed.
        /// Restore weight to 1 as soon as the condition clears (if still in combat mode).
        ///
        /// NOTE: Invector's "actions" flag is not available after decoupling.
        /// For now, the suppression only triggers on full-body skill activity.
        /// If action-state suppression (e.g. roll/land) is needed, GravesMotor
        /// should expose an `IsInAction` property and this method should use it.
        /// </summary>
        private void UpdateActionsSuppression()
        {
            if (_combatStateLayerIndex < 0 || _animator == null || _motor == null) return;

            bool fullBodySkillActive = animPlayer != null
                && (animPlayer.IsCasting || animPlayer.IsFadingOut)
                && _animator.GetFloat(InputMagnitudeHash) < 0.1f;

            if ((_motor != null && _motor.isInAction) || fullBodySkillActive)
            {
                _animator.SetLayerWeight(_combatStateLayerIndex, 0f);
            }
            else if (_inCombatMode && !_isCombatLayerFadingOut)
            {
                _animator.SetLayerWeight(_combatStateLayerIndex, 1f);
            }
        }

        #endregion

        #region Turn On Spot

        [Tooltip("原地转身动画(踱步):开=战斗中原地大角度转向播转身动画平滑过渡(第三人称手感);\n关=根直接对齐瞄准(顶视角射击手感,根≡瞄准铁律)。默认关(07-31 用户拍板)。")]
        public bool enableTurnOnSpot = false;

        private void UpdateTurnOnSpot()
        {
            if (!enableTurnOnSpot) return;   // 顶视角:战斗中原地转向由 ApplyCombatRotation 直接对齐,不踱步
            if (_animator == null || _motor == null) return;

            float h = _inputSource.MoveAxesRaw.x;
            float v = _inputSource.MoveAxesRaw.y;
            bool hasMovementInput = Mathf.Abs(h) > 0.05f || Mathf.Abs(v) > 0.05f;

            if (hasMovementInput)
            {
                if (_isTurningOnSpot)
                    EndTurnOnSpot();
                return;
            }

            float angleDiff = Vector3.SignedAngle(transform.forward, _targetDirection, Vector3.up);
            float absAngle = Mathf.Abs(angleDiff);

            if (absAngle > turnOnSpotAngleThreshold)
            {
                if (!_isTurningOnSpot)
                    BeginTurnOnSpot();

                float turnDirection = angleDiff;
                _animator.SetFloat(TurnOnSpotDirectionHash, turnDirection);
                _motor.turnOnSpotAnim = true;
            }
            else
            {
                if (_isTurningOnSpot)
                    EndTurnOnSpot();
            }
        }

        private void BeginTurnOnSpot()
        {
            _isTurningOnSpot = true;
            if (_motor != null)
                _motor.turnOnSpotAnim = true;
        }

        private void EndTurnOnSpot()
        {
            if (!_isTurningOnSpot) return;
            _isTurningOnSpot = false;
            if (_motor != null)
                _motor.turnOnSpotAnim = _savedTurnOnSpotAnim;
            if (_animator != null)
                _animator.SetFloat(TurnOnSpotDirectionHash, 0f);
        }

        private void ApplyCombatRotation()
        {
            // 与物理位移方向严格同源:优先用 Motor.CombatFacing(FixedUpdate 里同步的攻击朝向),
            // 回退 _targetDirection。保证「根朝向 ≡ 位移方向」,消除根(渲染帧率采样)与
            // 位移(物理步采样)的时刻错位——快速甩鼠标时身体不再朝一个方向、人却滑向另一方向。
            Vector3 facing = _targetDirection;
            if (_motor != null && _motor.CombatFacing.sqrMagnitude > 0.01f)
                facing = _motor.CombatFacing;
            if (facing.sqrMagnitude < 0.01f) return;

            // 有移动输入时直接对齐瞄准方向;无输入时:
            //   战斗模式(enableTurnOnSpot 关)=根≡瞄准直接对齐,不踱步(07-31 铁律);
            //   仅当 enableTurnOnSpot 开且已在转身中才走 TurnOnSpot 平滑转身。
            float h = _inputSource.MoveAxesRaw.x;
            float v = _inputSource.MoveAxesRaw.y;
            bool hasMovementInput = Mathf.Abs(h) > 0.05f || Mathf.Abs(v) > 0.05f;
            if (!hasMovementInput && !_isTurningOnSpot)
            {
                transform.rotation = Quaternion.LookRotation(facing);
                return;
            }

            if (_isTurningOnSpot)
            {
                Quaternion targetRot = Quaternion.LookRotation(facing);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, targetRot,
                    turnOnSpotRotationSpeed * Time.deltaTime);
            }
            else
            {
                // 快速面向攻击方向:移动中根瞬切对准鼠标/攻击方向,不做慢速弧线。
                // 移动输入是攻击朝向系(W跟枪口),根瞬切后 InputVertical/InputHorizontal
                // 立即反映「追击(W,与攻击同向)/边退边打(S,与攻击反向)」,下半身融合快速切换。
                transform.rotation = Quaternion.LookRotation(facing);
            }
        }

        #endregion

        #region Attack Phase Detection

        private bool IsInBackSwing()
        {
            if (animPlayer == null || !animPlayer.IsCasting) return false;
            if (animPlayer.CurrentData == null) return false;

            float h = _inputSource.MoveAxesRaw.x;
            float v = _inputSource.MoveAxesRaw.y;
            bool hasMovementInput = Mathf.Abs(h) > 0.05f || Mathf.Abs(v) > 0.05f;
            if (hasMovementInput) return false;

            return animPlayer.AnimTimer > animPlayer.FrontSwingSeconds;
        }

        #endregion

        #region Facing / Target

        private void UpdateTargetDirection()
        {
            var cam = _mainCamera != null ? _mainCamera : Camera.main;
            if (cam == null) return;

            // 攻击朝向用「硬贴摄像机位置」计算,不继承 TopdownCamera 的位置平滑(_followPosition 的 Lerp)。
            // 原因:摄像机位置平滑滞后于角色,角色移动时 ScreenPointToRay 起点漂移,鼠标地面点跟着漂,
            // 攻击朝向(进而移动方向/根朝向/下半身八向)全部漂移——「越操作越滞后、方向错乱」。
            // 硬贴公式只依赖摄像机旋转(拖拽时才有平滑,属镜头手感)与鼠标,与位置平滑无关。
            var tpc = cam.GetComponent<TopdownCameraController>();
            if (tpc != null)
            {
                Vector3 aim = tpc.GetAimDirection(transform.position, _inputSource.MousePosition);
                if (aim != Vector3.zero)
                {
                    _targetDirection = aim;
                    return;
                }
            }

            // 兜底(无 TopdownCameraController 时):保持原地面射线逻辑。
            Ray ray = cam.ScreenPointToRay(_inputSource.MousePosition);
            Vector3 targetPoint;

            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
                targetPoint = hit.point;
            else
            {
                Plane groundPlane = new Plane(Vector3.up, transform.position);
                if (groundPlane.Raycast(ray, out float distance))
                    targetPoint = ray.GetPoint(distance);
                else
                    return;
            }

            Vector3 dir = targetPoint - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
                _targetDirection = dir.normalized;
        }

        private void FaceMouseDirection()
        {
            UpdateTargetDirection();
            if (_targetDirection.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.LookRotation(_targetDirection);
                // 起手当帧同步位移方向基准:攻击/技能起手前若 CombatFacing 残留旧值(如非战斗期间),
                // 进入 Strafe 的第一帧物理步会用旧方向算位移 → 位移方向跳变。这里先钉住。
                if (_motor != null)
                    _motor.CombatFacing = _targetDirection;
            }
        }

        #endregion

        #region Public Queries

        public bool InCombatMode => _inCombatMode;
        public bool IsMovingAttackActive => _inCombatMode && _movingAttackIntent;
        public Vector3 TargetDirection => _targetDirection;

        /// <summary>
        /// 攻击朝向系：普攻锁定期间返回起手朝向,否则返回当前战斗朝向。
        /// 供 CharacterInputHandler 战斗输入映射使用。
        /// </summary>
        public Vector3 AttackFacingDirection =>
            _hasLastAttackFacing && _lastAttackFacing.sqrMagnitude > 0.01f
                ? _lastAttackFacing : _targetDirection;

        public float GetCooldownRemaining(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _instances.Length || _instances[slotIndex] == null)
                return 0f;
            return _instances[slotIndex].RemainingCooldown;
        }

        public float GetCooldownNormalized(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _instances.Length || _instances[slotIndex] == null)
                return 0f;
            return _instances[slotIndex].CooldownNormalized();
        }

        public bool IsSkillReady(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _instances.Length || _instances[slotIndex] == null)
                return false;
            return _instances[slotIndex].State == SkillState.Ready;
        }

        public bool IsCasting => _activeCastSlot >= 0;
        public int ActiveCastSlot => _activeCastSlot;

        public float GetModifiedDamage(SkillData data)
        {
            if (data == null) return 0f;
            var mods = weaponHolder != null ? weaponHolder.GetCombatModifiers() : WeaponCombatModifiers.Default;
            return SkillData.GetDamageFromGraph(data) * mods.damageMultiplier;
        }

        #endregion

        #region Editor Utility

        [ContextMenu("Reset All Cooldowns")]
        public void ResetAllCooldowns()
        {
            for (int i = 0; i < _instances.Length; i++)
            {
                if (_instances[i] != null)
                    _instances[i].ForceReady();
            }
            if (animPlayer != null)
                animPlayer.ResetSkillAnimation();
            _activeCastSlot = -1;
        }

        /// <summary>
        /// 立即打断当前技能/攻击动画（跳跃、翻滚等高优先级动作调用）。
        /// 仅当技能可被动作打断时生效（前摇结束后）；移动攻击无限制随时可打断。
        /// </summary>
        public void InterruptForAction()
        {
            if (animPlayer == null) return;
            if (!animPlayer.IsCasting && !animPlayer.IsFadingOut) return;

            bool canInterrupt = animPlayer.IsMoveAttack
                             || animPlayer.CanBeInterruptedByAction()
                             || animPlayer.IsFadingOut;
            if (!canInterrupt) return;

            animPlayer.ResetSkillAnimation();
            _activeCastSlot = -1;
        }

        private static int GetLayerIndex(Animator animator, string layerName)
        {
            for (int i = 0; i < animator.layerCount; i++)
            {
                if (animator.GetLayerName(i) == layerName)
                    return i;
            }
            return -1;
        }

        #endregion
    }
}
