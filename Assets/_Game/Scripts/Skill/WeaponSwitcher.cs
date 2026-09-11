using UnityEngine;
using UnityEngine.Events;
using System.Collections;
using System.Collections.Generic;
using Game.Character;

namespace Game.SkillSystem
{
    /// <summary>
    /// 运行时换武器系统 — 挂在角色根节点。
    ///
    /// 功能：
    ///   - 定义一个武器列表（WeaponData 数组）
    ///   - 按键（默认 Tab 或鼠标滚轮）轮切武器
    ///   - 支持换武器动画（可选）
    ///   - 换武器期间锁定技能输入
    ///   - 对外提供 OnWeaponChanged 事件
    ///   - 与 WeaponHolder / SkillController / CharacterInputHandler 协作
    ///
    /// 依赖：WeaponHolder（必须）、SkillController（可选）
    /// </summary>
    [AddComponentMenu("Game/Skill System/Weapon Switcher")]
    [RequireComponent(typeof(WeaponHolder))]
    public class WeaponSwitcher : MonoBehaviour
    {
        // ═══════════════════════════════════════════════════════════════
        // Inspector 配置
        // ═══════════════════════════════════════════════════════════════

        [Header("武器列表")]
        [Tooltip("可装备的武器列表（顺序即切换顺序）")]
        public WeaponData[] weaponList = new WeaponData[0];

        [Header("初始武器")]
        [Tooltip("游戏开始时装备的武器索引（-1 = 不自动装备）")]
        public int startWeaponIndex = 0;

        [Header("输入配置")]
        [Tooltip("切换到下一把武器的按键（默认 Alpha1-Alpha4 已被技能占用，建议用 F 键）")]
        public KeyCode nextWeaponKey = KeyCode.F;
        [Tooltip("切换到上一把武器的按键")]
        public KeyCode prevWeaponKey = KeyCode.G;
        [Tooltip("是否允许鼠标滚轮切换")]
        public bool useScrollWheel = false; // 默认关闭，避免与摄像机缩放冲突
        [Tooltip("滚轮方向：true = 向上切下一把，false = 反向")]
        public bool scrollUpForNext = true;

        [Header("换武器动画")]
        [Tooltip("换武器时播放的 Animator 触发器名（留空则不播放）")]
        public string switchAnimTrigger = "SwitchWeapon";
        [Tooltip("换武器动画持续时间（秒）。动画播放期间锁定输入。")]
        public float switchDuration = 0.4f;

        [Header("战斗模式行为")]
        [Tooltip("战斗模式下（SkillController.InCombatMode）是否允许换武器")]
        public bool allowSwitchInCombat = false;

        [Header("事件")]
        public UnityEvent<WeaponData, int> OnWeaponChanged; // 武器数据, 新索引
        public UnityEvent OnSwitchBegin;
        public UnityEvent OnSwitchEnd;

        // ═══════════════════════════════════════════════════════════════
        // 运行时状态
        // ═══════════════════════════════════════════════════════════════

        private WeaponHolder _holder;
        private SkillController _skillController;
        private Animator _animator;
        private CharacterInputHandler _inputHandler;

        private int _currentIndex = -1;
        private bool _isSwitching = false;

        // ═══════════════════════════════════════════════════════════════
        // 公开查询
        // ═══════════════════════════════════════════════════════════════

        /// <summary>当前装备的武器索引</summary>
        public int CurrentIndex => _currentIndex;

        /// <summary>当前装备的武器数据（null = 未装备）</summary>
        public WeaponData CurrentWeapon =>
            _currentIndex >= 0 && _currentIndex < weaponList.Length
                ? weaponList[_currentIndex]
                : null;

        /// <summary>是否正在换武器动画中</summary>
        public bool IsSwitching => _isSwitching;

        // ═══════════════════════════════════════════════════════════════
        // 生命周期
        // ═══════════════════════════════════════════════════════════════

        void Awake()
        {
            _holder = GetComponent<WeaponHolder>();
            _skillController = GetComponent<SkillController>();
            _animator = GetComponent<Animator>();
            _inputHandler = GetComponent<CharacterInputHandler>();
            _inputSource  = Game.Character.CharacterInputSourceResolver.Resolve(this);
        }

        private Game.Character.ICharacterInputSource _inputSource;   // 输入源(S2-0 收口)

        void Start()
        {
            if (weaponList.Length > 0 && startWeaponIndex >= 0)
            {
                int idx = Mathf.Clamp(startWeaponIndex, 0, weaponList.Length - 1);
                EquipByIndex(idx, skipAnimation: true);
            }
        }

