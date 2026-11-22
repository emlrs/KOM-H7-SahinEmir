using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif

namespace CollisionC
{
    /// <summary>Saved, editable sprite pieces; animation never moves the capsule or shield collider.</summary>
    [DisallowMultipleComponent]
    public sealed class CCharacterView : MonoBehaviour
    {
        [Serializable] private sealed class Piece
        {
            public SpriteRenderer Renderer;
            public Vector3 Position, Scale;
            public float Rotation;
            public Color Color;
        }
        [SerializeField] private CPlayerMotor motor;
        [SerializeField] private Transform pose, head, torso, leftLeg, rightLeg, leftBoot, rightBoot, leftArm, rightArm, scarf;
        [SerializeField] private SpriteRenderer shadow, shieldFill, flash;
        [SerializeField] private SpriteRenderer[] shieldRim;
        [SerializeField] private Piece[] pieces;
        private bool subscribed, wasAlive = true;
        private float deathAge, gait, idleAge, flashAge = 10f;
        public string AnimationState { get; private set; } = "Idle";
        public bool ShieldVisible => shieldFill && shieldFill.enabled;

        public static CCharacterView Build(Transform parent, Transform shield)
        {
            var root = new GameObject("Character artwork • editable pieces").transform;
            root.SetParent(parent, false);
            var view = root.gameObject.AddComponent<CCharacterView>();
            view.BuildArtwork(shield);
            return view;
        }
        public void Bind(CPlayerMotor target)
        { Unsubscribe(); motor = target; if (Application.isPlaying) Subscribe(); }
        private void Awake() { if (!motor) motor = GetComponentInParent<CPlayerMotor>(); Subscribe(); }
        private void OnEnable() => Subscribe();
        private void OnDisable() => Unsubscribe();
        private void Subscribe()
        {
            if (subscribed || !motor) return;
            motor.Died += OnDeath; subscribed = true;
        }
        private void Unsubscribe()
        { if (motor && subscribed) motor.Died -= OnDeath; subscribed = false; }
        private void OnDeath() { deathAge = 0f; wasAlive = false; HideShield(); }

