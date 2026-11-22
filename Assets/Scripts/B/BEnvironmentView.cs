using UnityEngine;

namespace LavaRiseB
{
    public class BEnvironmentView : MonoBehaviour
    {
        public Transform LavaBody, LavaSurface;
        public Transform[] LavaEmbers;
        public GameObject[] RelayGates;
        public SpriteRenderer[] RelayLights;
        public TextMesh[] RelayLabels;
        public Collider2D[] RelaySensors;
        public Collider2D ExitSensor;
        public SpriteRenderer ExitLight;
        public Transform[] ExplosionFragments;
        float visualTime, explosionTime=100;
        Vector2 explosionOrigin;
        static readonly Color Copper=new Color(.58f,.31f,.17f);
        static readonly Color Teal=new Color(.28f,.84f,.69f);
        static readonly Color Gold=new Color(.96f,.71f,.35f);

        public void ResetWorld(float lavaHeight)
        {
            visualTime=0; explosionTime=100;
            SetLavaHeight(lavaHeight);
            if(RelayGates!=null) foreach(var gate in RelayGates) if(gate)gate.SetActive(true);
            if(RelayLights!=null) foreach(var light in RelayLights) if(light)light.color=Teal;
            if(RelayLabels!=null) for(int i=0;i<RelayLabels.Length;i++) if(RelayLabels[i])RelayLabels[i].text=$"VALF 0{i+1}\nİN • ETKİNLEŞTİR";
            if(ExitLight) ExitLight.color=new Color(.57f,.35f,.18f);
            if(ExplosionFragments!=null) foreach(var piece in ExplosionFragments) if(piece)piece.gameObject.SetActive(false);
        }

        public void SetLavaHeight(float y)
        {
            if(LavaBody)LavaBody.position=new Vector3(0,y-50,0);
            if(LavaSurface)LavaSurface.position=new Vector3(0,y,0);
        }

        public void ActivateRelay(int relay)
        {
            if(RelayGates!=null && relay<RelayGates.Length && RelayGates[relay])RelayGates[relay].SetActive(false);
            if(RelayLights!=null && relay<RelayLights.Length && RelayLights[relay])RelayLights[relay].color=new Color(.89f,1,.73f);
            if(RelayLabels!=null && relay<RelayLabels.Length && RelayLabels[relay])RelayLabels[relay].text=$"VALF 0{relay+1}\nAÇIK • GERİ TIRMAN";
            bool all=true;
            foreach(var gate in RelayGates) if(gate && gate.activeSelf)all=false;
            if(all && ExitLight)ExitLight.color=Teal;
        }

        public void ShowExitComplete() { if(ExitLight)ExitLight.color=new Color(.87f,1,.7f); }
        public void PlayExplosion(Vector2 position)
        {
            explosionOrigin=position; explosionTime=0;
            if(ExplosionFragments!=null)foreach(var t in ExplosionFragments) if(t)t.gameObject.SetActive(true);
        }

