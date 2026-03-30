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
using ZC_ALM_TOOLS.Services.Generator;
using ZC_ALM_TOOLS.ViewModels;
using ZC_ALM_TOOLS.ViewModels.Vci;
using ZC_ALM_TOOLS.ViewModels.Generator;
using ZC_ALM_TOOLS.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace ZC_ALM_TOOLS
{
    public partial class App : Application
    {

        public static IServiceProvider ServiceProvider { get; private set; }



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
                var services = new ServiceCollection();
                ConfigureServices(services);
                ServiceProvider = services.BuildServiceProvider();

                try
                {
                    var appConfig = ServiceProvider.GetRequiredService<IAppConfigService>();
                    appConfig.InitializeEnvironment();
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

                var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
                this.MainWindow = mainWindow;

                this.ShutdownMode = ShutdownMode.OnMainWindowClose;
                mainWindow.Show();
            }
            else
            {
                // Si el usuario cancela, apagamos explícitamente
                Current.Shutdown();
            }
        }



        private void ConfigureServices(IServiceCollection services)
        {
            // 1. Singleton de TIA Portal obtenidos del Launcher
            services.AddSingleton(TiaManager.Process);
            services.AddSingleton(TiaManager.CurrentProject);

            // 2. Servicios Comunes (Nuevas Abstracciones)
            services.AddSingleton<ILogService, AppLogService>();
            services.AddSingleton<IStatusService, AppStatusService>();
            services.AddSingleton<IAppConfigService, AppConfigService>();

            // 3. Servicios de Estado Compartido
            services.AddSingleton<TargetStateService>();

            // 4. Servicios de Negocio 
            services.AddSingleton<TiaPlcService>();
            services.AddSingleton<TiaHmiService>();
            services.AddSingleton<TiaVciService>();
            services.AddSingleton<IDataService, DataService>();

            // 5. ViewModels Hijos (VCI)
            services.AddSingleton<VciMappingViewModel>();
            services.AddSingleton<VciAuditViewModel>();
            services.AddSingleton<VciDocGeneratorViewModel>();
            services.AddSingleton<DevicesViewModel>();
            services.AddSingleton<ParamsAlarmsViewModel>();
            services.AddSingleton<ProcessGeneratorViewModel>();

            // 6. ViewModels Principales
            services.AddSingleton<GeneratorMainViewModel>();
            services.AddSingleton<VciMainViewModel>();
            services.AddSingleton<SettingsMainViewModel>();
            services.AddSingleton<MainViewModel>();

            // 7. Views
            services.AddTransient<MainWindow>();
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