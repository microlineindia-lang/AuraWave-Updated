/*
========================================================
 AuraWave Professional Turntable Controller
========================================================

AUTHOR
--------------------------------------------------------
AuraWave Motion Control System

HARDWARE
--------------------------------------------------------
- Arduino UNO R3
- TB6600 Stepper Driver
- NEMA17 Stepper Motor
- 24V DC Power Supply

PIN CONFIGURATION
--------------------------------------------------------
D2  -> STEP
D3  -> DIRECTION
D4  -> ENABLE

SERIAL COMMAND FORMAT
--------------------------------------------------------

ROTATION COMMANDS
--------------------------------------------------------
CW,angle,count
CCW,angle,count

EXAMPLES
--------------------------------------------------------
CW,90,4
CCW,45,8
CW,1.8,200
CW,360,1

SYSTEM COMMANDS
--------------------------------------------------------
ESTOP      -> Immediate emergency stop
RESET      -> Clear emergency stop
SETHOME    -> Set current position as HOME (0°)
HOME       -> Return to HOME position
STATUS     -> Print controller status

CALIBRATION
--------------------------------------------------------
Empirically calibrated:
0.45° mechanical rotation per pulse

========================================================
*/


// =====================================================
// PIN DEFINITIONS
// =====================================================

const uint8_t STEP_PIN = 2;
const uint8_t DIR_PIN  = 3;
const uint8_t EN_PIN   = 4;


// =====================================================
// SYSTEM CALIBRATION
// =====================================================

// REAL CALIBRATED ANGLE PER STEP PULSE
const float DEGREES_PER_PULSE = 0.45f;


// =====================================================
// MOTION PARAMETERS
// =====================================================

// STEP PULSE WIDTH
// Lower value = faster rotation
const uint16_t PULSE_DELAY_US = 1000;

// PAUSE BETWEEN MOVEMENTS
const uint16_t WAIT_TIME_MS = 2000;


// =====================================================
// SYSTEM STATE
// =====================================================

bool motorBusy = false;

// SOFTWARE EMERGENCY STOP
bool emergencyStop = false;

// CURRENT ABSOLUTE POSITION
float currentAngle = 0.0f;

// SERIAL INPUT BUFFER
String serialBuffer = "";


// =====================================================
// INITIALIZATION
// =====================================================

void setup()
{
    Serial.begin(115200);

    pinMode(STEP_PIN, OUTPUT);
    pinMode(DIR_PIN, OUTPUT);
    pinMode(EN_PIN, OUTPUT);

    digitalWrite(STEP_PIN, LOW);
    digitalWrite(DIR_PIN, LOW);

    // DRIVER DISABLED AT STARTUP
    disableDriver();

    Serial.println();
    Serial.println("====================================");
    Serial.println(" AURAWAVE MOTION CONTROLLER READY");
    Serial.println("====================================");
    Serial.println();

    Serial.println("AVAILABLE COMMANDS:");
    Serial.println("CW,angle,count");
    Serial.println("CCW,angle,count");
    Serial.println("ESTOP");
    Serial.println("RESET");
    Serial.println("SETHOME");
    Serial.println("HOME");
    Serial.println("STATUS");
    Serial.println();
}


// =====================================================
// MAIN EXECUTION LOOP
// =====================================================

void loop()
{
    processSerialInput();
}


// =====================================================
// DRIVER CONTROL
// =====================================================

void enableDriver()
{
    // TB6600 ENABLE
    digitalWrite(EN_PIN, HIGH);
}

void disableDriver()
{
    // TB6600 DISABLE
    // Removes holding current from motor coils
    digitalWrite(EN_PIN, LOW);
}


// =====================================================
// EMERGENCY STOP CHECK
// =====================================================

bool checkEmergencyStop()
{
    if (emergencyStop)
    {
        disableDriver();

        Serial.println("EMERGENCY STOP ACTIVE");

        return true;
    }

    return false;
}


// =====================================================
// SERIAL PROCESSING
// =====================================================

void processSerialInput()
{
    while (Serial.available())
    {
        char incoming = Serial.read();

        // END OF COMMAND
        if (incoming == '\n' || incoming == '\r')
        {
            if (serialBuffer.length() > 0)
            {
                processCommand(serialBuffer);

                serialBuffer = "";
            }
        }
        else
        {
            serialBuffer += incoming;

            // SIMPLE BUFFER PROTECTION
            if (serialBuffer.length() > 64)
            {
                serialBuffer = "";

                Serial.println("ERROR:BUFFER_OVERFLOW");
            }
        }
    }
}


