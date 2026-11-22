using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif

namespace CollisionC
{
    /// <summary>Presentation only. Every world, HUD and effect object is saved by BuildWorld.</summary>
    public sealed class CEnvironmentView : MonoBehaviour
    {
        public CReceiver Receiver;
        public CExitTrigger Exit;
        public CPlate[] Plates;
        public COrbMotor Orb;
        public Transform DoorLeft, DoorRight;
        public Transform ReceiverCoverLeft, ReceiverCoverRight;
        public SpriteRenderer DoorLight, ReceiverRing, ReceiverCore, ReceiverWash;
        public SpriteRenderer[] ReceiverCorners, ImpactRings, DeathSparks;
        public TextMesh ReceiverLabel, ExitLabel;
        public Canvas HUDCanvas;
        public Text TimerText, PhaseText, ParryText, OverlayTitle, OverlayBody, OverlayAction;
        public Image TimerFill, OverlayAccent;
        public GameObject Overlay;

        bool receiverOpen, doorOpen;
        float visualTime, doorSlide, receiverSlide, deathAge = 10f;
        int nextImpact;
        float[] impactAges;
        Color[] impactColors;
        Vector2 deathOrigin;
        static readonly Color Blue = new Color(.22f, .76f, 1f);
        static readonly Color PaleBlue = new Color(.75f, .94f, 1f);
        static readonly Color Gold = new Color(.91f, .66f, .32f);
        static readonly Color Copper = new Color(.59f, .32f, .19f);
        static readonly Color Muted = new Color(.53f, .51f, .44f);

        public void ResetWorld()
        {
            visualTime = 0;
            doorSlide = 0;
            receiverSlide = 0;
            deathAge = 10f;
            nextImpact = 0;
            SetReceiverOpen(false);
            SetDoorOpen(false);
            SetDoorPosition();
            SetReceiverCoverPosition();
            if (ImpactRings != null)
            {
                impactAges = new float[ImpactRings.Length];
                impactColors = new Color[ImpactRings.Length];
                for (int i = 0; i < ImpactRings.Length; i++)
                {
                    impactAges[i] = 1f;
                    if (ImpactRings[i]) ImpactRings[i].enabled = false;
                }
            }
            if (DeathSparks != null)
                foreach (SpriteRenderer spark in DeathSparks) if (spark) spark.enabled = false;
        }

        public void SetReceiverOpen(bool open)
        {
            receiverOpen = open;
            if (ReceiverRing) ReceiverRing.color = open ? Blue : new Color(.36f, .32f, .25f);
            if (ReceiverCore) ReceiverCore.color = open ? PaleBlue : new Color(.45f, .33f, .19f);
            if (ReceiverWash) ReceiverWash.color = open ? new Color(.2f, .65f, 1f, .065f) : Color.clear;
            if (ReceiverCorners != null)
                foreach (SpriteRenderer corner in ReceiverCorners) if (corner) corner.color = open ? Blue : Muted;
            if (ReceiverLabel)
            {
                ReceiverLabel.text = open ? "ALICI HAZIR" : "ENERJİ ALICISI";
                ReceiverLabel.color = open ? PaleBlue : Muted;
            }
        }

        public void SetDoorOpen(bool open)
        {
            doorOpen = open;
            if (DoorLight) DoorLight.color = open ? new Color(.38f, .87f, .76f, .45f) : new Color(.13f, .16f, .16f);
            if (ExitLabel)
            {
                ExitLabel.text = open ? "ÇIKIŞ AÇIK" : "KİLİTLİ";
                ExitLabel.color = open ? new Color(.58f, 1f, .84f) : Muted;
            }
            if (open && ReceiverLabel) ReceiverLabel.text = "ENERJİ ALINDI";
        }

        public void PlayImpact(Vector2 position, Color tint)
        {
            if (ImpactRings == null || ImpactRings.Length == 0) return;
            if (impactAges == null || impactAges.Length != ImpactRings.Length)
            {
                impactAges = new float[ImpactRings.Length];
                impactColors = new Color[ImpactRings.Length];
                for (int i = 0; i < impactAges.Length; i++) impactAges[i] = 1f;
            }
            int index = nextImpact++ % ImpactRings.Length;
            if (!ImpactRings[index]) return;
            impactAges[index] = 0;
            impactColors[index] = tint;
            ImpactRings[index].transform.position = position;
            ImpactRings[index].transform.localScale = Vector3.one * .15f;
            ImpactRings[index].color = tint;
            ImpactRings[index].enabled = true;
        }

