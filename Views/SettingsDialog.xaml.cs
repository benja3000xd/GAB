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

        private void BtnAbrirAudios_Click(object sender, RoutedEventArgs e)
        {
            StorageManager.AbrirCarpetaAudios();
        }

        private void BtnAbrirLogs_Click(object sender, RoutedEventArgs e)
        {
            StorageManager.AbrirCarpetaLogs();
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
