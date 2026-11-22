#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CombinedD
{
    // Repair existing serialized objects only. Never calls the scene builder.
    public static class DFixTools
    {
        static DGameController Game()
        {
            if (SceneManager.GetActiveScene().path != "Assets/Scenes/D_Birlesik.unity") throw new InvalidOperationException("Existing D scene required.");
            return UnityEngine.Object.FindAnyObjectByType<DGameController>();
        }
        public static void Inspect()
        {
            var game = Game(); var text = new StringBuilder();
            text.AppendLine("Unity=" + Application.unityVersion + " Assets=" + Application.dataPath);
            foreach (var component in new UnityEngine.Object[] { game, game.Player, game.Orb, game.Attacks, game.Level, game.CameraRig, game.Lava, game.Effects, game.HUD })
            {
                text.AppendLine("\n[" + component.GetType().Name + "]");
                var serialized = new SerializedObject(component); var property = serialized.GetIterator();
                while (property.NextVisible(true))
                    if (property.propertyType == SerializedPropertyType.Float || property.propertyType == SerializedPropertyType.Boolean || property.propertyType == SerializedPropertyType.Integer)
                        text.AppendLine(property.propertyPath + "=" + (property.propertyType == SerializedPropertyType.Float ? property.floatValue.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) : property.propertyType == SerializedPropertyType.Boolean ? property.boolValue.ToString() : property.intValue.ToString()));
            }
            Directory.CreateDirectory("Belge/D_Duzeltme");
            File.WriteAllText("Belge/D_Duzeltme/inspector-values.txt", text.ToString());
        }
        public static void SnapshotLayout()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode required.");
            Game(); var text = new StringBuilder();
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                    text.AppendLine(GlobalObjectId.GetGlobalObjectIdSlow(tr) + "|" + tr.name + "|" + tr.localPosition.ToString("F6") + "|" + tr.localRotation.ToString("F6") + "|" + tr.localScale.ToString("F6"));
            Directory.CreateDirectory("Temp/D_Birlesik");
            File.WriteAllText("Temp/D_Birlesik/layout-snapshot.txt", text.ToString());
        }
        public static void ApplyFirstSettings()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode required.");
            var game = Game();
            Set(game.Lava, "RiseSpeed", 1.25f);
            Set(game.Attacks, "InitialMissileSpeed", 9f); Set(game.Attacks, "MaximumMissileSpeed", 14f);
            Set(game.Attacks, "InitialInterval", 2f); Set(game.Attacks, "MinimumInterval", 1f);
            Set(game.Attacks, "DifficultyDuration", 120f);
            Set(game.Attacks, "MinimumLaserWarning", 1.3f); Set(game.Attacks, "MaximumLaserWarning", 1.7f);
            Set(game.Attacks, "MinimumLaserDuration", 1f); Set(game.Attacks, "MaximumLaserDuration", 1.8f);
            Set(game.Attacks, "MinimumMissileReaction", .75f);
            Set(game.Attacks, "MinimumLaserStartGap", 8.5f);
            var director = new SerializedObject(game.Attacks);
            director.FindProperty("MaximumMissiles").intValue = 3; director.ApplyModifiedPropertiesWithoutUndo();
            Set(game.Attacks.MissilePrefab, "TelegraphSeconds", .30f);
            Set(game.CameraRig, "BaseOrthographicSize", 9.5f);
            Set(game.CameraRig, "CameraSmoothTime", .20f); Set(game.CameraRig, "CameraDeadZone", .9f);
            Set(game.CameraRig, "CameraCatchUpSpeed", 24f); Set(game.CameraRig, "FallLookAhead", 3.5f);
            Set(game.CameraRig, "ZoomInSpeed", 2.5f); Set(game.CameraRig, "PlayerViewPadding", .75f);
            Set(game.CameraRig, "CameraSideMargin", 1.888889f); Set(game.CameraRig, "BoundaryClearance", .12f);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene())) throw new InvalidOperationException("Could not save repaired scene.");
            AssetDatabase.SaveAssets(); Inspect();
        }
        public static void RepairShieldFoot()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode required.");
            var player = Game().Player;
            ExtendShield(player);
            string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(player.gameObject);
            if (!string.IsNullOrEmpty(prefabPath))
            {
                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                try { ExtendShield(root.GetComponentInChildren<DPlayerMotor>(true)); PrefabUtility.SaveAsPrefabAsset(root, prefabPath); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
        }
        static void ExtendShield(DPlayerMotor player)
        {
            if (!player || !(player.ShieldCollider is PolygonCollider2D shield)) throw new InvalidOperationException("Existing shield required.");
            var points = shield.points;
            if (points.Length != 14) throw new InvalidOperationException("Unexpected authored shield shape.");
            points[6] = new Vector2(.35f, -.78f); points[7] = new Vector2(.24f, -.70f);
            shield.points = points; EditorUtility.SetDirty(shield);
            foreach (var sprite in shield.GetComponentsInChildren<SpriteRenderer>(true))
                if (sprite.name == "Shield arc 5")
                {
                    Vector2 from = new Vector2(.58f, -.28f), to = new Vector2(.295f, -.74f), delta = to-from;
                    sprite.transform.localPosition = (from+to)*.5f;
                    sprite.transform.localScale = new Vector3(delta.magnitude+.035f, .12f, 1);
                    sprite.transform.localRotation = Quaternion.Euler(0,0,Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg);
                    EditorUtility.SetDirty(sprite.transform);
                }
        }
        static void Set(UnityEngine.Object target, string field, float value)
        {
            var serialized = new SerializedObject(target); var property = serialized.FindProperty(field);
            if (property == null) throw new InvalidOperationException("Missing field: " + field);
            property.floatValue = value; serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(target);
        }
    }
}
#endif
