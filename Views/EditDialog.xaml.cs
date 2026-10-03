using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using GAB.Models;
using GAB.Services;

namespace GAB
{
    public partial class EditDialog : Window
    {
        public AnuncioModel Anuncio { get; private set; }
        private string _rutaAudioOriginal = string.Empty;
        private List<AnuncioModel> _otrosAnuncios = new();
        private string _rutaArchivoSeleccionado = string.Empty;

        public EditDialog(AnuncioModel anuncio, List<AnuncioModel>? listaExistente = null)
        {
            InitializeComponent();
            ThemeHelper.ApplyTitleBarTheme(this);
            _otrosAnuncios = listaExistente?.Where(a => a.Id != anuncio.Id).ToList() ?? new List<AnuncioModel>();
            Anuncio = new AnuncioModel
            {
                Id = anuncio.Id,
                Nombre = anuncio.Nombre,
                RutaAudio = anuncio.RutaAudio,
                DiasSemana = anuncio.DiasSemana.ToList(),
                Modo = anuncio.Modo,
                HorasFijas = anuncio.HorasFijas.ToList(),
                HoraInicio = anuncio.HoraInicio,
                HoraFin = anuncio.HoraFin,
                IntervaloMinutos = anuncio.IntervaloMinutos
            };
            
            // Si es un anuncio nuevo sin días configurados, marcar lunes a viernes por defecto
            if (Anuncio.DiasSemana.Count == 0 && string.IsNullOrEmpty(anuncio.Nombre))
            {
                Anuncio.DiasSemana = new List<DayOfWeek>
                {
                    DayOfWeek.Monday,
                    DayOfWeek.Tuesday,
                    DayOfWeek.Wednesday,
                    DayOfWeek.Thursday,
                    DayOfWeek.Friday
                };
            }

            _rutaAudioOriginal = Anuncio.RutaAudio;
            LoadData();
        }

        private void LoadData()
        {
            TxtNombre.Text = Anuncio.Nombre;
            _rutaArchivoSeleccionado = Anuncio.RutaAudio;
            TxtRutaAudio.Text = Anuncio.NombreArchivo;
            TxtRutaAudio.ToolTip = Anuncio.RutaAudio;

            foreach (var child in PanelDias.Children)
            {
                if (child is CheckBox chk && int.TryParse(chk.Tag?.ToString(), out int dayInt))
                {
                    chk.IsChecked = Anuncio.DiasSemana.Contains((DayOfWeek)dayInt);
                }
            }

            if (Anuncio.HorasFijas != null && Anuncio.HorasFijas.Count > 0)
            {
                TxtHorasFijas.Text = string.Join(", ", Anuncio.HorasFijas.OrderBy(h => h).Select(h => h.ToString(@"hh\:mm")));
            }
            else
            {
                TxtHorasFijas.Text = "12:00";
            }

            bool esIntervalo = Anuncio.Modo == ModoProgramacion.Intervalo;
            ChkRepetirIntervalo.IsChecked = esIntervalo;
            PanelIntervaloConfig.IsEnabled = esIntervalo;
            TxtHoraInicio.Text = Anuncio.HoraInicio != TimeSpan.Zero ? Anuncio.HoraInicio.ToString(@"hh\:mm") : "08:00";
            TxtHoraFin.Text = Anuncio.HoraFin != TimeSpan.Zero ? Anuncio.HoraFin.ToString(@"hh\:mm") : "18:00";
            TxtIntervalo.Text = Anuncio.IntervaloMinutos > 0 ? Anuncio.IntervaloMinutos.ToString() : "30";
        }

        private void BtnTodosLosDias_Click(object sender, RoutedEventArgs e)
        {
            var checkboxes = PanelDias.Children.OfType<CheckBox>().ToList();
            bool allChecked = checkboxes.All(c => c.IsChecked == true);
            foreach (var chk in checkboxes)
            {
                chk.IsChecked = !allChecked;
            }
        }

        private void ChkRepetirIntervalo_Changed(object sender, RoutedEventArgs e)
        {
            if (PanelIntervaloConfig != null)
            {
                PanelIntervaloConfig.IsEnabled = ChkRepetirIntervalo.IsChecked == true;
            }
        }

