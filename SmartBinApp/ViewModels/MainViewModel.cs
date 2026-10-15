using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SmartBinApp.Models;
using SmartBinApp.Services;

namespace SmartBinApp.ViewModels
{
    /// <summary>
    /// Главная ViewModel приложения AI Smart Bin.
    /// Связывает бизнес-логику работы с веб-камерой, нейросетью YOLOv8 (ONNX)
    /// и микроконтроллером Arduino с пользовательским интерфейсом WPF.
    /// </summary>
    public class MainViewModel : ViewModelBase, IDisposable
    {
        private readonly ISerialManager _serialManager;
        private readonly ICameraService _cameraService;
        private readonly IWasteDetectorService _wasteDetector;

        private string? _selectedPort;
        private bool _isConnected;
        private bool _isDemoModeEnabled;
        private bool _isCameraRunning;
        private double _cameraFps;
        private BitmapSource? _currentFrame;
        private string _statusMessage = "Готов к работе. Подключите Arduino (или включите Демо-режим) и запустите камеру.";
        private bool _isArduinoBusy;
        private CancellationTokenSource? _lidTimeoutCts;
        private bool _isDisposed;
        private CameraDeviceInfo? _selectedCamera;

        // Поля состояния ИИ (YOLOv8)
        private bool _isModelLoaded;
        private string _modelName = "Модель не загружена";
        private ModelFileInfo? _selectedModel;
        private bool _isAutoModeEnabled = true;
        private double _confidenceThreshold = 0.40;
        private string _lastDetectionText = "Ожидание объекта перед камерой...";
        private WasteType _lastDetectedCategory = WasteType.Unknown;
        private DateTime _lastAutoTriggerTime = DateTime.MinValue;

        private SerialPortDevice? _selectedPortDevice;

        public ObservableCollection<SerialPortDevice> AvailablePorts { get; } = new();
        public ObservableCollection<CameraDeviceInfo> AvailableCameras { get; } = new();
        public ObservableCollection<ModelFileInfo> AvailableModels { get; } = new();
        public ObservableCollection<LogMessage> Logs { get; } = new();

        #region Свойства подключения и камеры

        public SerialPortDevice? SelectedPortDevice
        {
            get => _selectedPortDevice;
            set
            {
                if (SetProperty(ref _selectedPortDevice, value))
                {
                    _selectedPort = value?.PortName;
                    OnPropertyChanged(nameof(SelectedPort));
                    ConnectCommand.RaiseCanExecuteChanged();
                    AutoDetectAndConnectCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public string? SelectedPort
        {
            get => _selectedPort;
            set
            {
                if (SetProperty(ref _selectedPort, value))
                {
                    if (value != null && (_selectedPortDevice == null || _selectedPortDevice.PortName != value))
                    {
                        _selectedPortDevice = AvailablePorts.FirstOrDefault(p => p.PortName == value || p.Description.Contains(value));
                        OnPropertyChanged(nameof(SelectedPortDevice));
                    }
                    ConnectCommand.RaiseCanExecuteChanged();
                    AutoDetectAndConnectCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (SetProperty(ref _isConnected, value))
                {
                    OnPropertyChanged(nameof(CanSendCommands));
                    OnPropertyChanged(nameof(ArduinoStatusText));
                    OnPropertyChanged(nameof(ArduinoStatusBrush));
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }

        public bool IsDemoModeEnabled
        {
            get => _isDemoModeEnabled;
            set
            {
                if (SetProperty(ref _isDemoModeEnabled, value))
                {
                    OnPropertyChanged(nameof(CanSendCommands));
                    OnPropertyChanged(nameof(ArduinoStatusText));
                    OnPropertyChanged(nameof(ArduinoStatusBrush));
                    RaiseCommandsCanExecuteChanged();
                    AddLog(value
                        ? "[Демо-режим] Эмулятор Arduino ВКЛЮЧЕН. Вы можете тестировать сортировку без реальной платы."
                        : "[Демо-режим] Эмулятор Arduino ВЫКЛЮЧЕН.",
                        LogLevel.Info);
                }
            }
        }

        public string ArduinoStatusText
        {
            get
            {
                if (IsConnected) return "Arduino: В сети";
                if (IsDemoModeEnabled) return "Arduino: Демо (Эмулятор)";
                return "Arduino: Отключен";
            }
        }

        public System.Windows.Media.Brush ArduinoStatusBrush
        {
            get
            {
                if (IsConnected) return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 222, 128)); // #4ADE80
                if (IsDemoModeEnabled) return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 191, 36)); // #FBBF24
                return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184)); // #94A3B8
            }
        }

