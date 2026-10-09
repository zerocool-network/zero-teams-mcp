using System.IO.Compression;
using System.Text;
using ZeroTeamsMcp.Server.Tools;

namespace ZeroTeamsMcp.Tests;

public class ZipExtractionTests
{
    private static byte[] BuildZip(params (string name, byte[] bytes)[] files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            foreach (var (name, bytes) in files)
            {
                var entry = zip.CreateEntry(name);
                using var es = entry.Open();
                es.Write(bytes);
            }
        }
        return ms.ToArray();
    }

    private static byte[] UTF8(string s) => Encoding.UTF8.GetBytes(s);
    private static byte[] Bytes(int n) => new byte[n];

    // ── ZipStructureText ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ZipStructureText_ListaFicherosYDirectorios()
    {
        var zip = BuildZip(
            ("nota.txt", UTF8("hola")),
            ("docs/", Array.Empty<byte>()),
            ("docs/spec.md", UTF8("# spec")));

        var text = TeamsFileTools.ZipStructureText(zip);

        Assert.Contains("nota.txt", text);
        Assert.Contains("[DIR] docs/", text);
        Assert.Contains("docs/spec.md", text);
    }

    [Fact]
    public void ZipStructureText_RespetaLimiteDeEntradas()
    {
        var zip = BuildZip(
            ("a.txt", UTF8("a")),
            ("b.txt", UTF8("b")),
            ("c.txt", UTF8("c")));

        var text = TeamsFileTools.ZipStructureText(zip, maxEntries: 2);

        Assert.Contains("a.txt", text);
        Assert.Contains("b.txt", text);
        Assert.Contains("límite", text);
        Assert.DoesNotContain("c.txt", text);
    }

    [Fact]
    public void ZipStructureText_ZipVacio()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
        }

        Assert.Contains("ZIP vacío", TeamsFileTools.ZipStructureText(ms.ToArray()), StringComparison.OrdinalIgnoreCase);
    }

    // ── ExtractZipText ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractZipText_ExtraeTextoDeNuevasSoportadas()
    {
        var zip = BuildZip(
            ("nota.txt", UTF8("hola mundo")),
            ("datos.csv", UTF8("1,2,3")));

        var text = TeamsFileTools.ExtractZipText(zip);

        Assert.Contains("nota.txt", text);
        Assert.Contains("hola mundo", text);
        Assert.Contains("datos.csv", text);
        Assert.Contains("1,2,3", text);
    }

    [Fact]
    public void ExtractZipText_SpecOpenSpecAnidadaEnCarpertas()
    {
        var zip = BuildZip((
            "openspec/proposals/0llm-ampliado/proposal.md",
            UTF8("# Ampliar 0LLM\n\n- H1\n- H2\n\n## Contexto\n\nMejora el agente.")));

        var text = TeamsFileTools.ExtractZipText(zip);

        Assert.Contains("openspec/proposals/0llm-ampliado/proposal.md", text);
        Assert.Contains("# Ampliar 0LLM", text);
        Assert.Contains("H1", text);
        Assert.Contains("Mejora el agente", text);
    }

    [Fact]
    public void ExtractZipText_EntradaSinSoporte_SeAnotaSinRomper()
    {
        var zip = BuildZip(
            ("logo.png", Bytes(8)),
            ("nota.txt", UTF8("texto")));

        var text = TeamsFileTools.ExtractZipText(zip);

        Assert.Contains("logo.png", text);
        Assert.Contains("sin soporte", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("texto", text);
    }

    [Fact]
    public void ExtractZipText_ZipAnidado_NoSeExplora()
    {
        var zip = BuildZip(("lib/inner.zip", Bytes(64)));

        var text = TeamsFileTools.ExtractZipText(zip);

        Assert.Contains("inner.zip", text);
        Assert.Contains("sin soporte", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExtractZipText_EntradaMayorQueTope_SeAnotaSinLeer()
    {
        var zip = BuildZip(
            ("grande.txt", Bytes(1024)),
            ("peque.txt", UTF8("ok")));

        var text = TeamsFileTools.ExtractZipText(zip, maxFileBytes: 512);

        Assert.Contains("grande.txt", text);
        Assert.Contains("supera", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ok", text);
    }

    [Fact]
    public void ExtractZipText_RespetaLimiteDeDocumentos()
    {
        var zip = BuildZip(
            ("a.txt", UTF8("primero")),
            ("b.txt", UTF8("segundo")));

        var text = TeamsFileTools.ExtractZipText(zip, maxTextDocs: 1);

        Assert.Contains("primero", text);
        Assert.Contains("sin extraer", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("segundo", text);
    }

    [Fact]
    public void ExtractZipText_ZipVacio_DejaAviso()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
        }

        var text = TeamsFileTools.ExtractZipText(ms.ToArray());

        Assert.Contains("ZIP vacío", text, StringComparison.OrdinalIgnoreCase);
    }
}