// =====================================================
// NON‑BLOCKING WAIT THAT CHECKS FOR ESTOP
// =====================================================
void waitBetweenMoves()
{
    unsigned long waitStart = millis();
    String localBuffer = "";  // local buffer, not affected by global serialBuffer

    while (millis() - waitStart < WAIT_TIME_MS)
    {
        // Check for incoming serial commands during the wait
        while (Serial.available())
        {
            char c = Serial.read();
            localBuffer += c;

            if (c == '\n' || c == '\r')
            {
                localBuffer.trim();
                localBuffer.toUpperCase();

                if (localBuffer == "ESTOP")
                {
                    emergencyStop = true;
                    disableDriver();
                    motorBusy = false;

                    Serial.println();
                    Serial.println("================================");
                    Serial.println("    EMERGENCY STOP TRIGGERED    ");
                    Serial.println("================================");

                    // Clear the global buffer to avoid confusing the main loop
                    serialBuffer = "";
                    return;  // immediately exit the wait
                }

                localBuffer = ""; // discard any other command during wait
            }
        }

        delay(1); // small yield
    }
}

// =====================================================
// RETURN TO HOME FUNCTION
// =====================================================

void returnToHome()
{
    Serial.println();
    Serial.println("RETURNING HOME");

    // ALREADY AT HOME
    if (currentAngle == 0.0f)
    {
        Serial.println("ALREADY AT HOME");
        return;
    }

    float moveAngle;
    bool clockwise;

    // SHORTEST ROTATION PATH
    if (currentAngle <= 180.0f)
    {
        moveAngle = currentAngle;

        // ROTATE CCW BACK TO ZERO
        clockwise = false;
    }
    else
    {
        moveAngle = 360.0f - currentAngle;

        // ROTATE CW BACK TO ZERO
        clockwise = true;
    }

    uint32_t pulses =
        round(moveAngle / DEGREES_PER_PULSE);

    motorBusy = true;

    digitalWrite(
        DIR_PIN,
        clockwise ? HIGH : LOW);

    enableDriver();

    generatePulses(pulses);

    disableDriver();

    // IF ESTOP OCCURRED
    if (emergencyStop)
    {
        motorBusy = false;
        return;
    }

    currentAngle = 0.0f;

    Serial.println("HOME REACHED");

    motorBusy = false;
}


// =====================================================
// COMMAND EXECUTION
// =====================================================

