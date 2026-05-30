# EmailBroker — Roadmap

> Generado el 2026-05-29 tras completar batch-endpoint + validation gaps.
> Estado actual: **34 tests pasando, 0 warnings, 0 errors** en 6 proyectos.

---

## Fase 1 — Polly (Resiliencia)

**Esfuerzo**: Small · **Dependencias**: Ninguna · **Valor**: Tolerancia a fallos transitorios

Decorador `ResilientEmailSender` que wrappea `IEmailSender` y aplica:
- Retry con exponential backoff + jitter (3 intentos)
- Circuit breaker (evita llamar a un provider caído)
- Solo sobre excepciones transitorias (timeout, network, rate limit 429)

Registrado en DI wrappeando el provider activo. Sin cambios de interfaz.

**Archivos esperados**:
- `src/EmailBroker.Providers/Resilience/ResilientEmailSender.cs`
- `src/EmailBroker.Providers/Resilience/ResiliencePolicies.cs`
- Tests en `tests/EmailBroker.Providers.Tests/`

---

## Fase 2A — Webhooks (Delivery Status)

**Esfuerzo**: Medium-Large · **Dependencias**: Introduce DB · **Valor**: Feedback de entrega

Endpoints y modelos para recibir eventos de los providers:
- `POST /api/webhooks/resend` — verificación de firma HMAC
- Modelos: `WebhookEvent`, `DeliveryStatus` (delivered, bounced, complained, opened, clicked)
- Persistencia de eventos (primera DB del sistema)
- Opcional: reenvío a URL configurable (passthrough)

**Decisión clave**: SQLite (dev) / PostgreSQL (prod).

---

## Fase 2B — Nuevos Providers

**Esfuerzo**: Medium (Small por provider) · **Paralelizable con 2A**

Cada provider nuevo sigue el patrón existente:
1. SDK / NuGet dependency
2. `XyzEmailSender : IEmailSender` (~150 lines)
3. `XyzOptions` class
4. `XyzHealthCheck`
5. Tests unitarios

Candidatos: **SendGrid**, **Amazon SES**, **Mailgun**.

---

## Fase 3 — Templates de Email

**Esfuerzo**: Large · **Dependencias**: DB (de Fase 2A)

Motor de renderizado para plantillas de email:
- **Liquid** (recomendado: más simple que Razor para emails)
- CRUD de templates (POST/GET/PUT/DELETE /api/templates)
- Renderizado con variables → `EmailMessage` completo
- Almacenamiento en DB (file-based como MVP inicial)

Nuevo proyecto potencial: `EmailBroker.Templates`

---

## Fase 4 — Dashboard / Métricas

**Esfuerzo**: Large · **Dependencias**: Webhooks (Fase 2A)

Observabilidad:
- OpenTelemetry + Prometheus (mínimo impacto en código)
- Métricas: volumen de envíos, tasa de bounce, latencia por provider
- Opcional: admin UI custom vs Grafana dashboards

---

## Notas Arquitectónicas

| Decisión | Implicancia |
|----------|-------------|
| Polly como decorator | Zero cambios en interfaces o proyectos existentes |
| DB con webhooks | Primera vez que el sistema toca persistencia. Elegir bien: SQLite → PostgreSQL |
| Providers son paralelizables | Cada uno es aislado, toca solo `Providers/` project |
| Templates son large | Scope crece rápido. Mantener MVP acotado: Liquid + file-based, sin UI |
| Dashboard post-webhooks | Sin datos de entrega, las métricas son pobres |
