const byte phases[4][4] = {
  {1, 1, 0, 0},
  {0, 1, 1, 0},
  {0, 0, 1, 1},
  {1, 0, 0, 1}
};

const int pins[3][4] = {
  {8, 10, 9, 11},    // 0: P - пластик
  {12, 13, 14, 15},  // 1: B - бумага
  {16, 17, 18, 19}   // 2: O - органика (био)
};

// Пины для цветов (совпадают по порядку с индексами моторов 0, 1, 2):
// Пин 5 = Красный (P - пластик)
// Пин 6 = Зеленый (B - бумага)
// Пин 7 = Синий   (O - био/органика)
const int ledPins[3] = {5, 6, 7};

void setup() {
  Serial.begin(9600);
  for (int m = 0; m < 3; m++) {
    for (int i = 0; i < 4; i++) {
      pinMode(pins[m][i], OUTPUT);
    }
    pinMode(ledPins[m], OUTPUT);
    digitalWrite(ledPins[m], LOW); // Все цвета выключены на старте
  }
}

void rotate(int m, int steps, bool forward) {
  for (int s = 0; s < steps; s++) {
    int p = forward ? (s % 4) : (3 - (s % 4));
    for (int i = 0; i < 4; i++) {
      digitalWrite(pins[m][i], phases[p][i]);
    }
    delay(3);
  }
  for (int i = 0; i < 4; i++) {
    digitalWrite(pins[m][i], LOW);
  }
}

void openLid(int m) {
  digitalWrite(ledPins[m], HIGH); // Включаем нужный цвет (Красный, Зеленый или Синий)
  
  rotate(m, 512, true);
  delay(2000);
  rotate(m, 512, false);
  
  digitalWrite(ledPins[m], LOW);  // Гасим светодиод после закрытия крышки
  Serial.println("K");
}

void loop() {
  if (!Serial.available()) return;
  char c = Serial.read();
  if (c == 'P') openLid(0);      // Пластик -> Мотор 0 + Красный (пин 5)
  else if (c == 'B') openLid(1); // Бумага  -> Мотор 1 + Зеленый (пин 6)
  else if (c == 'O') openLid(2); // Био     -> Мотор 2 + Синий   (пин 7)
}