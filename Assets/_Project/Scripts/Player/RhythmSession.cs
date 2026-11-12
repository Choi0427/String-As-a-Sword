using System;
using StringAsASword.Board;
using StringAsASword.Rhythm;

namespace StringAsASword.Player
{
    public enum PlayerActionKind { Attack = 0, Guard = 1, Move = 2 }
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

    /// <summary>A hold note whose head action was accepted and whose key is still down.</summary>
    public readonly struct HoldState
    {
        public int Beat { get; }
        public PlayerActionKind Kind { get; }
        public MoveDirection Direction { get; }
        public double EndTime { get; }
        public HoldState(int beat, PlayerActionKind kind, MoveDirection direction, double endTime)
        { Beat = beat; Kind = kind; Direction = direction; EndTime = endTime; }
    }

    /// <summary>Session state and movement orchestration; never touches graphics, audio or input APIs.</summary>
    public sealed class RhythmSession
    {
        private readonly BoardState board;
        private readonly RhythmJudge judge;
        private readonly GridPosition start;
        private int nextUnresolvedBeat;
        private readonly int noteCount;
        public bool Stopped { get; set; }
        public int ClosedNotes => nextUnresolvedBeat;
        public ActionGate Gate { get; } = new ActionGate();
        public GridPosition Position { get; private set; }
        public int Combo { get; private set; }
        public int BestCombo { get; private set; }
        public int PerfectCount { get; private set; }
        public int GoodCount { get; private set; }
        public int MissCount { get; private set; }
        public int AcceptedMoves { get; private set; }
        public int AcceptedAttacks { get; private set; }
        public int AcceptedGuards { get; private set; }
        public event Action<ActionResult> ActionEvaluated;
        public event Action<int> NoteMissed;
        public event Action<int> BeatClosed;
        public event Action<TimingResult> HoldResolved;
        private readonly double[] holdEnds;
        public HoldState? ActiveHold { get; private set; }
        /// <summary>Most recent release judgement (BeatId = hold note), or null.</summary>
        public TimingResult? LastHoldResult { get; private set; }
        public int HoldPerfectCount { get; private set; }
        public int HoldGoodCount { get; private set; }
        public int HoldMissCount { get; private set; }
        public bool IsHold(int beat) => holdEnds != null && beat >= 0 && beat < holdEnds.Length && holdEnds[beat] > 0;
        public double HoldEnd(int beat) => IsHold(beat) ? holdEnds[beat] : 0;

        public RhythmSession(BoardState board, RhythmJudge judge, GridPosition start, int noteCount = int.MaxValue, double[] holdEndTimes = null)
        {
            if (!board.Contains(start)) throw new ArgumentOutOfRangeException(nameof(start));
            this.board = board; this.judge = judge; this.start = start; this.noteCount = noteCount;
            holdEnds = holdEndTimes == null || holdEndTimes.Length == 0 ? null : (double[])holdEndTimes.Clone();
            Reset();
        }
        public void Reset()
        {
            Position = start; Combo = BestCombo = PerfectCount = GoodCount = MissCount = nextUnresolvedBeat = 0;
            Gate.Reset();
            Stopped = false;
            AcceptedMoves = AcceptedAttacks = AcceptedGuards = 0;
            ActiveHold = null; LastHoldResult = null; HoldPerfectCount = HoldGoodCount = HoldMissCount = 0;
        }
        /// <summary>Releasing the key that started the active hold judges its tail; other releases are ignored.</summary>
        public void RequestRelease(PlayerActionKind kind, MoveDirection direction, double inputSongTime)
        {
            Advance(inputSongTime);
            if (!ActiveHold.HasValue) return;
            var hold = ActiveHold.Value;
            if (hold.Kind != kind || kind == PlayerActionKind.Move && hold.Direction != direction) return;
            ResolveHold(judge.EvaluateRelease(hold.Beat, inputSongTime, hold.EndTime));
        }
        private void ResolveHold(TimingResult result)
        {
            ActiveHold = null; LastHoldResult = result;
            if (result.Grade == Judgement.Perfect) HoldPerfectCount++;
            else if (result.Grade == Judgement.Good) HoldGoodCount++;
            else HoldMissCount++;
            if (result.IsHit) { Combo++; BestCombo = Math.Max(Combo, BestCombo); } else Combo = 0;
            HoldResolved?.Invoke(result);
        }
        /// <summary>One cell, no beat and no judgement: for the finale after the battle is decided.</summary>
        public bool MoveFreely(MoveDirection direction)
        {
            if (!board.TryGetDestination(Position, direction, out var destination)) return false;
            Position = destination; return true;
        }
        public ActionResult RequestMove(MoveDirection direction, double songTime)
            => RequestAction(PlayerActionKind.Move, direction, songTime);
        public ActionResult RequestAttack(double songTime)
            => RequestAction(PlayerActionKind.Attack, MoveDirection.Up, songTime);
        public ActionResult RequestGuard(double songTime)
            => RequestAction(PlayerActionKind.Guard, MoveDirection.Up, songTime);
        public ActionResult RequestAction(PlayerActionKind kind, MoveDirection direction, double inputSongTime)
        {
            if (!Enum.IsDefined(typeof(PlayerActionKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            Advance(inputSongTime);
            var timing = judge.Evaluate(inputSongTime);
            var rejection = ActionRejection.None;
            GridPosition destination = Position;
            if (Stopped || timing.BeatId >= noteCount) rejection = ActionRejection.BeatExpired;
            else if (!timing.IsHit) rejection = ActionRejection.OutsideWindow;
            else if (timing.BeatId < nextUnresolvedBeat) rejection = ActionRejection.BeatExpired;
            else if (!Gate.CanConsume(timing.BeatId)) rejection = ActionRejection.BeatAlreadyUsed;
            else if (kind == PlayerActionKind.Move && !board.TryGetDestination(Position, direction, out destination)) rejection = ActionRejection.OutsideBoard;

            if (rejection == ActionRejection.None)
            {
                Gate.TryConsume(timing.BeatId);
                if (kind == PlayerActionKind.Move) { Position = destination; AcceptedMoves++; }
                if (kind == PlayerActionKind.Attack) AcceptedAttacks++;
                if (kind == PlayerActionKind.Guard) AcceptedGuards++;
                if (IsHold(timing.BeatId)) ActiveHold = new HoldState(timing.BeatId, kind, direction, holdEnds[timing.BeatId]);
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
            // Every closed beat resolves once, including frames spanning multiple beats. A hold held past its
            // tail's Good window is a Miss; it resolves in time order with beat closes, so one large step and
            // many small steps (live play vs replay) raise events in the same order.
            while (true)
            {
                double beatClose = !Stopped && nextUnresolvedBeat < noteCount ? judge.CloseTime(nextUnresolvedBeat) : double.PositiveInfinity;
                double holdClose = ActiveHold.HasValue ? judge.ReleaseCloseTime(ActiveHold.Value.EndTime) : double.PositiveInfinity;
                if (Math.Min(beatClose, holdClose) >= songTime - 1e-9) break;
                if (holdClose < beatClose)
                {
                    ResolveHold(new TimingResult(ActiveHold.Value.Beat, judge.GoodWindow, Judgement.Miss)); // Late by at least the Good window.
                    continue;
                }
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
