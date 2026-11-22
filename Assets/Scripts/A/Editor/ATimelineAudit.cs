#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Measures the actual default director timeline without changing the scene.</summary>
public static class ATimelineAudit
{
    [MenuItem("Tek Tus/Zamanlama olcumunu yaz")]
    public static void Write()
    {
        var temporary = new GameObject("Temporary timeline measurement") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            DefenseGame game = temporary.AddComponent<DefenseGame>();
            game.AutoSchedule = true;
            game.BeginRun();
            DefenseSettings settings = game.Settings;
            AttackPlan[] plans = game.Director.PlannedAttacks.ToArray();
            AttackPlan[] missiles = plans.Where(p => p.Kind == AttackKind.Missile).OrderBy(p => p.ImpactTime).ToArray();
            AttackPlan[] lasers = plans.Where(p => p.Kind == AttackKind.Laser).OrderBy(p => p.WarningTime).ToArray();

            // The authored arena uses orthographic size 5.4 and a 16:9 target frame.
            const float visibleRightX = 5.4f * 16f / 9f;
            const float minimumVisibleApproach = 0.65f;
            float minimumLead = missiles.Min(p => VisibleLead(p, settings, visibleRightX));
            float minimumGap = (from missile in missiles from laser in lasers
                where laser.EndTime <= missile.ImpactTime select missile.ImpactTime - laser.EndTime).Min();
            float minimumSpacing = missiles.Zip(missiles.Skip(1), (a, b) => b.ImpactTime - a.ImpactTime).Min();
            int maximumConcurrent = lasers.Max(p => lasers.Count(l => l.StartTime <= p.StartTime && l.EndTime > p.StartTime));
            float firstFive = lasers.Where(p => lasers.Count(l => l.StartTime <= p.StartTime && l.EndTime > p.StartTime) == 5)
                .Select(p => p.StartTime).DefaultIfEmpty(-1f).Min();
            bool noImpactDuringLaser = missiles.All(m => lasers.All(l => m.ImpactTime < l.StartTime || m.ImpactTime >= l.EndTime));
            bool visibleApproachPass = missiles.All(m => VisibleLead(m, settings, visibleRightX) >= minimumVisibleApproach);
            bool safetyPass = noImpactDuringLaser && minimumGap >= settings.LaserToMissileSafetyGap - 0.0001f;

            var text = new StringBuilder();
            text.AppendLine("# Oyun A — varsayılan zaman çizelgesi ölçümü\n");
            text.AppendLine("Bu rapor Unity Editor içinde gerçek `AttackDirector` tarafından üretilen planın okunmasıyla oluşturulmuştur. " +
                "Sahne nesneleri değiştirilmez; ölçüm nesnesi işlem sonunda kaldırılır. Süreler oyun başlangıcından itibaren saniyedir.\n");
            text.AppendLine("Unity: " + Application.unityVersion + "; tohum: " + settings.PatternSeed + "; üretim: " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz") + ".\n");
            text.AppendLine("| Ölçüm | Gerçek değer |\n|---|---:|");
            Row(text, "Toplam roket", missiles.Length.ToString());
            Row(text, "Toplam lazer etkinleşmesi", lasers.Length.ToString());
            Row(text, "En çok eşzamanlı lazer", maximumConcurrent.ToString());
            Row(text, "İlk roket doğuşu / çarpması", F(missiles[0].LaunchTime) + " / " + F(missiles[0].ImpactTime));
            Row(text, "Son roket doğuşu / çarpması", F(missiles.Last().LaunchTime) + " / " + F(missiles.Last().ImpactTime));
            Row(text, "İlk / son gerçek roket hızı (birim/sn)", F(Speed(missiles[0], settings)) + " / " + F(Speed(missiles.Last(), settings)));
            Row(text, "En küçük ardışık roket çarpma aralığı", F(minimumSpacing));
            Row(text, "İlk lazer uyarısı / ateşi", F(lasers[0].WarningTime) + " / " + F(lasers[0].StartTime));
            Row(text, "Son lazer bitişi", F(lasers.Max(l => l.EndTime)));
            Row(text, "Beş lazerin ilk birlikte çalıştığı an", F(firstFive));
            Row(text, "En kısa / en uzun gerçek lazer uyarısı", F(lasers.Min(l => l.StartTime - l.WarningTime)) + " / " + F(lasers.Max(l => l.StartTime - l.WarningTime)));
            Row(text, "En kısa / en uzun gerçek lazer ateş süresi", F(lasers.Min(l => l.EndTime - l.StartTime)) + " / " + F(lasers.Max(l => l.EndTime - l.StartTime)));
            Row(text, "Ölçülen en küçük lazer → roket geçiş aralığı", F(minimumGap));
            Row(text, "16:9 görüntüde en kısa görünür roket yaklaşımı", F(minimumLead));
            text.AppendLine();
            text.AppendLine("- " + (safetyPass ? "PASS" : "FAIL") + " — Hiçbir roket aktif lazer sırasında çarpmıyor; son lazerden sonra en az " + F(settings.LaserToMissileSafetyGap) + " sn var.");
            text.AppendLine("- " + (visibleApproachPass ? "PASS" : "FAIL") + " — Her roket, 16:9 / 5.4 ortografik kamera çerçevesinde çarpmadan en az " + F(minimumVisibleApproach) + " sn önce görünür oluyor.");
            text.AppendLine("- " + (maximumConcurrent == 5 ? "PASS" : "FAIL") + " — Zorluk ilerledikçe beş bağımsız lazer aynı anda etkinleşiyor.\n");
            text.AppendLine("Görünür yaklaşım ölçümü, kamera sağ sınırı x = " + F(visibleRightX) + " ile roket merkezinin bu sınırı geçtiği zamanı kullanır. " +
                "Bu bir tasarım eşiği denetimidir; insan tepki süresi veya insanla oynanış testi iddiası değildir.\n");

            text.AppendLine("## Hesaplar\n");
            text.AppendLine("Roket hedef hızı = Lerp(7.4, 14.2, çarpmaZamanı / 90). Doğuş = çarpma − yolUzunluğu / hız; ilk 0.25 saniye öncesine doğuş konmaz. " +
                "Gerçek hız, doğuş ve çarpma arasındaki süre ile mesafeden yeniden ölçülür. Dalga içi aralık = Lerp(1.70, 0.72, dalgaBaşlangıcı / 90).\n");
            text.AppendLine("Lazer temel uyarısı = Lerp(1.20, 0.82, dalgaBaşlangıcı / 90); tek numaralı yuvalar 0.09 sn ek uyarı alır. " +
                "Temel ateş süresi = Lerp(1.30, 2.10, dalgaBaşlangıcı / 90); örüntüden 0–0.55 sn ve tohumdan 0–0.12 sn eklenir. " +
                "Hedefin sonuna taşan lazer 89.95 saniyede kesilir. Bir dalganın ilk roket çarpması = sonLazerBitişi + 0.42 + 0.18 sn.\n");

            text.AppendLine("## Bağımsız lazer dalgaları\n");
            text.AppendLine("| Dalga | Lazer | İlk uyarı | İlk ateş | Son bitiş | Sonraki roket | Geçiş aralığı |\n|---:|---:|---:|---:|---:|---:|---:|");
            var waves = new List<List<AttackPlan>>();
            foreach (AttackPlan laser in lasers)
            {
                if (waves.Count == 0 || laser.WarningTime > waves.Last().Max(l => l.EndTime)) waves.Add(new List<AttackPlan>());
                waves.Last().Add(laser);
            }
            for (int i = 0; i < waves.Count; i++)
            {
                List<AttackPlan> wave = waves[i];
                float end = wave.Max(l => l.EndTime);
                AttackPlan next = missiles.FirstOrDefault(m => m.ImpactTime > end);
                text.AppendLine("| " + (i + 1) + " | " + wave.Count + " | " + F(wave.Min(l => l.WarningTime)) + " | " + F(wave.Min(l => l.StartTime)) + " | " + F(end) + " | " +
                    (next == null ? "— | —" : F(next.ImpactTime) + " | " + F(next.ImpactTime - end)) + " |");
            }

            text.AppendLine("\n## Roketlerin görünür yaklaşım denetimi\n");
            text.AppendLine("| Roket | Doğuş | Çarpma | Gerçek hız | Görünür yaklaşım |\n|---:|---:|---:|---:|---:|");
            for (int i = 0; i < missiles.Length; i++)
            {
                AttackPlan missile = missiles[i];
                text.AppendLine("| " + (i + 1) + " | " + F(missile.LaunchTime) + " | " + F(missile.ImpactTime) + " | " + F(Speed(missile, settings)) + " | " + F(VisibleLead(missile, settings, visibleRightX)) + " |");
            }
            Directory.CreateDirectory("Belge");
            File.WriteAllText("Belge/A_Zamanlama_Olcumu.md", text.ToString(), new UTF8Encoding(false));
            Debug.Assert(safetyPass, "Timeline safety gap check failed.");
            Debug.Assert(visibleApproachPass, "Visible missile approach is shorter than the intended design threshold.");
            Debug.Log("A timeline audit written: missiles=" + missiles.Length + ", lasers=" + lasers.Length + ", minimum visible lead=" + F(minimumLead) + "s, minimum gap=" + F(minimumGap) + "s.");
        }
        finally { Object.DestroyImmediate(temporary); }
    }

    private static float Speed(AttackPlan missile, DefenseSettings settings)
    {
        float distance = Vector2.Distance(new Vector2(settings.MissileSpawnX, missile.OriginY), settings.ShieldPosition);
        return distance / (missile.ImpactTime - missile.LaunchTime);
    }

    private static float VisibleLead(AttackPlan missile, DefenseSettings settings, float rightEdge)
    {
        float fraction = Mathf.Clamp01((settings.MissileSpawnX - rightEdge) / (settings.MissileSpawnX - settings.ShieldX));
        return (missile.ImpactTime - missile.LaunchTime) * (1f - fraction);
    }

    private static string F(float value) => value.ToString("F3", CultureInfo.InvariantCulture);
    private static void Row(StringBuilder text, string label, string value) => text.AppendLine("| " + label + " | " + value + " |");
}
#endif
