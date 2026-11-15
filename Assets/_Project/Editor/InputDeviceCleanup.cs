using System.Collections.Generic;
using UnityEditor;
using UnityEngine.InputSystem;

namespace StringAsASword.EditorTools
{
    /// <summary>
    /// The PlayMode tests turn the real keyboard off and type on a virtual one. A test run that is stopped halfway never
    /// turns the real keyboard back on, and the Editor keeps that state until it restarts, so no key reaches the game.
    /// Every time the Editor is back in edit mode this removes leftover virtual keyboards and turns the real ones on.
    /// </summary>
    [InitializeOnLoad]
    public static class InputDeviceCleanup
    {
        static InputDeviceCleanup()
        {
            EditorApplication.playModeStateChanged += change => { if (change == PlayModeStateChange.EnteredEditMode) Restore(); };
            if (!EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.delayCall += Restore;
        }

        [MenuItem("String As a Sword/Input/Restore Keyboard")]
        public static void Restore()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            foreach (var device in new List<InputDevice>(InputSystem.devices))
            {
                if (!(device is Keyboard)) continue;
                if (!device.native) InputSystem.RemoveDevice(device);
                else if (!device.enabled) InputSystem.EnableDevice(device);
            }
        }
    }
}
