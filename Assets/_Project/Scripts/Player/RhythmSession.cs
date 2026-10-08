using System;
using StringAsASword.Board;
using StringAsASword.Rhythm;

namespace StringAsASword.Player
{
    public enum PlayerActionKind { Attack = 0, Move = 2 }
    public enum ActionRejection { None, OutsideWindow, OutsideBoard, BeatAlreadyUsed, BeatExpired }

    public readonly struct ActionResult
    {
        public PlayerActionKind Kind { get; }
        public TimingResult Timing { get; }
        public GridPosition Position { get; }
        public ActionRejection Rejection { get; }
        public bool Accepted => Rejection == ActionRejection.None;
        public ActionResult(PlayerActionKind kind, TimingResult timing, GridPosition position, ActionRejection rejection)
        { Kind = kind; Timing = timing; Position = position; Rejection = rejection; }
    }

    /// <summary>Session state and movement orchestration; never touches graphics, audio or input APIs.</summary>
    public sealed class RhythmSession
    {
        private readonly BoardState board;
        private readonly RhythmJudge judge;
        private readonly GridPosition start;
        private int nextUnresolvedBeat;
        public ActionGate Gate { get; } = new ActionGate();
        public GridPosition Position { get; private set; }
        public int Combo { get; private set; }
        public int BestCombo { get; private set; }
        public int PerfectCount { get; private set; }
        public int GoodCount { get; private set; }
        public int MissCount { get; private set; }
        public int AcceptedMoves { get; private set; }
        public int AcceptedAttacks { get; private set; }
        public event Action<ActionResult> ActionEvaluated;
        public event Action<int> NoteMissed;
        public event Action<int> BeatClosed;

        public RhythmSession(BoardState board, RhythmJudge judge, GridPosition start)
        {
            if (!board.Contains(start)) throw new ArgumentOutOfRangeException(nameof(start));
            this.board = board; this.judge = judge; this.start = start;
            Reset();
        }
        public void Reset()
        {
            Position = start; Combo = BestCombo = PerfectCount = GoodCount = MissCount = nextUnresolvedBeat = 0;
            Gate.Reset();
            AcceptedMoves = AcceptedAttacks = 0;
        }
        public ActionResult RequestMove(MoveDirection direction, double songTime)
            => RequestAction(PlayerActionKind.Move, direction, songTime);
        public ActionResult RequestAttack(double songTime)
            => RequestAction(PlayerActionKind.Attack, MoveDirection.Up, songTime);
        public ActionResult RequestAction(PlayerActionKind kind, MoveDirection direction, double inputSongTime)
        {
            if (!Enum.IsDefined(typeof(PlayerActionKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            Advance(inputSongTime);
            var timing = judge.Evaluate(inputSongTime);
            var rejection = ActionRejection.None;
            GridPosition destination = Position;
            if (!timing.IsHit) rejection = ActionRejection.OutsideWindow;
            else if (timing.BeatId < nextUnresolvedBeat) rejection = ActionRejection.BeatExpired;
            else if (!Gate.CanConsume(timing.BeatId)) rejection = ActionRejection.BeatAlreadyUsed;
            else if (kind == PlayerActionKind.Move && !board.TryGetDestination(Position, direction, out destination)) rejection = ActionRejection.OutsideBoard;

            if (rejection == ActionRejection.None)
            {
                Gate.TryConsume(timing.BeatId);
                if (kind == PlayerActionKind.Move) { Position = destination; AcceptedMoves++; }
                if (kind == PlayerActionKind.Attack) AcceptedAttacks++;
                Combo++;
                BestCombo = Math.Max(Combo, BestCombo);
                if (timing.Grade == Judgement.Perfect) PerfectCount++; else GoodCount++;
            }
            else if (rejection == ActionRejection.OutsideWindow) Combo = 0;
            var result = new ActionResult(kind, timing, Position, rejection);
            ActionEvaluated?.Invoke(result);
            return result;
        }
        public void Advance(double songTime)
        {
            // Every closed beat resolves once, including frames spanning multiple beats.
            while (judge.CloseTime(nextUnresolvedBeat) < songTime - 1e-9)
            {
                int beat = nextUnresolvedBeat++;
                if (Gate.LastConsumedBeat != beat)
                {
                    Combo = 0; MissCount++;
                    NoteMissed?.Invoke(beat);
                }
                BeatClosed?.Invoke(beat);
            }
        }
    }
}
