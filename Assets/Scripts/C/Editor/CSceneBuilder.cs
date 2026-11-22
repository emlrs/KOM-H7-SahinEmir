using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CollisionC
{
    /// <summary>Explicit editor authoring. Does not construct the scene at runtime.</summary>
    public static class CSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/C_Carpisma.unity";

        [MenuItem("C / Rebuild Carpisma Scene")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new System.InvalidOperationException("Build C only in Edit Mode.");
            Directory.CreateDirectory("Assets/Scenes");
            Directory.CreateDirectory("Assets/Art/C");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CEnvironmentView environment = CEnvironmentView.BuildWorld();
            CPlayerMotor player = CPlayerMotor.BuildPlayer(new Vector2(-5.8f, -3.24f));
            var cameraObject = new GameObject("C Camera • fixed full arena"); cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0, .7f, -10);
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 6.1f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.055f, .060f, .062f);
            camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
            cameraObject.AddComponent<AudioListener>();
            var gameRoot = new GameObject("C Game • 45 s survival / receiver / exit");
            var game = gameRoot.AddComponent<CGameController>(); game.CountdownDuration = 45;
            game.Configure(player, environment.Orb, environment, environment.Receiver, environment.Exit, environment.Plates);
            player.Game = game;
            environment.Orb.Game = game; environment.Orb.Player = player;
            environment.Receiver.Game = game; environment.Exit.Game = game;
            environment.UpdateHUD(game);
            Physics2D.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Unable to save C scene.");
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int existing = scenes.FindIndex(s => s.path == ScenePath);
            if (existing < 0) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            else scenes[existing] = new EditorBuildSettingsScene(ScenePath, true);
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("C scene saved: persistent player, one orb, 4 boundaries, 6 prefab plates, 2 triggers and Canvas. Existing A/B build entries preserved.");
        }
    }
}
