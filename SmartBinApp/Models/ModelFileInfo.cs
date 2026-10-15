namespace SmartBinApp.Models
{
    /// <summary>
    /// Описание доступного файла нейросети ONNX для выбора в интерфейсе.
    /// </summary>
    public class ModelFileInfo
    {
        public string Name { get; set; }
        public string FilePath { get; set; }
        public string Description { get; set; }

        public ModelFileInfo(string name, string filePath, string description = "")
        {
            Name = name;
            FilePath = filePath;
            Description = description;
        }

        public override string ToString() => Name;
    }
}
