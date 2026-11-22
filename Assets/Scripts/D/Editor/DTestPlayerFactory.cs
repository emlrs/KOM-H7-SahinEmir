#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace CombinedD
{
    // Test the same authored player that is used by the saved scene.
    public static class DTestPlayerFactory
    {
        public static DPlayerMotor Create(Vector2 position)
        {
            const string path = "Assets/Prefabs/D/D_Player.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab || !prefab.GetComponent<DPlayerMotor>())
                throw new InvalidOperationException("The authored D player prefab is required: " + path);
            DPlayerMotor player = UnityEngine.Object.Instantiate(prefab).GetComponent<DPlayerMotor>();
            player.ResetAt(position);
            return player;
        }
    }
}
#endif
