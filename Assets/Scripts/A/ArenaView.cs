using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif

/// <summary>Editable, asset-backed laboratory artwork and presentation for the A exercise.</summary>
[DisallowMultipleComponent]
public sealed class ArenaView : MonoBehaviour
{
    [Serializable] private sealed class BodyPart
    {
        public SpriteRenderer renderer;
        public Vector3 position, scale;
        public float rotation;
        public Color color;
    }
    private sealed class MissileVisual
    {
        public Transform root;
        public SpriteRenderer flame, glow;
        public TrailRenderer trail;
    }
    private sealed class Spark
    {
        public SpriteRenderer sprite;
        public Vector2 velocity;
        public float age, life, gravity, spin;
        public Vector3 size;
        public Color color;
    }
    private sealed class Pulse
    {
        public SpriteRenderer sprite;
        public float age, life, size;
        public Color color;
    }
    private struct LabelDraw
    {
        public Rect rect;
        public string text;
        public GUIStyle style;
        public Color color;
        public int size;
        public FontStyle fontStyle;
    }

    [SerializeField] private DefenseGame game;
    [SerializeField] private Transform heroRoot, shieldRoot, missileRoot, effectsRoot;
    [SerializeField] private Transform forearm, hand, upperArm;
    [SerializeField] private BodyPart[] bodyParts;
    [SerializeField] private SpriteRenderer shieldPlate, shieldEnergy, shieldHalo, reactor, burnGlow;
    [SerializeField] private SpriteRenderer[] emitterCores, emitterRings, emitterHalos;
    [SerializeField] private Transform[] warningMarkers;
    [SerializeField] private LineRenderer[] laserCores, laserGlows, laserGuides;
    [SerializeField] private Transform[] ambientLights;
    [SerializeField] private Sprite squareSprite, circleSprite, glowSprite, ringSprite, hexSprite, triangleSprite;
    [SerializeField] private Material spriteMaterial;
    [SerializeField] private Camera arenaCamera;

    private readonly Dictionary<int, MissileVisual> missiles = new Dictionary<int, MissileVisual>();
    private readonly HashSet<int> liveMissiles = new HashSet<int>();
    private readonly List<int> removedMissiles = new List<int>();
    private readonly List<Spark> sparks = new List<Spark>();
    private readonly List<Pulse> pulses = new List<Pulse>();
    private readonly Dictionary<long, GUIStyle> styles = new Dictionary<long, GUIStyle>();
    private readonly List<LabelDraw> labelDraws = new List<LabelDraw>(48);
    private AudioSource audioSource;
    private AudioClip parryClip, guardClip, launchClip, hitClip, laserHitClip, startClip, winClip, warningClip;
    private Font uiFont;
    private float guardPose, parryKick, contactFlash, screenFlash, shake, ashClock, lastGuardSound, lastLaunchSound;
    private float resultAge;
    private int lastFiringLaser = -1;
    private Vector3 cameraRest;
    private bool bound, initialized;
    private GamePhase previousPhase;
    private static readonly Color Navy = new Color(.027f, .052f, .093f);
    private static readonly Color Cyan = new Color(.24f, .94f, 1f);
    private static readonly Color Ice = new Color(.78f, .95f, 1f);
    private static readonly Color Orange = new Color(1f, .39f, .22f);
    private static readonly Color Pink = new Color(1f, .23f, .65f);
    private static readonly Color Muted = new Color(.43f, .59f, .68f);

    public static ArenaView Build(Transform parent)
    {
        var root = new GameObject("Arena • editable laboratory artwork").transform;
        root.SetParent(parent, false);
        ArenaView view = root.gameObject.AddComponent<ArenaView>();
        view.squareSprite = Shape("Square");
        view.circleSprite = Shape("Circle");
        view.glowSprite = Shape("Glow");
        view.ringSprite = Shape("Ring");
        view.hexSprite = Shape("Hex");
        view.triangleSprite = Shape("Triangle");
        view.spriteMaterial = PresentationMaterial();
        view.BuildEnvironment();
        view.BuildHero();
        view.BuildShield();
        view.BuildEmitters();
        view.missileRoot = Group("Missiles • live flight visuals", root);
        view.effectsRoot = Group("Impact sparks and ash", root);
        view.arenaCamera = Camera.main;
        return view;
    }

    public void Bind(DefenseGame controller)
    {
        Unbind();
        game = controller;
        if (Application.isPlaying) Subscribe();
    }

    private void Awake()
    {
        if (!game) game = FindAnyObjectByType<DefenseGame>();
        if (!arenaCamera) arenaCamera = Camera.main;
        if (arenaCamera) cameraRest = arenaCamera.transform.position;
        uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        CreateAudio();
        initialized = true;
        Subscribe();
    }
    private void OnEnable() { if (initialized) Subscribe(); }
    private void OnDisable() { Unbind(); if (arenaCamera) arenaCamera.transform.position = cameraRest; }
    private void Subscribe()
    {
        if (bound || !game) return;
        game.Hit += OnHit;
        game.ParrySucceeded += OnParry;
        game.GuardBlocked += OnGuard;
        game.RunStarted += OnRunStarted;
        game.RunEnded += OnRunEnded;
        game.MissileSpawned += OnMissileSpawned;
        game.LaserChanged += OnLaserChanged;
        bound = true;
    }
    private void Unbind()
    {
        if (!bound || !game) { bound = false; return; }
        game.Hit -= OnHit;
        game.ParrySucceeded -= OnParry;
        game.GuardBlocked -= OnGuard;
        game.RunStarted -= OnRunStarted;
        game.RunEnded -= OnRunEnded;
        game.MissileSpawned -= OnMissileSpawned;
        game.LaserChanged -= OnLaserChanged;
        bound = false;
    }

