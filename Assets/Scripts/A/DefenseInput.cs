using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>The only input adapter: E is sampled every Update, including key edges.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(DefenseGame))]
public sealed class DefenseInput : MonoBehaviour
{
    public DefenseGame Game;

    private void Awake()
    {
        if (Game == null) Game = GetComponent<DefenseGame>();
    }

    private void Update()
    {
        if (Game == null) return;
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            Game.Tick(Time.deltaTime, false, false, false);
            return;
        }

        var key = keyboard.eKey;
        Game.Tick(Time.deltaTime, key.wasPressedThisFrame, key.isPressed, key.wasReleasedThisFrame);
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus && Game != null) Game.CancelDefense();
    }

    private void OnDisable()
    {
        if (Game != null) Game.CancelDefense();
    }
}
