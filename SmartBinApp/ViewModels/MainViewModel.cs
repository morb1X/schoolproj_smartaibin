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
    public class LanguageOption
    {
        public AppLanguage Language { get; set; }
        public string DisplayName { get; set; }

        public LanguageOption(AppLanguage language, string displayName)
        {
            Language = language;
            DisplayName = displayName;
        }

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// Main ViewModel for AI Smart Bin.
    /// Supports English and German localization with real-time UI switching.
    /// Connects webcam, YOLOv8 ONNX inference, and Arduino UART serial communications.
    /// </summary>
    public class MainViewModel : ViewModelBase, IDisposable
    {
        private readonly ISerialManager _serialManager;
        private readonly ICameraService _cameraService;
        private readonly IWasteDetectorService _wasteDetector;
        private readonly LocalizationService _loc = LocalizationService.Instance;

        private string? _selectedPort;
        private bool _isConnected;
        private bool _isDemoModeEnabled;
        private bool _isCameraRunning;
        private double _cameraFps;
        private BitmapSource? _currentFrame;
        private string _statusMessage = "";
        private bool _isArduinoBusy;
        private CancellationTokenSource? _lidTimeoutCts;
        private bool _isDisposed;
        private CameraDeviceInfo? _selectedCamera;

        // Neural network fields (YOLOv8)
        private bool _isModelLoaded;
        private string _modelName = "best.onnx";
        private ModelFileInfo? _selectedModel;
        private bool _isAutoModeEnabled = true;
        private double _confidenceThreshold = 0.40;
        private string _lastDetectionText = "";
        private WasteType _lastDetectedCategory = WasteType.Unknown;
        private DateTime _lastAutoTriggerTime = DateTime.MinValue;

        private SerialPortDevice? _selectedPortDevice;
        private LanguageOption _selectedLanguage;

        public ObservableCollection<SerialPortDevice> AvailablePorts { get; } = new();
        public ObservableCollection<CameraDeviceInfo> AvailableCameras { get; } = new();
        public ObservableCollection<ModelFileInfo> AvailableModels { get; } = new();
        public ObservableCollection<LogMessage> Logs { get; } = new();
        public ObservableCollection<LanguageOption> AvailableLanguages { get; } = new();

        #region Localization Properties

        public LanguageOption SelectedLanguage
        {
            get => _selectedLanguage;
            set
            {
                if (SetProperty(ref _selectedLanguage, value) && value != null)
                {
                    _loc.CurrentLanguage = value.Language;
                }
            }
        }

        public string WindowTitle => _loc.Get("WindowTitle");
        public string HeaderTitle => _loc.Get("HeaderTitle");
        public string HeaderSubtitle => _loc.Get("HeaderSubtitle");
        public string VideoMonitorTitle => _loc.Get("VideoMonitorTitle");
        public string CameraPlaceholderTitle => _loc.Get("CameraPlaceholderTitle");
        public string CameraPlaceholderDesc => _loc.Get("CameraPlaceholderDesc");
        public string CameraTooltip => _loc.Get("CameraTooltip");
        public string SerialCardTitle => _loc.Get("SerialCardTitle");
        public string SerialComboTooltip => _loc.Get("SerialComboTooltip");
        public string RefreshPortsTooltip => _loc.Get("RefreshPortsTooltip");
        public string AutoConnectBtn => _loc.Get("AutoConnectBtn");
        public string AutoConnectTooltip => _loc.Get("AutoConnectTooltip");
        public string ConnectBtn => _loc.Get("ConnectBtn");
        public string DisconnectBtn => _loc.Get("DisconnectBtn");
        public string DemoModeCheckbox => _loc.Get("DemoModeCheckbox");
        public string DemoModeTooltip => _loc.Get("DemoModeTooltip");
        public string AiModelCardTitle => _loc.Get("AiModelCardTitle");
        public string ModelComboTooltip => _loc.Get("ModelComboTooltip");
        public string BrowseModelBtn => _loc.Get("BrowseModelBtn");
        public string BrowseModelTooltip => _loc.Get("BrowseModelTooltip");
        public string AutoSortCheckbox => _loc.Get("AutoSortCheckbox");
        public string ConfidenceThresholdLabel => string.Format(_loc.Get("ConfidenceThresholdLabel"), ConfidenceThresholdPercent);
        public string CurrentDetectionLabel => _loc.Get("CurrentDetectionLabel");
        public string ManualTestingCardTitle => _loc.Get("ManualTestingCardTitle");
        public string ServoBusyText => _loc.Get("ServoBusyText");
        public string BtnPlastic => _loc.Get("BtnPlastic");
        public string BtnPaper => _loc.Get("BtnPaper");
        public string BtnOrganic => _loc.Get("BtnOrganic");
        public string CalibrationTitle => _loc.Get("CalibrationTitle");
        public string CalibrationDesc => _loc.Get("CalibrationDesc");
        public string CalibrationMotorAll => _loc.Get("CalibrationMotorAll");
        public string CalibrationMotorPlastic => _loc.Get("CalibrationMotorPlastic");
        public string CalibrationMotorPaper => _loc.Get("CalibrationMotorPaper");
        public string CalibrationMotorOrganic => _loc.Get("CalibrationMotorOrganic");

        private int _selectedCalibrationMotor = 0; // 0 = All, 1 = Plastic, 2 = Paper, 3 = Organic
        public int SelectedCalibrationMotor
        {
            get => _selectedCalibrationMotor;
            set
            {
                if (SetProperty(ref _selectedCalibrationMotor, value))
                {
                    OnPropertyChanged(nameof(IsMotorAllSelected));
                    OnPropertyChanged(nameof(IsMotorPlasticSelected));
                    OnPropertyChanged(nameof(IsMotorPaperSelected));
                    OnPropertyChanged(nameof(IsMotorOrganicSelected));
                    OnPropertyChanged(nameof(SelectedMotorName));
                }
            }
        }

        public bool IsMotorAllSelected => _selectedCalibrationMotor == 0;
        public bool IsMotorPlasticSelected => _selectedCalibrationMotor == 1;
        public bool IsMotorPaperSelected => _selectedCalibrationMotor == 2;
        public bool IsMotorOrganicSelected => _selectedCalibrationMotor == 3;

        public string SelectedMotorName => _selectedCalibrationMotor switch
        {
            1 => CalibrationMotorPlastic,
            2 => CalibrationMotorPaper,
            3 => CalibrationMotorOrganic,
            _ => CalibrationMotorAll
        };

        public string BtnNudgeLeft => _loc.Get("BtnNudgeLeft");
        public string BtnNudgeRight => _loc.Get("BtnNudgeRight");
        public string BtnSetZero => _loc.Get("BtnSetZero");
        public string EventLogCardTitle => _loc.Get("EventLogCardTitle");
        public string ClearLogsBtn => _loc.Get("ClearLogsBtn");
        public string StatusLabel => _loc.Get("StatusLabel");
        public string FooterText => _loc.Get("FooterText");

        private void OnLanguageChanged()
        {
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(HeaderTitle));
            OnPropertyChanged(nameof(HeaderSubtitle));
            OnPropertyChanged(nameof(VideoMonitorTitle));
            OnPropertyChanged(nameof(CameraPlaceholderTitle));
            OnPropertyChanged(nameof(CameraPlaceholderDesc));
            OnPropertyChanged(nameof(CameraTooltip));
            OnPropertyChanged(nameof(CameraToggleText));
            OnPropertyChanged(nameof(CameraStatusText));
            OnPropertyChanged(nameof(SerialCardTitle));
            OnPropertyChanged(nameof(SerialComboTooltip));
            OnPropertyChanged(nameof(RefreshPortsTooltip));
            OnPropertyChanged(nameof(AutoConnectBtn));
            OnPropertyChanged(nameof(AutoConnectTooltip));
            OnPropertyChanged(nameof(ConnectBtn));
            OnPropertyChanged(nameof(DisconnectBtn));
            OnPropertyChanged(nameof(DemoModeCheckbox));
            OnPropertyChanged(nameof(DemoModeTooltip));
            OnPropertyChanged(nameof(AiModelCardTitle));
            OnPropertyChanged(nameof(ModelComboTooltip));
            OnPropertyChanged(nameof(BrowseModelBtn));
            OnPropertyChanged(nameof(BrowseModelTooltip));
            OnPropertyChanged(nameof(AutoSortCheckbox));
            OnPropertyChanged(nameof(ConfidenceThresholdLabel));
            OnPropertyChanged(nameof(CurrentDetectionLabel));
            OnPropertyChanged(nameof(ManualTestingCardTitle));
            OnPropertyChanged(nameof(ServoBusyText));
            OnPropertyChanged(nameof(BtnPlastic));
            OnPropertyChanged(nameof(BtnPaper));
            OnPropertyChanged(nameof(BtnOrganic));
            OnPropertyChanged(nameof(CalibrationTitle));
            OnPropertyChanged(nameof(CalibrationDesc));
            OnPropertyChanged(nameof(CalibrationMotorAll));
            OnPropertyChanged(nameof(CalibrationMotorPlastic));
            OnPropertyChanged(nameof(CalibrationMotorPaper));
            OnPropertyChanged(nameof(CalibrationMotorOrganic));
            OnPropertyChanged(nameof(SelectedMotorName));
            OnPropertyChanged(nameof(BtnNudgeLeft));
            OnPropertyChanged(nameof(BtnNudgeRight));
            OnPropertyChanged(nameof(BtnSetZero));
            OnPropertyChanged(nameof(EventLogCardTitle));
            OnPropertyChanged(nameof(ClearLogsBtn));
            OnPropertyChanged(nameof(StatusLabel));
            OnPropertyChanged(nameof(FooterText));
            OnPropertyChanged(nameof(ArduinoStatusText));
            OnPropertyChanged(nameof(ModelStatusBadge));

            if (LastDetectedCategory == WasteType.Unknown)
            {
                LastDetectionText = _loc.Get("WaitingForObject");
            }
            else
            {
                LastDetectionText = _loc.GetWasteShortName(LastDetectedCategory);
            }

            if (string.IsNullOrEmpty(_statusMessage) || _statusMessage.Contains("Ready") || _statusMessage.Contains("Bereit"))
            {
                StatusMessage = _loc.Get("ReadyStatus");
            }
        }

        #endregion

        #region Connection and Camera Properties

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
                    AddLog(value ? _loc.Get("LogDemoEnabled") : _loc.Get("LogDemoDisabled"), LogLevel.Info);
                }
            }
        }

        public string ArduinoStatusText
        {
            get
            {
                if (IsConnected) return _loc.Get("StatusArduinoOnline");
                if (IsDemoModeEnabled) return _loc.Get("StatusArduinoDemo");
                return _loc.Get("StatusArduinoOffline");
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
                        AddLog(string.Format(_loc.Get("LogCameraSwitched"), value.Name), LogLevel.Info);
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
                    OnPropertyChanged(nameof(CameraStatusText));
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

        public string CameraToggleText => IsCameraRunning ? _loc.Get("CameraStop") : _loc.Get("CameraStart");
        public string CameraStatusText => IsCameraRunning ? _loc.Get("CameraStatusLive") : _loc.Get("CameraStatusOff");

        #endregion

        #region Neural Network (AI) Properties

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

        public string ModelStatusBadge => IsModelLoaded
            ? string.Format(_loc.Get("ModelStatusLoaded"), ModelName)
            : _loc.Get("ModelStatusNone");

        public bool IsAutoModeEnabled
        {
            get => _isAutoModeEnabled;
            set
            {
                if (SetProperty(ref _isAutoModeEnabled, value))
                {
                    AddLog(value ? _loc.Get("LogAutoSortEnabled") : _loc.Get("LogAutoSortDisabled"), LogLevel.Info);
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
                    OnPropertyChanged(nameof(ConfidenceThresholdLabel));
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

        #region Commands

        public RelayCommand ConnectCommand { get; }
        public RelayCommand DisconnectCommand { get; }
        public RelayCommand AutoDetectAndConnectCommand { get; }
        public RelayCommand RefreshPortsCommand { get; }
        public RelayCommand ToggleCameraCommand { get; }
        public RelayCommand SimulatePlasticCommand { get; }
        public RelayCommand SimulatePaperCommand { get; }
        public RelayCommand SimulateOrganicCommand { get; }
        public RelayCommand SelectMotorAllCommand { get; }
        public RelayCommand SelectMotorPlasticCommand { get; }
        public RelayCommand SelectMotorPaperCommand { get; }
        public RelayCommand SelectMotorOrganicCommand { get; }
        public RelayCommand NudgeLeftCommand { get; }
        public RelayCommand NudgeRightCommand { get; }
        public RelayCommand SetZeroCommand { get; }
        public RelayCommand ClearLogsCommand { get; }
        public RelayCommand LoadModelCommand { get; }

        #endregion

        public MainViewModel(ISerialManager serialManager, ICameraService cameraService, IWasteDetectorService wasteDetector)
        {
            _serialManager = serialManager ?? throw new ArgumentNullException(nameof(serialManager));
            _cameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
            _wasteDetector = wasteDetector ?? throw new ArgumentNullException(nameof(wasteDetector));

            _cameraService.Detector = _wasteDetector;
            _cameraService.ConfidenceThreshold = (float)_confidenceThreshold;

            // Language setup
            AvailableLanguages.Add(new LanguageOption(AppLanguage.English, "🇬🇧 English"));
            AvailableLanguages.Add(new LanguageOption(AppLanguage.German, "🇩🇪 Deutsch"));
            _selectedLanguage = AvailableLanguages.First();
            _loc.LanguageChanged += OnLanguageChanged;

            _lastDetectionText = _loc.Get("WaitingForObject");
            _statusMessage = _loc.Get("ReadyStatus");

            // Commands
            ConnectCommand = new RelayCommand(ExecuteConnect, () => !IsConnected && !string.IsNullOrWhiteSpace(SelectedPort));
            DisconnectCommand = new RelayCommand(ExecuteDisconnect, () => IsConnected);
            AutoDetectAndConnectCommand = new RelayCommand(ExecuteAutoDetectAndConnect, () => !IsConnected);
            RefreshPortsCommand = new RelayCommand(ExecuteRefreshPorts);
            ToggleCameraCommand = new RelayCommand(ExecuteToggleCamera);

            SimulatePlasticCommand = new RelayCommand(async () => await SendWasteCommandAsync('P', _loc.GetWasteShortName(WasteType.Plastic)), () => CanSendCommands);
            SimulatePaperCommand = new RelayCommand(async () => await SendWasteCommandAsync('B', _loc.GetWasteShortName(WasteType.Paper)), () => CanSendCommands);
            SimulateOrganicCommand = new RelayCommand(async () => await SendWasteCommandAsync('O', _loc.GetWasteShortName(WasteType.Organic)), () => CanSendCommands);

            SelectMotorAllCommand = new RelayCommand(async () => await ExecuteSelectMotorAsync(0), () => !IsArduinoBusy);
            SelectMotorPlasticCommand = new RelayCommand(async () => await ExecuteSelectMotorAsync(1), () => !IsArduinoBusy);
            SelectMotorPaperCommand = new RelayCommand(async () => await ExecuteSelectMotorAsync(2), () => !IsArduinoBusy);
            SelectMotorOrganicCommand = new RelayCommand(async () => await ExecuteSelectMotorAsync(3), () => !IsArduinoBusy);

            NudgeLeftCommand = new RelayCommand(async () => await ExecuteNudgeAsync('<'), () => CanSendCommands);
            NudgeRightCommand = new RelayCommand(async () => await ExecuteNudgeAsync('>'), () => CanSendCommands);
            SetZeroCommand = new RelayCommand(async () => await ExecuteSetZeroAsync(), () => CanSendCommands);

            ClearLogsCommand = new RelayCommand(() => Logs.Clear());
            LoadModelCommand = new RelayCommand(ExecuteLoadModel);

            // Serial Manager events
            _serialManager.ConnectionChanged += OnSerialConnectionChanged;
            _serialManager.CommandSent += OnSerialCommandSent;
            _serialManager.DataReceived += OnSerialDataReceived;
            _serialManager.ErrorOccurred += OnSerialError;

            // Camera events
            _cameraService.StateChanged += OnCameraStateChanged;
            _cameraService.FrameReady += OnCameraFrameReady;
            _cameraService.DetectionsUpdated += OnCameraDetectionsUpdated;
            _cameraService.FpsUpdated += OnCameraFpsUpdated;
            _cameraService.ErrorOccurred += OnCameraError;

            // Available cameras
            AvailableCameras.Add(new CameraDeviceInfo(0, "Camera #0 (Primary)"));
            AvailableCameras.Add(new CameraDeviceInfo(1, "Camera #1 (USB External)"));
            AvailableCameras.Add(new CameraDeviceInfo(2, "Camera #2"));
            AvailableCameras.Add(new CameraDeviceInfo(3, "Camera #3"));
            _selectedCamera = AvailableCameras.FirstOrDefault();

            ExecuteRefreshPorts();
            AutoDetectModelFile();

            AddLog(_loc.Get("LogAppInitialized"), LogLevel.Info);
        }

        public MainViewModel() : this(new SerialManager(), new CameraService(), new YoloWasteDetector())
        {
        }

        #region AI Logic & Auto-Sorting

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
                AvailableModels.Add(new ModelFileInfo("♻️ Waste Classifier (ONNX)", classifierPath, _loc.Get("ModelClsDescription")));
            }

            if (File.Exists(bestPath))
            {
                AvailableModels.Add(new ModelFileInfo("🎯 YOLOv8 Det (best.onnx)", bestPath, _loc.Get("ModelDetDescription")));
            }

            if (AvailableModels.Count > 0)
            {
                SelectedModel = AvailableModels.First();
            }
            else
            {
                AddLog(_loc.Get("LogNoModelsFound"), LogLevel.Warning);
            }
        }

        private void ExecuteLoadModel()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Neural Network Model (ONNX)",
                Filter = "ONNX Model (*.onnx)|*.onnx|All files (*.*)|*.*",
                InitialDirectory = AppDomain.CurrentDomain.BaseDirectory
            };

            if (dialog.ShowDialog() == true)
            {
                var customModel = new ModelFileInfo(Path.GetFileName(dialog.FileName), dialog.FileName, _loc.Get("CustomModelLabel"));
                AvailableModels.Add(customModel);
                SelectedModel = customModel;
            }
        }

        private void LoadModelFile(string filePath)
        {
            try
            {
                AddLog(string.Format(_loc.Get("LogModelLoading"), Path.GetFileName(filePath)), LogLevel.Info);
                bool success = _wasteDetector.LoadModel(filePath);
                if (success)
                {
                    IsModelLoaded = true;
                    ModelName = Path.GetFileName(filePath);
                    AddLog(string.Format(_loc.Get("LogModelLoaded"), string.Join(", ", _wasteDetector.ClassNames)), LogLevel.Success);
                    StatusMessage = $"YOLOv8 Model loaded ({ModelName})";
                }
                else
                {
                    IsModelLoaded = false;
                    ModelName = "Load error";
                    AddLog(_loc.Get("LogModelLoadFailed"), LogLevel.Error);
                }
            }
            catch (Exception ex)
            {
                IsModelLoaded = false;
                ModelName = "Error";
                AddLog($"Exception loading model: {ex.Message}", LogLevel.Error);
            }
        }

        private void OnCameraDetectionsUpdated(IReadOnlyList<DetectionResult> detections)
        {
            Dispatch(() =>
            {
                if (detections != null && detections.Count > 0)
                {
                    var wasteCandidates = detections.Where(d => d.Category != WasteType.Unknown).ToList();
                    var bestDetection = wasteCandidates.Count > 0
                        ? wasteCandidates.OrderByDescending(d => d.Confidence).First()
                        : detections.OrderByDescending(d => d.Confidence).First();

                    LastDetectionText = $"{bestDetection.CategoryShortName} ({bestDetection.Confidence * 100:F0}%)";
                    LastDetectedCategory = bestDetection.Category;

                    // Auto-sort logic
                    if (IsAutoModeEnabled && CanSendCommands && bestDetection.ArduinoCommand.HasValue)
                    {
                        if (bestDetection.Confidence >= (float)ConfidenceThreshold)
                        {
                            if ((DateTime.Now - _lastAutoTriggerTime).TotalSeconds >= 6.0)
                            {
                                _lastAutoTriggerTime = DateTime.Now;
                                AddLog(string.Format(_loc.Get("LogObjectDetected"), bestDetection.CategoryName, bestDetection.Confidence, ConfidenceThresholdPercent), LogLevel.Success);
                                _ = SendWasteCommandAsync(bestDetection.ArduinoCommand.Value, bestDetection.CategoryShortName);
                            }
                        }
                    }
                }
                else
                {
                    if ((DateTime.Now - _lastAutoTriggerTime).TotalSeconds >= 1.5)
                    {
                        LastDetectionText = _loc.Get("WaitingForObject");
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
                AddLog(string.Format(_loc.Get("LogBoardFound"), arduino.Description, arduino.PortName), LogLevel.Success);
            }
            else if (!string.IsNullOrEmpty(currentPort) && AvailablePorts.Any(p => p.PortName == currentPort))
            {
                SelectedPortDevice = AvailablePorts.First(p => p.PortName == currentPort);
            }
            else
            {
                SelectedPortDevice = AvailablePorts.FirstOrDefault();
                AddLog(string.Format(_loc.Get("LogPortsFound"), AvailablePorts.Count), LogLevel.Info);
            }

            RaiseCommandsCanExecuteChanged();
        }

        private void ExecuteAutoDetectAndConnect()
        {
            AddLog(_loc.Get("LogAutoSearching"), LogLevel.Info);
            ExecuteRefreshPorts();

            var arduino = AvailablePorts.FirstOrDefault(d => d.IsArduino);
            if (arduino != null)
            {
                SelectedPortDevice = arduino;
                AddLog(string.Format(_loc.Get("LogAutoConnecting"), arduino.Description, arduino.PortName), LogLevel.Info);
                ExecuteConnect();
            }
            else if (SelectedPortDevice != null)
            {
                AddLog(string.Format(_loc.Get("LogNoArduinoByName"), SelectedPortDevice.PortName), LogLevel.Warning);
                ExecuteConnect();
            }
            else
            {
                AddLog(_loc.Get("LogNoPorts"), LogLevel.Error);
            }
        }

        private void ExecuteConnect()
        {
            if (string.IsNullOrWhiteSpace(SelectedPort))
            {
                AddLog(_loc.Get("LogNoPortSelected"), LogLevel.Warning);
                return;
            }

            AddLog(string.Format(_loc.Get("LogConnectingTo"), SelectedPort), LogLevel.Info);
            bool success = _serialManager.Open(SelectedPort, 9600);
            if (!success)
            {
                StatusMessage = $"Connection failed to {SelectedPort}";
                AddLog(string.Format(_loc.Get("LogPortLockedHint"), SelectedPort), LogLevel.Warning);
            }
        }

        private void ExecuteDisconnect()
        {
            _serialManager.Close();
            AddLog(_loc.Get("LogDisconnectedByUser"), LogLevel.Info);
            StatusMessage = "COM port disconnected";
        }

        private async Task SendWasteCommandAsync(char symbol, string wasteName)
        {
            if (!CanSendCommands) return;

            IsArduinoBusy = true;

            if (IsConnected)
            {
                StatusMessage = $"Sending command: {wasteName} ('{symbol}')...";
                AddLog(string.Format(_loc.Get("LogSendingWasteCmd"), wasteName, symbol), LogLevel.Info);

                bool sent = await _serialManager.SendCommandAsync(symbol);
                if (sent)
                {
                    StatusMessage = $"Arduino opening '{wasteName}' compartment...";
                    AddLog(string.Format(_loc.Get("LogCmdTransmitted"), symbol), LogLevel.Success);
                    StartLidSafetyTimeout();
                }
                else
                {
                    IsArduinoBusy = false;
                    StatusMessage = "Failed to send command to Arduino.";
                }
            }
            else if (IsDemoModeEnabled)
            {
                StatusMessage = $"[Demo Emulator] Opening '{wasteName}' (~2.5 sec)...";
                AddLog(string.Format(_loc.Get("LogDemoSimulation"), symbol, wasteName), LogLevel.Info);

                _ = Task.Run(async () =>
                {
                    await Task.Delay(2500);
                    Dispatch(() =>
                    {
                        if (IsArduinoBusy)
                        {
                            IsArduinoBusy = false;
                            StatusMessage = $"Ready. Compartment '{wasteName}' closed.";
                            AddLog(_loc.Get("LogDemoSignalK"), LogLevel.Success);
                        }
                    });
                });
            }
        }

        private void StartLidSafetyTimeout(int timeoutMs = 10000)
        {
            _lidTimeoutCts?.Cancel();
            _lidTimeoutCts = new CancellationTokenSource();
            var token = _lidTimeoutCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(timeoutMs, token);
                    if (!token.IsCancellationRequested && IsArduinoBusy)
                    {
                        Dispatch(() =>
                        {
                            if (IsArduinoBusy)
                            {
                                IsArduinoBusy = false;
                                StatusMessage = "Arduino response timeout (reset).";
                                AddLog(_loc.Get("LogTimeoutWarning"), LogLevel.Warning);
                            }
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }, token);
        }

        private async Task ExecuteSelectMotorAsync(int motorId)
        {
            if (IsArduinoBusy) return;
            SelectedCalibrationMotor = motorId;
            string motorName = SelectedMotorName;
            AddLog($"[Calibration] Target motor selected: {motorName} ('{motorId}')", LogLevel.Info);

            if (IsConnected)
            {
                char cmd = (char)('0' + motorId);
                await _serialManager.SendCommandAsync(cmd);
                StatusMessage = $"Selected motor: {motorName}";
            }
            else if (IsDemoModeEnabled)
            {
                StatusMessage = $"[Demo] Selected motor: {motorName}";
            }
        }

        private async Task ExecuteNudgeAsync(char direction)
        {
            if (!CanSendCommands) return;
            IsArduinoBusy = true;

            string dirName = direction == '<' ? "left" : "right";
            AddLog($"[Calibration] {SelectedMotorName}: nudge {dirName} ('{direction}')...", LogLevel.Info);

            if (IsConnected)
            {
                bool sent = await _serialManager.SendCommandAsync(direction);
                if (sent)
                {
                    StatusMessage = $"{SelectedMotorName}: step {dirName} ('{direction}')...";
                    StartLidSafetyTimeout(3000);
                }
                else
                {
                    IsArduinoBusy = false;
                    StatusMessage = "Failed to send calibration command.";
                }
            }
            else if (IsDemoModeEnabled)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(300);
                    Dispatch(() =>
                    {
                        IsArduinoBusy = false;
                        StatusMessage = $"[Demo] {SelectedMotorName}: stepped {dirName} ('{direction}')";
                        AddLog($"[Demo] {SelectedMotorName}: nudged {dirName} ('{direction}')", LogLevel.Success);
                    });
                });
            }
        }

        private async Task ExecuteSetZeroAsync()
        {
            if (!CanSendCommands) return;
            IsArduinoBusy = true;

            AddLog($"[Calibration] {SelectedMotorName}: saving Zero / Home position ('Z')...", LogLevel.Info);

            if (IsConnected)
            {
                bool sent = await _serialManager.SendCommandAsync('Z');
                if (sent)
                {
                    StatusMessage = $"{SelectedMotorName}: Home Zero set ('Z')";
                    AddLog($"[Calibration] {SelectedMotorName}: Zero set and saved to EEPROM!", LogLevel.Success);
                    StartLidSafetyTimeout(3000);
                }
                else
                {
                    IsArduinoBusy = false;
                    StatusMessage = "Failed to send zero command.";
                }
            }
            else if (IsDemoModeEnabled)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(400);
                    Dispatch(() =>
                    {
                        IsArduinoBusy = false;
                        StatusMessage = $"{SelectedMotorName}: Home Zero set ('Z')";
                        AddLog($"[Demo] {SelectedMotorName}: Zero set and saved!", LogLevel.Success);
                    });
                });
            }
        }

        private void OnSerialConnectionChanged(bool connected)
        {
            Dispatch(() =>
            {
                bool wasConnected = IsConnected;
                IsConnected = connected;
                if (connected)
                {
                    StatusMessage = $"Connected to Arduino ({_serialManager.CurrentPortName})";
                    AddLog(string.Format(_loc.Get("LogConnectedSuccess"), _serialManager.CurrentPortName), LogLevel.Success);
                }
                else
                {
                    IsArduinoBusy = false;
                    _lidTimeoutCts?.Cancel();
                    StatusMessage = "Arduino disconnected";
                    if (wasConnected)
                    {
                        AddLog(_loc.Get("LogPortClosed"), LogLevel.Info);
                    }
                }
            });
        }

        private void OnSerialCommandSent(char cmd)
        {
            Dispatch(() =>
            {
                AddLog(string.Format(_loc.Get("LogByteSent"), cmd), LogLevel.Info);
            });
        }

        private void OnSerialDataReceived(string data)
        {
            Dispatch(() =>
            {
                AddLog(string.Format(_loc.Get("LogDataReceived"), data.Trim()), LogLevel.Info);

                if (data.Contains('K'))
                {
                    _lidTimeoutCts?.Cancel();
                    IsArduinoBusy = false;
                    StatusMessage = "Ready for next object. Lid closed.";
                    AddLog(_loc.Get("LogSignalK"), LogLevel.Success);
                }
            });
        }

        private void OnSerialError(string error)
        {
            Dispatch(() =>
            {
                AddLog(string.Format(_loc.Get("LogPortError"), error), LogLevel.Error);
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
                AddLog(_loc.Get("LogCameraStopped"), LogLevel.Info);
            }
            else
            {
                int camIndex = SelectedCamera?.Index ?? 0;
                string camName = SelectedCamera?.Name ?? $"Camera #{camIndex}";
                AddLog($"Initializing webcam ({camName})...", LogLevel.Info);
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
                    StatusMessage = "Camera stopped";
                    LastDetectionText = _loc.Get("WaitingForObject");
                }
                else
                {
                    StatusMessage = "Camera stream active";
                    AddLog(_loc.Get("LogCameraStarted"), LogLevel.Success);
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
                AddLog(string.Format(_loc.Get("LogCameraError"), error), LogLevel.Error);
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
            SelectMotorAllCommand.RaiseCanExecuteChanged();
            SelectMotorPlasticCommand.RaiseCanExecuteChanged();
            SelectMotorPaperCommand.RaiseCanExecuteChanged();
            SelectMotorOrganicCommand.RaiseCanExecuteChanged();
            NudgeLeftCommand.RaiseCanExecuteChanged();
            NudgeRightCommand.RaiseCanExecuteChanged();
            SetZeroCommand.RaiseCanExecuteChanged();
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

            _loc.LanguageChanged -= OnLanguageChanged;

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
