using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GAB.Models;

namespace GAB.Services
{
    public class SchedulerService
    {
        private PeriodicTimer? _timer;
        private CancellationTokenSource? _cts;
        private List<AnuncioModel> _anuncios = new List<AnuncioModel>();
        private DateTime _lastEvaluatedMinute = DateTime.MinValue;

        // Anti-overlap: Evitar que el mismo anuncio se reproduzca más de una vez en el mismo minuto.
        private Dictionary<Guid, DateTime> _lastPlayedTracker = new Dictionary<Guid, DateTime>();

        public event Action<string>? OnAnuncioPlaying;
        public event Action<string, string>? OnAnuncioError;

        public void Start(List<AnuncioModel> anuncios)
        {
            _anuncios = anuncios;
            _cts = new CancellationTokenSource();
            _timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            
            _ = RunLoopAsync();
        }

        public void Stop()
        {
            _cts?.Cancel();
            _timer?.Dispose();
        }

        public void UpdateAnuncios(List<AnuncioModel> anuncios)
        {
            _anuncios = anuncios;
        }

        private async Task RunLoopAsync()
        {
            try
            {
                while (await _timer!.WaitForNextTickAsync(_cts!.Token))
                {
                    EvaluateSchedule();
                }
            }
            catch (OperationCanceledException)
            {
                // Task was canceled, exit gracefully
            }
        }

        private void EvaluateSchedule()
        {
            try
            {
                var now = DateTime.Now;
                var currentMinute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
                
                // Evaluar sólo una vez por minuto exacto
                if (currentMinute == _lastEvaluatedMinute)
                    return;

                _lastEvaluatedMinute = currentMinute;
                var currentDay = now.DayOfWeek;
                var currentTime = new TimeSpan(now.Hour, now.Minute, 0);

                foreach (var anuncio in _anuncios.Where(a => a.Activo))
                {
                    if (!anuncio.DiasSemana.Contains(currentDay))
                        continue;

                    bool shouldPlay = false;

                    if (anuncio.Modo == ModoProgramacion.HorasFijas)
                    {
                        shouldPlay = anuncio.HorasFijas.Any(h => h.Hours == currentTime.Hours && h.Minutes == currentTime.Minutes);
                    }
                    else if (anuncio.Modo == ModoProgramacion.Intervalo && anuncio.IntervaloMinutos > 0)
                    {
                        int currentTotalMin = now.Hour * 60 + now.Minute;
                        int startTotalMin = anuncio.HoraInicio.Hours * 60 + anuncio.HoraInicio.Minutes;
                        int endTotalMin = anuncio.HoraFin.Hours * 60 + anuncio.HoraFin.Minutes;

                        bool isInRange;
                        int diff;

                        if (startTotalMin <= endTotalMin)
                        {
                            // Intervalo regular dentro del mismo día
                            isInRange = currentTotalMin >= startTotalMin && currentTotalMin <= endTotalMin;
                            diff = currentTotalMin - startTotalMin;
                        }
                        else
                        {
                            // Intervalo nocturno que cruza la medianoche (ej. 22:00 a 06:00)
                            isInRange = currentTotalMin >= startTotalMin || currentTotalMin <= endTotalMin;
                            diff = currentTotalMin >= startTotalMin 
                                ? currentTotalMin - startTotalMin 
                                : (1440 - startTotalMin) + currentTotalMin;
                        }

                        if (isInRange && (diff % anuncio.IntervaloMinutos == 0))
                        {
                            shouldPlay = true;
                        }
                    }

                    if (shouldPlay)
                    {
                        // Evitar que el anuncio se dispare varias veces en el mismo minuto
                        if (_lastPlayedTracker.TryGetValue(anuncio.Id, out var lastPlayed))
                        {
                            if (lastPlayed.Year == now.Year && lastPlayed.Month == now.Month && 
                                lastPlayed.Day == now.Day && lastPlayed.Hour == now.Hour && lastPlayed.Minute == now.Minute)
                            {
                                continue; // Ya sonó en este minuto
                            }
                        }

                        var rutaAudio = StorageManager.ObtenerRutaValidaAudio(anuncio.RutaAudio);
                        if (!string.IsNullOrEmpty(rutaAudio))
                        {
                            _lastPlayedTracker[anuncio.Id] = now;
                            StorageManager.LogEvent(anuncio.Nombre, rutaAudio);
                            OnAnuncioPlaying?.Invoke(anuncio.Nombre);

                            // AudioManager encola de forma segura la reproducción
                            _ = AudioManager.PlayAudioAsync(rutaAudio);
                        }
                        else
                        {
                            StorageManager.LogError($"Anuncio '{anuncio.Nombre}' no pudo reproducirse: archivo no encontrado ({anuncio.RutaAudio})", new System.IO.FileNotFoundException());
                            OnAnuncioError?.Invoke(anuncio.Nombre, $"No se pudo reproducir {anuncio.Nombre}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StorageManager.LogError("Error inesperado durante la evaluación del planificador", ex);
            }
        }
    }
}