        public void TickVisual(float dt,bool running)
        {
            visualTime+=dt;
            if(LavaEmbers!=null && LavaSurface)
            {
                for(int i=0;i<LavaEmbers.Length;i++)
                {
                    float phase=Mathf.Repeat(visualTime*.32f+i*.137f,1);
                    var t=LavaEmbers[i]; if(!t)continue;
                    t.position=new Vector3(-25+i*2.15f+Mathf.Sin(visualTime+i)*.15f,LavaSurface.position.y+.08f+phase*.8f,0);
                    t.localScale=Vector3.one*(.05f+(.1f*(1-phase)));
                    var r=t.GetComponent<SpriteRenderer>(); r.color=new Color(1,.65f,.16f,(1-phase)*.7f);
                }
            }
            if(explosionTime<1.6f && ExplosionFragments!=null)
            {
                explosionTime+=dt;
                for(int i=0;i<ExplosionFragments.Length;i++)
                {
                    var t=ExplosionFragments[i]; if(!t)continue;
                    float angle=(i*137.5f+20)*Mathf.Deg2Rad;
                    Vector2 velocity=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*(2.5f+i%5*.6f);
                    t.position=(Vector3)(explosionOrigin+velocity*explosionTime+Vector2.down*explosionTime*explosionTime*2.6f);
                    t.rotation=Quaternion.Euler(0,0,i*22+explosionTime*(i%2==0?260:-210));
                    var r=t.GetComponent<SpriteRenderer>(); Color c=r.color;c.a=Mathf.Clamp01(1-explosionTime/1.5f);r.color=c;
                    if(explosionTime>=1.5f)t.gameObject.SetActive(false);
                }
            }
        }

#if UNITY_EDITOR
        public static BEnvironmentView BuildWorld()
        {
            GameObject root=new GameObject("B • Kül Kulesi / Editable World");
            BEnvironmentView view=root.AddComponent<BEnvironmentView>();
            Transform back=new GameObject("01 • Shaft walls / visual only").transform;back.SetParent(root.transform);
            Quad("Deep warm shaft",back,new Vector2(0,22),new Vector2(62,76),new Color(.055f,.066f,.069f),-50);
            for(int i=0;i<11;i++)
            {
                float x=-25+i*5;
                Quad($"Vertical stone seam {i:00}",back,new Vector2(x,22),new Vector2(.08f,76),new Color(.09f,.10f,.10f),-45);
                Quad($"Wall panel {i:00}",back,new Vector2(x+2.35f,22),new Vector2(4.5f,76),new Color(.065f+i%2*.008f,.075f+i%2*.007f,.076f),-49);
            }
            for(int row=0;row<6;row++)
            {
                var wallText=WorldText($"Shaft elevation {row}",back,new Vector2(-23,row*9+2),$"{row*9:00} M",new Color(.18f,.20f,.19f),.13f);
                wallText.anchor=TextAnchor.MiddleLeft;
                wallText.GetComponent<MeshRenderer>().sortingOrder=-40;
                Quad($"Elevation mark {row}",back,new Vector2(-23.6f,row*9+2),new Vector2(.25f,.04f),new Color(.22f,.22f,.19f),-40);
            }
            Transform platforms=new GameObject("02 • Platforms / real colliders").transform;platforms.SetParent(root.transform);
            foreach(BPlatformSpec p in BCourseDefinition.Platforms) BuildPlatform(platforms,p);
            // A real low ceiling makes the short press materially different from a held jump.
            Transform ceiling=new GameObject("Low clearance / short jump exercise").transform;ceiling.SetParent(platforms);
            Quad("Metal ceiling",ceiling,new Vector2(-6.8f,36.85f),new Vector2(4.2f,.35f),new Color(.33f,.34f,.29f),1);
            var ceilingCollider=ceiling.gameObject.AddComponent<BoxCollider2D>();ceilingCollider.offset=new Vector2(-6.8f,36.85f);ceilingCollider.size=new Vector2(4.2f,.35f);
            Quad("Ceiling amber underside",ceiling,new Vector2(-6.8f,36.66f),new Vector2(4.2f,.045f),Gold,2);

            Transform fixtures=new GameObject("03 • Thermal valves, gates, exit").transform;fixtures.SetParent(root.transform);
            view.RelayGates=new GameObject[2];view.RelayLights=new SpriteRenderer[2];view.RelayLabels=new TextMesh[2];view.RelaySensors=new Collider2D[2];
            for(int r=0;r<2;r++)
            {
                BPlatformSpec pad=BCourseDefinition.Platform(r==0?9:23);
                Transform valve=new GameObject($"Thermal valve 0{r+1} / walk into sensor").transform;valve.SetParent(fixtures);
                Quad("Valve mounting",valve,new Vector2(pad.CenterX,pad.TopY+.1f),new Vector2(1.7f,.16f),Copper,4);
                Quad("Valve stem",valve,new Vector2(pad.CenterX,pad.TopY+.61f),new Vector2(.1f,1.0f),new Color(.32f,.42f,.37f),-1);
                view.RelayLights[r]=Shape("Visible activation ring",valve,new Vector2(pad.CenterX,pad.TopY+.92f),new Vector2(1.0f,1.0f),Teal,"Ring",-1);
                Shape("Valve hub",valve,new Vector2(pad.CenterX,pad.TopY+.92f),new Vector2(.19f,.19f),Gold,"Hex",0);
                view.RelayLabels[r]=WorldText("Valve instruction",valve,new Vector2(pad.CenterX,pad.TopY+2.3f),$"VALF 0{r+1}\nİN • ETKİNLEŞTİR",Teal,.065f);
                var sensor=valve.gameObject.AddComponent<BoxCollider2D>();sensor.isTrigger=true;sensor.offset=new Vector2(pad.CenterX,pad.TopY+.9f);sensor.size=new Vector2(2.0f,1.5f);view.RelaySensors[r]=sensor;

                float gateX=r==0?-7.25f:7.7f;
                float gateBottom=r==0?13.35f:33.9f;
                Transform gate=new GameObject($"Valve 0{r+1} gate / opens on lower sensor").transform;gate.SetParent(fixtures);
                Quad("Gate silhouette",gate,new Vector2(gateX,gateBottom+1.8f),new Vector2(.36f,3.6f),new Color(.36f,.25f,.14f),3);
                Quad("Lit interlock",gate,new Vector2(gateX,gateBottom+1.8f),new Vector2(.11f,3.6f),Gold,4);
                var solid=gate.gameObject.AddComponent<BoxCollider2D>();solid.offset=new Vector2(gateX,gateBottom+1.8f);solid.size=new Vector2(.36f,3.6f);
                for(int b=0;b<6;b++)Quad($"Interlock brace {b}",gate,new Vector2(gateX,gateBottom+.25f+b*.58f),new Vector2(.65f,.08f),Copper,5);
                WorldText("Gate label",gate,new Vector2(gateX,gateBottom+4.0f),$"VALF 0{r+1}",Gold,.06f);
                view.RelayGates[r]=gate.gameObject;
                int approach=r==0?8:22;BPlatformSpec launch=BCourseDefinition.Platform(approach);
                float direction=Mathf.Sign(pad.CenterX-launch.CenterX);
                var arrow=Shape("Downward route arrow",fixtures,new Vector2(launch.CenterX+direction*2.1f,launch.TopY+1.7f),new Vector2(.34f,.44f),Teal,"Triangle",5);
                arrow.transform.rotation=Quaternion.Euler(0,0,direction<0?135:-135);
            }
            BPlatformSpec final=BCourseDefinition.Platform(32);float ex=final.CenterX+.65f;
            Transform exit=new GameObject("Physical exit / both valves required").transform;exit.SetParent(fixtures);
            Quad("Left door post",exit,new Vector2(ex-1.0f,final.TopY+1.45f),new Vector2(.26f,2.9f),new Color(.39f,.42f,.31f),0);
            Quad("Right door post",exit,new Vector2(ex+1.0f,final.TopY+1.45f),new Vector2(.26f,2.9f),new Color(.39f,.42f,.31f),0);
            Quad("Door arch",exit,new Vector2(ex,final.TopY+2.9f),new Vector2(2.25f,.26f),Gold,1);
            view.ExitLight=Quad("Exit indicator",exit,new Vector2(ex,final.TopY+1.4f),new Vector2(1.5f,2.55f),new Color(.57f,.35f,.18f),-2);
            WorldText("Exit signage",exit,new Vector2(ex,final.TopY+3.55f),"ÇIKIŞ\n2 VALF GEREKLİ",Teal,.09f);
            var exitSensor=exit.gameObject.AddComponent<BoxCollider2D>();exitSensor.offset=new Vector2(ex,final.TopY+1.45f);exitSensor.size=new Vector2(1.65f,2.7f);exitSensor.isTrigger=true;view.ExitSensor=exitSensor;

            Transform hazard=new GameObject("04 • Lava / fixed speed driven by game").transform;hazard.SetParent(root.transform);
            view.LavaBody=Quad("Molten body",hazard,new Vector2(0,BCourseDefinition.LavaStartY-50),new Vector2(62,100),new Color(.78f,.16f,.035f),20).transform;
            view.LavaSurface=Quad("Incandescent contact line",hazard,new Vector2(0,BCourseDefinition.LavaStartY),new Vector2(62,.14f),new Color(1,.68f,.14f),22).transform;
            var glow=Quad("Surface glow",view.LavaSurface,Vector2.zero,new Vector2(62,.75f),new Color(1,.29f,.03f,.22f),21);glow.transform.localPosition=Vector3.zero;
            view.LavaEmbers=new Transform[24];for(int i=0;i<24;i++)view.LavaEmbers[i]=Shape($"Lava ember {i:00}",hazard,new Vector2(0,-20),Vector2.one*.1f,Gold,"Circle",23).transform;
            Transform effects=new GameObject("05 • Death explosion pool").transform;effects.SetParent(root.transform);
            view.ExplosionFragments=new Transform[20];for(int i=0;i<20;i++)
            {
                var fragment=Shape($"Explosion spark {i:00}",effects,Vector2.zero,new Vector2(.07f+i%3*.035f,.17f),i%2==0?Gold:new Color(1,.3f,.09f),"Square",45);
                view.ExplosionFragments[i]=fragment.transform;fragment.gameObject.SetActive(false);
            }
            view.ResetWorld(BCourseDefinition.LavaStartY);
            return view;
        }

