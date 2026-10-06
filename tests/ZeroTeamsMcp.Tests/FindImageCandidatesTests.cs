using Microsoft.Graph.Models;
using ModelContextProtocol.Protocol;
using ZeroTeamsMcp.Server.Tools;
using Xunit;

namespace ZeroTeamsMcp.Tests;

public class FindImageCandidatesTests
{
    // Shape REAL verificado 2026-10-05 (tenant Optima, chat 19:9b04bb1…@thread.v2, msgs 1791198527412 / …8404576 /
    // …8162075 / …5756602): attachments vacío, imagen solo en body.content, hostedContents[0].contentType = null.
    public const string RealBody =
        "<p><img src=\"https://graph.microsoft.com/v1.0/chats/19:9b04bb1002bb4a83918cbae9d7cebb9c@thread.v2/messages/1791198527412/hostedContents/4::aHR0cHM6Ly9ldS1hcGkuYXNtLnNreXBlLmNvbS92MS9vYmplY3RzLzAtZnJjYS1kNGQ5ZTdmYTlkYTcxMDJkZjc4MWYxMDU2MzNlMzZiZDM3ZA/$value\" width=\"357\" height=\"130\" alt=\"imagen\" itemid=\"0-frca-d4-d9e7afd9aa7102dd781f105633e36bd3\"></p>";

    [Fact]
    public void ShapeReal_AttachmentsVacios_EncuentraImgEnElBody()
    {
        var hosted = new[] { new ChatMessageHostedContent { Id = "abc123", ContentType = null } };

        var candidates = TeamsImageTools.FindImageCandidates([], hosted, RealBody);

        var c = Assert.Single(candidates);
        Assert.Equal("body<img src>", c.Source);
        Assert.Contains("/hostedContents/", c.Url);
        Assert.EndsWith("/$value", c.Url);
        Assert.Null(c.HostedContentId);
    }

    [Fact]
    public void Attachment_ImagePng_ConContentUrl_EsCandidato()
    {
        var attachments = new[]
        {
            new ChatMessageAttachment { ContentType = "image/png", ContentUrl = "https://graph.microsoft.com/v1.0/x/$value", Name = "captura.png" },
            new ChatMessageAttachment { ContentType = "text/plain", ContentUrl = "https://ejemplo.com/nota.txt", Name = "nota.txt" }
        };

        var candidates = TeamsImageTools.FindImageCandidates(attachments, null, "<p>mirad esto</p>");

        var c = Assert.Single(candidates);
        Assert.Equal("attachment.contentUrl", c.Source);
        Assert.Equal("https://graph.microsoft.com/v1.0/x/$value", c.Url);
    }

    [Fact]
    public void Attachment_SinContentType_PeroConNombrePng_TambienEsCandidato()
    {
        // La URL real de skype no lleva extensión ("/views/img?token=x"); el nombre del adjunto sí.
        var attachments = new[]
        {
            new ChatMessageAttachment
            {
                ContentType = null,
                ContentUrl = "https://eu-api.asm.skype.com/v1/objects/0-frca/views/img?token=x",
                Name = "captura.png"
            }
        };

        var candidates = TeamsImageTools.FindImageCandidates(attachments, null, "<p>sin cuerpo</p>");

        var c = Assert.Single(candidates);
        Assert.Equal("attachment.contentUrl", c.Source);
    }

    [Fact]
    public void HostedContent_SinContentType_SinImgEnBody_CaeAFallback()
    {
        var hosted = new[]
        {
            new ChatMessageHostedContent { Id = "hc-1", ContentType = null },
            new ChatMessageHostedContent { Id = "hc-2", ContentType = "application/pdf" }
        };

        var candidates = TeamsImageTools.FindImageCandidates(null, hosted, "<p>Solo texto</p>");

        var c = Assert.Single(candidates);
        Assert.Equal("hostedContent (tipo desconocido)", c.Source);
        Assert.Equal("hc-1", c.HostedContentId);
    }

    [Fact]
    public void HostedContent_ConContentTypeImage_TienePrioridadYNoDuplica()
    {
        var hosted = new[]
        {
            new ChatMessageHostedContent { Id = "hc-img", ContentType = "image/jpeg" }
        };

        var candidates = TeamsImageTools.FindImageCandidates(null, hosted, "<p>texto</p>");

        var c = Assert.Single(candidates);
        Assert.Equal("hostedContent", c.Source);
        Assert.Equal("hc-img", c.HostedContentId);
    }

