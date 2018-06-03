#ifndef HMI_H
#define HMI_H
    
#include "project.h"
#define BUFFER_SIZE 64
#define MAX_RECIPE_STEPS ((BUFFER_SIZE - 2)/4)
#define IN_EP 2
#define OUT_EP 3
    
#define ELEMENT_RESISTANCE 17.05 //Ω ±0.01
#define MAX_POWER_OUTPUT 3500 //W
#define THERMAL_RESISTANCE 0.2
#define THERMAL_CAPACITANCE 20000
#define TIME_PERIOD 146 //cycles
#define CYCLE_TIME 1.0 //seconds
    
enum HMI_Command {
    HMI_Command_Start = 1,
    HMI_Command_Stop,
    HMI_Command_StartRecipe,
    HMI_Command_GetCurrentSample,
    HMI_Command_GetStatus,
    HMI_Command_SetAmbient,
    HMI_Command_SetPID,
    HMI_Command_GetPID,
    HMI_Command_EndRecipe = 0xFF,
};
enum HMI_Status {
    HMI_Status_WaitingForRecipe,
    HMI_Status_Standby,
    HMI_Status_Running,
    HMI_Status_Faulted,
    HMI_Status_NeedRestart,
};
struct TempTime
{
    double Temperature;
    double Time;
};

uint8 receiveBuffer[BUFFER_SIZE];
uint8 sendBuffer[BUFFER_SIZE];

double currentTime = 0.0;//s
double currentTemp = 0.0;//°C
double ambientTemperature = -14.0;//°C
uint8 pwm = 0;
float p_gain = 0.0;
float i_gain = 0.0;
float d_gain = 0.0;

enum HMI_Status status = HMI_Status_NeedRestart;

uint8 newDump = 1;
uint16 dumpIndex = 0;
uint8 receivedRecipeSize = 0;
struct TempTime recipe[MAX_RECIPE_STEPS];

uint8 startup = 1;