        public CameraDeviceInfo? SelectedCamera
        {
            get => _selectedCamera;
            set
            {
                if (SetProperty(ref _selectedCamera, value) && value != null)
                {
                    if (IsCameraRunning)
                    {
                        _cameraService.Stop();
                        _cameraService.Start(value.Index);
                        AddLog($"Переключение на камеру: {value.Name}", LogLevel.Info);
                    }
                }
            }
        }

        public bool IsCameraRunning
        {
            get => _isCameraRunning;
            private set
            {
                if (SetProperty(ref _isCameraRunning, value))
                {
                    OnPropertyChanged(nameof(CameraToggleText));
                }
            }
        }

        public double CameraFps
        {
            get => _cameraFps;
            private set => SetProperty(ref _cameraFps, value);
        }

        public BitmapSource? CurrentFrame
        {
            get => _currentFrame;
            private set => SetProperty(ref _currentFrame, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public bool IsArduinoBusy
        {
            get => _isArduinoBusy;
            private set
            {
                if (SetProperty(ref _isArduinoBusy, value))
                {
                    OnPropertyChanged(nameof(CanSendCommands));
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }

        public bool CanSendCommands => (IsConnected || IsDemoModeEnabled) && !IsArduinoBusy;

        public string CameraToggleText => IsCameraRunning ? "Остановить камеру" : "Запустить камеру";

        #endregion

        #region Свойства нейросети (ИИ)

        public bool IsModelLoaded
        {
            get => _isModelLoaded;
            private set
            {
                if (SetProperty(ref _isModelLoaded, value))
                {
                    OnPropertyChanged(nameof(ModelStatusBadge));
                }
            }
        }

        public string ModelName
        {
            get => _modelName;
            private set => SetProperty(ref _modelName, value);
        }

        public ModelFileInfo? SelectedModel
        {
            get => _selectedModel;
            set
            {
                if (SetProperty(ref _selectedModel, value) && value != null)
                {
                    LoadModelFile(value.FilePath);
                }
            }
        }

        public string ModelStatusBadge => IsModelLoaded ? $"✓ {ModelName}" : "✗ Модель не загружена";

        public bool IsAutoModeEnabled
        {
            get => _isAutoModeEnabled;
            set
            {
                if (SetProperty(ref _isAutoModeEnabled, value))
                {
                    AddLog(value
                        ? "[Режим ИИ] Автоматическая сортировка ВКЛЮЧЕНА."
                        : "[Режим ИИ] Автоматическая сортировка ВЫКЛЮЧЕНА (ручной режим).",
                        LogLevel.Info);
                }
            }
        }

        public double ConfidenceThreshold
        {
            get => _confidenceThreshold;
            set
            {
                if (SetProperty(ref _confidenceThreshold, value))
                {
                    _cameraService.ConfidenceThreshold = (float)value;
                    OnPropertyChanged(nameof(ConfidenceThresholdPercent));
                }
            }
        }

        public string ConfidenceThresholdPercent => $"{Math.Round(ConfidenceThreshold * 100)}%";

        public string LastDetectionText
        {
            get => _lastDetectionText;
            private set => SetProperty(ref _lastDetectionText, value);
        }

        public WasteType LastDetectedCategory
        {
            get => _lastDetectedCategory;
            private set => SetProperty(ref _lastDetectedCategory, value);
        }

        #endregion

        #region Команды

        public RelayCommand ConnectCommand { get; }
        public RelayCommand DisconnectCommand { get; }
        public RelayCommand AutoDetectAndConnectCommand { get; }
        public RelayCommand RefreshPortsCommand { get; }
        public RelayCommand ToggleCameraCommand { get; }
        public RelayCommand SimulatePlasticCommand { get; }
        public RelayCommand SimulatePaperCommand { get; }
        public RelayCommand SimulateOrganicCommand { get; }
        public RelayCommand ClearLogsCommand { get; }
        public RelayCommand LoadModelCommand { get; }

        #endregion

        public MainViewModel(ISerialManager serialManager, ICameraService cameraService, IWasteDetectorService wasteDetector)
        {
            _serialManager = serialManager ?? throw new ArgumentNullException(nameof(serialManager));
            _cameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
            _wasteDetector = wasteDetector ?? throw new ArgumentNullException(nameof(wasteDetector));

            // Связываем детектор с сервисом захвата камеры
            _cameraService.Detector = _wasteDetector;
            _cameraService.ConfidenceThreshold = (float)_confidenceThreshold;

            // Инициализация команд управления
            ConnectCommand = new RelayCommand(ExecuteConnect, () => !IsConnected && !string.IsNullOrWhiteSpace(SelectedPort));
            DisconnectCommand = new RelayCommand(ExecuteDisconnect, () => IsConnected);
            AutoDetectAndConnectCommand = new RelayCommand(ExecuteAutoDetectAndConnect, () => !IsConnected);
            RefreshPortsCommand = new RelayCommand(ExecuteRefreshPorts);
            ToggleCameraCommand = new RelayCommand(ExecuteToggleCamera);

            SimulatePlasticCommand = new RelayCommand(async () => await SendWasteCommandAsync('P', "Пластик"), () => CanSendCommands);
            SimulatePaperCommand = new RelayCommand(async () => await SendWasteCommandAsync('B', "Бумага"), () => CanSendCommands);
            SimulateOrganicCommand = new RelayCommand(async () => await SendWasteCommandAsync('O', "Органика"), () => CanSendCommands);

            ClearLogsCommand = new RelayCommand(() => Logs.Clear());
            LoadModelCommand = new RelayCommand(ExecuteLoadModel);

            // Подписка на события SerialManager
            _serialManager.ConnectionChanged += OnSerialConnectionChanged;
            _serialManager.CommandSent += OnSerialCommandSent;
            _serialManager.DataReceived += OnSerialDataReceived;
            _serialManager.ErrorOccurred += OnSerialError;

            // Подписка на события CameraService
            _cameraService.StateChanged += OnCameraStateChanged;
            _cameraService.FrameReady += OnCameraFrameReady;
            _cameraService.DetectionsUpdated += OnCameraDetectionsUpdated;
            _cameraService.FpsUpdated += OnCameraFpsUpdated;
            _cameraService.ErrorOccurred += OnCameraError;

            // Инициализация устройств видеозахвата
            AvailableCameras.Add(new CameraDeviceInfo(0, "Камера #0 (Основная)"));
            AvailableCameras.Add(new CameraDeviceInfo(1, "Камера #1 (Внешняя USB)"));
            AvailableCameras.Add(new CameraDeviceInfo(2, "Камера #2"));
            AvailableCameras.Add(new CameraDeviceInfo(3, "Камера #3"));
            _selectedCamera = AvailableCameras.FirstOrDefault();

            // Первоначальное сканирование COM-портов
            ExecuteRefreshPorts();

            // Автоматический поиск best.onnx в каталоге приложения
            AutoDetectModelFile();

            AddLog("Приложение AI Smart Bin инициализировано.", LogLevel.Info);
        }

        public MainViewModel() : this(new SerialManager(), new CameraService(), new YoloWasteDetector())
        {
        }

        #region Логика нейросети и авто-сортировки

        private void AutoDetectModelFile()
        {
            AvailableModels.Clear();

            string classifierPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "waste_classifier.onnx");
            if (!File.Exists(classifierPath)) classifierPath = Path.Combine(Environment.CurrentDirectory, "models", "waste_classifier.onnx");

            string bestPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "best.onnx");
            if (!File.Exists(bestPath)) bestPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "best.onnx");
            if (!File.Exists(bestPath)) bestPath = Path.Combine(Environment.CurrentDirectory, "best.onnx");

            if (File.Exists(classifierPath))
            {
                AvailableModels.Add(new ModelFileInfo("♻️ WasteWise AI (Спец. мусор)", classifierPath, "Классификатор: пластик, бумага, органика, стекло, металл"));
            }

            if (File.Exists(bestPath))
            {
                AvailableModels.Add(new ModelFileInfo("🎯 YOLOv8 Det (Детектор с рамками)", bestPath, "Детекция COCO: бутылки, посуда, бананы, яблоки"));
            }

            if (AvailableModels.Count > 0)
            {
                SelectedModel = AvailableModels.First();
            }
            else
            {
                AddLog("Файлы моделей нейросети пока не найдены. Укажите файл через кнопку «Обзор...».", LogLevel.Warning);
            }
        }

        private void ExecuteLoadModel()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Выберите файл модели нейросети (ONNX)",
                Filter = "ONNX Model (*.onnx)|*.onnx|All files (*.*)|*.*",
                InitialDirectory = AppDomain.CurrentDomain.BaseDirectory
            };

