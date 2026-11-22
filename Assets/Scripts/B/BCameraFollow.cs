using UnityEngine;

namespace LavaRiseB
{
    public class BCameraFollow : MonoBehaviour
    {
        public BPlayerMotor Target;
        public BGameController Game;
        [Min(.05f)] public float SmoothTime = .32f;
        public float AheadDistance = 2.7f;
        public float VerticalOffset = 2.2f;
        public bool ManualSimulation;
        Vector3 smoothing;

        void LateUpdate() { if (!ManualSimulation) TickFollow(Time.deltaTime); }

        public void Snap()
        {
            if (!Target) return;
            smoothing=Vector3.zero;
            transform.position=new Vector3(Target.transform.position.x, Target.transform.position.y+VerticalOffset, -10);
        }

        public void TickFollow(float dt)
        {
            if (!Target || dt<=0) return;
            Vector3 p=Target.transform.position;
            float horizontal=Mathf.Clamp(Target.Velocity.x/5.4f,-1,1)*AheadDistance;
            float vertical=VerticalOffset+Mathf.Clamp(Target.Velocity.y*.055f,-1.0f,.65f);
            // The lower relay is framed before stepping off. There is no upward-only camera clamp.
            if (Game && Game.Phase==BGamePhase.Playing)
            {
                int relay=Game.NextVisibleRelay(p);
                if (relay>=0)
                {
                    BPlatformSpec pad=BCourseDefinition.Platform(relay==0?9:23);
                    horizontal=Mathf.Lerp(horizontal,(pad.CenterX-p.x)*.48f,.7f);
                    vertical=.35f;
                }
            }
            Vector3 wanted=new Vector3(p.x+horizontal,p.y+vertical,-10);
            transform.position=Vector3.SmoothDamp(transform.position,wanted,ref smoothing,SmoothTime,Mathf.Infinity,dt);
        }
    }
}
