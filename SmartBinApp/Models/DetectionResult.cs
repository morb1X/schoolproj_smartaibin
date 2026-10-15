using System;
using OpenCvSharp;
using SmartBinApp.Services;

namespace SmartBinApp.Models
{
    /// <summary>
    /// Waste category supported by the AI Smart Bin system.
    /// </summary>
    public enum WasteType
    {
        Unknown,
        Plastic,
        Paper,
        Organic
    }

    /// <summary>
    /// Object detection result produced by the YOLO neural network.
    /// </summary>
    public class DetectionResult
    {
        /// <summary>
        /// Raw class name from the dataset (e.g. "plastic", "paper", "cardboard", "banana").
        /// </summary>
        public string RawClassName { get; }

        /// <summary>
        /// Normalized waste category for the Smart Bin.
        /// </summary>
        public WasteType Category { get; }

        /// <summary>
        /// Localized category display name (English or German).
        /// </summary>
        public string CategoryName => LocalizationService.Instance.GetWasteName(Category);

        /// <summary>
        /// Short localized category name.
        /// </summary>
        public string CategoryShortName => LocalizationService.Instance.GetWasteShortName(Category);

        /// <summary>
        /// Detection confidence score from 0.0 to 1.0 (e.g. 0.94 = 94%).
        /// </summary>
        public float Confidence { get; }

        /// <summary>
        /// Bounding box rectangle in frame coordinates.
        /// </summary>
        public Rect BoundingBox { get; }

        /// <summary>
        /// Hardware command character for Arduino UART ('P', 'B', 'O' or null).
        /// </summary>
        public char? ArduinoCommand { get; }

        public DetectionResult(string rawClassName, float confidence, Rect boundingBox)
        {
            RawClassName = rawClassName;
            Confidence = confidence;
            BoundingBox = boundingBox;

            string lower = rawClassName.ToLowerInvariant();

            if (lower.Contains("plastic") || lower.Contains("bottle") || lower.Contains("cup") ||
                lower.Contains("can") || lower.Contains("polythene") || lower.Contains("bag") ||
                lower.Contains("wrapper") || lower.Contains("foil") || lower.Contains("bowl") ||
                lower.Contains("fork") || lower.Contains("knife") || lower.Contains("spoon") ||
                lower.Contains("wine glass") || lower.Contains("trash") || lower.Contains("metal") ||
                lower.Contains("glass") || lower.Contains("battery"))
            {
                Category = WasteType.Plastic;
                ArduinoCommand = 'P';
            }
            else if (lower.Contains("paper") || lower.Contains("cardboard") || lower.Contains("box") ||
                     lower.Contains("carton") || lower.Contains("newspaper") || lower.Contains("magazine") ||
                     lower.Contains("book") || lower.Contains("tetra"))
            {
                Category = WasteType.Paper;
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
                ArduinoCommand = 'O';
            }
            else
            {
                Category = WasteType.Unknown;
                ArduinoCommand = null;
            }
        }

        public string DisplayText => $"{CategoryName} ({Confidence * 100:F0}%)";
    }
}
