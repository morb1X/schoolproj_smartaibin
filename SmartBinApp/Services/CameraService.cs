using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using SmartBinApp.Models;

namespace SmartBinApp.Services
{
    /// <summary>
    /// Interface for webcam video capture service.
    /// </summary>
    public interface ICameraService : IDisposable
    {
        bool IsRunning { get; }
        int CurrentCameraIndex { get; }
        IWasteDetectorService? Detector { get; set; }
        float ConfidenceThreshold { get; set; }

        event Action<BitmapSource>? FrameReady;
        event Action<IReadOnlyList<DetectionResult>>? DetectionsUpdated;
        event Action<bool>? StateChanged;
        event Action<string>? ErrorOccurred;
        event Action<double>? FpsUpdated;

        void Start(int cameraIndex = 0);
        void Stop();
    }

    /// <summary>
    /// Webcam capture service using OpenCvSharp4.
    /// Frame capture and conversion happen on a background thread to prevent UI freezing.
    /// </summary>
    public class CameraService : ICameraService
    {
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _captureTask;
        private readonly object _syncLock = new();
        private bool _isDisposed;

        public IWasteDetectorService? Detector { get; set; }
        public float ConfidenceThreshold { get; set; } = 0.65f;

        public event Action<BitmapSource>? FrameReady;
        public event Action<IReadOnlyList<DetectionResult>>? DetectionsUpdated;
        public event Action<bool>? StateChanged;
        public event Action<string>? ErrorOccurred;
        public event Action<double>? FpsUpdated;

        public bool IsRunning { get; private set; }
        public int CurrentCameraIndex { get; private set; } = -1;

        public void Start(int cameraIndex = 0)
        {
            lock (_syncLock)
            {
                if (IsRunning)
                {
                    StopInternal();
                }

                CurrentCameraIndex = cameraIndex;
                _cancellationTokenSource = new CancellationTokenSource();
                var token = _cancellationTokenSource.Token;

                _captureTask = Task.Run(() => CaptureLoop(cameraIndex, token), token);
                IsRunning = true;
                StateChanged?.Invoke(true);
            }
        }

        public void Stop()
        {
            lock (_syncLock)
            {
                StopInternal();
            }
        }

        private void StopInternal()
        {
            if (!IsRunning) return;

            try
            {
                _cancellationTokenSource?.Cancel();

                if (_captureTask != null && !_captureTask.IsCompleted)
                {
                    _captureTask.Wait(1500);
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                _captureTask = null;
                IsRunning = false;
                StateChanged?.Invoke(false);
            }
        }

        private void CaptureLoop(int cameraIndex, CancellationToken token)
        {
            VideoCapture? capture = null;

            try
            {
                capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.ANY);

                if (!capture.IsOpened())
                {
                    ErrorOccurred?.Invoke($"Webcam (index {cameraIndex}) was not found or is in use by another application.");
                    return;
                }

                capture.Set(VideoCaptureProperties.FrameWidth, 640);
                capture.Set(VideoCaptureProperties.FrameHeight, 480);
                capture.Set(VideoCaptureProperties.Fps, 30);

                using var mat = new Mat();
                var stopwatch = Stopwatch.StartNew();
                int frameCounter = 0;
                var fpsStopwatch = Stopwatch.StartNew();

                while (!token.IsCancellationRequested)
                {
                    stopwatch.Restart();

                    bool success = capture.Read(mat);
                    if (!success || mat.Empty())
                    {
                        Thread.Sleep(30);
                        continue;
                    }

                    try
                    {
                        var detector = Detector;
                        if (detector != null && detector.IsModelLoaded)
                        {
                            var detections = detector.Detect(mat, ConfidenceThreshold);
                            detector.DrawDetections(mat, detections);
                            DetectionsUpdated?.Invoke(detections);
                        }
                        else
                        {
                            DetectionsUpdated?.Invoke(Array.Empty<DetectionResult>());
                        }

                        var bitmap = mat.ToBitmapSource();
                        bitmap.Freeze();

                        FrameReady?.Invoke(bitmap);

                        frameCounter++;
                        if (fpsStopwatch.ElapsedMilliseconds >= 1000)
                        {
                            double currentFps = frameCounter / (fpsStopwatch.ElapsedMilliseconds / 1000.0);
                            FpsUpdated?.Invoke(currentFps);
                            frameCounter = 0;
                            fpsStopwatch.Restart();
                        }

                        int elapsedMs = (int)stopwatch.ElapsedMilliseconds;
                        int targetDelay = 33 - elapsedMs;
                        if (targetDelay > 0)
                        {
                            Thread.Sleep(targetDelay);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[FrameProcessing Exception]: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Camera error: {ex.Message}");
            }
            finally
            {
                capture?.Release();
                capture?.Dispose();
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            Stop();
            GC.SuppressFinalize(this);
        }
    }
}
