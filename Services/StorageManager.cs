using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using GAB.Models;

namespace GAB.Services
{
    public static class StorageManager
    {
        private static readonly string AppDataFolder = AppDomain.CurrentDomain.BaseDirectory;
        private static readonly string AudiosFolder = Path.Combine(AppDataFolder, "GAB_AUDIOS");
        public static string AudiosDirectory => AudiosFolder;
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

        public static string ConvertirARutaRelativa(string? ruta)
        {
            if (string.IsNullOrWhiteSpace(ruta))
                return string.Empty;

            try
            {
                var nombreArchivo = Path.GetFileName(ruta);

                // Si es solo el nombre de archivo o ya indica GAB_AUDIOS
                if (string.IsNullOrEmpty(Path.GetDirectoryName(ruta)) ||
                    ruta.StartsWith("GAB_AUDIOS", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine("GAB_AUDIOS", nombreArchivo);
                }

                // Si está dentro de GAB_AUDIOS
                var fullAudios = Path.GetFullPath(AudiosFolder);
                var fullRuta = Path.GetFullPath(ruta);
                if (fullRuta.StartsWith(fullAudios, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine("GAB_AUDIOS", nombreArchivo);
                }

                // Si está dentro de la carpeta base de la app
                var fullApp = Path.GetFullPath(AppDataFolder);
                if (fullRuta.StartsWith(fullApp, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetRelativePath(fullApp, fullRuta);
                }

                // Para cualquier otra ruta (incluyendo rutas absolutas externas de Python)
                return Path.Combine("GAB_AUDIOS", nombreArchivo);
            }
            catch
            {
                return Path.Combine("GAB_AUDIOS", Path.GetFileName(ruta));
            }
        }

        public static string ObtenerRutaValidaAudio(string? ruta, string? directorioContexto = null)
        {
            if (string.IsNullOrWhiteSpace(ruta))
                return string.Empty;

            var nombreArchivo = Path.GetFileName(ruta);

            // 1. En GAB_AUDIOS local por nombre de archivo
            var rutaAudios = Path.Combine(AudiosFolder, nombreArchivo);
            if (File.Exists(rutaAudios))
                return Path.GetFullPath(rutaAudios);

            // 2. Si se especifica un directorio de contexto (ej. donde estaba el json importado)
            if (!string.IsNullOrWhiteSpace(directorioContexto) && Directory.Exists(directorioContexto))
            {
                var cand1 = Path.Combine(directorioContexto, "GAB_AUDIOS", nombreArchivo);
                if (File.Exists(cand1)) return Path.GetFullPath(cand1);

                var cand2 = Path.Combine(directorioContexto, "audio", nombreArchivo);
                if (File.Exists(cand2)) return Path.GetFullPath(cand2);

                var cand3 = Path.Combine(directorioContexto, "_internal", "audio", nombreArchivo);
                if (File.Exists(cand3)) return Path.GetFullPath(cand3);

                var cand4 = Path.Combine(directorioContexto, nombreArchivo);
                if (File.Exists(cand4)) return Path.GetFullPath(cand4);
            }

            // 3. En carpetas vecinas al ejecutable (compatibilidad con estructura Python y carpetas relativas)
            var candVecino1 = Path.Combine(AppDataFolder, "audio", nombreArchivo);
            if (File.Exists(candVecino1))
                return Path.GetFullPath(candVecino1);

            var candVecino2 = Path.Combine(AppDataFolder, "_internal", "audio", nombreArchivo);
            if (File.Exists(candVecino2))
                return Path.GetFullPath(candVecino2);

            var candVecino3 = Path.Combine(AppDataFolder, nombreArchivo);
            if (File.Exists(candVecino3))
                return Path.GetFullPath(candVecino3);

            // 4. Si es relativa directa a AppDataFolder
            var rutaRelativa = Path.Combine(AppDataFolder, ruta);
            if (File.Exists(rutaRelativa))
                return Path.GetFullPath(rutaRelativa);

            // 5. Si la ruta absoluta original aún existe físicamente en el disco
            if (File.Exists(ruta))
                return Path.GetFullPath(ruta);

            return string.Empty;
        }

        public static List<AnuncioModel> DeserializarAnuncios(string json, string? directorioContexto, out bool esLegacy)
        {
            esLegacy = false;
            if (string.IsNullOrWhiteSpace(json))
                return new List<AnuncioModel>();

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return new List<AnuncioModel>();

                var array = doc.RootElement.EnumerateArray().ToList();
                if (array.Count == 0)
                    return new List<AnuncioModel>();

                // Detección de formato clásico Python:
                // En Python se usan propiedades como "ruta", "dias" (strings), "horas", "intervalo_min", "rango_desde"
                bool contieneCamposPython = array.Any(el =>
                    el.TryGetProperty("ruta", out _) ||
                    el.TryGetProperty("intervalo_min", out _) ||
                    el.TryGetProperty("rango_desde", out _) ||
                    el.TryGetProperty("rango_hasta", out _));

                if (contieneCamposPython)
                {
                    esLegacy = true;
                    return MigrarDesdeLegacyPython(array, directorioContexto);
                }
                else
                {
                    // Formato estándar C#
                    var lista = JsonSerializer.Deserialize<List<AnuncioModel>>(json, _jsonLoadOptions) ?? new List<AnuncioModel>();
                    return lista;
                }
            }
            catch (Exception ex)
            {
                LogError("Error al deserializar anuncios", ex);
                return new List<AnuncioModel>();
            }
        }

        private static List<AnuncioModel> MigrarDesdeLegacyPython(List<JsonElement> array, string? directorioContexto)
        {
            var resultado = new List<AnuncioModel>();

            foreach (var el in array)
            {
                try
                {
                    string nombre = el.TryGetProperty("nombre", out var pNom) ? (pNom.GetString() ?? "") : "";
                    string rutaOriginal = el.TryGetProperty("ruta", out var pRuta) ? (pRuta.GetString() ?? "") : "";
                    string nombreArchivo = Path.GetFileName(rutaOriginal);

                    // Resolver la mejor ruta posible buscando en carpetas vecinas y de contexto
                    string rutaResuelta = ObtenerRutaValidaAudio(rutaOriginal, directorioContexto);

                    // Si se encontró el archivo fuera de GAB_AUDIOS, copiarlo a GAB_AUDIOS para integrarlo
                    if (!string.IsNullOrEmpty(rutaResuelta) && File.Exists(rutaResuelta))
                    {
                        try
                        {
                            rutaResuelta = ProcesarArchivoAudio(rutaResuelta);
                        }
                        catch
                        {
                            // Si falla copiar, mantener la ruta resuelta
                        }
                    }
                    else
                    {
                        // Si no se encuentra aún, asignar la ruta relativa canónica en GAB_AUDIOS
                        rutaResuelta = Path.Combine(AudiosFolder, nombreArchivo);
                    }

                    // Días de la semana (en inglés o español en minúsculas)
                    var diasList = new List<DayOfWeek>();
                    if (el.TryGetProperty("dias", out var pDias) && pDias.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var diaEl in pDias.EnumerateArray())
                        {
                            string diaStr = diaEl.GetString()?.Trim().ToLowerInvariant() ?? "";
                            DayOfWeek? dia = diaStr switch
                            {
                                "monday" or "lunes" => DayOfWeek.Monday,
                                "tuesday" or "martes" => DayOfWeek.Tuesday,
                                "wednesday" or "miercoles" or "miércoles" => DayOfWeek.Wednesday,
                                "thursday" or "jueves" => DayOfWeek.Thursday,
                                "friday" or "viernes" => DayOfWeek.Friday,
                                "saturday" or "sabado" or "sábado" => DayOfWeek.Saturday,
                                "sunday" or "domingo" => DayOfWeek.Sunday,
                                _ => null
                            };
                            if (dia.HasValue && !diasList.Contains(dia.Value))
                            {
                                diasList.Add(dia.Value);
                            }
                        }
                    }

                    // Horas fijas
                    var horasList = new List<TimeSpan>();
                    if (el.TryGetProperty("horas", out var pHoras) && pHoras.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var hEl in pHoras.EnumerateArray())
                        {
                            string hStr = hEl.GetString()?.Trim() ?? "";
                            if (TimeSpan.TryParse(hStr, out var ts))
                            {
                                if (!horasList.Contains(ts))
                                    horasList.Add(ts);
                            }
                        }
                    }

                    // Intervalo
                    int? intervaloMin = null;
                    if (el.TryGetProperty("intervalo_min", out var pInt) && pInt.ValueKind == JsonValueKind.Number)
                    {
                        intervaloMin = pInt.GetInt32();
                    }

                    string? rangoDesde = el.TryGetProperty("rango_desde", out var pDesde) && pDesde.ValueKind == JsonValueKind.String ? pDesde.GetString() : null;
                    string? rangoHasta = el.TryGetProperty("rango_hasta", out var pHasta) && pHasta.ValueKind == JsonValueKind.String ? pHasta.GetString() : null;

                    ModoProgramacion modo = ModoProgramacion.HorasFijas;
                    TimeSpan horaInicio = TimeSpan.Zero;
                    TimeSpan horaFin = TimeSpan.Zero;
                    int intervalo = 30;

                    if (intervaloMin.HasValue && intervaloMin.Value > 0 && 
                        !string.IsNullOrWhiteSpace(rangoDesde) && !string.IsNullOrWhiteSpace(rangoHasta))
                    {
                        if (TimeSpan.TryParse(rangoDesde, out var iniTs) && TimeSpan.TryParse(rangoHasta, out var finTs))
                        {
                            modo = ModoProgramacion.Intervalo;
                            horaInicio = iniTs;
                            horaFin = finTs;
                            intervalo = intervaloMin.Value;
                        }
                    }

                    var anuncio = new AnuncioModel
                    {
                        Id = Guid.NewGuid(),
                        Nombre = string.IsNullOrWhiteSpace(nombre) ? nombreArchivo : nombre,
                        RutaAudio = rutaResuelta,
                        DiasSemana = diasList,
                        Modo = modo,
                        HorasFijas = horasList,
                        HoraInicio = horaInicio,
                        HoraFin = horaFin,
                        IntervaloMinutos = intervalo
                    };

                    resultado.Add(anuncio);
                }
                catch (Exception ex)
                {
                    LogError("Error al migrar anuncio legacy individual", ex);
                }
            }

            return resultado;
        }

