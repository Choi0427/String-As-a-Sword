using System;
using System.Collections.Generic;
using StringAsASword.Board;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace StringAsASword.Player
{
    /// <summary>Press edges only. Event timestamps are converted into DSP time, not sampled at render time.</summary>
    public sealed class PlayerInputAdapter : MonoBehaviour
    {
        public readonly struct TimedMove
        {
            public MoveDirection Direction { get; }
            public double DspTime { get; }
            public double InputEventTime { get; }
            public TimedMove(MoveDirection direction, double dspTime, double inputEventTime)
            { Direction = direction; DspTime = dspTime; InputEventTime = inputEventTime; }
        }
        private readonly List<InputAction> actions = new List<InputAction>();
        private readonly List<TimedMove> moves = new List<TimedMove>();
        private readonly Queue<string> commands = new Queue<string>();
        public event Action<TimedMove> MovePressed;
        public event Action<string> CommandPressed;

        private void Awake()
        {
            BindMove("w", MoveDirection.Up); BindMove("a", MoveDirection.Left);
            BindMove("s", MoveDirection.Down); BindMove("d", MoveDirection.Right);
            BindCommand("space", "start"); BindCommand("enter", "start");
            BindCommand("r", "restart"); BindCommand("escape", "pause"); BindCommand("m", "mute");
        }
        private InputAction AddAction(string key)
        {
            var action = new InputAction(key, InputActionType.Button, $"<Keyboard>/{key}");
            actions.Add(action); return action;
        }
        private void BindMove(string key, MoveDirection direction)
        {
            AddAction(key).performed += context =>
            {
                double dsp = AudioSettings.dspTime + context.time - InputState.currentTime;
                moves.Add(new TimedMove(direction, dsp, context.time));
            };
        }
        private void BindCommand(string key, string command) => AddAction(key).performed += _ => commands.Enqueue(command);
        public void Drain()
        {
            while (commands.Count > 0) CommandPressed?.Invoke(commands.Dequeue());
            // Equal timestamp ordering is explicitly W, A, S, D. Never synthesize a diagonal.
            moves.Sort((a, b) => { int time = a.InputEventTime.CompareTo(b.InputEventTime); return time != 0 ? time : a.Direction.CompareTo(b.Direction); });
            foreach (var move in moves) MovePressed?.Invoke(move);
            moves.Clear();
        }
        public void Clear() { moves.Clear(); commands.Clear(); }
        private void OnEnable() { foreach (var action in actions) action.Enable(); }
        private void OnDisable() { foreach (var action in actions) action.Disable(); Clear(); }
        private void OnDestroy() { foreach (var action in actions) action.Dispose(); }
    }
}
