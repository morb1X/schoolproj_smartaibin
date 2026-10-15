/*
 * =====================================================================================
 *  AI SMART BIN — ARDUINO MICROCONTROLLER FIRMWARE
 *  Project: Smart School Waste Sorting Station with AI (YOLOv8)
 * =====================================================================================
 * 
 *  SERIAL PROTOCOL SPECIFICATION (UART 9600 BAUD):
 *  Commands from PC (WPF C# Application):
 *    'P' — Plastic sorting cycle
 *    'B' — Paper & Cardboard sorting cycle
 *    'O' — Organic / Bio-waste sorting cycle
 *    '<' / 'L' — Manual calibration: Nudge motor left (counter-clockwise)
 *    '>' / 'R' — Manual calibration: Nudge motor right (clockwise)
 *    'Z' — Set Zero / Home position (saves to EEPROM and starts cycles from here)
 * 
 *  Responses to PC:
 *    "SMARTBIN_READY" — Startup ready signal
 *    'K'              — Actuation/calibration cycle complete, ready for next command
 * 
 * =====================================================================================
 */

#include <Servo.h>
#include <EEPROM.h>

// =====================================================================================
// SELECT ACTUATOR HARDWARE MODE (UNCOMMENT DESIRED MODE):
// =====================================================================================
#define MODE_THREE_SERVOS        // MODE 1 (DEFAULT): 3 independent servo lids
//#define MODE_STEPPER_MOTOR     // MODE 2: 28BYJ-48 Stepper motor with ULN2003 driver
//#define MODE_SINGLE_SERVO_CHUTE // MODE 3: Single servo rotating distribution chute

// =====================================================================================
// EEPROM CALIBRATION PERSISTENCE CONFIGURATION
// =====================================================================================
const int EEPROM_MAGIC_ADDR = 0;
const byte EEPROM_MAGIC_VAL = 0x53; // 'S'
const int EEPROM_OFFSET_ADDR = 2;   // 2 bytes for int offset

// =====================================================================================
// PIN CONFIGURATION & PARAMETERS
// =====================================================================================
const int LED_PIN = 13;          // Built-in activity LED

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
  // ULN2003 driver pins for 28BYJ-48
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

// Active motor target for calibration: 0 = ALL, 1 = PLASTIC, 2 = PAPER, 3 = ORGANIC
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
// SETUP
// =====================================================================================
void setup() {
  Serial.begin(9600);

  pinMode(LED_PIN, OUTPUT);
  digitalWrite(LED_PIN, LOW);

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

    digitalWrite(LED_PIN, HIGH);

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

      // Select Motor for Calibration: 0=All, 1=Plastic, 2=Paper, 3=Organic
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

    digitalWrite(LED_PIN, LOW);

    // CRITICAL: Send acknowledgement signal 'K' back to PC
    Serial.println('K');
    Serial.flush();
  }
}

// =====================================================================================
// CALIBRATION HANDLERS
// =====================================================================================

void handleNudgeLeft() {
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
// HELPER FUNCTIONS
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
