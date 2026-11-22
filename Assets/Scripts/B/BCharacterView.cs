using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif

namespace LavaRiseB
{
    /// <summary>Editable sprite pieces. Animation only changes descendants of the physics capsule.</summary>
    [DisallowMultipleComponent]
    public sealed class BCharacterView : MonoBehaviour
    {
        [Serializable] private sealed class Piece
        {
            public SpriteRenderer Renderer;
            public Vector3 Position, Scale;
            public float Rotation;
            public Color Color;
        }
        [SerializeField] private BPlayerMotor motor;
        [SerializeField] private Transform pose, head, torso, leftLeg, rightLeg, leftBoot, rightBoot, leftArm, rightArm, scarf;
        [SerializeField] private SpriteRenderer chestLight, helmetStripe, shadow;
        [SerializeField] private Piece[] pieces;
        private bool subscribed, wasAlive = true;
        private float landingAge = 1f, deathAge, facing = 1f, gait;
        public string AnimationState { get; private set; } = "Idle";

        public static BCharacterView Build(Transform parent)
        {
            var root = new GameObject("Character artwork • editable sprite pieces").transform;
            root.SetParent(parent, false);
            var view = root.gameObject.AddComponent<BCharacterView>();
            view.BuildArtwork();
            return view;
        }

        public void Bind(BPlayerMotor target)
        {
            Unsubscribe(); motor = target;
            if (Application.isPlaying) Subscribe();
            ApplyProfile();
        }
        private void Awake() { if (!motor) motor = GetComponentInParent<BPlayerMotor>(); Subscribe(); }
        private void OnEnable() => Subscribe();
        private void OnDisable() => Unsubscribe();
        private void Subscribe()
        {
            if (subscribed || !motor) return;
            motor.Landed += OnLand; motor.Died += OnDeath; motor.ProfileChanged += OnProfile;
            subscribed = true;
        }
        private void Unsubscribe()
        {
            if (motor && subscribed)
            {
                motor.Landed -= OnLand; motor.Died -= OnDeath; motor.ProfileChanged -= OnProfile;
            }
            subscribed = false;
        }
        private void OnLand() => landingAge = 0f;
        private void OnDeath() { deathAge = 0f; wasAlive = false; }
        private void OnProfile(int index) { if (motor && motor.IsAlive) RestorePieces(); ApplyProfile(); }
        private void ApplyProfile()
        {
            if (!motor) return;
            if (chestLight) chestLight.color = motor.CurrentProfile.Color;
            if (helmetStripe) helmetStripe.color = motor.CurrentProfile.Color;
        }

        private void BuildArtwork()
        {
            pose = new GameObject("Pose • animations never move physics").transform;
            pose.SetParent(transform, false);
            var parts = new List<Piece>();
            Color dark = new Color(.055f, .105f, .14f), suit = new Color(.24f, .43f, .47f);
            Color light = new Color(.72f, .87f, .83f), orange = new Color(1f, .48f, .26f);
            shadow = Part("Ground shadow", new Vector2(0, -.70f), new Vector2(.87f, .12f), new Color(.015f, .04f, .06f, .38f), 35, "Circle", parts);
            leftArm = Part("Rear arm", new Vector2(-.28f, -.05f), new Vector2(.15f, .41f), dark, 41, "Hex", parts, -12).transform;
            scarf = Part("Scarf tail", new Vector2(-.27f, .24f), new Vector2(.43f, .14f), orange, 42, "Triangle", parts, 173).transform;
            leftLeg = Part("Rear shin", new Vector2(-.13f, -.40f), new Vector2(.19f, .38f), dark, 43, "Hex", parts).transform;
            leftBoot = Part("Rear boot", new Vector2(-.12f, -.64f), new Vector2(.26f, .13f), dark, 44, "Hex", parts).transform;
            rightLeg = Part("Front shin", new Vector2(.13f, -.40f), new Vector2(.20f, .38f), suit, 45, "Hex", parts).transform;
            rightBoot = Part("Front boot", new Vector2(.16f, -.64f), new Vector2(.28f, .13f), light, 46, "Hex", parts).transform;
            Part("Backpack", new Vector2(-.23f, .02f), new Vector2(.21f, .41f), dark, 45, "Hex", parts);
            torso = Part("Jacket silhouette", new Vector2(0, -.035f), new Vector2(.48f, .54f), dark, 47, "Hex", parts).transform;
            Part("Jacket panel", new Vector2(.015f, -.01f), new Vector2(.36f, .43f), suit, 48, "Hex", parts);
            Part("Waist belt", new Vector2(0, -.245f), new Vector2(.41f, .075f), light, 49, "Square", parts);
            Part("Scarf collar", new Vector2(0, .23f), new Vector2(.43f, .115f), orange, 50, "Hex", parts);
            head = Part("Helmet silhouette", new Vector2(0, .455f), new Vector2(.55f, .46f), dark, 51, "Hex", parts).transform;
            Part("Helmet shell", new Vector2(.005f, .47f), new Vector2(.47f, .37f), light, 52, "Hex", parts);
            Part("Dark visor", new Vector2(.075f, .465f), new Vector2(.36f, .18f), dark, 53, "Hex", parts);
            Part("Visor reflection", new Vector2(.11f, .50f), new Vector2(.22f, .032f), new Color(.47f, .91f, .95f), 54, "Square", parts);
            Part("Helmet side disc", new Vector2(-.19f, .455f), new Vector2(.13f, .13f), suit, 55, "Circle", parts);
            helmetStripe = Part("Profile helmet stripe", new Vector2(-.055f, .625f), new Vector2(.10f, .045f), new Color(.28f, .92f, 1f), 56, "Square", parts);
            rightArm = Part("Front forearm", new Vector2(.275f, -.045f), new Vector2(.17f, .40f), light, 56, "Hex", parts, 12).transform;
            Part("Front glove", new Vector2(.30f, -.24f), new Vector2(.16f, .14f), dark, 57, "Circle", parts);
            chestLight = Part("Profile chest light", new Vector2(.09f, .055f), new Vector2(.105f, .105f), new Color(.28f, .92f, 1f), 58, "Circle", parts);
            pieces = parts.ToArray();
        }

