using Unity.Netcode.Components;
using UnityEngine;

namespace Game.Net
{
    /// <summary>
    /// 客户端权威 NetworkTransform(S2-2,移动客户端权威架构决策):
    /// 每个玩家的位置/旋转由自己的客户端写入并广播,Host 不校正他人移动。
    /// 敌人另用服务器权威 NetworkTransform(S2-3)。
    /// </summary>
    [DisallowMultipleComponent]
    public class ClientNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative() => false;
    }
}
