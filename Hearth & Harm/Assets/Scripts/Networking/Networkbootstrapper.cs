using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Pre-place this on a NetworkObject in your multiplayer scene.
/// On OnNetworkSpawn it sets GameManager.Mode for every peer so all
/// networked bridges (health, enemy, player, turn) activate correctly.
///
/// SETUP:
///   1. Create a GameObject in your MultiplayerScene called "NetworkBootstrapper"
///   2. Add NetworkObject + this component
///   3. Do NOT add it to NetworkManager's prefab list — it lives in the scene
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class NetworkBootstrapper : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if (IsHost)
        {
            GameManager.SetMode(GameMode.Host);
            Debug.Log("[NetworkBootstrapper] Mode → Host");
        }
        else
        {
            // IsClient is true for both host and pure clients in NGO.
            // We already handled IsHost above, so this branch is pure clients only.
            GameManager.SetMode(GameMode.Client);
            Debug.Log("[NetworkBootstrapper] Mode → Client");
        }
    }
}