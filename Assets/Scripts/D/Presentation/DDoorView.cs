using UnityEngine;

namespace CombinedD
{
    public sealed class DDoorView : MonoBehaviour
    {
        public SpriteRenderer DoorLight;
        public TextMesh DoorWorldLabel;

        public void SetDoorOpen(bool open)
        {
            if (DoorLight) DoorLight.color = open ? new Color(.25f, .95f, .74f, .70f) : new Color(.51f, .31f, .18f, .75f);
            if (DoorWorldLabel) DoorWorldLabel.text = open ? "KAPI AÇIK\nÇIKIŞA YÜRÜ" : "ÇIKIŞ\nKÜREYİ ALICIYA YOLLA";
        }
    }
}
