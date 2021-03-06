﻿using System;
using System.IO;
using System.Linq;
using ImageMagick;

namespace QaKit.FileComparer.Image
{
	public class ImageDocumentComparer
	{
		// make it field?
		private const int TrustLevel = 7; //means how much pixel we can ignore before failed comparing

		public static bool ComparingFilesAreEqual(FileInfo expectedImageFile, FileInfo actualImageFile)
		{
			using (var expectedImage = new MagickImage(expectedImageFile.FullName))
			using (var actualImage = new MagickImage(actualImageFile.FullName))
			{
				return ComparingFilesAreEqual(expectedImage, actualImage, new FileInfo(actualImageFile.FullName + "_comparingResults.png"));
			}
		}

		public static bool ComparingFilesAreEqual(MagickImage expectedImageFile, MagickImage actualImageFile, FileInfo outputDiffFile)
		{
			using (var outputErrorImage = new MagickImage())
			{
				double errors;
				try
				{
					errors = actualImageFile.Compare(expectedImageFile, ErrorMetric.Absolute, outputErrorImage);
				}
				catch (Exception e)
				{
					Console.WriteLine($"Error occured during comparing two images. Exception: {e.Message}");
					return false;
				}

				if (!(errors > TrustLevel)) return true;

				var outputErrorFile = outputDiffFile.FullName;
				outputErrorImage.Write(outputErrorFile);
				return false;

			}
		}

		public static bool AllImageFilesInDirectoryAreEqual(DirectoryInfo expectedImagesFolder, DirectoryInfo actualImagesFolder)
		{
			var actualImageFiles = actualImagesFolder.EnumerateFiles("*.*", SearchOption.AllDirectories).
					Where(file => file.Extension.Equals(".bmp", StringComparison.InvariantCultureIgnoreCase) ||
								  file.Extension.Equals(".jpg", StringComparison.InvariantCultureIgnoreCase) ||
								  file.Extension.Equals(".png", StringComparison.InvariantCultureIgnoreCase) ||
								  file.Extension.Equals(".gif", StringComparison.InvariantCultureIgnoreCase));

			var expectedImageFiles = expectedImagesFolder.EnumerateFiles("*.*", SearchOption.AllDirectories).
					Where(file => file.Extension.Equals(".bmp", StringComparison.InvariantCultureIgnoreCase) ||
								  file.Extension.Equals(".jpg", StringComparison.InvariantCultureIgnoreCase) ||
								  file.Extension.Equals(".png", StringComparison.InvariantCultureIgnoreCase) ||
								  file.Extension.Equals(".gif", StringComparison.InvariantCultureIgnoreCase));

			bool allFilesAreEqual = false;
			if (actualImageFiles.Count() != expectedImageFiles.Count())
			{
				Console.WriteLine("Number of images in directories are not equal to each other");
				return false;
			}

			foreach (FileInfo actualImageFile in actualImageFiles)
			{
				var expectedImageFile = expectedImageFiles.FirstOrDefault(s => s.Name == actualImageFile.Name);
				allFilesAreEqual = ComparingFilesAreEqual(expectedImageFile, actualImageFile);

				if (!allFilesAreEqual)
				{
					return false;
				}
			}

			return allFilesAreEqual;
		}
	}
}
