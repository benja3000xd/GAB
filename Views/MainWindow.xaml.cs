using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Forms; // Usado para el NotifyIcon
using GAB.Models;
using GAB.Services;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace GAB
{
    public partial class MainWindow : Window
    {
        private NotifyIcon? _notifyIcon;
        private System.Drawing.Icon? _appIcon;
        private SchedulerService _scheduler;
        private ObservableCollection<AnuncioModel> _anunciosList;

        private bool _isExiting = false;
        private bool _balloonShownOnce = false;
        private Guid? _currentlyPlayingId = null;
        private AppSettings _settings;
        private ToolStripMenuItem? _menuItemAutoStart;

        public MainWindow()
        {
            InitializeComponent();
            ThemeHelper.ApplyTitleBarTheme(this);
            _settings = StorageManager.CargarConfiguracion();
            SetupNotifyIcon();
            
            _anunciosList = new ObservableCollection<AnuncioModel>(StorageManager.CargarAnuncios());
            ActualizarNumerosFila();
            GridAnuncios.ItemsSource = _anunciosList;

            _scheduler = new SchedulerService();
            _scheduler.OnAnuncioPlaying += (nombre) =>
            {
                Dispatcher.Invoke(() =>
                {
                    TxtStatus.Text = $"Reproduciendo: {nombre}...";
                });
            };
            _scheduler.OnAnuncioError += (nombre, error) =>
            {
                Dispatcher.Invoke(() =>
                {
                    TxtStatus.Text = $"Error en {nombre}";
                    MostrarNotificacion("Error de Audio", $"No se pudo reproducir {nombre}", ToolTipIcon.Warning);
                });
            };

            AudioManager.PlaybackStopped += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    _currentlyPlayingId = null;
                    ResetAllPlayingStates();
                    TxtStatus.Text = "Listo.";
                });
            };

            _scheduler.Start(_anunciosList.ToList());
        }

        private void ActualizarNumerosFila()
        {
            for (int i = 0; i < _anunciosList.Count; i++)
            {
                _anunciosList[i].NumeroFila = i + 1;
            }
        }

        private void ResetAllPlayingStates()
        {
            foreach (var a in _anunciosList)
            {
                a.IsPlaying = false;
            }
        }

        private System.Drawing.Icon? CargarIconoAplicacion()
        {
            try
            {
                var iconUri = new Uri("pack://application:,,,/Resources/logo_gab.ico", UriKind.RelativeOrAbsolute);
                var streamInfo = Application.GetResourceStream(iconUri);
                if (streamInfo != null)
                {
                    using var stream = streamInfo.Stream;
                    return new System.Drawing.Icon(stream);
                }
            }
            catch
            {
                // Si falla como recurso incrustado, buscar en disco
            }

            try
            {
                string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "logo_gab.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    return new System.Drawing.Icon(iconPath);
                }

                string iconPathRoot = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo_gab.ico");
                if (System.IO.File.Exists(iconPathRoot))
                {
                    return new System.Drawing.Icon(iconPathRoot);
                }
            }
            catch
            {
                // Silencioso
            }

            return null;
        }

        private void SetupNotifyIcon()
        {
            _appIcon = CargarIconoAplicacion();

            _notifyIcon = new NotifyIcon
            {
                Icon = _appIcon ?? System.Drawing.SystemIcons.Application,
                Visible = true,
                Text = "GAB"
            };

            var chkAutoStart = new ToolStripMenuItem("Iniciar con Windows")
            {
                Checked = AutostartService.IsAutoStartEnabled(),
                CheckOnClick = true
            };
            chkAutoStart.Click += (s, e) =>
            {
                AutostartService.SetAutoStart(chkAutoStart.Checked);
                _settings.IniciarConWindows = chkAutoStart.Checked;
                StorageManager.GuardarConfiguracion(_settings);
            };
            _menuItemAutoStart = chkAutoStart;

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add(chkAutoStart);
            contextMenu.Items.Add("Ajustes...", null, (s, e) => AbrirAjustes());
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("Abrir", null, (s, e) => MostrarVentana());
            contextMenu.Items.Add("Salir", null, (s, e) => SalirAplicacion());
            
            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.DoubleClick += (s, e) => MostrarVentana();
        }

        public void MostrarNotificacion(string titulo, string mensaje, ToolTipIcon icono = ToolTipIcon.Info)
        {
            if (!_settings.NotificacionesActivas) return;

            try
            {
                _notifyIcon?.ShowBalloonTip(3000, titulo, mensaje, icono);
            }
            catch
            {
                // Ignorar errores en sistemas donde ShowBalloonTip no esté disponible
            }
        }

        private void BtnAjustes_Click(object sender, RoutedEventArgs e)
        {
            AbrirAjustes();
        }

        private void AbrirAjustes()
        {
            var dlg = new GAB.Views.SettingsDialog(_settings)
            {
                Owner = this
            };

            bool? res = dlg.ShowDialog();
            if (res == true || dlg.AnunciosFueronModificados)
            {
                if (_menuItemAutoStart != null)
                {
                    _menuItemAutoStart.Checked = _settings.IniciarConWindows;
                }

                if (dlg.AnunciosFueronModificados)
                {
                    RecargarAnuncios();
                }
            }
        }

        private void MostrarVentana()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                this.Hide();
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExiting)
            {
                // Evitamos cerrar la app con la X, en su lugar la ocultamos a la bandeja
                e.Cancel = true;
                this.Hide();

                if (!_balloonShownOnce)
                {
                    MostrarNotificacion("GAB", "GAB continúa ejecutándose en segundo plano.", ToolTipIcon.Info);
                    _balloonShownOnce = true;
                }
            }
        }

        private void SalirAplicacion()
        {
            _isExiting = true;
            AudioManager.StopPlayback();
            _scheduler.Stop();
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            _appIcon?.Dispose();
            Application.Current.Shutdown();
        }

        private void GuardarCambios()
        {
            StorageManager.GuardarAnuncios(_anunciosList.ToList());
            _scheduler.UpdateAnuncios(_anunciosList.ToList());
            GridAnuncios.Items.Refresh();
        }

        public void RecargarAnuncios()
        {
            var cargados = StorageManager.CargarAnuncios();
            _anunciosList.Clear();
            foreach (var a in cargados)
            {
                _anunciosList.Add(a);
            }
            ActualizarNumerosFila();
            _scheduler.UpdateAnuncios(_anunciosList.ToList());
            GridAnuncios.Items.Refresh();
        }

        private void GridAnuncios_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (GridAnuncios.SelectedItem is AnuncioModel)
            {
                BtnEditar_Click(sender, e);
            }
        }

        private void BtnAgregar_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new EditDialog(new AnuncioModel(), _anunciosList.ToList());
            if (dialog.ShowDialog() == true)
            {
                _anunciosList.Add(dialog.Anuncio);
                ActualizarNumerosFila();
                GuardarCambios();
            }
        }

        private void BtnEditar_Click(object sender, RoutedEventArgs e)
        {
            if (GridAnuncios.SelectedItem is AnuncioModel seleccionado)
            {
                var dialog = new EditDialog(seleccionado, _anunciosList.ToList());
                if (dialog.ShowDialog() == true)
                {
                    // Aplicar las modificaciones al elemento existente
                    seleccionado.Nombre = dialog.Anuncio.Nombre;
                    seleccionado.RutaAudio = dialog.Anuncio.RutaAudio;
                    seleccionado.DiasSemana = dialog.Anuncio.DiasSemana.ToList();
                    seleccionado.Modo = dialog.Anuncio.Modo;
                    seleccionado.HorasFijas = dialog.Anuncio.HorasFijas.ToList();
                    seleccionado.HoraInicio = dialog.Anuncio.HoraInicio;
                    seleccionado.HoraFin = dialog.Anuncio.HoraFin;
                    seleccionado.IntervaloMinutos = dialog.Anuncio.IntervaloMinutos;

                    seleccionado.NotifyPropertiesChanged();
                    GuardarCambios();
                }
            }
            else
            {
                MessageBox.Show("Por favor, seleccione un anuncio para editar.", "Editar", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if (GridAnuncios.SelectedItem is AnuncioModel seleccionado)
            {
                if (MessageBox.Show($"¿Eliminar anuncio seleccionado?", "Eliminar", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    if (seleccionado.IsPlaying)
                    {
                        AudioManager.StopPlayback();
                    }
                    _anunciosList.Remove(seleccionado);
                    ActualizarNumerosFila();
                    GuardarCambios();
                }
            }
            else
            {
                MessageBox.Show("Por favor, seleccione un anuncio para eliminar.", "Eliminar", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void BtnPlayRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.DataContext is AnuncioModel anuncio)
            {
                // Toggle: si ya está reproduciendo este mismo anuncio, detenerlo de inmediato
                if (AudioManager.IsPlaying && _currentlyPlayingId == anuncio.Id)
                {
                    AudioManager.StopPlayback();
                    anuncio.IsPlaying = false;
                    _currentlyPlayingId = null;
                    TxtStatus.Text = "Listo.";
                    return;
                }

                // Si otro anuncio estaba en reproducción, detenerlo primero
                if (AudioManager.IsPlaying)
                {
                    AudioManager.StopPlayback();
                }

                ResetAllPlayingStates();

                var rutaValida = StorageManager.ObtenerRutaValidaAudio(anuncio.RutaAudio);
                if (!string.IsNullOrEmpty(rutaValida) && System.IO.File.Exists(rutaValida))
                {
                    _currentlyPlayingId = anuncio.Id;
                    anuncio.IsPlaying = true;
                    TxtStatus.Text = $"Reproduciendo: {anuncio.Nombre}...";
                    await AudioManager.PlayAudioAsync(rutaValida);
                }
                else
                {
                    MessageBox.Show($"No se encuentra el archivo de audio:\n{anuncio.RutaAudio}\n\nVerifique que el archivo exista.", "Audio no encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void BtnVerLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var logsFolder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!System.IO.Directory.Exists(logsFolder))
                {
                    System.IO.Directory.CreateDirectory(logsFolder);
                }

                var logHoy = System.IO.Path.Combine(logsFolder, $"gab-{DateTime.Now:yyyy-MM-dd}.log");
                
                // Si aún no se han registrado eventos hoy, inicializar el archivo con cabecera informativa
                if (!System.IO.File.Exists(logHoy))
                {
                    var contenidoInicial = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Registro de eventos GAB - Fecha: {DateTime.Now:yyyy-MM-dd}{Environment.NewLine}" +
                                           $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Estado: Sistema activo. Sin reproducciones registradas aún hoy.{Environment.NewLine}";
                    System.IO.File.WriteAllText(logHoy, contenidoInicial);
                }

                // Abrir directamente el archivo de texto en el visor predeterminado (ej. Bloc de notas)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = logHoy,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo abrir el archivo de log: {ex.Message}", "Ver Log", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Window_DragEnter(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                var files = (string[]?)e.Data.GetData(System.Windows.DataFormats.FileDrop);
                if (files != null && files.Length > 0 && files.Any(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
                {
                    e.Effects = System.Windows.DragDropEffects.Copy;
                    e.Handled = true;
                    return;
                }
            }
            e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                var files = (string[]?)e.Data.GetData(System.Windows.DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    var jsonFile = files.FirstOrDefault(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
                    if (jsonFile != null)
                    {
                        ProcesarImportacionJson(jsonFile);
                    }
                }
            }
        }

        private void ProcesarImportacionJson(string rutaArchivo)
        {
            try
            {
                var nombreArchivo = System.IO.Path.GetFileName(rutaArchivo);
                var actuales = _anunciosList.ToList();
                bool reemplazar = false;

                if (actuales.Count > 0)
                {
                    var opcion = MessageBox.Show(
                        $"¿Cómo deseas importar el archivo '{nombreArchivo}'?\n\n" +
                        "• Sí: REEMPLAZAR todos los anuncios actuales por los importados.\n" +
                        "• No: AÑADIR los anuncios a la lista actual sin borrar los existentes.\n" +
                        "• Cancelar: Cancelar la importación.",
                        "Importar Anuncios (JSON)",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);

                    if (opcion == MessageBoxResult.Cancel)
                        return;

                    reemplazar = (opcion == MessageBoxResult.Yes);
                }

                var (importados, encontrados, faltantes, _) = StorageManager.ImportarAnunciosDesdeArchivo(
                    rutaArchivo,
                    reemplazar,
                    actuales);

                if (importados == 0)
                {
                    MessageBox.Show("No se encontraron anuncios válidos en el archivo seleccionado.",
                        "Importar Anuncios", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                RecargarAnuncios();

                string mensaje = $"Se han importado {importados} anuncio(s) correctamente.\n" +
                                 $"• Audios vinculados con éxito: {encontrados}";

                if (faltantes > 0)
                {
                    mensaje += $"\n• Audios pendientes de ubicar: {faltantes}\n\n" +
                               "Los audios que no estaban en la ruta se activarán automáticamente cuando coloques los archivos MP3 dentro de la carpeta 'GAB_AUDIOS'.";
                }

                MessageBox.Show(mensaje, "Importación Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al importar el archivo: {ex.Message}", "Error al Importar", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
