using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace LavaRiseB
{
    public static class BSceneBuilder
    {
        public const string ScenePath="Assets/Scenes/B_HareketHissi.unity";
        [MenuItem("B / Rebuild Hareket Hissi Scene")]
        public static void Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Build B only in Edit Mode.");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            BEnvironmentView environment=BEnvironmentView.BuildWorld();
            BPlayerMotor player=BPlayerMotor.BuildPlayer(BCourseDefinition.StartPosition);
            GameObject cameraObject=new GameObject("B Camera • reversible smooth follow");
            cameraObject.tag="MainCamera";
            Camera camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=6.8f;camera.backgroundColor=new Color(.055f,.066f,.069f);camera.clearFlags=CameraClearFlags.SolidColor;camera.nearClipPlane=.1f;camera.farClipPlane=100;
            var additional=cameraObject.AddComponent<UniversalAdditionalCameraData>();additional.renderPostProcessing=false;
            cameraObject.AddComponent<AudioListener>();
            BCameraFollow follow=cameraObject.AddComponent<BCameraFollow>();
            GameObject gameRoot=new GameObject("B Game • instructions, run, death, exit");
            BGameController game=gameRoot.AddComponent<BGameController>();game.Configure(player,environment,follow);
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene,ScenePath)) throw new System.IO.IOException("Unable to save B scene.");
            var scenes=new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int existing=scenes.FindIndex(s=>s.path==ScenePath);
            if(existing<0)scenes.Add(new EditorBuildSettingsScene(ScenePath,true));
            else scenes[existing]=new EditorBuildSettingsScene(ScenePath,true);
            EditorBuildSettings.scenes=scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log($"B scene built: {ScenePath}; {BCourseDefinition.Platforms.Length} physical platforms; {BCourseDefinition.Route.Length} intended transitions; A build entry preserved.");
        }
    }
}
