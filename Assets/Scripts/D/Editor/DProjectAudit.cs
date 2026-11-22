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

namespace CombinedD
{
    public static class DProjectAudit
    {
        public static void Run()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/D_Birlesik.unity") throw new InvalidOperationException("Open D first.");
            int missingScripts = 0, missingReferences = 0, sprites = 0, orbCount = 0;
            var components = new List<Component>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
                    missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(item.gameObject);
                components.AddRange(root.GetComponentsInChildren<Component>(true));
            }
            foreach (Component component in components)
            {
                if (!component) continue;
                var property = new SerializedObject(component).GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference && !property.objectReferenceValue && property.objectReferenceEntityIdValue != default)
                        missingReferences++;
                if (component is SpriteRenderer sprite)
                { sprites++; if (!sprite.sprite || !sprite.sharedMaterial) missingReferences++; }
                if (component is DOrbMotor) orbCount++;
            }
            DGameController game = components.OfType<DGameController>().Single();
            bool references = game.Player && game.Orb && game.Attacks && game.CameraRig && game.Level && game.Lava && game.Effects && game.HUD && game.Level.DoorView && game.Level.Receiver && game.Level.Exit && game.Level.PlayerSpawn && game.Level.OrbSpawn && game.Lava.StartMarker;
            bool deliveryFlags = references && !game.ManualSimulation && !game.Player.ManualSimulation && !game.Player.UseExternalInput && !game.Orb.ManualSimulation && !game.Attacks.ManualSimulation && !game.CameraRig.ManualSimulation
                && game.Player.UseAcceleration && game.Player.RequireStoppedGuard && game.Player.GuardLocksMovement && game.Orb.DirectedParry && game.Attacks.WaveEnabled;
            var platforms = components.OfType<DPlatform>().OrderBy(p => p.Index).ToArray();
            bool route = platforms.Length > 30;
            for (int i = 0; i < platforms.Length; i++)
                route &= platforms[i].Surface && (i == platforms.Length - 1 || platforms[i].Next == platforms[i + 1]);
            bool prefabs = game.Attacks.MissilePrefab && game.Attacks.LaserPrefab && AssetDatabase.GetAssetPath(game.Attacks.MissilePrefab).EndsWith(".prefab") && AssetDatabase.GetAssetPath(game.Attacks.LaserPrefab).EndsWith(".prefab");
            var panelPrefabs = new HashSet<string>();
            foreach (DPanel panel in game.Level.Panels)
            {
                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(panel.gameObject);
                if (!source) prefabs = false;
                else panelPrefabs.Add(AssetDatabase.GetAssetPath(source));
            }
            prefabs &= panelPrefabs.Count == 2 && PrefabUtility.IsPartOfPrefabInstance(game.Player.gameObject) && PrefabUtility.IsPartOfPrefabInstance(game.Orb.gameObject);
            bool modular = game.transform.childCount == 0 && game.GetComponents<MonoBehaviour>().Length == 1
                && game.gameObject != game.Attacks.gameObject && game.gameObject != game.Level.gameObject
                && game.Attacks.ProjectileRoot && game.CameraRig.Lava == game.Lava && game.HUD.Level == game.Level && game.Orb.Effects == game.Effects;
            bool triggers = game.Level.Receiver.Zone && game.Level.Receiver.Zone.isTrigger && game.Level.Receiver.Zone.enabled && game.Level.Exit.Zone && game.Level.Exit.Zone.isTrigger;
            bool physics = !game.Player.ShieldCollider.isTrigger && game.Player.ShieldCollider.gameObject.layer == 10 && game.Player.gameObject.layer == 11 && game.Orb.gameObject.layer == 14;
            bool build = EditorBuildSettings.scenes.Length == 1 && EditorBuildSettings.scenes[0].enabled && EditorBuildSettings.scenes[0].path == scene.path;
            string report = "Scene=" + scene.path + "\nMissing scripts=" + missingScripts + "\nMissing references=" + missingReferences + "\nPersistent sprites=" + sprites
                + "\nOrb count=" + orbCount + "\nPlatform count=" + platforms.Length + "\nLinked route=" + route + "\nCanvas count=" + components.OfType<Canvas>().Count()
                + "\nAuthored attack points=" + components.OfType<DAttackPoint>().Count() + "\nDistinct panel prefabs=" + panelPrefabs.Count
                + "\nSeparate systems/direct bindings=" + modular + "\nReferences=" + references + "\nDelivery flags=" + deliveryFlags + "\nReal prefabs=" + prefabs + "\nTriggers ready=" + triggers + "\nLayers/solid shield=" + physics + "\nOnly D build entry=" + build;
            Directory.CreateDirectory("Belge"); File.WriteAllText("Belge/D_Sahne_Denetimi.txt", report);
            if (missingScripts != 0 || missingReferences != 0 || orbCount != 1 || !references || !deliveryFlags || !route || !prefabs || !triggers || !physics || !build || !modular)
                throw new InvalidOperationException(report);
            if (!EditorApplication.isPlaying) EditorSceneManager.SaveScene(scene);
            Debug.Log(report);
        }
    }
}
#endif

