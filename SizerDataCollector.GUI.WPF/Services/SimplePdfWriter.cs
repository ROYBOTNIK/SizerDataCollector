using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SizerDataCollector.GUI.WPF.Services
{
	public static class SimplePdfWriter
	{
		public static void Write(string path, IReadOnlyList<string> lines)
		{
			if (lines == null || lines.Count == 0)
			{
				throw new ArgumentException("No content to write.", nameof(lines));
			}

			var contentBuilder = new StringBuilder();
			contentBuilder.AppendLine("BT");
			contentBuilder.AppendLine("/F1 10 Tf");

			var yPosition = 760;
			foreach (var line in lines)
			{
				var escaped = EscapePdfText(line);
				contentBuilder.AppendLine($"1 0 0 1 40 {yPosition} Tm ({escaped}) Tj");
				yPosition -= 14;
				if (yPosition < 40)
				{
					break;
				}
			}

			contentBuilder.AppendLine("ET");
			var contentBytes = Encoding.ASCII.GetBytes(contentBuilder.ToString());

			using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
			using (var writer = new StreamWriter(stream, Encoding.ASCII))
			{
				writer.WriteLine("%PDF-1.4");
				writer.Flush();

				var offsets = new List<long> { 0 };
				offsets.Add(stream.Position);
				writer.WriteLine("1 0 obj");
				writer.WriteLine("<< /Type /Catalog /Pages 2 0 R >>");
				writer.WriteLine("endobj");
				writer.Flush();

				offsets.Add(stream.Position);
				writer.WriteLine("2 0 obj");
				writer.WriteLine("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
				writer.WriteLine("endobj");
				writer.Flush();

				offsets.Add(stream.Position);
				writer.WriteLine("3 0 obj");
				writer.WriteLine("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>");
				writer.WriteLine("endobj");
				writer.Flush();

				offsets.Add(stream.Position);
				writer.WriteLine("4 0 obj");
				writer.WriteLine($"<< /Length {contentBytes.Length} >>");
				writer.WriteLine("stream");
				writer.Flush();
				stream.Write(contentBytes, 0, contentBytes.Length);
				writer.WriteLine();
				writer.WriteLine("endstream");
				writer.WriteLine("endobj");
				writer.Flush();

				offsets.Add(stream.Position);
				writer.WriteLine("5 0 obj");
				writer.WriteLine("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
				writer.WriteLine("endobj");
				writer.Flush();

				var xrefPosition = stream.Position;
				writer.WriteLine("xref");
				writer.WriteLine($"0 {offsets.Count}");
				writer.WriteLine("0000000000 65535 f ");
				foreach (var offset in offsets.Skip(1))
				{
					writer.WriteLine($"{offset:0000000000} 00000 n ");
				}

				writer.WriteLine("trailer");
				writer.WriteLine($"<< /Size {offsets.Count} /Root 1 0 R >>");
				writer.WriteLine("startxref");
				writer.WriteLine(xrefPosition);
				writer.WriteLine("%%EOF");
			}
		}

		private static string EscapePdfText(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return string.Empty;
			}

			return value.Replace("\\", "\\\\")
				.Replace("(", "\\(")
				.Replace(")", "\\)");
		}
	}
}
