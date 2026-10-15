/*
 * =====================================================================================
 *  AI SMART BIN — ARDUINO MICROCONTROLLER FIRMWARE (8-STEP HALF-STEPPING)
 *  Project: Smart School Waste Sorting Station with AI (YOLOv8)
 * =====================================================================================
 * 
 *  SERIAL PROTOCOL SPECIFICATION (UART 9600 BAUD):
 *  Commands from PC (WPF C# Application):
 *    'P' / 'p' — Plastic sorting cycle
 *    'B' / 'b' — Paper & Cardboard sorting cycle
 *    'O' / 'o' — Organic / Bio-waste sorting cycle
 *    '0'       — Select ALL motors for calibration
 *    '1'       — Select Motor 0 (Platform Lift Winch)
 *    '2'       — Select Motor 1 (Left Sorter Flap)
 *    '3'       — Select Motor 2 (Right Sorter Flap)
 *    '<' / 'L' — Manual calibration: Nudge motor left (CCW)
 *    '>' / 'R' — Manual calibration: Nudge motor right (CW)
 *    'Z' / 'z' — Set Zero / Home position
 * 
 *  Responses to PC:
 *    "SMARTBIN_READY" — Startup ready signal
 *    'K'              — Actuation/calibration cycle complete, ready for next command
 * 
 * =====================================================================================
 */

#include <Arduino.h>
#include <Servo.h>
#include <EEPROM.h>

// =====================================================================================
// SELECT ACTUATOR HARDWARE MODE (DEFAULT: FUNDUINO MEGA WITH 3 STEPPER MOTORS):
// =====================================================================================
#define MODE_FUNDUINO_THREE_STEPPERS  // MODE 1 (DEFAULT): Funduino Mega + 3 x 28BYJ-48 Steppers + 3 LEDs
//#define MODE_THREE_SERVOS           // MODE 2: 3 independent servo lids (D9, D10, D11)
//#define MODE_STEPPER_MOTOR          // MODE 3: Single 28BYJ-48 Stepper carousel
//#define MODE_SINGLE_SERVO_CHUTE     // MODE 4: Single servo rotating distribution chute

// =====================================================================================
// EEPROM CALIBRATION PERSISTENCE CONFIGURATION
// =====================================================================================
const int EEPROM_MAGIC_ADDR = 0;
const byte EEPROM_MAGIC_VAL = 0x53; // 'S'
const int EEPROM_OFFSET_ADDR = 2;   // 2 bytes for int offset

// =====================================================================================
// PIN CONFIGURATION & PARAMETERS
// =====================================================================================

#ifdef MODE_FUNDUINO_THREE_STEPPERS
  // Motor indices:
  const int MOTOR_PLATFORM = 0; // Winch thread motor on table (Pins 8, 10, 9, 11)
  const int MOTOR_LEFT     = 1; // Left sorter flap (Pins 12, 14, 13, 15)
  const int MOTOR_RIGHT    = 2; // Right sorter flap (Pins 16, 18, 17, 19)

  // 8-Phase Half-Stepping Drive Matrix for 28BYJ-48 (Maximum torque, zero stall in reverse):
  const byte phases[8][4] = {
    {1, 0, 0, 0},
    {1, 1, 0, 0},
    {0, 1, 0, 0},
    {0, 1, 1, 0},
    {0, 0, 1, 0},
    {0, 0, 1, 1},
    {0, 0, 0, 1},
    {1, 0, 0, 1}
  };

  // Funduino Mega Stepper Pinout with IN1, IN3, IN2, IN4 coil order:
  const int stepperPins[3][4] = {
    {8, 10, 9, 11},    // Motor 0: Table Platform Winch (IN1=8, IN3=10, IN2=9, IN4=11)
    {12, 14, 13, 15},  // Motor 1: Left Sorter Flap     (IN1=12, IN3=14, IN2=13, IN4=15)
    {16, 18, 17, 19}   // Motor 2: Right Sorter Flap    (IN1=16, IN3=18, IN2=17, IN4=19)
  };

  // Compartment Indicator LEDs:
  // Pin 5 = Red (Plastic), Pin 6 = Green (Paper), Pin 7 = Blue (Bio / Organic)
  const int ledPins[3] = {5, 6, 7};

  const int STEPS_FLAP_TILT     = 760;   // ~67 degrees tilt for sorter flaps (60-75 deg range)
  const int STEPS_PLATFORM_LIFT = 1024;  // Thread winding steps to lift inspection platform
  const int CALIBRATION_STEPS   = 128;   // Steps per manual calibration nudge click (~11.2 deg)
  const int STEP_DELAY_US       = 1200;  // 1200 microseconds per half-step (smooth & high torque)
  const int LID_HOLD_TIME_MS    = 1800;  // 1.8 seconds hold open
