using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.XmlDiffPatch;
using QaKit.FileComparer.Image;
using QaKit.FileComparer.XML;
using QaKit.FileComparer.XML.XmlDiffView;

namespace QaKit.FileComparer.Word
{
	public class WordDocXDocumentComparer
	{
		/// <summary>
		/// Compare two word documents in .docx file format
		/// 1. First extract xml from main docx file part and compare it using <see cref="XmlDocumentComparer"/>
		/// 2. Second extract images from docx file and compare them using <see cref="ImageDocumentComparer"/>
		/// 3. After that compare styles as xml documents using <see cref="XmlDocumentComparer"/>
		/// </summary>
		/// <param name="expectedWordFile"></param>
		/// <param name="actualWordFile"></param>
		/// <returns></returns>
		public static bool ComparingFilesAreEqual(FileInfo expectedWordFile, FileInfo actualWordFile)
		{
			//First compare main document
			var mainPartsAreEqual = MainDocumentsAreEqual(expectedWordFile, actualWordFile);

			//Now, compare image part
			var imagePartsAreEqual = DocumentImagesAreEqual(expectedWordFile, actualWordFile);

			//And style part
			var stylePartsAreEqual = DocumentStylesAreEqual(expectedWordFile, actualWordFile);

			return mainPartsAreEqual && imagePartsAreEqual && stylePartsAreEqual;
		}

		private static bool MainDocumentsAreEqual(FileInfo expectedOutputWordFile, FileInfo actualOutputWordFile)
		{
			WordprocessingDocument doc1 = WordprocessingDocument.Open(expectedOutputWordFile.FullName, false);
			WordprocessingDocument doc2 = WordprocessingDocument.Open(actualOutputWordFile.FullName, false);

			XDocument expectedDocumentFile = doc1.MainDocumentPart.GetXDocument();
			XDocument actualDocumentFile = doc2.MainDocumentPart.GetXDocument();

			var outputCompareHtmlFile =
				new FileInfo(Path.Combine(actualOutputWordFile.Directory.FullName,
					Path.GetFileNameWithoutExtension(actualOutputWordFile.FullName) + "_MainPart", actualOutputWordFile.Name + ".html"));

			doc1.Dispose();
			doc2.Dispose();

			RemoveAllIdFromDocument(expectedDocumentFile);
			RemoveAllIdFromDocument(actualDocumentFile);

			expectedDocumentFile.Save(expectedOutputWordFile.FullName + ".xml");
			actualDocumentFile.Save(actualOutputWordFile.FullName + ".xml");

			return ComparingElementsAreEqual(expectedDocumentFile.CreateReader(), actualDocumentFile.CreateReader(), outputCompareHtmlFile);
		}

		private static void RemoveAllIdFromDocument(XDocument document)
		{
			//Remove here bookmarkStart w:id and bookmarkEnd w:id elements from main part
			//Also remove embed attribute from <a:blip/> element (this id uses in media part to find related images and insert them in document.
			//for now it's not necessary for comparing in xml
			foreach (XAttribute xAttribute in from descendant in document.Descendants()
				where descendant.Name.LocalName == "bookmarkStart"
				      || descendant.Name.LocalName == "bookmarkEnd"
				      || descendant.Name.LocalName == "blip"
				      || descendant.Name.LocalName == "hlinkClick"
				      || descendant.Name.LocalName == "altChunk"
				      || descendant.Name.LocalName == "fill"
				from xAttribute in descendant.Attributes()
				where xAttribute.Name.LocalName == "id" || xAttribute.Name.LocalName == "embed"
				select xAttribute)
			{
				xAttribute.Remove();
			}
		}

		private static bool DocumentImagesAreEqual(FileInfo expectedOutputWordFile, FileInfo actualOutputWordFile)
		{
			var expectedOutputFileImageDirectory = SaveDocumentImages(expectedOutputWordFile);
			var actualOutputFileImageDirectory = SaveDocumentImages(actualOutputWordFile);

			//Return true if both directories are empty (it means that no images were saved from docx document).
			//in other case (if one folder contains images) compare them
			if (expectedOutputFileImageDirectory == null && actualOutputFileImageDirectory == null)
			{
				return true;
			}

			return ImageDocumentComparer.AllImageFilesInDirectoryAreEqual(expectedOutputFileImageDirectory, actualOutputFileImageDirectory);
		}

		private static bool DocumentStylesAreEqual(FileInfo expectedOutputWordFile, FileInfo actualOutputWordFile)
		{
			WordprocessingDocument doc1 = WordprocessingDocument.Open(expectedOutputWordFile.FullName, false);
			WordprocessingDocument doc2 = WordprocessingDocument.Open(actualOutputWordFile.FullName, false);

			XDocument expectedDocumentStyles = doc1.MainDocumentPart.StyleDefinitionsPart.GetXDocument();
			XDocument actualDocumentStyles = doc2.MainDocumentPart.StyleDefinitionsPart.GetXDocument();

			var outputCompareHtmlFile =
				new FileInfo(Path.Combine(actualOutputWordFile.Directory.FullName,
					Path.GetFileNameWithoutExtension(actualOutputWordFile.FullName) + "_StylePart", actualOutputWordFile.Name + ".html"));

			doc1.Dispose();
			doc2.Dispose();

			return ComparingElementsAreEqual(expectedDocumentStyles.CreateReader(), actualDocumentStyles.CreateReader(), outputCompareHtmlFile);
		}

