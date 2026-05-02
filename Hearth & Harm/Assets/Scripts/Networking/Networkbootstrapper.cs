using Unity.Netcode;
using UnityEngine;

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

            GameManager.SetMode(GameMode.Client);
            Debug.Log("[NetworkBootstrapper] Mode → Client");
        }
    }
}