#endif

#ifdef MODE_THREE_SERVOS
  // Servo pins for SG90 / MG995
  const int SERVO_PIN_PLASTIC = 9;   // Plastic lid servo
  const int SERVO_PIN_PAPER   = 10;  // Paper lid servo
  const int SERVO_PIN_ORGANIC = 11;  // Organic lid servo

  // Calibrated resting (closed) angles saved in EEPROM (0 to 150 degrees)
  int anglePlastic = 10;
  int anglePaper   = 10;
  int angleOrganic = 10;

  const int CALIBRATION_SERVO_STEP = 5; // Degrees per manual calibration click
  const int ANGLE_DELTA_OPEN = 85;      // Lid opening stroke (+85 degrees)
  const int LID_HOLD_TIME_MS = 2500;    // Hold open duration (2.5 sec)

  Servo servoPlastic;
  Servo servoPaper;
  Servo servoOrganic;
#endif

#ifdef MODE_STEPPER_MOTOR
  // ULN2003 driver pins for single 28BYJ-48 carousel
  const int STEPPER_IN1 = 8;
  const int STEPPER_IN2 = 9;
  const int STEPPER_IN3 = 10;
  const int STEPPER_IN4 = 11;

  const int STEPS_PER_CYCLE = 512;   // Steps count (~90 degrees)
  const int CALIBRATION_STEPS = 64;  // Steps per manual nudge (~11.2 degrees)
  const int STEP_DELAY_US = 1500;    // Step delay in microseconds
  const int HOLD_TIME_MS = 2000;     // Pause duration

  int currentStepperOffset = 0;      // Offset from initial boot
#endif

#ifdef MODE_SINGLE_SERVO_CHUTE
  const int CHUTE_SERVO_PIN = 9;     // Chute servo pin
  const int ANGLE_CHUTE_PLASTIC = 30; // Tilt left
  const int BASE_CHUTE_CENTER   = 90; // Center
  const int ANGLE_CHUTE_ORGANIC = 150;// Tilt right
  const int LID_HOLD_TIME_MS = 2500;

  int chuteZeroOffset = 0;           // Calibrated center offset (-50 to +50)
  Servo servoChute;
#endif

// Active motor target for calibration: 0 = ALL, 1 = Platform, 2 = Left Flap, 3 = Right Flap
byte activeCalibrationMotor = 0;

// =====================================================================================
// CALIBRATION & EEPROM HELPER FUNCTIONS
// =====================================================================================
void loadCalibration() {
  if (EEPROM.read(EEPROM_MAGIC_ADDR) == EEPROM_MAGIC_VAL) {
    #ifdef MODE_THREE_SERVOS
      EEPROM.get(2, anglePlastic);
      EEPROM.get(4, anglePaper);
      EEPROM.get(6, angleOrganic);
      anglePlastic = constrain(anglePlastic, 0, 150);
      anglePaper   = constrain(anglePaper, 0, 150);
      angleOrganic = constrain(angleOrganic, 0, 150);
    #endif
    #ifdef MODE_SINGLE_SERVO_CHUTE
      EEPROM.get(2, chuteZeroOffset);
      chuteZeroOffset = constrain(chuteZeroOffset, -50, 50);
    #endif
  }
}

void saveCalibration() {
  EEPROM.write(EEPROM_MAGIC_ADDR, EEPROM_MAGIC_VAL);
  #ifdef MODE_THREE_SERVOS
    EEPROM.put(2, anglePlastic);
    EEPROM.put(4, anglePaper);
    EEPROM.put(6, angleOrganic);
  #endif
  #ifdef MODE_SINGLE_SERVO_CHUTE
    EEPROM.put(2, chuteZeroOffset);
  #endif
}

