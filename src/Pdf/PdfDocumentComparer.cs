using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using ImageMagick;
using QaKit.FileComparer.Image;
using QaKit.FileComparer.PDF.PdfReader;

namespace QaKit.FileComparer.PDF
{
	public class PdfDocumentComparer
	{
		/// <summary>
		/// Compare two pdf files using handmade comparer. Works only for NOT COMPRESSED PDF
		/// Better use <see cref="ComparingFilesAsImageAreEqual"/> method instead
		/// </summary>
		/// <param name="expectedPdfFile"></param>
		/// <param name="actualPdfFile"></param>
		/// <returns></returns>
		public static bool ComparingFilesAreEqual(string expectedPdfFile, string actualPdfFile)
		{
			return ComparingFilesAreEqual(new FileInfo(expectedPdfFile), new FileInfo(actualPdfFile));
		}

		/// <summary>
		/// Compare two pdf files using handmade comparer. Works only for NOT COMPRESSED PDF
		/// Better use <see cref="ComparingFilesAsImageAreEqual"/> method instead
		/// </summary>
		/// <param name="expectedPdfFile"></param>
		/// <param name="actualPdfFile"></param>
		/// <returns></returns>
		public static bool ComparingFilesAreEqual(FileInfo expectedPdfFile, FileInfo actualPdfFile)
		{
			using (FileStream expectedOutputFileStream = expectedPdfFile.OpenRead())
			using (FileStream actualOutputFileStream = actualPdfFile.OpenRead())
				return Compare(actualOutputFileStream, expectedOutputFileStream) == 0;
		}

		/// <summary>
		/// Compare two pdf files using <see cref="ImageMagick"/>. First generate from pdf images after that compare all images in output directory
		/// </summary>
		/// <param name="expectedPdfFile"></param>
		/// <param name="actualPdfFile"></param>
		/// <returns></returns>
		public static bool ComparingFilesAsImageAreEqual(FileInfo expectedPdfFile, FileInfo actualPdfFile)
		{
			var expectedPdfImagesOutputDir = new DirectoryInfo(Path.Combine(expectedPdfFile.Directory.FullName, $"{expectedPdfFile.Name}_Images"));
			var actualPdfImagesOutputDir = new DirectoryInfo(Path.Combine(actualPdfFile.Directory.FullName, $"{actualPdfFile.Name}_Images"));

			GenerateImagesFromPdf(expectedPdfFile, expectedPdfImagesOutputDir);
			GenerateImagesFromPdf(actualPdfFile, actualPdfImagesOutputDir);

			return ImageDocumentComparer.AllImageFilesInDirectoryAreEqual(expectedPdfImagesOutputDir, actualPdfImagesOutputDir);
		}

		internal static void GenerateImagesFromPdf(FileInfo pdfFile, DirectoryInfo directoryToSaveImages)
		{
			var settings = new MagickReadSettings { Density = new Density(96, 96) };

			using (var images = new MagickImageCollection())
			{
				images.Read(pdfFile, settings);
				var page = 1;
				Console.WriteLine($"Saving generated image from pdf to {directoryToSaveImages.FullName}");

				foreach (var magickImage in images)
				{
					var outputFile = new FileInfo(Path.Combine(directoryToSaveImages.FullName, pdfFile.Name + "_" + page + ".png"));
					magickImage.Write(outputFile);
					page++;
				}
			}
		}

		#region Compare impl