    private void BuildEnvironment()
    {
        Transform room = Group("Environment • navy containment chamber", transform);
        Rect("Backdrop", room, Vector2.zero, new Vector2(26, 16), Navy, -100);
        Rect("Upper wall", room, new Vector2(0, 1.3f), new Vector2(20, 6.4f), new Color(.036f,.074f,.12f), -99);
        for (int i = 0; i < 7; i++)
        {
            float x = -8.25f + i * 2.75f;
            Rect("Recessed panel " + (i + 1), room, new Vector2(x, .25f), new Vector2(2.61f, 5.35f), new Color(.03f,.064f,.105f), -98);
            Rect("Panel seam", room, new Vector2(x - 1.3f, .25f), new Vector2(.018f, 5.35f), new Color(.082f,.14f,.195f), -97);
            Rect("Panel top lip", room, new Vector2(x, 2.92f), new Vector2(2.6f, .035f), new Color(.10f,.18f,.23f), -97);
            for (int j = 0; j < 4; j++)
                Rect("Vent", room, new Vector2(x + .7f, 2.55f - j * .10f), new Vector2(.49f,.025f), new Color(.115f,.19f,.24f), -96);
        }
        for (int i = 0; i < 24; i++)
            Rect("Wall calibration tick", room, new Vector2(-8.5f + i*.76f, -2.48f), new Vector2(.035f, i % 4 == 0 ? .16f : .07f), new Color(.16f,.25f,.31f), -94);
        Rect("Upper cable tray", room, new Vector2(0,3.1f), new Vector2(19.4f,.1f), new Color(.075f,.14f,.19f), -93);
        Rect("Cyan recessed strip", room, new Vector2(-3.8f,3.1f), new Vector2(7.1f,.025f), WithAlpha(Cyan,.56f), -92);
        Rect("Right recessed strip", room, new Vector2(6.4f,3.1f), new Vector2(3.7f,.025f), WithAlpha(Pink,.5f), -92);
        Rect("Floor", room, new Vector2(0,-5.25f), new Vector2(24,4.5f), new Color(.018f,.034f,.061f), -90);
        Rect("Floor lip", room, new Vector2(0,-2.98f), new Vector2(20,.055f), new Color(.15f,.25f,.30f), -89);
        Rect("Floor underlip", room, new Vector2(0,-3.055f), new Vector2(20,.055f), new Color(.035f,.105f,.14f), -89);
        for (int i = 0; i < 28; i++)
            Rect("Deck slat", room, new Vector2(-9.6f + i*.72f,-3.25f), new Vector2(.49f,.023f), new Color(.10f,.17f,.22f), -88, -18);
        for (int i = 0; i < 11; i++)
            Rect("Floor perspective joint", room, new Vector2(-10 + i*2,-4.2f), new Vector2(.018f,2.3f), new Color(.055f,.10f,.14f), -88, (i-5)*5f);

        Transform platform = Group("Hero induction platform", room);
        Disc("Platform shadow", platform, new Vector2(-5.4f,-2.92f), new Vector2(3.2f,.31f), new Color(.006f,.018f,.025f), -86);
        Disc("Cyan platform rim", platform, new Vector2(-5.4f,-2.86f), new Vector2(2.8f,.18f), WithAlpha(Cyan,.5f), -85);
        Disc("Platform surface", platform, new Vector2(-5.4f,-2.82f), new Vector2(2.7f,.15f), new Color(.08f,.17f,.21f), -84);
        Disc("Induction pool", platform, new Vector2(-5.4f,-2.78f), new Vector2(1.7f,.07f), WithAlpha(Cyan,.3f), -83);
        Rect("Deck marker left", platform, new Vector2(-7.16f,-2.82f), new Vector2(.15f,.04f), Cyan, -82);
        Rect("Deck marker right", platform, new Vector2(-3.66f,-2.82f), new Vector2(.15f,.04f), Cyan, -82);

        Transform console = Group("Left diagnostic column", room);
        Rect("Console frame", console, new Vector2(-8.2f,-.3f), new Vector2(.58f,4.65f), new Color(.075f,.135f,.18f), -81);
        Rect("Console inset", console, new Vector2(-8.2f,-.2f), new Vector2(.35f,4.2f), new Color(.018f,.048f,.07f), -80);
        ambientLights = new Transform[9];
        for(int i=0;i<9;i++)
            ambientLights[i] = Rect("Telemetry diode " + i, console, new Vector2(-8.2f,1.6f-i*.39f), new Vector2(.19f,.045f), WithAlpha(Cyan,.65f), -79).transform;

        Transform launcher = Group("Orange missile induction rail", room);
        Rect("Launcher pedestal", launcher, new Vector2(8.55f,-2.65f), new Vector2(1.3f,.55f), new Color(.13f,.18f,.22f), -70);
        Rect("Launcher throat", launcher, new Vector2(8.38f,-2.55f), new Vector2(.83f,.23f), new Color(.03f,.05f,.065f), -69);
        Rect("Rail lip", launcher, new Vector2(8.15f,-2.39f), new Vector2(1.19f,.045f), Orange, -68);
        for(int i=0;i<4;i++) Rect("Hazard tick",launcher,new Vector2(8.1f+i*.28f,-2.81f),new Vector2(.13f,.055f),WithAlpha(Orange,.5f),-68,35);
    }

    private void BuildHero()
    {
        heroRoot = Group("Hero • segmented sentinel", transform);
        heroRoot.localPosition = new Vector3(-5.4f,-.7f,0);
        var parts = new List<BodyPart>();
        Color armor = new Color(.27f,.39f,.47f), light = new Color(.49f,.66f,.72f), dark = new Color(.095f,.17f,.22f);
        MakePart(parts,"Rear upper leg",new Vector2(-.25f,-.62f),new Vector2(.29f,.71f),dark,0,-12);
        MakePart(parts,"Rear knee",new Vector2(-.31f,-.98f),new Vector2(.31f,.30f),armor,1,0,true);
        MakePart(parts,"Rear shin",new Vector2(-.34f,-1.43f),new Vector2(.25f,.82f),dark,2,4);
        MakePart(parts,"Rear boot",new Vector2(-.21f,-1.90f),new Vector2(.52f,.25f),armor,3);
        MakePart(parts,"Rear upper arm",new Vector2(-.38f,.02f),new Vector2(.24f,.7f),dark,4,-11);
        MakePart(parts,"Rear glove",new Vector2(-.38f,-.37f),new Vector2(.26f,.27f),armor,5,0,true);
        MakePart(parts,"Hip frame",new Vector2(0,-.36f),new Vector2(.63f,.33f),dark,6);
        MakePart(parts,"Front thigh",new Vector2(.17f,-.69f),new Vector2(.33f,.68f),armor,7,6);
        MakePart(parts,"Front thigh edge",new Vector2(.31f,-.65f),new Vector2(.055f,.45f),light,8,6);
        MakePart(parts,"Front knee",new Vector2(.20f,-1.01f),new Vector2(.36f,.31f),light,9,0,true);
        MakePart(parts,"Front shin",new Vector2(.17f,-1.465f),new Vector2(.30f,.82f),armor,10,-4);
        MakePart(parts,"Front shin light",new Vector2(.3f,-1.45f),new Vector2(.045f,.49f),Cyan,11,-4);
        MakePart(parts,"Front boot",new Vector2(.32f,-1.91f),new Vector2(.59f,.25f),light,12);
        MakePart(parts,"Boot sole",new Vector2(.31f,-2.04f),new Vector2(.61f,.08f),dark,13);
        MakePart(parts,"Torso silhouette",new Vector2(-.02f,.09f),new Vector2(.81f,1.07f),dark,14);
        MakePart(parts,"Chest armor",new Vector2(.02f,.17f),new Vector2(.65f,.86f),armor,15,0,false,true);
        MakePart(parts,"Chest edge",new Vector2(.27f,.23f),new Vector2(.07f,.57f),light,16);
        MakePart(parts,"Chest inset",new Vector2(.03f,.23f),new Vector2(.23f,.31f),dark,17,0,true);
        reactor = MakePart(parts,"Cyan reactor",new Vector2(.065f,.25f),new Vector2(.095f,.14f),Cyan,18,0,true);
        MakePart(parts,"Collar",new Vector2(0,.71f),new Vector2(.44f,.15f),light,19);
        MakePart(parts,"Helmet rim",new Vector2(.01f,1.03f),new Vector2(.73f,.73f),dark,20,0,true);
        MakePart(parts,"Helmet shell",new Vector2(.04f,1.08f),new Vector2(.63f,.61f),light,21,0,true);
        MakePart(parts,"Helmet crown",new Vector2(-.02f,1.34f),new Vector2(.42f,.13f),armor,22);
        MakePart(parts,"Visor housing",new Vector2(.20f,1.07f),new Vector2(.48f,.24f),dark,23);
        MakePart(parts,"Luminous visor",new Vector2(.26f,1.08f),new Vector2(.35f,.10f),Cyan,24);
        MakePart(parts,"Ear receiver",new Vector2(-.24f,1.03f),new Vector2(.17f,.22f),armor,25,0,true);
        MakePart(parts,"Receiver dot",new Vector2(-.26f,1.03f),new Vector2(.065f,.065f),Orange,26,0,true);
        upperArm = MakePart(parts,"Shield shoulder",new Vector2(.36f,.47f),new Vector2(.39f,.4f),light,27,0,true).transform;
        forearm = MakePart(parts,"Shield forearm",new Vector2(.52f,.13f),new Vector2(.27f,.59f),armor,28,-28).transform;
        hand = MakePart(parts,"Shield glove",new Vector2(.68f,-.09f),new Vector2(.25f,.27f),light,29,0,true).transform;
        bodyParts = parts.ToArray();
        burnGlow = Sprite("Laser burn bloom", transform, new Vector2(-5.4f,-.7f),new Vector2(2.2f,3.7f), WithAlpha(Pink,0),45,glowSprite);
    }

