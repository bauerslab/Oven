#include "project.h"
#include "Thermocouple.h"
#include "HMI.h"
#include <math.h>


int main(void)
{
    CyGlobalIntEnable; /* Enable global interrupts. */
    
    uint32 currentCycle = 0;
    uint8 elementStopped = 0;
    double timePeriodPower[TIME_PERIOD];
    double integratedTempError = 0.0;
    double lastTemperatureError = 0.0;
    uint8 predictionRecipeStep = 1;
    uint8 currentRecipeStep = 1;
        
    HMI_StartInit();
    RecipeTimer_Start();
    Thermocouple_Start();

    for(;;)
    {
        currentTemp = Thermocouple_Update();
        
        //Update current time from RecipeTimer
        uint32 counter = __UINT32_MAX__ - RecipeTimer_ReadCounter();
        //Divide by clock rate to get seconds
        currentTime = ((double)counter) / 600.0;
        //offset by the prediction interval so that the recipe begins when we will have had input
        currentTime -= TIME_PERIOD*CYCLE_TIME;

        //Get commands from HMI
        HMI_Update();
        
        if (status == HMI_Status_Running)
        {
            if (startup)
            {
                //Do startup sequence first loop only
                startup = 0;
                predictionRecipeStep = 1;
                currentRecipeStep = 1;
                integratedTempError = 0.0;
                lastTemperatureError = 0.0;
                
                //Engage safety relay
                ElementEnable_Write(1);
                CyDelay(150);//Wait for relay to connect to avoid arcing
                
                //reset recipe timer
                RecipeTimer_WriteCounter(__UINT32_MAX__);
                
                //clear laggy power output buffer
                for(currentCycle = 0; currentCycle < TIME_PERIOD; currentCycle++)
                    timePeriodPower[currentCycle] = 0.0;
                currentCycle = -1;
            }
            else if (currentCycle != (uint32)(currentTime / CYCLE_TIME + TIME_PERIOD*CYCLE_TIME))
            {
                currentCycle = (uint32)(currentTime / CYCLE_TIME + TIME_PERIOD*CYCLE_TIME);
                //predict one time period ahead to account for system latency
                double predictionTime = currentTime + TIME_PERIOD*CYCLE_TIME;
                
                //determine current step in recipe
                while (currentRecipeStep < receivedRecipeSize && currentTime > recipe[currentRecipeStep].Time)
                    currentRecipeStep++;
                
                //determine step in recipe needed for prediction
                while (predictionTime > recipe[predictionRecipeStep].Time)
                {
                    predictionRecipeStep++;
                    if (predictionRecipeStep >= receivedRecipeSize)
                    {
                        //Finished recipe, time to stop
                        predictionRecipeStep = 0;
                        status = HMI_Status_Standby;
                        break;
                    }
                }
                if (predictionRecipeStep > 0)
                {
                    //calculate desired temperature at prediction time from recipe
                    double totalStepTime = recipe[predictionRecipeStep].Time - recipe[predictionRecipeStep - 1].Time;
                    double stepRatio = (predictionTime - recipe[predictionRecipeStep - 1].Time) / totalStepTime;
                    double desiredTemperature = (recipe[predictionRecipeStep].Temperature * stepRatio) + (recipe[predictionRecipeStep - 1].Temperature * (1 - stepRatio));
                    double desiredTemperatureChange = desiredTemperature - currentTemp;
                    
                    //calculate current error for PID
                    double currentTemperatureError;
                    if (currentTime > 0)
                    {
                        totalStepTime = recipe[currentRecipeStep].Time - recipe[currentRecipeStep - 1].Time;
                        stepRatio = (currentTime - recipe[currentRecipeStep - 1].Time) / totalStepTime;
                        double currentRecipeTemperature = (recipe[currentRecipeStep].Temperature * stepRatio) + (recipe[currentRecipeStep - 1].Temperature * (1 - stepRatio));
                        currentTemperatureError = currentRecipeTemperature - currentTemp;
                    }
                    else
                        currentTemperatureError = recipe[0].Temperature - currentTemp;
                        
                    //calculate new temperature offset using PID
                    integratedTempError += currentTemperatureError;
                    double derivativeTempError = currentTemperatureError - lastTemperatureError;
                    lastTemperatureError = currentTemperatureError;
                    double pidTemperatureError = p_gain*currentTemperatureError + i_gain*integratedTempError + d_gain*derivativeTempError;
                    
                    //calculate energy not counted for in the current temperature due to latency
                    double laggyEnergy = 0.0;
                    int i = 0;
                    for(;i < TIME_PERIOD; i++)
                        laggyEnergy += timePeriodPower[i];
                    laggyEnergy *= CYCLE_TIME;
                        
                    double futureAmbientDifference = desiredTemperature - ambientTemperature;
                    
                    //calculate desired output power using thermal model
                    double netOutputEnergy = desiredTemperatureChange*THERMAL_CAPACITANCE - laggyEnergy;
                    double maintenencePower = futureAmbientDifference/THERMAL_RESISTANCE;
                    double pidPower = pidTemperatureError*THERMAL_CAPACITANCE/(TIME_PERIOD*CYCLE_TIME) + pidTemperatureError/THERMAL_RESISTANCE;
                    double outputPower = netOutputEnergy/CYCLE_TIME + maintenencePower + pidPower;
                        
                    //clamp output power to known capability
                    if (outputPower < 0)
                        outputPower = 0;
                    if (outputPower > MAX_POWER_OUTPUT)
                        outputPower = MAX_POWER_OUTPUT;
                                    
                    //Set PWM on Solid State Relay
                    pwm = outputPower * 120 / MAX_POWER_OUTPUT;
                    if (pwm > (Element_ReadPeriod() + 1))
                        pwm = (Element_ReadPeriod() + 1);
                    if (pwm == 0)
                    {
                        Element_Stop();
                        elementStopped = 1;
                    }
                    else
                    {
                        if (elementStopped)
                        {
                            Element_Start();
                            elementStopped = 0;
                        }
                        Element_WriteCompare(pwm - 1);
                    }
                    
                    //calculate actual power output after PWM aliasing
                    outputPower = pwm * MAX_POWER_OUTPUT / 120 - pidPower;//Keep power from PID out of the model

                    //keep track of energy output to compensate for system latency
                    timePeriodPower[currentCycle % TIME_PERIOD] = outputPower;
                    
                    //TODO: Check for Solid State Relay failure (Thermal runaway)
                }
            }
            
        }
        else
        {
            //Disengage Solid State Relay
            Element_Stop();
            elementStopped = 1;
            Element_WriteCompare(0);
            if (!startup)
            {
                //Wait a couple of PWM clocks for SSR to clear
                CyDelay(16);
            }
            //Disengage Safety Relay
            ElementEnable_Write(0);
            
            pwm = 0;
            startup = 1;
        }
    }
        
}
