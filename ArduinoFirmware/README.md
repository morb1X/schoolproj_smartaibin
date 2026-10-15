# 🔌 Arduino Wiring & Firmware Guide

This folder contains the complete firmware sketches for the **AI Smart Bin** microcontroller.

## 📌 Supported Microcontrollers
* **Funduino / Arduino Mega 2560** *(Primary School Project Setup)*
* **Arduino Uno R3 / R4**
* **Arduino Nano (ATmega328P)**

---

## 🛠️ Option 1: Funduino Mega with 3 Stepper Motors & 3 LEDs *(Active Default Mode)*
Configured in `sketch_oct13a.ino` and `SmartBin_Arduino.ino` (`MODE_FUNDUINO_THREE_STEPPERS`):

### Stepper Motor Wiring (28BYJ-48 with ULN2003 Drivers):
| Actuator / Compartment | Driver Inputs (`IN1`, `IN2`, `IN3`, `IN4`) | Mega Pins | Active LED Pin & Color |
|---|:---:|:---:|:---:|
| **Motor 0 (Plastic)** | `IN1, IN2, IN3, IN4` | **Pins 8, 10, 9, 11** | **Pin 5 (Red LED)** |
| **Motor 1 (Paper)** | `IN1, IN2, IN3, IN4` | **Pins 12, 13, 14, 15** | **Pin 6 (Green LED)** |
| **Motor 2 (Organic / Bio)** | `IN1, IN2, IN3, IN4` | **Pins 16, 17, 18, 19** | **Pin 7 (Blue LED)** |

* Coils are automatically de-energized (`LOW`) when idle to prevent motor and driver overheating.
* LED turns on during lid movement and turns off when the lid returns to the home position.

---

## 🛠️ Option 2: 3 Servos Setup (`MODE_THREE_SERVOS`)
Alternative setup for miniature bins using SG90 or MG995 RC servo motors:

| Servo Compartment | Signal Wire (PWM) | External Power (+5V) | Shared Ground (GND) |
|---|:---:|:---:|:---:|
| **Plastic Lid** | **Digital Pin D9** | External +5V | Shared GND |
| **Paper Lid** | **Digital Pin D10** | External +5V | Shared GND |
| **Organic Lid** | **Digital Pin D11** | External +5V | Shared GND |

> ⚠️ **POWER SUPPLY NOTE:** When using 3 servos, do NOT power them directly from the Arduino 5V regulator. Use an external regulated 5V (2A) power source with shared ground.

---

## 🚀 Firmware Upload Instructions:
1. Connect your Arduino board to the PC via USB.
2. Open either `SmartBin_Arduino/SmartBin_Arduino.ino` or `sketch_oct13a/sketch_oct13a.ino` in **Arduino IDE**.
3. In **Tools**:
   - **Board:** Select *Arduino Mega or Mega 2560* (or *Arduino Uno*).
   - **Processor:** *ATmega2560 (Mega 2560)*.
   - **Port:** Select the detected COM port (e.g. `COM3` or `COM4`).
4. Click **Upload** (`Ctrl + U`).
5. Once uploaded, close Arduino IDE or its Serial Monitor.
6. Open **SmartBinApp**, select the COM port, and click **Connect**!

---

## 🎯 Motor Calibration & EEPROM Zeroing

To ensure the bin lids align with the physical compartments:
1. In the **SmartBinApp** under *Manual Actuator Testing & Calibration*:
   - Select the target motor: **All**, **Plastic (M1)**, **Paper (M2)**, or **Organic (M3)**.
   - Click **◀ Step Left** (`'<'`) or **Step Right ▶** (`'>'`) to nudge the selected motor.
   - Once physically flush and closed, click **🎯 Set Zero / Home** (`'Z'`).
2. The Arduino firmware confirms with `'K'`, and all subsequent sorting cycles will open and close precisely relative to this home position!
