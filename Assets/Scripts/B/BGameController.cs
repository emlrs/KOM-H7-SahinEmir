using UnityEngine;
using UnityEngine.InputSystem;

namespace LavaRiseB
{
    public enum BGamePhase { Instruction, Playing, Dying, Lost, Won }

    public class BGameController : MonoBehaviour
    {
        public BPlayerMotor Player;
        public BEnvironmentView Environment;
        public BCameraFollow FollowCamera;
        [Tooltip("Fixed world-space lava speed. Never adjusted for profile or player progress.")]
        [Min(.01f)] public float FixedLavaSpeed=BCourseDefinition.LavaSpeed;
        public bool ManualSimulation;
        public BGamePhase Phase { get; private set; }=BGamePhase.Instruction;
        public float Elapsed { get; private set; }
        public float BElapsed => Elapsed;
        public float LavaY { get; private set; }=BCourseDefinition.LavaStartY;
        public int RelayMask { get; private set; }
        public float Height => Player?Mathf.Max(0,Player.Body.position.y-BCourseDefinition.FeetOffset):0;
        public float DeathElapsed { get; private set; }
        public int RunSerial { get; private set; }
        public bool StartJumpConsumed { get; private set; }
        int startProfile=0;
        GUIStyle small, body, heading, large, micro;
        Texture2D white;

        public void Configure(BPlayerMotor player,BEnvironmentView environment,BCameraFollow follow)
        {
            Player=player; Environment=environment; FollowCamera=follow;
            if (Player) { Player.Died-=OnPlayerDied; Player.Died+=OnPlayerDied; }
            if (FollowCamera) { FollowCamera.Target=Player; FollowCamera.Game=this; }
            ResetState(false);
        }

        void Awake()
        {
            if (Player) { Player.Died-=OnPlayerDied; Player.Died+=OnPlayerDied; }
            ResetState(false);
        }
        void OnDestroy() { if(Player) Player.Died-=OnPlayerDied; }
        void Update()
        {
            if(ManualSimulation) return;
            Keyboard k=Keyboard.current;
            if(k!=null)
            {
                if(k.rKey.wasPressedThisFrame && (Phase==BGamePhase.Lost || Phase==BGamePhase.Won)) { RestartRun(); return; }
                if(Phase==BGamePhase.Instruction && k.spaceKey.wasPressedThisFrame) BeginRun();
                if(StartJumpConsumed && !k.spaceKey.isPressed)
                {
                    StartJumpConsumed=false;
                    if(Phase==BGamePhase.Playing && Player) Player.InputEnabled=true;
                }
            }
            TickRun(Time.deltaTime);
        }

        public void BeginRun()
        {
            if(Phase!=BGamePhase.Instruction) return;
            Phase=BGamePhase.Playing;
            StartJumpConsumed=Keyboard.current!=null && Keyboard.current.spaceKey.isPressed && !ManualSimulation;
            if(Player) Player.InputEnabled=!StartJumpConsumed;
        }

        public void RestartRun() { ResetState(true); }

        void ResetState(bool startImmediately)
        {
            Elapsed=0; LavaY=BCourseDefinition.LavaStartY; RelayMask=0; DeathElapsed=0;
            RunSerial++; StartJumpConsumed=false;
            Phase=startImmediately?BGamePhase.Playing:BGamePhase.Instruction;
            if(Player)
            {
                Player.ResetAt(BCourseDefinition.StartPosition,startProfile);
                Player.InputEnabled=startImmediately;
            }
            if(Environment) Environment.ResetWorld(LavaY);
            if(FollowCamera) FollowCamera.Snap();
        }

        public void TickRun(float dt)
        {
            if(dt<=0) return;
            if(Phase==BGamePhase.Playing)
            {
                Elapsed+=dt;
                LavaY=BCourseDefinition.LavaStartY+FixedLavaSpeed*Elapsed;
                if(Environment) Environment.SetLavaHeight(LavaY);
                if(Player && Player.IsAlive)
                {
                    if(Player.Body.position.y-BCourseDefinition.FeetOffset<=LavaY) ContactLava();
                    else
                    {
                        ActivateRelay(0); ActivateRelay(1); TryExit();
                    }
                }
            }
            else if(Phase==BGamePhase.Dying)
            {
                DeathElapsed+=dt;
                if(DeathElapsed>=BCourseDefinition.DeathDuration) Phase=BGamePhase.Lost;
            }
            if(Environment) Environment.TickVisual(dt,Phase==BGamePhase.Playing);
        }

