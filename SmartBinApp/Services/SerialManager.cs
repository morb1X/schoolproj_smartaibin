using System;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using SmartBinApp.Models;

namespace SmartBinApp.Services
{
    /// <summary>
    /// Интерфейс менеджера последовательного порта (COM-порта).
    /// </summary>
    public interface ISerialManager : IDisposable
    {
        bool IsOpen { get; }
        string? CurrentPortName { get; }

        event Action<bool>? ConnectionChanged;
        event Action<string>? DataReceived;
        event Action<char>? CommandSent;
        event Action<string>? ErrorOccurred;

        string[] GetAvailablePorts();
        List<SerialPortDevice> GetAvailableDevices();
        SerialPortDevice? FindArduinoDevice();
        bool Open(string portName, int baudRate = 9600);
        void Close();
        Task<bool> SendCommandAsync(char command);
    }

    /// <summary>
    /// Потокобезопасный менеджер для обмена данными с микроконтроллером Arduino через Serial-порт.
    /// Поддерживает безопасное открытие/закрытие, асинхронную отправку команд,
    /// автоматический прием данных (ответ 'K' от Arduino) и обработку обрыва связи при отключении кабеля.
    /// </summary>
    public class SerialManager : ISerialManager
    {
        private SerialPort? _serialPort;
        private readonly SemaphoreSlim _sendSemaphore = new(1, 1);
        private readonly object _stateLock = new();
        private bool _isDisposed;

        /// <summary>
        /// Событие изменения состояния подключения (true - подключено, false - отключено).
        /// </summary>
        public event Action<bool>? ConnectionChanged;

        /// <summary>
        /// Событие получения текстовых данных от Arduino (например, символ 'K' при завершении цикла крышки).
        /// </summary>
        public event Action<string>? DataReceived;

        /// <summary>
        /// Событие успешной отправки команды ('P', 'B', 'O').
        /// </summary>
        public event Action<char>? CommandSent;

        /// <summary>
        /// Событие возникновения ошибки (например, сбой порта или внезапное отключение кабеля).
        /// </summary>
        public event Action<string>? ErrorOccurred;

        /// <summary>
        /// Флаг активности соединения.
        /// </summary>
        public bool IsOpen
        {
            get
            {
                lock (_stateLock)
                {
                    return _serialPort != null && _serialPort.IsOpen;
                }
            }
        }

        /// <summary>
        /// Имя текущего подключенного порта.
        /// </summary>
        public string? CurrentPortName
        {
            get
            {
                lock (_stateLock)
                {
                    return _serialPort?.PortName;
                }
            }
        }

