using System.IO;
using System.Xml;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;

namespace QaKit.FileComparer.Word
{
	public static class WordUtilExtensions
	{
		public static XDocument GetXDocument(this OpenXmlPart part)
		{
			XDocument xdoc = part.Annotation<XDocument>();
			if (xdoc != null) return xdoc;

			using (StreamReader streamReader = new StreamReader(part.GetStream()))
				xdoc = XDocument.Load(XmlReader.Create(streamReader));

			part.AddAnnotation(xdoc);
			return xdoc;
		}
	}
}
