using System;
using StringAsASword.Board;
using StringAsASword.Rhythm;

namespace StringAsASword.Player
{
    public enum MoveRejection { None, OutsideWindow, OutsideBoard, BeatAlreadyUsed, BeatExpired }

    public readonly struct MoveResult
    {
        public TimingResult Timing { get; }
        public GridPosition Position { get; }
        public MoveRejection Rejection { get; }
        public bool Accepted => Rejection == MoveRejection.None;
        public MoveResult(TimingResult timing, GridPosition position, MoveRejection rejection)
        { Timing = timing; Position = position; Rejection = rejection; }
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
        public int AcceptedMoves => PerfectCount + GoodCount;
        public event Action<MoveResult> MoveEvaluated;
        public event Action<int> NoteMissed;

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
        }
        public MoveResult RequestMove(MoveDirection direction, double inputSongTime)
        {
            Advance(inputSongTime);
            var timing = judge.Evaluate(inputSongTime);
            var rejection = MoveRejection.None;
            GridPosition destination = Position;
            if (!timing.IsHit) rejection = MoveRejection.OutsideWindow;
            else if (timing.BeatId < nextUnresolvedBeat) rejection = MoveRejection.BeatExpired;
            else if (!Gate.CanConsume(timing.BeatId)) rejection = MoveRejection.BeatAlreadyUsed;
            else if (!board.TryGetDestination(Position, direction, out destination)) rejection = MoveRejection.OutsideBoard;

            if (rejection == MoveRejection.None)
            {
                Gate.TryConsume(timing.BeatId);
                Position = destination;
                Combo++;
                BestCombo = Math.Max(Combo, BestCombo);
                if (timing.Grade == Judgement.Perfect) PerfectCount++; else GoodCount++;
            }
            else if (rejection == MoveRejection.OutsideWindow) Combo = 0;
            var result = new MoveResult(timing, Position, rejection);
            MoveEvaluated?.Invoke(result);
            return result;
        }
        public void Advance(double songTime)
        {
            // Every closed beat resolves once, including frames spanning multiple beats.
            while (judge.CloseTime(nextUnresolvedBeat) < songTime - 1e-9)
            {
                if (Gate.LastConsumedBeat != nextUnresolvedBeat)
                {
                    Combo = 0; MissCount++;
                    NoteMissed?.Invoke(nextUnresolvedBeat);
                }
                nextUnresolvedBeat++;
            }
        }
    }
}
