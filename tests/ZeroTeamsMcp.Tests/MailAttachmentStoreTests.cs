using ZeroTeamsMcp.Server.Services;

namespace ZeroTeamsMcp.Tests;

public class MailAttachmentStoreTests
{
    [Fact]
    public void GuardaEnDisco_SinDevolverBytes()
    {
        var root = Path.Combine(Path.GetTempPath(), "zt-att-" + Guid.NewGuid().ToString("n"));
        try
        {
            var bytes = "%PDF-1.4 hola"u8.ToArray();
            var saved = MailAttachmentStore.Save("informe.pdf", "application/pdf", bytes, root: root);
            Assert.Equal("informe.pdf", saved.Name);
            Assert.Equal(bytes.Length, saved.Size);
            Assert.True(File.Exists(saved.Path));
            Assert.Equal(bytes, File.ReadAllBytes(saved.Path));
            Assert.DoesNotContain("base64", File.ReadAllText(Path.Combine(root, saved.Id, "meta.json")), StringComparison.OrdinalIgnoreCase);

            var opened = MailAttachmentStore.Open(saved.Id, root);
            Assert.NotNull(opened);
            Assert.Equal(saved.Path, opened.Value.Path);
            Assert.Equal("application/pdf", opened.Value.ContentType);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NombreConRuta_SeQuedaEnElArchivo()
    {
        Assert.Equal("informe.pdf", MailAttachmentStore.SafeName("../secreto/informe.pdf"));
        Assert.Equal("adjunto", MailAttachmentStore.SafeName(".."));
    }

    [Fact]
    public void IdAjeno_NoAbre()
    {
        var root = Path.Combine(Path.GetTempPath(), "zt-att-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Null(MailAttachmentStore.Open("../" + new string('a', 32), root));
            Assert.Null(MailAttachmentStore.Open("no-es-un-id", root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