void applyZeroPosition() {
#ifdef MODE_FUNDUINO_THREE_STEPPERS
  for (int m = 0; m < 3; m++) {
    for (int i = 0; i < 4; i++) {
      digitalWrite(stepperPins[m][i], LOW);
    }
    digitalWrite(ledPins[m], LOW);
  }
#endif

#ifdef MODE_THREE_SERVOS
  servoPlastic.write(anglePlastic);
  servoPaper.write(anglePaper);
  servoOrganic.write(angleOrganic);
#endif

#ifdef MODE_SINGLE_SERVO_CHUTE
  servoChute.write(constrain(BASE_CHUTE_CENTER + chuteZeroOffset, 30, 150));
#endif

#ifdef MODE_STEPPER_MOTOR
  stopStepper();
#endif
}

// =====================================================================================
// FUNDUINO THREE STEPPERS HELPER FUNCTIONS
// =====================================================================================
#ifdef MODE_FUNDUINO_THREE_STEPPERS
void deenergizeStepper(int m) {
  if (m < 0 || m >= 3) return;
  for (int i = 0; i < 4; i++) {
    digitalWrite(stepperPins[m][i], LOW);
  }
}

void rotateStepper(int m, int steps, bool forward) {
  if (m < 0 || m >= 3) return;
  for (int s = 0; s < steps; s++) {
    int p = forward ? (s % 8) : (7 - (s % 8));
    for (int i = 0; i < 4; i++) {
      digitalWrite(stepperPins[m][i], phases[p][i]);
    }
    delayMicroseconds(STEP_DELAY_US);
  }
  deenergizeStepper(m);
}

void rotateTwoSteppers(int m1, bool fwd1, int m2, bool fwd2, int steps) {
  if (m1 < 0 || m1 >= 3 || m2 < 0 || m2 >= 3) return;
  for (int s = 0; s < steps; s++) {
    int p1 = fwd1 ? (s % 8) : (7 - (s % 8));
    int p2 = fwd2 ? (s % 8) : (7 - (s % 8));
    for (int i = 0; i < 4; i++) {
      digitalWrite(stepperPins[m1][i], phases[p1][i]);
      digitalWrite(stepperPins[m2][i], phases[p2][i]);
    }
    delayMicroseconds(STEP_DELAY_US);
  }
  deenergizeStepper(m1);
  deenergizeStepper(m2);
}

void nudgeStepper(int m, int steps, bool forward) {
  if (m < 0 || m >= 3) return;
  digitalWrite(ledPins[m], HIGH);
  rotateStepper(m, steps, forward);
  digitalWrite(ledPins[m], LOW);
  deenergizeStepper(m);
}
#endif

// =====================================================================================
// SETUP
// =====================================================================================
void setup() {
  Serial.begin(9600);

#ifdef MODE_FUNDUINO_THREE_STEPPERS
  for (int m = 0; m < 3; m++) {
    for (int i = 0; i < 4; i++) {
      pinMode(stepperPins[m][i], OUTPUT);
      digitalWrite(stepperPins[m][i], LOW);
    }
    pinMode(ledPins[m], OUTPUT);
    digitalWrite(ledPins[m], LOW);
  }

  // Diagnostic LED flash
  for (int m = 0; m < 3; m++) {
    digitalWrite(ledPins[m], HIGH);
    delay(80);
    digitalWrite(ledPins[m], LOW);
  }
#endif

  // Load saved calibration from EEPROM
  loadCalibration();

#ifdef MODE_THREE_SERVOS
  servoPlastic.attach(SERVO_PIN_PLASTIC);
  servoPaper.attach(SERVO_PIN_PAPER);
  servoOrganic.attach(SERVO_PIN_ORGANIC);
#endif

#ifdef MODE_STEPPER_MOTOR
  pinMode(STEPPER_IN1, OUTPUT);
  pinMode(STEPPER_IN2, OUTPUT);
  pinMode(STEPPER_IN3, OUTPUT);
  pinMode(STEPPER_IN4, OUTPUT);
#endif

#ifdef MODE_SINGLE_SERVO_CHUTE
  servoChute.attach(CHUTE_SERVO_PIN);
#endif

  // Move to calibrated zero position
  applyZeroPosition();

  delay(300);

  // Send ready signal to PC
  Serial.println(F("SMARTBIN_READY"));
  Serial.flush();
}

