using Unity.Netcode;
using UnityEngine;
using Game.SkillSystem;

namespace Game.Net
{
    /// <summary>
    /// 施法表现广播(S2-5 v1,2026-07-30):挂玩家 prefab。
    ///
    /// 本地施法成功后(SkillController 钩子)把"动画状态名+层"广播给其他端;
    /// 远端在自身 Animator 上 CrossFade 同名状态,实现攻击/技能表现同步。
    ///
    /// v1 范围 = 纯动画表现:
    ///   远端 SkillAnimPlayer 处于禁用态,Update 不跑——不执行技能图、不开 HitDetector、
    ///   不生成投射物,天然无重复伤害;命中权威仍在 NetworkCombatRelay(S2-4)。
    ///   远端弹道/技能 VFX 事件广播列为后续增量(S2-5b)。
    /// </summary>
    public class NetworkCastRelay : NetworkBehaviour
    {
        private NetworkCharacterSetup _setup;

        void Awake() => _setup = GetComponent<NetworkCharacterSetup>();

        /// <summary>本地普攻施法成功后调用(纯动画复播);单机未 Spawn / 非 Owner 直接忽略。</summary>
        public void BroadcastLocal(string stateName, int layerIndex)
        {
            if (!IsSpawned || !IsOwner) return;
            if (string.IsNullOrEmpty(stateName) || layerIndex < 0) return;
            CastServerRpc(stateName, layerIndex);
        }

        /// <summary>本地技能施法成功后调用(S2-5b:远端 visualOnly 图复播)。</summary>
        public void BroadcastSkillCast(string skillName)
        {
            if (!IsSpawned || !IsOwner) return;
            if (string.IsNullOrEmpty(skillName)) return;
            SkillCastServerRpc(skillName);
        }

        [ServerRpc]
        private void CastServerRpc(string stateName, int layerIndex)
            => CastClientRpc(stateName, layerIndex);

        [ClientRpc]
        private void CastClientRpc(string stateName, int layerIndex)
        {
            if (IsOwner) return;   // 发起端已本地播放
            if (_setup != null) _setup.PlayCastVisual(stateName, layerIndex);
        }

        [ServerRpc]
        private void SkillCastServerRpc(string skillName)
            => SkillCastClientRpc(skillName);

        [ClientRpc]
        private void SkillCastClientRpc(string skillName)
        {
            if (IsOwner) return;   // 发起端已本地完整施法
            var data = SkillCatalog.Find(skillName);
            if (data == null)
            {
                Debug.LogWarning($"[CastRelay] 技能目录未命中:{skillName}(需重跑 Rebuild Skill Catalog?)");
                return;
            }
            var animPlayer = GetComponent<SkillAnimPlayer>();
            if (animPlayer != null)
                animPlayer.PlayRemoteCast(data);
        }
    }
}
