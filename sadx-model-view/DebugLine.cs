﻿namespace sadx_model_view
{
	public struct DebugLine
	{
		public static int SizeInBytes => 2 * DebugPoint.SizeInBytes;

		public DebugPoint PointA, PointB;

		public DebugLine(DebugPoint a, DebugPoint b)
		{
			PointA = a;
			PointB = b;
		}

		public override bool Equals(object obj)
		{
			if (obj is null)
			{
				return false;
			}

			if (!(obj is DebugLine other))
			{
				return false;
			}

			return PointA == other.PointA && PointB == other.PointA;
		}

		public override int GetHashCode()
		{
			throw new System.NotImplementedException();
		}

		public static bool operator ==(DebugLine left, DebugLine right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(DebugLine left, DebugLine right)
		{
			return !(left == right);
		}
	}
}