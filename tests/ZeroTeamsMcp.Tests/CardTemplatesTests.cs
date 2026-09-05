using System.Text.Json;
using ZeroTeamsMcp.Core.Cards;

namespace ZeroTeamsMcp.Tests;

public class CardTemplatesTests
{
    [Fact]
    public void CreateWelcomeCard_ReturnsAdaptiveCard()
    {
        var card = CardTemplates.CreateWelcomeCard("TestUser");
        var json = JsonSerializer.Serialize(card);
        Assert.Contains("AdaptiveCard", json);
        Assert.Contains("TestUser", json);
        Assert.Contains("list_chats", json);
        Assert.Contains("list_teams", json);
    }

    [Fact]
    public void CreateChatListCard_Empty_ReturnsEmptyMessage()
    {
        var card = CardTemplates.CreateChatListCard([]);
        var json = JsonSerializer.Serialize(card);
        Assert.Contains("No se encontraron chats", json);
    }

    [Fact]
    public void CreateChatListCard_WithChats_ReturnsChatTopics()
    {
        var chats = new List<object>
        {
            new Dictionary<string, object>
            {
                ["Id"] = "chat-1",
                ["Topic"] = "Mi Chat",
                ["ChatType"] = "oneOnOne"
            }
        };

        var card = CardTemplates.CreateChatListCard(chats);
        var json = JsonSerializer.Serialize(card);
        Assert.Contains("Mi Chat", json);
        Assert.Contains("chat-1", json);
        Assert.Contains("get_chat_messages", json);
    }

    [Fact]
    public void CreateTeamListCard_Empty_ReturnsEmptyMessage()
    {
        var card = CardTemplates.CreateTeamListCard([]);
        var json = JsonSerializer.Serialize(card);
        Assert.Contains("No se encontraron equipos", json);
    }

    [Fact]
    public void CreateTeamListCard_WithTeams_ReturnsTeamNames()
    {
        var teams = new List<object>
        {
            new Dictionary<string, object>
            {
                ["Id"] = "team-1",
                ["DisplayName"] = "Mi Equipo"
            }
        };

        var card = CardTemplates.CreateTeamListCard(teams);
        var json = JsonSerializer.Serialize(card);
        Assert.Contains("Mi Equipo", json);
        Assert.Contains("list_channels", json);
    }

    [Fact]
    public void CreateErrorCard_ReturnsError()
    {
        var card = CardTemplates.CreateErrorCard("Algo fallo");
        var json = JsonSerializer.Serialize(card);
        Assert.Contains("Error", json);
        Assert.Contains("Algo fallo", json);
    }

    [Fact]
    public void CreateMessageListCard_WithMessages_ReturnsFromAndContent()
    {
        var messages = new List<object>
        {
            new Dictionary<string, object>
            {
                ["Id"] = "msg-1",
                ["From"] = "Juan",
                ["Content"] = "Hola mundo",
                ["CreatedDateTime"] = "05/09/2026 10:00"
            }
        };

        var card = CardTemplates.CreateMessageListCard("chat-1", messages);
        var json = JsonSerializer.Serialize(card);
        Assert.Contains("Juan", json);
        Assert.Contains("Hola mundo", json);
    }

    [Fact]
    public void CreateChannelListCard_Empty_ReturnsEmptyMessage()
    {
        var card = CardTemplates.CreateChannelListCard("team-1", []);
        var json = JsonSerializer.Serialize(card);
        Assert.Contains("No hay canales", json);
    }
}
