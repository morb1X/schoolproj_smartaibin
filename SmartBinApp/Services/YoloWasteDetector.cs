using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using SmartBinApp.Models;

namespace SmartBinApp.Services
{
    /// <summary>
    /// Waste detection and classification service using YOLOv8 (ONNX).
    /// Supports both object detection (YOLOv8-detect with bounding boxes)
    /// and classification models (YOLOv8-cls) with dynamic input resolution detection.
    /// </summary>
    public class YoloWasteDetector : IWasteDetectorService
    {
        private InferenceSession? _session;
        private string? _inputName;
        private readonly object _inferenceLock = new();
        private bool _isDisposed;

        public int InputWidth { get; private set; } = 640;
        public int InputHeight { get; private set; } = 640;
        public bool IsClassificationModel { get; private set; }

        public bool IsModelLoaded => _session != null;
        public string? ModelPath { get; private set; }
        public IReadOnlyList<string> ClassNames { get; private set; } = new List<string> { "plastic", "paper", "organic" };

        public bool LoadModel(string modelPath)
        {
            lock (_inferenceLock)
            {
                try
                {
                    if (!File.Exists(modelPath))
                    {
                        return false;
                    }

                    UnloadModelInternal();

                    var sessionOptions = new SessionOptions
                    {
                        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                        IntraOpNumThreads = Environment.ProcessorCount
                    };

                    _session = new InferenceSession(modelPath, sessionOptions);
                    ModelPath = modelPath;

                    _inputName = _session.InputMetadata.Keys.FirstOrDefault() ?? "images";

                    if (_session.InputMetadata.TryGetValue(_inputName, out var inputMeta) && inputMeta.Dimensions.Length == 4)
                    {
                        InputHeight = inputMeta.Dimensions[2] > 0 ? inputMeta.Dimensions[2] : 640;
                        InputWidth = inputMeta.Dimensions[3] > 0 ? inputMeta.Dimensions[3] : 640;
                    }
                    else
                    {
                        InputHeight = 640;
                        InputWidth = 640;
                    }

                    var outputMeta = _session.OutputMetadata.Values.FirstOrDefault();
                    IsClassificationModel = outputMeta != null && outputMeta.Dimensions.Length == 2;

                    ParseClassNamesFromMetadata();

                    System.Diagnostics.Debug.WriteLine($"[YoloDetector] Model loaded: {Path.GetFileName(modelPath)}, " +
                                                      $"Input: {InputWidth}x{InputHeight}, " +
                                                      $"Type: {(IsClassificationModel ? "Classification (cls)" : "Detection (detect)")}, " +
                                                      $"Classes: {ClassNames.Count}");

                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[YoloDetector] Model load error: {ex.Message}");
                    UnloadModelInternal();
                    return false;
                }
            }
        }

        public void UnloadModel()
        {
            lock (_inferenceLock)
            {
                UnloadModelInternal();
            }
        }

        private void UnloadModelInternal()
        {
            _session?.Dispose();
            _session = null;
            ModelPath = null;
            IsClassificationModel = false;
        }