        void Update()
        {
            if (_isSwitching) return;
            HandleInput();
        }

        // ═══════════════════════════════════════════════════════════════
        // 输入处理
        // ═══════════════════════════════════════════════════════════════

        private void HandleInput()
        {
            if (weaponList.Length <= 1) return;

            int delta = 0;

            if (_inputSource != null && _inputSource.GetKeyDown(nextWeaponKey)) delta = 1;
            else if (_inputSource != null && _inputSource.GetKeyDown(prevWeaponKey)) delta = -1;
            else if (useScrollWheel && _inputSource != null)
            {
                float scroll = _inputSource.MouseScroll;
                if (scroll > 0.01f) delta = scrollUpForNext ? 1 : -1;
                else if (scroll < -0.01f) delta = scrollUpForNext ? -1 : 1;
            }

            if (delta != 0)
                TrySwitchWeapon(delta);
        }

        // ═══════════════════════════════════════════════════════════════
        // 公开 API
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 尝试切换到下一把 / 上一把武器（delta=+1 / -1）
        /// </summary>
        public void TrySwitchWeapon(int delta)
        {
            if (_isSwitching) return;
            if (weaponList.Length == 0) return;

            // 战斗模式限制
            if (!allowSwitchInCombat && _skillController != null && _skillController.InCombatMode)
            {
                Debug.Log("[WeaponSwitcher] 战斗模式中，不允许换武器");
                return;
            }

            int nextIndex = (_currentIndex + delta + weaponList.Length) % weaponList.Length;
            if (nextIndex == _currentIndex) return;

            StartCoroutine(SwitchCoroutine(nextIndex));
        }

        /// <summary>
        /// 直接切换到指定索引的武器
        /// </summary>
        public void SwitchToIndex(int index)
        {
            if (_isSwitching) return;
            if (index < 0 || index >= weaponList.Length) return;
            if (index == _currentIndex) return;

            StartCoroutine(SwitchCoroutine(index));
        }

        /// <summary>
        /// 立即装备（无动画，用于初始化或强制重置）
        /// </summary>
        public void EquipByIndex(int index, bool skipAnimation = false)
        {
            if (index < 0 || index >= weaponList.Length) return;

            // 卸下当前武器
            if (_currentIndex >= 0)
                _holder.Unequip(WeaponSlotType.MainHand);

            _currentIndex = index;
            var weapon = weaponList[index];

            if (weapon != null)
                _holder.Equip(weapon, WeaponSlotType.MainHand, startInHolster: false);

            OnWeaponChanged?.Invoke(weapon, index);
        }

        // ═══════════════════════════════════════════════════════════════
        // 换武器协程
        // ═══════════════════════════════════════════════════════════════

        private IEnumerator SwitchCoroutine(int nextIndex)
        {
            _isSwitching = true;
            OnSwitchBegin?.Invoke();

            // 锁定输入
            if (_inputHandler != null)
                _inputHandler.lockInput = true;

            // 播放换武器动画
            bool hasAnim = _animator != null && !string.IsNullOrEmpty(switchAnimTrigger);
            if (hasAnim)
                _animator.SetTrigger(switchAnimTrigger);

            // 等待换武器时间的前半段 → 卸下旧武器
            float halfDur = switchDuration * 0.5f;
            yield return new WaitForSeconds(halfDur);

            // 卸下旧武器
            if (_currentIndex >= 0)
                _holder.Unequip(WeaponSlotType.MainHand);

            // 装备新武器
            _currentIndex = nextIndex;
            var weapon = weaponList[nextIndex];
            if (weapon != null)
                _holder.Equip(weapon, WeaponSlotType.MainHand, startInHolster: false);

            OnWeaponChanged?.Invoke(weapon, nextIndex);

            // 等待剩余时间
            yield return new WaitForSeconds(halfDur);

            // 解锁输入
            if (_inputHandler != null)
                _inputHandler.lockInput = false;

            OnSwitchEnd?.Invoke();
            _isSwitching = false;
        }

        // ═══════════════════════════════════════════════════════════════
        // Editor 辅助
        // ═══════════════════════════════════════════════════════════════

        [ContextMenu("Switch To Next Weapon")]
        private void DebugNextWeapon()
        {
            TrySwitchWeapon(1);
        }

        [ContextMenu("Switch To Prev Weapon")]
        private void DebugPrevWeapon()
        {
            TrySwitchWeapon(-1);
        }
    }
}