        private SpriteRenderer Part(string name, Vector2 at, Vector2 size, Color color, int order, string shape, List<Piece> parts, float angle = 0f)
        {
            var t = new GameObject(name).transform; t.SetParent(pose, false);
            t.localPosition = at; t.localScale = new Vector3(size.x, size.y, 1f);
            t.localRotation = Quaternion.Euler(0, 0, angle);
            var renderer = t.gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = Shape(shape); renderer.sharedMaterial = PresentationMaterial();
            renderer.color = color; renderer.sortingOrder = order;
            parts.Add(new Piece { Renderer = renderer, Position = t.localPosition, Scale = t.localScale, Rotation = angle, Color = color });
            return renderer;
        }

        private void LateUpdate()
        {
            if (!motor || !pose || pieces == null) return;
            float dt = Time.deltaTime;
            if (!motor.IsAlive)
            {
                if (wasAlive) OnDeath();
                deathAge += dt; AnimationState = "Death fragments";
                AnimateDeath(); return;
            }
            if (!wasAlive) { RestorePieces(); wasAlive = true; deathAge = 0f; }
            RestorePieces(); ApplyProfile();
            Vector2 velocity = motor.Velocity;
            if (Mathf.Abs(velocity.x) > .08f) facing = Mathf.Sign(velocity.x);
            landingAge += dt; gait += dt * Mathf.Abs(velocity.x) * 2.1f;
            float running = Mathf.Clamp01(Mathf.Abs(velocity.x) / 4f);
            float bounce = motor.Grounded ? Mathf.Sin(gait * 2f) * .018f * running : 0f;
            pose.localPosition = new Vector3(0f, bounce, 0f);
            pose.localRotation = Quaternion.Euler(0f, 0f, -velocity.x * 1.5f);
            pose.localScale = new Vector3(facing, 1f, 1f);
            if (!motor.Grounded)
            {
                bool rising = velocity.y > .15f;
                AnimationState = rising ? "Jump" : "Fall";
                Rotate(leftArm, rising ? -55f : 38f); Rotate(rightArm, rising ? 38f : -45f);
                Rotate(leftLeg, rising ? 24f : -13f); Rotate(rightLeg, rising ? -20f : 18f);
                Rotate(leftBoot, rising ? 16f : -12f); Rotate(rightBoot, rising ? -12f : 16f);
                scarf.localRotation = Quaternion.Euler(0, 0, 168f - Mathf.Clamp(velocity.y * 2f, -24f, 24f));
                shadow.color = new Color(.015f, .04f, .06f, .09f);
            }
            else if (landingAge < .19f)
            {
                AnimationState = "Landing";
                float squash = Mathf.Sin(landingAge / .19f * Mathf.PI);
                pose.localScale = new Vector3(facing * (1f + .14f * squash), 1f - .13f * squash, 1f);
                pose.localPosition = new Vector3(0, -.08f * squash, 0);
                Rotate(leftLeg, 12f * squash); Rotate(rightLeg, -12f * squash);
            }
            else if (running > .03f)
            {
                AnimationState = "Run";
                float swing = Mathf.Sin(gait) * 29f * running;
                Rotate(leftLeg, swing); Rotate(rightLeg, -swing);
                Rotate(leftBoot, swing * .6f); Rotate(rightBoot, -swing * .6f);
                Rotate(leftArm, -swing); Rotate(rightArm, swing);
                scarf.localRotation = Quaternion.Euler(0, 0, 176f + Mathf.Sin(gait * 1.4f) * 7f);
            }
            else
            {
                AnimationState = "Idle";
                float breath = Mathf.Sin(Time.time * 2.6f) * .009f;
                head.localPosition += Vector3.up * breath;
                torso.localScale += Vector3.up * breath;
                Rotate(leftArm, -12f + breath * 80f); Rotate(rightArm, 12f - breath * 80f);
            }
        }

