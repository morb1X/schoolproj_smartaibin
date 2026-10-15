/*
 * =====================================================================================
 *  AI SMART BIN — ARDUINO MICROCONTROLLER FIRMWARE
 *  Project: Smart School Waste Sorting Station with AI (YOLOv8)
 * =====================================================================================
 * 
 *  SERIAL PROTOCOL SPECIFICATION (UART 9600 BAUD):
 *  Commands from PC (WPF C# Application):
 *    'P' — Plastic
 *    'B' — Paper & Cardboard
 *    'O' — Organic / Bio-waste
 * 
 *  Responses to PC:
 *    "SMARTBIN_READY" — Startup ready signal
 *    'K'              — Actuation cycle complete, ready for next object
 * 
 * =====================================================================================
 */

#include <Servo.h>

// =====================================================================================
// SELECT ACTUATOR HARDWARE MODE (UNCOMMENT DESIRED MODE):
// =====================================================================================
#define MODE_THREE_SERVOS        // MODE 1 (DEFAULT): 3 independent servo lids
//#define MODE_STEPPER_MOTOR     // MODE 2: 28BYJ-48 Stepper motor with ULN2003 driver
//#define MODE_SINGLE_SERVO_CHUTE // MODE 3: Single servo rotating distribution chute

// =====================================================================================
// PIN CONFIGURATION & PARAMETERS
// =====================================================================================
const int LED_PIN = 13;          // Built-in activity LED

#ifdef MODE_THREE_SERVOS
  // Servo pins for SG90 / MG995
  const int SERVO_PIN_PLASTIC = 9;   // Plastic lid servo
  const int SERVO_PIN_PAPER   = 10;  // Paper lid servo
  const int SERVO_PIN_ORGANIC = 11;  // Organic lid servo

  // Lid angles
  const int ANGLE_CLOSED = 0;        // Closed position (degrees)
  const int ANGLE_OPENED = 95;       // Open position (degrees)
  const int LID_HOLD_TIME_MS = 2500; // Hold open duration (2.5 sec)

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
  const int STEP_DELAY_US = 1500;    // Step delay in microseconds
  const int HOLD_TIME_MS = 2000;     // Pause duration
#endif

#ifdef MODE_SINGLE_SERVO_CHUTE
  const int CHUTE_SERVO_PIN = 9;     // Chute servo pin
  const int ANGLE_CHUTE_PLASTIC = 30; // Tilt left
  const int ANGLE_CHUTE_PAPER   = 90; // Center
  const int ANGLE_CHUTE_ORGANIC = 150;// Tilt right
  Servo servoChute;
#endif

// =====================================================================================
// SETUP
// =====================================================================================
void setup() {
  Serial.begin(9600);

  pinMode(LED_PIN, OUTPUT);
  digitalWrite(LED_PIN, LOW);

#ifdef MODE_THREE_SERVOS
  servoPlastic.attach(SERVO_PIN_PLASTIC);
  servoPaper.attach(SERVO_PIN_PAPER);
  servoOrganic.attach(SERVO_PIN_ORGANIC);

  servoPlastic.write(ANGLE_CLOSED);
  servoPaper.write(ANGLE_CLOSED);
  servoOrganic.write(ANGLE_CLOSED);
#endif

#ifdef MODE_STEPPER_MOTOR
  pinMode(STEPPER_IN1, OUTPUT);
  pinMode(STEPPER_IN2, OUTPUT);
  pinMode(STEPPER_IN3, OUTPUT);
  pinMode(STEPPER_IN4, OUTPUT);
  stopStepper();
#endif

#ifdef MODE_SINGLE_SERVO_CHUTE
  servoChute.attach(CHUTE_SERVO_PIN);
  servoChute.write(ANGLE_CHUTE_PAPER);
#endif

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

      default:
        break;
    }

    digitalWrite(LED_PIN, LOW);

    // CRITICAL: Send acknowledgement signal 'K' back to PC
    Serial.println('K');
  }
}

// =====================================================================================
// COMPARTMENT HANDLERS
// =====================================================================================

void handleWastePlastic() {
#ifdef MODE_THREE_SERVOS
  smoothMove(servoPlastic, ANGLE_CLOSED, ANGLE_OPENED, 15);
  delay(LID_HOLD_TIME_MS);
  smoothMove(servoPlastic, ANGLE_OPENED, ANGLE_CLOSED, 15);
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
  servoChute.write(ANGLE_CHUTE_PAPER);
#endif
}

void handleWastePaper() {
#ifdef MODE_THREE_SERVOS
  smoothMove(servoPaper, ANGLE_CLOSED, ANGLE_OPENED, 15);
  delay(LID_HOLD_TIME_MS);
  smoothMove(servoPaper, ANGLE_OPENED, ANGLE_CLOSED, 15);
#endif

#ifdef MODE_STEPPER_MOTOR
  stepMotor(STEPS_PER_CYCLE * 2, true);
  delay(HOLD_TIME_MS);
  stepMotor(STEPS_PER_CYCLE * 2, false);
  stopStepper();
#endif

#ifdef MODE_SINGLE_SERVO_CHUTE
  servoChute.write(ANGLE_CHUTE_PAPER);
  delay(LID_HOLD_TIME_MS);
  servoChute.write(ANGLE_CHUTE_PAPER);
#endif
}

void handleWasteOrganic() {
#ifdef MODE_THREE_SERVOS
  smoothMove(servoOrganic, ANGLE_CLOSED, ANGLE_OPENED, 15);
  delay(LID_HOLD_TIME_MS);
  smoothMove(servoOrganic, ANGLE_OPENED, ANGLE_CLOSED, 15);
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
  servoChute.write(ANGLE_CHUTE_PAPER);
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
