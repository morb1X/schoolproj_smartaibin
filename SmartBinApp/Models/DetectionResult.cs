using System;
using OpenCvSharp;

namespace SmartBinApp.Models
{
    /// <summary>
    /// Категория отходов, поддерживаемая системой Smart Bin.
    /// </summary>
    public enum WasteType
    {
        Unknown,
        Plastic,
        Paper,
        Organic
    }

    /// <summary>
    /// Результат распознавания объекта нейросетью YOLO.
    /// </summary>
    public class DetectionResult
    {
        /// <summary>
        /// Исходное имя класса из датасета (например, "plastic", "paper", "cardboard", "banana").
        /// </summary>
        public string RawClassName { get; }

        /// <summary>
        /// Нормализованная категория мусора для нашего умного бака.
        /// </summary>
        public WasteType Category { get; }

        /// <summary>
        /// Название категории на русском языке.
        /// </summary>
        public string CategoryNameRu { get; }

        /// <summary>
        /// Уверенность нейросети от 0.0 до 1.0 (например, 0.94 = 94%).
        /// </summary>
        public float Confidence { get; }

        /// <summary>
        /// Прямоугольная область объекта в координатах исходного кадра.
        /// </summary>
        public Rect BoundingBox { get; }

        /// <summary>
        /// Символ команды для отправки в микроконтроллер Arduino ('P', 'B', 'O' или null).
        /// </summary>
        public char? ArduinoCommand { get; }

        public DetectionResult(string rawClassName, float confidence, Rect boundingBox)
        {
            RawClassName = rawClassName;
            Confidence = confidence;
            BoundingBox = boundingBox;

            // Сопоставление распознанного класса с категориями умного бака
            string lower = rawClassName.ToLowerInvariant();

            if (lower.Contains("plastic") || lower.Contains("bottle") || lower.Contains("cup") ||
                lower.Contains("can") || lower.Contains("polythene") || lower.Contains("bag") ||
                lower.Contains("wrapper") || lower.Contains("foil") || lower.Contains("bowl") ||
                lower.Contains("fork") || lower.Contains("knife") || lower.Contains("spoon") ||
                lower.Contains("wine glass") || lower.Contains("trash") || lower.Contains("metal") ||
                lower.Contains("glass") || lower.Contains("battery"))
            {
                Category = WasteType.Plastic;
                CategoryNameRu = "Пластик / Вторсырье";
                ArduinoCommand = 'P';
            }
            else if (lower.Contains("paper") || lower.Contains("cardboard") || lower.Contains("box") ||
                     lower.Contains("carton") || lower.Contains("newspaper") || lower.Contains("magazine") ||
                     lower.Contains("book") || lower.Contains("tetra"))
            {
                Category = WasteType.Paper;
                CategoryNameRu = "Бумага";
                ArduinoCommand = 'B';
            }
            else if (lower.Contains("organic") || lower.Contains("bio") || lower.Contains("biological") ||
                     lower.Contains("food") || lower.Contains("fruit") || lower.Contains("vegetable") ||
                     lower.Contains("apple") || lower.Contains("banana") || lower.Contains("orange") ||
                     lower.Contains("peel") || lower.Contains("bread") || lower.Contains("sandwich") ||
                     lower.Contains("pizza") || lower.Contains("donut") || lower.Contains("cake") ||
                     lower.Contains("hot dog") || lower.Contains("broccoli") || lower.Contains("carrot"))
            {
                Category = WasteType.Organic;
                CategoryNameRu = "Органика (Био)";
                ArduinoCommand = 'O';
            }
            else
            {
                Category = WasteType.Unknown;
                CategoryNameRu = rawClassName;
                ArduinoCommand = null;
            }
        }

        public string DisplayText => $"{CategoryNameRu} ({Confidence * 100:F0}%)";
    }
}
