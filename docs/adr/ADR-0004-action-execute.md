# ADR-0004: Action.Execute para Adaptive Cards

## Estado

**Aprobado** — 2026-09-05

## Contexto

El bot de Teams necesita presentar Adaptive Cards interactivas que permitan a los usuarios seleccionar opciones, enviar datos y recibir respuestas actualizadas. Las Adaptive Cards en Teams soportan dos tipos de acciones principales:

1. **Action.Submit**: Envía datos del formulario al canal de messaging (no al bot)
2. **Action.Execute**: Envía un invoke activity directamente al bot

### Limitaciones de Action.Submit

- Solo funciona en conversaciones 1:1 (chat privado con el bot)
- No funciona en canales de Teams
- No genera invoke activity → el bot no puede procesar la acción
- Input validation limitada al lado del cliente
- No permite lógica de negocio server-side antes de procesar

### Ventajas de Action.Execute

- Funciona en canales, chats grupales y 1:1
- Genera invoke activity (`action/execute`) → el bot recibe y procesa
- Permite validación server-side completa
- El bot puede actualizar la card después de la acción
- Soporta todos los tipos de inputs de Adaptive Cards

---

## Decisión

Usamos **exclusivamente Action.Execute** para todas las interacciones del bot con Adaptive Cards.

### Justificación

1. **Compatibilidad universal**: Funciona en todos los contextos de Teams
2. **Control server-side**: El bot valida y procesa cada acción
3. **Experiencia fluida**: Card se actualiza in-place después de la acción
4. **Seguridad**: Validación de input en el servidor, no solo en cliente
5. **Consistencia**: Un solo patrón para todas las interacciones

### Implementación

```csharp
// Handler genérico para Action.Execute
public class ActionExecuteHandler : IActivityHandler
{
    public async Task<InvokeResponse> HandleInvokeAsync(
        InvokeActivity invokeActivity,
        CancellationToken cancellationToken)
    {
        var actionData = invokeActivity.Value as JObject;
        var verb = actionData["verb"]?.ToString();

        return verb switch
        {
            "list-channels" => await HandleListChannels(actionData, cancellationToken),
            "list-messages" => await HandleListMessages(actionData, cancellationToken),
            "send-message" => await HandleSendMessage(actionData, cancellationToken),
            _ => CreateErrorResponse("Acción desconocida")
        };
    }
}
```

### Template de Adaptive Card

```json
{
  "type": "AdaptiveCard",
  "body": [
    {
      "type": "TextBlock",
      "text": "Selecciona un equipo",
      "style": "heading",
      "color": "accent",
      "fontType": "monospace"
    },
    {
      "type": "Input.ChoiceSet",
      "id": "teamId",
      "label": "Equipo",
      "choices": [
        { "title": "Equipo A", "value": "team-a-id" },
        { "title": "Equipo B", "value": "team-b-id" }
      ]
    }
  ],
  "actions": [
    {
      "type": "Action.Execute",
      "title": "Ver canales",
      "verb": "list-channels",
      "data": {
        "action": "list-channels"
      }
    }
  ]
}
```

### Flujo completo

```
1. Bot recibe mensaje del usuario
2. Bot consulta Graph API → obtiene datos
3. Bot construye Adaptive Card con Action.Execute
4. Bot envía card al usuario
5. Usuario selecciona opción y hace clic
6. Teams envía invoke activity (action/execute) al bot
7. Bot recibe invoke → deserializa actionData
8. Bot procesa según verb → consulta Graph API
9. Bot construye card actualizada
10. Bot responde con card actualizada (in-place update)
```

### Ejemplo de respuesta del bot

```csharp
// Después de procesar Action.Execute
var updatedCard = new AdaptiveCard
{
    Body = new List<BaseElement>
    {
        new TextBlock
        {
            Text = $"Canales de {teamName}",
            Style = TextBlockStyle.Heading,
            Color = TextColor.Accent
        },
        // ... lista de canales
    },
    Actions = new List<BaseAction>
    {
        new ActionExecute
        {
            Title = "Ver mensajes",
            Verb = "list-messages",
            Data = new { channelId = "selected-channel-id" }
        }
    }
};

return new InvokeResponse
{
    Status = 200,
    Body = new { card = updatedCard.ToJson() }
};
```

---

## Consecuencias

### Positivas

- Compatibilidad con todos los contextos de Teams (canales, chats, 1:1)
- Validación server-side completa de todas las acciones
- Cards se actualizan in-place (mejor UX)
- Un solo patrón de código para todas las interacciones
- El bot tiene control total sobre el flujo

### Negativas

- Requiere manejo de invoke activities (más complejo que submit)
- El bot debe estar registrado correctamente en Azure AD para recibir invokes
- Errores en el handler causan mensajes de error en Teams
- El invoke activity tiene timeout (debe responder rápido)

### Mitigaciones

- Handler genérico con pattern matching por verb
- Timeouts configurados en el adapter
- Error handling robusto con mensajes amigables
- Tests unitarios para cada verb handler
- Health check del bot endpoint