        public void ContactLava()
        {
            if(Phase!=BGamePhase.Playing || !Player || !Player.IsAlive) return;
            Player.Kill();
        }
        void OnPlayerDied()
        {
            if(Phase!=BGamePhase.Playing) return;
            Phase=BGamePhase.Dying; DeathElapsed=0;
            Player.InputEnabled=false;
            if(Environment) Environment.PlayExplosion(Player.Body.position);
        }

        public bool ActivateRelay(int relay)
        {
            if(Phase!=BGamePhase.Playing || !Player || relay<0 || relay>1) return false;
            if((RelayMask&(1<<relay))!=0) return true;
            BPlatformSpec pad=BCourseDefinition.Platform(relay==0?9:23);
            Vector2 p=Player.Body.position;
            // A visible sensor on the lower pad must be reached by the actual body.
            if(Mathf.Abs(p.x-pad.CenterX)>1.15f || p.y<pad.TopY+.3f || p.y>pad.TopY+1.75f) return false;
            RelayMask|=1<<relay;
            if(Environment) Environment.ActivateRelay(relay);
            return true;
        }

        public bool TryExit()
        {
            if(Phase!=BGamePhase.Playing || RelayMask!=3 || !Player || !Player.IsAlive) return false;
            Vector2 p=Player.Body.position;
            BPlatformSpec exit=BCourseDefinition.Platform(32);
            if(Mathf.Abs(p.x-(exit.CenterX+.65f))>1.0f || p.y<exit.TopY+.3f || p.y>exit.TopY+2.8f) return false;
            if(Environment && Environment.ExitSensor && !Environment.ExitSensor.bounds.Intersects(Player.GetComponent<Collider2D>().bounds)) return false;
            Phase=BGamePhase.Won; Player.InputEnabled=false;
            Player.Body.linearVelocity=Vector2.zero; Player.Body.simulated=false;
            if(Environment) Environment.ShowExitComplete();
            return true;
        }

        public int NextVisibleRelay(Vector3 position)
        {
            for(int r=0;r<2;r++)
            {
                if((RelayMask&(1<<r))!=0) continue;
                BPlatformSpec launch=BCourseDefinition.Platform(r==0?8:22);
                if(Mathf.Abs(position.x-launch.CenterX)<4.4f && Mathf.Abs(position.y-launch.TopY-.71f)<2.2f) return r;
            }
            return -1;
        }

