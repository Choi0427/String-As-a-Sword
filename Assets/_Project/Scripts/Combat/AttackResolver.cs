using System;
using StringAsASword.Board;
using StringAsASword.Player;

namespace StringAsASword.Combat
{
    public sealed class AttackResolver
    {
        private readonly BoardState board;
        public AttackResolver(BoardState board) { this.board = board; }
        public bool IsInRange(GridPosition position) => board.Contains(position) && position.Y == board.Height - 1;
        public int DamageRequested(ActionResult action, int damage)
        {
            if (damage < 0) throw new ArgumentOutOfRangeException(nameof(damage));
            return action.Accepted && action.Kind == PlayerActionKind.Attack && IsInRange(action.Position) ? damage : 0;
        }
    }
}