        public void PlayDeath(Vector2 position)
        {
            deathAge = 0;
            deathOrigin = position;
            PlayImpact(position, Gold);
            if (DeathSparks == null) return;
            foreach (SpriteRenderer spark in DeathSparks)
                if (spark) { spark.transform.position = position; spark.enabled = true; }
        }

        public void TickVisual(float dt)
        {
            dt = Mathf.Max(0, dt);
            visualTime += dt;
            doorSlide = Mathf.MoveTowards(doorSlide, doorOpen ? 1f : 0f, dt * 1.8f);
            receiverSlide = Mathf.MoveTowards(receiverSlide, receiverOpen ? 1f : 0f, dt / .4f);
            SetDoorPosition();
            SetReceiverCoverPosition();
            if (receiverOpen && ReceiverRing)
                ReceiverRing.color = Color.Lerp(Blue, PaleBlue, .18f + .15f * Mathf.Sin(visualTime * 4));
            if (impactAges != null && ImpactRings != null)
            {
                for (int i = 0; i < ImpactRings.Length; i++)
                {
                    SpriteRenderer ring = ImpactRings[i];
                    if (!ring || impactAges[i] >= .24f) continue;
                    impactAges[i] += dt;
                    float age = Mathf.Clamp01(impactAges[i] / .24f);
                    ring.transform.localScale = Vector3.one * Mathf.Lerp(.15f, .68f, age);
                    Color tint = impactColors[i]; tint.a *= 1f - age; ring.color = tint;
                    if (age >= 1) ring.enabled = false;
                }
            }
            if (deathAge < .9f && DeathSparks != null)
            {
                deathAge += dt;
                for (int i = 0; i < DeathSparks.Length; i++)
                {
                    SpriteRenderer spark = DeathSparks[i]; if (!spark) continue;
                    float angle = (i * 137.5f + 12) * Mathf.Deg2Rad;
                    Vector2 velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (1.5f + i % 3 * .4f);
                    spark.transform.position = deathOrigin + velocity * deathAge + Vector2.down * (deathAge * deathAge * 1.8f);
                    spark.transform.localRotation = Quaternion.Euler(0, 0, i * 29 + deathAge * 180);
                    spark.transform.localScale = new Vector3(.05f, .13f, 1) * Mathf.Lerp(1f, .2f, deathAge / .9f);
                    Color tint = i % 2 == 0 ? Gold : Blue; tint.a = Mathf.Clamp01(1 - deathAge / .9f); spark.color = tint;
                    if (deathAge >= .9f) spark.enabled = false;
                }
            }
        }

        void SetDoorPosition()
        {
            if (DoorLeft) DoorLeft.localPosition = new Vector3(-.49f - doorSlide * .76f, -2.43f, 0);
            if (DoorRight) DoorRight.localPosition = new Vector3(.49f + doorSlide * .76f, -2.43f, 0);
        }

        void SetReceiverCoverPosition()
        {
            // These small shutters only cover the receiver; the main exit has its own animation.
            float offset = Mathf.Lerp(.42f, 1.25f, Mathf.SmoothStep(0, 1, receiverSlide));
            if (ReceiverCoverLeft) ReceiverCoverLeft.localPosition = new Vector3(-offset, 0, 0);
            if (ReceiverCoverRight) ReceiverCoverRight.localPosition = new Vector3(offset, 0, 0);
        }