        private void BtnSeleccionarAudio_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Archivos de Audio (*.mp3;*.wav)|*.mp3;*.wav",
                Title = "Seleccionar archivo de audio"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                _rutaArchivoSeleccionado = openFileDialog.FileName;
                TxtRutaAudio.Text = openFileDialog.SafeFileName;
                TxtRutaAudio.ToolTip = openFileDialog.FileName;
            }
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtNombre.Text))
            {
                MessageBox.Show("Debe especificar un nombre para el anuncio.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtNombre.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(_rutaArchivoSeleccionado))
            {
                MessageBox.Show("Debe seleccionar un archivo de audio.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Procesar copia y hash de archivo si cambió la ruta (y si es una ruta local externa válida)
            if (_rutaArchivoSeleccionado != _rutaAudioOriginal)
            {
                var rutaValida = StorageManager.ObtenerRutaValidaAudio(_rutaArchivoSeleccionado);
                if (string.IsNullOrEmpty(rutaValida) || !System.IO.File.Exists(rutaValida))
                {
                    MessageBox.Show("El archivo de audio seleccionado no existe o no es accesible.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                try
                {
                    Anuncio.RutaAudio = StorageManager.ProcesarArchivoAudio(rutaValida);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al procesar el archivo de audio: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            // Validar días seleccionados
            var diasSeleccionados = new List<DayOfWeek>();
            foreach (var child in PanelDias.Children)
            {
                if (child is CheckBox chk && chk.IsChecked == true && int.TryParse(chk.Tag?.ToString(), out int dayInt))
                {
                    diasSeleccionados.Add((DayOfWeek)dayInt);
                }
            }

            // Días seleccionados: si no hay días, el anuncio queda desactivado
            Anuncio.DiasSemana = diasSeleccionados;
            Anuncio.Nombre = TxtNombre.Text.Trim();

            if (ChkRepetirIntervalo.IsChecked == true)
            {
                if (!TimeSpan.TryParse(TxtHoraInicio.Text, out TimeSpan tsInicio) ||
                    !TimeSpan.TryParse(TxtHoraFin.Text, out TimeSpan tsFin) ||
                    !int.TryParse(TxtIntervalo.Text, out int min) || min <= 0)
                {
                    MessageBox.Show("Valores de intervalo inválidos. Verifique que las horas tengan formato HH:mm y el intervalo sea mayor a 0 minutos.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Anuncio.Modo = ModoProgramacion.Intervalo;
                Anuncio.HoraInicio = tsInicio;
                Anuncio.HoraFin = tsFin;
                Anuncio.IntervaloMinutos = min;
                Anuncio.HorasFijas.Clear();
            }
            else
            {
                // Parsear horas fijas separadas por coma, punto y coma o espacio
                var partes = TxtHorasFijas.Text
                    .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);

                var listaHoras = new List<TimeSpan>();
                foreach (var p in partes)
                {
                    if (TimeSpan.TryParse(p.Trim(), out TimeSpan t))
                    {
                        var hLimpia = new TimeSpan(t.Hours, t.Minutes, 0);
                        if (!listaHoras.Contains(hLimpia))
                        {
                            listaHoras.Add(hLimpia);
                        }
                    }
                    else
                    {
                        MessageBox.Show($"La hora '{p}' no tiene un formato válido (HH:mm).", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                        TxtHorasFijas.Focus();
                        return;
                    }
                }

                if (listaHoras.Count == 0)
                {
                    MessageBox.Show("Debe especificar al menos una hora fija válida (ej. 12:30, 14:00) o activar la repetición por intervalo.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                    TxtHorasFijas.Focus();
                    return;
                }

                Anuncio.Modo = ModoProgramacion.HorasFijas;
                Anuncio.HorasFijas = listaHoras.OrderBy(h => h).ToList();
            }

            // Validar restricciones de horario para evitar colisiones
            if (Anuncio.Activo)
            {
                foreach (var otro in _otrosAnuncios.Where(o => o.Activo))
                {
                    if (Anuncio.VerificarConflicto(otro, out var dia, out var hora, out int totalCoincidencias))
                    {
                        string detalleExtra = totalCoincidencias > 1 
                            ? $" (y {totalCoincidencias - 1} horario(s) coincidente(s) más)" 
                            : "";

                        MessageBox.Show(
                            $"Restricción de Horario:\n\n" +
                            $"El anuncio activo '{otro.Nombre}' ya está programado para sonar el {AnuncioModel.ObtenerNombreDia(dia)} a las {hora:hh\\:mm}{detalleExtra}.\n\n" +
                            $"No se permite programar dos audios a la misma hora para evitar que suenen al mismo tiempo.",
                            "Conflicto de Horario",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }
                }
            }

            DialogResult = true;
            Close();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
