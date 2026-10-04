using System;

namespace StringAsASword.Board
{
    public readonly struct GridPosition : IEquatable<GridPosition>
    {
        public int X { get; }
        public int Y { get; }
        public GridPosition(int x, int y) { X = x; Y = y; }
        public GridPosition Offset(int x, int y) => new GridPosition(X + x, Y + y);
        public bool Equals(GridPosition other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is GridPosition other && Equals(other);
        public override int GetHashCode() => (X * 397) ^ Y;
        public override string ToString() => $"({X}, {Y})";
    }

    public enum MoveDirection { Up, Left, Down, Right }

    /// <summary>Board rules, independent of Unity views and future combat systems.</summary>
    public sealed class BoardState
    {
        public int Width { get; }
        public int Height { get; }
        public BoardState(int width = 8, int height = 8)
        {
            if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width));
            Width = width; Height = height;
        }
        public bool Contains(GridPosition cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < Width && cell.Y < Height;
        public bool TryGetDestination(GridPosition origin, MoveDirection direction, out GridPosition destination)
        {
            switch (direction)
            {
                case MoveDirection.Up: destination = origin.Offset(0, 1); break;
                case MoveDirection.Left: destination = origin.Offset(-1, 0); break;
                case MoveDirection.Down: destination = origin.Offset(0, -1); break;
                case MoveDirection.Right: destination = origin.Offset(1, 0); break;
                default: destination = origin; return false;
            }
            return Contains(origin) && Contains(destination);
        }
    }
}