    private SpriteRenderer MakePart(List<BodyPart> parts,string label,Vector2 position,Vector2 scale,Color color,int order,float angle=0,bool circle=false,bool hex=false)
    {
        SpriteRenderer sr = Sprite(label,heroRoot,position,scale,color,10+order,circle?circleSprite:hex?hexSprite:squareSprite,angle);
        parts.Add(new BodyPart {renderer=sr,position=sr.transform.localPosition,scale=sr.transform.localScale,rotation=angle,color=color});
        return sr;
    }

    private void BuildShield()
    {
        shieldRoot=Group("Shield • guard plate and energy membrane",transform);
        shieldRoot.localPosition=new Vector3(-4.68f,-1.14f,0);
        shieldRoot.localRotation=Quaternion.Euler(0,0,-27);
        shieldHalo=Sprite("Energy halo",shieldRoot,Vector2.zero,new Vector2(.75f,2.14f),WithAlpha(Cyan,.16f),49,glowSprite);
        shieldPlate=Sprite("Armored shield core",shieldRoot,Vector2.zero,new Vector2(.26f,1.45f),new Color(.21f,.40f,.46f),50,hexSprite);
        Rect("Rear rim",shieldRoot,new Vector2(-.11f,0),new Vector2(.045f,1.1f),new Color(.57f,.78f,.81f),51);
        shieldEnergy=Sprite("Cyan defensive membrane",shieldRoot,new Vector2(.12f,0),new Vector2(.115f,1.45f),WithAlpha(Cyan,.62f),52,hexSprite);
        Rect("Shield center light",shieldRoot,new Vector2(.18f,0),new Vector2(.04f,.4f),Ice,53);
        for(int i=-1;i<=1;i+=2)
            Rect("Shield end cap",shieldRoot,new Vector2(.07f,i*.59f),new Vector2(.20f,.08f),Ice,53,-i*20);
    }

    private void BuildEmitters()
    {
        Transform bank=Group("Five independent laser emitters",transform);
        Rect("Emitter spine",bank,new Vector2(8.72f,.15f),new Vector2(.46f,5.08f),new Color(.12f,.16f,.22f),-62);
        Rect("Spine seam",bank,new Vector2(8.89f,.15f),new Vector2(.035f,5.0f),WithAlpha(Pink,.3f),-61);
        emitterCores=new SpriteRenderer[5];emitterRings=new SpriteRenderer[5];emitterHalos=new SpriteRenderer[5];
        warningMarkers=new Transform[5];
        laserCores=new LineRenderer[5];laserGlows=new LineRenderer[5];laserGuides=new LineRenderer[5];
        for(int i=0;i<5;i++)
        {
            float y=2.1f-i*1.0f;
            Transform emitter=Group("Laser emitter "+(i+1).ToString("00"),bank);
            emitter.localPosition=new Vector3(8.7f,y,0);
            Sprite("Emitter armored socket",emitter,Vector2.zero,new Vector2(.66f,.62f),new Color(.20f,.24f,.31f),-59,hexSprite);
            Disc("Dark lens cavity",emitter,new Vector2(-.04f,0),new Vector2(.43f,.43f),new Color(.025f,.035f,.055f),-58);
            emitterHalos[i]=Sprite("Charging aura",emitter,new Vector2(-.06f,0),new Vector2(.9f,.9f),WithAlpha(Pink,0),-57,glowSprite);
            emitterRings[i]=Sprite("Charge ring",emitter,new Vector2(-.06f,0),new Vector2(.37f,.37f),WithAlpha(Pink,.26f),-56,ringSprite);
            emitterCores[i]=Disc("Laser lens",emitter,new Vector2(-.06f,0),new Vector2(.17f,.17f),WithAlpha(Pink,.35f),-55);
            Transform marker=Group("Warning ! • visible before this laser fires",emitter);
            marker.localPosition=new Vector3(-.65f,0,0);
            Disc("Warning badge",marker,Vector2.zero,new Vector2(.43f,.43f),new Color(.13f,.025f,.075f),66);
            Sprite("Warning badge ring",marker,Vector2.zero,new Vector2(.43f,.43f),Pink,67,ringSprite);
            Rect("Exclamation stroke",marker,new Vector2(0,.055f),new Vector2(.055f,.17f),Ice,68);
            Disc("Exclamation dot",marker,new Vector2(0,-.095f),new Vector2(.065f,.065f),Ice,68);
            marker.gameObject.SetActive(false);warningMarkers[i]=marker;
            laserGuides[i]=Beam("Laser "+(i+1)+" charge guide",bank,.014f,WithAlpha(Pink,0),1);
            laserGlows[i]=Beam("Laser "+(i+1)+" outer glow",bank,.19f,WithAlpha(Pink,0),44);
            laserCores[i]=Beam("Laser "+(i+1)+" white hot core",bank,.035f,WithAlpha(Ice,0),46);
        }
    }

    private void Update()
    {
        if(!game || !heroRoot) return;
        float dt=Mathf.Min(Time.deltaTime,.05f), now=Time.time;
        if(previousPhase!=game.Phase) {resultAge=0;previousPhase=game.Phase;}
        resultAge+=dt;
        bool alive=game.Phase!=GamePhase.Dying && game.Phase!=GamePhase.Lost;
        float target=alive && game.Mode==DefenseMode.Guard?1:0;
        guardPose=Mathf.MoveTowards(guardPose,target,dt*(target>guardPose?9:5));
        parryKick=Mathf.MoveTowards(parryKick,0,dt*4.3f);
        if(alive && game.Mode==DefenseMode.Parry) parryKick=Mathf.Max(parryKick,Mathf.Clamp01(1-game.ModeAge/ .18f));
        contactFlash=Mathf.MoveTowards(contactFlash,0,dt*3);
        screenFlash=Mathf.MoveTowards(screenFlash,0,dt*2.8f);
        shake=Mathf.MoveTowards(shake,0,dt*1.5f);
        UpdateHero(now,alive);
        UpdateShield(now,alive);
        UpdateMissiles(now);
        UpdateLasers(now,alive);
        UpdateEffects(dt);
        if(ambientLights!=null) for(int i=0;i<ambientLights.Length;i++)
            if(ambientLights[i]) ambientLights[i].localScale=new Vector3(.19f*(.4f+.6f*Mathf.PerlinNoise(now*.33f,i*.7f)),.045f,1);
        if(arenaCamera)
            arenaCamera.transform.position=cameraRest+new Vector3(Mathf.Sin(now*81)*shake*.10f,Mathf.Cos(now*69)*shake*.075f,0);
        if(!alive && game.LastHitWasLaser && game.Phase==GamePhase.Dying)
        {
            ashClock-=dt;
            if(ashClock<=0 && game.DeathProgress>.35f)
            {
                ashClock=.027f;
                SpawnSpark(new Vector2(-5.4f+UnityEngine.Random.Range(-.45f,.45f),-.7f+UnityEngine.Random.Range(-1.3f,1.15f)),new Vector2(UnityEngine.Random.Range(-.4f,.4f),UnityEngine.Random.Range(.3f,1.2f)),new Color(.58f,.66f,.69f,.7f),UnityEngine.Random.Range(.4f,1.1f),.035f,0);
            }
        }
    }

