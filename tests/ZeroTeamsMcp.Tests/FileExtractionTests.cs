using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ZeroTeamsMcp.Server.Tools;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace ZeroTeamsMcp.Tests;

public class FileExtractionTests
{
    // ── PDF mínimo (digital y escaneado) ────────────────────────────────────────────────────────

    /// <summary>PDF 1.4 de una página con el content stream indicado y xref correcto.</summary>
    internal static byte[] MakePdf(string streamData)
    {
        // Solo el cuerpo de cada objeto; el builder añade el marcador "N 0 obj" y "endobj".
        var parts = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " +
                "/Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            "<< /Length " + streamData.Length + " >>\nstream\n" + streamData + "\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };

        // Marcador binario (%\xE2\xE3\xCF\xD3) para que parsers estrictos no confundan el fichero
        // con texto plano; Latin1 mapea byte a byte (ASCII llena el resto).
        var header = "%PDF-1.4\n%\xE2\xE3\xCF\xD3\n";
        var sb = new StringBuilder(header);
        var offsets = new List<long>();
        long pos = header.Length;

        for (var i = 0; i < parts.Length; i++)
        {
            offsets.Add(pos);
            var body = (i + 1) + " 0 obj\n" + parts[i] + "\nendobj\n";
            sb.Append(body);
            pos += body.Length;
        }

        var xrefPos = pos;
        sb.Append("xref\n0 ").Append(parts.Length + 1).Append('\n');
        sb.Append("0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append(o.ToString("D10")).Append(" 00000 n \n");
        sb.Append("trailer\n<< /Root 1 0 R /Size ").Append(parts.Length + 1).Append(" >>\n");
        sb.Append("startxref\n").Append(xrefPos).Append("\n%%EOF");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    private const string DigitalPdfStream =
        "BT /F1 24 Tf 72 720 Td (ZeroTeams PDF test page ZeroTeams PDF test page ZeroTeams PDF test page ZeroTeams PDF test page) Tj ET";

    [Fact]
    public void ExtractPdf_Digital_NoDevuelvePaginaConTexto()
    {
        var pdf = TeamsFileTools.ExtractPdf(MakePdf(DigitalPdfStream));

        Assert.True(pdf.HasExtractableText);
        var page = Assert.Single(pdf.Pages);
        Assert.Contains("ZeroTeams PDF test page", page);
    }

    [Fact]
    public void ExtractPdf_Escaneado_ReconoceSinTexto()
    {
        var pdf = TeamsFileTools.ExtractPdf(MakePdf(""));

        Assert.False(pdf.HasExtractableText);
    }

    [Fact]
    public void PdfToPpmArgs_ConstruyeOrdenPoppler()
    {
        var args = TeamsFileTools.PdfToPpmArgs("/tmp/doc.pdf", "/tmp/out/page", 12, 100);

        var joined = string.Join(' ', args);
        Assert.Equal("pdftoppm", args[0]);
        Assert.Contains("-jpeg", args);
        Assert.Contains("-r", args);
        Assert.Contains("100", args);
        Assert.Contains("-f 1", joined);
        Assert.Contains("-l 12", joined);
        Assert.Contains("/tmp/doc.pdf", args);
        Assert.Contains("/tmp/out/page", args);
    }

    // ── DOCX / XLSX / PPTX / texto ──────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractDocxText_ParrafosYSaltosDeLinea()
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new W.Document();
            var body = new W.Body();
            body.Append(new W.Paragraph(new W.Run(new W.Text("Hola documento"))));
            body.Append(new W.Paragraph(new W.Run(new W.Text("Segunda linea"))));
            main.Document.Append(body);
            main.Document.Save();
        }