            if (dialog.ShowDialog() == true)
            {
                var customModel = new ModelFileInfo(Path.GetFileName(dialog.FileName), dialog.FileName, "Пользовательская модель");
                AvailableModels.Add(customModel);
                SelectedModel = customModel;
            }
        }

        private void LoadModelFile(string filePath)
        {
            try
            {
                AddLog($"Загрузка модели нейросети: {Path.GetFileName(filePath)}...", LogLevel.Info);
                bool success = _wasteDetector.LoadModel(filePath);
                if (success)
                {
                    IsModelLoaded = true;
                    ModelName = Path.GetFileName(filePath);
                    AddLog($"Модель успешно инициализирована! Распознаваемые классы: {string.Join(", ", _wasteDetector.ClassNames)}", LogLevel.Success);
                    StatusMessage = $"Модель YOLOv8 загружена ({ModelName})";
                }
                else
                {
                    IsModelLoaded = false;
                    ModelName = "Сбой загрузки";
                    AddLog("Ошибка инициализации файла ONNX модели.", LogLevel.Error);
                }
            }
            catch (Exception ex)
            {
                IsModelLoaded = false;
                ModelName = "Ошибка";
                AddLog($"Исключение при загрузке модели: {ex.Message}", LogLevel.Error);
            }
        }