// =====================================================================================
// MAIN LOOP
// =====================================================================================
void loop() {
  if (Serial.available() > 0) {
    char cmd = Serial.read();

    if (cmd == '\r' || cmd == '\n' || cmd == ' ') {
      return;
    }

    switch (cmd) {
      // Normal Sorting Commands
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

      // Select Motor for Calibration: 0=All, 1=Platform, 2=Left Flap, 3=Right Flap
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

      // Manual Motor Calibration Commands
      case '<':
      case 'L':
      case 'l':
        handleNudgeLeft();
        break;

      case '>':
      case 'R':
      case 'r':
        handleNudgeRight();
        break;

      case 'Z':
      case 'z':
        handleSetZero();
        break;

      default:
        break;
    }

    // CRITICAL: Send acknowledgement signal 'K' back to PC
    Serial.println('K');
    Serial.flush();
  }
}

// =====================================================================================
// CALIBRATION HANDLERS
// =====================================================================================

void handleNudgeLeft() {
#ifdef MODE_FUNDUINO_THREE_STEPPERS
  if (activeCalibrationMotor == 0) {
    for (int m = 0; m < 3; m++) nudgeStepper(m, CALIBRATION_STEPS, false);
  } else if (activeCalibrationMotor >= 1 && activeCalibrationMotor <= 3) {
    nudgeStepper(activeCalibrationMotor - 1, CALIBRATION_STEPS, false);
  }
#endif

#ifdef MODE_STEPPER_MOTOR
  stepMotor(CALIBRATION_STEPS, false); // Nudge CCW
  currentStepperOffset -= CALIBRATION_STEPS;
  stopStepper();
#endif

#ifdef MODE_THREE_SERVOS
  if (activeCalibrationMotor == 1 || activeCalibrationMotor == 0) {
    anglePlastic = constrain(anglePlastic - CALIBRATION_SERVO_STEP, 0, 150);
    servoPlastic.write(anglePlastic);
  }
  if (activeCalibrationMotor == 2 || activeCalibrationMotor == 0) {
    anglePaper = constrain(anglePaper - CALIBRATION_SERVO_STEP, 0, 150);
    servoPaper.write(anglePaper);
  }
  if (activeCalibrationMotor == 3 || activeCalibrationMotor == 0) {
    angleOrganic = constrain(angleOrganic - CALIBRATION_SERVO_STEP, 0, 150);
    servoOrganic.write(angleOrganic);
  }
#endif

#ifdef MODE_SINGLE_SERVO_CHUTE
  chuteZeroOffset = constrain(chuteZeroOffset - 3, -40, 40);
  applyZeroPosition();
#endif
}

void handleNudgeRight() {
#ifdef MODE_FUNDUINO_THREE_STEPPERS
  if (activeCalibrationMotor == 0) {
    for (int m = 0; m < 3; m++) nudgeStepper(m, CALIBRATION_STEPS, true);
  } else if (activeCalibrationMotor >= 1 && activeCalibrationMotor <= 3) {
    nudgeStepper(activeCalibrationMotor - 1, CALIBRATION_STEPS, true);
  }
#endif

#ifdef MODE_STEPPER_MOTOR
  stepMotor(CALIBRATION_STEPS, true); // Nudge CW
  currentStepperOffset += CALIBRATION_STEPS;
  stopStepper();
#endif

#ifdef MODE_THREE_SERVOS
  if (activeCalibrationMotor == 1 || activeCalibrationMotor == 0) {
    anglePlastic = constrain(anglePlastic + CALIBRATION_SERVO_STEP, 0, 150);
    servoPlastic.write(anglePlastic);
  }
  if (activeCalibrationMotor == 2 || activeCalibrationMotor == 0) {
    anglePaper = constrain(anglePaper + CALIBRATION_SERVO_STEP, 0, 150);
    servoPaper.write(anglePaper);
  }
  if (activeCalibrationMotor == 3 || activeCalibrationMotor == 0) {
    angleOrganic = constrain(angleOrganic + CALIBRATION_SERVO_STEP, 0, 150);
    servoOrganic.write(angleOrganic);
  }
#endif

#ifdef MODE_SINGLE_SERVO_CHUTE
  chuteZeroOffset = constrain(chuteZeroOffset + 3, -40, 40);
  applyZeroPosition();
#endif
}