        private static void Rotate(Transform target, float angle) => target.localRotation = Quaternion.Euler(0, 0, angle);
        private void RestorePieces()
        {
            if (pieces == null) return;
            foreach (Piece piece in pieces)
            {
                if (!piece.Renderer) continue;
                Transform t = piece.Renderer.transform;
                t.localPosition = piece.Position; t.localScale = piece.Scale;
                t.localRotation = Quaternion.Euler(0, 0, piece.Rotation);
                piece.Renderer.color = piece.Color;
            }
            if (pose) { pose.localScale = Vector3.one; pose.localPosition = Vector3.zero; pose.localRotation = Quaternion.identity; }
        }
        private void AnimateDeath()
        {
            float age = Mathf.Min(deathAge, 1.35f);
            for (int i = 0; i < pieces.Length; i++)
            {
                Piece piece = pieces[i]; if (!piece.Renderer) continue;
                if (piece.Renderer == shadow) { piece.Renderer.color = Color.clear; continue; }
                float angle = i * 2.399963f;
                Vector3 launch = new Vector3(Mathf.Cos(angle) * (1.2f + i % 3 * .36f), 1.8f + Mathf.Sin(angle) * .8f, 0);
                Transform t = piece.Renderer.transform;
                t.localPosition = piece.Position + launch * age + Vector3.down * (2.1f * age * age);
                t.localRotation = Quaternion.Euler(0, 0, piece.Rotation + age * (i % 2 == 0 ? 180f : -225f));
                t.localScale = piece.Scale * Mathf.Lerp(1f, .08f, Mathf.Clamp01(age / 1.35f));
                Color burnt = Color.Lerp(piece.Color, new Color(.12f, .045f, .025f), Mathf.Clamp01(age * 2f));
                burnt.a = Mathf.Clamp01(1.35f - age) / 1.35f;
                piece.Renderer.color = burnt;
            }
        }

        /// <summary>Shared B-only persistent geometry provider for the scene builder.</summary>
        public static Sprite Shape(string name)
        {
#if UNITY_EDITOR
            const string folder = "Assets/Art/B";
            Directory.CreateDirectory(folder);
            string path = folder + "/" + name + ".png";
            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path); if (existing) return existing;
#endif
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "B " + name };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float px = (x + .5f) / size * 2f - 1f, py = (y + .5f) / size * 2f - 1f;
                float radius = Mathf.Sqrt(px * px + py * py);
                float d;
                switch (name)
                {
                    case "Circle": d = radius - .96f; break;
                    case "Ring": d = Mathf.Abs(radius - .89f) - .047f; break;
                    case "Hex": d = Mathf.Max(Mathf.Abs(py) - .94f, Mathf.Abs(px) + Mathf.Abs(py) * .24f - .98f); break;
                    case "Triangle": d = Mathf.Max(-px - .95f, Mathf.Abs(py) * 2f + px - .95f); break;
                    default: d = Mathf.Max(Mathf.Abs(px), Mathf.Abs(py)) - .99f; break;
                }
                float alpha = name == "Glow" ? Mathf.Pow(Mathf.Clamp01(1f - radius), 2f) : Mathf.Clamp01(.5f - d * size * .5f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
            texture.SetPixels(pixels); texture.Apply();
#if UNITY_EDITOR
            File.WriteAllBytes(path, texture.EncodeToPNG()); DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite; importer.spritePixelsPerUnit = size;
            importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport(); return AssetDatabase.LoadAssetAtPath<Sprite>(path);
#else
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size);
#endif
        }

        public static Material PresentationMaterial()
        {
#if UNITY_EDITOR
            const string path = "Assets/Art/B/BUnlit.mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path); if (existing) return existing;
            Directory.CreateDirectory("Assets/Art/B");
#endif
            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (!shader) shader = Shader.Find("Sprites/Default");
            var material = new Material(shader) { name = "B • unlit geometry" };
#if UNITY_EDITOR
            AssetDatabase.CreateAsset(material, path);
#endif
            return material;
        }
    }
}