        private void BuildArtwork(Transform shield)
        {
            pose = new GameObject("Pose • idle run parry death").transform;
            pose.SetParent(transform, false);
            var parts = new List<Piece>();
            Color dark = new Color(.045f, .09f, .14f), suit = new Color(.19f, .39f, .52f);
            Color light = new Color(.76f, .88f, .91f), orange = new Color(1f, .49f, .25f);
            shadow = Part("Ground shadow", new Vector2(0, -.72f), new Vector2(.85f, .10f), new Color(.01f,.03f,.06f,.4f), 35, "Circle", parts);
            leftArm = Part("Rear arm", new Vector2(-.27f,-.055f), new Vector2(.15f,.41f), dark,41,"Hex",parts,-12).transform;
            scarf = Part("Scarf tail",new Vector2(-.27f,.24f),new Vector2(.43f,.14f),orange,42,"Triangle",parts,173).transform;
            leftLeg = Part("Rear shin",new Vector2(-.13f,-.41f),new Vector2(.19f,.38f),dark,43,"Hex",parts).transform;
            leftBoot = Part("Rear boot",new Vector2(-.12f,-.67f),new Vector2(.26f,.13f),dark,44,"Hex",parts).transform;
            rightLeg = Part("Front shin",new Vector2(.13f,-.41f),new Vector2(.20f,.38f),suit,45,"Hex",parts).transform;
            rightBoot = Part("Front boot",new Vector2(.16f,-.67f),new Vector2(.28f,.13f),light,46,"Hex",parts).transform;
            Part("Backpack",new Vector2(-.23f,.02f),new Vector2(.21f,.41f),dark,45,"Hex",parts);
            torso = Part("Jacket silhouette",new Vector2(0,-.035f),new Vector2(.48f,.54f),dark,47,"Hex",parts).transform;
            Part("Jacket panel",new Vector2(.015f,-.01f),new Vector2(.36f,.43f),suit,48,"Hex",parts);
            Part("Waist belt",new Vector2(0,-.245f),new Vector2(.41f,.075f),light,49,"Square",parts);
            Part("Scarf collar",new Vector2(0,.23f),new Vector2(.43f,.115f),orange,50,"Hex",parts);
            head = Part("Helmet silhouette",new Vector2(0,.455f),new Vector2(.55f,.46f),dark,51,"Hex",parts).transform;
            Part("Helmet shell",new Vector2(.005f,.47f),new Vector2(.47f,.37f),light,52,"Hex",parts);
            Part("Dark visor • facing indicator",new Vector2(.075f,.465f),new Vector2(.36f,.18f),dark,53,"Hex",parts);
            Part("Visor reflection",new Vector2(.11f,.50f),new Vector2(.22f,.032f),new Color(.47f,.91f,.99f),54,"Square",parts);
            Part("Helmet side disc",new Vector2(-.19f,.455f),new Vector2(.13f,.13f),suit,55,"Circle",parts);
            Part("Helmet stripe",new Vector2(-.055f,.625f),new Vector2(.10f,.045f),new Color(.44f,.86f,1f),56,"Square",parts);
            rightArm = Part("Front forearm",new Vector2(.275f,-.045f),new Vector2(.17f,.40f),light,56,"Hex",parts,12).transform;
            Part("Front glove",new Vector2(.30f,-.24f),new Vector2(.16f,.14f),dark,57,"Circle",parts);
            Part("Shield emitter • always visible",new Vector2(.32f,-.11f),new Vector2(.14f,.27f),new Color(.45f,.80f,1f),58,"Hex",parts,-12);
            Part("Chest light",new Vector2(.09f,.055f),new Vector2(.105f,.105f),new Color(.28f,.82f,1f),58,"Circle",parts);
            pieces = parts.ToArray();

            // The sprite uses the exact same polygon as the physical collider, including its crown.
            shieldFill = Sprite(shield,"Light blue shield • exact collider silhouette",new Vector2(.31f,.155f),new Vector2(.78f,1.51f),new Color(.35f,.78f,1f,.72f),65,"Shield");
            Vector2[] points = CPlayerMotor.ShieldPoints();
            shieldRim = new SpriteRenderer[6];
            for(int i=0;i<shieldRim.Length;i++)
            {
                Vector2 a=points[i], b=points[i+1], delta=b-a;
                shieldRim[i]=Sprite(shield,"Shield bright edge "+(i+1),(a+b)*.5f,new Vector2(delta.magnitude+.018f,.034f),new Color(.78f,.95f,1f),66,"Square");
                shieldRim[i].transform.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg);
            }
            flash=Sprite(transform,"Parry contact ring • persistent effect",Vector2.zero,Vector2.one,new Color(.71f,.94f,1f),75,"Ring");
            flash.enabled=false;HideShield();
        }
        private SpriteRenderer Part(string name,Vector2 at,Vector2 size,Color color,int order,string shape,List<Piece> parts,float angle=0f)
        {
            SpriteRenderer renderer=Sprite(pose,name,at,size,color,order,shape);
            renderer.transform.localRotation=Quaternion.Euler(0,0,angle);
            parts.Add(new Piece{Renderer=renderer,Position=renderer.transform.localPosition,Scale=renderer.transform.localScale,Rotation=angle,Color=color});
            return renderer;
        }
        private static SpriteRenderer Sprite(Transform parent,string name,Vector2 at,Vector2 size,Color color,int order,string shape)
        {
            var t=new GameObject(name).transform;t.SetParent(parent,false);
            t.localPosition=at;t.localScale=new Vector3(size.x,size.y,1);
            var renderer=t.gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite=Shape(shape);renderer.sharedMaterial=PresentationMaterial();
            renderer.color=color;renderer.sortingOrder=order;return renderer;
        }
        public void ResetVisual()
        {
            wasAlive=true;deathAge=0;gait=0;idleAge=0;flashAge=10;
            RestorePieces();HideShield();if(flash)flash.enabled=false;
            AnimationState="Idle";
        }
        public void FlashParry(Vector2 contact)
        {
            if(!flash)return;
            flashAge=0;flash.transform.position=contact;flash.enabled=true;
        }
        private void LateUpdate() => TickVisual(Time.deltaTime);
        public void TickVisual(float dt)
        {
            if(!motor || !pose || pieces==null)return;
            flashAge+=dt;
            if(flash)
            {
                flash.enabled=flashAge<.28f;
                flash.transform.localScale=Vector3.one*Mathf.Lerp(.25f,.95f,Mathf.Clamp01(flashAge/.28f));
                flash.color=new Color(.73f,.96f,1f,1f-Mathf.Clamp01(flashAge/.28f));
            }
            if(!motor.Alive)
            {
                if(wasAlive)OnDeath();
                deathAge+=dt;AnimationState="Death fragments";AnimateDeath();HideShield();return;
            }
            if(!wasAlive)ResetVisual();
            RestorePieces();
            float speed=Mathf.Abs(motor.Velocity.x),running=Mathf.Clamp01(speed/4f);
            gait+=dt*speed*2.1f;idleAge+=dt;
            pose.localScale=new Vector3(motor.Facing,1f,1f);
            pose.localPosition=new Vector3(0,Mathf.Sin(gait*2f)*.015f*running,0);
            if(motor.ParryActive)
            {
                AnimationState="Parry";
                Rotate(rightArm,-62);Rotate(leftArm,16);Rotate(rightLeg,-8);Rotate(leftLeg,8);
                pose.localPosition+=new Vector3(-motor.Facing*.025f,-.018f,0);
            }
            else if(running>.03f)
            {
                AnimationState="Run";
                float swing=Mathf.Sin(gait)*26f*running;
                Rotate(leftLeg,swing);Rotate(rightLeg,-swing);Rotate(leftBoot,swing*.5f);Rotate(rightBoot,-swing*.5f);
                Rotate(leftArm,-swing);Rotate(rightArm,swing);
                Rotate(scarf,176+Mathf.Sin(gait*1.4f)*7);
            }
            else
            {
                AnimationState="Idle";
                float breath=Mathf.Sin(idleAge*2.6f)*.009f;
                head.localPosition+=Vector3.up*breath;torso.localScale+=Vector3.up*breath;
                Rotate(leftArm,-12+breath*80);Rotate(rightArm,12-breath*80);
            }
            SetShield(motor.ParryActive);
        }
        private void SetShield(bool visible)
        {
            if(shieldFill)shieldFill.enabled=visible;
            if(shieldRim!=null)foreach(var rim in shieldRim)if(rim)rim.enabled=visible;
        }
        private void HideShield()=>SetShield(false);
        private static void Rotate(Transform t,float angle){if(t)t.localRotation=Quaternion.Euler(0,0,angle);}
        private void RestorePieces()
        {
            if(pieces==null)return;
            foreach(Piece piece in pieces)
            {
                if(!piece.Renderer)continue;
                Transform t=piece.Renderer.transform;
                t.localPosition=piece.Position;t.localScale=piece.Scale;t.localRotation=Quaternion.Euler(0,0,piece.Rotation);
                piece.Renderer.color=piece.Color;
            }
            if(pose){pose.localScale=Vector3.one;pose.localPosition=Vector3.zero;pose.localRotation=Quaternion.identity;}
        }
        private void AnimateDeath()
        {
            float age=Mathf.Min(deathAge,1.35f);
            for(int i=0;i<pieces.Length;i++)
            {
                Piece piece=pieces[i];if(!piece.Renderer)continue;
                if(piece.Renderer==shadow){piece.Renderer.color=Color.clear;continue;}
                float angle=i*2.399963f;
                Vector3 launch=new Vector3(Mathf.Cos(angle)*(1.2f+i%3*.36f),1.8f+Mathf.Sin(angle)*.8f,0);
                Transform t=piece.Renderer.transform;
                t.localPosition=piece.Position+launch*age+Vector3.down*(2.1f*age*age);
                t.localRotation=Quaternion.Euler(0,0,piece.Rotation+age*(i%2==0?180f:-225f));
                t.localScale=piece.Scale*Mathf.Lerp(1f,.08f,Mathf.Clamp01(age/1.35f));
                Color burnt=Color.Lerp(piece.Color,new Color(.12f,.05f,.03f),Mathf.Clamp01(age*2f));
                burnt.a=Mathf.Clamp01(1.35f-age)/1.35f;piece.Renderer.color=burnt;
            }
        }

