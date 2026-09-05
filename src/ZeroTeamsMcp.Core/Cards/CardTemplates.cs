namespace ZeroTeamsMcp.Core.Cards;

public static class CardTemplates
{
    private static string Get(IDictionary<string, object>? dict, string key, string fallback = "")
    {
        if (dict is null) return fallback;
        return dict.TryGetValue(key, out var val) ? val?.ToString() ?? fallback : fallback;
    }
    public static object CreateWelcomeCard(string userName)
    {
        return new
        {
            type = "AdaptiveCard",
            version = "1.5",
            body = new object[]
            {
                new { type = "TextBlock", text = $"Hola {userName}", weight = "Bolder", size = "Large", wrap = true },
                new { type = "TextBlock", text = "Soy ZeroTeams MCP. Puedo ayudarte con:", wrap = true, spacing = "Medium" },
                new { type = "TextBlock", text = "• Listar tus chats de Teams\n• Ver canales de un equipo\n• Enviar mensajes\n• Gestionar miembros", wrap = true, spacing = "Small", isSubtle = true }
            },
            actions = new object[]
            {
                new { type = "Action.Execute", title = "Mis chats", data = new { action = "list_chats" } },
                new { type = "Action.Execute", title = "Mis equipos", data = new { action = "list_teams" } }
            }
        };
    }

    public static object CreateChatListCard(IReadOnlyList<object> chats)
    {
        var body = new List<object>
        {
            new { type = "TextBlock", text = "Tus chats", weight = "Bolder", size = "Large", wrap = true }
        };
        var actions = new List<object>();

        if (chats.Count == 0)
        {
            body.Add(new { type = "TextBlock", text = "No se encontraron chats.", wrap = true, isSubtle = true });
        }
        else
        {
            foreach (var chat in chats.Take(10))
            {
                var dict = chat as IDictionary<string, object>;
                var topic = Get(dict, "Topic", "Sin nombre");
                var chatType = Get(dict, "ChatType", "?");
                var chatId = Get(dict, "Id");

                body.Add(new { type = "TextBlock", text = $"{topic} ({chatType})", weight = "Normal", size = "Medium", wrap = true, spacing = "Medium" });

                if (!string.IsNullOrEmpty(chatId))
                    actions.Add(new { type = "Action.Execute", title = $"Ver mensajes: {topic}", data = new { action = "get_chat_messages", chatId } });
            }
        }

        return new { type = "AdaptiveCard", version = "1.5", body = body.ToArray(), actions = actions.ToArray() };
    }

    public static object CreateMessageListCard(string chatId, IReadOnlyList<object> messages)
    {
        var body = new List<object>
        {
            new { type = "TextBlock", text = "Mensajes del chat", weight = "Bolder", size = "Large", wrap = true }
        };

        if (messages.Count == 0)
        {
            body.Add(new { type = "TextBlock", text = "No hay mensajes.", wrap = true, isSubtle = true });
        }
        else
        {
            foreach (var msg in messages.Take(10))
            {
                var dict = msg as IDictionary<string, object>;
                var from = Get(dict, "From", "?");
                var content = Get(dict, "Content");
                body.Add(new { type = "TextBlock", text = $"{from}: {content}", wrap = true, spacing = "Small", size = "Small" });
            }
        }

        return new
        {
            type = "AdaptiveCard",
            version = "1.5",
            body = body.ToArray(),
            actions = new object[]
            {
                new { type = "Action.Execute", title = "Volver a chats", data = new { action = "list_chats" } }
            }
        };
    }

    public static object CreateTeamListCard(IReadOnlyList<object> teams)
    {
        var body = new List<object>
        {
            new { type = "TextBlock", text = "Tus equipos", weight = "Bolder", size = "Large", wrap = true }
        };
        var actions = new List<object>();

        if (teams.Count == 0)
        {
            body.Add(new { type = "TextBlock", text = "No se encontraron equipos.", wrap = true, isSubtle = true });
        }
        else
        {
            foreach (var team in teams.Take(10))
            {
                var dict = team as IDictionary<string, object>;
                var name = Get(dict, "DisplayName", "Sin nombre");
                var teamId = Get(dict, "Id");

                body.Add(new { type = "TextBlock", text = name, weight = "Normal", size = "Medium", spacing = "Medium" });

                if (!string.IsNullOrEmpty(teamId))
                    actions.Add(new { type = "Action.Execute", title = $"Canales: {name}", data = new { action = "list_channels", teamId } });
            }
        }

        return new { type = "AdaptiveCard", version = "1.5", body = body.ToArray(), actions = actions.ToArray() };
    }

    public static object CreateChannelListCard(string teamId, IReadOnlyList<object> channels)
    {
        var body = new List<object>
        {
            new { type = "TextBlock", text = "Canales del equipo", weight = "Bolder", size = "Large", wrap = true }
        };
        var actions = new List<object>();

        if (channels.Count == 0)
        {
            body.Add(new { type = "TextBlock", text = "No hay canales.", wrap = true, isSubtle = true });
        }
        else
        {
            foreach (var ch in channels.Take(10))
            {
                var dict = ch as IDictionary<string, object>;
                var name = Get(dict, "DisplayName", "?");
                var chId = Get(dict, "Id");

                body.Add(new { type = "TextBlock", text = $"# {name}", weight = "Normal", size = "Medium", spacing = "Small" });

                if (!string.IsNullOrEmpty(chId))
                    actions.Add(new { type = "Action.Execute", title = $"Enviar a #{name}", data = new { action = "send_channel_message_prompt", teamId, channelId = chId, channelName = name } });
            }
        }

        actions.Add(new { type = "Action.Execute", title = "Volver a equipos", data = new { action = "list_teams" } });

        return new { type = "AdaptiveCard", version = "1.5", body = body.ToArray(), actions = actions.ToArray() };
    }

    public static object CreateSendPromptCard(string channelId, string channelName)
    {
        return new
        {
            type = "AdaptiveCard",
            version = "1.5",
            body = new object[]
            {
                new { type = "TextBlock", text = $"Enviar mensaje a #{channelName}", weight = "Bolder", size = "Medium", wrap = true },
                new { type = "TextBlock", text = "Escribe tu mensaje en el chat. Luego haz clic en Enviar.", wrap = true, isSubtle = true }
            },
            actions = Array.Empty<object>()
        };
    }

    public static object CreateSentConfirmationCard(string channelName, string content)
    {
        return new
        {
            type = "AdaptiveCard",
            version = "1.5",
            body = new object[]
            {
                new { type = "TextBlock", text = $"Mensaje enviado a #{channelName}", weight = "Bolder", size = "Medium", wrap = true, color = "Good" },
                new { type = "TextBlock", text = content, wrap = true, isSubtle = true, size = "Small" }
            },
            actions = new object[]
            {
                new { type = "Action.Execute", title = "Volver a equipos", data = new { action = "list_teams" } }
            }
        };
    }

    public static object CreateErrorCard(string message)
    {
        return new
        {
            type = "AdaptiveCard",
            version = "1.5",
            body = new object[]
            {
                new { type = "TextBlock", text = "Error", weight = "Bolder", size = "Medium", color = "Attention" },
                new { type = "TextBlock", text = message, wrap = true, isSubtle = true }
            },
            actions = Array.Empty<object>()
        };
    }

}
