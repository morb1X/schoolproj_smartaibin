/*
 * =====================================================================================
 *  FUNDUINO MEGA 2560 SMART WASTE BIN CONTROLLER
 *  AI Smart Bin Project (YOLOv8 + WPF Desktop Application)
 * =====================================================================================
 * 
 *  HARDWARE SETUP (FUNDUINO / ARDUINO MEGA 2560):
 *  - 3 x 28BYJ-48 Stepper Motors with ULN2003 Drivers:
 *      Motor 0 (Platform Lift Winch): Pins 8, 10, 9, 11
 *      Motor 1 (Left Sorter Flap):    Pins 12, 13, 14, 15
 *      Motor 2 (Right Sorter Flap):   Pins 16, 17, 18, 19
 *  - 3 x Compartment Indicator LEDs:
 *      Pin 5: Red LED   (Plastic)
 *      Pin 6: Green LED (Paper)
 *      Pin 7: Blue LED  (Organic / Bio)
 * 
 *  SORTING MECHANICS:
 *  - PLASTIC ('P'): Sorter flaps tilt inward FIRST (Left CW, Right CCW), then platform
 *                   lifts to drop plastic straight down the center chute.
 *  - PAPER   ('B'): Platform lifts FIRST, then Left flap tilts CCW (left ramp)
 *                   so paper slides off to the left box.
 *  - BIO     ('O'): Platform lifts FIRST, then Right flap tilts CW (right ramp)
 *                   so organic waste slides off to the right box.
 * 
 *  CALIBRATION:
 *  - '0' = All Motors, '1' = Platform Winch, '2' = Left Flap, '3' = Right Flap
 *  - '<' / 'L' = Nudge CCW, '>' / 'R' = Nudge CW, 'Z' = Set Zero / Home
 * =====================================================================================
 */

#include <Arduino.h>

// Motor indices:
const int MOTOR_PLATFORM = 0; // Winch thread motor on table (Pins 8, 10, 9, 11)
const int MOTOR_LEFT     = 1; // Left sorter flap (Pins 12, 13, 14, 15)
const int MOTOR_RIGHT    = 2; // Right sorter flap (Pins 16, 17, 18, 19)

// 4-Phase Stepper Drive Matrix:
const byte phases[4][4] = {
  {1, 1, 0, 0},
  {0, 1, 1, 0},
  {0, 0, 1, 1},
  {1, 0, 0, 1}
};

const int pins[3][4] = {
  {8, 10, 9, 11},    // Motor 0: Table Platform Winch
  {12, 13, 14, 15},  // Motor 1: Left Sorter Flap (Paper)
  {16, 17, 18, 19}   // Motor 2: Right Sorter Flap (Bio)
};

// Indicator LEDs: Pin 5 = Red (Plastic), Pin 6 = Green (Paper), Pin 7 = Blue (Bio)
const int ledPins[3] = {5, 6, 7};

// Mechanical motion parameters:
const int STEPS_FLAP_TILT     = 380;  // ~67 degrees tilt for sorter flaps (60-75 deg range)
const int STEPS_PLATFORM_LIFT = 512;  // Thread winding steps to lift inspection platform
const int CALIBRATION_STEPS   = 64;   // Steps per manual calibration nudge click (~11 deg)
const int STEP_DELAY_MS       = 3;    // 3 ms per step (smooth & reliable torque)
const int LID_HOLD_TIME_MS    = 1800; // Hold open duration for trash to fall through

// Active calibration motor target: 0 = ALL, 1 = Platform, 2 = Left Flap, 3 = Right Flap
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

// Rotate a single stepper motor
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

// Rotate two stepper motors simultaneously (for opening/closing dual flaps together)
void rotateTwo(int m1, bool fwd1, int m2, bool fwd2, int steps) {
  if (m1 < 0 || m1 >= 3 || m2 < 0 || m2 >= 3) return;
  for (int s = 0; s < steps; s++) {
    int p1 = fwd1 ? (s % 4) : (3 - (s % 4));
    int p2 = fwd2 ? (s % 4) : (3 - (s % 4));
    for (int i = 0; i < 4; i++) {
      digitalWrite(pins[m1][i], phases[p1][i]);
      digitalWrite(pins[m2][i], phases[p2][i]);
    }
    delay(STEP_DELAY_MS);
  }
  deenergizeMotor(m1);
  deenergizeMotor(m2);
}

// -------------------------------------------------------------------------------------
// SORTING SEQUENCES
// -------------------------------------------------------------------------------------

