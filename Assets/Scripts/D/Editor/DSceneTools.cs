#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace CombinedD
{
    // Open the authored scene. This tool never regenerates or overwrites its content.
    public static class DSceneTools
    {
        public const string ScenePath = "Assets/Scenes/D_Birlesik.unity";

        [MenuItem("D/Open existing D scene")]
        public static void OpenExistingScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open D only in Edit Mode.");
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
                throw new InvalidOperationException("The authored D scene is missing: " + ScenePath);
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }
    }
}
#endif
