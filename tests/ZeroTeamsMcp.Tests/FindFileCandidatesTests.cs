using Microsoft.Graph.Models;
using ZeroTeamsMcp.Server.Tools;

namespace ZeroTeamsMcp.Tests;

public class FindFileCandidatesTests
{
    // Shapes REALES verificados 2026-10-08 (tenant Optima, app-only): los ficheros son
    // attachments[].contentType='reference' con name real y contentUrl OneDrive directo.
    // msg 1790759023381 → "Nora_Informe_Coste_Operativo_Septiembre_2026.pdf";
    // msgs 1789730915804 / 1788867928262 / 1788190592549 → xlsx.
    public const string RealSharePointUrl =
        "https://optimaretail-my.sharepoint.com/personal/fullod_optimaretail_com/Documents/Archivos de chat de Microsoft Teams/Nora_Informe_Coste_Operativo_Septiembre_2026.pdf";

    [Fact]
    public void ShapeReal_AttachmentReferencePdf_EsCandidato()
    {
        var attachments = new[]
        {
            new ChatMessageAttachment
            {
                ContentType = "reference",
                ContentUrl = RealSharePointUrl,
                Name = "Nora_Informe_Coste_Operativo_Septiembre_2026.pdf",
            }
        };

        var c = Assert.Single(TeamsFileTools.FindFileCandidates(attachments));
        Assert.Equal("attachment.reference", c.Source);
        Assert.Equal(RealSharePointUrl, c.Url);
        Assert.Equal("Nora_Informe_Coste_Operativo_Septiembre_2026.pdf", c.Name);
    }

    [Fact]
    public void AttachmentReference_SinContentUrl_NoCandidato()
    {
        var attachments = new[]
        {
            new ChatMessageAttachment { ContentType = "reference", ContentUrl = null, Name = "roto.pdf" }
        };

        Assert.Empty(TeamsFileTools.FindFileCandidates(attachments));
    }

    [Fact]
    public void AttachmentMessageReference_Excluida()
    {
        var attachments = new[]
        {
            new ChatMessageAttachment
            {
                ContentType = "messageReference",
                ContentUrl = "https://graph.microsoft.com/v1.0/teams/x/channels/y/messages/z",
                Name = "cita"
            }
        };

        Assert.Empty(TeamsFileTools.FindFileCandidates(attachments));
    }

    [Fact]
    public void AttachmentTarjetaAdaptativa_Excluida()
    {
        var attachments = new[]
        {
            new ChatMessageAttachment
            {
                ContentType = "application/vnd.microsoft.card.adaptive",
                ContentUrl = "https://graph.microsoft.com/v1.0/chats/1/messages/2/hostedContents/3/$value",
                Name = "card"
            }
        };

        Assert.Empty(TeamsFileTools.FindFileCandidates(attachments));
    }

    [Fact]
    public void AttachmentMedia_ImagenAudioVideo_Excluidos()
    {
        var attachments = new[]
        {
            new ChatMessageAttachment { ContentType = "image/png",    ContentUrl = "https://x/img.png",  Name = "img.png" },
            new ChatMessageAttachment { ContentType = "audio/mp3",    ContentUrl = "https://x/a.mp3",    Name = "a.mp3" },
            new ChatMessageAttachment { ContentType = "video/mp4",    ContentUrl = "https://x/v.mp4",    Name = "v.mp4" },
            new ChatMessageAttachment { ContentType = "reference",    ContentUrl = "https://x/foto.gif", Name = "anim.gif" }
        };

        Assert.Empty(TeamsFileTools.FindFileCandidates(attachments));
    }

    [Fact]
    public void DosFicheros_ReferencePdfYXlsx_DosCandidatos()
    {
        var attachments = new[]
        {
            new ChatMessageAttachment
            {
                ContentType = "reference",
                ContentUrl = RealSharePointUrl,
                Name = "Nora_Informe_Coste_Operativo_Septiembre_2026.pdf"
            },
            new ChatMessageAttachment
            {
                ContentType = "reference",
                ContentUrl = "https://optimaretail-my.sharepoint.com/personal/x/Documents/Archivos de chat de Microsoft Teams/Saldos.xlsx",
                Name = "Saldos.xlsx"
            }
        };

        var candidates = TeamsFileTools.FindFileCandidates(attachments);
        Assert.Equal(2, candidates.Count);
        Assert.Equal(".pdf", TeamsFileTools.ExtensionOf(candidates[0].Name));
        Assert.Equal(".xlsx", TeamsFileTools.ExtensionOf(candidates[1].Name));
    }

    [Fact]
    public void MismaUrlDuplicada_SoloUnCandidato()
    {
        var attachments = new[]
        {
            new ChatMessageAttachment { ContentType = "reference", ContentUrl = RealSharePointUrl, Name = "a.pdf" },
            new ChatMessageAttachment { ContentType = "reference", ContentUrl = RealSharePointUrl, Name = "b.pdf" }
        };

        Assert.Single(TeamsFileTools.FindFileCandidates(attachments));
    }

    [Fact]
    public void AttachmentNull_Vacio()
    {
        Assert.Empty(TeamsFileTools.FindFileCandidates(null));
    }
}