void HMI_StartInit() {
    HMI_Start(0, HMI_5V_OPERATION);
    currentTime = 0;
    int i = 0;
    for (; i < BUFFER_SIZE; i++)
        receiveBuffer[i] = sendBuffer[i] = 0;
    for (; i < BUFFER_SIZE; i++)
        sendBuffer[i] = 0;
}
void HMI_Update() {
    
    /* Host can send double SET_INTERFACE request. */
    if (0u != HMI_IsConfigurationChanged())
    {
        /* Initialize IN endpoints when device is configured. */
        if (0u != HMI_GetConfiguration())
        {
            /* Enumeration is done, enable OUT endpoint to receive data 
             * from host. */
            HMI_CDC_Init();
        }
    }
    
    //Parse commands from the HMI
    if (HMI_GetConfiguration() && HMI_DataIsReady())
    {
        int receivedBytes = HMI_GetAll(receiveBuffer);
        int i;
        
        switch(receiveBuffer[0])
        {
            case HMI_Command_Start:
                if (status == HMI_Status_Standby)
                {
                    startup = 1;
                    status = HMI_Status_Running;
                }
                
                sendBuffer[0] = status;
                HMI_PutData(sendBuffer, 1);
                break;
            case HMI_Command_Stop:
                if (status == HMI_Status_Running)
                    status = HMI_Status_Standby;
                
                sendBuffer[0] = status;
                HMI_PutData(sendBuffer, 1);
                break;
            case HMI_Command_StartRecipe:
                                                                                    //recipe is 4 bytes per step + 1 start byte + 1 stop byte
                if (receivedBytes >= 10                                             //minimum 2 steps (2*4+2)
                    && receivedBytes <= MAX_RECIPE_STEPS*4+2                        //maximum 15 steps (15*4+2) limited by 64 byte max packet
                    && receivedBytes % 4 == 2                                       //packet size must be start and stop bytes plus a multiple of 4
                    && receiveBuffer[receivedBytes - 1] == HMI_Command_EndRecipe    //check stop byte
                    && status != HMI_Status_Running)                                //don't change recipe while running
                {
                    //parse received recipe
                    receivedRecipeSize = receivedBytes / 4;
                    for(i = 0; i < receivedBytes - 4; i += 4)
                    {
                        recipe[i/4].Time = (receiveBuffer[i + 1] * 0x100 + receiveBuffer[i + 2]) * 4;
                        recipe[i/4].Temperature = (receiveBuffer[i + 3] * 0x100 + receiveBuffer[i + 4]) / 4.0;
                    }
                    
                    //echo back recipe command
                    sendBuffer[0] = HMI_Command_StartRecipe;
                    for (i = 0; i < receivedRecipeSize; i++)
                    {
                        uint16 time = (uint16)(recipe[i].Time / 4);
                        sendBuffer[i*4 + 1] = *((uint8*)(&time) + 1);
                        sendBuffer[i*4 + 2] = *((uint8*)(&time));
                        int16 temp = recipe[i].Temperature * 4;
                        sendBuffer[i*4 + 3] = *((uint8*)(&temp) + 1);
                        sendBuffer[i*4 + 4] = *((uint8*)(&temp));
                    }
                    sendBuffer[4*receivedRecipeSize + 1] = HMI_Command_EndRecipe;
                    HMI_PutData(sendBuffer, 4*receivedRecipeSize + 2);
                    
                    //no longer waiting for recipe
                    status = HMI_Status_Standby;
                }
                else
                {
                    //Reply with some incorrect data so that validation will fail
                    //That way the HMI doesn't have to wait for timeout
                    HMI_PutData(sendBuffer, receivedBytes);
                }
                break;
            case HMI_Command_GetCurrentSample:
                i = 0;
                uint16 time = (uint16)(currentTime < 0 ? 0 : currentTime / 4);
                sendBuffer[i++] = *((uint8*)(&time) + 1);
                sendBuffer[i++] = *((uint8*)(&time));
                int16 curTemp = currentTemp * 4;
                sendBuffer[i++] = *((uint8*)(&curTemp) + 1);
                sendBuffer[i++] = *((uint8*)(&curTemp));
                int16 ambTemp = ambientTemperature * 4;
                sendBuffer[i++] = *((uint8*)(&ambTemp) + 1);
                sendBuffer[i++] = *((uint8*)(&ambTemp));
                sendBuffer[i++] = pwm;
                
                HMI_PutData(sendBuffer, i);
                break;
            case HMI_Command_GetStatus:
                sendBuffer[0] = status;
                HMI_PutData(sendBuffer, 1);
                if (status == HMI_Status_NeedRestart)
                    status = HMI_Status_WaitingForRecipe;
                break;
            case HMI_Command_SetAmbient:
                if (receivedBytes > 2)
                    ambientTemperature = (receiveBuffer[1] * 0x100 + receiveBuffer[2]) / 4.0;
                break;
            case HMI_Command_SetPID:
                if (receivedBytes < 13)
                    break;
                uint8* raw = (uint8*)(&p_gain);
                raw[3] = receiveBuffer[1];
                raw[2] = receiveBuffer[2];
                raw[1] = receiveBuffer[3];
                raw[0] = receiveBuffer[4];
                raw = (uint8*)(&i_gain);
                raw[3] = receiveBuffer[5];
                raw[2] = receiveBuffer[6];
                raw[1] = receiveBuffer[7];
                raw[0] = receiveBuffer[8];
                raw = (uint8*)(&d_gain);
                raw[3] = receiveBuffer[9];
                raw[2] = receiveBuffer[10];
                raw[1] = receiveBuffer[11];
                raw[0] = receiveBuffer[12];
                //nobreak; echo back PID coefficients
            case HMI_Command_GetPID:
                i = 0;
                sendBuffer[i++] = *((uint8*)(&p_gain) + 3);
                sendBuffer[i++] = *((uint8*)(&p_gain) + 2);
                sendBuffer[i++] = *((uint8*)(&p_gain) + 1);
                sendBuffer[i++] = *((uint8*)(&p_gain) + 0);
                sendBuffer[i++] = *((uint8*)(&i_gain) + 3);
                sendBuffer[i++] = *((uint8*)(&i_gain) + 2);
                sendBuffer[i++] = *((uint8*)(&i_gain) + 1);
                sendBuffer[i++] = *((uint8*)(&i_gain) + 0);
                sendBuffer[i++] = *((uint8*)(&d_gain) + 3);
                sendBuffer[i++] = *((uint8*)(&d_gain) + 2);
                sendBuffer[i++] = *((uint8*)(&d_gain) + 1);
                sendBuffer[i++] = *((uint8*)(&d_gain) + 0);
                HMI_PutData(sendBuffer, i);
                break;
        }
    }
}


#endif