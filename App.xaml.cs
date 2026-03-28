using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.Win32;
using ZC_ALM_TOOLS.Services.Common;
using ZC_ALM_TOOLS.Services.TiaPortal;
using ZC_ALM_TOOLS.Views;
using ZC_ALM_TOOLS.Views.Launcher;

namespace ZC_ALM_TOOLS
{
    public partial class App : Application
    {
        public App()
        {
            // Inyectar el resolver estricto
            AppDomain.CurrentDomain.AssemblyResolve += ResolveOpenness;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. Evitar que WPF mate la app al cerrar el Launcher
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            StartApplication();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void StartApplication()
        {
            var selectionWindow = new TiaLauncherView();

            if (selectionWindow.ShowDialog() == true)
            {

                try
                {
                    AppConfigService.InitializeEnvironment();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error fatal al cargar la configuración JSON:\n{ex.Message}", "Error Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
                    Current.Shutdown();
                    return;
                }


                if (TiaManager.CurrentProject == null)
                {
                    MessageBox.Show("Advertencia: Conectado a TIA Portal, pero no hay ningún proyecto cargado.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // 2. Instanciar la ventana principal
                MainWindow mainWindow = new MainWindow(TiaManager.Process, TiaManager.CurrentProject);
                this.MainWindow = mainWindow; // Asignarla como principal en la App

                // 3. Restaurar el comportamiento de cierre normal
                this.ShutdownMode = ShutdownMode.OnMainWindowClose;


                mainWindow.Show();
            }
            else
            {
                // Si el usuario cancela, apagamos explícitamente
                Current.Shutdown();
            }
        }

        private Assembly ResolveOpenness(object sender, ResolveEventArgs args)
        {
            if (args.Name.StartsWith("Siemens.Engineering", StringComparison.OrdinalIgnoreCase))
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Siemens\Automation\Openness\18.0\PublicAPI\18.0.0.0"))
                {
                    if (key != null)
                    {
                        string opennessPath = key.GetValue("Siemens.Engineering") as string;
                        if (!string.IsNullOrWhiteSpace(opennessPath))
                        {
                            string directory = Path.GetDirectoryName(opennessPath);

                            // CORRECCIÓN: Obtener el nombre exacto de la DLL que falta (ej: Siemens.Engineering.Contract)
                            string assemblyName = new AssemblyName(args.Name).Name;
                            string fullPath = Path.Combine(directory, assemblyName + ".dll");

                            if (File.Exists(fullPath))
                            {
                                return Assembly.LoadFrom(fullPath);
                            }
                        }
                    }
                }
            }
            return null;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Liberación de memoria
            TiaManager.Dispose();
            base.OnExit(e);
        }
    }
}