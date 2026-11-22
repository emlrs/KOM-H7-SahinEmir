#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CollisionC
{
    public static class CProjectAudit
    {
        public static void Run()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/C_Carpisma.unity") throw new InvalidOperationException("Open the saved C scene first.");
            int missingScripts = 0, missingReferences = 0, sprites = 0, colliders = 0, canvas = 0, text = 0, balls = 0;
            var plates = new List<CPlate>();
            CGameController game = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
                    missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(node.gameObject);
                foreach (Component component in root.GetComponentsInChildren<Component>(true))
                {
                    if (!component) continue;
                    var iterator = new SerializedObject(component).GetIterator();
                    while (iterator.Next(true))
                        if (iterator.propertyType == SerializedPropertyType.ObjectReference && iterator.objectReferenceValue == null && iterator.objectReferenceEntityIdValue != default) missingReferences++;
                }
                foreach (SpriteRenderer renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
                { sprites++; if (!renderer.sprite || !renderer.sharedMaterial) missingReferences++; }
                colliders += root.GetComponentsInChildren<Collider2D>(true).Length;
                canvas += root.GetComponentsInChildren<Canvas>(true).Length;
                text += root.GetComponentsInChildren<Text>(true).Length;
                balls += root.GetComponentsInChildren<COrbMotor>(true).Length;
                plates.AddRange(root.GetComponentsInChildren<CPlate>(true));
                if (!game) game = root.GetComponentInChildren<CGameController>(true);
            }
            int connected = 0;
            var prefabPaths = new HashSet<string>();
            foreach (CPlate plate in plates)
            {
                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(plate.gameObject);
                if (!source) continue;
                connected++; prefabPaths.Add(AssetDatabase.GetAssetPath(source));
            }
            bool scenes = new[] { "A_TekTus", "B_HareketHissi", "C_Carpisma" }.All(name => EditorBuildSettings.scenes.Any(s => s.enabled && s.path == "Assets/Scenes/" + name + ".unity"));
            bool references = game && game.Player && game.Orb && game.Environment && game.Receiver && game.Exit && game.Plates != null;
            bool triggers = references && game.Receiver.GetComponent<Collider2D>().isTrigger && game.Exit.GetComponent<Collider2D>().isTrigger;
            bool shield = references && game.Player.ShieldCollider && !game.Player.ShieldCollider.isTrigger;
            string report = "Scene=" + scene.path + "\nMissing scripts=" + missingScripts + "\nMissing references=" + missingReferences +
                "\nSprites=" + sprites + "\nCollider2D=" + colliders + "\nPersistent Canvas=" + canvas + "\nPersistent UI Text=" + text +
                "\nOrb count=" + balls + "\nPlate instances=" + plates.Count + "\nConnected prefab instances=" + connected +
                "\nDistinct plate prefabs=" + prefabPaths.Count + "\n" + string.Join("\n", prefabPaths) +
                "\nGame references=" + references + "\nReceiver and exit are triggers=" + triggers + "\nShield is solid=" + shield + "\nA/B/C build entries=" + scenes;
            Directory.CreateDirectory("Belge"); File.WriteAllText("Belge/C_Sahne_Kontrolu.txt", report);
            if (missingScripts != 0 || missingReferences != 0 || balls != 1 || !references || !triggers || !shield || !scenes || canvas < 1 || connected != plates.Count || prefabPaths.Count != 2)
                throw new InvalidOperationException(report);
            if (!EditorApplication.isPlaying) EditorSceneManager.SaveScene(scene);
            Debug.Log(report);
        }
    }
}
#endif