        public void UpdateHUD(CGameController game)
        {
            if (!game) return;
            if (TimerText) TimerText.text = game.Remaining > 0 ? Mathf.CeilToInt(game.Remaining).ToString("00") : "00";
            if (TimerFill) TimerFill.fillAmount = game.CountdownDuration > 0 ? Mathf.Clamp01(game.Remaining / game.CountdownDuration) : 0;
            if (ParryText) ParryText.text = "BAŞARILI PARRY  " + game.ParryCount.ToString("00");
            if (TimerText) TimerText.color = game.ReceiverReady ? Blue : PaleBlue;
            if (PhaseText)
            {
                switch (game.Phase)
                {
                    case CGamePhase.Instruction: PhaseText.text = "ENERJİ KİLİDİ  /  HAZIRLIK"; break;
                    case CGamePhase.Playing: PhaseText.text = "HAYATTA KAL · TOPU PARRY İLE GERİ GÖNDER"; break;
                    case CGamePhase.ReceiverReady: PhaseText.text = "Topu enerji alıcısına gönder!"; break;
                    case CGamePhase.DoorOpen: PhaseText.text = "KAPI AÇILDI · ORTADAKİ ÇIKIŞA YÜRÜ"; break;
                    case CGamePhase.Dying: PhaseText.text = "ENERJİ TEMASI"; break;
                    case CGamePhase.Lost: PhaseText.text = "DENEME SONLANDI"; break;
                    case CGamePhase.Won: PhaseText.text = "ÇIKIŞ TAMAMLANDI"; break;
                }
                PhaseText.color = game.Phase == CGamePhase.ReceiverReady ? Blue : Gold;
            }
            bool showOverlay = game.Phase == CGamePhase.Instruction || game.Phase == CGamePhase.Lost || game.Phase == CGamePhase.Won;
            if (Overlay) Overlay.SetActive(showOverlay);
            if (!showOverlay) return;
            bool instruction = game.Phase == CGamePhase.Instruction;
            bool won = game.Phase == CGamePhase.Won;
            if (OverlayTitle) OverlayTitle.text = instruction ? "ENERJİ KİLİDİ" : won ? "ÇIKIŞ TAMAMLANDI" : "ENERJİ TEMASI";
            if (OverlayBody)
                OverlayBody.text = instruction
                    ? Mathf.CeilToInt(Mathf.Max(0, game.CountdownDuration)) + " saniye hayatta kal. Mavi topa dokunma.\n\nA / D veya ← / → ile yürü ve kalkan yönünü seç.\nE'ye kısa basıp bırak: kısa bir parry penceresi açılır.\nE'yi uzun tutmak savunma sağlamaz.\n\nSüre dolunca yeni bir parry ile topu yükle;\nortadaki ışıklı alıcıya gönder ve açılan kapıya yürü."
                    : won
                        ? "Top alıcıya ulaştı, enerji kilidi çözüldü.\n\nBaşarılı parry: " + game.ParryCount + "\nKapıdan güvenle çıktın."
                        : "Top kalkan penceresinin dışında sana ulaştı.\n\nTopun geldiği tarafa dön;\nE'ye kısa basıp bırakarak zamanlamayı yakala.";
            if (OverlayAction) OverlayAction.text = instruction ? "[ E ]  BAŞLA" : "[ R ]  YENİDEN DENE";
            if (OverlayAccent) OverlayAccent.color = won ? new Color(.40f, .91f, .74f) : instruction ? Blue : Gold;
        }

#if UNITY_EDITOR
        public static CEnvironmentView BuildWorld()
        {
            var root = new GameObject("C World • Enerji Kilidi / editable arena");
            var view = root.AddComponent<CEnvironmentView>();
            BuildBackground(Group(root.transform, "01 • Back wall / decoration only"));
            BuildBounds(Group(root.transform, "02 • Solid arena boundaries / 20 x 8.8 m"));
            BuildDoor(view, Group(root.transform, "03 • Energy receiver and exit / triggers only"));
            BuildPlates(view, Group(root.transform, "04 • Breakable plate prefab instances"));
            BuildOrb(view, Group(root.transform, "05 • Blue energy orb / swept Collider2D"));
            BuildEffects(view, Group(root.transform, "06 • Persistent impact and death pool"));
            BuildHUD(view);
            view.ResetWorld();
            return view;
        }

