using System;

namespace VOA.CouncilTax.AutoProcessing.Helpers;

internal static class DateTimeExtensions
{
	/// <summary>
	/// Truncates a <see cref="DateTime"/> to the specified tick resolution.
	/// </summary>
	public static DateTime Truncate(this DateTime date, long resolution)
	{
		if (resolution <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(resolution), "Resolution must be greater than zero.");
		}

		return new DateTime(date.Ticks - (date.Ticks % resolution), date.Kind);
	}
}