        void EnsureGUI()
        {
            if(white) return;
            white=Texture2D.whiteTexture;
            var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            small=new GUIStyle(GUI.skin.label){font=font,fontSize=17,normal={textColor=new Color(.82f,.82f,.77f)}};
            body=new GUIStyle(small){fontSize=22,wordWrap=true};
            heading=new GUIStyle(small){fontSize=34,fontStyle=FontStyle.Bold};
            large=new GUIStyle(heading){fontSize=54};
            micro=new GUIStyle(small){fontSize=13};
        }
        void Fill(Rect r,Color c) { Color old=GUI.color; GUI.color=c; GUI.DrawTexture(r,white); GUI.color=old; }
        void Label(Rect r,string text,GUIStyle style,Color? tint=null)
        { Color old=GUI.color; if(tint.HasValue)GUI.color=tint.Value; GUI.Label(r,text,style); GUI.color=old; }
        static readonly Color Gold=new Color(1,.76f,.35f);
        static readonly Color Teal=new Color(.38f,.85f,.74f);
        void OnGUI()
        {
            if(!Player) return;
            EnsureGUI();
            float s=Mathf.Min(Screen.width/1440f,Screen.height/900f);
            float w=Screen.width/s,h=Screen.height/s;
            Matrix4x4 previous=GUI.matrix;
            GUI.matrix=Matrix4x4.Scale(new Vector3(s,s,1));
            Fill(new Rect(0,0,w,104),new Color(.035f,.042f,.047f,.94f));
            Fill(new Rect(32,27,4,51),Gold);
            Label(new Rect(50,21,270,33),"KÜL KULESİ",heading);
            Label(new Rect(52,61,330,24),"B / HAREKET HİSSİ     •     YÜKSELEN LAV",micro);
            Label(new Rect(w*.38f,25,220,28),$"SÜRE  {Elapsed:000.0} sn",body);
            Label(new Rect(w*.38f,61,260,26),$"YÜKSEKLİK  {Height:0.0} / 44 m",small);
            Label(new Rect(w*.59f,25,220,28),$"VALFLER  {((RelayMask&1)!=0?1:0)+((RelayMask&2)!=0?1:0)} / 2",body,Teal);
            Label(new Rect(w*.59f,61,270,24),"İki alt valfi aç • Tepedeki kapıya ulaş",micro);
            Fill(new Rect(w-264,19,232,64),new Color(.14f,.17f,.17f));
            Label(new Rect(w-246,25,210,28),Player.ProfileIndex==0?"01  AKIŞ":"02  DENGE",body,Player.CurrentProfile.Color);
            Label(new Rect(w-245,57,210,24),Player.ProfileIndex==0?"Hızlı koşu • uzun fren":"Güçlü fren • hassas yön",micro);

            float gap=Mathf.Max(0,Player.Body.position.y-BCourseDefinition.FeetOffset-LavaY);
            bool danger=gap<3.0f;
            Fill(new Rect(32,h-92,335,59),new Color(.06f,.065f,.065f,.91f));
            Fill(new Rect(32,h-92,4,59),danger?new Color(1,.25f,.12f):Gold);
            Label(new Rect(48,h-85,312,27),$"LAV  ↓  {gap:0.0} m aşağıda",body,danger?new Color(1,.4f,.25f):Gold);
            Label(new Rect(49,h-58,300,22),$"SABİT YÜKSELİŞ  +{FixedLavaSpeed:0.00} m/sn",micro);
            Fill(new Rect(w-574,h-74,542,41),new Color(.06f,.065f,.065f,.88f));
            Label(new Rect(w-558,h-66,520,30),"A / D   hareket     BOŞLUK   sıçra     E   profil",small);

            if(Phase==BGamePhase.Instruction) DrawCard(w,h,"KÜL KULESİ","Lav yükselmeden ritmini bul.",false);
            else if(Phase==BGamePhase.Lost) DrawCard(w,h,"LAV YAKALADI",$"{Elapsed:0.0} saniyede {Height:0.0} metreye ulaştın.",true);
            else if(Phase==BGamePhase.Won) DrawCard(w,h,"ÇIKIŞA ULAŞTIN",$"İki valf açık. Kule {Elapsed:0.0} saniyede aşıldı.",true);
            GUI.matrix=previous;
        }

        void DrawCard(float w,float h,string title,string subtitle,bool finished)
        {
            Fill(new Rect(0,104,w,h-104),new Color(.015f,.022f,.027f,.73f));
            float x=(w-830)*.5f,y=(h-536)*.5f;
            Fill(new Rect(x,y,830,536),new Color(.075f,.088f,.091f,.98f));
            Fill(new Rect(x,y,830,5),Phase==BGamePhase.Won?Teal:Gold);
            Label(new Rect(x+45,y+32,740,64),title,large,Phase==BGamePhase.Won?Teal:Gold);
            Label(new Rect(x+48,y+104,735,40),subtitle,body);
            if(!finished)
            {
                Label(new Rect(x+48,y+169,735,42),"A / D veya ← / →       Yürü, hızlan ve ters yöne frenle",body);
                Label(new Rect(x+48,y+218,735,65),"BOŞLUK       Kısa bas: alçak sıçra   /   Basılı tut: yüksek sıçra",body);
                Label(new Rect(x+48,y+284,735,44),"E       Akış ↔ Denge; profil koşarken de değişir",body);
                Label(new Rect(x+48,y+339,735,64),"Turkuaz valflere inip geri tırman. İkisi de açılınca tepedeki fiziksel çıkıştan geç. Lav her saniye aynı hızla yükselir.",body);
                Fill(new Rect(x+48,y+436,734,56),new Color(.22f,.19f,.12f));
                Label(new Rect(x+72,y+447,690,40),"BAŞLAMAK İÇİN BOŞLUK   •   Sonra bırak ve yeniden bas",body,Gold);
            }
            else
            {
                Label(new Rect(x+48,y+184,735,56),"Her denemede başlangıç, lav, süre ve iki valf sıfırlanır.",body);
                Label(new Rect(x+48,y+258,735,62),Phase==BGamePhase.Won?"Akış profilinde momentumu taşı; Denge profilinde dar inişleri dene.":"Lavla temas anında hareket kesilir. İnişte ters yönü kullan; geniş platformlarda sonraki sıçrayışa hazırlan.",body);
                Fill(new Rect(x+48,y+407,734,64),new Color(.22f,.19f,.12f));
                Label(new Rect(x+74,y+420,690,42),"R   YENİDEN BAŞLA",heading,Gold);
            }
        }
    }
}