		internal static bool ComparingElementsAreEqual(XmlReader expectedMasterReader, XmlReader actualOutputReader, FileInfo outputCompareResultFile)
		{
			bool bIdentical = false;

			var xmlDiff = new XmlDiff(XmlDiffOptions.IgnoreComments | XmlDiffOptions.IgnoreWhitespace | XmlDiffOptions.IgnoreXmlDecl | XmlDiffOptions.IgnorePrefixes);
			var xmlDiffGram = new StringBuilder();
			var xmlDiffWriter = new XmlTextWriter(new StringWriter(xmlDiffGram));

			try
			{
				bIdentical = xmlDiff.Compare(expectedMasterReader, actualOutputReader, xmlDiffWriter);
				expectedMasterReader.Close();
				actualOutputReader.Close();
				xmlDiffWriter.Close();
			}
			catch (Exception e)
			{
				Console.WriteLine("Error occured during comparing two styles: {0}", e.Message);
			}

			if (!bIdentical)
			{
				if (Directory.Exists(outputCompareResultFile.Directory.FullName))
					outputCompareResultFile.Directory.Delete(true);
				outputCompareResultFile.Directory.Create();
				WriteHtmlDiffViewFile(expectedMasterReader, false, xmlDiffGram.ToString(), outputCompareResultFile.FullName);
			}

			return bIdentical;
		}

		private static void WriteHtmlDiffViewFile(XmlReader expectedDocumentReader, bool bIdentical, string xmlDiffGram, string resultHtmlFile)
		{
			// Write HTML View File:
			TextWriter resultHtml = new StreamWriter(new FileStream(resultHtmlFile, FileMode.Create, FileAccess.Write));
			resultHtml.WriteLine("<html><head>");
			resultHtml.WriteLine("<style TYPE='text/css' MEDIA='screen'>");
			resultHtml.Write("<!-- td { font-family: Courier New; font-size:14; } " + "th { font-family: Arial; } " + "p { font-family: Arial; } -->");
			resultHtml.WriteLine("</style></head>");
			resultHtml.WriteLine("<body><h3 style='font-family:Arial'>XmlDiff view</h3><table border='0'><tr><td><table border='0'>");
			resultHtml.WriteLine("<tr><th>" + "ExpectedOutput Style File" + "</th><th>" + "ActualOutput Style File" + "</th></tr>" + "<tr><td colspan=2><hr size=1></td></tr>");

			resultHtml.WriteLine(bIdentical
				? "<tr><td colspan='2' align='middle'>Files are identical.</td></tr>"
				: "<tr><td colspan='2' align='middle'>Files are different.</td></tr>");

			var xmlDiffView = new QaKit.FileComparer.XML.XmlDiffView.XmlDiffView();

			xmlDiffView.Load(expectedDocumentReader, new XmlTextReader(new StringReader(xmlDiffGram)));

			xmlDiffView.GetHtml(resultHtml);
			expectedDocumentReader.Close();

			resultHtml.WriteLine("</table></table></body></html>");
			resultHtml.Close();
		}

		private static DirectoryInfo SaveDocumentImages(FileInfo document)
		{
			var fileName = Path.GetFileNameWithoutExtension(document.FullName);
			var outputDirectory =
				new DirectoryInfo(Path.Combine(document.Directory.FullName, fileName + "_ImagePart"));

			using (var wordDocument = WordprocessingDocument.Open(document.FullName, false))
			{
				//Get image parts from document
				var pictures = wordDocument.MainDocumentPart.ImageParts.ToList();
				//If no images in document return null so identify that no images in document
				if (pictures.Count == 0) return null;

				if (Directory.Exists(outputDirectory.FullName)) outputDirectory.Delete(true);
				outputDirectory.Create();
				int imageCount = 0;
				foreach (ImagePart picture in pictures)
				{
					Stream stream = picture.GetStream();
					var length = stream.Length;
					var byteStream = new byte[length];
					stream.Read(byteStream, 0, (int)length);

					var fileStream =
						new FileStream(Path.Combine(outputDirectory.FullName, fileName + imageCount + ".png"), FileMode.OpenOrCreate);
					fileStream.Write(byteStream, 0, (int)length);
					fileStream.Close();
					imageCount++;
				}

				wordDocument.Dispose();
			}

			return outputDirectory;
		}

		public static void KillWordProcess()
		{
			Process[] process = Process.GetProcessesByName("WINWORD");
			foreach (Process p1 in process)
			{
				p1.Kill();
			}
		}
	}
}
