using System;
using System.Collections.Generic;

namespace SizerDataCollector.Core.AnomalyDetection
{
	/// <summary>
	/// Immutable snapshot of a lanes x grades FPM count matrix.
	/// Values[lane][grade] holds the count for that cell.
	/// GradeKeys provides the ordered list of grade identifiers.
	/// </summary>
	public sealed class GradeMatrix
	{
		public int LaneCount { get; }
		public int GradeCount { get; }
		public IReadOnlyList<string> GradeKeys { get; }
		private readonly double[][] _values;

		public GradeMatrix(double[][] values, IReadOnlyList<string> gradeKeys)
		{
			if (values == null) throw new ArgumentNullException(nameof(values));
			if (gradeKeys == null) throw new ArgumentNullException(nameof(gradeKeys));

			LaneCount = values.Length;
			GradeCount = gradeKeys.Count;
			GradeKeys = gradeKeys;

			_values = new double[LaneCount][];
			for (int i = 0; i < LaneCount; i++)
			{
				if (values[i] == null || values[i].Length != GradeCount)
				{
					throw new ArgumentException(
						$"Lane {i} has {values[i]?.Length ?? 0} grades but expected {GradeCount}.",
						nameof(values));
				}

				_values[i] = new double[GradeCount];
				Array.Copy(values[i], _values[i], GradeCount);
			}
		}

		public double this[int lane, int grade] => _values[lane][grade];

		public double[] GetLaneRow(int lane)
		{
			var row = new double[GradeCount];
			Array.Copy(_values[lane], row, GradeCount);
			return row;
		}

		public double Total()
		{
			double sum = 0;
			for (int i = 0; i < LaneCount; i++)
				for (int j = 0; j < GradeCount; j++)
					sum += _values[i][j];
			return sum;
		}

		public double LaneSum(int lane)
		{
			double sum = 0;
			for (int j = 0; j < GradeCount; j++)
				sum += _values[lane][j];
			return sum;
		}

		public double GradeSum(int grade)
		{
			double sum = 0;
			for (int i = 0; i < LaneCount; i++)
				sum += _values[i][grade];
			return sum;
		}
	}
}