        public static List<AnuncioModel> CargarAnuncios()
        {
            if (!File.Exists(JsonPath))
                return new List<AnuncioModel>();

            try
            {
                var json = File.ReadAllText(JsonPath);
                var lista = DeserializarAnuncios(json, AppDataFolder, out bool fueLegacy);

                // Resolver rutas completas para reproducción en memoria
                foreach (var anuncio in lista)
                {
                    var rutaValida = ObtenerRutaValidaAudio(anuncio.RutaAudio);
                    if (!string.IsNullOrEmpty(rutaValida))
                    {
                        anuncio.RutaAudio = rutaValida;
                    }
                }

                // Si provenía de formato legacy de Python, migrar guardando en formato nuevo con rutas relativas
                if (fueLegacy)
                {
                    GuardarAnuncios(lista);
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
            // Guardar siempre rutas relativas en anuncios.json para garantizar 100% de portabilidad
            var copiaParaGuardar = anuncios.Select(a => new AnuncioModel
            {
                Id = a.Id,
                Nombre = a.Nombre,
                RutaAudio = ConvertirARutaRelativa(a.RutaAudio),
                DiasSemana = a.DiasSemana.ToList(),
                Modo = a.Modo,
                HorasFijas = a.HorasFijas.ToList(),
                HoraInicio = a.HoraInicio,
                HoraFin = a.HoraFin,
                IntervaloMinutos = a.IntervaloMinutos
            }).ToList();

            var json = JsonSerializer.Serialize(copiaParaGuardar, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(JsonPath, json);
        }

        public static (int importados, int encontrados, int faltantes, List<AnuncioModel> listaFinal) ImportarAnunciosDesdeArchivo(
            string rutaArchivo, bool reemplazar, List<AnuncioModel> actuales)
        {
            if (!File.Exists(rutaArchivo))
                throw new FileNotFoundException("El archivo de anuncios no existe.", rutaArchivo);

            var json = File.ReadAllText(rutaArchivo);
            var directorioContexto = Path.GetDirectoryName(rutaArchivo);
            var nuevos = DeserializarAnuncios(json, directorioContexto, out _);

            if (nuevos.Count == 0)
                return (0, 0, 0, actuales);

            int encontrados = 0;
            int faltantes = 0;

            foreach (var anuncio in nuevos)
            {
                var rutaValida = ObtenerRutaValidaAudio(anuncio.RutaAudio, directorioContexto);
                if (!string.IsNullOrEmpty(rutaValida) && File.Exists(rutaValida))
                {
                    try
                    {
                        anuncio.RutaAudio = ProcesarArchivoAudio(rutaValida);
                        encontrados++;
                    }
                    catch
                    {
                        anuncio.RutaAudio = rutaValida;
                        encontrados++;
                    }
                }
                else
                {
                    anuncio.RutaAudio = Path.Combine(AudiosFolder, Path.GetFileName(anuncio.RutaAudio));
                    faltantes++;
                }
            }

            List<AnuncioModel> resultado;
            if (reemplazar)
            {
                resultado = nuevos;
            }
            else
            {
                resultado = new List<AnuncioModel>(actuales);
                foreach (var nuevo in nuevos)
                {
                    // Comprobar si ya existe uno con el mismo nombre y configuración
                    if (!resultado.Any(a => a.Nombre.Equals(nuevo.Nombre, StringComparison.OrdinalIgnoreCase)))
                    {
                        resultado.Add(nuevo);
                    }
                    else
                    {
                        nuevo.Nombre = $"{nuevo.Nombre} (importado)";
                        resultado.Add(nuevo);
                    }
                }
            }

            GuardarAnuncios(resultado);
            return (nuevos.Count, encontrados, faltantes, resultado);
        }

        public static void ExportarAnunciosAArchivo(string destinoRuta, List<AnuncioModel> anuncios)
        {
            var copiaParaGuardar = anuncios.Select(a => new AnuncioModel
            {
                Id = a.Id,
                Nombre = a.Nombre,
                RutaAudio = ConvertirARutaRelativa(a.RutaAudio),
                DiasSemana = a.DiasSemana.ToList(),
                Modo = a.Modo,
                HorasFijas = a.HorasFijas.ToList(),
                HoraInicio = a.HoraInicio,
                HoraFin = a.HoraFin,
                IntervaloMinutos = a.IntervaloMinutos
            }).ToList();

            var json = JsonSerializer.Serialize(copiaParaGuardar, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(destinoRuta, json);
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
            if (string.IsNullOrWhiteSpace(rutaOriginal))
                throw new FileNotFoundException("La ruta de audio no puede estar vacía.");

            string rutaOrigen;
            if (File.Exists(rutaOriginal))
            {
                rutaOrigen = rutaOriginal;
            }
            else
            {
                var rutaValida = ObtenerRutaValidaAudio(rutaOriginal);
                if (!string.IsNullOrEmpty(rutaValida) && File.Exists(rutaValida))
                {
                    rutaOrigen = rutaValida;
                }
                else
                {
                    throw new FileNotFoundException("El archivo de audio no existe.", rutaOriginal);
                }
            }

            // Si el archivo ya se encuentra dentro de GAB_AUDIOS, retornar su ruta directamente
            var fullRutaValida = Path.GetFullPath(rutaOrigen);
            var fullAudiosFolder = Path.GetFullPath(AudiosFolder);
            if (fullRutaValida.StartsWith(fullAudiosFolder, StringComparison.OrdinalIgnoreCase))
            {
                return fullRutaValida;
            }

            string nombreOriginal = Path.GetFileName(rutaOrigen);
            string nombreSinExt = Path.GetFileNameWithoutExtension(rutaOrigen);
            string extension = Path.GetExtension(rutaOrigen);

            string destino = Path.Combine(AudiosFolder, nombreOriginal);

            // Si no existe aún en GAB_AUDIOS, copiarlo directamente con su nombre original
            if (!File.Exists(destino))
            {
                File.Copy(rutaOrigen, destino);
                return Path.GetFullPath(destino);
            }

            // Si ya existe un archivo con ese nombre, comparar el hash de contenido
            string hashOrigen = CalcularHashArchivo(rutaOrigen);
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

            File.Copy(rutaOrigen, nuevoDestino);
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
