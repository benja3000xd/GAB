using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using GAB.Models;

namespace GAB.Services
{
    public static class StorageManager
    {
        private static readonly string AppDataFolder = AppDomain.CurrentDomain.BaseDirectory;
        private static readonly string AudiosFolder = Path.Combine(AppDataFolder, "GAB_AUDIOS");
        private static readonly string LogsFolder = Path.Combine(AppDataFolder, "logs");
        private static readonly string JsonPath = Path.Combine(AppDataFolder, "anuncios.json");
        private static readonly string SettingsJsonPath = Path.Combine(AppDataFolder, "settings.json");
        private static readonly object _logLock = new object();
        private static readonly JsonSerializerOptions _jsonLoadOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        static StorageManager()
        {
            if (!Directory.Exists(AudiosFolder))
            {
                Directory.CreateDirectory(AudiosFolder);
            }
            if (!Directory.Exists(LogsFolder))
            {
                Directory.CreateDirectory(LogsFolder);
            }
        }

        public static string AudiosFolderPath => AudiosFolder;
        public static string LogsFolderPath => LogsFolder;
        public static string SettingsFilePath => SettingsJsonPath;

        public static string ObtenerRutaValidaAudio(string? ruta)
        {
            if (string.IsNullOrWhiteSpace(ruta))
                return string.Empty;

            if (File.Exists(ruta))
                return Path.GetFullPath(ruta);

            // Si no existe, verificar si existe dentro de la carpeta GAB_AUDIOS local por nombre de archivo
            var nombreArchivo = Path.GetFileName(ruta);
            var rutaLocal = Path.Combine(AudiosFolder, nombreArchivo);
            if (File.Exists(rutaLocal))
                return Path.GetFullPath(rutaLocal);

            // Verificar si es relativa a AppDataFolder
            var rutaRelativa = Path.Combine(AppDataFolder, ruta);
            if (File.Exists(rutaRelativa))
                return Path.GetFullPath(rutaRelativa);

            return string.Empty;
        }

        public static List<AnuncioModel> CargarAnuncios()
        {
            if (!File.Exists(JsonPath))
                return new List<AnuncioModel>();

            try
            {
                var json = File.ReadAllText(JsonPath);
                var lista = JsonSerializer.Deserialize<List<AnuncioModel>>(json, _jsonLoadOptions) ?? new List<AnuncioModel>();
                
                // Resolver y auto-corregir rutas que hayan cambiado de disco o ubicación
                foreach (var anuncio in lista)
                {
                    var rutaValida = ObtenerRutaValidaAudio(anuncio.RutaAudio);
                    if (!string.IsNullOrEmpty(rutaValida))
                    {
                        anuncio.RutaAudio = rutaValida;
                    }
                }
                return lista;
            }
            catch (Exception ex)
            {
                LogError("Error al cargar y deserializar anuncios.json", ex);
                return new List<AnuncioModel>();
            }
        }

