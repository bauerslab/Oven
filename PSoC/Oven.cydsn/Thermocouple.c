#include "Thermocouple.h"

#define SAMPLE_SMOOTHING 16

uint8 Converting = 0;
uint8 SampleIndex = 0;
double temp[SAMPLE_SMOOTHING];


void Thermocouple_Start() {
    for (int i = 0; i < SAMPLE_SMOOTHING; i++)
        temp[i] = 0;
}
uint8 Thermocouple_Reset() {
    //Reset pulse
    OneWire_Write(0);
    CyDelayUs(480);
    OneWire_Write(1);
    
    CyDelayUs(60);
    
    //Presence pulse
    uint8 presence = ! OneWire_Read();
    CyDelayUs(200);
    return presence;
}
uint8 Thermocouple_Read1() {
    OneWire_Write(0);
    CyDelayUs(2);
    OneWire_Write(1);
    CyDelayUs(8);
    uint8 value = OneWire_Read();
    CyDelayUs(50);
    return value;
}
uint8 Thermocouple_Read8() {
    uint8 byte = 0, shiftcount;
    for(shiftcount=0; shiftcount < 8; shiftcount++)
        byte |= Thermocouple_Read1() << shiftcount;
    return byte;
}
void Thermocouple_Write1(uint8 bit) {
    OneWire_Write(0);
    CyDelayUs(50 - 40*bit);
    OneWire_Write(1);
    CyDelayUs(10 + 40*bit);
}
void Thermocouple_Write8(uint8 byte) {
    uint8 shiftcount;
    for (shiftcount = 0; shiftcount < 8; shiftcount++)
        Thermocouple_Write1((byte >> shiftcount) & 1);
}
uint8 CRC(uint8* rawData, uint8 count, uint8 compare) {
    return 1;
}

double Thermocouple_GetTemp() {
double output = 0;
    for (int i = 0; i < SAMPLE_SMOOTHING; i++)
        output += temp[i];
    return output / SAMPLE_SMOOTHING;
}
double Thermocouple_Update() {
    if (!Converting)
    {
        //Start Conversion
        Thermocouple_Reset();
        Thermocouple_Write8(0xCC);
        Thermocouple_Write8(0x44);
        Converting = 1;
    }
    //Conversion in progress
    if (!Thermocouple_Read1())
        return Thermocouple_GetTemp();
    Converting = 0;
    
    //Send scratchpad
    Thermocouple_Reset();
    Thermocouple_Write8(0xCC);
    Thermocouple_Write8(0xBE);
    
    uint8 rawData[8];
    int i = 0;
    for(; i < 8; i++)
        rawData[i] = Thermocouple_Read8();
    
    //Check CRC
    if (!(CRC(rawData, 8, Thermocouple_Read8())))
        return Thermocouple_GetTemp();
    
    //Check for fault
    scratchpad.Fault = rawData[0] & 1;
    //if (scratchpad.Fault)
        //return temp;
    
    //Extract temperature from bits
    int16 newTemp = (rawData[0] & ~0b11) >> 2;
    newTemp |= rawData[1] << 6;
    if (newTemp &  0b00010000000000000)
        newTemp |= 0b11110000000000000;
    if (newTemp != 0)
        scratchpad.Temperature = newTemp;
    
    newTemp = (rawData[2] & ~0b1111) >> 4;
    newTemp |= rawData[3] << 4;
    if (newTemp &  0b00000100000000000)
        newTemp |= 0b11111100000000000;
    if (newTemp != 0)
        scratchpad.InternalTemperature = newTemp;
    
    temp[SampleIndex++] = scratchpad.Temperature / 4.0;
    if (SampleIndex >= SAMPLE_SMOOTHING)
        SampleIndex = 0;
    return Thermocouple_GetTemp();
}
double Thermocouple_GetInternal()
{
    return (scratchpad.InternalTemperature / 16.0);
}