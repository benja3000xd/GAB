using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace GAB.Services
{
    public enum AppTheme
    {
        Dark,
        Light
    }

    public enum ThemeMode
    {
        System,
        Dark,
        Light
    }

    public static class ThemeHelper
    {
        // Atributos DwmSetWindowAttribute (DWM API)
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_CAPTION_COLOR = 35; // Windows 11 Build 22000+
        private const int DWMWA_TEXT_COLOR = 36;    // Windows 11 Build 22000+

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private static AppTheme _currentTheme = AppTheme.Dark;
        private static ThemeMode _currentThemeMode = ThemeMode.System;

        public static AppTheme CurrentTheme => _currentTheme;
        public static ThemeMode CurrentThemeMode => _currentThemeMode;
        public static event Action<AppTheme>? ThemeChanged;

        /// <summary>
        /// Establece el modo de tema ("Sistema", "Oscuro" o "Claro").
        /// </summary>
        public static void SetThemeMode(ThemeMode mode)
        {
            _currentThemeMode = mode;
            if (mode == ThemeMode.System)
            {
                ApplyCurrentSystemTheme();
            }
            else
            {
                ApplyTheme(mode == ThemeMode.Light ? AppTheme.Light : AppTheme.Dark);
            }
        }

        /// <summary>
        /// Establece el modo de tema a partir de una cadena ("Sistema", "Oscuro", "Claro").
        /// </summary>
        public static void SetThemeMode(string modeStr)
        {
            if (modeStr.Equals("Oscuro", StringComparison.OrdinalIgnoreCase) || modeStr.Equals("Dark", StringComparison.OrdinalIgnoreCase))
            {
                SetThemeMode(ThemeMode.Dark);
            }
            else if (modeStr.Equals("Claro", StringComparison.OrdinalIgnoreCase) || modeStr.Equals("Light", StringComparison.OrdinalIgnoreCase))
            {
                SetThemeMode(ThemeMode.Light);
            }
            else
            {
                SetThemeMode(ThemeMode.System);
            }
        }

        /// <summary>
        /// Detecta si Windows está configurado en modo claro para aplicaciones en el registro del sistema.
        /// </summary>
        public static bool IsWindowsInLightTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key != null)
                {
                    var value = key.GetValue("AppsUseLightTheme");
                    if (value is int intValue)
                    {
                        return intValue == 1;
                    }
                }
            }
            catch
            {
                // Silencioso ante entornos restringidos
            }
            return false;
        }

        /// <summary>
        /// Inicializa el soporte de temas y escucha cambios de personalización del sistema en tiempo real.
        /// </summary>
        public static void Initialize(string initialModeStr = "Sistema")
        {
            try
            {
                SystemEvents.UserPreferenceChanged += (sender, args) =>
                {
                    if (args.Category == UserPreferenceCategory.General && _currentThemeMode == ThemeMode.System)
                    {
                        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                        {
                            ApplyCurrentSystemTheme();
                        });
                    }
                };
            }
            catch
            {
                // Entornos sin soporte de SystemEvents
            }

            SetThemeMode(initialModeStr);
        }

        /// <summary>
        /// Aplica automáticamente el tema presente en Windows (Claro u Oscuro).
        /// </summary>
        public static void ApplyCurrentSystemTheme()
        {
            bool useLight = IsWindowsInLightTheme();
            ApplyTheme(useLight ? AppTheme.Light : AppTheme.Dark);
        }

        /// <summary>
        /// Cambia el tema de la aplicación intercambiando los diccionarios de recursos XAML.
        /// </summary>
        public static void ApplyTheme(AppTheme theme)
        {
            _currentTheme = theme;
            string themeUri = theme == AppTheme.Light 
                ? "Themes/LightTheme.xaml" 
                : "Themes/DarkTheme.xaml";

            try
            {
                var app = System.Windows.Application.Current;
                if (app != null)
                {
                    var newDict = new ResourceDictionary { Source = new Uri(themeUri, UriKind.Relative) };
                    var merged = app.Resources.MergedDictionaries;

                    var existing = merged.FirstOrDefault(d => d.Source != null && 
                        (d.Source.OriginalString.Contains("DarkTheme") || d.Source.OriginalString.Contains("LightTheme")));

                    if (existing != null)
                    {
                        int index = merged.IndexOf(existing);
                        merged[index] = newDict;
                    }
                    else
                    {
                        merged.Add(newDict);
                    }

                    // Actualizar barra de título de todas las ventanas abiertas
                    foreach (Window window in app.Windows)
                    {
                        ApplyTitleBarTheme(window);
                    }
                }

                ThemeChanged?.Invoke(_currentTheme);
            }
            catch (Exception ex)
            {
                StorageManager.LogError($"Error aplicando tema {theme}", ex);
            }
        }

        /// <summary>
        /// Mantiene compatibilidad con llamadas anteriores.
        /// </summary>
        public static void ApplyDarkMode(Window window) => ApplyTitleBarTheme(window);

        /// <summary>
        /// Aplica el modo visual (DWM) correspondiente a la barra de título nativa de Windows.
        /// </summary>
        public static void ApplyTitleBarTheme(Window window)
        {
            if (window == null) return;

            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
            {
                ApplyTitleBarThemeToHwnd(hwnd, _currentTheme == AppTheme.Dark);
            }
            else
            {
                void OnSourceInitialized(object? sender, EventArgs e)
                {
                    window.SourceInitialized -= OnSourceInitialized;
                    var handle = new WindowInteropHelper(window).Handle;
                    if (handle != IntPtr.Zero)
                    {
                        ApplyTitleBarThemeToHwnd(handle, _currentTheme == AppTheme.Dark);
                    }
                }
                window.SourceInitialized += OnSourceInitialized;
            }
        }

        private static void ApplyTitleBarThemeToHwnd(IntPtr hwnd, bool isDark)
        {
            try
            {
                int darkMode = isDark ? 1 : 0;
                int hr = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
                if (hr != 0)
                {
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref darkMode, sizeof(int));
                }

                if (Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000)
                {
                    // COLORREF format 0x00BBGGRR
                    // Modo Oscuro: Barra de título DWM a juego #16161B (R=0x16, G=0x16, B=0x1B) -> 0x001B1616
                    // Modo Claro: Barra de título DWM definida #F1F5F9 (R=0xF1, G=0xF5, B=0xF9) -> 0x00F9F5F1
                    int captionColor = isDark ? 0x001B1616 : 0x00F9F5F1;
                    DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));

                    // Color del texto de la barra de título
                    int textColor = isDark ? 0x00FFFFFF : 0x002A170F; // #0F172A (R=0x0F, G=0x17, B=0x2A) -> 0x002A170F
                    DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));
                }
            }
            catch (Exception ex)
            {
                StorageManager.LogError("Error al aplicar estilo DWM a la barra de título", ex);
            }
        }
    }
}
