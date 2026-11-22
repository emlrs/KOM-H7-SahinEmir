#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ASceneBuilder
{
    public const string ScenePath = "Assets/Scenes/A_TekTus.unity";

    [MenuItem("Tek Tus/Sahneyi yeniden kur")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Exit Play Mode first.");
        Directory.CreateDirectory("Assets/Scenes");
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 5.4f;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.025f, .047f, .085f);
        camera.nearClipPlane = .1f;
        camera.farClipPlane = 100f;
        cameraObject.AddComponent<AudioListener>();

        var manager = new GameObject("Oyun A - Yonetim ve Ayarlar");
        var game = manager.AddComponent<DefenseGame>();
        manager.AddComponent<DefenseInput>().Game = game;
        var visualRoot = new GameObject("Test Alani - Gorseller");
        var view = ArenaView.Build(visualRoot.transform);
        view.Bind(game);

        PlayerSettings.defaultScreenWidth = 1920;
        PlayerSettings.defaultScreenHeight = 1080;
        PlayerSettings.runInBackground = true;
        Application.targetFrameRate = 120;
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Scene could not be saved.");
        EditorBuildSettings.scenes = new[] {new EditorBuildSettingsScene(ScenePath, true)};
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeGameObject = manager;
        Directory.CreateDirectory("Belge");
        File.WriteAllText("Belge/A_Gercek_Ayarlar.json", JsonUtility.ToJson(game.Settings, true));
        int missing = 0;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
        if (missing > 0) throw new System.InvalidOperationException("Missing scripts: " + missing);
        Debug.Log("A_TekTus scene saved; missing scripts=0; build scenes=1.");
    }

    [MenuItem("Tek Tus/Sahne referanslarini dogrula")]
    public static void Audit()
    {
        Scene scene = SceneManager.GetActiveScene();
        int missingScripts = 0, missingReferences = 0, spriteCount = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var node in root.GetComponentsInChildren<Transform>(true))
                missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(node.gameObject);
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                var serialized = new SerializedObject(component);
                var property = serialized.GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == null && property.objectReferenceEntityIdValue != default)
                        missingReferences++;
            }
            foreach (var sprite in root.GetComponentsInChildren<SpriteRenderer>(true))
            {
                spriteCount++;
                if (sprite.sprite == null || sprite.sharedMaterial == null) missingReferences++;
            }
        }
        bool buildValid = EditorBuildSettings.scenes.Length == 1 && EditorBuildSettings.scenes[0].path == ScenePath && EditorBuildSettings.scenes[0].enabled;
        string report = "Scene=" + scene.path + "\nMissing scripts=" + missingScripts + "\nMissing references=" + missingReferences + "\nSpriteRenderer count=" + spriteCount + "\nOnly A build scene=" + buildValid;
        File.WriteAllText("Belge/A_Sahne_Kontrolu.txt", report);
        if (missingScripts != 0 || missingReferences != 0 || !buildValid) throw new System.InvalidOperationException(report);
        if (!EditorApplication.isPlaying) EditorSceneManager.SaveScene(scene);
        Debug.Log(report);
    }
}
#endif
