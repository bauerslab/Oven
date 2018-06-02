using System;

namespace OvenHMI
{
    public class Sample
    {
        public UInt16 RawTime { get; set; }
        public Int16 RawTemp { get; set; }
        public Int16 RawAmbient { get; set; }
        public byte RawOutput { get; set; }
        /// <summary>
        /// Time in seconds since beginning of recipe
        /// </summary>
        public TimeSpan Time => TimeSpan.FromSeconds(RawTime * 4u);
        /// <summary>
        /// Temperature in degrees Celcius to 0.25°C
        /// </summary>
        public float Temperature => RawTemp * 0.25f;
        /// <summary>
        /// The power output as estimated by the set PWM duty cycle
        /// </summary>
        public float Power => (Oven.MaxPower * RawOutput) / Oven.PWMSteps;
        /// <summary>
        /// Calculated ambient temperature in degrees Celcius to 0.25°C
        /// </summary>
        public float Ambient => RawAmbient * 0.25f;
        /// <summary>
        /// The real time clock value of when the sample was received.
        /// </summary>
        public DateTime RealTime { get; set; } = DateTime.UtcNow;
    }
}