        /// <summary>
        /// Возвращает список всех доступных устройств с подробным описанием и индикатором Arduino.
        /// </summary>
        public List<SerialPortDevice> GetAvailableDevices()
        {
            var result = new List<SerialPortDevice>();
            var foundPortNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Caption, DeviceID, PNPDeviceID FROM Win32_PnPEntity WHERE Caption LIKE '%(COM%'");
                foreach (ManagementObject obj in searcher.Get())
                {
                    string caption = obj["Caption"]?.ToString() ?? "";
                    string pnpId = obj["PNPDeviceID"]?.ToString() ?? "";

                    var match = Regex.Match(caption, @"\((COM\d+)\)", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        string portName = match.Groups[1].Value.ToUpperInvariant();
                        bool isArduino = caption.Contains("Arduino", StringComparison.OrdinalIgnoreCase)
                                      || caption.Contains("CH340", StringComparison.OrdinalIgnoreCase)
                                      || caption.Contains("USB Serial", StringComparison.OrdinalIgnoreCase)
                                      || caption.Contains("FTDI", StringComparison.OrdinalIgnoreCase)
                                      || caption.Contains("CP210", StringComparison.OrdinalIgnoreCase)
                                      || pnpId.Contains("VID_2341", StringComparison.OrdinalIgnoreCase);

                        result.Add(new SerialPortDevice(portName, caption, isArduino));
                        foundPortNames.Add(portName);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WMI Error]: {ex.Message}");
            }

            // Дополняем портами из стандартного API, если WMI что-то упустил
            try
            {
                string[] basicPorts = SerialPort.GetPortNames();
                foreach (string port in basicPorts)
                {
                    string cleanPort = port.Trim().ToUpperInvariant();
                    if (!foundPortNames.Contains(cleanPort))
                    {
                        result.Add(new SerialPortDevice(cleanPort, cleanPort, false));
                        foundPortNames.Add(cleanPort);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GetPortNames Error]: {ex.Message}");
            }

            // Платы Arduino сортируем первыми в списке
            return result.OrderByDescending(d => d.IsArduino).ThenBy(d => d.PortName).ToList();
        }

        /// <summary>
        /// Автоматически находит устройство Arduino среди подключенных портов.
        /// </summary>
        public SerialPortDevice? FindArduinoDevice()
        {
            var devices = GetAvailableDevices();
            return devices.FirstOrDefault(d => d.IsArduino);
        }

        /// <summary>
        /// Возвращает список всех доступных в операционной системе COM-портов.
        /// </summary>
        public string[] GetAvailablePorts()
        {
            var devices = GetAvailableDevices();
            return devices.Select(d => d.DisplayName).ToArray();
        }

        /// <summary>
        /// Открывает соединение с указанным COM-портом.
        /// </summary>
        /// <param name="portName">Имя порта или строка устройства (например, "COM6" или "Arduino Mega 2560 (COM6)")</param>
        /// <param name="baudRate">Скорость передачи данных (по умолчанию 9600 согласно скетчу Arduino)</param>
        /// <returns>True, если порт успешно открыт</returns>
        public bool Open(string portName, int baudRate = 9600)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                ErrorOccurred?.Invoke("Имя COM-порта не может быть пустым.");
                return false;
            }

            // Автоматически извлекаем чистый идентификатор COM-порта (например, "COM6" из "Arduino Mega 2560 (COM6)")
            var portMatch = Regex.Match(portName, @"(COM\d+)", RegexOptions.IgnoreCase);
            string actualPortName = portMatch.Success ? portMatch.Groups[1].Value.ToUpperInvariant() : portName.Trim().ToUpperInvariant();

            lock (_stateLock)
            {
                try
                {
                    // Если уже открыт другой порт, закрываем его
                    if (_serialPort != null)
                    {
                        CloseInternal();
                    }

                    _serialPort = new SerialPort
                    {
                        PortName = actualPortName,
                        BaudRate = baudRate,
                        Parity = Parity.None,
                        DataBits = 8,
                        StopBits = StopBits.One,
                        Handshake = Handshake.None,
                        Encoding = Encoding.ASCII,
                        // Важно для Arduino: включение DTR и RTS обеспечивает стабильную инициализацию UART
                        DtrEnable = true,
                        RtsEnable = true,
                        ReadTimeout = 1000,
                        WriteTimeout = 1500
                    };

                    _serialPort.DataReceived += OnSerialDataReceived;
                    _serialPort.Open();

                    // Очищаем входные и выходные буферы
                    _serialPort.DiscardInBuffer();
                    _serialPort.DiscardOutBuffer();

                    ConnectionChanged?.Invoke(true);
                    return true;
                }
                catch (UnauthorizedAccessException)
                {
                    // Часто порт занят процессом serial-monitor из Arduino IDE. Пробуем освободить его.
                    TryFreePortLock(actualPortName);

                    try
                    {
                        if (_serialPort != null)
                        {
                            _serialPort.Open();
                            _serialPort.DiscardInBuffer();
                            _serialPort.DiscardOutBuffer();
                            ConnectionChanged?.Invoke(true);
                            return true;
                        }
                    }
                    catch
                    {
                        // Если повторная попытка не удалась - выводим четкую причину
                    }

                    ErrorOccurred?.Invoke($"Порт {actualPortName} занят другой программой (вероятно, в Arduino IDE открыт «Монитор порта»). Закройте монитор порта в Arduino IDE и повторите попытку.");
                    CloseInternal();
                    return false;
                }
                catch (IOException ex)
                {
                    ErrorOccurred?.Invoke($"Ошибка ввода-вывода при открытии порта {actualPortName}: {ex.Message}");
                    CloseInternal();
                    return false;
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke($"Не удалось открыть порт {actualPortName}: {ex.Message}");
                    CloseInternal();
                    return false;
                }
            }
        }