        static void BuildPlatform(Transform parent,BPlatformSpec p)
        {
            Transform root=new GameObject($"P{p.Id:00} • {p.Label} / {p.TopY:0.0}m").transform;root.SetParent(parent);
            root.position=new Vector3(p.CenterX,p.TopY-.28f,0);
            bool metal=p.Kind!=BPlatformKind.Stone;
            Color body=metal?new Color(.30f,.33f,.30f):new Color(.39f,.35f,.28f);
            Quad("Solid load-bearing body",root,new Vector2(p.CenterX,p.TopY-.28f),new Vector2(p.Width,.56f),body,0);
            Quad("Warm top / playable surface",root,new Vector2(p.CenterX,p.TopY-.045f),new Vector2(p.Width,.09f),p.Kind==BPlatformKind.Relay?Teal:Gold,2);
            Quad("Underside bevel",root,new Vector2(p.CenterX,p.TopY-.52f),new Vector2(p.Width-.15f,.09f),new Color(.20f,.22f,.20f),1);
            int bolts=Mathf.Max(2,Mathf.RoundToInt(p.Width/1.3f));
            for(int i=0;i<bolts;i++)
            {
                float x=p.Left+.3f+(p.Width-.6f)*i/(bolts-1);
                Shape($"Metal rivet {i}",root,new Vector2(x,p.TopY-.28f),Vector2.one*.075f,metal?Gold:new Color(.23f,.24f,.21f),"Circle",3);
            }
            var collider=root.gameObject.AddComponent<BoxCollider2D>();collider.size=new Vector2(p.Width,.56f);collider.usedByEffector=true;
            // Full top collision; one-way undersides keep vertically stacked routes from snagging heads.
            var effector=root.gameObject.AddComponent<PlatformEffector2D>();effector.useOneWay=true;effector.surfaceArc=160;effector.useSideFriction=false;effector.useSideBounce=false;
            TextMesh label=WorldText("Platform route label",root,new Vector2(p.CenterX,p.TopY-.85f),$"{p.Id:00}  /  {p.Label}",p.Kind==BPlatformKind.Relay?Teal:new Color(.55f,.57f,.50f),.052f);
            label.GetComponent<MeshRenderer>().sortingOrder=-1;
        }

