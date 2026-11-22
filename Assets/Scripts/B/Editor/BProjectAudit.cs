#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LavaRiseB
{
    public static class BProjectAudit
    {
        public static void Run()
        {
            Scene scene=SceneManager.GetActiveScene();int scripts=0,references=0,sprites=0,colliders=0;
            foreach(var root in scene.GetRootGameObjects())
            {
                foreach(var node in root.GetComponentsInChildren<Transform>(true))scripts+=GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(node.gameObject);
                foreach(var component in root.GetComponentsInChildren<Component>(true))
                {
                    if(component==null)continue;
                    var serialized=new SerializedObject(component);var property=serialized.GetIterator();
                    while(property.Next(true))if(property.propertyType==SerializedPropertyType.ObjectReference && property.objectReferenceValue==null && property.objectReferenceEntityIdValue!=default)references++;
                }
                foreach(var renderer in root.GetComponentsInChildren<SpriteRenderer>(true)){sprites++;if(!renderer.sprite || !renderer.sharedMaterial)references++;}
                colliders+=root.GetComponentsInChildren<Collider2D>(true).Length;
            }
            var build=EditorBuildSettings.scenes;
            bool both=build.Any(s=>s.enabled&&s.path=="Assets/Scenes/A_TekTus.unity")&&build.Any(s=>s.enabled&&s.path=="Assets/Scenes/B_HareketHissi.unity");
            string report="Scene="+scene.path+"\nMissing scripts="+scripts+"\nMissing references="+references+"\nSprites="+sprites+"\nColliders="+colliders+"\nA and B in Build Settings="+both;
            Directory.CreateDirectory("Belge");File.WriteAllText("Belge/B_Sahne_Kontrolu.txt",report);
            if(scripts!=0||references!=0||!both)throw new InvalidOperationException(report);
            if(!EditorApplication.isPlaying && scene.path=="Assets/Scenes/B_HareketHissi.unity")EditorSceneManager.SaveScene(scene);
            Debug.Log(report);
        }
    }
}
#endif
