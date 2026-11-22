#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CombinedD
{
    // Explicit camera regression checks. A local PhysicsScene2D simulates the real player capsule.
    public static class DCameraFixVerification
    {
        public const string StatusPath = "Temp/D_Birlesik/camera-fix-status.txt";
        static readonly List<string> lines = new List<string>();
        static Scene testScene;
        static PhysicsScene2D physics;
        static GameObject fixture;
        static DPlayerMotor player;
        static DOrbMotor orb;
        static DCameraController cameraRig, source;
        static BoxCollider2D upperFloor, landing;
        static bool noWarp, constantSpeed, contained, playerVisible, geometryMatches, lavaMatches;
        static float maxStep, maxSize, minCameraY, normalLavaWidth;
        public static int FailureCount { get; private set; }
        public static bool IsRunning { get; private set; }
        const float Dt = .02f;

        [MenuItem("D/Verify camera fall and zoom in Play Mode")]
        public static void Begin()
        {
            if (!EditorApplication.isPlaying || IsRunning) throw new InvalidOperationException("Idle Play Mode is required.");
            source = UnityEngine.Object.FindAnyObjectByType<DCameraController>();
            if (!source || !source.Player || !source.Orb || !source.Lava) throw new InvalidOperationException("Open the saved D scene first.");
            IsRunning = true; FailureCount = 0; lines.Clear();
            Directory.CreateDirectory("Temp/D_Birlesik"); Directory.CreateDirectory("Belge");
            File.WriteAllText(StatusPath, "RUNNING");
            Scene original = SceneManager.GetActiveScene();
            try
            {
                testScene = SceneManager.CreateScene("D Camera Fix " + Guid.NewGuid().ToString("N"),
                    new CreateSceneParameters(LocalPhysicsMode.Physics2D));
                SceneManager.SetActiveScene(testScene); physics = testScene.GetPhysicsScene2D();
                Check(!Physics2D.GetIgnoreLayerCollision(11, 8) && Physics2D.GetIgnoreLayerCollision(14, 9),
                    "Existing delivery collision matrix has physical ground and isolated manual orb motion.");
                OrdinaryJumps();
                FastFallAndRecovery();
                AspectChange();
            }
            catch (Exception error) { Check(false, "Unexpected exception: " + error); }
            finally
            {
                if (fixture) UnityEngine.Object.DestroyImmediate(fixture);
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                if (testScene.IsValid() && testScene.isLoaded) SceneManager.UnloadSceneAsync(testScene);
                string report = "# D — Kamera düşüş ve zoom düzeltmesi\n\n" + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")
                    + "; Unity " + Application.unityVersion + ".\n\n"
                    + "Ayrı PhysicsScene2D; sahnedeki gerçek karakter kopyası, gerçek yer/iniş collider'ları ve gerçek küre duvar taramaları. "
                    + "Kamera testi sırasında kürenin yalnız duvar maskesi açık: oyuncu hasarı ve saldırılar bu testin kapsamı dışındadır. "
                    + "Düşüş başlangıcında üst test zemini kaldırılır; düşüş sırasında oyuncu/küre konumu atanmaz. "
                    + "Ana sahne nesneleri ve global Physics2D ayarları değiştirilmez. Bu kontrollü test tam oyun veya insan denemesi değildir.\n\n"
                    + string.Join("\n", lines) + "\n\nFailures: " + FailureCount + "\n";
                File.WriteAllText("Belge/D_Kamera_Duzeltme_Test.md", report);
                File.WriteAllText(StatusPath, "DONE failures=" + FailureCount);
                IsRunning = false;
                Debug.Log("D camera fix verification finished. failures=" + FailureCount);
            }
        }

        static void NewFixture()
        {
            if (fixture) UnityEngine.Object.DestroyImmediate(fixture);
            fixture = new GameObject("Camera regression fixture");
            var copy = UnityEngine.Object.Instantiate(source.Player.gameObject);
            SceneManager.MoveGameObjectToScene(copy, testScene); copy.transform.SetParent(fixture.transform);
            player = copy.GetComponent<DPlayerMotor>(); player.Game = null;
            player.ManualSimulation = true; player.UseExternalInput = true;
            player.ResetAt(new Vector2(-6, .85f)); player.Body.interpolation = RigidbodyInterpolation2D.None;
            upperFloor = Box("Takeoff platform", new Vector2(-6, -.25f), new Vector2(6, .5f), 8);
            landing = Box("Landing 24 metres below", new Vector2(-6, -24.25f), new Vector2(6, .5f), 8);
            var ball = Child("Constant speed orb"); ball.layer = 14;
            orb = ball.AddComponent<DOrbMotor>(); orb.ManualSimulation = true;
            orb.Speed = source.Orb.Speed; orb.CollisionShape.radius = source.Orb.Radius;
            orb.CollisionMask = 1 << 9; orb.Player = null; orb.Game = null;
            cameraRig = Child("Camera controller").AddComponent<DCameraController>(); cameraRig.ManualSimulation = true;
            cameraRig.Player = player; cameraRig.Orb = orb;
            cameraRig.Lava = Child("Lava controller").AddComponent<DLavaController>();
            cameraRig.BaseOrthographicSize = source.BaseOrthographicSize;
            cameraRig.CameraHalfHeight = source.CameraHalfHeight; cameraRig.CameraTopMargin = source.CameraTopMargin;
            cameraRig.CameraFollowSpeed = source.CameraFollowSpeed; cameraRig.InitialCameraY = source.InitialCameraY;
            cameraRig.FollowLead = source.FollowLead; cameraRig.CameraSmoothTime = source.CameraSmoothTime;
            cameraRig.CameraDeadZone = source.CameraDeadZone; cameraRig.CameraCatchUpSpeed = source.CameraCatchUpSpeed;
            cameraRig.FallLookAhead = source.FallLookAhead; cameraRig.ZoomInSpeed = source.ZoomInSpeed;
            cameraRig.PlayerViewPadding = source.PlayerViewPadding; cameraRig.CameraSideMargin = source.CameraSideMargin;
            cameraRig.BoundaryClearance = source.BoundaryClearance;
            if (source.Lava.LavaBody)
            {
                var lava = UnityEngine.Object.Instantiate(source.Lava.LavaBody.gameObject);
                SceneManager.MoveGameObjectToScene(lava, testScene); lava.transform.SetParent(fixture.transform, true);
                cameraRig.Lava.LavaBody = lava.transform;
                normalLavaWidth = lava.GetComponent<SpriteRenderer>().bounds.size.x;
            }
            if (source.Lava.LavaSurface)
            {
                var lava = UnityEngine.Object.Instantiate(source.Lava.LavaSurface.gameObject);
                SceneManager.MoveGameObjectToScene(lava, testScene); lava.transform.SetParent(fixture.transform, true);
                cameraRig.Lava.LavaSurface = lava.transform;
            }
            cameraRig.ViewCamera = Child("Verification camera").AddComponent<Camera>();
            cameraRig.ViewCamera.orthographic = true; cameraRig.ViewCamera.aspect = 16f / 9f; cameraRig.ViewCamera.enabled = false;
            cameraRig.CameraBottom = Box("Bottom orb wall", Vector2.zero, new Vector2(31, .5f), 9).transform;
            cameraRig.CameraTop = Box("Top orb wall", Vector2.zero, new Vector2(31, .5f), 9).transform;
            cameraRig.CameraLeft = Box("Left orb wall", Vector2.zero, new Vector2(.5f, 18.4f), 9).transform;
            cameraRig.CameraRight = Box("Right orb wall", Vector2.zero, new Vector2(.5f, 18.4f), 9).transform;
            cameraRig.ResetCamera(); orb.ResetOrb(new Vector2(8, 5), new Vector2(-.85f, -.52f));
            for (int i = 0; i < 15; i++) Step(new DInputFrame(0, false, false, false, false, false, false));
            noWarp = constantSpeed = contained = playerVisible = geometryMatches = lavaMatches = true;
            maxStep = 0; maxSize = cameraRig.ViewCamera.orthographicSize; minCameraY = cameraRig.ViewCamera.transform.position.y;
        }

        static void Step(DInputFrame input)
        {
            player.SetInput(input); player.Step(Dt);
            Vector2 before = orb.Position; int bounces = orb.WallBounceCount;
            orb.Step(Dt);
            constantSpeed &= Mathf.Abs(orb.Velocity.magnitude - 8) < .0001f;
            if (orb.WallBounceCount == bounces)
                constantSpeed &= Mathf.Abs(Vector2.Distance(before, orb.Position) - 8 * Dt) < .0003f;
            Vector2 afterOrb = orb.Position;
            float previousY = cameraRig.ViewCamera.transform.position.y;
            cameraRig.FollowStep(Dt);
            noWarp &= orb.Position == afterOrb;
            physics.Simulate(Dt); Physics2D.SyncTransforms();
            noWarp &= Vector2.Distance(orb.Position, afterOrb) < .00001f;
            float center = cameraRig.ViewCamera.transform.position.y;
            maxStep = Mathf.Max(maxStep, Mathf.Abs(center - previousY));
            maxSize = Mathf.Max(maxSize, cameraRig.ViewCamera.orthographicSize); minCameraY = Mathf.Min(minCameraY, center);
            if (cameraRig.Lava.LavaBody && cameraRig.ViewCamera.orthographicSize > cameraRig.BaseOrthographicSize + .001f)
                lavaMatches &= cameraRig.Lava.LavaBody.GetComponent<SpriteRenderer>().bounds.size.x
                    >= 2 * cameraRig.ViewCamera.orthographicSize * cameraRig.ViewCamera.aspect + .99f;
            Rect area = cameraRig.VisiblePlayBounds;
            Bounds playerShape = player.BodyCollider.bounds;
            playerVisible &= playerShape.min.y >= area.yMin - .003f && playerShape.max.y <= area.yMax + .003f
                && playerShape.min.x >= area.xMin - .003f && playerShape.max.x <= area.xMax + .003f;
            contained &= orb.Position.x - orb.Radius >= area.xMin - .003f && orb.Position.x + orb.Radius <= area.xMax + .003f
                && orb.Position.y - orb.Radius >= area.yMin - .003f && orb.Position.y + orb.Radius <= area.yMax + .003f;
            Vector2 middle = area.center;
            float bottom = cameraRig.CameraBottom.GetComponent<BoxCollider2D>().ClosestPoint(middle).y;
            float top = cameraRig.CameraTop.GetComponent<BoxCollider2D>().ClosestPoint(middle).y;
            float left = cameraRig.CameraLeft.GetComponent<BoxCollider2D>().ClosestPoint(middle).x;
            float right = cameraRig.CameraRight.GetComponent<BoxCollider2D>().ClosestPoint(middle).x;
            geometryMatches &= Mathf.Abs(bottom - area.yMin) < .015f && Mathf.Abs(top - area.yMax) < .015f
                && Mathf.Abs(left - area.xMin) < .015f && Mathf.Abs(right - area.xMax) < .015f;
            contained &= orb.Position.y - orb.Radius >= bottom - .003f && orb.Position.y + orb.Radius <= top + .003f
                && orb.Position.x - orb.Radius >= left - .003f && orb.Position.x + orb.Radius <= right + .003f;
        }

        static void OrdinaryJumps()
        {
            NewFixture(); float minimum = cameraRig.ViewCamera.transform.position.y, maximum = minimum;
            for (int jump = 0; jump < 3; jump++)
            {
                Step(new DInputFrame(0, true, true, false, false, false, false));
                for (int i = 0; i < 120; i++)
                {
                    Step(new DInputFrame(0, false, i < 40, i == 40, false, false, false));
                    minimum = Mathf.Min(minimum, cameraRig.ViewCamera.transform.position.y);
                    maximum = Mathf.Max(maximum, cameraRig.ViewCamera.transform.position.y);
                }
            }
            Check(player.Grounded && playerVisible && contained, "Üç gerçek normal zıplamada karakter ve küre görünür kaldı.");
            Check(maxStep < .20f && maximum - minimum < 4.5f && maxSize < cameraRig.BaseOrthographicSize + 1.5f,
                "Kısa hareketler yumuşak; ani kamera sıçraması veya aşırı zoom yok. maxStep=" + maxStep.ToString("F4")
                + "; cameraRange=" + (maximum - minimum).ToString("F3") + "; maxOrtho=" + maxSize.ToString("F3"));
            Check(noWarp && constantSpeed && geometryMatches && orb.SolverLimitCount == 0,
                "Normal zıplamada küreye kamera/native fizik ek hareket vermedi; duvarlar görünüm geometrisiyle uyumlu.");
        }

        static void FastFallAndRecovery()
        {
            NewFixture();
            float initialCamera = cameraRig.ViewCamera.transform.position.y, fastestFall = 0;
            bool landingVisible = true; int nearLandingFrames = 0; bool landed = false;
            upperFloor.enabled = false; // An actual unsupported capsule now falls under its motor gravity.
            for (int i = 0; i < 300; i++)
            {
                Step(new DInputFrame(0, false, false, false, false, false, false));
                fastestFall = Mathf.Max(fastestFall, -player.Velocity.y);
                float feet = player.BodyCollider.bounds.min.y;
                if (player.Velocity.y < -1 && feet - landing.bounds.max.y <= cameraRig.FallLookAhead + cameraRig.PlayerViewPadding)
                {
                    nearLandingFrames++;
                    landingVisible &= landing.bounds.max.y >= cameraRig.VisiblePlayBounds.yMin - .003f;
                }
                if (player.Grounded && player.Position.y < -20) { landed = true; break; }
            }
            Check(landed && fastestFall >= 17.9f && minCameraY < initialCamera - 10,
                "Kamera aşağıdaki gerçek platforma 24m düşüşü takip etti; MaxFallSpeed'e ulaşıldı. speed="
                + fastestFall.ToString("F3") + "; minCameraY=" + minCameraY.ToString("F3"));
            Check(playerVisible && landingVisible && nearLandingFrames > 0 && maxSize > cameraRig.BaseOrthographicSize + .2f,
                "Hızlı düşüşte tüm karakter, küre ve yakın iniş alanı görünür; gerektiğinde geçici zoom oluştu. maxOrtho=" + maxSize.ToString("F3"));
            for (int i = 0; i < 1500; i++) Step(new DInputFrame(0, false, false, false, false, false, false));
            Check(cameraRig.ViewCamera.orthographicSize <= cameraRig.BaseOrthographicSize + .10f,
                "İnişten sonra kadraj uygunsa normal ortografik boyuta yumuşak dönüş. finalOrtho=" + cameraRig.ViewCamera.orthographicSize.ToString("F3"));
            Check(noWarp && constantSpeed && contained && geometryMatches && playerVisible && orb.SolverLimitCount == 0
                && maxStep <= cameraRig.CameraCatchUpSpeed * Dt + .0001f,
                "Düşüş ve toparlanma boyunca küre8 sabit, kamera küreyi taşımadı, duvar küreyi yutmadı, takip adımı sınırlı. maxStep=" + maxStep.ToString("F4"));
            Check(lavaMatches, "Zoom sırasında mevcut lav görseli yeni görünüm genişliğini kapladı.");
            orb.ResetOrb(new Vector2(8, 5), new Vector2(-.85f, -.52f));
            player.ResetAt(new Vector2(-6, .85f)); cameraRig.ResetCamera();
            Check(Mathf.Abs(cameraRig.ViewCamera.orthographicSize - cameraRig.BaseOrthographicSize) < .001f
                && Mathf.Abs(cameraRig.ViewCamera.transform.position.y - cameraRig.InitialCameraY) < .001f,
                "ResetCamera kamera konumunu, zoom'u ve takip belleğini sıfırlar.");
            Check(!cameraRig.Lava.LavaBody || Mathf.Abs(cameraRig.Lava.LavaBody.GetComponent<SpriteRenderer>().bounds.size.x - normalLavaWidth) < .01f,
                "ResetCamera lavın özgün genişliğini geri getirir; yeni görsel nesne oluşturulmaz.");
        }

        static void AspectChange()
        {
            NewFixture();
            for (int i = 0; i < 150; i++) Step(new DInputFrame(0, false, false, false, false, false, false));
            Vector2 before = orb.Position;
            cameraRig.ViewCamera.aspect = 4f / 3f; cameraRig.FollowStep(Dt);
            noWarp &= orb.Position == before;
            for (int i = 0; i < 200; i++) Step(new DInputFrame(0, false, false, false, false, false, false));
            Check(noWarp && contained && playerVisible && geometryMatches,
                "4:3 görünümde dört gerçek duvar yeni görünümle eşleşir; oyuncu/küre görünür ve küre yerinde kalır.");
        }

        static GameObject Child(string name)
        { var item = new GameObject(name); item.transform.SetParent(fixture.transform); return item; }
        static BoxCollider2D Box(string name, Vector2 position, Vector2 size, int layer)
        {
            var item = Child(name); item.layer = layer; item.transform.position = position;
            var box = item.AddComponent<BoxCollider2D>(); box.size = size; return box;
        }
        static void Check(bool passed, string label)
        { lines.Add("- " + (passed ? "PASS" : "FAIL") + " — " + label); if (!passed) FailureCount++; }
    }
}
#endif
