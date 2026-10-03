# GAB — Gestor de Anuncios de Audio

**GAB** es una aplicación de escritorio para Windows diseñada para programar y reproducir automáticamente anuncios y cuñas de audio, reduciendo de forma inteligente y suave el volumen de otras aplicaciones (como Spotify, navegadores, etc.) mientras suena el anuncio.

---

## 🚀 Características

- **Programación flexible:** Disparo por horas fijas o intervalos regulares (compatible con franjas horarias que cruzan la medianoche).
- **Atenuación inteligente (Audio Ducking):** Reduce suavemente el volumen de otras aplicaciones durante la emisión (*fade-in / fade-out* sin cortes) y lo restaura al finalizar.
- **Prevención de solapamientos:** Impide que dos anuncios coincidan en el mismo horario.
- **Temas Claro y Oscuro:** Compatible con modo claro, modo oscuro y seguimiento automático del tema del sistema Windows.
- **Bandeja del sistema:** Funciona en segundo plano minimizado en la bandeja del sistema (*System Tray*) con opción de inicio automático con Windows.
- **Portable:** Se compila en un único archivo ejecutable (`GAB.exe`) sin necesidad de instalación.

---

## 📋 Requisitos

- **Sistema Operativo:** Windows 10 o Windows 11 (x64).
- **SDK:** [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

---

## 🛠️ Cómo Compilar y Ejecutar

1. **Clonar el repositorio:**
   ```bash
   git clone https://github.com/TU_USUARIO/GAB.git
   cd GAB
   ```

2. **Compilar y ejecutar:**
   ```bash
   dotnet run
   ```

3. **Generar el ejecutable portable (`GAB.exe`):**
   ```bash
   dotnet publish GAB.csproj -c Release -r win-x64 --no-self-contained
   ```
   El archivo listo para usar se generará en:
   `bin/Release/net8.0-windows/win-x64/publish/GAB.exe`

---

## 📄 Licencia

Este proyecto está bajo la Licencia [MIT](LICENSE).