        private void OnCameraDetectionsUpdated(IReadOnlyList<DetectionResult> detections)
        {
            Dispatch(() =>
            {
                if (detections != null && detections.Count > 0)
                {
                    // Отбираем детекции, отдавая строгий приоритет известным типам мусора (не Unknown)
                    var wasteCandidates = detections.Where(d => d.Category != WasteType.Unknown).ToList();
                    var bestDetection = wasteCandidates.Count > 0
                        ? wasteCandidates.OrderByDescending(d => d.Confidence).First()
                        : detections.OrderByDescending(d => d.Confidence).First();

                    LastDetectionText = $"{bestDetection.CategoryNameRu} ({bestDetection.Confidence * 100:F0}%)";
                    LastDetectedCategory = bestDetection.Category;

                    // Логика авто-сортировки
                    if (IsAutoModeEnabled && CanSendCommands && bestDetection.ArduinoCommand.HasValue)
                    {
                        if (bestDetection.Confidence >= (float)ConfidenceThreshold)
                        {
                            // Предохранительный кулдаун (не менее 6 секунд между сработками, чтобы мусор успел упасть)
                            if ((DateTime.Now - _lastAutoTriggerTime).TotalSeconds >= 6.0)
                            {
                                _lastAutoTriggerTime = DateTime.Now;
                                AddLog($"[Авто-ИИ] Зафиксирован объект: {bestDetection.CategoryNameRu} (уверенность {bestDetection.Confidence:P0} >= {ConfidenceThresholdPercent})", LogLevel.Success);
                                _ = SendWasteCommandAsync(bestDetection.ArduinoCommand.Value, bestDetection.CategoryNameRu);
                            }
                        }
                    }
                }
                else
                {
                    // Если объект убран, через 1.5 секунды сбрасываем статус
                    if ((DateTime.Now - _lastAutoTriggerTime).TotalSeconds >= 1.5)
                    {
                        LastDetectionText = "Ожидание объекта перед камерой...";
                        LastDetectedCategory = WasteType.Unknown;
                    }
                }
            });
        }