    [Fact]
    public void SinAdjuntosNiImgNiHostedContents_NoHayCandidatos()
    {
        var candidates = TeamsImageTools.FindImageCandidates(null, [], "<p>Mensaje normal de texto</p>");

        Assert.Empty(candidates);
    }

    [Fact]
    public void BodyConVariasImagenes_DevuelveTodasSinDuplicar()
    {
        var body = "<p><img src=\"https://graph.microsoft.com/v1.0/a/$value\"></p>" +
                   "<p><img src=\"https://graph.microsoft.com/v1.0/b/$value\"></p>" +
                   "<p><img src=\"https://graph.microsoft.com/v1.0/a/$value\"></p>";

        var candidates = TeamsImageTools.FindImageCandidates([], null, body);

        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, c => Assert.Equal("body<img src>", c.Source));
    }

    [Fact]
    public void BodyConSrcset_UsaLaPrimeraUrlDelSrcset()
    {
        var body = "<p><img srcset=\"https://graph.microsoft.com/v1.0/grande.png 1x, https://graph.microsoft.com/v1.0/pequeno.png 2x\" alt=\"imagen\"></p>";

        var candidates = TeamsImageTools.FindImageCandidates([], null, body);

        Assert.Equal(2, candidates.Count);
        Assert.Contains(candidates, c => c.Url!.EndsWith("/grande.png"));
        Assert.Contains(candidates, c => c.Url!.EndsWith("/pequeno.png"));
    }
}