    private void UpdateHero(float now,bool alive)
    {
        float death=alive?0:game.DeathProgress;
        float breath=Mathf.Sin(now*2.1f)*.024f;
        for(int i=0;i<bodyParts.Length;i++)
        {
            BodyPart p=bodyParts[i];if(!p.renderer)continue;
            Transform t=p.renderer.transform;
            p.renderer.enabled=alive || death<.99f;
            t.localPosition=p.position+new Vector3(parryKick*.09f,breath*Mathf.Clamp01(p.position.y+1.7f),0);
            t.localScale=p.scale;
            t.localRotation=Quaternion.Euler(0,0,p.rotation);
            p.renderer.color=p.color;
            if(!alive)
            {
                if(game.LastHitWasLaser)
                {
                    float charred=Mathf.InverseLerp(.22f,.57f,death),ash=Mathf.InverseLerp(.60f,1,death);
                    Color hot=Color.Lerp(p.color,new Color(1,.83f,.91f),Mathf.Clamp01(death/.14f));
                    p.renderer.color=Color.Lerp(hot,new Color(.038f,.045f,.055f),charred);
                    Color c=p.renderer.color;c.a=1-ash;p.renderer.color=c;
                    t.localPosition=p.position+new Vector3(Mathf.Sin(i*3.7f)*ash*.28f,-charred*.18f+ash*(i%3)*.10f,0);
                    t.localScale=Vector3.Scale(p.scale,new Vector3(1-ash*.6f,1-ash*.6f,1));
                    t.localRotation=Quaternion.Euler(0,0,p.rotation+ash*Mathf.Sin(i)*40);
                }
                else
                {
                    float fly=Mathf.InverseLerp(.035f,1,death);
                    float angle=(i*2.39996f)+.4f;
                    Vector2 v=new Vector2(Mathf.Cos(angle)*(1.3f+(i%4)*.35f)-.55f,Mathf.Abs(Mathf.Sin(angle))*3.0f+.5f);
                    t.localPosition=p.position+(Vector3)(v*fly+Vector2.down*(fly*fly*4.3f));
                    t.localRotation=Quaternion.Euler(0,0,p.rotation+fly*(i%2==0?1:-1)*(100+i*9));
                    Color c=Color.Lerp(new Color(1,.78f,.44f),p.color,Mathf.Clamp01(death*6));c.a=1-Mathf.InverseLerp(.68f,1,death);p.renderer.color=c;
                }
            }
        }
        if(alive)
        {
            forearm.localPosition=Vector3.Lerp(new Vector3(.52f,.13f,0),new Vector3(.60f,.30f,0),guardPose)+Vector3.right*parryKick*.17f;
            forearm.localRotation=Quaternion.Euler(0,0,Mathf.Lerp(-28,-55,guardPose)-parryKick*17);
            hand.localPosition=Vector3.Lerp(new Vector3(.68f,-.09f,0),new Vector3(.78f,.11f,0),guardPose)+Vector3.right*parryKick*.26f;
            upperArm.localRotation=Quaternion.Euler(0,0,-guardPose*12-parryKick*15);
            reactor.color=Color.Lerp(Cyan,Ice,.2f+.2f*Mathf.Sin(now*3));
        }
        if(burnGlow)
        {
            float glow=!alive&&game.LastHitWasLaser?Mathf.Sin(Mathf.Clamp01(death/.6f)*Mathf.PI)*.80f:0;
            burnGlow.color=WithAlpha(Pink,glow);
            burnGlow.transform.localScale=new Vector3(2.1f+glow,3.7f+glow,1);
        }
    }

    private void UpdateShield(float now,bool alive)
    {
        shieldRoot.gameObject.SetActive(alive);
        if(!alive)return;
        shieldRoot.localPosition=Vector3.Lerp(new Vector3(-4.68f,-1.14f,0),new Vector3(-4.6f,-.7f,0),guardPose)+new Vector3(parryKick*.38f,parryKick*.22f,0);
        shieldRoot.localRotation=Quaternion.Euler(0,0,Mathf.Lerp(-27,0,guardPose)+parryKick*23);
        shieldRoot.localScale=new Vector3(1+parryKick*.10f,1+guardPose*.49f,1);
        shieldEnergy.color=Color.Lerp(WithAlpha(Cyan,.63f),Ice,Mathf.Clamp01(parryKick+contactFlash));
        shieldPlate.color=Color.Lerp(new Color(.21f,.40f,.46f),new Color(.34f,.64f,.7f),guardPose*.5f);
        shieldHalo.color=WithAlpha(Cyan,.18f+guardPose*.20f+contactFlash*.45f+parryKick*.3f);
        shieldHalo.transform.localScale=new Vector3(.75f+contactFlash*.26f+guardPose*.10f,2.14f+Mathf.Sin(now*11)*.025f,1);
    }

    private void UpdateMissiles(float now)
    {
        liveMissiles.Clear();
        foreach(MissileState state in game.Missiles)
        {
            liveMissiles.Add(state.Id);
            MissileVisual visual;
            if(!missiles.TryGetValue(state.Id,out visual))
            {
                visual=CreateMissile(state.Id);missiles.Add(state.Id,visual);
                visual.root.position=state.Position;
                visual.trail.Clear();
            }
            visual.root.position=state.Position;
            visual.root.rotation=Quaternion.Euler(0,0,state.Rotation+(state.IsReflected?Mathf.Sin(now*17+state.Id)*4:Mathf.Sin(now*9+state.Id)*1.5f));
            float flicker=.8f+Mathf.Sin(now*47+state.Id)*.18f;
            visual.flame.transform.localScale=new Vector3((state.IsReflected?.64f:.40f)*flicker,.16f,1);
            visual.flame.color=state.IsReflected?Ice:new Color(1,.68f,.22f);
            visual.glow.color=WithAlpha(state.IsReflected?Cyan:Orange,.38f);
            visual.trail.startColor=WithAlpha(state.IsReflected?Cyan:Orange,.8f);
            visual.trail.endColor=WithAlpha(state.IsReflected?Cyan:Orange,0);
        }
        removedMissiles.Clear();
        foreach(var pair in missiles)if(!liveMissiles.Contains(pair.Key))removedMissiles.Add(pair.Key);
        foreach(int id in removedMissiles){Destroy(missiles[id].root.gameObject);missiles.Remove(id);}
    }