		private static int Compare(Stream actualDocumentStream, Stream expectedDocumentStream)
		{
			PdfScanner reader1 = new PdfScanner(actualDocumentStream);
			PdfScanner reader2 = new PdfScanner(expectedDocumentStream);

			// Ignore some of next values:
			const string BYTERANGE = "ByteRange";
			const string CREATIONDATE = "CreationDate";
			const string MODDATE = "ModDate";
			const string PRODUCER = "Producer";
			const string CREATOR = "Creator";
			const string ID = "ID";

			// font setting can very from machine
			const string BASEFONT = "BaseFont";
			const string FONTNAME = "FontName";
			const string CAPHEIGHT = "CapHeight";
			const int CapHeightDelta = 200;
			const string FONTBBOX = "FontBBox";
			const string METADATA = "Metadata";
			const int FontBBoxValueDelta = 1000;

			// index depend on file size so should be skipped too
			// see http://labs.appligent.com/pdfblog/pdf_cross_reference_table.php
			const string XREF = "xref";
			const string STARTXREF = "startxref";
			const int FileSizeDelta = 100;

			bool continue1, continue2;

			do
			{
				Token token1, token2;
				continue1 = reader1.NextToken(out token1);
				continue2 = reader2.NextToken(out token2);
				if (continue1 != continue2)
					throw new ApplicationException("One reader finished before the other!");

				if (token1.TokenType == TokenType.Name
					&& token2.TokenType == TokenType.Name)
				{
					if (token1.IsEquialTo(token2, BYTERANGE))
					{
						// skip unstable signature bytes
						do
						{
							reader1.NextToken(out token1);
							reader2.NextToken(out token2);
						} while (token1.TokenType != TokenType.DictionaryEnd);
						continue;// this will skip the item
					}
					if (token1.IsEquialTo(token2, CREATIONDATE) ||
						token1.IsEquialTo(token2, MODDATE) ||
						token1.IsEquialTo(token2, PRODUCER) ||
						token1.IsEquialTo(token2, CREATOR))
					{
						// skip the next item (expect a string)
						reader1.NextToken(out token1);
						reader2.NextToken(out token2);
						Debug.Assert(token1.TokenType == TokenType.PdfString && token2.TokenType == TokenType.PdfString, "Unexpected type for CreationDate!");
						continue;// this will skip the item
					}
					if (token1.IsEquialTo(token2, ID))
					{
						reader1.NextToken(out token1);
						reader2.NextToken(out token2);
						if (token1.TokenType == TokenType.ArrayBegin && token2.TokenType == TokenType.ArrayBegin)
						{//Unique file identifier needs skipped (ArrayBegin, PdfString, PdfString, ArrayEnd)
							reader1.NextToken(out token1);
							reader2.NextToken(out token2);
							Debug.Assert(token1.TokenType == TokenType.PdfString && token2.TokenType == TokenType.PdfString, "Unexpected type for ID!");
							reader1.NextToken(out token1);
							reader2.NextToken(out token2);
							Debug.Assert(token1.TokenType == TokenType.PdfString && token2.TokenType == TokenType.PdfString, "Unexpected type for ID!");
							reader1.NextToken(out token1);
							reader2.NextToken(out token2);
							Debug.Assert(token1.TokenType == TokenType.ArrayEnd && token2.TokenType == TokenType.ArrayEnd, "Unexpected type for ID!");
							reader1.NextToken(out token1);
							reader2.NextToken(out token2);
						}
						continue;// this will skip the item
					}

					if (token1.IsEquialTo(token2, BASEFONT) ||
						token1.IsEquialTo(token2, FONTNAME))
					{//For embedded fonts, there is a subset prefix added to the name of the
					 //font that is random.  The format is random string then a '+' then the font name.
						reader1.NextToken(out token1);
						reader2.NextToken(out token2);
						if (token1.TokenType == TokenType.Name && token2.TokenType == TokenType.Name)
						{
							string font1 = token1.ValueString;
							string font2 = token2.ValueString;
							if (font1.Equals(font2))
								continue;
							int index = font1.IndexOf("+");
							if (index >= 0)
								font1 = font1.Substring(index + 1);
							index = font2.IndexOf("+");
							if (index >= 0)
								font2 = font2.Substring(index + 1);
							if (font1.Equals(font2))
								continue;
							return 1;
						}
					}

					// these properties can be different on different machines
					if (token1.IsEquialTo(token2, CAPHEIGHT))
					{
						// this value is different in XP and Win 7 (it looks, like MC changed fonts)
						reader1.NextToken(out token1);
						reader2.NextToken(out token2);
						if (token1.TokenType == TokenType.Number && token2.TokenType == TokenType.Number)
						{
							// the value is about 500-800
							if (Math.Abs(token1.ValueNumber - token2.ValueNumber) < CapHeightDelta)
								continue;
							return 1;
						}
					}
					if (token1.IsEquialTo(token2, FONTBBOX))
					{
						// this value is different in XP and Win 7 (it looks, like MC changed fonts)
						reader1.NextToken(out token1);
						reader2.NextToken(out token2);
						if (token1.TokenType == TokenType.ArrayBegin && token2.TokenType == TokenType.ArrayBegin)
						{
							while (true)
							{
								reader1.NextToken(out token1);
								reader2.NextToken(out token2);

								// TODO - improve comparison
								if (token1.TokenType == TokenType.Number && token2.TokenType == TokenType.Number)
								{
									if (Math.Abs(token1.ValueNumber - token2.ValueNumber) > FontBBoxValueDelta)
										return 1;
								}

								if (token1.TokenType == TokenType.ArrayEnd || token2.TokenType == TokenType.ArrayEnd)
									break;
							}
						}
					}

					if (token1.IsEquialTo(token2, METADATA))
					{
						reader1.NextToken(out token1);
						reader2.NextToken(out token2);
						if (token1.TokenType == TokenType.DictionaryEnd && token2.TokenType == TokenType.DictionaryEnd)
						{
							reader1.NextToken(out token1);
							reader2.NextToken(out token2);
							if (token1.TokenType == TokenType.Stream && token2.TokenType == TokenType.Stream)
							{
								var xmlDocument1 = new XmlDocument();
								var bytes1 = ((MemoryStream)token1.ValueStream).ToArray();
								xmlDocument1.InnerXml = "<root></root>";
								xmlDocument1.DocumentElement.InnerXml = Encoding.ASCII.GetString(bytes1);

								RemoveXmlElementsByName(xmlDocument1, "ModifyDate", "http://ns.adobe.com/xap/1.0/");
								RemoveXmlElementsByName(xmlDocument1, "CreateDate", "http://ns.adobe.com/xap/1.0/");

								var xmlDocument2 = new XmlDocument();
								var bytes2 = ((MemoryStream)token2.ValueStream).ToArray();
								xmlDocument2.InnerXml = "<root></root>";
								xmlDocument2.DocumentElement.InnerXml = Encoding.ASCII.GetString(bytes2);

								RemoveXmlElementsByName(xmlDocument2, "ModifyDate", "http://ns.adobe.com/xap/1.0/");
								RemoveXmlElementsByName(xmlDocument2, "CreateDate", "http://ns.adobe.com/xap/1.0/");

								// Check remains data
								if (xmlDocument1.InnerXml != xmlDocument2.InnerXml)
								{
									Trace.WriteLine("The metadata objects are different.");
									return 1;
								}
								continue;
							}
						}
					}
				}
				else if (token1.TokenType == TokenType.Comment && token2.TokenType == TokenType.Comment)
				{ // skip comment type token
					reader1.NextToken(out token1);
					reader2.NextToken(out token2);
					continue;
				}
				else if (token1.TokenType == TokenType.Ident && token2.TokenType == TokenType.Ident)
				{
					// indeces depend on file size
					if (token1.IsEquialTo(token2, STARTXREF))
					{
						// this value is different in XP and Win 7 (it looks, like MC changed fonts)
						reader1.NextToken(out token1);
						reader2.NextToken(out token2);
						if (token1.TokenType == TokenType.Number && token2.TokenType == TokenType.Number)
						{
							// files size value should not be very different
							if (Math.Abs(token1.ValueNumber - token2.ValueNumber) < FileSizeDelta)
								continue;
						}
					}
					if (token1.IsEquialTo(token2, XREF))
					{
						reader1.NextToken(out token1);
						reader2.NextToken(out token2);
						if (token1.TokenType == TokenType.Number && token2.TokenType == TokenType.Number)
						{
							if (!token1.Equals(token2))
								return 1;
							int start = (int)token1.ValueNumber;
							reader1.NextToken(out token1);
							reader2.NextToken(out token2);
							if (token1.TokenType == TokenType.Number && token2.TokenType == TokenType.Number)
							{
								if (!token1.Equals(token2))
									return 1;
								int count = (int)token1.ValueNumber;

								// read all indeces
								for (int i = start; i < count; i++)
								{
									reader1.NextToken(out token1);
									reader2.NextToken(out token2);
									if (token1.TokenType != TokenType.Number || token2.TokenType != TokenType.Number)
										return 1;
									// files size value should not be very different
									if (Math.Abs(token1.ValueNumber - token2.ValueNumber) > FileSizeDelta)
										return 1;
									reader1.NextToken(out token1);
									reader2.NextToken(out token2);
									if (token1.TokenType != TokenType.Number || token2.TokenType != TokenType.Number)
										return 1;
									// files size value should not be very different
									if (Math.Abs(token1.ValueNumber - token2.ValueNumber) > FileSizeDelta)
										return 1;
									reader1.NextToken(out token1);
									reader2.NextToken(out token2);
									if (!token1.Equals(token2))
										return 1;
								}
							}
						}
					}
				}


				if (!token1.Equals(token2))
				{
					Trace.WriteLine("The tokens " + token1 + " and " + token2 + " are different.");
					return 1;
				}

			} while (continue1 && continue2);

			return 0;
		}

		private static void RemoveXmlElementsByName(XmlDocument xmlDocument, string elementName, string namespaceUri)
		{
			foreach (var elementToRemove in
					xmlDocument.GetElementsByTagName(elementName, namespaceUri)
					.Cast<XmlElement>().ToArray())
			{
				elementToRemove.ParentNode.RemoveChild(elementToRemove);
			}
		}

		#endregion
	}
}
