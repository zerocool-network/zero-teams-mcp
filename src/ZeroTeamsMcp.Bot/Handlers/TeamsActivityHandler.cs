using System.Text.Json;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Microsoft.Graph;
using ZeroTeamsMcp.Core.Cards;
using ZeroTeamsMcp.Core.Graph;

namespace ZeroTeamsMcp.Bot.Handlers;

public sealed class TeamsActivityHandler : ActivityHandler
{
    private readonly GraphService _graph;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public TeamsActivityHandler(GraphService graph)
    {
        _graph = graph;
    }

    protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken ct)
    {
        var text = turnContext.Activity.Text?.Trim().ToLowerInvariant() ?? "";

        switch (text)
        {
            case "help" or "ayuda":
                await SendCardAsync(turnContext, CardTemplates.CreateWelcomeCard(
                    turnContext.Activity.From?.Name ?? "usuario"), ct);
                break;
            case "chats" or "mis chats":
                await DispatchAsync(turnContext, "list_chats", new { }, ct);
                break;
            case "equipos" or "mis equipos" or "teams":
                await DispatchAsync(turnContext, "list_teams", new { }, ct);
                break;
            default:
                var card = CardTemplates.CreateWelcomeCard(
                    turnContext.Activity.From?.Name ?? "usuario");
                await SendCardAsync(turnContext, card, ct);
                break;
        }
    }

    protected override async Task<InvokeResponse> OnInvokeActivityAsync(
        ITurnContext<IInvokeActivity> turnContext, CancellationToken ct)
    {
        try
        {
            var invoke = turnContext.Activity.Value as JsonElement?;
            if (invoke is null)
                return CreateInvokeResponse(CardTemplates.CreateErrorCard("No se pudo procesar la accion."));

            var action = invoke.Value.GetProperty("action").GetString() ?? "";
            var dataElement = invoke.Value.TryGetProperty("data", out var data) ? data : default;
            var dataDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(dataElement.GetRawText(), JsonOpts) ?? new();

            var card = await DispatchInvokeAsync(action, dataDict, ct);
            return CreateInvokeResponse(card);
        }
        catch (Exception ex)
        {
            return CreateInvokeResponse(CardTemplates.CreateErrorCard(ex.Message));
        }
    }

    private async Task<object> DispatchInvokeAsync(
        string action, Dictionary<string, JsonElement> data, CancellationToken ct)
    {
        return action switch
        {
            "list_chats" => await HandleListChatsAsync(ct),
            "list_teams" => await HandleListTeamsAsync(ct),
            "get_chat_messages" => await HandleGetMessagesAsync(GetString(data, "chatId"), ct),
            "list_channels" => await HandleListChannelsAsync(GetString(data, "teamId"), ct),
            "send_channel_message_prompt" => CardTemplates.CreateSendPromptCard(
                GetString(data, "channelId"),
                GetString(data, "channelName") ?? "canal"),
            _ => CardTemplates.CreateErrorCard($"Accion desconocida: {action}")
        };
    }

    private async Task<object> HandleListChatsAsync(CancellationToken ct)
    {
        var client = await _graph.GetClientAsync(ct: ct);
        var result = await client.Chats.GetAsync(cfg => cfg.QueryParameters.Top = 20, ct);

        var chats = result?.Value?.Select(c => (object)new Dictionary<string, object>
        {
            ["Id"] = c.Id ?? "",
            ["Topic"] = c.Topic ?? "Sin nombre",
            ["ChatType"] = c.ChatType?.ToString() ?? "?"
        }).ToList() ?? [];

        return CardTemplates.CreateChatListCard(chats);
    }

    private async Task<object> HandleListTeamsAsync(CancellationToken ct)
    {
        var client = await _graph.GetClientAsync(ct: ct);
        var result = await client.Teams.GetAsync(cfg => cfg.QueryParameters.Top = 20, ct);

        var teams = result?.Value?.Select(t => (object)new Dictionary<string, object>
        {
            ["Id"] = t.Id ?? "",
            ["DisplayName"] = t.DisplayName ?? "Sin nombre"
        }).ToList() ?? [];

        return CardTemplates.CreateTeamListCard(teams);
    }

    private async Task<object> HandleGetMessagesAsync(string chatId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(chatId))
            return CardTemplates.CreateErrorCard("Falta chatId.");

        var client = await _graph.GetClientAsync(ct: ct);
        var result = await client.Chats[chatId].Messages.GetAsync(cfg => cfg.QueryParameters.Top = 20, ct);

        var messages = result?.Value?.Select(m => (object)new Dictionary<string, object>
        {
            ["Id"] = m.Id ?? "",
            ["From"] = m.From?.User?.DisplayName ?? "?",
            ["Content"] = m.Body?.Content ?? "",
            ["CreatedDateTime"] = m.CreatedDateTime?.ToString("g") ?? ""
        }).ToList() ?? [];

        return CardTemplates.CreateMessageListCard(chatId, messages);
    }

    private async Task<object> HandleListChannelsAsync(string teamId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(teamId))
            return CardTemplates.CreateErrorCard("Falta teamId.");

        var client = await _graph.GetClientAsync(ct: ct);
        var result = await client.Teams[teamId].Channels.GetAsync(cfg => cfg.QueryParameters.Top = 20, ct);

        var channels = result?.Value?.Select(ch => (object)new Dictionary<string, object>
        {
            ["Id"] = ch.Id ?? "",
            ["DisplayName"] = ch.DisplayName ?? "?"
        }).ToList() ?? [];

        return CardTemplates.CreateChannelListCard(teamId, channels);
    }

    private async Task DispatchAsync(ITurnContext turnContext, string action, object data, CancellationToken ct)
    {
        var card = await DispatchInvokeAsync(action, new Dictionary<string, JsonElement>(), ct);
        await SendCardAsync(turnContext, card, ct);
    }

    private static async Task SendCardAsync(ITurnContext turnContext, object card, CancellationToken ct)
    {
        var attachment = new Attachment
        {
            ContentType = "application/vnd.microsoft.card.adaptive",
            Content = card
        };
        var reply = MessageFactory.Attachment(attachment);
        await turnContext.SendActivityAsync(reply, ct);
    }

    private static new InvokeResponse CreateInvokeResponse(object card)
    {
        return new InvokeResponse
        {
            Status = 200,
            Body = card
        };
    }

    private static string GetString(Dictionary<string, JsonElement> data, string key)
    {
        return data.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? ""
            : "";
    }
}
