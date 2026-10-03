using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace GAB.Models
{
    public enum ModoProgramacion
    {
        HorasFijas,
        Intervalo
    }

    public class AnuncioModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public Guid Id { get; set; } = Guid.NewGuid();
        public string Nombre { get; set; } = string.Empty;
        
        // Path to the file in the GAB_AUDIOS folder
        public string RutaAudio { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonIgnore]
        public string NombreArchivo => !string.IsNullOrWhiteSpace(RutaAudio)
            ? System.IO.Path.GetFileName(RutaAudio)
            : string.Empty;
        
        // State: Un anuncio está activo si tiene al menos un día asignado; sin días queda desactivado.
        [System.Text.Json.Serialization.JsonIgnore]
        public bool Activo => DiasSemana != null && DiasSemana.Count > 0;

        [System.Text.Json.Serialization.JsonIgnore]
        private int _numeroFila = 1;

        [System.Text.Json.Serialization.JsonIgnore]
        public int NumeroFila
        {
            get => _numeroFila;
            set
            {
                if (_numeroFila != value)
                {
                    _numeroFila = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isPlaying;
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (_isPlaying != value)
                {
                    _isPlaying = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PlayButtonText));
                }
            }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public string PlayButtonText => IsPlaying ? "⏹ Stop" : "▶ Play";

        // Scheduled Days (Monday=0 to Sunday=6, or using DayOfWeek enum)
        public List<DayOfWeek> DiasSemana { get; set; } = new List<DayOfWeek>();

        private static readonly Dictionary<DayOfWeek, (int Orden, string Nombre)> DiasMap = new()
        {
            { DayOfWeek.Monday, (1, "Lun") },
            { DayOfWeek.Tuesday, (2, "Mar") },
            { DayOfWeek.Wednesday, (3, "Mié") },
            { DayOfWeek.Thursday, (4, "Jue") },
            { DayOfWeek.Friday, (5, "Vie") },
            { DayOfWeek.Saturday, (6, "Sáb") },
            { DayOfWeek.Sunday, (7, "Dom") },
        };

        [System.Text.Json.Serialization.JsonIgnore]
        public string DiasSemanaString => DiasSemana != null && DiasSemana.Count > 0
            ? string.Join(", ", DiasSemana
                .OrderBy(d => DiasMap.TryGetValue(d, out var info) ? info.Orden : 99)
                .Select(d => DiasMap.TryGetValue(d, out var info) ? info.Nombre : d.ToString()))
            : "Desactivado";

        public ModoProgramacion Modo { get; set; } = ModoProgramacion.HorasFijas;

        // Modo A: Horas fijas (e.g. 10:00, 11:30)
        public List<TimeSpan> HorasFijas { get; set; } = new List<TimeSpan>();

        // Modo B: Intervalo
        public TimeSpan HoraInicio { get; set; } = TimeSpan.Zero;
        public TimeSpan HoraFin { get; set; } = TimeSpan.Zero;
        public int IntervaloMinutos { get; set; } = 30;

        [System.Text.Json.Serialization.JsonIgnore]
        public string HorarioString
        {
            get
            {
                if (Modo == ModoProgramacion.HorasFijas)
                {
                    if (HorasFijas == null || HorasFijas.Count == 0) return "-";
                    return string.Join(", ", HorasFijas.OrderBy(h => h).Select(h => h.ToString(@"hh\:mm")));
                }
                else
                {
                    return $"Cada {IntervaloMinutos} min, de {HoraInicio:hh\\:mm} a {HoraFin:hh\\:mm}";
                }
            }
        }

        public void NotifyPropertiesChanged()
        {
            OnPropertyChanged(nameof(DiasSemanaString));
            OnPropertyChanged(nameof(HorarioString));
            OnPropertyChanged(nameof(NombreArchivo));
            OnPropertyChanged(nameof(Activo));
        }

        public static string ObtenerNombreDia(DayOfWeek dia)
        {
            return dia switch
            {
                DayOfWeek.Monday => "Lunes",
                DayOfWeek.Tuesday => "Martes",
                DayOfWeek.Wednesday => "Miércoles",
                DayOfWeek.Thursday => "Jueves",
                DayOfWeek.Friday => "Viernes",
                DayOfWeek.Saturday => "Sábado",
                DayOfWeek.Sunday => "Domingo",
                _ => dia.ToString()
            };
        }

        public HashSet<TimeSpan> ObtenerHorariosDisparo()
        {
            var horarios = new HashSet<TimeSpan>();
            if (Modo == ModoProgramacion.HorasFijas)
            {
                foreach (var h in HorasFijas)
                {
                    horarios.Add(new TimeSpan(h.Hours, h.Minutes, 0));
                }
            }
            else if (Modo == ModoProgramacion.Intervalo && IntervaloMinutos > 0)
            {
                int startMin = HoraInicio.Hours * 60 + HoraInicio.Minutes;
                int endMin = HoraFin.Hours * 60 + HoraFin.Minutes;

                if (startMin <= endMin)
                {
                    for (int m = startMin; m <= endMin; m += IntervaloMinutos)
                    {
                        horarios.Add(new TimeSpan(m / 60, m % 60, 0));
                    }
                }
                else
                {
                    // Intervalo nocturno que cruza la medianoche (ej. 22:00 a 06:00)
                    int totalMinutos = (1440 - startMin) + endMin;
                    for (int step = 0; step <= totalMinutos; step += IntervaloMinutos)
                    {
                        int m = (startMin + step) % 1440;
                        horarios.Add(new TimeSpan(m / 60, m % 60, 0));
                    }
                }
            }
            return horarios;
        }

        public bool VerificarConflicto(AnuncioModel otro, out DayOfWeek diaConflicto, out TimeSpan horaConflicto, out int totalCoincidencias)
        {
            diaConflicto = default;
            horaConflicto = default;
            totalCoincidencias = 0;

            if (otro == null || otro.Id == this.Id)
                return false;

            var diasComunes = this.DiasSemana.Intersect(otro.DiasSemana).ToList();
            if (diasComunes.Count == 0)
                return false;

            var misHorarios = this.ObtenerHorariosDisparo();
            if (misHorarios.Count == 0)
                return false;

            var otrosHorarios = otro.ObtenerHorariosDisparo();
            if (otrosHorarios.Count == 0)
                return false;

            var horariosComunes = misHorarios.Intersect(otrosHorarios).OrderBy(t => t).ToList();
            if (horariosComunes.Count == 0)
                return false;

            var primerDia = diasComunes
                .OrderBy(d => DiasMap.TryGetValue(d, out var info) ? info.Orden : 99)
                .First();

            diaConflicto = primerDia;
            horaConflicto = horariosComunes.First();
            totalCoincidencias = horariosComunes.Count;
            return true;
        }
    }
}