        private static void TryFreePortLock(string portName)
        {
            try
            {
                var procs = System.Diagnostics.Process.GetProcessesByName("serial-monitor");
                foreach (var p in procs)
                {
                    try { p.Kill(); } catch { }
                }
                if (procs.Length > 0)
                {
                    System.Threading.Thread.Sleep(300);
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Безопасно закрывает соединение с COM-портом.
        /// </summary>
        public void Close()
        {
            lock (_stateLock)
            {
                CloseInternal();
            }
        }

        private void CloseInternal()
        {
            if (_serialPort == null) return;

            bool wasOpen = false;
            try
            {
                _serialPort.DataReceived -= OnSerialDataReceived;
                if (_serialPort.IsOpen)
                {
                    wasOpen = true;
                    _serialPort.DiscardInBuffer();
                    _serialPort.DiscardOutBuffer();
                    _serialPort.Close();
                }
            }
            catch (Exception)
            {
                // Игнорируем ошибки при штатном закрытии уже поврежденного порта
            }
            finally
            {
                _serialPort.Dispose();
                _serialPort = null;
                if (wasOpen)
                {
                    ConnectionChanged?.Invoke(false);
                }
            }
        }

        /// <summary>
        /// Потокобезопасная асинхронная отправка управляющего символа в Arduino ('P', 'B', 'O').
        /// Не блокирует вызывающий (UI) поток.
        /// </summary>
        /// <param name="command">Символ команды</param>
        /// <returns>True, если команда отправлена в буфер порта успешно</returns>
        public async Task<bool> SendCommandAsync(char command)
        {
            if (!IsOpen)
            {
                ErrorOccurred?.Invoke("Невозможно отправить команду: COM-порт не открыт.");
                return false;
            }

            // Семафор гарантирует, что одновременные запросы не перемешаются в потоке порта
            await _sendSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                // Повторная проверка состояния под защитой блокировки
                SerialPort? port;
                lock (_stateLock)
                {
                    port = _serialPort;
                    if (port == null || !port.IsOpen)
                    {
                        ErrorOccurred?.Invoke("COM-порт закрыт или недоступен.");
                        return false;
                    }
                }

                // Выполняем запись в отдельном пуле потоков с контролем таймаута
                await Task.Run(() =>
                {
                    char[] buffer = { command };
                    port.Write(buffer, 0, 1);
                }).ConfigureAwait(false);

                CommandSent?.Invoke(command);
                return true;
            }
            catch (IOException ex)
            {
                // Типичная ситуация: кабель Arduino выдернут из USB во время работы
                HandlePortFailure($"Связь с Arduino потеряна (кабель отключен?): {ex.Message}");
                return false;
            }
            catch (TimeoutException)
            {
                ErrorOccurred?.Invoke($"Превышено время ожидания отправки команды '{command}' на Arduino.");
                return false;
            }
            catch (Exception ex)
            {
                HandlePortFailure($"Критическая ошибка отправки в COM-порт: {ex.Message}");
                return false;
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }

        /// <summary>
        /// Обработчик события получения данных из Serial-порта в фоновом потоке.
        /// </summary>
        private void OnSerialDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                SerialPort? port;
                lock (_stateLock)
                {
                    port = _serialPort;
                    if (port == null || !port.IsOpen) return;
                }

                string data = port.ReadExisting();
                if (!string.IsNullOrEmpty(data))
                {
                    DataReceived?.Invoke(data);
                }
            }
            catch (IOException ex)
            {
                // При выдергивании кабеля попытка чтения вызывает IOException
                HandlePortFailure($"Ошибка чтения из порта (устройство отключено): {ex.Message}");
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Ошибка при приеме данных из порта: {ex.Message}");
            }
        }

        /// <summary>
        /// Обрабатывает аварийный сбой порта (например, физическое отключение кабеля)
        /// и переводит менеджер в отключенное состояние.
        /// </summary>
        private void HandlePortFailure(string errorMessage)
        {
            ErrorOccurred?.Invoke(errorMessage);
            Close();
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            Close();
            _sendSemaphore.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
