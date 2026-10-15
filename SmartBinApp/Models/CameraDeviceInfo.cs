namespace SmartBinApp.Models
{
    /// <summary>
    /// Модель описания устройства видеозахвата (веб-камеры).
    /// </summary>
    public class CameraDeviceInfo
    {
        public int Index { get; set; }
        public string Name { get; set; }

        public CameraDeviceInfo(int index, string name)
        {
            Index = index;
            Name = name;
        }

        public override string ToString() => Name;
    }
}