    private MissileVisual CreateMissile(int id)
    {
        Transform root=Group("Missile "+id,missileRoot);
        var v=new MissileVisual {root=root};
        v.glow=Sprite("Engine bloom",root,new Vector2(-.30f,0),new Vector2(.65f,.32f),WithAlpha(Orange,.38f),58,glowSprite);
        v.flame=Sprite("Engine flame",root,new Vector2(-.34f,0),new Vector2(.40f,.16f),new Color(1,.68f,.22f),59,triangleSprite,180);
        Sprite("Fins upper",root,new Vector2(-.16f,.12f),new Vector2(.20f,.16f),Orange,60,triangleSprite,-24);
        Sprite("Fins lower",root,new Vector2(-.16f,-.12f),new Vector2(.20f,.16f),Orange,60,triangleSprite,24);
        Sprite("Missile casing",root,new Vector2(0,0),new Vector2(.55f,.21f),new Color(.83f,.85f,.81f),61,hexSprite);
        Sprite("Orange nose",root,new Vector2(.28f,0),new Vector2(.22f,.20f),Orange,62,triangleSprite);
        Rect("Payload band",root,new Vector2(.10f,0),new Vector2(.065f,.22f),Orange,63);
        Rect("Casing glint",root,new Vector2(-.04f,.07f),new Vector2(.27f,.025f),Ice,64);
        GameObject trailObject=new GameObject("Flight trail");trailObject.transform.SetParent(root,false);
        trailObject.transform.localPosition=new Vector3(-.3f,0,0);
        v.trail=trailObject.AddComponent<TrailRenderer>();
        v.trail.sharedMaterial=spriteMaterial;v.trail.time=.32f;v.trail.minVertexDistance=.07f;
        v.trail.startWidth=.10f;v.trail.endWidth=.005f;v.trail.sortingOrder=57;
        v.trail.startColor=WithAlpha(Orange,.7f);v.trail.endColor=WithAlpha(Orange,0);
        v.trail.numCapVertices=3;
        return v;
    }

    private void UpdateLasers(float now,bool alive)
    {
        for(int i=0;i<5;i++)
        {
            LaserState laser=game.Lasers[i];
            bool warning=laser.Phase==LaserPhase.Warning;
            // Gameplay clears attacks immediately on death; retain only the fatal beam
            // for 0.30 s so the thermal hit remains visually understandable.
            bool fatalBeam=game.Phase==GamePhase.Dying&&game.LastHitWasLaser&&i==lastFiringLaser&&game.DeathProgress*game.DeathDuration<.30f;
            bool firing=laser.Phase==LaserPhase.Firing||fatalBeam;
            float charge=warning?1-Mathf.Clamp01(laser.WarningRemaining/Mathf.Max(.01f,laser.WarningDuration)):0;
            float pulse=.5f+.5f*Mathf.Sin(now*(warning?21:2.7f)+i);
            if(warningMarkers!=null&&i<warningMarkers.Length&&warningMarkers[i])
            {
                warningMarkers[i].gameObject.SetActive(warning);
                warningMarkers[i].localScale=Vector3.one*(.94f+pulse*.14f);
            }
            Transform emitter=emitterCores[i].transform.parent;
            emitter.localPosition=new Vector3(game.Settings.LaserSourceX,laser.OriginY,0);
            emitterCores[i].color=Color.Lerp(WithAlpha(Pink,.30f),Ice,firing?1:charge*.7f);
            emitterCores[i].transform.localScale=Vector3.one*(firing?.23f:.17f+charge*.07f);
            emitterRings[i].color=WithAlpha(Pink,firing?.95f:warning?.45f+pulse*.5f:.24f);
            emitterRings[i].transform.localScale=Vector3.one*(warning?.39f+charge*.22f:.37f);
            emitterHalos[i].color=WithAlpha(Pink,firing?.48f:warning?.08f+charge*.26f:0);
            emitterHalos[i].transform.localScale=Vector3.one*(firing?1.3f:.9f);
            Vector3 from=new Vector3(game.Settings.LaserSourceX-.13f,laser.OriginY,0);
            bool guarded=alive&&game.Mode==DefenseMode.Guard;
            Vector3 to=new Vector3(guarded?game.Settings.ShieldX:game.Settings.HeroX,game.Settings.HeroCenterY,0);
            SetBeam(laserGuides[i],from,to,WithAlpha(Pink,warning?.07f+charge*.12f:0),.014f);
            float shimmer=.82f+.18f*Mathf.Sin(now*64+i*1.7f);
            SetBeam(laserGlows[i],from,to,WithAlpha(Pink,firing?.25f*shimmer:0),.18f+shimmer*.04f);
            SetBeam(laserCores[i],from,to,WithAlpha(new Color(1,.83f,.95f),firing?1:0),.024f+shimmer*.016f);
            if(firing&&guarded)
            {
                contactFlash=Mathf.Max(contactFlash,.65f);
                if(UnityEngine.Random.value<Time.deltaTime*12)
                    SpawnSpark(to,new Vector2(UnityEngine.Random.Range(.5f,2.4f),UnityEngine.Random.Range(-2.7f,2.7f)),Cyan,.25f,.045f,0);
            }
        }
    }

    private void OnParry()
    {
        parryKick=1;contactFlash=1;shake=.2f;
        Vector2 at=new Vector2(game.Settings.ShieldX+.15f,game.Settings.HeroCenterY);
        Burst(at,Cyan,19,1.5f,4.8f,.50f);
        AddPulse(at,Cyan,.35f,1.35f);
        Play(parryClip,.65f);
    }
    private void OnGuard()
    {
        contactFlash=1;
        if(Time.unscaledTime-lastGuardSound>.20f){Play(guardClip,.3f);lastGuardSound=Time.unscaledTime;}
    }
    private void OnHit(bool laser)
    {
        screenFlash=laser?.45f:.8f;shake=laser?.45f:1;
        Vector2 at=new Vector2(game.Settings.HeroX,game.Settings.HeroCenterY);
        if(laser){Burst(at,Pink,34,.3f,2.4f,.7f);AddPulse(at,Pink,.65f,2.4f);}
        else{Burst(at,Orange,62,1.5f,6.0f,1.0f);Burst(at,Ice,22,2,5,.45f);AddPulse(at,Orange,.6f,4.4f);AddPulse(at,Ice,.25f,2.2f);}
        Play(laser?laserHitClip:hitClip,.68f);
    }
    private void OnRunStarted()
    {
        guardPose=parryKick=contactFlash=screenFlash=shake=ashClock=0;lastFiringLaser=-1;
        foreach(var m in missiles.Values)if(m.root)Destroy(m.root.gameObject);missiles.Clear();
        foreach(var s in sparks)if(s.sprite)Destroy(s.sprite.gameObject);sparks.Clear();
        foreach(var p in pulses)if(p.sprite)Destroy(p.sprite.gameObject);pulses.Clear();
        Play(startClip,.55f);
    }
    private void OnRunEnded(bool won){if(won){Play(winClip,.55f);AddPulse(new Vector2(-5.4f,-.7f),Cyan,1.1f,4.0f);}}
    private void OnMissileSpawned(MissileState missile){if(Time.unscaledTime-lastLaunchSound>.15f){Play(launchClip,.22f);lastLaunchSound=Time.unscaledTime;}}
    private void OnLaserChanged(LaserState laser)
    {
        if(laser.Phase==LaserPhase.Firing)lastFiringLaser=laser.Slot;
        if(laser.Phase==LaserPhase.Warning&&laser.Slot==0)Play(warningClip,.40f);
    }