        static void BuildBackground(Transform parent)
        {
            Quad("Warm darkness", parent, new Vector2(0, .4f), new Vector2(24, 14), new Color(.055f, .060f, .062f), -70);
            Quad("Arena recess", parent, new Vector2(0, .4f), new Vector2(20, 8.8f), new Color(.085f, .091f, .094f), -65);
            for (int column = 0; column < 8; column++)
            {
                float x = -8.75f + column * 2.5f;
                Quad("Brushed wall panel " + column, parent, new Vector2(x, .4f), new Vector2(2.44f, 8.72f),
                    column % 2 == 0 ? new Color(.091f, .097f, .098f) : new Color(.085f, .089f, .090f), -64);
                Quad("Panel inlay " + column, parent, new Vector2(x, 3.9f), new Vector2(.76f, .026f), new Color(.22f, .19f, .14f), -60);
                for (int row = 0; row < 3; row++)
                    Quad("Wall joint " + column + "/" + row, parent, new Vector2(x, -2.4f + row * 2.5f), new Vector2(2.38f, .025f), new Color(.12f, .12f, .11f), -61);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                Quad("Copper wall conduit " + side, parent, new Vector2(side * 7.3f, .4f), new Vector2(.075f, 8.5f), new Color(.25f, .18f, .13f), -58);
                Quad("Dim conduit light " + side, parent, new Vector2(side * 7.3f, 1.1f), new Vector2(.026f, 1.1f), new Color(.65f, .42f, .18f), -57);
                var label = WorldText("Sector stencil " + side, parent, new Vector2(side * 6.1f, -.7f), side < 0 ? "SEKTÖR  C" : "ENERJİ  /  03", new Color(.21f, .21f, .18f), .055f, -55);
                label.characterSize = .055f;
            }
            WorldText("Backdrop facility title", parent, new Vector2(0, 2.7f), "ÇARPIŞMA ODASI", new Color(.20f, .21f, .20f), .095f, -54);
            Quad("Center backdrop rule", parent, new Vector2(0, 2.3f), new Vector2(3.6f, .018f), new Color(.26f, .22f, .16f), -54);
        }

        static void BuildBounds(Transform parent)
        {
            Bound("Floor / continuous flat ground", parent, new Vector2(0, -4.28f), new Vector2(20.72f, .56f), new Color(.24f, .24f, .21f));
            Bound("Ceiling / solid behind plates", parent, new Vector2(0, 4.98f), new Vector2(20.72f, .36f), new Color(.24f, .22f, .18f));
            Bound("Left wall / solid behind plates", parent, new Vector2(-10.18f, .4f), new Vector2(.36f, 8.8f), new Color(.25f, .23f, .19f));
            Bound("Right wall / solid behind plates", parent, new Vector2(10.18f, .4f), new Vector2(.36f, 8.8f), new Color(.25f, .23f, .19f));
            Quad("Ground contact stripe", parent, new Vector2(0, -4.035f), new Vector2(20, .07f), Gold, 2);
            Quad("Ceiling contact stripe", parent, new Vector2(0, 4.83f), new Vector2(20, .06f), Copper, 2);
            for (int i = 0; i < 20; i++)
            {
                Quad("Floor inset " + i, parent, new Vector2(-9.5f + i, -4.36f), new Vector2(.66f, .035f), new Color(.12f, .14f, .14f), 2);
                Sprite("Ceiling bolt " + i, parent, new Vector2(-9.5f + i, 5.0f), Vector2.one * .055f, Gold, "Circle", 3);
            }
        }

        static void Bound(string name, Transform parent, Vector2 center, Vector2 size, Color color)
        {
            Transform root = Group(parent, name); root.localPosition = center;
            Quad("Solid metal", root, Vector2.zero, size, color, 0);
            var collider = root.gameObject.AddComponent<BoxCollider2D>(); collider.size = size;
        }

