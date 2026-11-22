using UnityEngine;

namespace CombinedD
{
    // Authored level references stay together; the game controller owns the run state.
    public sealed class DLevelManager : MonoBehaviour
    {
        public DPlatform[] Platforms;
        public DPanel[] Panels;
        public Transform PlayerSpawn, OrbSpawn;
        public Vector2 InitialOrbDirection = new Vector2(-.85f, -.52f);
        public DReceiver Receiver;
        public DExitTrigger Exit;
        public DDoorView DoorView;

        public Vector2 GetPlayerSpawn(Vector2 fallback) => PlayerSpawn ? (Vector2)PlayerSpawn.position : fallback;
        public Vector2 GetOrbSpawn(Vector2 fallback) => OrbSpawn ? (Vector2)OrbSpawn.position : fallback;

        public void ResetPanels()
        {
            if (Panels != null) foreach (var panel in Panels) if (panel) panel.ResetPanel();
        }

        public void SetDoorOpen(bool open)
        {
            if (DoorView) DoorView.SetDoorOpen(open);
        }
    }
}
