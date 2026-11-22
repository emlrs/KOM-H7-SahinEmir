using UnityEngine;
using UnityEngine.UI;

namespace CombinedD
{
    public class DHUDController : MonoBehaviour
    {
        public DPlayerMotor Player;
        public DLevelManager Level;
        public Text DoorLabel, HUDStats, HUDStatus, TerminalTitle, TerminalBody;
        public GameObject InstructionPanel, TerminalPanel;

        public void SetDoorOpen(bool open)
        {
            if (DoorLabel) DoorLabel.text = open ? "KAPI AÇIK" : "ALICI HAZIR";
        }

        public void UpdateHUD(DGameController game)
        {
            if (!game) return;
            DPlatform[] platforms = Level ? Level.Platforms : null;
            float finalHeight = platforms != null && platforms.Length > 0 && platforms[platforms.Length - 1]
                ? platforms[platforms.Length - 1].Surface.bounds.max.y : 0;
            float playerHeight = Player && Player.BodyCollider ? Mathf.Clamp(Player.BodyCollider.bounds.min.y, 0, finalHeight) : 0;
            float gap = Player && Player.BodyCollider ? Player.BodyCollider.bounds.min.y - game.LavaHeight : 0;
            if (HUDStats) HUDStats.text = $"KÜL KULESİ / D    YÜKSEKLİK {Mathf.Max(0, playerHeight):000} / {finalHeight:000} m    LAV MESAFESİ {Mathf.Max(0, gap):0.0} m    {Mathf.FloorToInt(game.Elapsed / 60):00}:{Mathf.FloorToInt(game.Elapsed % 60):00}";
            if (HUDStatus)
            {
                float pause = game.Attacks ? game.Attacks.PauseRemaining : 0;
                HUDStatus.text = game.DoorOpen ? "KAPI AÇIK  •  ÇIKIŞA ULAŞ" : pause > 0
                    ? $"ENERJİ DALGASI  {pause:0.0} sn  •  SALDIRILAR DURDU"
                    : "A / D  YÜRÜ     SPACE  ZIPLA     E  KISA BIRAK: PARRY  /  DURUP TUT: KALKAN";
                HUDStatus.color = pause > 0 || game.DoorOpen ? new Color(.40f, 1, .85f) : new Color(.79f, .83f, .80f);
            }
            if (InstructionPanel) InstructionPanel.SetActive(game.Phase == DGamePhase.Instruction);
            bool terminal = game.Phase == DGamePhase.Lost || game.Phase == DGamePhase.Won;
            if (TerminalPanel) TerminalPanel.SetActive(terminal);
            if (terminal)
            {
                bool won = game.Phase == DGamePhase.Won;
                if (TerminalTitle) TerminalTitle.text = won ? "KULEDEN ÇIKTIN" : "YENİDEN DENE";
                if (TerminalBody) TerminalBody.text = (won
                    ? $"Küre alıcıya ulaştı. Kapıdan geçtin.\nSüre {game.Elapsed:0.0} sn • Küre parry {game.OrbParries} • Füze parry {game.MissileParries}"
                    : "Lav, küre veya bir saldırı tırmanışı bitirdi.\nDur, yönünü seç, savun ve yükselmeye devam et.") + "\n\nR  •  YENİ KOŞU";
            }
        }
    }
}
