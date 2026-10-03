using System;

namespace GAB.Models
{
    /// <summary>
    /// Representa las preferencias generales configuradas por el usuario en la ventana de Ajustes.
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// Indica si la aplicación debe iniciarse automáticamente al iniciar sesión en Windows (en segundo plano).
        /// </summary>
        public bool IniciarConWindows { get; set; } = false;

        /// <summary>
        /// Modo de tema visual: "Sistema" (sigue Windows), "Oscuro" o "Claro".
        /// </summary>
        public string ModoTema { get; set; } = "Sistema";

        /// <summary>
        /// Nivel de volumen objetivo para la atenuación (ducking) de otras aplicaciones (0.0f a 0.5f).
        /// 0.0f = silencio total (predeterminado).
        /// </summary>
        public float NivelAtenuacion { get; set; } = 0.0f;

        /// <summary>
        /// Indica si se deben mostrar notificaciones del sistema en la bandeja (minimizado a la bandeja o errores).
        /// </summary>
        public bool NotificacionesActivas { get; set; } = true;
    }
}