        public static void GuardarAnuncios(List<AnuncioModel> anuncios)
        {
            var json = JsonSerializer.Serialize(anuncios, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(JsonPath, json);
        }

        public static AppSettings CargarConfiguracion()
        {
            if (!File.Exists(SettingsJsonPath))
            {
                var defaults = new AppSettings
                {
                    IniciarConWindows = AutostartService.IsAutoStartEnabled()
                };
                return defaults;
            }

            try
            {
                var json = File.ReadAllText(SettingsJsonPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonLoadOptions) ?? new AppSettings();
                return settings;
            }
            catch (Exception ex)
            {
                LogError("Error al cargar settings.json", ex);
                return new AppSettings();
            }
        }

        public static void GuardarConfiguracion(AppSettings settings)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsJsonPath, json);
            }
            catch (Exception ex)
            {
                LogError("Error al guardar settings.json", ex);
            }
        }

        public static void AbrirCarpetaAudios()
        {
            try
            {
                if (!Directory.Exists(AudiosFolder))
                    Directory.CreateDirectory(AudiosFolder);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = AudiosFolder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LogError("Error al abrir carpeta de audios", ex);
            }
        }

        public static void AbrirCarpetaLogs()
        {
            try
            {
                if (!Directory.Exists(LogsFolder))
                    Directory.CreateDirectory(LogsFolder);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = LogsFolder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LogError("Error al abrir carpeta de logs", ex);
            }
        }

        public static string ProcesarArchivoAudio(string rutaOriginal)
        {
            var rutaValida = ObtenerRutaValidaAudio(rutaOriginal);
            if (string.IsNullOrEmpty(rutaValida) || !File.Exists(rutaValida))
            {
                if (!File.Exists(rutaOriginal))
                    throw new FileNotFoundException("El archivo de audio no existe.", rutaOriginal);
                rutaValida = rutaOriginal;
            }

            // Si el archivo ya se encuentra dentro de GAB_AUDIOS, retornar su ruta directamente
            var fullRutaValida = Path.GetFullPath(rutaValida);
            var fullAudiosFolder = Path.GetFullPath(AudiosFolder);
            if (fullRutaValida.StartsWith(fullAudiosFolder, StringComparison.OrdinalIgnoreCase))
            {
                return fullRutaValida;
            }

            string nombreOriginal = Path.GetFileName(rutaValida);
            string nombreSinExt = Path.GetFileNameWithoutExtension(rutaValida);
            string extension = Path.GetExtension(rutaValida);

            string destino = Path.Combine(AudiosFolder, nombreOriginal);

            // Si no existe aún en GAB_AUDIOS, copiarlo directamente con su nombre original
            if (!File.Exists(destino))
            {
                File.Copy(rutaValida, destino);
                return Path.GetFullPath(destino);
            }

            // Si ya existe un archivo con ese nombre, comparar el hash de contenido
            string hashOrigen = CalcularHashArchivo(rutaValida);
            string hashDestino = CalcularHashArchivo(destino);

            if (hashOrigen == hashDestino)
            {
                // Es idéntico en contenido, reutilizar el existente
                return Path.GetFullPath(destino);
            }

            // Si tienen el mismo nombre pero contenido diferente, generar nombre único legible (ej. audio_1.mp3)
            int contador = 1;
            string nuevoDestino;
            do
            {
                string nuevoNombre = $"{nombreSinExt}_{contador}{extension}";
                nuevoDestino = Path.Combine(AudiosFolder, nuevoNombre);
                if (File.Exists(nuevoDestino) && CalcularHashArchivo(nuevoDestino) == hashOrigen)
                {
                    return Path.GetFullPath(nuevoDestino);
                }
                contador++;
            } while (File.Exists(nuevoDestino));

            File.Copy(rutaValida, nuevoDestino);
            return Path.GetFullPath(nuevoDestino);
        }

        private static string CalcularHashArchivo(string ruta)
        {
            using (var md5 = MD5.Create())
            using (var stream = File.OpenRead(ruta))
            {
                var hashBytes = md5.ComputeHash(stream);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
        }

        private static string GetDailyLogPath()
        {
            return Path.Combine(LogsFolder, $"gab-{DateTime.Now:yyyy-MM-dd}.log");
        }

        public static void LogEvent(string nombreAnuncio, string ruta)
        {
            try
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Anuncio: {nombreAnuncio} | Ruta: {ruta}{Environment.NewLine}";
                lock (_logLock)
                {
                    File.AppendAllText(GetDailyLogPath(), line);
                }
            }
            catch
            {
                // Ignorar errores de logging
            }
        }

        public static void LogError(string mensaje, Exception? ex)
        {
            try
            {
                var detalle = ex != null ? $" | Detalle: {ex.Message}" : string.Empty;
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR: {mensaje}{detalle}{Environment.NewLine}";
                lock (_logLock)
                {
                    File.AppendAllText(GetDailyLogPath(), line);
                }
            }
            catch
            {
                // Ignorar errores de logging
            }
        }

        public static void LimpiarLogsAntiguos(int diasRetencion = 30)
        {
            try
            {
                if (!Directory.Exists(LogsFolder))
                    return;

                var limite = DateTime.Now.Date.AddDays(-diasRetencion);
                var archivos = Directory.GetFiles(LogsFolder, "gab-*.log");

                foreach (var archivo in archivos)
                {
                    try
                    {
                        var nombre = Path.GetFileNameWithoutExtension(archivo); // gab-yyyy-MM-dd
                        var partes = nombre.Split('-');
                        if (partes.Length == 4 && 
                            DateTime.TryParse($"{partes[1]}-{partes[2]}-{partes[3]}", out DateTime fechaLog))
                        {
                            if (fechaLog < limite)
                            {
                                File.Delete(archivo);
                            }
                        }
                        else
                        {
                            // Si el formato del nombre no es estándar, revisar LastWriteTime
                            if (File.GetLastWriteTime(archivo) < limite)
                            {
                                File.Delete(archivo);
                            }
                        }
                    }
                    catch
                    {
                        // Si un archivo está bloqueado o en uso, continuar con los demás
                    }
                }
            }
            catch
            {
                // Ignorar fallos de limpieza
            }
        }
    }
}
