using System;
using System.IO;
using System.Text;
using System.Xml;
using Microsoft.XmlDiffPatch;

namespace QaKit.FileComparer.XML
{
	public class XmlDocumentComparer
	{
		/// <summary>
		/// Performs an XML diff on two files and dumps a visual diff HTML file if they differ.
		/// </summary>
		/// <param name="expectedXmlFile">The name of one of the files used in the comparison (master xml file/stable etc...)</param>
		/// <param name="actualXmlFile">The name of the other file used in the comparison. Usually output xml file.</param>
		/// <returns>True if the files have the same XML structure, vales etc...</returns>
		public static bool ComparingFilesAreEqual(string expectedXmlFile, string actualXmlFile)
		{
			return ComparingFilesAreEqual(new FileInfo(expectedXmlFile), new FileInfo(actualXmlFile));
		}

		public static bool ComparingFilesAreEqual(FileInfo expectedXmlFile, FileInfo actualXmlFile)
		{
			return ComparingFilesAreEqual(expectedXmlFile, actualXmlFile, GetDefaultOptions());
		}

		public static bool ComparingFilesAreEqual(FileInfo expectedXmlFile, FileInfo actualXmlFile, XmlDiffOptions options)
		{
			bool bIdentical = false;
			var xmlDiff = new XmlDiff(options);

			// contains the xml string revealing the differences between the two files if they are not identical.
			var xmlDiffGram = new StringBuilder();
			var xmlDiffWriter = new XmlTextWriter(new StringWriter(xmlDiffGram));
			try
			{
				var expectedMasterXmlTextReader = new XmlTextReader(expectedXmlFile.OpenText()) { DtdProcessing = DtdProcessing.Parse, XmlResolver = null };
				var actualOutputXmlTextReader = new XmlTextReader(actualXmlFile.OpenText()) { DtdProcessing = DtdProcessing.Parse, XmlResolver = null };

				bIdentical = xmlDiff.Compare(expectedMasterXmlTextReader, actualOutputXmlTextReader, xmlDiffWriter);

				//close readers.
				expectedMasterXmlTextReader.Close();
				actualOutputXmlTextReader.Close();
			}
			catch (Exception e)
			{
				Console.WriteLine($"Error occured on comparing files: {e.Message}");
			}
			xmlDiffWriter.Close();

			if (!bIdentical)
			{
				var resultHtmlFile = Path.Combine(actualXmlFile.Directory.FullName, actualXmlFile.Name + ".html");
				WriteHtmlDiffViewFile(expectedXmlFile.FullName, actualXmlFile.FullName, false, xmlDiffGram.ToString(),
					resultHtmlFile);

				Console.WriteLine($"XMLDiff Result of \'{expectedXmlFile.Name}\' & \'{actualXmlFile.Name}\' was \'Different\', see \'{resultHtmlFile}\'.");
			}

			return bIdentical;
		}

		/// <summary>
		/// Writes out an HTML file showing any differecnes between the two specified files.
		/// </summary>
		/// <param name="expectedXmlFile">The "expected" or "source" XML file used in the comparison.</param>
		/// <param name="actualXmlFile">The "actual" or "changed" XML file used in the comparison.</param>
		/// <param name="bIdentical">True if the two files were found to be identical.</param>
		/// <param name="xmlDiffGram">The xml DiffGram from the XmlDiff tool describing the differences between the two files.</param>
		/// <param name="resultHtmlViewFile">The path and name of the HTML file this function should save.</param>
		internal static void WriteHtmlDiffViewFile(string expectedXmlFile, string actualXmlFile, bool bIdentical, string xmlDiffGram, string resultHtmlViewFile)
		{
			// Write HTML View File:
			TextWriter resultHtml = new StreamWriter(new FileStream(resultHtmlViewFile, FileMode.Create, FileAccess.Write));
			resultHtml.WriteLine("<html><head>");
			resultHtml.WriteLine("<style TYPE='text/css' MEDIA='screen'>");
			resultHtml.Write("<!-- td { font-family: Courier New; font-size:14; } " + "th { font-family: Arial; } " + "p { font-family: Arial; } -->");
			resultHtml.WriteLine("</style></head>");
			resultHtml.WriteLine("<body><h3 style='font-family:Arial'>XmlDiff view</h3><table border='0'><tr><td><table border='0'>");
			resultHtml.WriteLine("<tr><th>" + expectedXmlFile + "</th><th>" + actualXmlFile + "</th></tr>" + "<tr><td colspan=2><hr size=1></td></tr>");
			if (bIdentical)
			{
				resultHtml.WriteLine("<tr><td colspan='2' align='middle'>Files are identical.</td></tr>");
			}
			else
			{
				resultHtml.WriteLine("<tr><td colspan='2' align='middle'>Files are different.</td></tr>");
			}

			var masterReader = new XmlTextReader(expectedXmlFile);
			var xmlDiffView = new XmlDiffView.XmlDiffView();
			masterReader.XmlResolver = null;
			xmlDiffView.Load(masterReader, new XmlTextReader(new StringReader(xmlDiffGram)));

			xmlDiffView.GetHtml(resultHtml);
			masterReader.Close();

			resultHtml.WriteLine("</table></table></body></html>");
			resultHtml.Close();
		}

		internal static XmlDiffOptions GetDefaultOptions()
		{
			return XmlDiffOptions.IgnoreComments | XmlDiffOptions.IgnoreWhitespace | XmlDiffOptions.IgnoreXmlDecl | XmlDiffOptions.IgnoreChildOrder | XmlDiffOptions.IgnorePrefixes;
		}
	}
}