    private void Burst(Vector2 position,Color color,int count,float minSpeed,float maxSpeed,float life)
    {
        for(int i=0;i<count;i++)
        {
            float angle=UnityEngine.Random.Range(0,Mathf.PI*2),speed=UnityEngine.Random.Range(minSpeed,maxSpeed);
            SpawnSpark(position,new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*speed,color,UnityEngine.Random.Range(life*.5f,life*1.25f),UnityEngine.Random.Range(.025f,.075f),color==Orange?2.6f:1f);
        }
    }
    private void SpawnSpark(Vector2 position,Vector2 velocity,Color color,float life,float size,float gravity)
    {
        SpriteRenderer sr=Sprite("Spark",effectsRoot,position,new Vector2(size*2.4f,size),color,80,squareSprite,UnityEngine.Random.Range(0,360));
        sparks.Add(new Spark{sprite=sr,velocity=velocity,life=life,size=sr.transform.localScale,color=color,gravity=gravity,spin=UnityEngine.Random.Range(-200,200)});
    }
    private void AddPulse(Vector2 at,Color color,float life,float size)
    {
        SpriteRenderer sr=Sprite("Impact shockwave",effectsRoot,at,Vector2.one*.1f,color,79,ringSprite);
        pulses.Add(new Pulse{sprite=sr,life=life,size=size,color=color});
    }
    private void UpdateEffects(float dt)
    {
        for(int i=sparks.Count-1;i>=0;i--)
        {
            Spark s=sparks[i];s.age+=dt;
            if(s.age>=s.life){Destroy(s.sprite.gameObject);sparks.RemoveAt(i);continue;}
            float p=s.age/s.life;s.velocity+=Vector2.down*s.gravity*dt;
            s.sprite.transform.position+=(Vector3)s.velocity*dt;
            s.sprite.transform.Rotate(0,0,s.spin*dt);
            s.sprite.transform.localScale=s.size*(1-p*.7f);
            s.sprite.color=WithAlpha(s.color,(1-p)*s.color.a);
        }
        for(int i=pulses.Count-1;i>=0;i--)
        {
            Pulse p=pulses[i];p.age+=dt;
            if(p.age>=p.life){Destroy(p.sprite.gameObject);pulses.RemoveAt(i);continue;}
            float t=p.age/p.life;p.sprite.transform.localScale=Vector3.one*Mathf.Lerp(.1f,p.size,1-Mathf.Pow(1-t,2));
            p.sprite.color=WithAlpha(p.color,(1-t)*(1-t)*.8f);
        }
    }

    private void OnGUI()
    {
        if(!game)return;
        Matrix4x4 old=GUI.matrix;Color oldColor=GUI.color;
        labelDraws.Clear();
        float scale=Mathf.Min(Screen.width/1600f,Screen.height/900f);
        float dx=(Screen.width-1600*scale)*.5f,dy=(Screen.height-900*scale)*.5f;
        GUI.matrix=Matrix4x4.TRS(new Vector3(dx,dy,0),Quaternion.identity,Vector3.one*scale);
        GUI.color=Color.white;
        Fill(new Rect(0,0,1600,156),new Color(.018f,.035f,.06f,.95f));
        Fill(new Rect(65,151,1470,1),new Color(.17f,.28f,.34f,.7f));
        Text(new Rect(68,31,530,25),"A E G I S    /    SAVUNMA LABORATUVARI",14,Muted,FontStyle.Bold);
        Text(new Rect(66,60,530,53),"SON SAVUNMA",37,Ice,FontStyle.Bold);
        Text(new Rect(69,116,490,22),"DENEY A–01    •    TEK TUŞ / İKİ TEPKİ",12,Muted);

        Text(new Rect(632,28,336,24),"KALAN SÜRE",13,Muted,FontStyle.Bold,TextAnchor.MiddleCenter);
        DrawTimer(Mathf.Max(0,game.Remaining),game.Remaining<=15&&game.Phase==GamePhase.Playing?Orange:Ice);
        Fill(new Rect(642,126,316,3),new Color(.12f,.22f,.27f));
        Fill(new Rect(642,126,316*Mathf.Clamp01(game.Progress),3),Cyan);

        Text(new Rect(1185,35,160,23),"SAVUŞTURMA",12,Muted,FontStyle.Bold);
        DrawDigits(game.ParryCount.ToString("00"),1183,66,.68f,Cyan);
        Text(new Rect(1381,35,154,23),"ENGELLEME",12,Muted,FontStyle.Bold);
        DrawDigits(game.BlockCount.ToString("00"),1379,66,.68f,Ice);
        Text(new Rect(1186,116,349,22),"EN İYİ  "+game.BestSeconds.ToString("0.0")+" s",12,Muted);

        Fill(new Rect(0,758,1600,142),new Color(.018f,.035f,.06f,.96f));
        Fill(new Rect(65,758,1470,1),new Color(.17f,.28f,.34f,.7f));
        KeyCap(new Rect(70,790,48,48),"E",game.Mode!=DefenseMode.None?Cyan:Muted);
        Text(new Rect(141,779,560,29),"E’ye kısa bas-bırak: Füze savuştur.",20,Ice,FontStyle.Bold);
        Text(new Rect(141,816,560,28),"E’yi basılı tut: Lazeri engelle.",20,Ice,FontStyle.Bold);
        Fill(new Rect(755,786,1,65),new Color(.17f,.28f,.34f));
        string mode=game.Mode==DefenseMode.Guard?"KALKAN AKTİF":game.Mode==DefenseMode.Parry?"SAVUŞTURMA":"HAZIR POZİSYON";
        Color modeColor=game.Mode==DefenseMode.None?Muted:Cyan;
        Text(new Rect(788,782,390,26),mode,15,modeColor,FontStyle.Bold);
        Text(new Rect(788,817,390,23),game.Mode==DefenseMode.Guard?"BASILI TUT  •  LAZER KORUMASI":game.Mode==DefenseMode.Parry?"KISA DARBE  •  FÜZEYİ GERİ ÇEVİR":"ZAMANLAMANI KORU",12,Muted);
        Text(new Rect(1223,784,311,25),"HEDEF",12,Muted,FontStyle.Bold,TextAnchor.MiddleRight);
        Text(new Rect(1223,812,311,32),"90 saniye hayatta kal",19,Ice,FontStyle.Bold,TextAnchor.MiddleRight);

        if(game.Phase==GamePhase.Playing)
        {
            bool warning=false,firing=false;
            foreach(LaserState l in game.Lasers){warning|=l.Phase==LaserPhase.Warning;firing|=l.Phase==LaserPhase.Firing;}
            if(warning||firing)
            {
                Color c=firing?Pink:Color.Lerp(Pink,Ice,.35f+.25f*Mathf.Sin(Time.time*13));
                Panel(new Rect(628,179,584,58),new Color(.13f,.035f,.085f,.93f),WithAlpha(Pink,.55f));
                Text(new Rect(651,189,538,37),warning?"!  LAZER HAZIRLANIYOR  ·  E’Yİ TUT":"!  LAZER SALVOSU  ·  BASILI TUT",18,c,FontStyle.Bold,TextAnchor.MiddleCenter);
            }
            else
            {
                Text(new Rect(1089,184,440,28),game.Elapsed<25?"01 / ISINMA":game.Elapsed<55?"02 / BASINÇ ARTIYOR":"03 / SON DİRENİŞ",13,Muted,FontStyle.Bold,TextAnchor.MiddleRight);
            }
        }
        else if(game.Phase==GamePhase.Ready) DrawReady();
        else if(game.Phase==GamePhase.Lost) DrawResult(false);
        else if(game.Phase==GamePhase.Won) DrawResult(true);
        if(game.Phase==GamePhase.Dying)
            Text(new Rect(590,640,830,36),game.LastHitWasLaser?"KRİTİK ISI  /  KORUMA KAYBI":"DİREKT İSABET  /  GÖVDE KAYBI",17,game.LastHitWasLaser?Pink:Orange,FontStyle.Bold,TextAnchor.MiddleCenter);
        if(screenFlash>0)Fill(new Rect(0,156,1600,602),WithAlpha(game.LastHitWasLaser?Pink:Orange,screenFlash*.16f));
        FlushLabels();
        GUI.matrix=old;GUI.color=oldColor;
    }