        private void ParseClassNamesFromMetadata()
        {
            if (_session == null) return;

            try
            {
                var meta = _session.ModelMetadata.CustomMetadataMap;
                if (meta.TryGetValue("names", out string? namesStr) && !string.IsNullOrWhiteSpace(namesStr))
                {
                    var matches = Regex.Matches(namesStr, @"'([^']+)'|""([^""]+)""");
                    if (matches.Count > 0)
                    {
                        var names = new List<string>();
                        foreach (Match m in matches)
                        {
                            string val = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                            names.Add(val);
                        }
                        ClassNames = names;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[YoloDetector] Warning reading class names: {ex.Message}");
            }
        }

        public List<DetectionResult> Detect(Mat frame, float confidenceThreshold = 0.5f)
        {
            if (frame == null || frame.Empty() || _session == null || _inputName == null)
            {
                return new List<DetectionResult>();
            }

            lock (_inferenceLock)
            {
                if (_session == null || _inputName == null) return new List<DetectionResult>();

                try
                {
                    int originalWidth = frame.Width;
                    int originalHeight = frame.Height;

                    // MODE 1: CLASSIFICATION MODEL (YOLOv8-cls)
                    if (IsClassificationModel)
                    {
                        int cropSize = (int)(Math.Min(originalWidth, originalHeight) * 0.82);
                        int cropX = (originalWidth - cropSize) / 2;
                        int cropY = (originalHeight - cropSize) / 2;
                        var centerRect = new Rect(cropX, cropY, cropSize, cropSize);

                        using var centerCrop = new Mat(frame, centerRect);
                        var (centerClassId, centerScore) = ClassifySubImage(centerCrop);
                        var (fullClassId, fullScore) = ClassifySubImage(frame);

                        int bestClassId = centerScore >= fullScore ? centerClassId : fullClassId;
                        float bestScore = Math.Max(centerScore, fullScore);
                        var targetRect = centerScore >= fullScore
                            ? centerRect
                            : new Rect(originalWidth / 8, originalHeight / 8, originalWidth * 3 / 4, originalHeight * 3 / 4);

                        if (bestScore >= confidenceThreshold && bestClassId >= 0 && bestClassId < ClassNames.Count)
                        {
                            string rawClassName = ClassNames[bestClassId];
                            if (!IsIgnoredClass(rawClassName))
                            {
                                return new List<DetectionResult> { new DetectionResult(rawClassName, bestScore, targetRect) };
                            }
                        }

                        return new List<DetectionResult>();
                    }

                    // MODE 2: OBJECT DETECTION (YOLOv8-detect)
                    var tensor = CreateInputTensor(frame, InputWidth, InputHeight);
                    var inputs = new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor(_inputName, tensor)
                    };

                    using var results = _session.Run(inputs);
                    var outputTensor = results.First().AsTensor<float>();
                    var dimensions = outputTensor.Dimensions;

                    if (dimensions.Length < 3)
                    {
                        return new List<DetectionResult>();
                    }

                    int numFeatures = dimensions[1];
                    int numBoxes = dimensions[2];
                    int numClassesDet = numFeatures - 4;

                    float scaleX = (float)originalWidth / InputWidth;
                    float scaleY = (float)originalHeight / InputHeight;

                    var candidateBoxes = new List<Rect2d>();
                    var candidateScores = new List<float>();
                    var candidateClassIds = new List<int>();

                    for (int i = 0; i < numBoxes; i++)
                    {
                        float maxScore = 0f;
                        int maxClassId = -1;

                        for (int c = 0; c < numClassesDet; c++)
                        {
                            float score = outputTensor[0, 4 + c, i];
                            if (score > maxScore)
                            {
                                maxScore = score;
                                maxClassId = c;
                            }
                        }

                        if (maxScore >= confidenceThreshold && maxClassId >= 0)
                        {
                            string rawClassName = maxClassId < ClassNames.Count ? ClassNames[maxClassId] : $"class_{maxClassId}";

                            if (IsIgnoredClass(rawClassName))
                            {
                                continue;
                            }

                            float cx = outputTensor[0, 0, i];
                            float cy = outputTensor[0, 1, i];
                            float w = outputTensor[0, 2, i];
                            float h = outputTensor[0, 3, i];

                            float x = (cx - w / 2f) * scaleX;
                            float y = (cy - h / 2f) * scaleY;
                            float boxW = w * scaleX;
                            float boxH = h * scaleY;

                            candidateBoxes.Add(new Rect2d(x, y, boxW, boxH));
                            candidateScores.Add(maxScore);
                            candidateClassIds.Add(maxClassId);
                        }
                    }

                    if (candidateBoxes.Count == 0)
                    {
                        return new List<DetectionResult>();
                    }

                    // Non-Maximum Suppression (NMS)
                    CvDnn.NMSBoxes(candidateBoxes, candidateScores, confidenceThreshold, 0.45f, out int[] indices);

                    var finalDetections = new List<DetectionResult>();
                    foreach (int idx in indices)
                    {
                        var box2d = candidateBoxes[idx];
                        int classId = candidateClassIds[idx];
                        float score = candidateScores[idx];

                        string rawClassName = classId < ClassNames.Count ? ClassNames[classId] : $"class_{classId}";

                        int rx = Math.Max(0, (int)box2d.X);
                        int ry = Math.Max(0, (int)box2d.Y);
                        int rw = Math.Min(originalWidth - rx, (int)box2d.Width);
                        int rh = Math.Min(originalHeight - ry, (int)box2d.Height);

                        if (rw > 5 && rh > 5)
                        {
                            finalDetections.Add(new DetectionResult(rawClassName, score, new Rect(rx, ry, rw, rh)));
                        }
                    }

                    return finalDetections;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[YoloDetector] Inference error: {ex.Message}");
                    return new List<DetectionResult>();
                }
            }
        }

        private (int classId, float score) ClassifySubImage(Mat image)
        {
            try
            {
                var tensor = CreateInputTensor(image, InputWidth, InputHeight);
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor(_inputName!, tensor)
                };

                using var results = _session!.Run(inputs);
                var outputTensor = results.First().AsTensor<float>();

                int numClasses = outputTensor.Dimensions[1];
                float maxScore = 0f;
                int maxClassId = -1;

                for (int c = 0; c < numClasses; c++)
                {
                    float score = outputTensor[0, c];
                    if (score > maxScore)
                    {
                        maxScore = score;
                        maxClassId = c;
                    }
                }

                return (maxClassId, maxScore);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[YoloDetector] Classification error: {ex.Message}");
                return (-1, 0f);
            }
        }

