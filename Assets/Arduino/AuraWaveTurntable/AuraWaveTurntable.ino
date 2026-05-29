/*
 * AuraWave Turntable Firmware v2.0
 * Enterprise-grade prototype firmware for:
 * - Arduino UNO R3
 * - TB6600 / STEP-DIR driver
 * - NEMA17 (17HS8401S)
 * - Mechanical home switch
 *
 * Features:
 * - Absolute angle positioning
 * - Relative movement
 * - Precision homing
 * - Emergency stop
 * - Motion timeout protection 
 * - Non-dangerous soft limits
 * - Enable/disable driver
 * - AuraWave serial protocol
 *
 * Recommended TB6600 settings:
 * Microstep: 1/16
 * Current: 1.5A
 */

#include <AccelStepper.h>

/* =========================================================
   PIN CONFIGURATION
   ========================================================= */

#define STEP_PIN 2
#define DIR_PIN 3
#define EN_PIN 4
#define HOME_PIN 8

/* =========================================================
   MECHANICAL CONFIGURATION
   ========================================================= */

/*
 * 200 step motor
 * 1/16 microstep
 * 3200 pulses/rev
 */

#define STEPS_PER_REV 3200.0f

:contentReference[oaicite:0]{index=0}

#define STEPS_PER_DEG 8.8889f

/* =========================================================
   SAFETY LIMITS
   ========================================================= */

#define MIN_ANGLE        -360.0f
#define MAX_ANGLE         360.0f

#define MOTION_TIMEOUT_MS 120000UL
#define HOME_TIMEOUT_MS    90000UL
#define CW_SEEK_MAX_MS      8000UL

/* =========================================================
   HOMING SPEEDS
   ========================================================= */

#define SPEED_CW            500
#define SPEED_CCW          -350
#define SPEED_CCW_SLOW     -120
#define SPEED_CW_BACKOFF    180

/* =========================================================
   MOTION CONFIGURATION
   ========================================================= */

#define DEFAULT_SPEED_DPS   15.0f
#define DEFAULT_ACCEL       3000.0f

/* =========================================================
   GLOBAL STATE
   ========================================================= */

AccelStepper stepper(
  AccelStepper::DRIVER,
  STEP_PIN,
  DIR_PIN
);

float currentAngle = 0.0f;
float speedDegPerSec = DEFAULT_SPEED_DPS;

bool homed = false;
volatile bool emergencyStop = false;

/* =========================================================
   UTILITIES
   ========================================================= */

bool estopTriggered() {
  return emergencyStop;
}

void enableDriver() {
  digitalWrite(EN_PIN, LOW);
}

void disableDriver() {
  digitalWrite(EN_PIN, HIGH);
}

long angleToSteps(float angle) {
  return (long)(angle * STEPS_PER_DEG);
}

float stepsToAngle(long steps) {
  return ((float)steps / STEPS_PER_DEG);
}

/* =========================================================
   SETUP
   ========================================================= */

void setup() {

  Serial.begin(115200);

  pinMode(HOME_PIN, INPUT_PULLUP);

  pinMode(EN_PIN, OUTPUT);

  enableDriver();

  stepper.setMaxSpeed(
    speedDegPerSec * STEPS_PER_DEG
  );

  stepper.setAcceleration(DEFAULT_ACCEL);

  Serial.println("OK:BOOT");
}

/* =========================================================
   MAIN LOOP
   ========================================================= */

void loop() {

  if (Serial.available()) {

    String line =
      Serial.readStringUntil('\n');

    line.trim();

    if (line.length() > 0) {
      handleCommand(line);
    }
  }

  if (!emergencyStop) {
    stepper.run();
  }
}

/* =========================================================
   COMMAND HANDLER
   ========================================================= */