void handleSetZero() {
#ifdef MODE_FUNDUINO_THREE_STEPPERS
  for (int m = 0; m < 3; m++) {
    deenergizeStepper(m);
    digitalWrite(ledPins[m], LOW);
  }
#endif

#ifdef MODE_STEPPER_MOTOR
  // Mark current position as Home / 0
  currentStepperOffset = 0;
  stopStepper();
#endif

#ifdef MODE_THREE_SERVOS
  saveCalibration();
  applyZeroPosition();
#endif

#ifdef MODE_SINGLE_SERVO_CHUTE
  saveCalibration();
  applyZeroPosition();
#endif
}

// =====================================================================================
// COMPARTMENT HANDLERS
// =====================================================================================

void handleWastePlastic() {
#ifdef MODE_FUNDUINO_THREE_STEPPERS
  digitalWrite(ledPins[0], HIGH); // Red LED on

  // 1. Sorter flaps tilt inward: Left CW, Right CCW (opening middle gap)
  rotateTwoSteppers(MOTOR_LEFT, true, MOTOR_RIGHT, false, STEPS_FLAP_TILT);
  delay(300);

  // 2. Platform lifts to dump plastic down into the center
  rotateStepper(MOTOR_PLATFORM, STEPS_PLATFORM_LIFT, true);
  delay(LID_HOLD_TIME_MS);

  // 3. Platform lowers back down to table
  rotateStepper(MOTOR_PLATFORM, STEPS_PLATFORM_LIFT, false);
  delay(300);

  // 4. Sorter flaps return back to horizontal (Left CCW, Right CW)
  rotateTwoSteppers(MOTOR_LEFT, false, MOTOR_RIGHT, true, STEPS_FLAP_TILT);

  digitalWrite(ledPins[0], LOW); // Red LED off
  for (int m = 0; m < 3; m++) deenergizeStepper(m);
#endif

#ifdef MODE_THREE_SERVOS
  int openAngle = constrain(anglePlastic + ANGLE_DELTA_OPEN, 20, 180);
  smoothMove(servoPlastic, anglePlastic, openAngle, 15);
  delay(LID_HOLD_TIME_MS);
  smoothMove(servoPlastic, openAngle, anglePlastic, 15);
#endif

#ifdef MODE_STEPPER_MOTOR
  stepMotor(STEPS_PER_CYCLE, true);
  delay(HOLD_TIME_MS);
  stepMotor(STEPS_PER_CYCLE, false);
  stopStepper();
#endif

#ifdef MODE_SINGLE_SERVO_CHUTE
  servoChute.write(ANGLE_CHUTE_PLASTIC);
  delay(LID_HOLD_TIME_MS);
  applyZeroPosition();
#endif
}

void handleWastePaper() {
#ifdef MODE_FUNDUINO_THREE_STEPPERS
  digitalWrite(ledPins[1], HIGH); // Green LED on

  // 1. Platform lifts to dump paper onto sorter
  rotateStepper(MOTOR_PLATFORM, STEPS_PLATFORM_LIFT, true);
  delay(200);

  // 2. Left flap tilts CCW (left ramp into paper compartment)
  rotateStepper(MOTOR_LEFT, STEPS_FLAP_TILT, false);
  delay(LID_HOLD_TIME_MS);

  // 3. Platform lowers back down to table
  rotateStepper(MOTOR_PLATFORM, STEPS_PLATFORM_LIFT, false);
  delay(200);

  // 4. Left flap returns to horizontal (CW)
  rotateStepper(MOTOR_LEFT, STEPS_FLAP_TILT, true);

  digitalWrite(ledPins[1], LOW); // Green LED off
  for (int m = 0; m < 3; m++) deenergizeStepper(m);
#endif

#ifdef MODE_THREE_SERVOS
  int openAngle = constrain(anglePaper + ANGLE_DELTA_OPEN, 20, 180);
  smoothMove(servoPaper, anglePaper, openAngle, 15);
  delay(LID_HOLD_TIME_MS);
  smoothMove(servoPaper, openAngle, anglePaper, 15);
#endif

#ifdef MODE_STEPPER_MOTOR
  stepMotor(STEPS_PER_CYCLE * 2, true);
  delay(HOLD_TIME_MS);
  stepMotor(STEPS_PER_CYCLE * 2, false);
  stopStepper();
#endif

#ifdef MODE_SINGLE_SERVO_CHUTE
  servoChute.write(constrain(BASE_CHUTE_CENTER + chuteZeroOffset, 30, 150));
  delay(LID_HOLD_TIME_MS);
  applyZeroPosition();
#endif
}

