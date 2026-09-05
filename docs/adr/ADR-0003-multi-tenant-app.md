# ADR-0003: App Multi-Tenant Única

## Estado

**Aprobado** — 2026-09-05

## Contexto

zero-teams-mcp debe acceder a Microsoft Teams de múltiples organizaciones (tenants). Cada tenant tiene su propio directorio de Azure AD con usuarios, equipos y recursos independientes.

### Opciones consideradas

1. **App multi-tenant única**: Un registro de app en Azure AD que sirve a todos los tenants
2. **App por tenant**: Cada tenant registra su propia aplicación
3. **App single-tenant + permisos de aplicación**: App en un tenant con acceso cross-tenant (no posible para Teams)

### Factores relevantes

- El modelo client credentials requiere admin consent por tenant
- Cada tenant ejecuta su propio host MCP (proceso aislado)
- La app debe poder ser instalada por admins de tenant sin intervención manual
- El registro de app se gestiona desde el ControlPlane

---

## Decisión

Usamos **una sola aplicación multi-tenant** registrada en Azure AD, con consentimiento admin por tenant.

### Justificación

1. **Un solo registro**: El admin de cada tenant solo necesita aprobar una app
2. **Escalable**: Añadir un nuevo tenant no requiere crear nuevos recursos en Azure
3. **Gestión centralizada**: Permisos y actualizaciones se manejan en un solo lugar
4. **Consent admin**: Cada tenant autoriza explícitamente la app
5. **Host aislado**: Cada tenant ejecuta su propio proceso con sus credenciales

### Configuración en Azure AD

```
App Registration
├── Nombre: "Zero Teams MCP"
├── Tipo: Accounts in any organizational directory (Multitenant)
├── Redirect URI: https://<controlplane>/auth/callback
├── Client ID: [generado]
├── Client Secret: [generado, solo en env]
└── API Permissions:
    ├── User.Read.All (Delegado)
    ├── Team.ReadBasic.All (Delegado)
    ├── Channel.ReadBasic.All (Delegado)
    ├── ChannelMessage.Read (Delegado)
    ├── ChannelMessage.Send (Delegado)
    ├── Chat.Read.All (Delegado)
    ├── Chat.ReadWrite.All (Delegado)
    └── ChatMessage.Send (Delegado)
```

### Flujo de consentimiento por tenant

```
1. Admin del tenant accede a:
   https://login.microsoftonline.com/{tenant-id}/adminconsent
     ?client_id={app-client-id}
     &redirect_uri={controlplane}/auth/callback

2. Azure AD muestra los permisos solicitados

3. Admin aprueba (consentimiento global para el tenant)

4. ControlPlane recibe callback con:
   - tenant_id
   - state (token CSRF)
   - consent_status: "approved"

5. ControlPlane almacena tenant_id + estado

6. Host MCP configurado con ese tenant_id puede operar
```

### Aislamiento por tenant

```
Host A (tenant "acme.onmicrosoft.com")
├── TENANT_ID: xxx-xxx-xxx
├── CLIENT_ID: [misma app]
├── CLIENT_SECRET: [misma secret]
├── GraphServiceClient → acme.onmicrosoft.com
└── Scope: acme.onmicrosoft.com

Host B (tenant "globex.onmicrosoft.com")
├── TENANT_ID: yyy-yyy-yyy
├── CLIENT_ID: [misma app]
├── CLIENT_SECRET: [misma secret]
├── GraphServiceClient → globex.onmicrosoft.com
└── Scope: globex.onmicrosoft.com
```

---

## Consecuencias

### Positivas

- Un solo registro de app para todos los tenants
- Consentimiento admin simple y documentado
- Permisos actualizados una vez, aplicados a todos
- ControlPlane centraliza la gestión de tenants
- Cada host es independiente (aislamiento de proceso)

### Negativas

- Client secret compartido entre tenants (mitigado: misma app = misma secret)
- Un tenant puede ver los permisos solicitados a otros (no los datos)
- Requiere consent admin (no self-service para usuarios finales)
- Límite de 100 apps por tenant en Azure AD (no es problema: somos 1)

### Mitigaciones

- Client secret en variables de entorno, nunca en código
- Secret rotation periódica via ControlPlane
- Cada host aísla su token cache en memoria
- Logging de consentimiento para auditoría
