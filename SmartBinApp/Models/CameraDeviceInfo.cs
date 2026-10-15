namespace SmartBinApp.Models
{
    /// <summary>
    /// Model representing a webcam capture device.
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