    private void DrawReady()
    {
        Panel(new Rect(682,282,667,361),new Color(.024f,.054f,.085f,.98f),new Color(.20f,.37f,.43f));
        Fill(new Rect(682,282,5,361),Cyan);
        Text(new Rect(719,305,587,28),"SAVUNMA PROTOKOLÜ",13,Cyan,FontStyle.Bold);
        Text(new Rect(716,344,594,106),"90 SANİYE.\nTEK KALKAN.",39,Ice,FontStyle.Bold);
        Text(new Rect(720,469,575,63),"Füzeleri geri çevir. Beş lazeri aynı anda engelle.\nHareket yok. Zamanlama her şey.",18,Muted);
        Fill(new Rect(720,555,590,1),new Color(.14f,.25f,.31f));
        KeyCap(new Rect(721,579,37,37),"E",Cyan);
        Text(new Rect(777,581,530,34),"DENEYİ BAŞLAT",18,Ice,FontStyle.Bold);
    }
    private void DrawResult(bool won)
    {
        Color accent=won?Cyan:game.LastHitWasLaser?Pink:Orange;
        Panel(new Rect(640,281,730,369),new Color(.022f,.045f,.075f,.98f),WithAlpha(accent,.55f));
        Fill(new Rect(640,281,5,369),accent);
        Text(new Rect(678,304,652,29),won?"PROTOKOL TAMAMLANDI":"DENEY SONLANDI",13,accent,FontStyle.Bold);
        Text(new Rect(675,346,659,67),won?"SAVUNMA BAŞARILI.":"KALKAN DÜŞTÜ.",36,Ice,FontStyle.Bold);
        Text(new Rect(680,426,645,57),won?"90 saniye boyunca hattı korudun.":game.LastHitWasLaser?"Lazerler için E’yi basılı tut.\nKalkanı salvolar bitene kadar koru.":"Füze kalkana yaklaşınca E’ye kısa bas-bırak.\nBasılı kalkan füzeyi savuşturmaz.",18,Muted);
        Text(new Rect(680,504,645,34),"SÜRE  "+Mathf.Min(90,game.Elapsed).ToString("0.0")+" s     /     SAVUŞTURMA  "+game.ParryCount+"     /     ENGELLEME  "+game.BlockCount,14,Ice,FontStyle.Bold);
        Fill(new Rect(679,553,652,1),new Color(.14f,.25f,.31f));
        KeyCap(new Rect(680,578,37,37),"E",accent);
        Text(new Rect(738,580,591,34),game.IsInputReleaseRequired?"E’Yİ BIRAK, SONRA YENİDEN BAS":"YENİDEN BAŞLAT",17,Ice,FontStyle.Bold);
    }

    private void Text(Rect r,string text,int size,Color color,FontStyle fontStyle=FontStyle.Normal,TextAnchor align=TextAnchor.UpperLeft)
    {
        // A style belongs to a single label position. Mixed-size dynamic labels must
        // not share mutable style state while Unity submits its GUI text batches.
        long key=((long)Mathf.RoundToInt(r.x)<<32)|((long)Mathf.RoundToInt(r.y)<<16)|(long)(size*100+(int)fontStyle*10+(int)align);
        GUIStyle style;
        if(!styles.TryGetValue(key,out style))
        {
            style=new GUIStyle(GUI.skin.label){font=uiFont,fontSize=size,fontStyle=fontStyle,alignment=align,wordWrap=false,padding=new RectOffset(0,0,0,0)};
            styles.Add(key,style);
        }
        labelDraws.Add(new LabelDraw{rect=r,text=text,style=style,color=color,size=size,fontStyle=fontStyle});
    }
    private void FlushLabels()
    {
        // Populate the complete current-frame font atlas before submitting any
        // labels, including first-frame result headings and changing score digits.
        if(uiFont)foreach(LabelDraw draw in labelDraws)
            uiFont.RequestCharactersInTexture(draw.text,draw.size,draw.fontStyle);
        foreach(LabelDraw draw in labelDraws)
        {
            draw.style.normal.textColor=draw.color;
            GUI.Label(draw.rect,draw.text,draw.style);
        }
    }
    private static readonly int[] DigitSegments={0x3f,0x06,0x5b,0x4f,0x66,0x6d,0x7d,0x07,0x7f,0x6f};
    private static void DrawTimer(float remaining,Color color)
    {
        string digits=remaining.ToString("00.0",System.Globalization.CultureInfo.InvariantCulture);
        float width=0;
        foreach(char c in digits)width+=c=='.'?14:41;
        DrawDigits(digits,800-(width-12)*.5f,58,1,color);
    }
    private static void DrawDigits(string digits,float x,float y,float scale,Color color)
    {
        foreach(char c in digits)
        {
            if(c=='.'){Fill(new Rect(x+scale,y+43*scale,5*scale,5*scale),color);x+=14*scale;continue;}
            if(c<'0'||c>'9')continue;
            int mask=DigitSegments[c-'0'];
            DrawTimerSegment(new Rect(x+4*scale,y,21*scale,4*scale),color,(mask&1)!=0);
            DrawTimerSegment(new Rect(x+25*scale,y+4*scale,4*scale,18*scale),color,(mask&2)!=0);
            DrawTimerSegment(new Rect(x+25*scale,y+26*scale,4*scale,18*scale),color,(mask&4)!=0);
            DrawTimerSegment(new Rect(x+4*scale,y+44*scale,21*scale,4*scale),color,(mask&8)!=0);
            DrawTimerSegment(new Rect(x,y+26*scale,4*scale,18*scale),color,(mask&16)!=0);
            DrawTimerSegment(new Rect(x,y+4*scale,4*scale,18*scale),color,(mask&32)!=0);
            DrawTimerSegment(new Rect(x+4*scale,y+22*scale,21*scale,4*scale),color,(mask&64)!=0);
            x+=41*scale;
        }
    }
    private static void DrawTimerSegment(Rect rect,Color color,bool active)
    {
        if(!active)return;
        Fill(new Rect(rect.x-1,rect.y-1,rect.width+2,rect.height+2),WithAlpha(color,.10f));
        Fill(rect,color);
    }
    private static void Fill(Rect r,Color color){Color old=GUI.color;GUI.color=color;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=old;}
    private static void Panel(Rect r,Color background,Color edge){Fill(r,background);Fill(new Rect(r.x,r.y,r.width,1),edge);Fill(new Rect(r.x,r.yMax-1,r.width,1),edge);Fill(new Rect(r.x,r.y,1,r.height),edge);Fill(new Rect(r.xMax-1,r.y,1,r.height),edge);}
    private void KeyCap(Rect r,string text,Color color){Panel(r,new Color(.06f,.13f,.17f),WithAlpha(color,.55f));Text(r,text,22,color,FontStyle.Bold,TextAnchor.MiddleCenter);}