        static SpriteRenderer Quad(string name,Transform parent,Vector2 position,Vector2 size,Color color,int order) => Shape(name,parent,position,size,color,"Square",order);
        static SpriteRenderer Shape(string name,Transform parent,Vector2 position,Vector2 size,Color color,string shape,int order)
        {
            var go=new GameObject(name);go.transform.SetParent(parent);go.transform.position=position;
            Vector3 parentScale=parent?parent.lossyScale:Vector3.one;
            go.transform.localScale=new Vector3(size.x/parentScale.x,size.y/parentScale.y,1);
            var r=go.AddComponent<SpriteRenderer>();r.sprite=BCharacterView.Shape(shape);r.sharedMaterial=BCharacterView.PresentationMaterial();r.color=color;r.sortingOrder=order;return r;
        }
        static TextMesh WorldText(string name,Transform parent,Vector2 position,string text,Color color,float size)
        {
            var go=new GameObject(name);go.transform.SetParent(parent);go.transform.position=new Vector3(position.x,position.y,0);
            var label=go.AddComponent<TextMesh>();label.text=text;label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.fontSize=64;label.characterSize=size;label.color=color;label.anchor=TextAnchor.MiddleCenter;label.alignment=TextAlignment.Center;
            var renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterial=label.font.material;renderer.sortingOrder=4;return label;
        }
#endif
    }
}
