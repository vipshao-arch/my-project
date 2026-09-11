using UnityEngine;

namespace Game.Character
{
    /// <summary>
    /// 伤害飘字系统。监听场景中所有 IDamageable 的 OnHealthChanged，
    /// 在受击位置生成伤害数字 TextMesh，向上浮动并淡出。
    ///
    /// 用法：挂在场景中任意物体上即可（推荐挂在 GameManager 或 EventSystem 下）。
    /// </summary>
    [AddComponentMenu("Game/Character System/Damage Number Spawner")]
    public class DamageNumberSpawner : MonoBehaviour
    {
        [Header("Prefab Settings")]
        [Tooltip("伤害数字 prefab（需含 TextMesh 或 TextMeshPro）。留空则用内置文字渲染。")]
        public GameObject damageNumberPrefab;

        [Tooltip("伤害数字字体大小")]
        public float fontSize = 0.5f;

        [Header("Animation")]
        public float floatUpSpeed = 2f;
        public float lifetime = 1f;
        public float popupOffset = 1.5f;

        [Header("Colors")]
        public Color normalColor = new Color(1f, 0.85f, 0.2f);
        public Color critColor = new Color(1f, 0.2f, 0.2f);
        public Color healColor = new Color(0.3f, 1f, 0.3f);

        [Header("Font")]
        [Tooltip("伤害数字字体。留空则使用 Unity 内置默认字体。")]
        public Font customFont;

        private Camera _mainCamera;
        private Font _fallbackFont;

        void Awake()
        {
            _mainCamera = Camera.main;
            _fallbackFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        void Start()
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
        }

        /// <summary>
        /// 在世界空间指定位置显示伤害数字。
        /// 由外部脚本（如 HitDetector 或 IDamageable 实现）调用。
        /// </summary>
        public void ShowDamage(Vector3 worldPos, float damage, bool isHeal = false, bool isCrit = false)
        {
            if (_mainCamera == null) _mainCamera = Camera.main;

            if (damageNumberPrefab != null)
            {
                SpawnPrefabNumber(worldPos, damage, isHeal, isCrit);
            }
            else
            {
                SpawnBuiltinNumber(worldPos, damage, isHeal, isCrit);
            }
        }

        private Font GetFont() => customFont != null ? customFont : _fallbackFont;

        private void SpawnPrefabNumber(Vector3 worldPos, float damage, bool isHeal, bool isCrit)
        {
            Vector3 spawnPos = worldPos + Vector3.up * popupOffset;
            GameObject go = Instantiate(damageNumberPrefab, spawnPos, Quaternion.identity);

            // 设置文字
            var tm = go.GetComponent<TextMesh>();
            if (tm != null)
            {
                tm.font = GetFont();
                tm.text = Mathf.RoundToInt(damage).ToString();
                tm.color = isHeal ? healColor : (isCrit ? critColor : normalColor);
                tm.fontSize = Mathf.RoundToInt(fontSize * 64);
            }

            // 添加浮动+淡出组件
            var fader = go.AddComponent<DamageNumberFader>();
            fader.Init(floatUpSpeed, lifetime, isHeal ? healColor : (isCrit ? critColor : normalColor));
        }

        private void SpawnBuiltinNumber(Vector3 worldPos, float damage, bool isHeal, bool isCrit)
        {
            Vector3 spawnPos = worldPos + Vector3.up * popupOffset;
            GameObject go = new GameObject("DamageNumber");

            var tm = go.AddComponent<TextMesh>();
            tm.font = GetFont();
            tm.text = Mathf.RoundToInt(damage).ToString();
            tm.fontSize = Mathf.RoundToInt(fontSize * 32);
            tm.characterSize = 0.15f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = isHeal ? healColor : (isCrit ? critColor : normalColor);

            // 面向摄像机
            if (_mainCamera != null)
                go.transform.LookAt(_mainCamera.transform);

            go.transform.position = spawnPos;

            var fader = go.AddComponent<DamageNumberFader>();
            fader.Init(floatUpSpeed, lifetime, tm.color);

            Destroy(go, lifetime + 0.1f);
        }
    }

    /// <summary>
    /// 伤害数字浮动+淡出组件。运行时动态添加到伤害数字 GameObject 上。
    /// </summary>
    public class DamageNumberFader : MonoBehaviour
    {
        private float _speed;
        private float _lifetime;
        private float _timer;
        private Color _color;
        private TextMesh _textMesh;
        private Camera _mainCamera;

        public void Init(float speed, float lifetime, Color color)
        {
            _speed = speed;
            _lifetime = lifetime;
            _color = color;
            _textMesh = GetComponent<TextMesh>();
            _mainCamera = Camera.main;
        }

        void Update()
        {
            _timer += Time.deltaTime;

            // 向上浮动
            transform.position += Vector3.up * _speed * Time.deltaTime;

            // 面向摄像机
            if (_mainCamera != null)
                transform.rotation = Quaternion.LookRotation(_mainCamera.transform.forward);

            // 淡出
            if (_textMesh != null)
            {
                float alpha = 1f - Mathf.Clamp01(_timer / _lifetime);
                _textMesh.color = new Color(_color.r, _color.g, _color.b, alpha);
            }
        }
    }
}