void handleCommand(String cmd) {

  if (cmd == "IDENT") {

    Serial.println(
      "OK:AuraWave-Turntable-2.0"
    );

    return;
  }

  /* =========================
     CLEAR ESTOP
     ========================= */

  if (cmd == "CLR_ESTOP") {

    emergencyStop = false;

    enableDriver();

    Serial.println("OK:CLR_ESTOP");

    return;
  }

  /* =========================
     EMERGENCY STOP
     ========================= */

  if (cmd == "ESTOP") {

    emergencyStop = true;

    stepper.stop();

    disableDriver();

    Serial.println("OK:ESTOP");

    return;
  }

  if (emergencyStop) {

    Serial.println("ERR:ESTOP_ACTIVE");

    return;
  }

  /* =========================
     HOME
     ========================= */

  if (cmd == "HOME") {

    homeTurntable();

    return;
  }

  /* =========================
     ABSOLUTE MOVE
     ========================= */

  if (cmd.startsWith("MOVETO:")) {

    float target =
      cmd.substring(7).toFloat();

    moveToAngle(target);

    return;
  }

  /* =========================
     RELATIVE MOVE
     ========================= */

  if (cmd.startsWith("MOVEREL:")) {

    float delta =
      cmd.substring(8).toFloat();

    moveToAngle(currentAngle + delta);

    return;
  }

  /* =========================
     SPEED
     ========================= */

  if (cmd.startsWith("SPEED:")) {

    float dps =
      cmd.substring(6).toFloat();

    if (dps <= 0.0f || dps > 120.0f) {

      Serial.println("ERR:BAD_SPEED");

      return;
    }

    speedDegPerSec = dps;

    stepper.setMaxSpeed(
      speedDegPerSec * STEPS_PER_DEG
    );

    Serial.println("OK:SPEED");

    return;
  }

  /* =========================
     STOP
     ========================= */

  if (cmd == "STOP") {

    stepper.stop();

    while (stepper.distanceToGo() != 0) {
      stepper.run();
    }

    currentAngle =
      stepsToAngle(
        stepper.currentPosition()
      );

    Serial.println("OK:STOP");

    return;
  }

  /* =========================
     POSITION QUERY
     ========================= */

  if (cmd == "POS?") {

    currentAngle =
      stepsToAngle(
        stepper.currentPosition()
      );

    Serial.print("POS:");

    Serial.println(currentAngle, 3);

    return;
  }

  /* =========================
     HOME STATUS
     ========================= */

  if (cmd == "HOMED?") {

    Serial.print("HOMED:");

    Serial.println(homed ? "1" : "0");

    return;
  }

  /* =========================
     ENABLE
     ========================= */

  if (cmd == "ENABLE") {

    enableDriver();

    Serial.println("OK:ENABLE");

    return;
  }

  /* =========================
     DISABLE
     ========================= */

  if (cmd == "DISABLE") {

    disableDriver();

    Serial.println("OK:DISABLE");

    return;
  }

  Serial.println("ERR:UNKNOWN");
}

/* =========================================================
   HOMING
   ========================================================= */

void homeTurntable() {

  if (estopTriggered()) {

    Serial.println("ERR:ESTOP_ABORT");

    return;
  }

  Serial.println("OK:HOME_BEGIN");

  unsigned long t0 = millis();

  homed = false;

  enableDriver();

  /*
   * If already on switch:
   * move away first
   */

  if (digitalRead(HOME_PIN) == LOW) {

    stepper.setSpeed(SPEED_CW_BACKOFF);

    unsigned long tBack = millis();

    while (
      digitalRead(HOME_PIN) == LOW &&
      millis() - tBack < 4000
    ) {

      if (estopTriggered()) {

        Serial.println("ERR:ESTOP_ABORT");

        return;
      }

      stepper.runSpeed();
    }
  }

  /*
   * Fast CW seek
   */

  Serial.println("OK:HOME_CW");

  stepper.setSpeed(SPEED_CW);

  unsigned long tCw = millis();

  while (
    digitalRead(HOME_PIN) == HIGH &&
    millis() - tCw < CW_SEEK_MAX_MS
  ) {

    if (estopTriggered()) {

      Serial.println("ERR:ESTOP_ABORT");

      return;
    }

    stepper.runSpeed();

    if (millis() - t0 > HOME_TIMEOUT_MS) {

      Serial.println("ERR:HOME_TIMEOUT");

      return;
    }
  }

  /*
   * Precision reverse approach
   */

  Serial.println("OK:HOME_CCW");

  stepper.setSpeed(SPEED_CCW);

  while (digitalRead(HOME_PIN) == HIGH) {

    if (estopTriggered()) {

      Serial.println("ERR:ESTOP_ABORT");

      return;
    }

    stepper.runSpeed();

    if (millis() - t0 > HOME_TIMEOUT_MS) {

      Serial.println("ERR:HOME_TIMEOUT");

      return;
    }
  }

  /*
   * Back off slightly
   */

  stepper.setSpeed(SPEED_CW_BACKOFF);

  for (int i = 0; i < 200; i++) {

    if (digitalRead(HOME_PIN) == HIGH) {
      break;
    }

    stepper.runSpeed();
  }

  /*
   * Slow precision engage
   */

  stepper.setSpeed(SPEED_CCW_SLOW);

  while (digitalRead(HOME_PIN) == HIGH) {

    if (estopTriggered()) {

      Serial.println("ERR:ESTOP_ABORT");

      return;
    }

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

/* =========================================================
   MOTION
   ========================================================= */

void moveToAngle(float targetAngle) {

  if (estopTriggered()) {

    Serial.println("ERR:ESTOP_ABORT");

    return;
  }

  if (!homed) {

    Serial.println("ERR:NOT_HOMED");

    return;
  }

  /*
   * Soft limits
   */

  if (
    targetAngle < MIN_ANGLE ||
    targetAngle > MAX_ANGLE
  ) {

    Serial.println("ERR:LIMIT");

    return;
  }

  enableDriver();

  long targetSteps =
    angleToSteps(targetAngle);

  stepper.moveTo(targetSteps);

  unsigned long t0 = millis();

  while (stepper.distanceToGo() != 0) {

    if (estopTriggered()) {

      stepper.stop();

      Serial.println("ERR:ESTOP_ABORT");

      return;
    }

    stepper.run();

    if (millis() - t0 > MOTION_TIMEOUT_MS) {

      stepper.stop();

      Serial.println("ERR:MOTION_TIMEOUT");

      return;
    }
  }

  currentAngle =
    stepsToAngle(
      stepper.currentPosition()
    );

  Serial.println("OK:DONE");
}