void handleWasteOrganic() {
#ifdef MODE_FUNDUINO_THREE_STEPPERS
  digitalWrite(ledPins[2], HIGH); // Blue LED on

  // 1. Platform lifts to dump organic waste onto sorter
  rotateStepper(MOTOR_PLATFORM, STEPS_PLATFORM_LIFT, true);
  delay(200);

  // 2. Right flap tilts CW (right ramp into bio compartment)
  rotateStepper(MOTOR_RIGHT, STEPS_FLAP_TILT, true);
  delay(LID_HOLD_TIME_MS);

  // 3. Platform lowers back down to table
  rotateStepper(MOTOR_PLATFORM, STEPS_PLATFORM_LIFT, false);
  delay(200);

  // 4. Right flap returns to horizontal (CCW)
  rotateStepper(MOTOR_RIGHT, STEPS_FLAP_TILT, false);

  digitalWrite(ledPins[2], LOW); // Blue LED off
  for (int m = 0; m < 3; m++) deenergizeStepper(m);
#endif

#ifdef MODE_THREE_SERVOS
  int openAngle = constrain(angleOrganic + ANGLE_DELTA_OPEN, 20, 180);
  smoothMove(servoOrganic, angleOrganic, openAngle, 15);
  delay(LID_HOLD_TIME_MS);
  smoothMove(servoOrganic, openAngle, angleOrganic, 15);
#endif

#ifdef MODE_STEPPER_MOTOR
  stepMotor(STEPS_PER_CYCLE * 3, true);
  delay(HOLD_TIME_MS);
  stepMotor(STEPS_PER_CYCLE * 3, false);
  stopStepper();
#endif

#ifdef MODE_SINGLE_SERVO_CHUTE
  servoChute.write(ANGLE_CHUTE_ORGANIC);
  delay(LID_HOLD_TIME_MS);
  applyZeroPosition();
#endif
}

// =====================================================================================
// HELPER FUNCTIONS FOR SERVOS & SINGLE STEPPER
// =====================================================================================

#ifdef MODE_THREE_SERVOS
void smoothMove(Servo &s, int fromAngle, int toAngle, int stepDelayMs) {
  if (fromAngle < toAngle) {
    for (int pos = fromAngle; pos <= toAngle; pos += 2) {
      s.write(pos);
      delay(stepDelayMs);
    }
  } else {
    for (int pos = fromAngle; pos >= toAngle; pos -= 2) {
      s.write(pos);
      delay(stepDelayMs);
    }
  }
}
#endif

#ifdef MODE_STEPPER_MOTOR
const uint8_t stepMatrix[4] = {
  0b0001,
  0b0010,
  0b0100,
  0b1000
};

void stepMotor(int steps, bool clockwise) {
  for (int i = 0; i < steps; i++) {
    int index = clockwise ? (i % 4) : (3 - (i % 4));
    uint8_t bits = stepMatrix[index];

    digitalWrite(STEPPER_IN1, (bits & 0b0001) ? HIGH : LOW);
    digitalWrite(STEPPER_IN2, (bits & 0b0010) ? HIGH : LOW);
    digitalWrite(STEPPER_IN3, (bits & 0b0100) ? HIGH : LOW);
    digitalWrite(STEPPER_IN4, (bits & 0b1000) ? HIGH : LOW);

    delayMicroseconds(STEP_DELAY_US);
  }
}

void stopStepper() {
  digitalWrite(STEPPER_IN1, LOW);
  digitalWrite(STEPPER_IN2, LOW);
  digitalWrite(STEPPER_IN3, LOW);
  digitalWrite(STEPPER_IN4, LOW);
}
#endif
