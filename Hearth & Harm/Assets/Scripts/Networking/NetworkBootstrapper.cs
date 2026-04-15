// using System;
// using System.Threading.Tasks;
// using Unity.Netcode;
// using Unity.Netcode.Transports.UTP;
// using Unity.Networking.Transport.Relay;
// using Unity.Services.Authentication;
// using Unity.Services.Core;
// using Unity.Services.Relay;
// using Unity.Services.Relay.Models;
// using UnityEngine;


// public class NetworkBootstrapper : MonoBehaviour
// {
//     public static NetworkBootstrapper Instance { get; private set; }

//     [Header("Settings")]
//     [SerializeField] private int maxPlayers = 4;
//     [SerializeField] private string relayRegion = "";

//     public event Action<string> OnJoinCodeReady;
//     public event Action         OnConnected;
//     public event Action<string> OnConnectionFailed;

//     public bool   IsInitialized   { get; private set; }
//     public string CurrentJoinCode { get; private set; }

//     private void Awake()
//     {
//         if (Instance != null) { Destroy(gameObject); return; }
//         Instance = this;
//         DontDestroyOnLoad(gameObject);
//     }

//     public async Task InitializeAsync()
//     {
//         if (IsInitialized) return;
//         try
//         {
//             await UnityServices.InitializeAsync();
//             if (!AuthenticationService.Instance.IsSignedIn)
//                 await AuthenticationService.Instance.SignInAnonymouslyAsync();
//             IsInitialized = true;
//             Debug.Log($"[NetworkBootstrapper] Signed in. PlayerId={AuthenticationService.Instance.PlayerId}");
//         }
//         catch (Exception e)
//         {
//             Debug.LogError($"[NetworkBootstrapper] Init failed: {e.Message}");
//             OnConnectionFailed?.Invoke($"Init failed: {e.Message}");
//         }
//     }

//     public async Task HostGame()
//     {
//         if (!IsInitialized) await InitializeAsync();
//         try
//         {
//             Allocation allocation = string.IsNullOrEmpty(relayRegion)
//                 ? await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1)
//                 : await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1, relayRegion);

//             CurrentJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
//             Debug.Log($"[NetworkBootstrapper] Join code: {CurrentJoinCode}");

//             var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
//             transport.SetRelayServerData(BuildRelayServerData(allocation));

//             if (!NetworkManager.Singleton.StartHost())
//             {
//                 OnConnectionFailed?.Invoke("NGO StartHost() returned false.");
//                 return;
//             }

//             GameManager.SetMode(GameMode.Host);
//             OnJoinCodeReady?.Invoke(CurrentJoinCode);
//             OnConnected?.Invoke();
//             Debug.Log("[NetworkBootstrapper] Hosting started.");
//         }
//         catch (Exception e)
//         {
//             Debug.LogError($"[NetworkBootstrapper] HostGame failed: {e.Message}");
//             OnConnectionFailed?.Invoke($"Host failed: {e.Message}");
//         }
//     }

//     public async Task JoinGame(string joinCode)
//     {
//         if (!IsInitialized) await InitializeAsync();
//         try
//         {
//             joinCode = joinCode.Trim().ToUpper();
//             JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

//             var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
//             transport.SetRelayServerData(BuildRelayServerData(allocation));

//             if (!NetworkManager.Singleton.StartClient())
//             {
//                 OnConnectionFailed?.Invoke("NGO StartClient() returned false.");
//                 return;
//             }

//             GameManager.SetMode(GameMode.Client);
//             OnConnected?.Invoke();
//             Debug.Log($"[NetworkBootstrapper] Joined with code: {joinCode}");
//         }
//         catch (Exception e)
//         {
//             Debug.LogError($"[NetworkBootstrapper] JoinGame failed: {e.Message}");
//             OnConnectionFailed?.Invoke($"Join failed: {e.Message}");
//         }
//     }

//     public void Disconnect()
//     {
//         if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
//             NetworkManager.Singleton.Shutdown();
//         GameManager.SetMode(GameMode.Offline);
//         CurrentJoinCode = null;
//         Debug.Log("[NetworkBootstrapper] Disconnected.");
//     }



//     private static RelayServerData BuildRelayServerData(Allocation allocation)
//     {
//         return new RelayServerData(
//             allocation.RelayServer.IpV4,
//             (ushort)allocation.RelayServer.Port,
//             allocation.AllocationIdBytes,
//             allocation.ConnectionData,
//             allocation.ConnectionData,  // host: both connection data fields are the same
//             allocation.Key,
//             useDtls: true
//         );
//     }

//     private static RelayServerData BuildRelayServerData(JoinAllocation allocation)
//     {
//         return new RelayServerData(
//             allocation.RelayServer.IpV4,
//             (ushort)allocation.RelayServer.Port,
//             allocation.AllocationIdBytes,
//             allocation.ConnectionData,
//             allocation.HostConnectionData,  // client: needs the HOST's connection data
//             allocation.Key,
//             useDtls: true
//         );
//     }
// }