        /// <summary>Editor construction persists C-only shape sprites; saved objects hold all asset references.</summary>
        public static Sprite Shape(string name)
        {
#if UNITY_EDITOR
            const string folder="Assets/Art/C";Directory.CreateDirectory(folder);
            string path=folder+"/"+name+".png";
            Sprite existing=AssetDatabase.LoadAssetAtPath<Sprite>(path);if(existing)return existing;
#endif
            const int size=128;
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false){name="C "+name};
            var pixels=new Color[size*size];
            Vector2[] shield=name=="Shield"?CPlayerMotor.ShieldPoints():null;
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float px=(x+.5f)/size*2-1,py=(y+.5f)/size*2-1;
                float radius=Mathf.Sqrt(px*px+py*py),d;
                switch(name)
                {
                    case "Circle":d=radius-.96f;break;
                    case "Ring":d=Mathf.Abs(radius-.89f)-.047f;break;
                    case "Hex":d=Mathf.Max(Mathf.Abs(py)-.94f,Mathf.Abs(px)+Mathf.Abs(py)*.24f-.98f);break;
                    case "Triangle":d=Mathf.Max(-px-.95f,Mathf.Abs(py)*2+px-.95f);break;
                    default:d=Mathf.Max(Mathf.Abs(px),Mathf.Abs(py))-.99f;break;
                }
                float alpha=Mathf.Clamp01(.5f-d*size*.5f);
                if(name=="Glow")alpha=Mathf.Pow(Mathf.Clamp01(1-radius),2);
                if(shield!=null)
                {
                    // Four coverage samples preserve a crisp, antialiased physical silhouette.
                    alpha=0;
                    for(int sy=0;sy<2;sy++)for(int sx=0;sx<2;sx++)
                    {
                        var p=new Vector2(-.08f+(x+(sx+.5f)*.5f)/size*.78f,-.60f+(y+(sy+.5f)*.5f)/size*1.51f);
                        if(InsidePolygon(p,shield))alpha+=.25f;
                    }
                }
                pixels[y*size+x]=new Color(1,1,1,alpha);
            }
            texture.SetPixels(pixels);texture.Apply();
