using System;
using System.Windows;
using GAB.Models;
using GAB.Services;

namespace GAB.Views
{
    public partial class SettingsDialog : Window
    {
        private readonly AppSettings _settings;
        private readonly string _initialModoTema;
        private bool _isInitializing = true;

        public SettingsDialog(AppSettings settings)
        {
            InitializeComponent();
            ThemeHelper.ApplyTitleBarTheme(this);

            _settings = settings;
            _initialModoTema = settings.ModoTema;

            CargarValoresEnUI();
            _isInitializing = false;
        }

        private void CargarValoresEnUI()
        {
            // Sincronizar estado real del registro de autoarranque
            _settings.IniciarConWindows = AutostartService.IsAutoStartEnabled();
            ChkAutostart.IsChecked = _settings.IniciarConWindows;
            ChkNotificaciones.IsChecked = _settings.NotificacionesActivas;

            // Tema visual
            if (_settings.ModoTema.Equals("Oscuro", StringComparison.OrdinalIgnoreCase))
            {
                RbTemaOscuro.IsChecked = true;
            }
            else if (_settings.ModoTema.Equals("Claro", StringComparison.OrdinalIgnoreCase))
            {
                RbTemaClaro.IsChecked = true;
            }
            else
            {
                RbTemaSistema.IsChecked = true;
            }

            // Nivel de atenuación (ducking)
            int porcentaje = (int)Math.Round(_settings.NivelAtenuacion * 100.0f);
            porcentaje = Math.Clamp(porcentaje, 0, 50);
            SliderDucking.Value = porcentaje;
            ActualizarEtiquetaDucking(porcentaje);
        }

        private void ActualizarEtiquetaDucking(int porcentaje)
        {
            if (porcentaje == 0)
            {
                TxtDuckingValor.Text = "0% (Silencio total)";
            }
            else
            {
                TxtDuckingValor.Text = $"{porcentaje}% (Fondo tenue)";
            }
        }

        private void SliderDucking_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtDuckingValor == null) return;
            int porcentaje = (int)Math.Round(e.NewValue);
            ActualizarEtiquetaDucking(porcentaje);
        }

        private void RbTema_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            string modo = "Sistema";
            if (RbTemaOscuro.IsChecked == true) modo = "Oscuro";
            else if (RbTemaClaro.IsChecked == true) modo = "Claro";

            ThemeHelper.SetThemeMode(modo);
            ThemeHelper.ApplyTitleBarTheme(this);
        }

        public bool AnunciosFueronModificados { get; private set; } = false;

        private void BtnAbrirAudios_Click(object sender, RoutedEventArgs e)
        {
            StorageManager.AbrirCarpetaAudios();
        }

        private void BtnAbrirLogs_Click(object sender, RoutedEventArgs e)
        {
            StorageManager.AbrirCarpetaLogs();
        }

        private void BtnImportarAnuncios_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Importar anuncios desde archivo JSON",
                    Filter = "Archivos JSON (*.json)|*.json|Todos los archivos (*.*)|*.*",
                    CheckFileExists = true
                };

                if (dlg.ShowDialog() != true) return;

                var actuales = StorageManager.CargarAnuncios();
                bool reemplazar = false;

                if (actuales.Count > 0)
                {
                    var res = MessageBox.Show(
                        "¿Deseas reemplazar todos tus anuncios actuales con los del archivo importado?\n\n" +
                        "• Sí: Reemplaza la lista completa.\n" +
                        "• No: Añade los anuncios nuevos conservando los actuales.\n" +
                        "• Cancelar: Cancela la importación.",
                        "Importar Anuncios",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);

                    if (res == MessageBoxResult.Cancel) return;
                    reemplazar = (res == MessageBoxResult.Yes);
                }

                var (importados, encontrados, faltantes, listaFinal) = 
                    StorageManager.ImportarAnunciosDesdeArchivo(dlg.FileName, reemplazar, actuales);

                if (importados == 0)
                {
                    MessageBox.Show("No se encontraron anuncios válidos en el archivo seleccionado.", 
                        "Importar Anuncios", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                AnunciosFueronModificados = true;

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

        private void BtnExportarAnuncios_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var actuales = StorageManager.CargarAnuncios();
                if (actuales.Count == 0)
                {
                    MessageBox.Show("No hay anuncios programados para exportar.", "Exportar Anuncios", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Exportar anuncios a JSON",
                    Filter = "Archivo JSON (*.json)|*.json",
                    FileName = $"gab_anuncios_{DateTime.Now:yyyyMMdd}.json"
                };

                if (dlg.ShowDialog() != true) return;

                StorageManager.ExportarAnunciosAArchivo(dlg.FileName, actuales);
                MessageBox.Show($"Se han exportado {actuales.Count} anuncio(s) correctamente en:\n{dlg.FileName}", "Exportación Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al exportar: {ex.Message}", "Error al Exportar", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            // Guardar valores en el modelo
            _settings.IniciarConWindows = ChkAutostart.IsChecked == true;
            _settings.NotificacionesActivas = ChkNotificaciones.IsChecked == true;

            string modo = "Sistema";
            if (RbTemaOscuro.IsChecked == true) modo = "Oscuro";
            else if (RbTemaClaro.IsChecked == true) modo = "Claro";
            _settings.ModoTema = modo;

            _settings.NivelAtenuacion = (float)(SliderDucking.Value / 100.0);

            // Aplicar cambios inmediatamente
            AutostartService.SetAutoStart(_settings.IniciarConWindows);
            AudioManager.DuckingTargetVolume = _settings.NivelAtenuacion;
            ThemeHelper.SetThemeMode(_settings.ModoTema);
            StorageManager.GuardarConfiguracion(_settings);

            DialogResult = true;
            Close();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            // Revertir tema si fue previsualizado pero cancelado
            ThemeHelper.SetThemeMode(_initialModoTema);
            DialogResult = false;
            Close();
        }
    }
}
