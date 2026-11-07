using System;
using System.Collections.Generic;
using StringAsASword.Board;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace StringAsASword.Player
{
    /// <summary>Press and release edges. Event timestamps are converted into DSP time, not sampled at render time.</summary>
    public sealed class PlayerInputAdapter : MonoBehaviour
    {
        public readonly struct TimedAction
        {
            public PlayerActionKind Kind { get; }
            public MoveDirection Direction { get; }
            public double DspTime { get; }
            public double InputEventTime { get; }
            public bool IsRelease { get; }
            public TimedAction(PlayerActionKind kind, MoveDirection direction, double dspTime, double inputEventTime, bool isRelease = false)
            { Kind = kind; Direction = direction; DspTime = dspTime; InputEventTime = inputEventTime; IsRelease = isRelease; }
        }
        private readonly List<InputAction> actions = new List<InputAction>();
        private readonly List<TimedAction> requests = new List<TimedAction>();
        private readonly Queue<string> commands = new Queue<string>();
        public event Action<TimedAction> ActionPressed;
        /// <summary>Key went up. Only hold notes care.</summary>
        public event Action<TimedAction> ActionReleased;
        public event Action<string> CommandPressed;

        private void Awake()
        {
            BindMove("w", MoveDirection.Up); BindMove("a", MoveDirection.Left);
            BindMove("s", MoveDirection.Down); BindMove("d", MoveDirection.Right);
            BindGameplay("j", PlayerActionKind.Attack);
            BindGameplay("k", PlayerActionKind.Guard);
            BindCommand("space", "start"); BindCommand("enter", "start");
            BindCommand("r", "restart"); BindCommand("escape", "pause"); BindCommand("m", "mute");
        }
        private InputAction AddAction(string key)
        {
            var action = new InputAction(key, InputActionType.Button, $"<Keyboard>/{key}");
            actions.Add(action); return action;
        }
        private void BindMove(string key, MoveDirection direction)
            => BindGameplay(key, PlayerActionKind.Move, direction);
        private void BindGameplay(string key, PlayerActionKind kind, MoveDirection direction = MoveDirection.Up)
        {
            var action = AddAction(key);
            action.performed += context => requests.Add(new TimedAction(kind, direction, ToDsp(context.time), context.time));
            action.canceled += context => requests.Add(new TimedAction(kind, direction, ToDsp(context.time), context.time, true));
        }
        private static double ToDsp(double eventTime) => AudioSettings.dspTime + eventTime - InputState.currentTime;
        private void BindCommand(string key, string command) => AddAction(key).performed += _ => commands.Enqueue(command);
        public void Drain()
        {
            while (commands.Count > 0) CommandPressed?.Invoke(commands.Dequeue());
            // Original timestamps first. Releases before presses, then J -> K -> W -> A -> S -> D.
            requests.Sort((a, b) =>
            {
                int time = a.InputEventTime.CompareTo(b.InputEventTime);
                if (time != 0) return time;
                if (a.IsRelease != b.IsRelease) return a.IsRelease ? -1 : 1;
                int kind = a.Kind.CompareTo(b.Kind);
                return kind != 0 ? kind : a.Direction.CompareTo(b.Direction);
            });
            foreach (var request in requests)
                if (request.IsRelease) ActionReleased?.Invoke(request); else ActionPressed?.Invoke(request);
            requests.Clear();
        }
        public void Clear() { requests.Clear(); commands.Clear(); }
        private void OnEnable() { foreach (var action in actions) action.Enable(); }
        private void OnDisable() { foreach (var action in actions) action.Disable(); Clear(); }
        private void OnDestroy() { foreach (var action in actions) action.Dispose(); }
    }
}
