using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace GAB.Services
{
    public static class AudioManager
    {
        private static readonly SemaphoreSlim _playbackLock = new SemaphoreSlim(1, 1);
        private static CancellationTokenSource? _currentPlaybackCts;
        private static WasapiOut? _currentOutputDevice;
        private static readonly object _stopLock = new object();

        public static bool IsPlaying => _playbackLock.CurrentCount == 0;

        /// <summary>
        /// Volumen objetivo para la atenuación de otras aplicaciones (0.0f = silencio, hasta 0.5f).
        /// </summary>
        public static float DuckingTargetVolume { get; set; } = 0.0f;

        public static event Action? PlaybackStarted;
        public static event Action? PlaybackStopped;

        public static void StopPlayback()
        {
            lock (_stopLock)
            {
                try
                {
                    _currentPlaybackCts?.Cancel();
                    if (_currentOutputDevice != null && _currentOutputDevice.PlaybackState == PlaybackState.Playing)
                    {
                        _currentOutputDevice.Stop();
                    }
                }
                catch (Exception ex)
                {
                    StorageManager.LogError("Error al detener la reproducción", ex);
                }
            }
        }

        public static async Task PlayAudioAsync(string filePath)
        {
            var rutaValida = StorageManager.ObtenerRutaValidaAudio(filePath);
            if (string.IsNullOrEmpty(rutaValida) || !File.Exists(rutaValida))
            {
                StorageManager.LogError($"No se encontró el archivo de audio para reproducir: '{filePath}'", new FileNotFoundException("Archivo no encontrado", filePath));
                return;
            }

            // Encolar la reproducción para evitar solapamientos y pérdida de anuncios simultáneos
            await _playbackLock.WaitAsync();

            CancellationTokenSource cts;
            lock (_stopLock)
            {
                _currentPlaybackCts = new CancellationTokenSource();
                cts = _currentPlaybackCts;
            }

            MMDeviceEnumerator? enumerator = null;
            MMDevice? defaultDevice = null;
            var duckedSessions = new List<(SimpleAudioVolume Session, float OriginalVolume)>();

            try
            {
                PlaybackStarted?.Invoke();

                // Ducking suave antes de la reproducción
                try
                {
                    enumerator = new MMDeviceEnumerator();
                    defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    if (defaultDevice != null)
                    {
                        uint currentProcessId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                        var sessions = defaultDevice.AudioSessionManager.Sessions;
                        var duckTasks = new List<Task>();

                        for (int i = 0; i < sessions.Count; i++)
                        {
                            var s = sessions[i];
                            var pid = s.GetProcessID;
                            if (pid != currentProcessId && pid != 0)
                            {
                                // Solo atenuar sesiones que no estén ya muteadas por el usuario
                                if (!s.SimpleAudioVolume.Mute && s.SimpleAudioVolume.Volume > 0.0f)
                                {
                                    float origVol = s.SimpleAudioVolume.Volume;
                                    float targetVol = Math.Min(origVol, Math.Max(0.0f, DuckingTargetVolume));
                                    duckedSessions.Add((s.SimpleAudioVolume, origVol));
                                    duckTasks.Add(FadeVolumeAsync(s.SimpleAudioVolume, targetVol, 200));
                                }
                            }
                        }

                        if (duckTasks.Count > 0)
                        {
                            await Task.WhenAll(duckTasks);
                        }
                    }
                }
                catch (Exception exDuck)
                {
                    StorageManager.LogError("Error aplicando ducking de audio", exDuck);
                }

                // Reproducir el archivo de audio con NAudio
                using (var audioFile = new AudioFileReader(rutaValida))
                using (var outputDevice = new WasapiOut(AudioClientShareMode.Shared, 200))
                {
                    lock (_stopLock)
                    {
                        if (cts.IsCancellationRequested)
                            return;

                        _currentOutputDevice = outputDevice;
                    }

                    var tcs = new TaskCompletionSource<bool>();
                    outputDevice.PlaybackStopped += (s, e) =>
                    {
                        if (e.Exception != null)
                            tcs.TrySetException(e.Exception);
                        else
                            tcs.TrySetResult(true);
                    };

                    outputDevice.Init(audioFile);
                    outputDevice.Play();

                    // Esperar a que el evento PlaybackStopped finalice o que el estado ya no sea Playing o se cancele
                    var waitTask = tcs.Task;
                    while (outputDevice.PlaybackState == PlaybackState.Playing && !waitTask.IsCompleted && !cts.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(100, cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }

                    if (outputDevice.PlaybackState == PlaybackState.Playing)
                    {
                        outputDevice.Stop();
                        try
                        {
                            await Task.WhenAny(tcs.Task, Task.Delay(200));
                        }
                        catch { }
                    }

                    lock (_stopLock)
                    {
                        _currentOutputDevice = null;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelación intencional de reproducción
            }
            catch (Exception ex)
            {
                StorageManager.LogError($"Error al reproducir audio: {rutaValida}", ex);
            }
            finally
            {
                // Restauración suave (Fade-in) SIEMPRE dentro del bloque finally
                try
                {
                    if (duckedSessions.Count > 0)
                    {
                        var restoreTasks = new List<Task>();
                        foreach (var (session, originalVol) in duckedSessions)
                        {
                            restoreTasks.Add(FadeVolumeAsync(session, originalVol, 200));
                        }
                        await Task.WhenAll(restoreTasks);
                    }
                }
                catch (Exception exRestore)
                {
                    StorageManager.LogError("Error restaurando volumen tras ducking", exRestore);
                }
                finally
                {
                    defaultDevice?.Dispose();
                    enumerator?.Dispose();
                    lock (_stopLock)
                    {
                        _currentOutputDevice = null;
                        _currentPlaybackCts?.Dispose();
                        _currentPlaybackCts = null;
                    }
                    _playbackLock.Release();
                    PlaybackStopped?.Invoke();
                }
            }
        }

        private static async Task FadeVolumeAsync(SimpleAudioVolume session, float targetVolume, int durationMs = 200)
        {
            try
            {
                float initialVolume = session.Volume;
                if (Math.Abs(initialVolume - targetVolume) < 0.01f)
                {
                    session.Volume = targetVolume;
                    return;
                }

                int stepDelayMs = 15;
                int steps = Math.Max(1, durationMs / stepDelayMs);
                float stepChange = (targetVolume - initialVolume) / steps;

                for (int i = 0; i < steps; i++)
                {
                    float nextVol = session.Volume + stepChange;
                    session.Volume = Math.Clamp(nextVol, 0.0f, 1.0f);
                    await Task.Delay(stepDelayMs);
                }

                session.Volume = targetVolume;
            }
            catch
            {
                // Manejo seguro ante sesiones cerradas durante el fade
            }
        }
    }
}