        private static DenseTensor<float> CreateInputTensor(Mat sourceMat, int targetWidth, int targetHeight)
        {
            using var resizedMat = new Mat();
            Cv2.Resize(sourceMat, resizedMat, new Size(targetWidth, targetHeight));
            using var rgbMat = new Mat();
            Cv2.CvtColor(resizedMat, rgbMat, ColorConversionCodes.BGR2RGB);

            var tensor = new DenseTensor<float>(new[] { 1, 3, targetHeight, targetWidth });

            unsafe
            {
                byte* dataPtr = (byte*)rgbMat.DataPointer;
                int step = (int)rgbMat.Step();

                for (int y = 0; y < targetHeight; y++)
                {
                    byte* rowPtr = dataPtr + y * step;
                    for (int x = 0; x < targetWidth; x++)
                    {
                        int pixelOffset = x * 3;
                        tensor[0, 0, y, x] = rowPtr[pixelOffset] / 255.0f;     // R
                        tensor[0, 1, y, x] = rowPtr[pixelOffset + 1] / 255.0f; // G
                        tensor[0, 2, y, x] = rowPtr[pixelOffset + 2] / 255.0f; // B
                    }
                }
            }

            return tensor;
        }

        private static bool IsIgnoredClass(string className)
        {
            string lower = className.Trim().ToLowerInvariant();
            return lower == "person"
                || lower == "chair"
                || lower == "couch"
                || lower == "bed"
                || lower == "tv"
                || lower == "laptop"
                || lower == "keyboard"
                || lower == "mouse"
                || lower == "cell phone";
        }

        public void DrawDetections(Mat frame, IEnumerable<DetectionResult> detections)
        {
            if (frame == null || frame.Empty() || detections == null) return;

            foreach (var det in detections)
            {
                Scalar boxColor = det.Category switch
                {
                    WasteType.Plastic => new Scalar(248, 189, 56),  // Cyan
                    WasteType.Paper => new Scalar(11, 158, 245),    // Amber
                    WasteType.Organic => new Scalar(94, 197, 34),   // Green
                    _ => new Scalar(168, 85, 247)                   // Purple
                };

                if (IsClassificationModel)
                {
                    DrawHudCornerBrackets(frame, det.BoundingBox, boxColor, length: 30, thickness: 3);

                    string cmdText = det.ArduinoCommand.HasValue ? $"[CMD: '{det.ArduinoCommand}']" : "";
                    string bannerText = $"AI: {det.CategoryShortName.ToUpperInvariant()} - {det.Confidence * 100:F0}% {cmdText}";

                    var textSize = Cv2.GetTextSize(bannerText, HersheyFonts.HersheySimplex, 0.65, 2, out int baseline);
                    int bannerW = textSize.Width + 28;
                    int bannerH = textSize.Height + 16;
                    int bannerX = Math.Max(12, (frame.Width - bannerW) / 2);
                    int bannerY = 12;

                    Cv2.Rectangle(frame, new Rect(bannerX, bannerY, bannerW, bannerH), new Scalar(15, 23, 42), -1);
                    Cv2.Rectangle(frame, new Rect(bannerX, bannerY, bannerW, bannerH), boxColor, 2, LineTypes.AntiAlias);

                    Cv2.PutText(frame, bannerText, new Point(bannerX + 14, bannerY + bannerH - 8),
                        HersheyFonts.HersheySimplex, 0.65, boxColor, 2, LineTypes.AntiAlias);
                }
                else
                {
                    Cv2.Rectangle(frame, det.BoundingBox, boxColor, 2, LineTypes.AntiAlias);

                    string label = $"{det.CategoryShortName} {det.Confidence * 100:F0}%";
                    var textSize = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, 0.6, 1, out int baseline);

                    int textY = Math.Max(det.BoundingBox.Y, textSize.Height + 8);
                    var textBgRect = new Rect(
                        det.BoundingBox.X,
                        textY - textSize.Height - 6,
                        textSize.Width + 12,
                        textSize.Height + 8
                    );

                    Cv2.Rectangle(frame, textBgRect, boxColor, -1);
                    Cv2.PutText(frame, label, new Point(det.BoundingBox.X + 6, textY - 2),
                        HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 0, 0), 2, LineTypes.AntiAlias);
                }
            }
        }

        private static void DrawHudCornerBrackets(Mat frame, Rect box, Scalar color, int length = 26, int thickness = 3)
        {
            int x1 = Math.Max(0, box.X);
            int y1 = Math.Max(0, box.Y);
            int x2 = Math.Min(frame.Width - 1, box.X + box.Width);
            int y2 = Math.Min(frame.Height - 1, box.Y + box.Height);

            Cv2.Line(frame, new Point(x1, y1), new Point(x1 + length, y1), color, thickness);
            Cv2.Line(frame, new Point(x1, y1), new Point(x1, y1 + length), color, thickness);

            Cv2.Line(frame, new Point(x2, y1), new Point(x2 - length, y1), color, thickness);
            Cv2.Line(frame, new Point(x2, y1), new Point(x2, y1 + length), color, thickness);

            Cv2.Line(frame, new Point(x1, y2), new Point(x1 + length, y2), color, thickness);
            Cv2.Line(frame, new Point(x1, y2), new Point(x1, y2 - length), color, thickness);

            Cv2.Line(frame, new Point(x2, y2), new Point(x2 - length, y2), color, thickness);
            Cv2.Line(frame, new Point(x2, y2), new Point(x2 - length, y2), color, thickness);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            UnloadModel();
            GC.SuppressFinalize(this);
        }
    }
}
