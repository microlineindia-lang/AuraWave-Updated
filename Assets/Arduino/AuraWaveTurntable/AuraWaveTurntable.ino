/*
 * AuraWave Turntable Firmware v1.2
 * NEMA 17 + L298N — prototype-aligned homing (CW then CCW to reference sensor).
 */
#include <AccelStepper.h>

#define STEP_PIN 3
#define DIR_PIN 4
#define HOME_PIN 2
#define STEPS_PER_DEG 40.0f

#define SPEED_CW  350
#define SPEED_CCW -280
#define SPEED_CCW_SLOW -120
#define SPEED_CW_BACKOFF 180

#define HOME_TIMEOUT_MS 90000
#define CW_SEEK_MAX_MS 8000

AccelStepper stepper(AccelStepper::DRIVER, STEP_PIN, DIR_PIN);
float currentAngle = 0.0f;
float speedDegPerSec = 10.0f;
bool homed = false;
volatile bool emergencyStop = false;

bool estopTriggered() {
  return emergencyStop;
}

void setup() {
  Serial.begin(115200);
  pinMode(HOME_PIN, INPUT_PULLUP);
  stepper.setMaxSpeed(8000);
  stepper.setAcceleration(2000);
  Serial.println("OK:BOOT");
}

void loop() {
  if (Serial.available()) {
    String line = Serial.readStringUntil('\n');
    line.trim();
    handleCommand(line);
  }
  if (!emergencyStop) {
    stepper.run();
  }
}

void handleCommand(String cmd) {
  if (cmd == "IDENT") {
    Serial.println("OK:AuraWave-Turntable-1.2");
    return;
  }
  if (cmd == "CLR_ESTOP") {
    emergencyStop = false;
    Serial.println("OK:CLR_ESTOP");
    return;
  }
  if (cmd == "ESTOP") {
    emergencyStop = true;
    stepper.stop();
    stepper.setSpeed(0);
    stepper.setCurrentPosition(stepper.currentPosition());
    Serial.println("OK:ESTOP");
    return;
  }
  if (emergencyStop) {
    Serial.println("ERR:ESTOP_ACTIVE");
    return;
  }
  if (cmd == "HOME") {
    homeTurntable();
    return;
  }
  if (cmd.startsWith("MOVETO:")) {
    moveToAngle(cmd.substring(7).toFloat());
    return;
  }
  if (cmd.startsWith("MOVEREL:")) {
    moveToAngle(currentAngle + cmd.substring(8).toFloat());
    return;
  }
  if (cmd.startsWith("SPEED:")) {
    speedDegPerSec = cmd.substring(6).toFloat();
    stepper.setMaxSpeed(speedDegPerSec * STEPS_PER_DEG);
    Serial.println("OK");
    return;
  }
  if (cmd == "STOP") {
    stepper.stop();
    stepper.setCurrentPosition(stepper.currentPosition());
    Serial.println("OK");
    return;
  }
  if (cmd == "POS?") {
    Serial.print("POS:");
    Serial.println(currentAngle, 3);
    return;
  }
  Serial.println("ERR:UNKNOWN");
}

void homeTurntable() {
  if (estopTriggered()) {
    Serial.println("ERR:ESTOP_ABORT");
    return;
  }

  unsigned long t0 = millis();
  homed = false;

  if (digitalRead(HOME_PIN) == LOW) {
    stepper.setSpeed(SPEED_CW_BACKOFF);
    unsigned long tBack = millis();
    while (digitalRead(HOME_PIN) == LOW && (millis() - tBack < 4000)) {
      if (estopTriggered()) { Serial.println("ERR:ESTOP_ABORT"); return; }
      stepper.runSpeed();
    }
  }

  Serial.println("OK:HOME_CW");
  stepper.setSpeed(SPEED_CW);
  unsigned long tCw = millis();
  while (digitalRead(HOME_PIN) == HIGH && (millis() - tCw < CW_SEEK_MAX_MS)) {
    if (estopTriggered()) { Serial.println("ERR:ESTOP_ABORT"); return; }
    stepper.runSpeed();
    if (millis() - t0 > HOME_TIMEOUT_MS) {
      Serial.println("ERR:HOME_TIMEOUT");
      return;
    }
  }

  Serial.println("OK:HOME_CCW");
  stepper.setSpeed(SPEED_CCW);
  while (digitalRead(HOME_PIN) == HIGH) {
    if (estopTriggered()) { Serial.println("ERR:ESTOP_ABORT"); return; }
    stepper.runSpeed();
    if (millis() - t0 > HOME_TIMEOUT_MS) {
      Serial.println("ERR:HOME_TIMEOUT");
      return;
    }
  }

  stepper.setSpeed(SPEED_CW_BACKOFF);
  for (int i = 0; i < 160; i++) {
    if (estopTriggered()) { Serial.println("ERR:ESTOP_ABORT"); return; }
    if (digitalRead(HOME_PIN) == HIGH) break;
    stepper.runSpeed();
  }

  stepper.setSpeed(SPEED_CCW_SLOW);
  while (digitalRead(HOME_PIN) == HIGH) {
    if (estopTriggered()) { Serial.println("ERR:ESTOP_ABORT"); return; }
    stepper.runSpeed();
    if (millis() - t0 > HOME_TIMEOUT_MS) {
      Serial.println("ERR:HOME_TIMEOUT");
      return;
    }
  }

  stepper.stop();
  stepper.setCurrentPosition(0);
  currentAngle = 0.0f;
  homed = true;
  Serial.println("OK:HOMED");
}

void moveToAngle(float target) {
  if (estopTriggered()) {
    Serial.println("ERR:ESTOP_ABORT");
    return;
  }

  long steps = (long)((target - currentAngle) * STEPS_PER_DEG);
  stepper.move(steps);
  while (stepper.distanceToGo() != 0) {
    if (estopTriggered()) {
      stepper.stop();
      stepper.setCurrentPosition(stepper.currentPosition());
      Serial.println("ERR:ESTOP_ABORT");
      return;
    }
    stepper.run();
  }
  currentAngle = target;
  stepper.setCurrentPosition((long)(currentAngle * STEPS_PER_DEG));
  Serial.println("OK:DONE");
}