        #endregion

        #region Serial Port Logic

        private void ExecuteRefreshPorts()
        {
            string? currentPort = SelectedPort;
            AvailablePorts.Clear();

            var devices = _serialManager.GetAvailableDevices();
            foreach (var dev in devices)
            {
                AvailablePorts.Add(dev);
            }

            var arduino = AvailablePorts.FirstOrDefault(d => d.IsArduino);
            if (arduino != null)
            {
                SelectedPortDevice = arduino;
                AddLog($"[Авто-обнаружение] Найдена плата: {arduino.Description} ({arduino.PortName})", LogLevel.Success);
            }
            else if (!string.IsNullOrEmpty(currentPort) && AvailablePorts.Any(p => p.PortName == currentPort))
            {
                SelectedPortDevice = AvailablePorts.First(p => p.PortName == currentPort);
            }
            else
            {
                SelectedPortDevice = AvailablePorts.FirstOrDefault();
                AddLog($"Обнаружено COM-портов: {AvailablePorts.Count}", LogLevel.Info);
            }

            RaiseCommandsCanExecuteChanged();
        }

        private void ExecuteAutoDetectAndConnect()
        {
            AddLog("Авто-поиск и подключение платы Arduino...", LogLevel.Info);
            ExecuteRefreshPorts();

            var arduino = AvailablePorts.FirstOrDefault(d => d.IsArduino);
            if (arduino != null)
            {
                SelectedPortDevice = arduino;
                AddLog($"[Авто-подключение] Подключение к {arduino.Description} ({arduino.PortName})...", LogLevel.Info);
                ExecuteConnect();
            }
            else if (SelectedPortDevice != null)
            {
                AddLog($"Arduino по имени не найден, подключаемся к выбранному порту {SelectedPortDevice.PortName}...", LogLevel.Warning);
                ExecuteConnect();
            }
            else
            {
                AddLog("Ошибка: COM-порты не найдены. Подключите Arduino кабелем USB.", LogLevel.Error);
            }
        }

        private void ExecuteConnect()
        {
            if (string.IsNullOrWhiteSpace(SelectedPort))
            {
                AddLog("Ошибка: COM-порт не выбран.", LogLevel.Warning);
                return;
            }

            AddLog($"Попытка подключения к {SelectedPort} на скорости 9600 бод...", LogLevel.Info);
            bool success = _serialManager.Open(SelectedPort, 9600);
            if (!success)
            {
                StatusMessage = $"Не удалось подключиться к порту {SelectedPort}";
                AddLog($"[Подсказка] Если порт {SelectedPort} заблокирован, закройте вкладку «Монитор порта» (Serial Monitor) в Arduino IDE.", LogLevel.Warning);
            }
        }

        private void ExecuteDisconnect()
        {
            _serialManager.Close();
            AddLog("Соединение с COM-портом разорвано пользователем.", LogLevel.Info);
            StatusMessage = "COM-порт отключен";
        }

