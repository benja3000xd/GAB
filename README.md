# GAB — Gestor de Anuncios de Audio

**GAB** es una aplicación de escritorio para Windows diseñada para programar y reproducir automáticamente anuncios y cuñas de audio, reduciendo de forma inteligente y suave el volumen de otras aplicaciones (como Spotify, navegadores, etc.) mientras suena el anuncio.

---

## 🚀 Características

- **Programación flexible:** Disparo por horas fijas o intervalos regulares (compatible con franjas horarias que cruzan la medianoche).
- **Atenuación inteligente (Audio Ducking):** Reduce suavemente el volumen de otras aplicaciones durante la emisión (*fade-in / fade-out* sin cortes) y lo restaura al finalizar.
- **Prevención de solapamientos:** Impide que dos anuncios coincidan en el mismo horario.
- **Temas Claro y Oscuro:** Compatible con modo claro, modo oscuro y seguimiento automático del tema del sistema Windows.
- **Bandeja del sistema:** Funciona en segundo plano minimizado en la bandeja del sistema (*System Tray*) con opción de inicio automático con Windows.
- **100% Portable:** Ejecutable único sin instalador. Todos sus datos y configuraciones se generan de forma autónoma en el mismo directorio donde se ubica el `.exe`.

---

## 📁 Portabilidad y Datos Locales

GAB es completamente autocontenido y no ensucia el sistema. Se recomienda colocar `GAB.exe` en su propia carpeta (por ejemplo, `C:\GAB\` o en la ubicación que prefieras). 

Al ejecutarse, la aplicación creará automáticamente en ese mismo directorio:
- **`GAB_AUDIOS/`**: Carpeta donde se guardan los audios de los anuncios programados.
- **`anuncios.json`**: Lista de anuncios, horarios y días configurados.
- **`settings.json`**: Preferencias de tema, nivel de ducking, notificaciones e inicio.
- **`logs/`**: Registros diarios de actividad (con limpieza automática de más de 30 días).

---

## 📋 Requisitos

- **Sistema Operativo:** Windows 10 o Windows 11 (x64).
- **SDK:** [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

---

## 🛠️ Cómo Compilar y Ejecutar

1. **Clonar el repositorio:**
   ```bash
   git clone https://github.com/benja3000xd/GAB.git
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
   El ejecutable generado estará en:
   `bin/Release/net8.0-windows/win-x64/publish/GAB.exe`

---

## 📄 Licencia

Este proyecto está bajo la Licencia [MIT](LICENSE).