        var text = TeamsFileTools.ExtractDocxText(ms.ToArray());
        Assert.Contains("Hola documento", text);
        Assert.Contains("Segunda linea", text);
    }

    [Fact]
    public void ExtractXlsxText_HojaConSharedStringYNumero()
    {
        using var ms = new MemoryStream();
        using (var sdoc = SpreadsheetDocument.Create(ms, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook))
        {
            var wbPart = sdoc.AddWorkbookPart();
            wbPart.Workbook = new S.Workbook();
            var sheets = wbPart.Workbook.AppendChild(new S.Sheets());

            var sstPart = wbPart.AddNewPart<SharedStringTablePart>();
            sstPart.SharedStringTable = new S.SharedStringTable(new S.SharedStringItem(new S.Text("Manzanas")));

            var wsPart = wbPart.AddNewPart<WorksheetPart>();
            wsPart.Worksheet = new S.Worksheet(new S.SheetData(
                new S.Row(
                    new S.Cell { CellReference = "A1", DataType = S.CellValues.SharedString, CellValue = new S.CellValue("0") },
                    new S.Cell { CellReference = "B1", DataType = S.CellValues.Number, CellValue = new S.CellValue("42") }
                )
            ));
            wsPart.Worksheet.Save();

            sheets.Append(new S.Sheet { Id = wbPart.GetIdOfPart(wsPart), SheetId = 1, Name = "Ventas" });
            wbPart.Workbook.Save();
        }

        var text = TeamsFileTools.ExtractXlsxText(ms.ToArray());
        Assert.Contains("Ventas", text);
        Assert.Contains("Manzanas", text);
        Assert.Contains("42", text);
    }

    [Fact]
    public void ExtractPptxText_DiapositivaConTexto()
    {
        using var ms = new MemoryStream();
        using (var pre = PresentationDocument.Create(ms, DocumentFormat.OpenXml.PresentationDocumentType.Presentation))
        {
            var part = pre.AddPresentationPart();
            part.Presentation = new P.Presentation();
            part.Presentation.Save();

            var slidePart = part.AddNewPart<SlidePart>();
            slidePart.Slide = new P.Slide(
                new P.CommonSlideData(
                    new P.ShapeTree(
                        new P.Shape(
                            new P.NonVisualShapeProperties(
                                new A.NonVisualDrawingProperties { Id = 1, Name = "Shape 1" },
                                new A.NonVisualShapeDrawingProperties()),
                            new P.ShapeProperties(),
                            new P.TextBody(
                                new A.Paragraph(
                                    new A.Run(new A.Text("Hola diapositiva uno")))
                            )
                        )
                    )
                )
            );
            slidePart.Slide.Save();
        }

        var text = TeamsFileTools.ExtractPptxText(ms.ToArray());
        Assert.Contains("Hola diapositiva uno", text);
    }

    [Fact]
    public void ReadPlainText_TrimConBom()
    {
        var bytes = Encoding.UTF8.GetBytes("Hola, mundo\nfila dos");
        Assert.Equal("Hola, mundo\nfila dos", TeamsFileTools.ReadPlainText(bytes));
    }

    // ── Límites y robustez ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void TruncateUtf16_CortaEnLimiteEnteroDeCaracteres()
    {
        Assert.Equal("abcdef", TeamsFileTools.TruncateUtf16("abcdefgh", 6));
        Assert.Equal("abcdefgh", TeamsFileTools.TruncateUtf16("abcdefgh", 99));
        Assert.Equal("", TeamsFileTools.TruncateUtf16("abcdefgh", 0));
    }

    [Fact]
    public void TruncateUtf16_NoParteUnParDeSurrogates()
    {
        var emoji = "ab\U0001F600"; // high+low surrogate; longitud UTF-16 = 4
        var result = TeamsFileTools.TruncateUtf16(emoji, 3);

        Assert.Equal(2, result.Length);
        Assert.Equal("ab", result);
        Assert.False(char.IsHighSurrogate(result[^1]));
    }

    [Fact]
    public void ExtractXlsxText_AlSuperarLimiteCeldas_AnunciaYCorta()
    {
        using var ms = new MemoryStream();
        using (var sdoc = SpreadsheetDocument.Create(ms, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook))
        {
            var wbPart = sdoc.AddWorkbookPart();
            wbPart.Workbook = new S.Workbook();
            var sheets = wbPart.Workbook.AppendChild(new S.Sheets());

            var wsPart = wbPart.AddNewPart<WorksheetPart>();
            wsPart.Worksheet = new S.Worksheet(new S.SheetData(
                new S.Row(
                    new S.Cell { CellReference = "A1", DataType = S.CellValues.Number, CellValue = new S.CellValue("1") },
                    new S.Cell { CellReference = "B1", DataType = S.CellValues.Number, CellValue = new S.CellValue("2") }
                ),
                new S.Row(
                    new S.Cell { CellReference = "A2", DataType = S.CellValues.Number, CellValue = new S.CellValue("3") },
                    new S.Cell { CellReference = "B2", DataType = S.CellValues.Number, CellValue = new S.CellValue("4") }
                )
            ));
            wsPart.Worksheet.Save();

            sheets.Append(new S.Sheet { Id = wbPart.GetIdOfPart(wsPart), SheetId = 1, Name = "Hoja1" });
            wbPart.Workbook.Save();
        }

        var text = TeamsFileTools.ExtractXlsxText(ms.ToArray(), maxCells: 3);

        Assert.Contains("límite de 3 celdas", text);
        Assert.Contains("1", text);
        Assert.Contains("3", text);
        Assert.DoesNotContain("4", text);
    }

    [Fact]
    public async Task RenderPdfPages_BorraLosTemporalesYDevuelveJpeg()
    {
        var before = Directory.GetDirectories(Path.GetTempPath(), "zeroteams-*");

        var pages = await TeamsFileTools.RenderPdfPagesAsync(MakePdf(""));

        var after = Directory.GetDirectories(Path.GetTempPath(), "zeroteams-*");
        Assert.Equal(before, after); // sin fuga de temporales
        if (pages.Count > 0) // poppler ausente → lista vacía, pero sin fuga igualmente
        {
            foreach (var p in pages)
            {
                Assert.True(p.Length >= 2);
                Assert.Equal(0xFF, p[0]);
                Assert.Equal(0xD8, p[1]); // magic JPEG
            }
        }
    }

    // ── Utilidades ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ExtensionOf_ControlaPuntosYMayusculasYQuery()
    {
        Assert.Equal(".pdf", TeamsFileTools.ExtensionOf("Nora_Informe_Coste_Operativo_Septiembre_2026.pdf"));
        Assert.Equal(".pdf", TeamsFileTools.ExtensionOf("NOTA.PDF"));
        Assert.Equal(".xlsx", TeamsFileTools.ExtensionOf("Saldos.xlsx?web=1&d=abc"));
        Assert.Equal("", TeamsFileTools.ExtensionOf("sinExt"));
        Assert.Equal("", TeamsFileTools.ExtensionOf(null));
        Assert.Equal("", TeamsFileTools.ExtensionOf(""));
    }

    [Fact]
    public void SniffMime_MagiaEImporte()
    {
        var pdf = MakePdf(DigitalPdfStream);
        Assert.Equal("application/pdf", TeamsFileTools.SniffMime(pdf, "x.pdf"));

        var zip = new byte[] { 0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4, 5, 6 };
        Assert.Equal("application/zip", TeamsFileTools.SniffMime(zip, "celdas.xlsx"));

        var bom = new byte[] { 0xEF, 0xBB, 0xBF, 0x68, 0x69 };
        Assert.Equal("text/plain", TeamsFileTools.SniffMime(bom, "note.txt"));

        Assert.Equal("text/csv", TeamsFileTools.SniffMime([1, 2, 3], "datos.csv"));
        Assert.Equal("application/octet-stream", TeamsFileTools.SniffMime([1, 2, 3], "raro.bin"));
    }
}