        static void BuildDoor(CEnvironmentView view, Transform parent)
        {
            Quad("Door mounting shadow", parent, new Vector2(0, -2.4f), new Vector2(2.75f, 3.25f), new Color(.045f, .055f, .058f), -40);
            view.DoorLight = Quad("Open passage light", parent, new Vector2(0, -2.4f), new Vector2(1.92f, 3.0f), new Color(.13f, .16f, .16f), -36);
            for (int side = -1; side <= 1; side += 2)
            {
                Quad("Door jamb " + side, parent, new Vector2(side * 1.18f, -2.4f), new Vector2(.24f, 3.2f), Copper, -29);
                Quad("Door edge rail " + side, parent, new Vector2(side * 1.03f, -2.4f), new Vector2(.045f, 3.08f), Gold, -28);
                Transform leaf = Group(parent, side < 0 ? "Left sliding door / visual only" : "Right sliding door / visual only");
                leaf.localPosition = new Vector2(side * .49f, -2.43f);
                Quad("Door leaf", leaf, Vector2.zero, new Vector2(.94f, 2.98f), new Color(.25f, .25f, .21f), -33);
                Quad("Door leaf inset", leaf, new Vector2(0, -.15f), new Vector2(.74f, 2.30f), new Color(.19f, .20f, .18f), -32);
                Quad("Door vertical seam", leaf, new Vector2(-side * .41f, 0), new Vector2(.035f, 2.9f), Copper, -31);
                for (int i = 0; i < 3; i++) Quad("Door vent " + i, leaf, new Vector2(0, -1.0f + i * .12f), new Vector2(.44f, .035f), new Color(.09f, .12f, .12f), -31);
                if (side < 0) view.DoorLeft = leaf; else view.DoorRight = leaf;
            }
            Quad("Door header", parent, new Vector2(0, -.8f), new Vector2(2.6f, .21f), Copper, -28);
            view.ExitLabel = WorldText("Exit status", parent, new Vector2(0, -3.43f), "KİLİTLİ", Muted, .054f, -20);
            var receiverRoot = Group(parent, "Receiver / 3.2 x 2.2 trigger"); receiverRoot.localPosition = new Vector2(0, -1.05f);
            var receiverCollider = receiverRoot.gameObject.AddComponent<BoxCollider2D>(); receiverCollider.size = new Vector2(3.2f, 2.2f); receiverCollider.isTrigger = true;
            view.Receiver = receiverRoot.gameObject.AddComponent<CReceiver>(); view.Receiver.Zone = receiverCollider;
            view.ReceiverWash = Quad("Receiver active region wash", receiverRoot, Vector2.zero, new Vector2(3.2f, 2.2f), Color.clear, -27);
            view.ReceiverRing = Sprite("Receiver energy ring", receiverRoot, Vector2.zero, new Vector2(1.65f, 1.65f), Muted, "Ring", -23);
            Sprite("Receiver inner socket", receiverRoot, Vector2.zero, new Vector2(.70f, .70f), new Color(.075f, .10f, .11f), "Hex", -22);
            view.ReceiverCore = Sprite("Receiver core", receiverRoot, Vector2.zero, new Vector2(.24f, .24f), Copper, "Hex", -21);
            for (int side = -1; side <= 1; side += 2)
            {
                Transform cover = Group(receiverRoot, side < 0 ? "Receiver left cover / opens when timer ends" : "Receiver right cover / opens when timer ends");
                cover.localPosition = new Vector2(side * .42f, 0);
                Quad("Receiver cover plate / visual only", cover, Vector2.zero, new Vector2(.82f, 1.75f), new Color(.25f, .26f, .23f), -19);
                Quad("Receiver cover inset", cover, Vector2.zero, new Vector2(.64f, 1.51f), new Color(.16f, .18f, .18f), -18);
                Quad("Receiver cover center seam", cover, new Vector2(-side * .38f, 0), new Vector2(.035f, 1.66f), Copper, -17);
                for (int i = 0; i < 3; i++)
                    Quad("Receiver cover vent " + i, cover, new Vector2(0, -.12f + i * .12f), new Vector2(.32f, .03f), Muted, -17);
                if (side < 0) view.ReceiverCoverLeft = cover; else view.ReceiverCoverRight = cover;
            }
            view.ReceiverCorners = new SpriteRenderer[8]; int index = 0;
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2)
            {
                view.ReceiverCorners[index++] = Quad("Receiver horizontal bracket " + x + y, receiverRoot, new Vector2(x * 1.43f, y * 1.08f), new Vector2(.34f, .04f), Muted, -16);
                view.ReceiverCorners[index++] = Quad("Receiver vertical bracket " + x + y, receiverRoot, new Vector2(x * 1.58f, y * .93f), new Vector2(.04f, .34f), Muted, -16);
            }
            view.ReceiverLabel = WorldText("Receiver status", parent, new Vector2(0, .43f), "ENERJİ ALICISI", Muted, .065f, -20);
            var exitRoot = Group(parent, "Exit / 1.8 x 2.1 trigger"); exitRoot.localPosition = new Vector2(0, -2.85f);
            var exitCollider = exitRoot.gameObject.AddComponent<BoxCollider2D>(); exitCollider.size = new Vector2(1.8f, 2.1f); exitCollider.isTrigger = true;
            view.Exit = exitRoot.gameObject.AddComponent<CExitTrigger>(); view.Exit.Zone = exitCollider;
        }