public class ListChatImagesProjectionTests
{
    private static ChatMessage Msg(string id, string? from, string? body, DateTimeOffset? created = null) => new()
    {
        Id = id,
        From = from is null ? null : new ChatMessageFromIdentitySet { User = new Identity { DisplayName = from, Id = "u-" + from } },
        Body = body is null ? null : new ItemBody { Content = body, ContentType = BodyType.Html },
        CreatedDateTime = created ?? new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero)
    };

    [Fact]
    public void MensajeSinImagenes_NoDevuelveFila()
    {
        var row = TeamsImageTools.ProjectImageRow(Msg("m1", "Fran", "<p>Hola, ¿qué tal?</p>"));

        Assert.Null(row);
    }

    [Fact]
    public void MensajeConImagen_DevuelveFilaConDimensionesDelTag()
    {
        var row = TeamsImageTools.ProjectImageRow(Msg("m2", "Noe", FindImageCandidatesTests.RealBody));

        Assert.NotNull(row);
        Assert.Equal("m2", row!.MessageId);
        Assert.Equal("Noe", row.From);
        Assert.Equal(1, row.ImageCount);
        Assert.Equal(357, row.Images[0].Width);
        Assert.Equal(130, row.Images[0].Height);
        Assert.Equal("body<img src>", row.Images[0].Source);
    }

    [Fact]
    public void Excerpt_SinHtmlNiEntidades()
    {
        var excerpt = TeamsImageTools.Excerpt("<p>Os paso la captura &amp; el informe</p>");

        Assert.Equal("Os paso la captura & el informe", excerpt);
    }

    [Fact]
    public void ApplySniffedContentTypes_RellenaVaciosYConservaLosConocidos()
    {
        var row = new TeamsImageTools.ImageRow("m3", "Fran", "u-Fran", DateTimeOffset.UnixEpoch, 2,
        [
            new TeamsImageTools.ImageInfo(0, "body<img src>", 357, 130, null),
            new TeamsImageTools.ImageInfo(1, "attachment.contentUrl", null, null, "image/jpeg")
        ], "hola");
        var candidates = new List<TeamsImageTools.ImageCandidate>
        {
            new("body<img src>", "https://graph.microsoft.com/v1.0/chats/c/messages/m3/hostedContents/h1/$value", null, null, null),
            new("attachment.contentUrl", "https://x/doc.png", null, "image/jpeg", "doc.png")
        };

        var resolved = TeamsImageTools.ApplySniffedContentTypes(row, candidates, ["image/png", null]);

        Assert.Equal("image/png", resolved.Images[0].ContentType);
        Assert.Equal("image/jpeg", resolved.Images[1].ContentType);
        Assert.Equal(357, resolved.Images[0].Width);
        Assert.Equal(2, resolved.ImageCount);
    }

    [Fact]
    public void ApplySniffedContentTypes_SinSniffedDevuelveMismaFila()
    {
        var row = new TeamsImageTools.ImageRow("m4", "Noe", "u-Noe", DateTimeOffset.UnixEpoch, 1,
        [
            new TeamsImageTools.ImageInfo(0, "body<img src>", 100, 50, null)
        ], "hola");
        var candidates = new List<TeamsImageTools.ImageCandidate>
        {
            new("body<img src>", "https://graph.microsoft.com/v1.0/x/hostedContents/h2/$value", null, null, null)
        };

        var resolved = TeamsImageTools.ApplySniffedContentTypes(row, candidates, [null]);

        Assert.Same(row, resolved);
        Assert.Null(resolved.Images[0].ContentType);
    }

    [Fact]
    public void ApplySniffedContentTypes_NoPisaContentTypeExistente()
    {
        var row = new TeamsImageTools.ImageRow("m5", "Luis", "u-Luis", DateTimeOffset.UnixEpoch, 1,
        [
            new TeamsImageTools.ImageInfo(0, "attachment.contentUrl", null, null, "image/gif")
        ], "hola");
        var candidates = new List<TeamsImageTools.ImageCandidate>
        {
            new("attachment.contentUrl", "https://x/a.gif", null, "image/gif", null)
        };

        var resolved = TeamsImageTools.ApplySniffedContentTypes(row, candidates, ["image/png"]);

        Assert.Equal("image/gif", resolved.Images[0].ContentType);
    }

    [Fact]
    public void Excerpt_TruncaA200()
    {
        var excerpt = TeamsImageTools.Excerpt(new string('x', 300));

        Assert.Equal(201, excerpt.Length);
        Assert.EndsWith("…", excerpt);
    }

    [Theory]
    [InlineData("Fran", false)]
    [InlineData("fran ortiz", false)]   // substring case-insensitive sobre el displayName completo
    [InlineData("Noe", false)]
    [InlineData("Maruan", false)]
    [InlineData("Luis", false)]
    [InlineData("Pedro", true)]        // fuera de la lista: se excluye
    public void FiltroPorNombre_SubstringCaseInsensitive(string name, bool excluido)
    {
        var names = TeamsImageTools.SplitNames("Fran,Noe,Luis,Miguel,Maruan");

        Assert.Equal(excluido, TeamsImageTools.ShouldExclude(name, names, null, null));
    }

    [Fact]
    public void FiltroPorNombre_SinNombres_NoExcluye()
    {
        Assert.False(TeamsImageTools.ShouldExclude("Luis", [], null, null));
    }

    [Fact]
    public void FiltroPorFecha_ExcluyeLoAnterior()
    {
        var since = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var viejo = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        var nuevo = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);

        Assert.True(TeamsImageTools.ShouldExclude("Fran", [], viejo, since));
        Assert.False(TeamsImageTools.ShouldExclude("Fran", [], nuevo, since));
    }

    [Fact]
    public void SplitNames_TrimYVaciosACero()
    {
        var names = TeamsImageTools.SplitNames(" Fran , Noe ,,Luis ");

        Assert.Equal(["Fran", "Noe", "Luis"], names);
        Assert.Empty(TeamsImageTools.SplitNames(null));
        Assert.Empty(TeamsImageTools.SplitNames("   "));
    }
}

public class ImageContentBlockTests
{
    [Fact]
    public void BloqueImagen_DevuelveLosMismosBytesYMismosMetadatos()
    {
        byte[] raw = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
        var image = new TeamsImageTools.CachedImage("m1", "image/png", raw, 357, 130);
        var message = new ChatMessage
        {
            Id = "m1",
            From = new ChatMessageFromIdentitySet { User = new Identity { DisplayName = "Fran", Id = "u1" } },
            CreatedDateTime = new DateTimeOffset(2026, 10, 5, 10, 30, 0, TimeSpan.Zero)
        };
        var candidate = new TeamsImageTools.ImageCandidate("body<img src>", "https://graph.microsoft.com/v1.0/x/$value", null, "image/png", null);

        var blocks = TeamsImageTools.BuildContentBlocks(message, candidate, total: 1, index: 0, image, cached: false);

        Assert.Equal(2, blocks.Count);

        var text = Assert.IsType<TextContentBlock>(blocks[0]);
        Assert.Contains("1/1", text.Text);
        Assert.Contains("Fran", text.Text);
        Assert.Contains("357x130px", text.Text);
        Assert.Contains("body<img src>", text.Text);
        Assert.Contains("Prompt sugerido", text.Text);

        var img = Assert.IsType<ImageContentBlock>(blocks[1]);
        Assert.Equal("image/png", img.MimeType);
        // Data guarda el base64 en UTF-8; DecodedData debe devolver los bytes originales.
        Assert.True(img.DecodedData.ToArray().SequenceEqual(raw));
    }

