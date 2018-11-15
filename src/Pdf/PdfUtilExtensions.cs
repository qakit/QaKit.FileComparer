using QaKit.FileComparer.PDF.PdfReader;

namespace QaKit.FileComparer.PDF
{
	public static class PdfUtilExtensions
	{
		public static bool IsEquialTo(this Token token1, Token token2, string value)
		{
			if (token1.TokenType != token2.TokenType)
				return false;

			return token1.ValueString == value && token2.ValueString == value;
		}
	}
}