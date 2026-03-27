using System.Collections.Generic;
using Siemens.Engineering;
using Siemens.Engineering.Hmi;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using ZC_ALM_TOOLS.Models.TiaPortal;

namespace ZC_ALM_TOOLS.Services.TiaPortal
{
    // ==================================================================================================================
    /// <summary>
    /// Servicio encargado de buscar y escanear el proyecto de Tia Portal
    /// </summary>
    public static class TiaDeviceScanner
    {

        // ==================================================================================================================
        /// <summary>
        /// Metodo para escanear el proyecto de Tia Portal
        /// </summary>
        public static List<TiaTarget> ScanProject(Project project)
        {
            var targets = new List<TiaTarget>();

            // Escanear dispositivos en la raíz
            foreach (Device device in project.Devices)
            {
                FindSoftwareInDevice(device, targets);
            }

            // Escanear dispositivos dentro de grupos (carpetas) - Recursivo
            ScanGroups(project.DeviceGroups, targets);

            return targets;
        }

        // ==================================================================================================================
        /// <summary>
        /// Metodo para escanear en equipos dentro de grupos
        /// </summary>
        private static void ScanGroups(DeviceUserGroupComposition groups, List<TiaTarget> targets)
        {
            foreach (DeviceUserGroup group in groups)
            {
                foreach (Device device in group.Devices)
                {
                    FindSoftwareInDevice(device, targets);
                }
                ScanGroups(group.Groups, targets);
            }
        }

        // ==================================================================================================================
        /// <summary>
        /// Metodo para estanear el software en el equipo
        /// </summary>
        private static void FindSoftwareInDevice(Device device, List<TiaTarget> targets)
        {
            foreach (DeviceItem item in device.DeviceItems)
            {
                // Buscamos el contenedor de software
                var container = item.GetService<SoftwareContainer>();
                if (container != null)
                {
                    var software = container.Software;

                    if (software is PlcSoftware plc)
                    {
                        targets.Add(new TiaTarget
                        {
                            Name = device.DeviceItems[1].Name,
                            Type = TargetType.PLC,
                            SoftwareObject = plc,
                            IsChecked = false,
                            DeviceItem = item
                        });
                    }
                    else if (software is HmiTarget hmi)
                    {
                        // Diferenciamos entre Panel (HMI) y PC Station (SCADA)
                        bool isScada = device.Name.ToUpper().Contains("SCADA") || device.Name.ToUpper().Contains("PC");

                        targets.Add(new TiaTarget
                        {
                            Name = device.Name,
                            Type = isScada ? TargetType.SCADA : TargetType.HMI,
                            SoftwareObject = hmi,
                            IsChecked = false,
                            DeviceItem = item
                        });
                    }
                }
            }
        }


    }
}