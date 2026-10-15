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
    /// Интерфейс службы захвата видео с веб-камеры.
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
    /// Сервис захвата видеопотока с веб-камеры с использованием OpenCvSharp4.
    /// Захват кадров и их конвертация в BitmapSource происходят в фоновом потоке,
    /// предотвращая зависание и подтормаживание UI-потока WPF.
    /// </summary>
    public class CameraService : ICameraService
    {
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _captureTask;
        private readonly object _syncLock = new();
        private bool _isDisposed;

        public IWasteDetectorService? Detector { get; set; }
        public float ConfidenceThreshold { get; set; } = 0.65f;

        /// <summary>
        /// Событие готовности очередного кадра для отображения в элементе Image.
        /// Кадр заморожен (.Freeze()) и полностью безопасен для межпоточного использования.
        /// </summary>
        public event Action<BitmapSource>? FrameReady;

        /// <summary>
        /// Событие обнаружения объектов нейросетью на текущем кадре.
        /// </summary>
        public event Action<IReadOnlyList<DetectionResult>>? DetectionsUpdated;

        /// <summary>
        /// Событие изменения состояния камеры (true - запущена, false - остановлена).
        /// </summary>
        public event Action<bool>? StateChanged;

        /// <summary>
        /// Событие ошибки захвата камеры.
        /// </summary>
        public event Action<string>? ErrorOccurred;

        /// <summary>
        /// Событие обновления фактического FPS захвата.
        /// </summary>
        public event Action<double>? FpsUpdated;

        public bool IsRunning { get; private set; }
        public int CurrentCameraIndex { get; private set; } = 0;

        /// <summary>
        /// Запускает захват видеопотока с камеры в фоновом потоке.
        /// </summary>
        /// <param name="cameraIndex">Индекс камеры (по умолчанию 0)</param>
        public void Start(int cameraIndex = 0)
        {
            lock (_syncLock)
            {
                if (IsRunning)
                {
                    if (CurrentCameraIndex == cameraIndex) return;
                    StopInternal();
                }

                CurrentCameraIndex = cameraIndex;
                _cancellationTokenSource = new CancellationTokenSource();
                var token = _cancellationTokenSource.Token;

                IsRunning = true;
                StateChanged?.Invoke(true);

                _captureTask = Task.Run(() => CaptureLoop(cameraIndex, token), token);
            }
        }

        /// <summary>
        /// Останавливает захват видеопотока и освобождает устройство камеры.
        /// </summary>
        public void Stop()
        {
            lock (_syncLock)
            {
                StopInternal();
            }
        }

        private void StopInternal()
        {
            if (!IsRunning && _cancellationTokenSource == null) return;

            try
            {
                _cancellationTokenSource?.Cancel();
                if (_captureTask != null && !_captureTask.IsCompleted)
                {
                    // Даем задаче до 1.5 секунд на корректное закрытие дескриптора камеры
                    _captureTask.Wait(1500);
                }
            }
            catch (Exception)
            {
                // Игнорируем возможные OperationCanceledException при прерывании задачи
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

        /// <summary>
        /// Основной цикл захвата кадров, выполняющийся в фоновом пуле потоков.
        /// </summary>
        private void CaptureLoop(int cameraIndex, CancellationToken token)
        {
            VideoCapture? capture = null;

            try
            {
                // Инициализируем захват через OpenCV (DSHOW или ANY для Windows)
                capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.ANY);

                if (!capture.IsOpened())
                {
                    ErrorOccurred?.Invoke($"Веб-камера (индекс {cameraIndex}) не обнаружена или заблокирована другим процессом.");
                    return;
                }

                // Задаем желаемые базовые параметры
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

                    // Считываем очередной кадр
                    bool success = capture.Read(mat);
                    if (!success || mat.Empty())
                    {
                        // Если несколько кадров подряд не читаются — возможно, камера отключена
                        Thread.Sleep(30);
                        continue;
                    }

                    try
                    {
                        IReadOnlyList<DetectionResult>? detections = null;
                        if (Detector != null && Detector.IsModelLoaded)
                        {
                            var detectedList = Detector.Detect(mat, ConfidenceThreshold);
                            if (detectedList.Count > 0)
                            {
                                Detector.DrawDetections(mat, detectedList);
                            }
                            detections = detectedList;
                        }

                        // Преобразуем Mat из OpenCV в WPF BitmapSource
                        BitmapSource bitmapSource = BitmapSourceConverter.ToBitmapSource(mat);

                        // КРИТИЧЕСКИ ВАЖНО: Freeze() переводит объект в режим только для чтения
                        // и позволяет безопасно передать его из фонового потока в поток UI WPF!
                        bitmapSource.Freeze();

                        // Оповещаем подписчиков
                        FrameReady?.Invoke(bitmapSource);

                        if (detections != null)
                        {
                            DetectionsUpdated?.Invoke(detections);
                        }
                    }
                    catch (Exception ex)
                    {
                        ErrorOccurred?.Invoke($"Ошибка обработки кадра нейросетью: {ex.Message}");
                    }

                    frameCounter++;
                    if (fpsStopwatch.ElapsedMilliseconds >= 1000)
                    {
                        double fps = frameCounter * 1000.0 / fpsStopwatch.ElapsedMilliseconds;
                        FpsUpdated?.Invoke(Math.Round(fps, 1));
                        frameCounter = 0;
                        fpsStopwatch.Restart();
                    }

                    // Ограничиваем цикл приблизительно до ~30 FPS (интервал ~33 мс),
                    // если захват происходит слишком быстро, чтобы снизить нагрузку на CPU
                    int elapsed = (int)stopwatch.ElapsedMilliseconds;
                    int sleepTime = 33 - elapsed;
                    if (sleepTime > 0)
                    {
                        Thread.Sleep(sleepTime);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Корректная остановка по токену отмены
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Критическая ошибка видеопотока: {ex.Message}");
            }
            finally
            {
                try
                {
                    if (capture != null)
                    {
                        capture.Release();
                        capture.Dispose();
                    }
                }
                catch (Exception)
                {
                    // Игнорируем ошибки при освобождении
                }

                IsRunning = false;
                StateChanged?.Invoke(false);
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