        static void BuildPlates(CEnvironmentView view, Transform parent)
        {
            Directory.CreateDirectory("Assets/Prefabs/C");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            GameObject normal = CreatePlatePrefab(false);
            GameObject armored = CreatePlatePrefab(true);
            Vector2[] positions = { new Vector2(-5.0f, 4.48f), new Vector2(4.8f, 4.48f), new Vector2(-9.68f, 2.45f), new Vector2(-9.68f, -.25f), new Vector2(9.68f, 2.45f), new Vector2(9.68f, -.25f) };
            view.Plates = new CPlate[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                bool isArmored = i == 1 || i == 2 || i == 5;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(isArmored ? armored : normal, parent);
                instance.name = (isArmored ? "Armored" : "Normal") + " plate " + (i + 1).ToString("00");
                instance.transform.localPosition = positions[i];
                instance.transform.localRotation = Quaternion.Euler(0, 0, i < 2 ? 0 : 90);
                view.Plates[i] = instance.GetComponent<CPlate>();
            }
        }

        static GameObject CreatePlatePrefab(bool armored)
        {
            string path = armored ? "Assets/Prefabs/C/C_ArmoredPlate.prefab" : "Assets/Prefabs/C/C_NormalPlate.prefab";
            var root = new GameObject(armored ? "C Armored Plate • two separate impacts" : "C Normal Plate • one impact");
            var plate = root.AddComponent<CPlate>(); plate.MaximumHits = armored ? 2 : 1;
            var solid = root.AddComponent<BoxCollider2D>(); solid.size = new Vector2(1.75f, .48f); plate.Solid = solid;
            plate.Frame = Quad("Frame", root.transform, Vector2.zero, new Vector2(1.75f, .48f), armored ? new Color(.39f, .23f, .15f) : new Color(.31f, .37f, .37f), 7);
            plate.Face = Quad("Face", root.transform, new Vector2(0, -.015f), new Vector2(1.57f, .31f), armored ? new Color(.75f, .46f, .24f) : new Color(.63f, .70f, .67f), 8);
            // One sprite per layer keeps CPlate's hide/reset rules explicit.
            plate.Crack = Quad("First-impact crack", root.transform, Vector2.zero, new Vector2(.055f, .41f), new Color(.14f, .075f, .035f), 9);
            plate.Crack.transform.localRotation = Quaternion.Euler(0, 0, -24);
            plate.Crack.enabled = false;
            plate.ResetPlate();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            DestroyImmediate(root);
            return prefab;
        }

        static void BuildOrb(CEnvironmentView view, Transform parent)
        {
            parent.localPosition = new Vector2(6.7f, 2f);
            var body = parent.gameObject.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic; body.gravityScale = 0; body.freezeRotation = true;
            body.useFullKinematicContacts = true; body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            var circle = parent.gameObject.AddComponent<CircleCollider2D>(); circle.radius = .20f;
            view.Orb = parent.gameObject.AddComponent<COrbMotor>(); view.Orb.Body = body; view.Orb.CollisionShape = circle;
            const string settingsPath = "Assets/Art/C/COrbSettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<COrbSettings>(settingsPath);
            if (!settings) { settings = ScriptableObject.CreateInstance<COrbSettings>(); AssetDatabase.CreateAsset(settings, settingsPath); }
            view.Orb.Settings = settings;
            Sprite("Orb restrained halo", parent, Vector2.zero, Vector2.one * .56f, new Color(.15f, .62f, 1f, .15f), "Circle", 60);
            Sprite("Orb body / radius .20", parent, Vector2.zero, Vector2.one * .40f, Blue, "Circle", 61);
            Sprite("Orb brilliant core", parent, new Vector2(-.025f, .025f), Vector2.one * .22f, PaleBlue, "Circle", 62);
            var trail = parent.gameObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = CCharacterView.PresentationMaterial(); trail.time = .12f; trail.minVertexDistance = .04f;
            trail.startWidth = .16f; trail.endWidth = .015f; trail.numCapVertices = 3;
            trail.startColor = new Color(.25f, .75f, 1f, .46f); trail.endColor = new Color(.15f, .58f, 1f, 0);
            trail.sortingOrder = 59; trail.emitting = false;
        }