        private async Task SendWasteCommandAsync(char symbol, string wasteName)
        {
            if (!CanSendCommands) return;

            IsArduinoBusy = true;

            if (IsConnected)
            {
                StatusMessage = $"Отправка команды для: {wasteName} ('{symbol}')...";
                AddLog($"[Команда] Запуск цикла сортировки: {wasteName} ('{symbol}')", LogLevel.Info);

                bool sent = await _serialManager.SendCommandAsync(symbol);
                if (sent)
                {
                    StatusMessage = $"Arduino открывает отсек '{wasteName}' (цикл ~5 сек)...";
                    AddLog($"Команда '{symbol}' передана в контроллер. Ожидание завершения вращения мотора...", LogLevel.Success);

                    // Запускаем предохранительный таймаут на случай, если ответ 'K' не вернется
                    StartLidSafetyTimeout();
                }
                else
                {
                    IsArduinoBusy = false;
                    StatusMessage = "Сбой отправки команды в Arduino.";
                }
            }
            else if (IsDemoModeEnabled)
            {
                StatusMessage = $"[Демо-эмулятор] Открытие отсека '{wasteName}' (цикл ~2.5 сек)...";
                AddLog($"[Демо-эмулятор] Эмуляция отправки байта '{symbol}' ({wasteName}). Сервопривод открывает люк...", LogLevel.Info);

                _ = Task.Run(async () =>
                {
                    await Task.Delay(2500);
                    Dispatch(() =>
                    {
                        if (IsArduinoBusy)
                        {
                            IsArduinoBusy = false;
                            StatusMessage = $"Готов к следующему объекту. Отсек '{wasteName}' закрыт.";
                            AddLog($"[Демо-эмулятор] Получен виртуальный сигнал 'K': цикл мотора завершен, люк закрыт!", LogLevel.Success);
                        }
                    });
                });
            }
        }

        private void StartLidSafetyTimeout()
        {
            _lidTimeoutCts?.Cancel();
            _lidTimeoutCts = new CancellationTokenSource();
            var token = _lidTimeoutCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    // Полный цикл Arduino (rotate 512 + delay 2000 + rotate 512) занимает ~5.1 сек
                    // Даем 10 секунд с запасом на возврат символа 'K'
                    await Task.Delay(10000, token);
                    if (!token.IsCancellationRequested && IsArduinoBusy)
                    {
                        Dispatch(() =>
                        {
                            if (IsArduinoBusy)
                            {
                                IsArduinoBusy = false;
                                StatusMessage = "Таймаут ответа от Arduino (контроллер разблокирован).";
                                AddLog("Превышено время ожидания ответа 'K' от Arduino. Статус готовности сброшен.", LogLevel.Warning);
                            }
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    // Ответ получен вовремя, таймаут штатно отменен
                }
            }, token);
        }

        private void OnSerialConnectionChanged(bool connected)
        {
            Dispatch(() =>
            {
                bool wasConnected = IsConnected;
                IsConnected = connected;
                if (connected)
                {
                    StatusMessage = $"Подключено к Arduino ({_serialManager.CurrentPortName})";
                    AddLog($"Успешное подключение к {_serialManager.CurrentPortName} (9600 бод).", LogLevel.Success);
                }
                else
                {
                    IsArduinoBusy = false;
                    _lidTimeoutCts?.Cancel();
                    StatusMessage = "Arduino отключен";
                    if (wasConnected)
                    {
                        AddLog("Порт закрыт.", LogLevel.Info);
                    }
                }
            });
        }

        private void OnSerialCommandSent(char cmd)
        {
            Dispatch(() =>
            {
                AddLog($"[TX -> Arduino] Отправлен байт: '{cmd}'", LogLevel.Info);
            });
        }

