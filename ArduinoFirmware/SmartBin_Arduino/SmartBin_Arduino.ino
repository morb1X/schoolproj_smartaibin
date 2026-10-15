/*
 * =====================================================================================
 *  AI SMART BIN — ПРОШИВКА МИКРОКОНТРОЛЛЕРА ARDUINO
 *  Проект: Школьная умная станция раздельного сбора отходов с ИИ (YOLOv8)
 * =====================================================================================
 * 
 *  ПРОТОКОЛ ОБМЕНА С ПК (UART 9600 бод):
 *  Команды от ПК (WPF C# приложение):
 *    'P' — Пластик (Plastic)
 *    'B' — Бумага / Картон (Paper / Box)
 *    'O' — Органика (Organic)
 * 
 *  Ответы контроллера в ПК:
 *    "SMARTBIN_READY" — сигнал готовности при включении питания/перезагрузке
 *    'K'              — цикл открытия/закрытия отсека завершен, бак готов к следующему объекту
 * 
 * =====================================================================================
 */

#include <Servo.h>

// =====================================================================================
// ВЫБОР РЕЖИМА РАБОТЫ МЕХАНИКИ (РАСКОММЕНТИРУЙТЕ НУЖНЫЙ РЕЖИМ):
// =====================================================================================
#define MODE_THREE_SERVOS        // РЕЖИМ 1 (ПО УМОЛЧАНИЮ): 3 сервопривода (по 1 на каждый люк)
//#define MODE_STEPPER_MOTOR     // РЕЖИМ 2: Шаговый двигатель 28BYJ-48 с драйвером ULN2003
//#define MODE_SINGLE_SERVO_CHUTE // РЕЖИМ 3: Один сервопривод поворачивает распределительный лоток

// =====================================================================================
// КОНФИГУРАЦИЯ ПИНОВ И ПАРАМЕТРОВ
// =====================================================================================
const int LED_PIN = 13;          // Встроенный светодиод индикации активности

#ifdef MODE_THREE_SERVOS
  // Пины подключения сервоприводов SG90/MG995
  const int SERVO_PIN_PLASTIC = 9;   // Сервопривод крышки «Пластик»
  const int SERVO_PIN_PAPER   = 10;  // Сервопривод крышки «Бумага»
  const int SERVO_PIN_ORGANIC = 11;  // Сервопривод крышки «Органика»

  // Углы открытия и закрытия крышек
  const int ANGLE_CLOSED = 0;        // Угол закрытого люка (градусы)
  const int ANGLE_OPENED = 95;       // Угол открытого люка (градусы)
  const int LID_HOLD_TIME_MS = 2500; // Время удержания открытого люка (2.5 сек)

  Servo servoPlastic;
  Servo servoPaper;
  Servo servoOrganic;
#endif

#ifdef MODE_STEPPER_MOTOR
  // Пины подключения драйвера ULN2003 для шагового двигателя 28BYJ-48
  const int STEPPER_IN1 = 8;
  const int STEPPER_IN2 = 9;
  const int STEPPER_IN3 = 10;
  const int STEPPER_IN4 = 11;

  const int STEPS_PER_CYCLE = 512;   // Количество шагов (~90 градусов)
  const int STEP_DELAY_US = 1500;    // Скорость вращения (микросекунды между шагами)
  const int HOLD_TIME_MS = 2000;     // Пауза перед возвратом
#endif

#ifdef MODE_SINGLE_SERVO_CHUTE
  const int CHUTE_SERVO_PIN = 9;     // Сервопривод распределительного желоба
  const int ANGLE_CHUTE_PLASTIC = 30; // Наклон влево
  const int ANGLE_CHUTE_PAPER   = 90; // По центру
  const int ANGLE_CHUTE_ORGANIC = 150;// Наклон вправо
  Servo servoChute;
#endif

// =====================================================================================
// ИНИЦИАЛИЗАЦИЯ (SETUP)
// =====================================================================================
void setup() {
  // Инициализация последовательного порта на скорости 9600 бод (согласно C# SerialManager)
  Serial.begin(9600);

  // Светодиод индикации
  pinMode(LED_PIN, OUTPUT);
  digitalWrite(LED_PIN, LOW);

#ifdef MODE_THREE_SERVOS
  servoPlastic.attach(SERVO_PIN_PLASTIC);
  servoPaper.attach(SERVO_PIN_PAPER);
  servoOrganic.attach(SERVO_PIN_ORGANIC);

  // Установка исходного закрытого положения
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
  servoChute.write(ANGLE_CHUTE_PAPER); // Исходное нейтральное положение
#endif

  // Небольшая задержка для стабилизации питания сервоприводов
  delay(300);

  // Отправляем сигнал готовности в приложение C# WPF
  Serial.println(F("SMARTBIN_READY"));
}

// =====================================================================================
// ОСНОВНОЙ ЦИКЛ (LOOP)
// =====================================================================================
void loop() {
  if (Serial.available() > 0) {
    char cmd = Serial.read();

    // Фильтруем символы перевода строки и пробелы
    if (cmd == '\r' || cmd == '\n' || cmd == ' ') {
      return;
    }

    // Включаем светодиод на время обработки
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
        // Неизвестная команда — отправляем подтверждение, чтобы программа не зависла в ожидании
        break;
    }

    // Выключаем светодиод
    digitalWrite(LED_PIN, LOW);

    // КРИТИЧЕСКИ ВАЖНО: Отправляем символ 'K' в ПК!
    // Получив этот байт, C# приложение сбрасывает флаг IsArduinoBusy и разрешает следующий предмет.
    Serial.println('K');
  }
}

// =====================================================================================
// ОБРАБОТЧИКИ ОТСЕКОВ ОТХОДОВ
// =====================================================================================

void handleWastePlastic() {
#ifdef MODE_THREE_SERVOS
  // Открываем люк пластика, ждем и закрываем
  smoothMove(servoPlastic, ANGLE_CLOSED, ANGLE_OPENED, 15);
  delay(LID_HOLD_TIME_MS);
  smoothMove(servoPlastic, ANGLE_OPENED, ANGLE_CLOSED, 15);
#endif

#ifdef MODE_STEPPER_MOTOR
  stepMotor(STEPS_PER_CYCLE, true); // Вперед
  delay(HOLD_TIME_MS);
  stepMotor(STEPS_PER_CYCLE, false); // Назад
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
  // Открываем люк бумаги, ждем и закрываем
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
#endif
}

void handleWasteOrganic() {
#ifdef MODE_THREE_SERVOS
  // Открываем люк органики, ждем и закрываем
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
// ВСПОМОГАТЕЛЬНЫЕ ФУНКЦИИ
// =====================================================================================

#ifdef MODE_THREE_SERVOS
// Плавное перемещение сервопривода (снижает нагрузку на редуктор и исключает удары)
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
// Полношаговый алгоритм управления 28BYJ-48
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
