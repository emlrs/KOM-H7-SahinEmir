using System;
using UnityEngine;

namespace LavaRiseB
{
    public enum BPlatformKind { Stone, Metal, Narrow, Relay, Exit }

    [Serializable]
    public struct BPlatformSpec
    {
        public int Id;
        public float CenterX, TopY, Width;
        public BPlatformKind Kind;
        public string Label;
        public BPlatformSpec(int id, float x, float y, float width, BPlatformKind kind, string label)
        { Id = id; CenterX = x; TopY = y; Width = width; Kind = kind; Label = label; }
        public float Left => CenterX - Width * .5f;
        public float Right => CenterX + Width * .5f;
        public Vector2 StandingPosition => new Vector2(CenterX, TopY + .73f);
    }

    [Serializable]
    public struct BRouteStep
    {
        public int FromId, ToId;
        public float LaunchX, TargetX, JumpHold, Tolerance;
        public bool IsDrop;
        public string Purpose;
    }

    /// <summary>World-space authoring data. The route is a suggestion, never a platform-touch gate.</summary>
    public static class BCourseDefinition
    {
        public const float LavaStartY = -7.6f;
        public const float LavaSpeed = .46f;
        public const float FeetOffset = .71f;
        public const float DeathDuration = 1.5f;
        public static readonly Vector2 StartPosition = new Vector2(-14.5f, .74f);
        public static readonly BPlatformSpec[] Platforms =
        {
            P(0,-12,0,8,BPlatformKind.Stone,"BAŞLANGIÇ"),
            P(1,-3.8f,1.4f,6.6f,BPlatformKind.Stone,"GENİŞ İNİŞ"),
            P(2,4.4f,2.8f,6.6f,BPlatformKind.Stone,"HIZLAN"),
            P(3,12.6f,4.2f,6.6f,BPlatformKind.Stone,"KENARA KADAR"),
            P(4,18.5f,5.7f,4,BPlatformKind.Metal,"YÖN DEĞİŞTİR"),
            P(5,12,7.3f,5.6f,BPlatformKind.Metal,"KONTROLLÜ İNİŞ"),
            P(6,4,8.8f,6,BPlatformKind.Stone,"UZUN ADIM"),
            P(7,-4,10.3f,6,BPlatformKind.Stone,"SOL HAT"),
            P(8,-12,11.8f,6,BPlatformKind.Metal,"VALF 01  ↙"),
            P(9,-18.2f,10.2f,4.6f,BPlatformKind.Relay,"VALF 01 • İN VE GERİ DÖN"),
            P(10,-4,13.3f,6,BPlatformKind.Stone,"AÇILAN HAT"),
            P(11,4,14.8f,6,BPlatformKind.Stone,"YUKARI"),
            P(12,12,16.3f,6,BPlatformKind.Stone,"KOŞU ARALIĞI"),
            P(13,18,18,4,BPlatformKind.Metal,"DÖNÜŞ"),
            P(14,11.5f,19.5f,5.6f,BPlatformKind.Metal,"FRENLE"),
            P(15,3.5f,21,6,BPlatformKind.Stone,"SOL HAT"),
            P(16,-4.5f,22.5f,6,BPlatformKind.Stone,"AKIŞI KORU"),
            P(17,-12.5f,24,6,BPlatformKind.Stone,"YÜKSEL"),
            P(18,-19,25.5f,4.5f,BPlatformKind.Metal,"TERS YÖN"),
            P(19,-12.4f,27,5.6f,BPlatformKind.Metal,"SAĞ HAT"),
            P(20,-4.4f,28.4f,6,BPlatformKind.Stone,"KOŞU ARALIĞI"),
            P(21,3.6f,29.8f,6,BPlatformKind.Stone,"YÜKSEL"),
            P(22,11.6f,31.2f,6,BPlatformKind.Metal,"VALF 02  ↘"),
            P(23,17.8f,29.6f,4.6f,BPlatformKind.Relay,"VALF 02 • İN VE GERİ DÖN"),
            P(24,4.2f,32.7f,5.6f,BPlatformKind.Stone,"SON HAT"),
            P(25,-3.5f,34.2f,5.8f,BPlatformKind.Metal,"KISA BAS  ←"),
            P(26,-9.5f,34.2f,5,BPlatformKind.Metal,"ALÇAK GEÇİT"),
            P(27,-15.6f,35.9f,5,BPlatformKind.Stone,"BASILI TUT • YÜKSEK SIÇRA"),
            P(28,-9.6f,37.5f,2.2f,BPlatformKind.Narrow,"HASSAS FREN"),
            P(29,-3.6f,38.9f,5,BPlatformKind.Metal,"DENGE"),
            P(30,3.8f,40.4f,5.7f,BPlatformKind.Stone,"SON YÜKSELİŞ"),
            P(31,11.8f,41.9f,6,BPlatformKind.Stone,"ÇIKIŞA DOĞRU"),
            P(32,18.8f,43.6f,5,BPlatformKind.Exit,"ÇIKIŞ"),
        };
        static BPlatformSpec P(int id,float x,float y,float w,BPlatformKind k,string label) => new BPlatformSpec(id,x,y,w,k,label);
        public static BPlatformSpec Platform(int id) => Platforms[id];
        public static readonly int[] IntendedLandings = { 0,1,2,3,4,5,6,7,8,9,8,10,11,12,13,14,15,16,17,18,19,20,21,22,23,22,24,25,26,27,28,29,30,31,32 };
        public static readonly BRouteStep[] Route = MakeRoute();

        static BRouteStep[] MakeRoute()
        {
            var route = new BRouteStep[IntendedLandings.Length - 1];
            for (int i=0;i<route.Length;i++)
            {
                BPlatformSpec a=Platform(IntendedLandings[i]), b=Platform(IntendedLandings[i+1]);
                float direction=Mathf.Sign(b.CenterX-a.CenterX);
                bool drop=b.TopY<a.TopY-.1f;
                route[i]=new BRouteStep
                {
                    FromId=a.Id, ToId=b.Id,
                    LaunchX=a.CenterX+direction*(a.Width*.5f-.27f),
                    TargetX=b.Kind==BPlatformKind.Relay?b.CenterX:b.CenterX-direction*(b.Width*.5f-.42f),
                    JumpHold=drop?0:(a.Id==25?.045f:.28f),
                    IsDrop=drop, Tolerance=.30f,
                    Purpose=b.Label
                };
            }
            return route;
        }
    }
}