#if UNITY_EDITOR
            File.WriteAllBytes(path,texture.EncodeToPNG());DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Sprite;importer.spritePixelsPerUnit=size;
            importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.filterMode=FilterMode.Bilinear;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
#else
            return UnityEngine.Sprite.Create(texture,new Rect(0,0,size,size),new Vector2(.5f,.5f),size);
#endif
        }
        private static bool InsidePolygon(Vector2 p,Vector2[] points)
        {
            bool inside=false;
            for(int i=0,j=points.Length-1;i<points.Length;j=i++)
                if((points[i].y>p.y)!=(points[j].y>p.y) && p.x<(points[j].x-points[i].x)*(p.y-points[i].y)/(points[j].y-points[i].y)+points[i].x)inside=!inside;
            return inside;
        }
        public static Material PresentationMaterial()
        {
#if UNITY_EDITOR
            const string path="Assets/Art/C/CUnlit.mat";
            Material existing=AssetDatabase.LoadAssetAtPath<Material>(path);if(existing)return existing;
            Directory.CreateDirectory("Assets/Art/C");
#endif
            Shader shader=Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if(!shader)shader=Shader.Find("Sprites/Default");
            var material=new Material(shader){name="C • unlit geometry"};
#if UNITY_EDITOR
            AssetDatabase.CreateAsset(material,path);
#endif
            return material;
        }
    }
}
