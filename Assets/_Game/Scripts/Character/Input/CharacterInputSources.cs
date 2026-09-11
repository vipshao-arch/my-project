using UnityEngine;

namespace Game.Character
{
    /// <summary>
    /// 角色输入源抽象(联机接缝 S2-0,2026-07-30 建立,P0 遗留项补齐)。
    ///
    /// 原语级接口:消费方保留各自按键配置(skill1Key/jumpKey 等不动),
    /// 仅把 `Input.GetXxx` 换成 `_inputSource.GetXxx`。
    ///
    /// 实现:
    ///   本地玩家 → <see cref="LocalInputSource"/>(实时转发 Unity Input,与旧行为逐帧一致);
    ///   远端副本 → <see cref="NullInputSource"/>(全空,防远端代理被本地输入误驱动);
    ///   后续 NetworkInputSource 由网络事件喂入(联机打怪阶段)。
    ///
    /// 解析约定:消费方 Awake 中 `GetComponent&lt;ICharacterInputSource&gt;() ?? LocalInputSource.Shared`。
    /// </summary>
    public interface ICharacterInputSource
    {
        Vector2 MoveAxesRaw { get; }    // GetAxisRaw("Horizontal"/"Vertical")
        Vector2 MoveAxes { get; }       // GetAxis("Horizontal"/"Vertical") 平滑
        Vector2 MousePosition { get; }
        float MouseScroll { get; }
        bool GetKey(KeyCode key);
        bool GetKeyDown(KeyCode key);
        bool GetMouseButton(int button);
        bool GetMouseButtonDown(int button);
        bool GetMouseButtonUp(int button);
    }

    /// <summary>本地玩家输入源:实时转发 Unity Input。纯单例,无需挂组件。</summary>
    public sealed class LocalInputSource : ICharacterInputSource
    {
        public static readonly LocalInputSource Shared = new LocalInputSource();
        private LocalInputSource() { }

        public Vector2 MoveAxesRaw => new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        public Vector2 MoveAxes => new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
        public Vector2 MousePosition => Input.mousePosition;
        public float MouseScroll => Input.GetAxis("Mouse ScrollWheel");
        public bool GetKey(KeyCode key) => Input.GetKey(key);
        public bool GetKeyDown(KeyCode key) => Input.GetKeyDown(key);
        public bool GetMouseButton(int button) => Input.GetMouseButton(button);
        public bool GetMouseButtonDown(int button) => Input.GetMouseButtonDown(button);
        public bool GetMouseButtonUp(int button) => Input.GetMouseButtonUp(button);
    }

    /// <summary>输入源解析(全部消费方统一入口)。</summary>
    public static class CharacterInputSourceResolver
    {
        /// <summary>
        /// 优先取物体上的输入源组件(远端=NullInputSource),无则回退本地源。
        /// 注意:不用 GetComponent&lt;ICharacterInputSource&gt;()——物体含 Missing Script 时
        /// Unity 的接口查询会抛 NRE(已知引擎问题),安全遍历规避。
        /// </summary>
        public static ICharacterInputSource Resolve(Component owner)
        {
            var comps = owner.GetComponents<MonoBehaviour>();
            foreach (var c in comps)
                if (c is ICharacterInputSource s)
                    return s;
            return LocalInputSource.Shared;
        }
    }

    /// <summary>
    /// 空输入源(挂组件):远端网络副本专用。联机装配时挂到远端玩家上,
    /// 各消费方 GetComponent 优先命中它,从而读不到本地输入。
    /// </summary>
    public sealed class NullInputSource : MonoBehaviour, ICharacterInputSource
    {
        public Vector2 MoveAxesRaw => Vector2.zero;
        public Vector2 MoveAxes => Vector2.zero;
        public Vector2 MousePosition => Vector2.zero;
        public float MouseScroll => 0f;
        public bool GetKey(KeyCode key) => false;
        public bool GetKeyDown(KeyCode key) => false;
        public bool GetMouseButton(int button) => false;
        public bool GetMouseButtonDown(int button) => false;
        public bool GetMouseButtonUp(int button) => false;
    }
}
