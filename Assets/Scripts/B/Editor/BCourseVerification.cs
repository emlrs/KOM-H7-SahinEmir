using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace LavaRiseB
{
    /// <summary>Authoring audit only. Full completion must additionally use real motor inputs and Physics2D.</summary>
    public static class BCourseVerification
    {
        public static string Run()
        {
            var report=new StringBuilder("# B — Parkur Geometrisi\n\n");
            int drops=0;float travel=0,maxGap=0,maxRise=0;
            foreach(BRouteStep step in BCourseDefinition.Route)
            {
                var a=BCourseDefinition.Platform(step.FromId);var b=BCourseDefinition.Platform(step.ToId);
                float gap=Mathf.Max(0,Mathf.Abs(b.CenterX-a.CenterX)-(a.Width+b.Width)*.5f);
                float rise=b.TopY-a.TopY;
                if(step.IsDrop)drops++;
                travel+=Mathf.Abs(b.CenterX-a.CenterX);maxGap=Mathf.Max(maxGap,gap);maxRise=Mathf.Max(maxRise,rise);
                if(gap>2.51f)throw new Exception($"Unexpected long gap P{a.Id} to P{b.Id}: {gap:0.00}");
                if(rise>1.81f)throw new Exception($"Unexpected high rise P{a.Id} to P{b.Id}: {rise:0.00}");
                report.AppendLine($"- P{a.Id:00} → P{b.Id:00}: Δy {rise:+0.00;-0.00;0.00} m; clear gap {gap:0.00} m; launch {step.LaunchX:0.00}; target {step.TargetX:0.00}; hold {step.JumpHold:0.000} s{(step.IsDrop?"; mandatory lower valve":"")}");
            }
            if(drops<2)throw new Exception("Route needs two required descents.");
            report.AppendLine($"\n{BCourseDefinition.Platforms.Length} physical platforms / {BCourseDefinition.Route.Length} transitions / {drops} descents. Center-to-center horizontal travel {travel:0.0} m. Maximum edge gap {maxGap:0.00} m; maximum rise {maxRise:0.00} m.");
            report.AppendLine($"\nFixed lava formula: y = {BCourseDefinition.LavaStartY:0.00} + {BCourseDefinition.LavaSpeed:0.00} × elapsed. At 90 s: {BCourseDefinition.LavaStartY+90*BCourseDefinition.LavaSpeed:0.0} m; at 110 s: {BCourseDefinition.LavaStartY+110*BCourseDefinition.LavaSpeed:0.0} m. The finish platform is 43.6 m. This is an authoring audit, not proof of a completed run.");
            string path=Path.GetFullPath("Belge/B_Parkur_Geometri_Denetimi.md");Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,report.ToString());
            Debug.Log(report.ToString());return report.ToString();
        }
        public static void RunAll() { Run(); }
    }
}
