#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LavaRiseB
{
    /// <summary>Actual Unity 2D physics traversal. No position changes between start and exit.</summary>
    public static class BRouteVerification
    {
        [Serializable] public class Frame { public float Move; public bool Press, Held, Release; }
        [Serializable] public class Visit { public int Platform; public float Time, X, Y, LavaGap; }
        [Serializable] public class RunRecord
        {
            public int Profile; public bool Passed; public float Time, DecisionPause; public string Detail;
            public List<Frame> Frames=new List<Frame>(); public List<Visit> Visits=new List<Visit>();
        }
        [Serializable] public class Options
        {
            public float LandingInset=1.15f;
            public float BrakeFactor=1.0f;
            public float LaunchOffset=0f;
            public float SettleSpeed=.5f;
            public float DecisionPause=0f;
        }
        static BGameController game;
        static BPlayerMotor player;
        static SimulationMode2D previousSimulation;
        static RigidbodyInterpolation2D previousInterpolation;
        static readonly string Folder=Path.GetFullPath("Temp/B_HareketHissi");
        static readonly string Evidence=Path.GetFullPath("Belge/B_TestKaniti");
        static readonly List<string> errors=new List<string>();
        static RunRecord replay;
        static int replayIndex;
        static double wallStart,lastWall;
        static float accumulator;
        static bool replaying;
        static bool shotRun,shotDrop,shotHigh;
        const float Dt=.02f;

        public static void Begin()
        {
            if(!EditorApplication.isPlaying)throw new InvalidOperationException("Play Mode required.");
            if(replaying)throw new InvalidOperationException("Replay already active.");
            Directory.CreateDirectory(Folder);Directory.CreateDirectory(Evidence);
            File.WriteAllText(Path.Combine(Folder,"route-status.txt"),"RUNNING");
            var options=File.Exists(Path.Combine(Folder,"route-options.json"))?JsonUtility.FromJson<Options>(File.ReadAllText(Path.Combine(Folder,"route-options.json"))):new Options();
            errors.Clear();Application.logMessageReceived+=OnLog;
            var runs=new List<RunRecord>();
            try
            {
                Bind();
                for(int profile=0;profile<2;profile++)
                {
                    Prepare(profile);
                    var pilot=new Pilot(game,options);
                    var record=new RunRecord{Profile=profile,DecisionPause=options.DecisionPause};
                    for(int i=0;i<8500 && game.Phase==BGamePhase.Playing;i++)
                    {
                        Frame frame=pilot.Input(Dt,record.Visits);
                        record.Frames.Add(frame);Step(frame);
                        if(pilot.Failed){record.Detail=pilot.Failure;break;}
                    }
                    record.Time=game.Elapsed;
                    record.Passed=game.Phase==BGamePhase.Won && game.RelayMask==3;
                    if(record.Detail==null)record.Detail="phase="+game.Phase+", step="+pilot.StepIndex+", position="+player.Body.position+", relays="+game.RelayMask;
                    if(record.Passed)File.WriteAllText(Path.Combine(Folder,"route-tape-"+profile+".json"),JsonUtility.ToJson(record));
                    File.WriteAllText(Path.Combine(Folder,"route-last-"+profile+".json"),JsonUtility.ToJson(record));
                    runs.Add(record);
                }
                WriteReport(runs);
            }
            finally {Application.logMessageReceived-=OnLog;Restore();}
        }

        static void Bind()
        {
            game=UnityEngine.Object.FindAnyObjectByType<BGameController>();
            if(!game)throw new InvalidOperationException("B scene is not active.");
            player=game.Player;
            previousSimulation=Physics2D.simulationMode;
            previousInterpolation=player.Body.interpolation;
        }
        static void Prepare(int profile)
        {
            Physics2D.simulationMode=SimulationMode2D.Script;
            game.ManualSimulation=true;player.ManualSimulation=true;player.UseExternalInput=true;
            if(game.FollowCamera)game.FollowCamera.ManualSimulation=true;
            game.RestartRun();player.ResetAt(BCourseDefinition.StartPosition,profile);
            player.Body.interpolation=RigidbodyInterpolation2D.None;
            player.InputEnabled=true;
            Physics2D.SyncTransforms();
        }
        static void Restore()
        {
            Physics2D.simulationMode=previousSimulation;
            if(!game||!player)return;
            game.ManualSimulation=false;player.ManualSimulation=false;player.UseExternalInput=false;
            player.Body.interpolation=previousInterpolation;
            if(game.FollowCamera)game.FollowCamera.ManualSimulation=false;
        }
        static void Step(Frame frame)
        {
            player.SetInput(new BInputFrame(frame.Move,frame.Press,frame.Held,frame.Release,false));
            player.ManualFixedStep(Dt);
            Physics2D.Simulate(Dt);
            game.TickRun(Dt);
            if(game.FollowCamera)game.FollowCamera.TickFollow(Dt);
        }
        static void OnLog(string message,string stack,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message);}

        static void WriteReport(List<RunRecord> runs)
        {
            var text=new StringBuilder("# B — Tam rota: gerçek Unity 2D fizik testi\n\n");
            text.AppendLine("Çalıştırma: "+DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")+"; Unity "+Application.unityVersion+".\n");
            text.AppendLine("Play Mode içinde aynı motor adımı ve gerçek Physics2D.Simulate(0.02) kullanıldı. Başlangıç sıfırlaması dışında karakter konumu değiştirilmedi. Girdiler yalnızca -1/0/+1 yatay yön ile Space bas/tut/bırak durumlarıdır. Süreler simülasyon oyun süresidir; hesaplar hızlandırılmıştır. Bir üst platforma ışınlanma veya fizik yerine kinematik rota çizimi yapılmamıştır.\n");
            foreach(var r in runs)
            {
                text.AppendLine("## "+(r.Profile==0?"Akış":"Denge"));
                text.AppendLine("\n"+(r.Passed?"PASS":"FAIL")+" — "+r.Time.ToString("F2")+" sn; "+r.Detail+"\n");
                text.AppendLine("Giriş pilotunun her güvenli inişten sonraki karar beklemesi: "+r.DecisionPause.ToString("F2")+" sn. Bu süre oyunun zorunlu beklemesi değildir; test girdisinin parçasıdır. İnsan koşusu ölçümü değildir.\n");
                text.AppendLine("| İniş platformu | Oyun süresi | X | Ayak yüksekliği | Lav payı |\n|---:|---:|---:|---:|---:|");
                foreach(var v in r.Visits)text.AppendLine("| "+v.Platform+" | "+v.Time.ToString("F2")+" | "+v.X.ToString("F2")+" | "+v.Y.ToString("F2")+" | "+v.LavaGap.ToString("F2")+" |");
                text.AppendLine();
            }
            bool pass=runs.Count==2&&runs.TrueForAll(r=>r.Passed)&&errors.Count==0;
            text.AppendLine("Runtime Error/Exception: "+errors.Count+".\n\nGenel sonuç: "+(pass?"PASS":"FAIL"));
            foreach(var error in errors)text.AppendLine(error);
            File.WriteAllText("Belge/B_TamRota_Test_Raporu.md",text.ToString());
            File.WriteAllText(Path.Combine(Folder,"route-status.txt"),(pass?"PASS":"FAIL")+"\n"+string.Join("\n",runs.ConvertAll(r=>(r.Profile==0?"Akış":"Denge")+": "+r.Time.ToString("F2")+" "+r.Detail)));
            Debug.Log("B full physics route: "+(pass?"PASS":"FAIL"));
        }

        public static void Replay()
        {
            if(!EditorApplication.isPlaying || replaying)throw new InvalidOperationException("Play Mode required; replay must be idle.");
            int profile=File.Exists(Path.Combine(Folder,"replay-profile.txt"))?int.Parse(File.ReadAllText(Path.Combine(Folder,"replay-profile.txt")).Trim()):0;
            replay=JsonUtility.FromJson<RunRecord>(File.ReadAllText(Path.Combine(Folder,"route-tape-"+profile+".json")));
            Bind();Prepare(profile);replayIndex=0;accumulator=0;shotRun=shotDrop=shotHigh=false;
            errors.Clear();Application.logMessageReceived+=OnLog;
            wallStart=lastWall=EditorApplication.timeSinceStartup;replaying=true;
            File.WriteAllText(Path.Combine(Folder,"replay-status.txt"),"RUNNING profile="+profile);
            EditorApplication.update+=Pump;
        }
        static void Pump()
        {
            if(!EditorApplication.isPlaying || !game){FinishReplay(false);return;}
            double now=EditorApplication.timeSinceStartup;
            accumulator+=Mathf.Min(.1f,(float)(now-lastWall));lastWall=now;
            while(accumulator>=Dt && replayIndex<replay.Frames.Count && game.Phase==BGamePhase.Playing)
            {accumulator-=Dt;Step(replay.Frames[replayIndex++]);}
            string prefix=replay.Profile==0?"Akis":"Denge";
            if(!shotRun && game.Elapsed>2){shotRun=true;Shot(prefix+"_01_kosu");}
            if(!shotDrop && game.RelayMask==1){shotDrop=true;Shot(prefix+"_02_alt_valf");}
            if(!shotHigh && game.Height>34){shotHigh=true;Shot(prefix+"_03_ust_parkur");}
            if(game.Phase!=BGamePhase.Playing || replayIndex>=replay.Frames.Count)FinishReplay(game.Phase==BGamePhase.Won&&game.RelayMask==3&&errors.Count==0);
        }
        static void Shot(string name)=>ScreenCapture.CaptureScreenshot(Path.Combine(Evidence,name+".png"));
        static void FinishReplay(bool passed)
        {
            EditorApplication.update-=Pump;Application.logMessageReceived-=OnLog;replaying=false;
            string prefix=replay.Profile==0?"Akis":"Denge";
            string report=(passed?"PASS":"FAIL")+" — "+prefix+" fizik tekrarı\nOyun süresi="+(game?game.Elapsed:0).ToString("F2")+"\nDuvar süresi="+(EditorApplication.timeSinceStartup-wallStart).ToString("F2")+"\nRuntime errors="+errors.Count+"\n"+
                "Önceden gerçek fizikle tamamlanan giriş dizisi gerçek zaman ölçeğinde Physics2D.Simulate ile tekrar oynatıldı. Kontrol kodu, çarpışmalar, lav ve çıkış koşulları aynıdır; teleport kullanılmadı.\n"+
                "Her güvenli inişte test pilotunun karar beklemesi="+replay.DecisionPause.ToString("F2")+" sn; oyun bunu zorunlu kılmaz. İnsan koşusu değildir.\n";
            Shot(prefix+"_04_sonuc");
            File.WriteAllText("Belge/B_"+prefix+"_GercekZaman_Test.txt",report);
            File.WriteAllText(Path.Combine(Folder,"replay-status.txt"),report);Restore();
        }

        sealed class Pilot
        {
            readonly BGameController game;readonly BPlayerMotor motor;readonly Options options;
            bool launched,settling,previousHeld;float airAge,stepAge,decisionAge;
            public int StepIndex {get;private set;}
            public bool Failed{get;private set;}
            public string Failure{get;private set;}
            public Pilot(BGameController game,Options options){this.game=game;motor=game.Player;this.options=options;}
            public Frame Input(float dt,List<Visit> visits)
            {
                if(StepIndex>=BCourseDefinition.Route.Length)return Make(Mathf.Sign(BCourseDefinition.Platform(32).CenterX+.65f-motor.Body.position.x),false,false);
                BRouteStep step=BCourseDefinition.Route[StepIndex];
                BPlatformSpec from=BCourseDefinition.Platform(step.FromId),to=BCourseDefinition.Platform(step.ToId);
                Vector2 p=motor.Body.position,v=motor.Velocity;
                float feet=p.y-BCourseDefinition.FeetOffset;
                float direction=Mathf.Sign(to.CenterX-from.CenterX);
                float landing=to.Kind==BPlatformKind.Relay||to.Kind==BPlatformKind.Narrow?to.CenterX:
                    to.Kind==BPlatformKind.Exit?to.CenterX+.65f:to.CenterX-direction*(to.Width*.5f-options.LandingInset);
                stepAge+=dt;
                bool onTarget=motor.Grounded && Mathf.Abs(feet-to.TopY)<.16f && p.x>to.Left-.12f && p.x<to.Right+.12f;
                if((launched||step.IsDrop) && onTarget)settling=true;
                if(settling)
                {
                    bool relayReady=to.Kind!=BPlatformKind.Relay || (game.RelayMask&(to.Id==9?1:2))!=0;
                    bool reached=to.Kind==BPlatformKind.Relay?Mathf.Abs(p.x-landing)<.9f:true;
                    if(onTarget && relayReady && reached && Mathf.Abs(v.x)<options.SettleSpeed)
                    {
                        decisionAge+=dt;
                        if(decisionAge<options.DecisionPause)return Make(0,false,false);
                        visits.Add(new Visit{Platform=to.Id,Time=game.Elapsed,X=p.x,Y=feet,LavaGap=feet-game.LavaY});
                        StepIndex++;launched=settling=false;airAge=stepAge=decisionAge=0;
                        return Make(0,false,false);
                    }
                    return Make(Steer(landing,p.x,v.x,true),false,false);
                }
                if(stepAge>10 || feet<Mathf.Min(from.TopY,to.TopY)-2.2f)
                {Failed=true;Failure="stuck/fell at edge "+step.FromId+"→"+step.ToId+" pos="+p+" velocity="+v+" grounded="+motor.Grounded;return Make(0,false,false);}
                if(step.IsDrop)
                {
                    if(!motor.Grounded) {launched=true;airAge+=dt;}
                    return Make(launched?Steer(landing,p.x,v.x,false):direction,false,false);
                }
                if(!launched)
                {
                    float launch=step.LaunchX+direction*options.LaunchOffset;
                    if(direction*(p.x-launch)>=-.015f)
                    {launched=true;airAge=0;return Make(direction,true,true);}
                    return Make(direction,false,false);
                }
                airAge+=dt;
                bool held=airAge<step.JumpHold;
                return Make(Steer(landing,p.x,v.x,false),false,held);
            }
            float Steer(float target,float position,float speed,bool grounded)
            {
                float error=target-position;
                var profile=motor.CurrentProfile;
                float deceleration=profile.CounterSteer*(grounded?1:profile.AirControl);
                float stopping=speed*speed/(2*Mathf.Max(.1f,deceleration))*options.BrakeFactor;
                if(Mathf.Abs(speed)>.18f && Mathf.Sign(speed)==Mathf.Sign(error) && Mathf.Abs(error)<=stopping+.07f)return -Mathf.Sign(speed);
                if(Mathf.Abs(error)<.12f)return Mathf.Abs(speed)>.4f?-Mathf.Sign(speed):0;
                return Mathf.Sign(error);
            }
            Frame Make(float axis,bool press,bool held)
            {
                var frame=new Frame{Move=axis,Press=press,Held=held,Release=previousHeld&&!held};previousHeld=held;return frame;
            }
        }
    }
}
#endif
