#ifndef THERMOCOUPLE_H
#define THERMOCOUPLE_H

#include "project.h"
    
struct Thermocouple_Scratchpad
{
    int16 Temperature;
    int16 InternalTemperature;
    char Fault;
};
volatile struct Thermocouple_Scratchpad scratchpad;
void Thermocouple_Start();
uint8 Thermocouple_Reset();
uint8 Thermocouple_Read1();
uint8 Thermocouple_Read8();
void Thermocouple_Write1(uint8 byte);
void Thermocouple_Write8(uint8 byte);
uint8 CRC(uint8* buffer, uint8 count, uint8 compare);
double Thermocouple_Update();
double Thermocouple_GetInternal();

#endif