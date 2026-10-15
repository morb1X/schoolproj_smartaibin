using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SmartBinApp.Models;

namespace SmartBinApp.Services
{
    /// <summary>
    /// Serial port manager interface for communicating with Arduino.
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
    /// Thread-safe manager for serial communication with Arduino microcontroller.
    /// Handles asynchronous command sending, reception of telemetry (e.g. 'K' acknowledgement),
    /// and graceful recovery on hardware disconnection.
    /// </summary>
    public class SerialManager : ISerialManager
    {
        private SerialPort? _serialPort;
        private readonly SemaphoreSlim _sendSemaphore = new(1, 1);
        private readonly object _stateLock = new();
        private bool _isDisposed;

        public event Action<bool>? ConnectionChanged;
        public event Action<string>? DataReceived;
        public event Action<char>? CommandSent;
        public event Action<string>? ErrorOccurred;

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

            return result.OrderByDescending(d => d.IsArduino).ThenBy(d => d.PortName).ToList();
        }

        public SerialPortDevice? FindArduinoDevice()
        {
            var devices = GetAvailableDevices();
            return devices.FirstOrDefault(d => d.IsArduino);
        }

        public string[] GetAvailablePorts()
        {
            var devices = GetAvailableDevices();
            return devices.Select(d => d.DisplayName).ToArray();
        }

        public bool Open(string portName, int baudRate = 9600)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                ErrorOccurred?.Invoke("COM port name cannot be empty.");
                return false;
            }

            var portMatch = Regex.Match(portName, @"(COM\d+)", RegexOptions.IgnoreCase);
            string actualPortName = portMatch.Success ? portMatch.Groups[1].Value.ToUpperInvariant() : portName.Trim().ToUpperInvariant();

            lock (_stateLock)
            {
                try
                {
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
                        DtrEnable = true,
                        RtsEnable = true,
                        ReadTimeout = 1000,
                        WriteTimeout = 1500
                    };

                    _serialPort.DataReceived += OnSerialDataReceived;
                    _serialPort.Open();

                    _serialPort.DiscardInBuffer();
                    _serialPort.DiscardOutBuffer();

                    ConnectionChanged?.Invoke(true);
                    return true;
                }
                catch (UnauthorizedAccessException)
                {
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
                    }

                    ErrorOccurred?.Invoke($"Port {actualPortName} is in use by another application (e.g. Serial Monitor in Arduino IDE). Close the Serial Monitor and retry.");
                    CloseInternal();
                    return false;
                }
                catch (IOException ex)
                {
                    ErrorOccurred?.Invoke($"I/O error opening port {actualPortName}: {ex.Message}");
                    CloseInternal();
                    return false;
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke($"Failed to open port {actualPortName}: {ex.Message}");
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

        public async Task<bool> SendCommandAsync(char command)
        {
            if (!IsOpen)
            {
                ErrorOccurred?.Invoke("Cannot send command: COM port is not open.");
                return false;
            }

            await _sendSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                SerialPort? port;
                lock (_stateLock)
                {
                    port = _serialPort;
                    if (port == null || !port.IsOpen)
                    {
                        ErrorOccurred?.Invoke("COM port is closed or unavailable.");
                        return false;
                    }
                }

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
                HandlePortFailure($"Connection to Arduino lost (cable disconnected?): {ex.Message}");
                return false;
            }
            catch (TimeoutException)
            {
                ErrorOccurred?.Invoke($"Timeout sending command '{command}' to Arduino.");
                return false;
            }
            catch (Exception ex)
            {
                HandlePortFailure($"Critical error writing to COM port: {ex.Message}");
                return false;
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }

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
                HandlePortFailure($"Port read error (device disconnected): {ex.Message}");
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Error receiving data from port: {ex.Message}");
            }
        }

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
