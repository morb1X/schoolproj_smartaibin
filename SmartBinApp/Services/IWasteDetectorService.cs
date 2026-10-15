using System;
using System.Collections.Generic;
using OpenCvSharp;
using SmartBinApp.Models;

namespace SmartBinApp.Services
{
    public interface IWasteDetectorService : IDisposable
    {
        bool IsModelLoaded { get; }
        string? ModelPath { get; }
        IReadOnlyList<string> ClassNames { get; }

        bool LoadModel(string modelPath);
        void UnloadModel();
        List<DetectionResult> Detect(Mat frame, float confidenceThreshold = 0.5f);
        void DrawDetections(Mat frame, IEnumerable<DetectionResult> detections);
    }
}
