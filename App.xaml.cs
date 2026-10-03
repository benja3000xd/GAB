using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using GAB.Services;

namespace GAB
{
    public partial class App : System.Windows.Application
    {
        private Mutex? _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            RegisterGlobalExceptionHandlers();

            const string appName = "GAB_Gestor_Anuncios_Audio_Mutex_Unique";
            bool createdNew;

            try
            {
                _mutex = new Mutex(true, appName, out createdNew);
            }
            catch (AbandonedMutexException)
            {
                // Si la instancia anterior terminó abruptamente, el mutex fue abandonado.
                // Tomamos posesión válida de la instancia.
                createdNew = true;
            }

            if (!createdNew)
            {
                MessageBox.Show("La aplicación GAB ya se encuentra en ejecución.", "Instancia Múltiple", MessageBoxButton.OK, MessageBoxImage.Information);
                System.Windows.Application.Current.Shutdown();
                return;
            }

            base.OnStartup(e);

            // Permitir que la aplicación permanezca viva en la bandeja del sistema sin ventanas visibles
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Limpieza automática de logs mayores a 30 días
            StorageManager.LimpiarLogsAntiguos(30);

            // Cargar configuración guardada de usuario
            var settings = StorageManager.CargarConfiguracion();

            // Inicializar tema visual según preferencia ("Sistema", "Oscuro" o "Claro")
            ThemeHelper.Initialize(settings.ModoTema);

            // Configurar volumen de atenuación (ducking)
            AudioManager.DuckingTargetVolume = settings.NivelAtenuacion;

            // Evaluar si se debe arrancar minimizado a la bandeja
            bool startMinimized = false;
            foreach (var arg in e.Args)
            {
                if (arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase))
                {
                    startMinimized = true;
                    break;
                }
            }

            var mainWindow = new MainWindow();
            this.MainWindow = mainWindow;

            if (!startMinimized)
            {
                mainWindow.Show();
            }
        }

        private void RegisterGlobalExceptionHandlers()
        {
            // 1. Excepciones no controladas en el hilo de UI de WPF
            DispatcherUnhandledException += (sender, args) =>
            {
                StorageManager.LogError("Excepción no controlada en el hilo UI (Dispatcher)", args.Exception);
                args.Handled = true; // Prevenir cierre forzoso si la app puede continuar
            };

            // 2. Excepciones no controladas críticas a nivel de AppDomain
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    StorageManager.LogError("Excepción no controlada en AppDomain", ex);
                }
            };

            // 3. Excepciones asíncronas no observadas en el ThreadPool
            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                StorageManager.LogError("Excepción asíncrona no observada en TaskScheduler", args.Exception);
                args.SetObserved();
            };
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_mutex != null)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Mutex no poseído por este hilo
                }
                _mutex.Dispose();
                _mutex = null;
            }
            base.OnExit(e);
        }
    }
}
