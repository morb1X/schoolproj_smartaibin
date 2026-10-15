namespace SmartBinApp.Models
{
    /// <summary>
    /// Информация о COM-порте и подключенном устройстве.
    /// </summary>
    public class SerialPortDevice
    {
        public string PortName { get; set; }
        public string Description { get; set; }
        public bool IsArduino { get; set; }

        public SerialPortDevice(string portName, string description, bool isArduino = false)
        {
            PortName = portName;
            Description = description;
            IsArduino = isArduino;
        }

        public string DisplayName => IsArduino ? $"🤖 {Description}" : Description;

        public override string ToString() => DisplayName;
    }
}