    [Fact]
    public void ImagenGrande_IncluyeAvisoDeReescalado()
    {
        var png = new byte[]
        {
            0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,
            0x00,0x00,0x00,0x0D, 0x49,0x48,0x44,0x52,
            0x00,0x00,0x06,0x93, 0x00,0x00,0x01,0x2C,
            0x08,0x06,0x00,0x00, 0x00
        };
        var image = new TeamsImageTools.CachedImage("m1", "image/png", png, 1683, 300);

        var blocks = TeamsImageTools.BuildContentBlocks(null, new TeamsImageTools.ImageCandidate("body<img src>", "https://x/$value", null, null, null), 1, 0, image, true);

        var text = Assert.IsType<TextContentBlock>(blocks[0]);
        Assert.Contains("Aviso:", text.Text);
        Assert.Contains("no se ha vuelto a descargar", text.Text);
        Assert.Contains("1683x300px", text.Text);
    }
}

public class ImageDimensionsTests
{
    [Fact]
    public void JpegLeeDimensionesDelSof0()
    {
        // SOI + SOF0 (0xFFC0) con longitud 0x0011, precision 8, height 0x012C (300), width 0x0693 (1683).
        var jpeg = new byte[]
        {
            0xFF, 0xD8,
            0xFF, 0xC0, 0x00, 0x11, 0x08,
            0x01, 0x2C,
            0x06, 0x93,
            0x03
        };

        var dims = TeamsImageTools.ImageDimensions(jpeg);

        Assert.NotNull(dims);
        Assert.Equal(1683, dims.Value.Width);
        Assert.Equal(300, dims.Value.Height);
        Assert.NotNull(TeamsImageTools.OversizeNote(jpeg));
    }

    [Fact]
    public void GifLeeDimensionesDeLaCabecera()
    {
        var gif = new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 0x00, 0x01, 0x2C, 0x01 };

        var dims = TeamsImageTools.ImageDimensions(gif);

        Assert.NotNull(dims);
        Assert.Equal(256, dims.Value.Width);   // 0x0100 little-endian
        Assert.Equal(300, dims.Value.Height);
    }
}

public class PngDimensionsTests
{
    [Fact]
    public void LeeWidthYHeightDeIHDR()
    {
        // PNG 1x1 real (67 bytes) truncado no: construimos solo cabecera + IHDR.
        var png = new byte[]
        {
            0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,
            0x00,0x00,0x00,0x0D, 0x49,0x48,0x44,0x52,
            0x00,0x00,0x06,0x93, // width  = 0x693 = 1683
            0x00,0x00,0x01,0x2C, // height = 0x12C = 300
            0x08,0x06,0x00,0x00,0x00
        };

        var dims = TeamsImageTools.PngDimensions(png);

        Assert.NotNull(dims);
        Assert.Equal(1683, dims.Value.Width);
        Assert.Equal(300, dims.Value.Height);
        Assert.NotNull(TeamsImageTools.OversizeNote(png));
    }

    [Fact]
    public void ImagenPequena_No_avisa()
    {
        var png = new byte[]
        {
            0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,
            0x00,0x00,0x00,0x0D, 0x49,0x48,0x44,0x52,
            0x00,0x00,0x01,0x65, // 357
            0x00,0x00,0x00,0x82, // 130
            0x08,0x06,0x00,0x00,0x00
        };

        Assert.Null(TeamsImageTools.OversizeNote(png));
    }

    [Fact]
    public void BytesNoPng_NoLanzaYDevuelveNull()
    {
        Assert.Null(TeamsImageTools.PngDimensions([1, 2, 3]));
    }
}