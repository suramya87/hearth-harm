// using UnityEngine;

// /// <summary>
// /// Place this component on a trigger collider inside your End room prefab.
// /// When the player walks into it, WinScreen.Show() is called.
// ///
// /// Setup:
// ///   1. Add a child GameObject to your End room prefab.
// ///   2. Give it a Collider2D (or Collider) set to "Is Trigger".
// ///   3. Attach this script.
// ///   4. Make sure the player GameObject is tagged "Player".
// /// </summary>
// public class EndRoomTrigger : MonoBehaviour
// {
//     [Tooltip("Only fire once per level load.")]
//     private bool _triggered;

//     private void OnEnable()
//     {
//         // Reset when the level regenerates
//         LevelGenerator.OnLevelReady += ResetTrigger;
//     }

//     private void OnDisable()
//     {
//         LevelGenerator.OnLevelReady -= ResetTrigger;
//     }

//     private void ResetTrigger() => _triggered = false;

//     // 2D physics
//     private void OnTriggerEnter2D(Collider2D other)
//     {
//         if (!_triggered && other.CompareTag("Player"))
//             Trigger();
//     }

//     // 3D physics fallback
//     private void OnTriggerEnter(Collider other)
//     {
//         if (!_triggered && other.CompareTag("Player"))
//             Trigger();
//     }

//     private void Trigger()
//     {
//         if (GameStateManager.Instance?.CurrentState != GameStateManager.State.Playing) return;
//         _triggered = true;
//         Debug.Log("[EndRoomTrigger] Player reached the End room → Win!");
//         WinScreen.Show();
//     }
// }