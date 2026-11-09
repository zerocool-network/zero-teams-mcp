# Teams de Optima Retail

Consulta de Teams, calendario, correo y OneDrive. No hace falta login de Microsoft. En el cliente solo va este JSON.

## Pegar en Cursor o Claude Desktop

En Cursor, dentro de `mcpServers` en `~/.cursor/mcp.json`. En Claude Desktop, la misma forma en `claude_desktop_config.json`.

```json
"optimaretail-teams": {
  "url": "https://mcp-teams.optimaonline.eu/mcp",
  "headers": {
    "X-Api-Key": "zk_DNgDAiBZqAsi92biYpNFoCefoFXft5_FfQw0dV6E-N4"
  }
}
```

Guarda y recarga los MCP. El servidor tiene que aparecer conectado.

El correo se lee solo con este MCP. No uses Outlook ni otro conector de Microsoft 365. Para un periodo indica las fechas, si es bandeja o enviados, y si buscas adjuntos, un remitente, un destinatario (email o dominio, también en Enviados), una palabra del asunto o del cuerpo, o un asunto. Si la respuesta trae `before`, vuelve a pedir con ese valor.

## Casos de uso

`userId` es el email de Microsoft 365 de la persona. El id de un chat sale de la lista de chats.

- «Lista los equipos de Teams.»
- «Lista los 10 últimos chats de ana.perez@optimaretail.es.»
- «Lee los últimos mensajes del chat `<id>`.»
- «Qué reuniones tiene ana.perez@optimaretail.es esta semana.»
- «Describe la imagen del mensaje `<id>` en ese chat.»
- «Lee el texto del fichero adjunto al mensaje `<id>`.»
- «Lista los 10 últimos correos de ana.perez@optimaretail.es.»
- «Lista los correos de ana.perez@optimaretail.es entre el 2026-10-13 y el 2026-10-27.»
- «Lista los enviados de ana.perez@optimaretail.es de esas dos semanas que tengan adjuntos.»
- «Busca en ese periodo los correos de ese buzón cuyo asunto contenga el texto que te pida.»
- «Busca en Enviados los correos a un email o un dominio, o cuyo texto contenga una palabra. Si son varias palabras, pásalas juntas: son una frase.»
- «Lista los adjuntos de un correo y lee el texto de uno.»
- «Guarda el adjunto de un correo en disco. La respuesta trae la ruta y una url, no el archivo.»
- «Busca el email actual de una persona por su nombre.»
- «Lista los buzones de la empresa. Si la respuesta trae next, sigue con ese next.»
- «Si la respuesta trae before, vuelve a pedir los anteriores pasando ese before.»
- «Abre el mensaje `<id>` de ese buzón.»
- «Lista la raíz del OneDrive de ana.perez@optimaretail.es.»
- «Busca un archivo en todo el tenant, por ejemplo filename:informe.pdf. No recorras usuario por usuario.»

Estos casos son de lectura. Enviar un mensaje de Teams, crear un chat o convocar una reunión no es uno de ellos.
