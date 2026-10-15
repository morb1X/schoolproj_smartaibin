# ♻️ AI Smart Bin — Project Architecture & Developer Guide

This document serves as the master technical specification, protocol definition, and development manual for **AI Smart Bin** (Intelligent School Waste Sorting Station with AI).

---

## 📑 Table of Contents
1. [Overview & Project Goals](#1-overview--project-goals)
2. [Architecture & Technology Stack](#2-architecture--technology-stack)
3. [Repository Structure](#3-repository-structure)
4. [Hardware Communication Protocol (UART 9600)](#4-hardware-communication-protocol-uart-9600)
5. [Building and Running the WPF Operator Client](#5-building-and-running-the-wpf-operator-client)
6. [Training YOLOv8 in Google Colab](#6-training-yolov8-in-google-colab)
7. [Flashing Arduino Microcontroller](#7-flashing-arduino-microcontroller)
8. [Operating Modes: Physical Hardware & Demo Emulator](#8-operating-modes-physical-hardware--demo-emulator)

---

## 1. Overview & Project Goals
**AI Smart Bin** is an intelligent hardware-software system designed for autonomous waste sorting and educational demonstrations.

### Core Workflow:
1. A user presents waste (plastic bottle, paper/box, food waste/peel) in front of the camera.
2. The **YOLOv8** neural network processes the live video stream in real-time, detecting the item, its bounding box, class, and confidence score.
3. When confidence exceeds the configured threshold (default 40–65%) and cooldown period (6 seconds), the app sends a command byte via Serial UART to **Arduino**.
4. The microcontroller activates the corresponding lid servo or stepper motor, holds the compartment open for 2.5–3 seconds, closes it, and responds with `'K'` to confirm readiness.

---

## 2. Architecture & Technology Stack

| Component | Stack | Purpose |
|---|---|---|
| **Operator GUI** | C# .NET 8, WPF, XAML, MVVM | Modern dark-themed GUI with live video, telemetry, status badges, and event logs |
| **Computer Vision** | `OpenCvSharp4`, `OpenCvSharp4.WpfExtensions` | Non-blocking background camera capture and HUD/Bounding Box overlay |
| **Neural Network Inference** | `Microsoft.ML.OnnxRuntime` | High-speed YOLOv8 ONNX model execution with multi-threading |
| **Model Training** | Python, Ultralytics YOLOv8, PyTorch | Cloud GPU training pipeline in Google Colab |
| **Microcontroller Communication** | `System.IO.Ports` (SerialPort) | Full-duplex asynchronous UART communication (9600 baud, 8-N-1) |
| **Actuation Hardware** | Arduino Uno / Nano / Mega (C++) | Servo control (SG90/MG995) or stepper motor (28BYJ-48 with ULN2003) |

---

## 3. Repository Structure

```text
schoolproj_smartaibin/
├── README.md                           # Main English documentation & GitHub landing page
├── README.de.md                        # Complete German documentation
├── COLAB_TRAINING_GUIDE.md             # Google Colab YOLOv8 training walkthrough
├── PROJECT_DOCUMENTATION.md            # Technical specifications and developer guide
├── Train_SmartBin_YOLO.ipynb           # Ready-to-run Jupyter training notebook
├── SmartBin.slnx                       # .NET 8 Visual Studio / VS Code solution
├── ArduinoFirmware/
│   ├── README.md                       # Wiring diagrams and pinout specifications
│   └── SmartBin_Arduino/
│       └── SmartBin_Arduino.ino        # Microcontroller firmware sketch
└── SmartBinApp/
    ├── SmartBinApp.csproj              # WPF project file
    ├── best.onnx                       # Pretrained YOLOv8 ONNX model weights
    ├── Converters/                     # XAML value converters
    ├── Models/                         # Data models (DetectionResult, LogMessage, etc.)
    ├── Services/                       # Localization, camera, AI, and UART services
    └── ViewModels/                     # MVVM architecture & UI state bindings
```

---

## 4. Hardware Communication Protocol (UART 9600)

* **Baud Rate:** `9600 baud`
* **Frame Format:** `8 data bits, no parity, 1 stop bit (8-N-1)`
* **Flow Control:** `DTR / RTS = true`

### Commands (PC ➔ Arduino):
| Byte | Compartment | Action |
|:---:|---|---|
| `'P'` | **Plastic** | Open plastic compartment lid for 2.5–3 sec, then close |
| `'B'` | **Paper** | Open paper compartment lid for 2.5–3 sec, then close |
| `'O'` | **Organic** | Open organic/bio compartment lid for 2.5–3 sec, then close |

### Responses (Arduino ➔ PC):
| String / Char | Source | Meaning |
|:---:|---|---|
| `"SMARTBIN_READY\n"` | `setup()` on boot | Microcontroller successfully initialized |
| `'K'` | Motor completion | Lid closed, controller ready for next object |

---

## 5. Building and Running the WPF Operator Client

### Requirements:
* Windows 10 / 11 (x64)
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
* USB or integrated webcam

### CLI Build:
```powershell
cd SmartBinApp
dotnet build
dotnet run
```

---

## 6. Training YOLOv8 in Google Colab
See [`COLAB_TRAINING_GUIDE.md`](COLAB_TRAINING_GUIDE.md).

---

## 7. Flashing Arduino Microcontroller
See [`ArduinoFirmware/README.md`](ArduinoFirmware/README.md).

---

## 8. Operating Modes: Physical Hardware & Demo Emulator

1. **Hardware Mode:** Connect Arduino via USB, select COM port, and click **Connect**.
2. **Demo Emulator Mode:** Toggle the **Demo Mode** checkbox to test detection, automated sorting, and telemetry without any hardware connected.
