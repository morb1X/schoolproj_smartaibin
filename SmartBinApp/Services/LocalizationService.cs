using System;
using System.Collections.Generic;
using SmartBinApp.Models;

namespace SmartBinApp.Services
{
    public enum AppLanguage
    {
        English,
        German
    }

    /// <summary>
    /// Centralized localization service supporting English and German.
    /// Provides dynamic real-time language switching without application restart.
    /// </summary>
    public class LocalizationService
    {
        private static readonly Lazy<LocalizationService> _instance = new(() => new LocalizationService());
        public static LocalizationService Instance => _instance.Value;

        private AppLanguage _currentLanguage = AppLanguage.English;

        public AppLanguage CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                if (_currentLanguage != value)
                {
                    _currentLanguage = value;
                    LanguageChanged?.Invoke();
                }
            }
        }

        public event Action? LanguageChanged;

        private readonly Dictionary<string, string> _en = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _de = new(StringComparer.OrdinalIgnoreCase);

        private LocalizationService()
        {
            RegisterEnglishStrings();
            RegisterGermanStrings();
        }

        public string Get(string key)
        {
            var dict = _currentLanguage == AppLanguage.German ? _de : _en;
            if (dict.TryGetValue(key, out var val))
            {
                return val;
            }
            if (_en.TryGetValue(key, out var enVal))
            {
                return enVal;
            }
            return key;
        }

        public string GetWasteName(WasteType type)
        {
            return type switch
            {
                WasteType.Plastic => _currentLanguage == AppLanguage.German ? "Plastik / Wertstoffe" : "Plastic / Recyclables",
                WasteType.Paper => _currentLanguage == AppLanguage.German ? "Papier & Karton" : "Paper & Cardboard",
                WasteType.Organic => _currentLanguage == AppLanguage.German ? "Bioabfall / Organisch" : "Organic / Bio-waste",
                _ => _currentLanguage == AppLanguage.German ? "Unbekannt" : "Unknown"
            };
        }

        public string GetWasteShortName(WasteType type)
        {
            return type switch
            {
                WasteType.Plastic => _currentLanguage == AppLanguage.German ? "Plastik" : "Plastic",
                WasteType.Paper => _currentLanguage == AppLanguage.German ? "Papier" : "Paper",
                WasteType.Organic => _currentLanguage == AppLanguage.German ? "Bioabfall" : "Organic",
                _ => _currentLanguage == AppLanguage.German ? "Unbekannt" : "Unknown"
            };
        }

        private void RegisterEnglishStrings()
        {
            _en["WindowTitle"] = "AI Smart Bin — Waste Sorting Control Panel";
            _en["HeaderTitle"] = "AI SMART BIN • WASTE SORTING SYSTEM";
            _en["HeaderSubtitle"] = "Autonomous recognition and intelligent waste sorting station";

            _en["StatusArduinoOnline"] = "Arduino: Online";
            _en["StatusArduinoDemo"] = "Arduino: Demo (Emulator)";
            _en["StatusArduinoOffline"] = "Arduino: Offline";

            _en["CameraStatusOff"] = "Camera: OFF";
            _en["CameraStatusLive"] = "Camera: LIVE";

            _en["ModelStatusLoaded"] = "✓ {0}";
            _en["ModelStatusNone"] = "✗ Model not loaded";

            _en["VideoMonitorTitle"] = "📹 Video Recognition Monitor";
            _en["CameraPlaceholderTitle"] = "Camera Feed Inactive";
            _en["CameraPlaceholderDesc"] = "Click \"Start Camera\" for real-time waste analysis";
            _en["CameraStart"] = "Start Camera";
            _en["CameraStop"] = "Stop Camera";
            _en["CameraTooltip"] = "Select video capture device";

            _en["SerialCardTitle"] = "🔌 Arduino Connection (Serial UART)";
            _en["SerialComboTooltip"] = "Select detected Arduino device or COM port";
            _en["RefreshPortsTooltip"] = "Rescan COM ports";
            _en["AutoConnectBtn"] = "⚡ Auto-Detect & Connect Arduino";
            _en["AutoConnectTooltip"] = "Automatically finds the Arduino board port and establishes connection";
            _en["ConnectBtn"] = "Connect";
            _en["DisconnectBtn"] = "Disconnect";
            _en["DemoModeCheckbox"] = "🧪 Demo Mode (Arduino Emulator)";
            _en["DemoModeTooltip"] = "Allows testing automatic sorting and lid cycles without physical hardware attached";

            _en["AiModelCardTitle"] = "🤖 Neural Network Model (ONNX)";
            _en["ModelComboTooltip"] = "Select active neural network for waste classification";
            _en["BrowseModelBtn"] = "📁 Browse...";
            _en["BrowseModelTooltip"] = "Load custom .onnx model from disk";
            _en["AutoSortCheckbox"] = "⚡ Automatic Sorting";
            _en["ConfidenceThresholdLabel"] = "Threshold: {0}";

            _en["CurrentDetectionLabel"] = "Current Detection:";
            _en["WaitingForObject"] = "Waiting for object in camera view...";

            _en["ManualTestingCardTitle"] = "🎯 Manual Compartment Testing";
            _en["ServoBusyText"] = "⚙ Servo rotating lid (~5 sec)...";
            _en["BtnPlastic"] = "Plastic 'P'";
            _en["BtnPaper"] = "Paper 'B'";
            _en["BtnOrganic"] = "Organic 'O'";

            _en["CalibrationTitle"] = "📐 Motor Calibration & Zero Position";
            _en["CalibrationDesc"] = "Select a motor, adjust left/right, and save as Home Zero.";
            _en["CalibrationMotorAll"] = "All";
            _en["CalibrationMotorPlastic"] = "Plastic (M1)";
            _en["CalibrationMotorPaper"] = "Paper (M2)";
            _en["CalibrationMotorOrganic"] = "Organic (M3)";
            _en["BtnNudgeLeft"] = "◀ Step Left";
            _en["BtnNudgeRight"] = "Step Right ▶";
            _en["BtnSetZero"] = "🎯 Set Zero / Home";

            _en["EventLogCardTitle"] = "📋 Event Log & Arduino Telemetry";
            _en["ClearLogsBtn"] = "Clear";

            _en["StatusLabel"] = "Status: ";
            _en["ReadyStatus"] = "Ready. Connect Arduino (or enable Demo mode) and start camera.";
            _en["FooterText"] = "AI Smart Bin • YOLOv8 ONNX • OpenCV & Serial UART";

            // Logs
            _en["LogNudgeLeft"] = "[Calibration] Stepping motor left ('<')...";
            _en["LogNudgeRight"] = "[Calibration] Stepping motor right ('>')...";
            _en["LogSetZero"] = "[Calibration] Setting current position as Home/Zero ('Z')...";
            _en["LogZeroSet"] = "[Calibration] Home position saved! Cycles will now start from this position.";
            _en["StatusZeroSet"] = "Calibrated Home Position set ('Z')";
            _en["LogAppInitialized"] = "AI Smart Bin application initialized.";
            _en["LogDemoEnabled"] = "[Demo Mode] Arduino Emulator ENABLED. You can test sorting without physical hardware.";
            _en["LogDemoDisabled"] = "[Demo Mode] Arduino Emulator DISABLED.";
            _en["LogCameraSwitched"] = "Switched to camera: {0}";
            _en["LogCameraStarted"] = "Camera video feed successfully started.";
            _en["LogCameraStopped"] = "Camera stopped by user.";
            _en["LogCameraError"] = "[Camera Error] {0}";
            _en["LogModelLoading"] = "Loading neural network model: {0}...";
            _en["LogModelLoaded"] = "Model successfully initialized! Classes: {0}";
            _en["LogModelLoadFailed"] = "Failed to initialize ONNX model file.";
            _en["LogAutoSortEnabled"] = "[AI Mode] Automatic sorting ENABLED.";
            _en["LogAutoSortDisabled"] = "[AI Mode] Automatic sorting DISABLED (manual mode).";
            _en["LogObjectDetected"] = "[Auto AI] Object detected: {0} (confidence {1:P0} >= {2})";
            _en["LogPortsFound"] = "Detected COM ports: {0}";
            _en["LogBoardFound"] = "[Auto-Detect] Found board: {0} ({1})";
            _en["LogAutoSearching"] = "Auto-searching and connecting to Arduino...";
            _en["LogAutoConnecting"] = "[Auto-Connect] Connecting to {0} ({1})...";
            _en["LogNoArduinoByName"] = "Arduino not recognized by name, connecting to selected port {0}...";
            _en["LogNoPorts"] = "Error: No COM ports found. Connect Arduino via USB cable.";
            _en["LogNoPortSelected"] = "Error: No COM port selected.";
            _en["LogConnectingTo"] = "Connecting to {0} at 9600 baud...";
            _en["LogPortLockedHint"] = "[Tip] If port {0} is busy, close the Serial Monitor in Arduino IDE.";
            _en["LogDisconnectedByUser"] = "COM port connection closed by user.";
            _en["LogPortClosed"] = "Port closed.";
            _en["LogSendingWasteCmd"] = "[Command] Initiating sorting cycle: {0} ('{1}')";
            _en["LogCmdTransmitted"] = "Command '{0}' sent to controller. Waiting for motor cycle completion...";
            _en["LogDemoSimulation"] = "[Demo Emulator] Simulating byte '{0}' ({1}). Servo opening compartment...";
            _en["LogDemoSignalK"] = "[Demo Emulator] Virtual signal 'K' received: motor cycle complete, lid closed!";
            _en["LogTimeoutWarning"] = "Timeout waiting for 'K' response from Arduino. Ready status reset.";
            _en["LogConnectedSuccess"] = "Successfully connected to {0} (9600 baud).";
            _en["LogByteSent"] = "[TX -> Arduino] Byte sent: '{0}'";
            _en["LogDataReceived"] = "[RX <- Arduino] Received: \"{0}\"";
            _en["LogSignalK"] = "[Arduino] Received 'K' signal: motor cycle completed, lid closed. System ready!";
            _en["LogPortError"] = "[Port Error] {0}";
            _en["LogNoModelsFound"] = "No neural network model files found. Select file via 'Browse...' button.";
            _en["CustomModelLabel"] = "Custom model";
            _en["ModelDetDescription"] = "YOLOv8 Detection (bounding boxes)";
            _en["ModelClsDescription"] = "Waste Classifier (plastic, paper, organic)";
        }

        private void RegisterGermanStrings()
        {
            _de["WindowTitle"] = "AI Smart Bin — Abfallsortierung Kontrollpanel";
            _de["HeaderTitle"] = "AI SMART BIN • INTELLIGENTE ABFALLSORTIERUNG";
            _de["HeaderSubtitle"] = "Autonome Erkennungs- und Abfalltrennstation mit künstlicher Intelligenz";

            _de["StatusArduinoOnline"] = "Arduino: Online";
            _de["StatusArduinoDemo"] = "Arduino: Demo (Emulator)";
            _de["StatusArduinoOffline"] = "Arduino: Offline";

            _de["CameraStatusOff"] = "Kamera: AUS";
            _de["CameraStatusLive"] = "Kamera: LIVE";

            _de["ModelStatusLoaded"] = "✓ {0}";
            _de["ModelStatusNone"] = "✗ Kein Modell geladen";

            _de["VideoMonitorTitle"] = "📹 Video-Erkennungsmonitor";
            _de["CameraPlaceholderTitle"] = "Videostream inaktiv";
            _de["CameraPlaceholderDesc"] = "Klicken Sie auf \"Kamera starten\" für Echtzeit-Abfallanalyse";
            _de["CameraStart"] = "Kamera starten";
            _de["CameraStop"] = "Kamera stoppen";
            _de["CameraTooltip"] = "Videoaufnahmegerät auswählen";

            _de["SerialCardTitle"] = "🔌 Arduino-Verbindung (Serial UART)";
            _de["SerialComboTooltip"] = "Erkanntes Arduino-Gerät oder COM-Port auswählen";
            _de["RefreshPortsTooltip"] = "COM-Ports erneut scannen";
            _de["AutoConnectBtn"] = "⚡ Arduino automatisch suchen & verbinden";
            _de["AutoConnectTooltip"] = "Findet den Arduino-Port automatisch und stellt die Verbindung her";
            _de["ConnectBtn"] = "Verbinden";
            _de["DisconnectBtn"] = "Trennen";
            _de["DemoModeCheckbox"] = "🧪 Demo-Modus (Arduino-Emulator)";
            _de["DemoModeTooltip"] = "Ermöglicht das Testen der automatischen Sortierung ohne physische Hardware";

            _de["AiModelCardTitle"] = "🤖 KI-Modell (ONNX)";
            _de["ModelComboTooltip"] = "Aktives neuronales Netz für Abfallerkennung auswählen";
            _de["BrowseModelBtn"] = "📁 Durchsuchen...";
            _de["BrowseModelTooltip"] = "Eigenes .onnx-Modell von Festplatte laden";
            _de["AutoSortCheckbox"] = "⚡ Automatische Sortierung";
            _de["ConfidenceThresholdLabel"] = "Schwellenwert: {0}";

            _de["CurrentDetectionLabel"] = "Aktuelle Erkennung:";
            _de["WaitingForObject"] = "Warte auf Objekt vor der Kamera...";

            _de["ManualTestingCardTitle"] = "🎯 Manuelle Fachtests";
            _de["ServoBusyText"] = "⚙ Servomotor dreht Klappe (~5 Sek.)...";
            _de["BtnPlastic"] = "Plastik 'P'";
            _de["BtnPaper"] = "Papier 'B'";
            _de["BtnOrganic"] = "Bioabfall 'O'";

            _de["CalibrationTitle"] = "📐 Motorkalibrierung (Nullposition)";
            _de["CalibrationDesc"] = "Motor wählen, links/rechts ausrichten und als Nullpunkt speichern.";
            _de["CalibrationMotorAll"] = "Alle";
            _de["CalibrationMotorPlastic"] = "Plastik (M1)";
            _de["CalibrationMotorPaper"] = "Papier (M2)";
            _de["CalibrationMotorOrganic"] = "Bioabfall (M3)";
            _de["BtnNudgeLeft"] = "◀ Schritt Links";
            _de["BtnNudgeRight"] = "Schritt Rechts ▶";
            _de["BtnSetZero"] = "🎯 Nullpunkt setzen";

            _de["EventLogCardTitle"] = "📋 Ereignisprotokoll & Arduino-Telemetrie";
            _de["ClearLogsBtn"] = "Löschen";

            _de["StatusLabel"] = "Status: ";
            _de["ReadyStatus"] = "Bereit. Verbinden Sie Arduino (oder Demo-Modus) und starten Sie die Kamera.";
            _de["FooterText"] = "AI Smart Bin • YOLOv8 ONNX • OpenCV & Serial UART";

            // Logs
            _de["LogNudgeLeft"] = "[Kalibrierung] Motor nach links bewegen ('<')...";
            _de["LogNudgeRight"] = "[Kalibrierung] Motor nach rechts bewegen ('>')...";
            _de["LogSetZero"] = "[Kalibrierung] Speichere aktuelle Position als Nullpunkt ('Z')...";
            _de["LogZeroSet"] = "[Kalibrierung] Nullpunkt gespeichert! Zyklen starten ab jetzt hier.";
            _de["StatusZeroSet"] = "Kalibrierter Nullpunkt gesetzt ('Z')";
            _de["LogAppInitialized"] = "AI Smart Bin Anwendung initialisiert.";
            _de["LogDemoEnabled"] = "[Demo-Modus] Arduino-Emulator AKTIVIERT. Sortierung kann ohne Hardware getestet werden.";
            _de["LogDemoDisabled"] = "[Demo-Modus] Arduino-Emulator DEAKTIVIERT.";
            _de["LogCameraSwitched"] = "Kamera gewechselt zu: {0}";
            _de["LogCameraStarted"] = "Kamera-Videostream erfolgreich gestartet.";
            _de["LogCameraStopped"] = "Kamera vom Benutzer gestoppt.";
            _de["LogCameraError"] = "[Kamerafehler] {0}";
            _de["LogModelLoading"] = "Lade KI-Modell: {0}...";
            _de["LogModelLoaded"] = "Modell erfolgreich initialisiert! Klassen: {0}";
            _de["LogModelLoadFailed"] = "Fehler beim Initialisieren der ONNX-Modelldatei.";
            _de["LogAutoSortEnabled"] = "[KI-Modus] Automatische Sortierung AKTIVIERT.";
            _de["LogAutoSortDisabled"] = "[KI-Modus] Automatische Sortierung DEAKTIVIERT (manueller Modus).";
            _de["LogObjectDetected"] = "[Auto-KI] Objekt erkannt: {0} (Konfidenz {1:P0} >= {2})";
            _de["LogPortsFound"] = "Erkannte COM-Ports: {0}";
            _de["LogBoardFound"] = "[Auto-Erkennung] Board gefunden: {0} ({1})";
            _de["LogAutoSearching"] = "Automatische Suche und Verbindung zu Arduino...";
            _de["LogAutoConnecting"] = "[Auto-Verbindung] Verbinde mit {0} ({1})...";
            _de["LogNoArduinoByName"] = "Arduino nicht namentlich erkannt, verbinde mit ausgewähltem Port {0}...";
            _de["LogNoPorts"] = "Fehler: Keine COM-Ports gefunden. Schließen Sie Arduino per USB an.";
            _de["LogNoPortSelected"] = "Fehler: Kein COM-Port ausgewählt.";
            _de["LogConnectingTo"] = "Verbinde mit {0} bei 9600 Baud...";
            _de["LogPortLockedHint"] = "[Hinweis] Falls Port {0} belegt ist, schließen Sie den seriellen Monitor in der Arduino IDE.";
            _de["LogDisconnectedByUser"] = "COM-Port-Verbindung vom Benutzer getrennt.";
            _de["LogPortClosed"] = "Port geschlossen.";
            _de["LogSendingWasteCmd"] = "[Befehl] Starte Sortierzyklus: {0} ('{1}')";
            _de["LogCmdTransmitted"] = "Befehl '{0}' an Controller gesendet. Warte auf Abschluss des Motorzyklus...";
            _de["LogDemoSimulation"] = "[Demo-Emulator] Simuliere Byte '{0}' ({1}). Servo öffnet Klappe...";
            _de["LogDemoSignalK"] = "[Demo-Emulator] Virtuelles Signal 'K' empfangen: Motorzyklus beendet, Klappe geschlossen!";
            _de["LogTimeoutWarning"] = "Zeitüberschreitung beim Warten auf 'K'-Antwort von Arduino. Status zurückgesetzt.";
            _de["LogConnectedSuccess"] = "Erfolgreich mit {0} verbunden (9600 Baud).";
            _de["LogByteSent"] = "[TX -> Arduino] Byte gesendet: '{0}'";
            _de["LogDataReceived"] = "[RX <- Arduino] Empfangen: \"{0}\"";
            _de["LogSignalK"] = "[Arduino] Signal 'K' empfangen: Motorzyklus abgeschlossen, Klappe geschlossen. System bereit!";
            _de["LogPortError"] = "[Portfehler] {0}";
            _de["LogNoModelsFound"] = "Keine KI-Modelle gefunden. Wählen Sie eine Datei über 'Durchsuchen...' aus.";
            _de["CustomModelLabel"] = "Benutzerdefiniertes Modell";
            _de["ModelDetDescription"] = "YOLOv8 Detektion (Begrenzungsrahmen)";
            _de["ModelClsDescription"] = "Abfall-Klassifikator (Plastik, Papier, Bioabfall)";
        }
    }
}