    private void CreateAudio()
    {
        audioSource=gameObject.AddComponent<AudioSource>();audioSource.playOnAwake=false;audioSource.spatialBlend=0;audioSource.volume=.8f;
        parryClip=Tone("Cyan parry",.16f,880,1800,.15f);
        guardClip=Tone("Energy impact",.11f,170,380,.3f);
        launchClip=Tone("Missile ignition",.18f,180,70,.55f);
        hitClip=Tone("Hull explosion",.6f,95,22,.93f);
        laserHitClip=Tone("Thermal overload",.8f,620,80,.47f);
        startClip=Tone("Protocol start",.27f,420,1100,.02f);
        winClip=Tone("Protocol complete",.75f,500,1500,.01f);
        warningClip=Tone("Five laser charge",.25f,780,980,.03f);
    }
    private static AudioClip Tone(string name,float duration,float start,float end,float noise)
    {
        int rate=22050,count=Mathf.CeilToInt(duration*rate);float[] data=new float[count];double phase=0;
        var rng=new System.Random(name.GetHashCode());
        for(int i=0;i<count;i++)
        {
            float t=(float)i/count,frequency=Mathf.Lerp(start,end,t);phase+=frequency/rate*Math.PI*2;
            float envelope=Mathf.Min(1,t*35)*Mathf.Pow(1-t,1.8f);
            float wave=(float)Math.Sin(phase)*.7f+(float)Math.Sin(phase*2.01)*.12f;
            data[i]=(wave*(1-noise)+((float)rng.NextDouble()*2-1)*noise)*envelope*.3f;
        }
        AudioClip clip=AudioClip.Create(name,count,1,rate,false);clip.SetData(data,0);return clip;
    }
    private void Play(AudioClip clip,float volume){if(audioSource&&clip)audioSource.PlayOneShot(clip,volume);}

    private static Transform Group(string label,Transform parent){var t=new GameObject(label).transform;t.SetParent(parent,false);return t;}
    private SpriteRenderer Rect(string name,Transform parent,Vector2 position,Vector2 scale,Color color,int order,float angle=0){return Sprite(name,parent,position,scale,color,order,squareSprite,angle);}
    private SpriteRenderer Disc(string name,Transform parent,Vector2 position,Vector2 scale,Color color,int order){return Sprite(name,parent,position,scale,color,order,circleSprite);}
    private SpriteRenderer Sprite(string name,Transform parent,Vector2 position,Vector2 scale,Color color,int order,Sprite sprite,float angle=0)
    {
        Transform t=Group(name,parent);t.localPosition=position;t.localScale=new Vector3(scale.x,scale.y,1);t.localRotation=Quaternion.Euler(0,0,angle);
        SpriteRenderer renderer=t.gameObject.AddComponent<SpriteRenderer>();renderer.sprite=sprite;renderer.sharedMaterial=spriteMaterial;renderer.color=color;renderer.sortingOrder=order;return renderer;
    }
    private LineRenderer Beam(string name,Transform parent,float width,Color color,int order)
    {
        Transform t=Group(name,parent);LineRenderer line=t.gameObject.AddComponent<LineRenderer>();line.sharedMaterial=spriteMaterial;line.positionCount=2;line.useWorldSpace=true;
        line.startWidth=line.endWidth=width;line.startColor=line.endColor=color;line.sortingOrder=order;line.numCapVertices=4;return line;
    }
    private static void SetBeam(LineRenderer line,Vector3 from,Vector3 to,Color color,float width){line.SetPosition(0,from);line.SetPosition(1,to);line.startColor=line.endColor=color;line.startWidth=line.endWidth=width;}
    private static Color WithAlpha(Color color,float alpha){color.a=alpha;return color;}

    private static Sprite Shape(string name)
    {
#if UNITY_EDITOR
        const string folder="Assets/Art/A";
        Directory.CreateDirectory(folder);
        string path=folder+"/"+name+".png";
        Sprite existing=AssetDatabase.LoadAssetAtPath<Sprite>(path);if(existing)return existing;
#endif
        const int size=128;Texture2D texture=new Texture2D(size,size,TextureFormat.RGBA32,false);texture.name="Arena "+name;
        Color[] pixels=new Color[size*size];
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)
        {
            float px=(x+.5f)/size*2-1,py=(y+.5f)/size*2-1;
            float d=0;
            switch(name)
            {
                case "Circle":d=Mathf.Sqrt(px*px+py*py)-.96f;break;
                case "Ring":d=Mathf.Abs(Mathf.Sqrt(px*px+py*py)-.89f)-.047f;break;
                case "Hex":d=Mathf.Max(Mathf.Abs(py)-.94f,Mathf.Abs(px)+Mathf.Abs(py)*.24f-.98f);break;
                case "Triangle":d=Mathf.Max(-px-.95f,Mathf.Abs(py)*2+px-.95f);break;
                default:d=Mathf.Max(Mathf.Abs(px),Mathf.Abs(py))-.99f;break;
            }
            float alpha=name=="Glow"?Mathf.Pow(Mathf.Clamp01(1-Mathf.Sqrt(px*px+py*py)),2):Mathf.Clamp01(.5f-d*size*.5f);
            pixels[y*size+x]=new Color(1,1,1,alpha);
        }
        texture.SetPixels(pixels);texture.Apply();
#if UNITY_EDITOR
        File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType=TextureImporterType.Sprite;importer.spritePixelsPerUnit=size;importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.filterMode=FilterMode.Bilinear;importer.textureCompression=TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Sprite>(path);
#else
        return UnityEngine.Sprite.Create(texture,new Rect(0,0,size,size),new Vector2(.5f,.5f),size);
#endif
    }
    private static Material PresentationMaterial()
    {
#if UNITY_EDITOR
        const string path="Assets/Art/A/ArenaUnlit.mat";
        Material existing=AssetDatabase.LoadAssetAtPath<Material>(path);if(existing)return existing;
#endif
        Shader shader=Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");if(!shader)shader=Shader.Find("Sprites/Default");
        Material material=new Material(shader){name="Arena • unlit geometry"};
#if UNITY_EDITOR
        AssetDatabase.CreateAsset(material,path);AssetDatabase.SaveAssets();
#endif
        return material;
    }
}
