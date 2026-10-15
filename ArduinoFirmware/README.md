# 🔌 Arduino Wiring & Firmware Guide

This folder contains the complete firmware sketch for the **AI Smart Bin** microcontroller.

## 📌 Supported Microcontrollers
* **Arduino Uno R3 / R4**
* **Arduino Nano (ATmega328P)**
* **Arduino Mega 2560**

---

## 🛠️ Option 1: 3 Servos Setup (Default Mode: `MODE_THREE_SERVOS`)
Ideal for a 3-compartment waste bin with separate lids:

### Pinout Configuration:
| Servo Compartment | Signal Wire (Orange / Yellow) | Power (Red VCC) | Ground (Brown / Black GND) |
|---|:---:|:---:|:---:|
| **Plastic Lid** | **Digital Pin D9** | External +5V | GND |
| **Paper Lid** | **Digital Pin D10** | External +5V | GND |
| **Organic Lid** | **Digital Pin D11** | External +5V | GND |

> ⚠️ **CRITICAL POWER SUPPLY RULE:**  
> Three SG90/MG995 servomotors during rotation can draw up to 1.5–2.0 Amperes of peak current.  
> **Do NOT power all servos directly from the Arduino 5V pin!**  
> Use an external regulated 5V (2A) power supply or breadboard power module (MB102).  
> **Always connect the external power supply GND to the Arduino GND pin!**

---

## ⚙️ Option 2: 28BYJ-48 Stepper Motor Setup (`MODE_STEPPER_MOTOR`)
Suitable for carousel or rotating chute bin designs:

### ULN2003 Driver Board Pinout:
| ULN2003 Driver Pin | Arduino Pin |
|---|:---:|
| `IN1` | **D8** |
| `IN2` | **D9** |
| `IN3` | **D10** |
| `IN4` | **D11** |
| `+` (5V-12V) | +5V from external PSU |
| `-` (GND) | Shared GND with Arduino & PSU |

---

## 🚀 Firmware Upload Instructions:
1. Connect your Arduino board to the PC via USB.
2. Open `SmartBin_Arduino.ino` in **Arduino IDE**.
3. In **Tools**:
   - **Board:** Select *Arduino Uno* (or *Arduino Nano*).
   - *For Arduino Nano:* If upload fails, switch **Processor** to *ATmega328P (Old Bootloader)*.
   - **Port:** Select the detected COM port (e.g. `COM3` or `COM4`).
4. Click **Upload** (`Ctrl + U`).
5. Once uploaded, close the Serial Monitor in Arduino IDE (to allow the C# app to access the port).
6. Open **SmartBinApp**, select the COM port and click **Connect**!

---

## 🎯 Motor Calibration & EEPROM Zeroing

To ensure the bin lids or sorting chute align with the physical compartments:
1. In the **SmartBinApp** under *Manual Actuator Testing & Calibration*:
   - Select the target motor: **All**, **Plastic (D9)**, **Paper (D10)**, or **Organic (D11)**.
   - Click **◀ Step Left** (`'<'`) or **Step Right ▶** (`'>'`) to nudge the selected motor in small increments.
   - Once physically aligned, click **🎯 Set Zero / Home** (`'Z'`).
2. The Arduino firmware writes the zero reference offsets into internal **EEPROM**.
3. The zero position is **persistent across power cycles and reboots**, so every subsequent sorting cycle starts from this exact calibrated home position!

