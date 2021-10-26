using System.IO;

namespace QaKit.FileComparer.Image
{
	public class ImageCompareResult
	{
		public FileInfo ExpectedImage { get; set; } = null;
		public FileInfo ActualImage { get; set; } = null;
		public FileInfo DiffImage { get; set; } = null;
	}
}