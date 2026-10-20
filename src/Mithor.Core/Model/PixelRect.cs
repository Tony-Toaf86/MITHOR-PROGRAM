using System;

namespace Mithor.Core.Model
{
    /// <summary>
    /// Rectángulo en píxeles físicos. Es el único tipo geométrico que usa el núcleo,
    /// de forma que no depende de System.Drawing y puede compilarse también en Linux.
    /// </summary>
    public struct PixelRect : IEquatable<PixelRect>
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;

        public PixelRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public int Right { get { return X + Width; } }
        public int Bottom { get { return Y + Height; } }
        public bool IsEmpty { get { return Width <= 0 || Height <= 0; } }
        public int CenterX { get { return X + Width / 2; } }
        public int CenterY { get { return Y + Height / 2; } }
        public long Area { get { return (long)Math.Max(0, Width) * Math.Max(0, Height); } }

        public bool Contains(int px, int py)
        {
            return px >= X && px < Right && py >= Y && py < Bottom;
        }

        public PixelRect Intersect(PixelRect other)
        {
            int x1 = Math.Max(X, other.X);
            int y1 = Math.Max(Y, other.Y);
            int x2 = Math.Min(Right, other.Right);
            int y2 = Math.Min(Bottom, other.Bottom);
            if (x2 <= x1 || y2 <= y1)
            {
                return new PixelRect(0, 0, 0, 0);
            }
            return new PixelRect(x1, y1, x2 - x1, y2 - y1);
        }

        public bool IntersectsWith(PixelRect other)
        {
            return !Intersect(other).IsEmpty;
        }

        public bool Equals(PixelRect other)
        {
            return X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
        }

        public override bool Equals(object obj)
        {
            return obj is PixelRect && Equals((PixelRect)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + X;
                hash = hash * 31 + Y;
                hash = hash * 31 + Width;
                hash = hash * 31 + Height;
                return hash;
            }
        }

        public override string ToString()
        {
            return string.Format("{0},{1} {2}x{3}", X, Y, Width, Height);
        }
    }
}
