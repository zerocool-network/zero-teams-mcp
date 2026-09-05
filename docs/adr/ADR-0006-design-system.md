# ADR-0006: shadcn/ui + Neón M365 Design System

## Estado

**Aprobado** — 2026-09-05

## Contexto

El panel de administración de zero-teams-mcp necesita una interfaz de usuario moderna, accesible y visualmente consistente. El proyecto se integra con el ecosistema Microsoft 365, por lo que el diseño debe reflejar esa identidad sin ser una copia directa.

### Opciones consideradas

1. **shadcn/ui + tema Neón M365**: Componentes copiables, tema personalizado
2. **Fluent UI v9**: Design system oficial de Microsoft
3. **Tailwind UI**: Licencia comercial, temas predefinidos
4. **Chakra UI**: Librería completa, tema customizable
5. **Componentes propios**: Desde cero, máximo control

### Factores relevantes

- shadcn/ui no es una librería: es un conjunto de componentes copiables
- Tema personalizado vía CSS variables (shadcn soporta esto nativamente)
- Next.js 15 App Router con React Server Components
- Neón M365: estética tech, colores eléctricos, gradientes sutiles
- Accesibilidad (WCAG 2.1 AA) es requisito

---

## Decisión

Usamos **shadcn/ui** como base de componentes con un **tema Neón M365** personalizado.

### Justificación

1. **Componentes copiables**: No hay dependencia de paquete externo; el código es nuestro
2. **Tema vía CSS variables**: Fácil de personalizar sin forks
3. **Tailwind CSS**: Utilidad-first, optimizado para Next.js
4. **Accesibilidad**: shadcn/ui implementa WAI-ARIA patterns
5. **Comunidad activa**: Componentes actualizados regularmente
6. **No lock-in**: Si cambiamos, el código es nuestro

### Tema Neón M365

```css
/* globals.css */
:root {
  /* Brand Microsoft */
  --m365-brand: #0078D4;
  --m365-brand-light: #2B88D8;
  --m365-brand-dark: #005A9E;

  /* Neón accent */
  --neon-cyan: #00E5FF;
  --neon-cyan-glow: rgba(0, 229, 255, 0.15);

  /* Superficies */
  --bg-primary: #1B1B1F;
  --bg-secondary: #242428;
  --bg-tertiary: #2C2C30;
  --border-default: #333336;
  --border-hover: #44444A;

  /* Texto */
  --text-primary: #FFFFFF;
  --text-secondary: #ADADAD;
  --text-muted: #6B6B70;

  /* Estados */
  --success: #4CAF50;
  --warning: #FFC107;
  --error: #F44336;
  --info: #2196F3;

  /* shadcn/ui variables */
  --background: 240 6% 10%;
  --foreground: 0 0% 100%;
  --card: 240 6% 14%;
  --card-foreground: 0 0% 100%;
  --primary: 207 90% 51%;
  --primary-foreground: 0 0% 100%;
  --secondary: 240 4% 16%;
  --secondary-foreground: 0 0% 100%;
  --muted: 240 4% 16%;
  --muted-foreground: 0 0% 63%;
  --accent: 187 100% 50%;
  --accent-foreground: 0 0% 100%;
  --destructive: 0 84% 60%;
  --destructive-foreground: 0 0% 100%;
  --border: 240 4% 21%;
  --input: 240 4% 21%;
  --ring: 207 90% 51%;
  --radius: 0.5rem;
}
```

### Componentes shadcn/ui utilizados

| Componente | Uso en el panel |
|------------|----------------|
| `Button` | Acciones primarias, secundarias, destructivas |
| `Card` | Tenants, stats, health status |
| `Dialog` | Confirmaciones, formularios |
| `DropdownMenu` | Acciones de tenant (editar, eliminar) |
| `Input` | Formularios de tenant |
| `Select` | Filtros, selección de tenant |
| `Table` | Lista de tenants, logs |
| `Tabs` | Navegación entre vistas |
| `Toast` | Notificaciones de éxito/error |
| `Badge` | Estados de tenant (active, error, pending) |
| `Skeleton` | Loading states |
| `Avatar` | Usuarios, tenants |

### Estructura de componentes

```
apps/controlplane/
├── src/
│   ├── app/
│   │   ├── layout.tsx          # Root layout con tema
│   │   ├── page.tsx            # Dashboard
│   │   ├── tenants/
│   │   │   ├── page.tsx        # Lista de tenants
│   │   │   └── [id]/page.tsx   # Detalle de tenant
│   │   └── health/page.tsx     # Health dashboard
│   ├── components/
│   │   ├── ui/                 # shadcn/ui components (copiados)
│   │   ├── neon/               # Componentes Neón custom
│   │   │   ├── NeonCard.tsx
│   │   │   ├── NeonBadge.tsx
│   │   │   ├── NeonButton.tsx
│   │   │   └── NeonGlow.tsx
│   │   ├── dashboard/
│   │   │   ├── StatsGrid.tsx
│   │   │   ├── TenantList.tsx
│   │   │   └── HealthStatus.tsx
│   │   └── layout/
│   │       ├── Sidebar.tsx
│   │       ├── Header.tsx
│   │       └── Footer.tsx
│   └── lib/
│       ├── api.ts              # Fetch wrapper para ControlPlane API
│       └── utils.ts            # cn() utility de shadcn
```

### Estilos Neón personalizados

```tsx
// NeonCard.tsx
export function NeonCard({ children, ...props }) {
  return (
    <Card
      className={cn(
        "relative overflow-hidden",
        "border border-[--border-default]",
        "bg-[--bg-secondary]",
        "hover:border-[--neon-cyan] hover:shadow-[0_0_20px_var(--neon-cyan-glow)]",
        "transition-all duration-300",
        props.className
      )}
      {...props}
    >
      {children}
      {/* Glow effect en hover */}
      <div className="absolute inset-0 opacity-0 hover:opacity-100 transition-opacity pointer-events-none">
        <div className="absolute inset-0 bg-gradient-to-br from-[--neon-cyan-glow] to-transparent" />
      </div>
    </Card>
  );
}
```

### Accesibilidad

- **WCAG 2.1 AA**: Todos los componentes shadcn/ui cumplen este nivel
- **Keyboard navigation**: Tab order, focus visible, escape para dialogs
- **Screen readers**: Labels, ARIA attributes, live regions para toasts
- **Contraste**: Colores del tema verificados contra WCAG AA (4.5:1 texto, 3:1 UI)
- **Reduced motion**: Animaciones deshabilitadas con `prefers-reduced-motion`

---

## Consecuencias

### Positivas

- Componentes copiables: sin dependencia de paquete externo
- Tema personalizable vía CSS variables (cambio en un archivo)
- Tailwind CSS: utilidades atómicas, optimizado para Next.js
- Accesibilidad incluida por defecto
- Comunidad activa, componentes actualizados regularmente
- Neón M365: identidad visual única, diferenciada de Fluent UI

### Negativas

- Componentes copiados requieren mantenimiento manual
- shadcn/ui actualiza componentes; debemos sincronizar manualmente
- Tailwind learning curve para el equipo
- Tema custom puede romper con actualizaciones de shadcn/ui

### Mitigaciones

- Script de sincronización para actualizar componentes
- Documentación del tema Neón M365
- Tests visuales para detectar regressiones de diseño
- Documentación de decisiones de diseño en ADR
