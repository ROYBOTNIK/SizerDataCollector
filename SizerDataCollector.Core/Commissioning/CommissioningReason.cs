namespace SizerDataCollector.Core.Commissioning
{
	public sealed class CommissioningReason
	{
		public string Code { get; }
		public string Message { get; }

		public CommissioningReason(string code, string message)
		{
			Code = code ?? string.Empty;
			Message = message ?? string.Empty;
		}

		public override string ToString()
		{
			return string.IsNullOrWhiteSpace(Code) ? Message : $"{Code}: {Message}";
		}
	}
}