        static void BuildEffects(CEnvironmentView view, Transform parent)
        {
            view.ImpactRings = new SpriteRenderer[8];
            for (int i = 0; i < view.ImpactRings.Length; i++)
            {
                var ring = Sprite("Impact ring " + i.ToString("00"), parent, Vector2.zero, Vector2.one * .2f, Blue, "Ring", 70);
                ring.enabled = false; view.ImpactRings[i] = ring;
            }
            view.DeathSparks = new SpriteRenderer[12];
            for (int i = 0; i < view.DeathSparks.Length; i++)
            {
                var spark = Quad("Death spark " + i.ToString("00"), parent, Vector2.zero, new Vector2(.05f, .13f), Gold, 71);
                spark.enabled = false; view.DeathSparks[i] = spark;
            }
        }

        static void BuildHUD(CEnvironmentView view)
        {
            var root = new GameObject("C UI • persistent Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            view.HUDCanvas = root.GetComponent<Canvas>(); view.HUDCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            view.HUDCanvas.sortingOrder = 20;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f;
            Transform canvas = root.transform;
            UIBox("Header backing", canvas, new Vector2(.5f, 1), new Vector2(0, -60), new Vector2(1600, 120), new Color(.035f, .045f, .05f, .96f));
            UIText("Scene title", canvas, new Vector2(0, 1), new Vector2(220, -40), new Vector2(360, 38), "C / ÇARPIŞMA", 28, PaleBlue, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIText("Scene subtitle", canvas, new Vector2(0, 1), new Vector2(220, -73), new Vector2(360, 28), "ENERJİ KİLİDİ   •   TEK ODA", 15, Muted, TextAnchor.MiddleLeft);
            UIText("Countdown label", canvas, new Vector2(.5f, 1), new Vector2(0, -23), new Vector2(260, 24), "ALICININ AÇILMASINA", 13, Muted, TextAnchor.MiddleCenter);
            view.TimerText = UIText("Countdown seconds", canvas, new Vector2(.5f, 1), new Vector2(0, -59), new Vector2(160, 54), "45", 43, PaleBlue, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIBox("Countdown track", canvas, new Vector2(.5f, 1), new Vector2(0, -96), new Vector2(200, 3), new Color(.18f, .21f, .21f));
            view.TimerFill = UIBox("Countdown fill", canvas, new Vector2(.5f, 1), new Vector2(0, -96), new Vector2(200, 3), Blue);
            view.TimerFill.sprite = CCharacterView.Shape("Square"); view.TimerFill.type = Image.Type.Filled; view.TimerFill.fillMethod = Image.FillMethod.Horizontal; view.TimerFill.fillOrigin = 0;
            view.ParryText = UIText("Parry total", canvas, new Vector2(1, 1), new Vector2(-220, -45), new Vector2(360, 32), "BAŞARILI PARRY  00", 19, Gold, TextAnchor.MiddleRight);
            UIText("Plate legend", canvas, new Vector2(1, 1), new Vector2(-220, -77), new Vector2(360, 24), "AÇIK PLAKA: 1 VURUŞ   /   BAKIR: 2 VURUŞ", 13, Muted, TextAnchor.MiddleRight);
            view.PhaseText = UIText("Current objective", canvas, new Vector2(.5f, 1), new Vector2(0, -109), new Vector2(1200, 22), "ENERJİ KİLİDİ  /  HAZIRLIK", 16, Gold, TextAnchor.MiddleCenter);
            UIBox("Footer backing", canvas, new Vector2(.5f, 0), new Vector2(0, 29), new Vector2(1600, 58), new Color(.035f, .045f, .05f, .96f));
            UIText("Controls", canvas, new Vector2(.5f, 0), new Vector2(0, 29), new Vector2(1450, 35), "[ A / D ] veya [ ← / → ]  YÜRÜ & YÖN SEÇ          [ E ]  KISA BAS / BIRAK · PARRY", 17, new Color(.70f, .74f, .72f), TextAnchor.MiddleCenter);
            Image panel = UIBox("Instruction and result card", canvas, new Vector2(.5f, .5f), new Vector2(0, 8), new Vector2(790, 478), new Color(.055f, .074f, .080f, .97f));
            view.Overlay = panel.gameObject;
            view.OverlayAccent = UIBox("Card accent", panel.transform, new Vector2(.5f, 1), new Vector2(0, -2), new Vector2(790, 4), Blue);
            view.OverlayTitle = UIText("Card title", panel.transform, new Vector2(.5f, 1), new Vector2(0, -58), new Vector2(700, 46), "ENERJİ KİLİDİ", 33, PaleBlue, TextAnchor.MiddleCenter, FontStyle.Bold);
            view.OverlayBody = UIText("Card instructions", panel.transform, new Vector2(.5f, .5f), new Vector2(0, -1), new Vector2(700, 280), "45 saniye hayatta kal. Mavi topa dokunma.\n\nA / D veya ← / → ile yürü ve kalkan yönünü seç.\nE'ye kısa basıp bırak: kısa bir parry penceresi açılır.\nE'yi uzun tutmak savunma sağlamaz.\n\nSüre dolunca yeni bir parry ile topu yükle;\nortadaki ışıklı alıcıya gönder ve açılan kapıya yürü.", 21, new Color(.77f, .81f, .80f), TextAnchor.MiddleCenter);
            view.OverlayAction = UIText("Card action", panel.transform, new Vector2(.5f, 0), new Vector2(0, 46), new Vector2(600, 40), "[ E ]  BAŞLA", 22, Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
        }

        static Transform Group(Transform parent, string name)
        {
            var result = new GameObject(name).transform; result.SetParent(parent, false); return result;
        }
        static SpriteRenderer Quad(string name, Transform parent, Vector2 position, Vector2 size, Color color, int order)
            => Sprite(name, parent, position, size, color, "Square", order);
        static SpriteRenderer Sprite(string name, Transform parent, Vector2 position, Vector2 size, Color color, string shape, int order)
        {
            Transform t = Group(parent, name); t.localPosition = position; t.localScale = new Vector3(size.x, size.y, 1);
            var renderer = t.gameObject.AddComponent<SpriteRenderer>(); renderer.sprite = CCharacterView.Shape(shape);
            renderer.sharedMaterial = CCharacterView.PresentationMaterial(); renderer.color = color; renderer.sortingOrder = order;
            return renderer;
        }
        static TextMesh WorldText(string name, Transform parent, Vector2 position, string text, Color color, float size, int order)
        {
            Transform t = Group(parent, name); t.localPosition = position;
            var label = t.gameObject.AddComponent<TextMesh>(); label.text = text; label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 64; label.characterSize = size; label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center; label.color = color;
            var renderer = label.GetComponent<MeshRenderer>(); renderer.sharedMaterial = label.font.material; renderer.sortingOrder = order;
            return label;
        }
        static RectTransform UIRect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform)); var rect = go.GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor; rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
        }
        static Image UIBox(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, Color color)
        {
            var image = UIRect(name, parent, anchor, position, size).gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; return image;
        }
        static Text UIText(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, string value, int fontSize, Color color, TextAnchor align, FontStyle style = FontStyle.Normal)
        {
            var label = UIRect(name, parent, anchor, position, size).gameObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.text = value; label.fontSize = fontSize; label.fontStyle = style;
            label.color = color; label.alignment = align; label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate;
            label.supportRichText = false; label.raycastTarget = false; label.lineSpacing = 1.12f; return label;
        }
#endif
    }
}
