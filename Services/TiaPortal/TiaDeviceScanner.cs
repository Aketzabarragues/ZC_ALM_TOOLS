using System.Collections.Generic;
using Siemens.Engineering;
using Siemens.Engineering.AddIn;
using Siemens.Engineering.Hmi; // Necesitas referenciar Siemens.Engineering.Hmi.dll
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using ZC_ALM_TOOLS.Models;

namespace ZC_ALM_TOOLS.Services
{
    public static class TiaDeviceScanner
    {
        public static List<TiaTarget> ScanProject(Project project)
        {
            var targets = new List<TiaTarget>();

            // 1. Escanear dispositivos en la raíz
            foreach (Device device in project.Devices)
            {
                FindSoftwareInDevice(device, targets);
            }

            // 2. Escanear dispositivos dentro de grupos (carpetas) - Recursivo
            ScanGroups(project.DeviceGroups, targets);

            return targets;
        }

        private static void ScanGroups(DeviceUserGroupComposition groups, List<TiaTarget> targets)
        {
            foreach (DeviceUserGroup group in groups)
            {
                foreach (Device device in group.Devices)
                {
                    FindSoftwareInDevice(device, targets);
                }
                ScanGroups(group.Groups, targets); // Profundizar en subcarpetas
            }
        }

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
                            IsChecked = false
                        });
                    }
                    else if (software is HmiTarget hmi)
                    {
                        // Diferenciamos entre Panel (HMI) y PC Station (SCADA)
                        // Una forma simple es mirar el nombre o tipo del padre
                        bool isScada = device.Name.ToUpper().Contains("SCADA") || device.Name.ToUpper().Contains("PC");

                        targets.Add(new TiaTarget
                        {
                            Name = device.Name,
                            Type = isScada ? TargetType.SCADA : TargetType.HMI,
                            SoftwareObject = hmi,
                            IsChecked = false
                        });
                    }
                }
            }
        }
    }
}