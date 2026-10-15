/*
 * =====================================================================================
 *  FUNDUINO MEGA 2560 SMART WASTE BIN CONTROLLER
 *  AI Smart Bin Project (YOLOv8 + WPF Desktop Application)
 * =====================================================================================
 * 
 *  HARDWARE SETUP (FUNDUINO / ARDUINO MEGA 2560):
 *  - 3 x 28BYJ-48 Stepper Motors with ULN2003 Drivers:
 *      Motor 0 (Plastic): Pins 8, 10, 9, 11
 *      Motor 1 (Paper):   Pins 12, 13, 14, 15
 *      Motor 2 (Organic): Pins 16, 17, 18, 19
 *  - 3 x Compartment Indicator LEDs:
 *      Pin 5: Red LED   (Plastic)
 *      Pin 6: Green LED (Paper)
 *      Pin 7: Blue LED  (Organic / Bio)
 * 
 *  SERIAL PROTOCOL SPECIFICATION (UART 9600 BAUD):
 *    'P' / 'p' — Plastic sorting cycle
 *    'B' / 'b' — Paper & Cardboard sorting cycle
 *    'O' / 'o' — Organic / Bio-waste sorting cycle
 *    '0'       — Select ALL motors for calibration
 *    '1'       — Select Motor 0 (Plastic)
 *    '2'       — Select Motor 1 (Paper)
 *    '3'       — Select Motor 2 (Organic)
 *    '<' / 'L' — Manual calibration: Nudge motor left (CCW)
 *    '>' / 'R' — Manual calibration: Nudge motor right (CW)
 *    'Z' / 'z' — Set Zero / Home position
 * 
 *  RESPONSES TO PC:
 *    "SMARTBIN_READY" — Startup ready signal
 *    'K'              — Cycle/calibration complete, ready for next command
 * =====================================================================================
 */

#include <Arduino.h>

const byte phases[4][4] = {
  {1, 1, 0, 0},
  {0, 1, 1, 0},
  {0, 0, 1, 1},
  {1, 0, 0, 1}
};

const int pins[3][4] = {
  {8, 10, 9, 11},    // Motor 0: Plastic
  {12, 13, 14, 15},  // Motor 1: Paper
  {16, 17, 18, 19}   // Motor 2: Organic (Bio)
};

// Compartment indicator LEDs:
// Pin 5 = Red (Plastic)
// Pin 6 = Green (Paper)
// Pin 7 = Blue (Organic / Bio)
const int ledPins[3] = {5, 6, 7};

// Parameters for 28BYJ-48 stepper motors with ULN2003 driver:
const int STEPS_PER_CYCLE   = 512;   // ~90 degrees rotation per sorting cycle
const int CALIBRATION_STEPS = 64;    // ~11.25 degrees per manual calibration click
const int STEP_DELAY_MS     = 3;     // 3 ms per step (smooth & reliable torque)
const int LID_HOLD_TIME_MS  = 2000;  // 2 seconds hold open duration

// Active calibration motor target: 0 = ALL, 1 = Plastic, 2 = Paper, 3 = Organic
byte activeCalibrationMotor = 0;

void deenergizeMotor(int m) {
  if (m < 0 || m >= 3) return;
  for (int i = 0; i < 4; i++) {
    digitalWrite(pins[m][i], LOW);
  }
}

void deenergizeAll() {
  for (int m = 0; m < 3; m++) {
    deenergizeMotor(m);
  }
}

void rotate(int m, int steps, bool forward) {
  if (m < 0 || m >= 3) return;
  for (int s = 0; s < steps; s++) {
    int p = forward ? (s % 4) : (3 - (s % 4));
    for (int i = 0; i < 4; i++) {
      digitalWrite(pins[m][i], phases[p][i]);
    }
    delay(STEP_DELAY_MS);
  }
  deenergizeMotor(m);
}

void openLid(int m) {
  if (m < 0 || m >= 3) return;
  digitalWrite(ledPins[m], HIGH); // Turn on indicator LED
  rotate(m, STEPS_PER_CYCLE, true);
  delay(LID_HOLD_TIME_MS);
  rotate(m, STEPS_PER_CYCLE, false);
  digitalWrite(ledPins[m], LOW);  // Turn off indicator LED
  deenergizeMotor(m);
  Serial.println('K');
  Serial.flush();
}

void nudgeMotor(int m, int steps, bool forward) {
  if (m < 0 || m >= 3) return;
  digitalWrite(ledPins[m], HIGH);
  rotate(m, steps, forward);
  digitalWrite(ledPins[m], LOW);
  deenergizeMotor(m);
}

void handleNudge(bool forward) {
  if (activeCalibrationMotor == 0) {
    // Nudge all motors
    for (int m = 0; m < 3; m++) {
      nudgeMotor(m, CALIBRATION_STEPS, forward);
    }
  } else if (activeCalibrationMotor >= 1 && activeCalibrationMotor <= 3) {
    // Nudge specific motor (index 0, 1, or 2)
    nudgeMotor(activeCalibrationMotor - 1, CALIBRATION_STEPS, forward);
  }
  Serial.println('K');
  Serial.flush();
}

void setup() {
  Serial.begin(9600);

  for (int m = 0; m < 3; m++) {
    for (int i = 0; i < 4; i++) {
      pinMode(pins[m][i], OUTPUT);
      digitalWrite(pins[m][i], LOW);
    }
    pinMode(ledPins[m], OUTPUT);
    digitalWrite(ledPins[m], LOW);
  }

  // Self-test LED flash sequence on startup (confirm board & wiring active)
  for (int m = 0; m < 3; m++) {
    digitalWrite(ledPins[m], HIGH);
    delay(80);
    digitalWrite(ledPins[m], LOW);
  }

  deenergizeAll();
  delay(200);

  // Send ready signal to PC
  Serial.println(F("SMARTBIN_READY"));
  Serial.flush();
}

void loop() {
  if (Serial.available() > 0) {
    char c = Serial.read();

    if (c == '\r' || c == '\n' || c == ' ') {
      return;
    }

    switch (c) {
      // Normal Sorting Cycles
      case 'P':
      case 'p':
        openLid(0); // Plastic
        break;

      case 'B':
      case 'b':
        openLid(1); // Paper
        break;

      case 'O':
      case 'o':
        openLid(2); // Organic (Bio)
        break;

      // Select Target Motor for Calibration: 0=All, 1=Plastic, 2=Paper, 3=Organic
      case '0':
        activeCalibrationMotor = 0;
        Serial.println('K');
        Serial.flush();
        break;

      case '1':
        activeCalibrationMotor = 1;
        Serial.println('K');
        Serial.flush();
        break;

      case '2':
        activeCalibrationMotor = 2;
        Serial.println('K');
        Serial.flush();
        break;

      case '3':
        activeCalibrationMotor = 3;
        Serial.println('K');
        Serial.flush();
        break;

      // Manual Motor Calibration Commands
      case '<':
      case 'L':
      case 'l':
        handleNudge(false); // CCW (Left)
        break;

      case '>':
      case 'R':
      case 'r':
        handleNudge(true);  // CW (Right)
        break;

      case 'Z':
      case 'z':
        deenergizeAll();
        Serial.println('K');
        Serial.flush();
        break;

      default:
        break;
    }
  }
}