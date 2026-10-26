using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading;
using LibreHardwareMonitor.Hardware;
using SystemWM.Models;

namespace SystemWM.Services
{
    /// <summary>
    /// Encapsula o LibreHardwareMonitorLib e expõe os dados já tratados
    /// no formato que a interface usa (CPU, RAM, Discos, GPU).
    /// Precisa rodar como Administrador para ler sensores de temperatura/fan.
    /// </summary>
    public class HardwareMonitorService : IDisposable
    {
        private readonly Computer _computer;

        public HardwareMonitorService()
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsMemoryEnabled = true,
                IsStorageEnabled = true,
                IsGpuEnabled = true,
                IsMotherboardEnabled = true,
                IsControllerEnabled = true, // fans / superIO
                IsNetworkEnabled = false
            };

            try
            {
                _computer.Open();
            }
            catch
            {
                // Se o driver de hardware não estiver disponível na máquina,
                // o app continua abrindo com os dados de sistema e rede.
            }
        }

        /// <summary>Atualiza todos os sensores. Chame antes de ler os valores.</summary>
        private void AtualizarSensores()
        {
            if (_computer == null) return;
            foreach (var hw in _computer.Hardware)
            {
                try
                {
                    hw.Update();
                    foreach (var sub in hw.SubHardware)
                        sub.Update();
                }
                catch
                {
                    // Ignora sensores indisponíveis do hardware atual.
                }
            }
        }

        public DiagnosticoCompleto ColetarTudo(bool incluirDispositivos = true)
        {
            try
            {
                AtualizarSensores();

                var diag = new DiagnosticoCompleto
                {
                    Cpu = ColetarCpu(),
                    Ram = ColetarRam(),
                    Discos = ColetarDiscos(),
                    Gpu = ColetarGpu(),
                    Sistema = ColetarSistema()
                };

                if (incluirDispositivos)
                    diag.Dispositivos = ColetarDispositivos();

                return diag;
            }
            catch
            {
                return new DiagnosticoCompleto();
            }
        }

        public DiagnosticoCompleto ColetarTudoSemDispositivos()
        {
            return ColetarTudo(false);
        }

        private static IEnumerable<ISensor> ObterSensoresDoHardware(IHardware hw)
        {
            var sensores = new List<ISensor>();
            if (hw == null)
                return sensores;

            sensores.AddRange(hw.Sensors);
            sensores.AddRange(hw.SubHardware.SelectMany(sh => sh.Sensors));
            return sensores;
        }

        private static ISensor? BuscarSensor(IHardware hw, SensorType tipo, params string[] nomesPreferidos)
        {
            var sensores = ObterSensoresDoHardware(hw)
                .Where(s => s.SensorType == tipo && s.Value.HasValue && s.Value.Value > 0)
                .ToList();
            if (sensores.Count == 0)
                return ObterSensoresDoHardware(hw).FirstOrDefault(s => s.SensorType == tipo);

            foreach (var nome in nomesPreferidos)
            {
                var sensor = sensores.FirstOrDefault(s => s.Name.Contains(nome, StringComparison.OrdinalIgnoreCase));
                if (sensor != null) return sensor;
            }

            return sensores.OrderByDescending(s => s.Value ?? 0).FirstOrDefault();
        }

        public CpuInfo ColetarCpu()
        {
            var info = new CpuInfo();
            var cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            if (cpu == null) return info;

            info.Nome = cpu.Name;

            var sensoresCpu = ObterSensoresDoHardware(cpu).ToList();

            var usoTotal = sensoresCpu.Where(s => s.SensorType == SensorType.Load && s.Value.HasValue)
                .OrderByDescending(s => s.Value ?? 0)
                .FirstOrDefault(s => s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase)
                    || s.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase)
                    || s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase));

            double usoCpu = usoTotal?.Value ?? 0;
            if (usoCpu <= 0)
            {
                var loadSensors = sensoresCpu.Where(s => s.SensorType == SensorType.Load && s.Value.HasValue).Select(s => s.Value ?? 0).ToList();
                usoCpu = loadSensors.Count > 0 ? loadSensors.Average() : ObterUsoCpuFallback();
            }

            info.UsoAtualPercent = Math.Round(Math.Max(0, Math.Min(100, usoCpu)), 1);

            var clocks = sensoresCpu.Where(s => s.SensorType == SensorType.Clock && s.Value.HasValue && s.Value.Value > 0).ToList();
            if (clocks.Any())
                info.FrequenciaAtualGHz = Math.Round((clocks.Max(s => s.Value) ?? 0) / 1000.0, 2);

            var loadsPorNucleo = sensoresCpu
                .Where(s => s.SensorType == SensorType.Load && s.Value.HasValue && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.Name)
                .Select(s => Math.Round(s.Value ?? 0, 1))
                .ToList();
            if (loadsPorNucleo.Count > 0)
                info.CargaPorNucleo = loadsPorNucleo;

            var temperaturasPorNucleo = sensoresCpu
                .Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.Name)
                .Select(s => Math.Round(s.Value ?? 0, 1))
                .ToList();
            if (temperaturasPorNucleo.Count > 0)
                info.TemperaturaPorNucleo = temperaturasPorNucleo;

            var tempSensors = sensoresCpu
                .Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue)
                .OrderByDescending(s => s.Value ?? 0)
                .ToList();

            var tempPackage = tempSensors.FirstOrDefault(s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase)
                || s.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase)
                || s.Name.Contains("Tdie", StringComparison.OrdinalIgnoreCase));
            var tempCore = tempSensors.FirstOrDefault(s => s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase));
            if (tempPackage != null)
            {
                info.TemperaturaC = Math.Round((double)tempPackage.Value, 1);
            }
            else if (info.TemperaturaPorNucleo.Count > 0)
            {
                info.TemperaturaC = Math.Round(info.TemperaturaPorNucleo.Average(), 1);
            }
            else
            {
                var temp = tempCore ?? tempSensors.FirstOrDefault();
                info.TemperaturaC = Math.Round(temp?.Value ?? 0, 1);
            }

            var power = BuscarSensor(cpu, SensorType.Power, "Package", "CPU", "Total");
            info.TdpWatts = Math.Round(power?.Value ?? 0, 1);

            var volt = BuscarSensor(cpu, SensorType.Voltage, "Core", "CPU", "VID");
            info.VoltagemV = Math.Round(volt?.Value ?? 0, 2);

            info.NucleosFisicos = Environment.ProcessorCount / 2 == 0 ? Environment.ProcessorCount : Environment.ProcessorCount / 2;
            info.Threads = Environment.ProcessorCount;

            // Frequência base via WMI
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT MaxClockSpeed, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
                foreach (ManagementObject mo in searcher.Get())
                {
                    info.FrequenciaBaseGHz = Math.Round(Convert.ToDouble(mo["MaxClockSpeed"]) / 1000.0, 2);
                    info.NucleosFisicos = Convert.ToInt32(mo["NumberOfCores"]);
                    info.Threads = Convert.ToInt32(mo["NumberOfLogicalProcessors"]);
                }
            }
            catch { /* segue sem esse dado se falhar */ }

            // Fans - buscando no motherboard/superIO
            var fans = _computer.Hardware
                .Where(h => h.HardwareType == HardwareType.Motherboard)
                .SelectMany(h => h.SubHardware)
                .SelectMany(sh => sh.Sensors)
                .Where(s => s.SensorType == SensorType.Fan)
                .ToList();

            var fanCpu = fans.FirstOrDefault(f => f.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase));
            var fanSis = fans.FirstOrDefault(f => !f.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase));
            info.FanCpuRpm = fanCpu?.Value ?? 0;
            info.FanSistemaRpm = fanSis?.Value ?? 0;

            if (info.CargaPorNucleo.Count == 0 && info.NucleosFisicos > 1)
            {
                for (var i = 0; i < info.NucleosFisicos; i++)
                    info.CargaPorNucleo.Add(Math.Round(info.UsoAtualPercent, 1));
            }

            if (info.TemperaturaPorNucleo.Count == 0 && info.NucleosFisicos > 1)
            {
                for (var i = 0; i < info.NucleosFisicos; i++)
                    info.TemperaturaPorNucleo.Add(Math.Round(info.TemperaturaC, 1));
            }

            return info;
        }

        public RamInfo ColetarRam()
        {
            var info = new RamInfo();
            var mem = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory);

            double totalGB = 0, disponivelGB = 0;
            if (mem != null)
            {
                var usado = mem.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name.Contains("Used"));
                var disponivel = mem.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name.Contains("Available"));
                var usoPercent = mem.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load);

                info.UsadaGB = Math.Round(usado?.Value ?? 0, 2);
                disponivelGB = Math.Round(disponivel?.Value ?? 0, 2);
                info.DisponivelGB = disponivelGB;
                info.UsoPercent = Math.Round(usoPercent?.Value ?? 0, 1);
                totalGB = Math.Round(info.UsadaGB + disponivelGB, 2);
            }

            if (info.UsoPercent <= 0 && totalGB > 0)
                info.UsoPercent = Math.Round(Math.Max(0, Math.Min(100, ((info.UsadaGB / totalGB) * 100d))), 1);

            // Dados de módulos físicos (tipo, velocidade, slots) via WMI
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Capacity, Speed, MemoryType, ConfiguredClockSpeed FROM Win32_PhysicalMemory");
                int slots = 0;
                double somaCapacidade = 0;
                foreach (ManagementObject mo in searcher.Get())
                {
                    slots++;
                    somaCapacidade += Convert.ToDouble(mo["Capacity"]);
                    info.VelocidadeMHz = Convert.ToDouble(mo["ConfiguredClockSpeed"] ?? mo["Speed"] ?? 0);
                    int tipo = mo["MemoryType"] != null ? Convert.ToInt32(mo["MemoryType"]) : 0;
                    info.Tipo = tipo switch { 26 => "DDR4", 34 => "DDR5", 24 => "DDR3", _ => "DDR" };
                }
                info.SlotsUsados = slots;

                using var slotSearcher = new ManagementObjectSearcher("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray");
                foreach (ManagementObject mo in slotSearcher.Get())
                    info.SlotsTotais = Convert.ToInt32(mo["MemoryDevices"]);

                if (totalGB == 0) totalGB = Math.Round(somaCapacidade / (1024.0 * 1024 * 1024), 2);
            }
            catch { }

            info.TotalGB = totalGB;
            // Cache/Standby (estimativas aproximadas — Windows não expõe isso de forma simples e confiável)
            info.CacheGB = Math.Round(totalGB * 0.07, 2);
            info.EmEsperaGB = Math.Round(totalGB * 0.08, 2);
            info.ComprometidoGB = Math.Round(info.UsadaGB + info.EmEsperaGB, 2);

            return info;
        }

        public static string FormatarSaudeDisco(double? saudePercent)
        {
            if (saudePercent is not double valor || double.IsNaN(valor) || double.IsInfinity(valor) || valor <= 0)
                return "Saudável";

            var percentual = Math.Round(Math.Max(0, Math.Min(100, valor)), 0);
            var status = percentual >= 90
                ? "Excelente"
                : percentual >= 80
                    ? "Bom"
                    : percentual >= 70
                        ? "Saudável"
                        : percentual >= 55
                            ? "OK"
                            : percentual >= 35
                                ? "Ruim"
                                : "Crítico";

            return $"{percentual:0}% ({status})";
        }

        public List<DiscoInfo> ColetarDiscos()
        {
            var lista = new List<DiscoInfo>();
            var storages = _computer.Hardware.Where(h => h.HardwareType == HardwareType.Storage);

            foreach (var disco in storages)
            {
                var d = new DiscoInfo { Modelo = disco.Name, Marca = ExtrairMarcaDisco(disco.Name) };

                var temp = disco.Sensors
                    .Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue && s.Value.Value > 0)
                    .OrderByDescending(s => s.Value ?? 0)
                    .FirstOrDefault()
                    ?? disco.Sensors
                        .Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue)
                        .OrderByDescending(s => s.Value ?? 0)
                        .FirstOrDefault();

                d.TemperaturaC = temp?.Value ?? 0;

                var vida = disco.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Level && s.Name.Contains("Life", StringComparison.OrdinalIgnoreCase));
                var saude = vida?.Value;
                d.SaudePercentTexto = FormatarSaudeDisco(saude);

                var usoSensor = disco.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load)
                               ?? disco.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Data && s.Name.Contains("Used", StringComparison.OrdinalIgnoreCase));
                d.UsoPercent = usoSensor?.Value ?? 0;

                if (d.UsoPercent <= 0)
                {
                    d.UsoPercent = Math.Round(Math.Max(0, Math.Min(100, (d.EspacoUsadoGB / Math.Max(1, d.CapacidadeTotalGB)) * 100d)), 1);
                }

                lista.Add(d);
            }

            // Espaço total/usado/livre e interface via WMI (mais confiável que os sensores para isso)
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Model, InterfaceType, Size FROM Win32_DiskDrive");
                var discosFisicos = searcher.Get().Cast<ManagementObject>().ToList();

                foreach (var dFisico in discosFisicos)
                {
                    string modelo = dFisico["Model"]?.ToString() ?? "";
                    var match = lista.FirstOrDefault(x => modelo.Contains(x.Modelo, StringComparison.OrdinalIgnoreCase)
                                                        || x.Modelo.Contains(modelo, StringComparison.OrdinalIgnoreCase))
                                ?? new DiscoInfo { Modelo = modelo, Marca = ExtrairMarcaDisco(modelo) };

                    if (!lista.Contains(match)) lista.Add(match);

                    match.Marca = ExtrairMarcaDisco(modelo);
                    match.Modelo = modelo;
                    match.CapacidadeTotalGB = Math.Round(Convert.ToDouble(dFisico["Size"] ?? 0) / (1024.0 * 1024 * 1024), 2);
                    match.Interface = dFisico["InterfaceType"]?.ToString() ?? "";
                }

                var volumes = new List<(string DriveLetter, double FreeSpaceGB, double TotalGB)>();
                using var volSearcher = new ManagementObjectSearcher("SELECT DeviceID, FreeSpace, Size FROM Win32_LogicalDisk WHERE DriveType=3");
                foreach (ManagementObject vol in volSearcher.Get())
                {
                    var drive = vol["DeviceID"]?.ToString() ?? "";
                    var freeSpace = Convert.ToDouble(vol["FreeSpace"] ?? 0);
                    var size = Convert.ToDouble(vol["Size"] ?? 0);
                    volumes.Add((drive, Math.Round(freeSpace / (1024.0 * 1024 * 1024), 2), Math.Round(size / (1024.0 * 1024 * 1024), 2)));
                }

                for (var i = 0; i < lista.Count && i < volumes.Count; i++)
                {
                    var disco = lista[i];
                    var volume = volumes[i];

                    disco.EspacoLivreGB = volume.FreeSpaceGB;
                    disco.CapacidadeTotalGB = Math.Max(disco.CapacidadeTotalGB, volume.TotalGB);
                    disco.EspacoUsadoGB = Math.Round(Math.Max(0, disco.CapacidadeTotalGB - disco.EspacoLivreGB), 2);
                    if (string.IsNullOrEmpty(disco.Interface)) disco.Interface = "NVMe / PCIe";
                }

                if (lista.Any())
                {
                    var principal = lista.First();
                    if (principal.CapacidadeTotalGB <= 0 && volumes.Any())
                        principal.CapacidadeTotalGB = volumes.Sum(v => v.TotalGB);
                    if (principal.EspacoLivreGB <= 0 && volumes.Any())
                        principal.EspacoLivreGB = volumes.Sum(v => v.FreeSpaceGB);
                    if (principal.CapacidadeTotalGB > 0)
                        principal.EspacoUsadoGB = Math.Round(Math.Max(0, principal.CapacidadeTotalGB - principal.EspacoLivreGB), 2);
                }
            }
            catch { }

            return lista;
        }

        public List<DeviceInfo> ColetarDispositivos()
        {
            var dispositivos = new List<DeviceInfo>();

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, Manufacturer, Status, PNPClass, ClassGuid, PNPDeviceID FROM Win32_PnPEntity");

                foreach (ManagementObject mo in searcher.Get())
                {
                    var nome = mo["Name"]?.ToString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(nome))
                        continue;

                    var fabricante = mo["Manufacturer"]?.ToString() ?? string.Empty;
                    var status = mo["Status"]?.ToString() ?? string.Empty;
                    var pnpClass = mo["PNPClass"]?.ToString() ?? string.Empty;
                    var classGuid = mo["ClassGuid"]?.ToString() ?? string.Empty;
                    var deviceId = mo["PNPDeviceID"]?.ToString() ?? string.Empty;

                    dispositivos.Add(new DeviceInfo
                    {
                        Nome = nome,
                        Fabricante = fabricante,
                        Status = string.IsNullOrWhiteSpace(status) ? "Desconhecido" : status,
                        Classe = string.IsNullOrWhiteSpace(pnpClass) ? ObterClassePeloGuid(classGuid) : pnpClass,
                        Tipo = ObterTipoDispositivo(pnpClass, classGuid, nome),
                        DeviceId = deviceId
                    });
                }
            }
            catch
            {
                // Silencia falhas de WMI na enumeração de dispositivos.
            }

            return dispositivos
                .OrderBy(d => d.Tipo)
                .ThenBy(d => d.Nome)
                .ToList();
        }

        private static string ObterClassePeloGuid(string classGuid)
        {
            if (string.IsNullOrWhiteSpace(classGuid))
                return "Desconhecida";

            var mapaClasses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["{4d36e96f-e325-11ce-bfc1-08002be10318}"] = "Teclado",
                ["{4d36e96b-e325-11ce-bfc1-08002be10318}"] = "Mouse",
                ["{4d36e96d-e325-11ce-bfc1-08002be10318}"] = "Impressora",
                ["{4d36e972-e325-11ce-bfc1-08002be10318}"] = "Rede",
                ["{4d36e967-e325-11ce-bfc1-08002be10318}"] = "Porta USB",
                ["{4d36e96c-e325-11ce-bfc1-08002be10318}"] = "Áudio",
                ["{4d36e96e-e325-11ce-bfc1-08002be10318}"] = "Vídeo",
                ["{6bdd1fc1-810f-11d0-bec7-08002be2092f}"] = "Câmera"
            };

            return mapaClasses.TryGetValue(classGuid, out var nome) ? nome : classGuid;
        }

        private static string ObterTipoDispositivo(string pnpClass, string classGuid, string nome)
        {
            if (pnpClass.Contains("printer", StringComparison.OrdinalIgnoreCase) || nome.Contains("impressora", StringComparison.OrdinalIgnoreCase))
                return "Impressora";
            if (pnpClass.Contains("mouse", StringComparison.OrdinalIgnoreCase) || nome.Contains("mouse", StringComparison.OrdinalIgnoreCase))
                return "Mouse";
            if (pnpClass.Contains("keyboard", StringComparison.OrdinalIgnoreCase) || nome.Contains("teclado", StringComparison.OrdinalIgnoreCase))
                return "Teclado";
            if (pnpClass.Contains("hid", StringComparison.OrdinalIgnoreCase))
                return "HID";
            if (pnpClass.Contains("net", StringComparison.OrdinalIgnoreCase) || nome.Contains("rede", StringComparison.OrdinalIgnoreCase))
                return "Rede";
            if (pnpClass.Contains("usb", StringComparison.OrdinalIgnoreCase) || classGuid.Equals("{36fc9e60-c465-11cf-8056-444553540000}", StringComparison.OrdinalIgnoreCase))
                return "USB";
            if (pnpClass.Contains("sound", StringComparison.OrdinalIgnoreCase) || pnpClass.Contains("audio", StringComparison.OrdinalIgnoreCase))
                return "Áudio";
            if (pnpClass.Contains("image", StringComparison.OrdinalIgnoreCase) || nome.Contains("câmera", StringComparison.OrdinalIgnoreCase) || nome.Contains("camera", StringComparison.OrdinalIgnoreCase))
                return "Câmera";
            if (pnpClass.Contains("display", StringComparison.OrdinalIgnoreCase) || nome.Contains("monitor", StringComparison.OrdinalIgnoreCase))
                return "Tela";
            if (pnpClass.Contains("battery", StringComparison.OrdinalIgnoreCase))
                return "Bateria";
            if (!string.IsNullOrWhiteSpace(pnpClass))
                return pnpClass;
            if (!string.IsNullOrWhiteSpace(classGuid))
                return ObterClassePeloGuid(classGuid);
            return "Dispositivo";
        }

        private static string ExtrairMarcaDisco(string nome)
        {
            if (string.IsNullOrWhiteSpace(nome)) return "Desconhecida";

            string[] marcas =
            {
                "Samsung", "Kingston", "Western Digital", "WD", "Seagate", "Crucial",
                "Intel", "Toshiba", "SanDisk", "Corsair", "Adata", "Lexar", "Patriot"
            };

            foreach (var marca in marcas)
            {
                if (nome.Contains(marca, StringComparison.OrdinalIgnoreCase))
                    return marca;
            }

            return "Desconhecida";
        }

        private static double ObterUsoCpuFallback()
        {
            try
            {
                using var counter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                counter.NextValue();
                Thread.Sleep(150);
                return Math.Round(Math.Max(0, Math.Min(100, counter.NextValue())), 1);
            }
            catch
            {
                return 0;
            }
        }

        public GpuInfo? ColetarGpu()
        {
            var gpu = _computer.Hardware.FirstOrDefault(h =>
                h.HardwareType == HardwareType.GpuNvidia ||
                h.HardwareType == HardwareType.GpuAmd ||
                h.HardwareType == HardwareType.GpuIntel);

            if (gpu == null) return null; // só GPU integrada básica sem sensores, ou nenhuma dedicada

            var info = new GpuInfo { Nome = gpu.Name };

            var uso = BuscarSensor(gpu, SensorType.Load, "Core", "GPU", "Total");
            info.UsoPercent = Math.Round(uso?.Value ?? 0, 1);

            var temp = BuscarSensor(gpu, SensorType.Temperature, "GPU", "Core", "Package");
            info.TemperaturaC = Math.Round(temp?.Value ?? 0, 1);

            var memTotal = gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase));
            var memUsada = gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("Used", StringComparison.OrdinalIgnoreCase));
            info.MemoriaTotalGB = Math.Round((memTotal?.Value ?? 0) / 1024.0, 2);
            info.MemoriaUsadaGB = Math.Round((memUsada?.Value ?? 0) / 1024.0, 2);

            var power = BuscarSensor(gpu, SensorType.Power, "GPU", "Package", "Total");
            info.ConsumoWatts = Math.Round(power?.Value ?? 0, 1);

            var fan = BuscarSensor(gpu, SensorType.Fan, "GPU", "Fan");
            info.FanRpm = Math.Round(fan?.Value ?? 0, 1);

            return info;
        }

        public SistemaInfo ColetarSistema()
        {
            var info = new SistemaInfo
            {
                NomeHost = Environment.MachineName,
                Usuario = Environment.UserName,
                SistemaOperacional = "Windows",
                Arquitetura = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit"
            };

            try
            {
                using var os = new ManagementObjectSearcher("SELECT Caption, LastBootUpTime FROM Win32_OperatingSystem");
                foreach (ManagementObject mo in os.Get())
                {
                    info.SistemaOperacional = mo["Caption"]?.ToString() ?? "Windows";
                    var boot = ManagementDateTimeConverter.ToDateTime(mo["LastBootUpTime"].ToString());
                    var uptime = DateTime.Now - boot;
                    info.Uptime = $"{uptime.Days} dias, {uptime.Hours} horas, {uptime.Minutes} minutos";
                }

                using var board = new ManagementObjectSearcher("SELECT Product, Manufacturer FROM Win32_BaseBoard");
                foreach (ManagementObject mo in board.Get())
                    info.PlacaMae = $"{mo["Manufacturer"]} {mo["Product"]}".Trim();

                using var bios = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS");
                foreach (ManagementObject mo in bios.Get())
                {
                    var data = mo["ReleaseDate"] != null ? ManagementDateTimeConverter.ToDateTime(mo["ReleaseDate"].ToString()) : (DateTime?)null;
                    info.BiosVersao = $"{mo["SMBIOSBIOSVersion"]}" + (data.HasValue ? $" | {data:MM/dd/yyyy}" : "");
                }

                using var chipset = new ManagementObjectSearcher("SELECT Name FROM Win32_PnPEntity WHERE ClassGuid='{4d36e97d-e325-11ce-bfc1-08002be10318}'");
                foreach (ManagementObject mo in chipset.Get())
                {
                    info.Chipset = mo["Name"]?.ToString() ?? "";
                    break;
                }
            }
            catch { }

            PreencherRede(info);

            return info;
        }

        private void PreencherRede(SistemaInfo info)
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                var ipv4 = Array.Find(host.AddressList, ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                info.IpLocal = ipv4?.ToString() ?? "-";
                info.RedeStatus = ipv4 != null ? "Conectado" : "Sem conexão";
            }
            catch
            {
                info.IpLocal = "-";
                info.RedeStatus = "Desconhecido";
            }

            // IP público real pode ser buscado de forma assíncrona (ver NetworkService.ObterIpPublicoAsync).
            info.IpPublica = "-";
            info.VpnAtiva = System.Linq.Enumerable.Any(
                System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces(),
                ni => ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                      (ni.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase) ||
                       ni.Description.Contains("TAP", StringComparison.OrdinalIgnoreCase) ||
                       ni.Description.Contains("Radmin", StringComparison.OrdinalIgnoreCase) ||
                       ni.Name.Contains("VPN", StringComparison.OrdinalIgnoreCase)));
        }

        public void Dispose()
        {
            _computer.Close();
        }
    }
}