        private void OnSerialDataReceived(string data)
        {
            Dispatch(() =>
            {
                AddLog($"[RX <- Arduino] Получено: \"{data.Trim()}\"", LogLevel.Info);

                // Согласно скетчу sketch_oct13a.ino:
                // void openLid(int m) { ... Serial.println("K"); }
                if (data.Contains('K'))
                {
                    _lidTimeoutCts?.Cancel();
                    IsArduinoBusy = false;
                    StatusMessage = "Готов к следующему объекту. Крышка закрыта.";
                    AddLog("[Arduino] Получен сигнал 'K': цикл мотора завершен, крышка закрыта. Система готова к следующему предмету!", LogLevel.Success);
                }
            });
        }

        private void OnSerialError(string error)
        {
            Dispatch(() =>
            {
                AddLog($"[Ошибка порта] {error}", LogLevel.Error);
                StatusMessage = error;
            });
        }

        #endregion

        #region Camera Logic

        private void ExecuteToggleCamera()
        {
            if (IsCameraRunning)
            {
                _cameraService.Stop();
                AddLog("Веб-камера остановлена пользователем.", LogLevel.Info);
            }
            else
            {
                int camIndex = SelectedCamera?.Index ?? 0;
                string camName = SelectedCamera?.Name ?? $"Камера #{camIndex}";
                AddLog($"Инициализация захвата веб-камеры ({camName})...", LogLevel.Info);
                _cameraService.Start(camIndex);
            }
        }

        private void OnCameraStateChanged(bool running)
        {
            Dispatch(() =>
            {
                IsCameraRunning = running;
                if (!running)
                {
                    CurrentFrame = null;
                    CameraFps = 0;
                    StatusMessage = "Камера остановлена";
                    LastDetectionText = "Камера не запущена";
                }
                else
                {
                    StatusMessage = "Видеопоток с камеры запущен";
                    AddLog("Видеопоток камеры успешно запущен.", LogLevel.Success);
                }
            });
        }

        private void OnCameraFrameReady(BitmapSource frame)
        {
            Dispatch(() =>
            {
                CurrentFrame = frame;
            });
        }

        private void OnCameraFpsUpdated(double fps)
        {
            Dispatch(() =>
            {
                CameraFps = fps;
            });
        }

        private void OnCameraError(string error)
        {
            Dispatch(() =>
            {
                AddLog($"[Ошибка камеры] {error}", LogLevel.Error);
                StatusMessage = error;
            });
        }

        #endregion

        #region Helpers

        private void AddLog(string message, LogLevel level)
        {
            Dispatch(() =>
            {
                Logs.Insert(0, new LogMessage(message, level));
                while (Logs.Count > 250)
                {
                    Logs.RemoveAt(Logs.Count - 1);
                }
            });
        }

        private void RaiseCommandsCanExecuteChanged()
        {
            ConnectCommand.RaiseCanExecuteChanged();
            DisconnectCommand.RaiseCanExecuteChanged();
            AutoDetectAndConnectCommand.RaiseCanExecuteChanged();
            SimulatePlasticCommand.RaiseCanExecuteChanged();
            SimulatePaperCommand.RaiseCanExecuteChanged();
            SimulateOrganicCommand.RaiseCanExecuteChanged();
        }

        private void Dispatch(Action action)
        {
            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.BeginInvoke(action);
            }
            else
            {
                action();
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _lidTimeoutCts?.Cancel();
            _lidTimeoutCts?.Dispose();

            _cameraService.FrameReady -= OnCameraFrameReady;
            _cameraService.DetectionsUpdated -= OnCameraDetectionsUpdated;
            _cameraService.StateChanged -= OnCameraStateChanged;
            _cameraService.FpsUpdated -= OnCameraFpsUpdated;
            _cameraService.ErrorOccurred -= OnCameraError;
            _cameraService.Dispose();

            _wasteDetector.Dispose();

            _serialManager.ConnectionChanged -= OnSerialConnectionChanged;
            _serialManager.CommandSent -= OnSerialCommandSent;
            _serialManager.DataReceived -= OnSerialDataReceived;
            _serialManager.ErrorOccurred -= OnSerialError;
            _serialManager.Dispose();

            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
