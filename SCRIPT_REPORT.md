# Reporte de scripts del proyecto

## LLM.cs
Componente central para levantar el servidor LLM. Permite configurar la ejecución local o remota, selección de modelo y plantilla de chat, opciones de rendimiento (GPU, lotes, hilos, capas offloaded) y extras como LORA o claves de API. Expone estados estáticos `started` y `failed` para que otros componentes validen si el servidor está listo.

## LLMCharacter.cs
Caller principal que arma el prompt con historial, maneja streaming, slots/caché y persistencia del historial. Expone parámetros de generación (semilla, temperatura, top-k/p, penalizaciones, Mirostat) y coordina las llamadas locales mediante `PostRequestLocal`, disparando callbacks durante el streaming.

## LLMCharacterStats.cs
Extiende el caller añadiendo métricas globales como tokens por segundo, tokens de contexto restantes y tokens usados en total. Actualiza estas métricas durante el streaming y ofrece un cálculo aproximado del contexto disponible a partir de la tokenización de mensajes.

## LLMChatTemplates.cs
Registro de plantillas de chat (p. ej. ChatML, Alpaca, Gemma, Mistral, Llama, Phi, DeepSeek, Vicuna, Zephyr, Qwen, BitNet). Incluye detección por nombre o por contenido Jinja y un flag `thinkingMode` que puede activarse globalmente.

## MobileDemoOvertoneChatBubbles.cs
Controlador para la demo móvil con burbujas de chat y TTS Overtone. Inicializa la UI (input, botón, toggle), realiza warmup del LLM, gestiona el envío del usuario y controla la reproducción de voz con colas de oraciones y efectos de sonido.

## LLMStatsUIUpdater.cs
Coroutine que refresca periódicamente textos de UI con TPS y tokens de contexto restantes, con método manual `UpdateAllStats` para forzar un refresco inmediato.

## TPSUpdater.cs
Componente sencillo de UI que actualiza cada frame un `TextMeshProUGUI` con los tokens por segundo. Registra un error en `Start` si falta la referencia.

## ContextUpdater.cs
Componente que actualiza cada frame el `TextMeshProUGUI` asignado con los tokens de contexto restantes usando `LLMCharacterStats.RemainingContextTokens`. En `Start` valida que la referencia haya sido asignada y reporta un error si falta.

## Oportunidades de mejora
- Cambiar métodos `async void` (ej. en métricas) por `Task` para propagar errores y permitir cancelación.
- Evitar duplicación entre `LLMCharacter` y `LLMCharacterStats` extrayendo una base común para métricas y persistencia.
- Robustecer los componentes de UI (TPS/Context) desactivándolos o usando `RequireComponent`/`[SerializeField]` para prevenir referencias nulas en runtime.
- Permitir carga/edición de plantillas de chat externas en lugar de registros hardcodeados, para mayor extensibilidad.
