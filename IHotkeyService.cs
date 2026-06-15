using System;

namespace AltRunSharp
{
    public interface IHotkeyService
    {
        /// <summary>
        /// Register the global hotkey. clickMode: "single" | "double" | "triple".
        /// Works for both modifier-only keys (e.g. "Ctrl") and combos (e.g. "Alt+R").
        /// </summary>
        void RegisterGlobalHotkey(string keyCombination, string clickMode, Action callback);
        void StopHook();
        void HandleHotkeyMessage(int id);
    }
}