// Plastic ('P'): Flaps tilt inward FIRST (opening center chute), then platform lifts
void handleWastePlastic() {
  digitalWrite(ledPins[0], HIGH); // Red LED on

  // 1. Sorter flaps tilt inward: Left CW, Right CCW (opening middle gap)
  rotateTwo(MOTOR_LEFT, true, MOTOR_RIGHT, false, STEPS_FLAP_TILT);
  delay(300);

  // 2. Platform lifts to dump plastic down into the center
  rotate(MOTOR_PLATFORM, STEPS_PLATFORM_LIFT, true);
  delay(LID_HOLD_TIME_MS);

  // 3. Platform lowers back down to table
  rotate(MOTOR_PLATFORM, STEPS_PLATFORM_LIFT, false);
  delay(300);

  // 4. Sorter flaps return back to horizontal (Left CCW, Right CW)
  rotateTwo(MOTOR_LEFT, false, MOTOR_RIGHT, true, STEPS_FLAP_TILT);

  digitalWrite(ledPins[0], LOW); // Red LED off
  deenergizeAll();
}

// Paper ('B'): Platform lifts FIRST, then Left flap tilts CCW (left ramp)
void handleWastePaper() {
  digitalWrite(ledPins[1], HIGH); // Green LED on

  // 1. Platform lifts to dump paper onto sorter
  rotate(MOTOR_PLATFORM, STEPS_PLATFORM_LIFT, true);
  delay(200);

  // 2. Left flap tilts CCW (left ramp into paper compartment)
  rotate(MOTOR_LEFT, STEPS_FLAP_TILT, false);
  delay(LID_HOLD_TIME_MS);

  // 3. Platform lowers back down to table
  rotate(MOTOR_PLATFORM, STEPS_PLATFORM_LIFT, false);
  delay(200);

  // 4. Left flap returns to horizontal (CW)
  rotate(MOTOR_LEFT, STEPS_FLAP_TILT, true);

  digitalWrite(ledPins[1], LOW); // Green LED off
  deenergizeAll();
}

// Bio / Organic ('O'): Platform lifts FIRST, then Right flap tilts CW (right ramp)
void handleWasteOrganic() {
  digitalWrite(ledPins[2], HIGH); // Blue LED on

  // 1. Platform lifts to dump organic waste onto sorter
  rotate(MOTOR_PLATFORM, STEPS_PLATFORM_LIFT, true);
  delay(200);

  // 2. Right flap tilts CW (right ramp into bio compartment)
  rotate(MOTOR_RIGHT, STEPS_FLAP_TILT, true);
  delay(LID_HOLD_TIME_MS);

  // 3. Platform lowers back down to table
  rotate(MOTOR_PLATFORM, STEPS_PLATFORM_LIFT, false);
  delay(200);

  // 4. Right flap returns to horizontal (CCW)
  rotate(MOTOR_RIGHT, STEPS_FLAP_TILT, false);

  digitalWrite(ledPins[2], LOW); // Blue LED off
  deenergizeAll();
}

// -------------------------------------------------------------------------------------
// CALIBRATION LOGIC
// -------------------------------------------------------------------------------------
void handleNudge(bool forward) {
  if (activeCalibrationMotor == 0) {
    // All motors
    for (int m = 0; m < 3; m++) {
      digitalWrite(ledPins[m], HIGH);
      rotate(m, CALIBRATION_STEPS, forward);
      digitalWrite(ledPins[m], LOW);
    }
  } else if (activeCalibrationMotor >= 1 && activeCalibrationMotor <= 3) {
    int m = activeCalibrationMotor - 1;
    digitalWrite(ledPins[m], HIGH);
    rotate(m, CALIBRATION_STEPS, forward);
    digitalWrite(ledPins[m], LOW);
  }
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

  // Self-test LED sequence on boot
  for (int m = 0; m < 3; m++) {
    digitalWrite(ledPins[m], HIGH);
    delay(80);
    digitalWrite(ledPins[m], LOW);
  }

  deenergizeAll();
  delay(200);

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
      // Sorting Cycles
      case 'P':
      case 'p':
        handleWastePlastic();
        break;

      case 'B':
      case 'b':
        handleWastePaper();
        break;

      case 'O':
      case 'o':
        handleWasteOrganic();
        break;

      // Calibration Motor Selection: 0=All, 1=Platform, 2=Left Flap, 3=Right Flap
      case '0':
        activeCalibrationMotor = 0;
        break;

      case '1':
        activeCalibrationMotor = 1;
        break;

      case '2':
        activeCalibrationMotor = 2;
        break;

      case '3':
        activeCalibrationMotor = 3;
        break;

      // Manual Calibration Nudge
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

      // Set Zero / Home Position
      case 'Z':
      case 'z':
        deenergizeAll();
        break;

      default:
        break;
    }

    // Acknowledge command completion to PC
    Serial.println('K');
    Serial.flush();
  }
}