void processCommand(String command)
{
    command.trim();

    // CONVERT TO UPPERCASE
    command.toUpperCase();

    // -------------------------------------------------
    // SOFTWARE EMERGENCY STOP
    // -------------------------------------------------

    if (command == "ESTOP")
    {
        emergencyStop = true;

        disableDriver();

        motorBusy = false;

        Serial.println();
        Serial.println("================================");
        Serial.println(" EMERGENCY STOP TRIGGERED");
        Serial.println("================================");

        return;
    }

    // -------------------------------------------------
    // RESET SYSTEM
    // -------------------------------------------------

    if (command == "RESET")
    {
        emergencyStop = false;

        Serial.println();
        Serial.println("SYSTEM RESET");

        return;
    }

    // -------------------------------------------------
    // BLOCK MOTION DURING ESTOP
    // -------------------------------------------------

    if (emergencyStop)
    {
        Serial.println("ERROR:ESTOP_ACTIVE");

        return;
    }

    // -------------------------------------------------
    // SET CURRENT POSITION AS HOME
    // -------------------------------------------------

    if (command == "SETHOME")
    {
        currentAngle = 0.0f;

        Serial.println();
        Serial.println("HOME POSITION SET");

        return;
    }

    // -------------------------------------------------
    // RETURN TO HOME POSITION
    // -------------------------------------------------

    if (command == "HOME")
    {
        returnToHome();
        return;
    }

    // -------------------------------------------------
    // SYSTEM STATUS
    // -------------------------------------------------

    if (command == "STATUS")
    {
        Serial.println();
        Serial.println("===== SYSTEM STATUS =====");

        Serial.print("Position= ");
        Serial.print(currentAngle, 2);
        Serial.println(" Degree");

        Serial.print("Emergency Stop= ");
        Serial.println(emergencyStop ? "Active" : "Clear");

        Serial.print("Motor= ");
        Serial.println(motorBusy ? "Busy" : "Idle");

        Serial.println("=========================");

        return;
    }

    // -------------------------------------------------
    // PREVENT OVERLAPPING COMMANDS
    // -------------------------------------------------

    if (motorBusy)
    {
        Serial.println("ERROR:MOTOR_BUSY");
        return;
    }

    // -------------------------------------------------
    // FIND COMMAND DELIMITERS
    // -------------------------------------------------

    int comma1 = command.indexOf(',');
    int comma2 = command.indexOf(',', comma1 + 1);

    if (comma1 == -1 || comma2 == -1)
    {
        Serial.println("ERROR:INVALID_FORMAT");
        return;
    }

    // -------------------------------------------------
    // PARSE COMMAND
    // -------------------------------------------------

    String direction =
        command.substring(0, comma1);

    float angle =
        command.substring(
            comma1 + 1,
            comma2).toFloat();

    int count =
        command.substring(
            comma2 + 1).toInt();

    // -------------------------------------------------
    // VALIDATE DIRECTION
    // -------------------------------------------------

    bool clockwise = false;

    if (direction == "CW")
    {
        clockwise = true;
    }
    else if (direction == "CCW")
    {
        clockwise = false;
    }
    else
    {
        Serial.println("ERROR:INVALID_DIRECTION");

        return;
    }

    // -------------------------------------------------
    // VALIDATE ANGLE
    // -------------------------------------------------

    if (angle <= 0.0f || angle > 360.0f)
    {
        Serial.println("ERROR:INVALID_ANGLE");

        return;
    }

    // -------------------------------------------------
    // VALIDATE COUNT
    // -------------------------------------------------

    if (count <= 0)
    {
        Serial.println("ERROR:INVALID_COUNT");

        return;
    }

    // -------------------------------------------------
    // CALCULATE REQUIRED PULSES
    // -------------------------------------------------

    uint32_t pulses =
        round(angle / DEGREES_PER_PULSE);

    // -------------------------------------------------
    // MOTION START
    // -------------------------------------------------

    motorBusy = true;

    // SET ROTATION DIRECTION
    digitalWrite(
        DIR_PIN,
        clockwise ? HIGH : LOW);

    // -------------------------------------------------
    // TELEMETRY
    // -------------------------------------------------

    Serial.println();
    Serial.println("================================");
    Serial.println("              START             ");
    Serial.println("================================");

    Serial.print("Direction= ");
    Serial.println(clockwise? "Clockwise": "Counter Clockwise"
);

    Serial.print("Step Angle= ");
    Serial.print(angle, 3);
    Serial.println(" Degree");

    Serial.print("Move Count= ");
    Serial.println(count);

    float totalSweep = count * angle;


    // Serial.print("PULSES_PER_MOVE=");
    // Serial.println(pulses);

    Serial.println();

    // -------------------------------------------------
    // EXECUTE MOVEMENTS
    // -------------------------------------------------

    for (int move = 0; move < count; move++)
    {
        // ENABLE DRIVER ONLY DURING ACTIVE MOTION
        enableDriver();

        // ---------------------------------------------
        // GENERATE STEP PULSES
        // ---------------------------------------------

        generatePulses(pulses);

        // ---------------------------------------------
        // IF ESTOP OCCURRED
        // ---------------------------------------------

        if (emergencyStop)
        {
            motorBusy = false;
            return;
        }

        // ---------------------------------------------
        // DISABLE DRIVER AFTER MOVEMENT
        // ---------------------------------------------

        disableDriver();

        // ---------------------------------------------
        // UPDATE ABSOLUTE POSITION
        // ---------------------------------------------

        if (clockwise)
        {
            currentAngle += angle;
        }
        else
        {
            currentAngle -= angle;
        }

        // NORMALIZE POSITION
        while (currentAngle >= 360.0f)
        {
            currentAngle -= 360.0f;
        }

        while (currentAngle < 0.0f)
        {
            currentAngle += 360.0f;
        }

        // ---------------------------------------------
        // POSITION TELEMETRY
        // ---------------------------------------------

        Serial.print("Current Position= #");
        Serial.print(move+1);
        Serial.print(" ");
        Serial.print(currentAngle, 2);
        Serial.print(" Degree of ");
        Serial.print(totalSweep, 2);
        Serial.println(" Degree");


        // ---------------------------------------------
        // NON‑BLOCKING WAIT BETWEEN MOVEMENTS (checks for ESTOP)
        // ---------------------------------------------

        if (move < count - 1)
        {
            waitBetweenMoves();

            // If ESTOP was triggered during the wait, exit the whole loop
            if (emergencyStop)
            {
                motorBusy = false;
                return;
            }
        }
    }

    // -------------------------------------------------
    // MOVEMENT COMPLETE
    // -------------------------------------------------

    Serial.println();
    Serial.println("================================");
    Serial.println("            COMPLETE            ");
    Serial.println("================================");

    motorBusy = false;
}


// =====================================================
// STEP PULSE GENERATOR
// =====================================================

void generatePulses(uint32_t pulseCount)
{
    for (uint32_t i = 0; i < pulseCount; i++)
    {
        // ============================================
        // CHECK FOR INCOMING SERIAL DATA DURING MOTION
        // ============================================

        while (Serial.available())
        {
            char incoming = Serial.read();

            serialBuffer += incoming;

            // COMMAND COMPLETE
            if (incoming == '\n' || incoming == '\r')
            {
                serialBuffer.trim();
                serialBuffer.toUpperCase();

                // SOFTWARE EMERGENCY STOP
                if (serialBuffer == "ESTOP")
                {
                    emergencyStop = true;

                    disableDriver();

                    motorBusy = false;

                    Serial.println();
                    Serial.println("================================");
                    Serial.println("    EMERGENCY STOP TRIGGERED    ");
                    Serial.println("================================");
                }

                serialBuffer = "";
            }
        }

        // ============================================
        // IMMEDIATE STOP
        // ============================================

        if (emergencyStop)
        {
            disableDriver();
            return;
        }

        // ============================================
        // STEP PULSE
        // ============================================

        digitalWrite(STEP_PIN, HIGH);
        delayMicroseconds(PULSE_DELAY_US);

        digitalWrite(STEP_PIN, LOW);
        delayMicroseconds(PULSE_DELAY_US);
    }
}