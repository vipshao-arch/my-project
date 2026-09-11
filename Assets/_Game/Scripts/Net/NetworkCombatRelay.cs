using Unity.Netcode;
using UnityEngine;
using Game.Character;
using Game.SkillSystem;

namespace Game.Net
{
    /// <summary>
    /// 命中权威路由(S2-4,2026-07-30):Host 唯一伤害落地通道。
    ///
    /// 各端命中(近战挥砍/投射物/链式/AOE 最终都落到 IDamageable.TakeDamage)先经
    /// <see cref="TryRouteDamage"/> 分流:
    ///   单机或 Host → 本地直接落地(零改动);
    ///   Client → ServerRpc 上 Host,Host 复核后落地:
    ///     存活校验 + 攻击方存在 + 距离粗检(≤30m) + 掩体 LoS 复核
    ///     (复用 SkillNodeBehaviorUtil.IsBlockedByObstacle,与格挡矩阵同一真源)。
    ///   HP 经 CharacterStats/NetworkEnemySetup 的 NetworkVariable 回同步各端。
    ///
    /// 生命周期:Host 在 StartHost 后由 NetworkGameManager 生成并 Spawn,各端自动复制。
    /// </summary>
    public class NetworkCombatRelay : NetworkBehaviour
    {
        public static NetworkCombatRelay Instance { get; private set; }

        public override void OnNetworkSpawn() => Instance = this;
        public override void OnNetworkDespawn() { if (Instance == this) Instance = null; }

        /// <summary>
        /// 伤害分流入口(在所有 IDamageable.TakeDamage 实现的首行调用)。
        /// 返回 true = 已路由到 Host,调用方直接 return;false = 调用方继续本地落地。
        /// </summary>
        public static bool TryRouteDamage(GameObject target, float damage, GameObject source, Vector3 hitDir)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening || nm.IsServer) return false;  // 单机/Host:本地落地
            if (Instance == null) return false;

            var no = target.GetComponentInParent<NetworkObject>();
            if (no == null || !no.IsSpawned) return false;

            ulong srcId = ulong.MaxValue;
            if (source != null)
            {
                var sNo = source.GetComponentInParent<NetworkObject>();
                if (sNo != null && sNo.IsSpawned) srcId = sNo.NetworkObjectId;
            }
            Instance.RequestDamageServerRpc(no.NetworkObjectId, damage, srcId, hitDir);
            return true;
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestDamageServerRpc(ulong targetNetId, float damage, ulong sourceNetId, Vector3 hitDir)
        {
            var spawned = NetworkManager.Singleton.SpawnManager.SpawnedObjects;
            if (!spawned.TryGetValue(targetNetId, out var targetNo)) return;

            var damageable = targetNo.GetComponent<IDamageable>();
            if (damageable == null || damageable.isDead) return;

            GameObject source = null;
            if (sourceNetId != ulong.MaxValue &&
                spawned.TryGetValue(sourceNetId, out var sNo))
                source = sNo.gameObject;

            if (!ValidateHit(source, targetNo)) return;

            damageable.TakeDamage(damage, source, hitDir);   // Host 本地落地 → NetworkVariable 回同步

            // 对战(3.2):玩家被击杀则上报记分+重生
            if (targetNo.IsPlayerObject && damageable.isDead && NetworkMatchManager.Instance != null)
                NetworkMatchManager.Instance.ReportKill(source, targetNo.gameObject);
        }

        /// <summary>Host 复核:友伤门禁(合作模式) + 距离粗检 + 掩体 LoS 复核(与格挡矩阵同一判定真源)。</summary>
        private static bool ValidateHit(GameObject source, NetworkObject targetNo)
        {
            if (source == null) return true;   // 无来源(陷阱/环境):放行

            // 友伤门禁(3.1/阵营版):同队禁伤;异队/FFA 放行(无管理器=合作=全同盟)
            bool srcPlayer = source.GetComponent<CharacterMotor>() != null;
            bool tgtPlayer = targetNo.GetComponent<CharacterMotor>() != null;
            if (srcPlayer && tgtPlayer)
            {
                var srcNo = source.GetComponent<NetworkObject>();
                ulong srcId = srcNo != null ? srcNo.OwnerClientId : targetNo.OwnerClientId;
                if (NetworkMatchManager.IsAlly(srcId, targetNo.OwnerClientId))
                    return false;
            }

            var col = targetNo.GetComponentInChildren<Collider>();
            if (col == null) return true;

            float dist = Vector3.Distance(source.transform.position, targetNo.transform.position);
            if (dist > 30f)
            {
                Debug.LogWarning($"[Relay] 复核拒绝:距离 {dist:F1}m > 30m src={source.name} tgt={targetNo.name}");
                return false;
            }

            Vector3 eye = source.transform.position + Vector3.up * 1.2f;
            if (SkillNodeBehaviorUtil.IsBlockedByObstacle(eye, col, 1, source))
            {
                if (debugRelay)
                    Debug.Log($"[Relay] 复核拒绝:掩体阻挡 src={source.name} tgt={targetNo.name}");
                return false;
            }
            return true;
        }

        [Tooltip("复核诊断日志")]
        public static bool debugRelay = false;
    }
}
