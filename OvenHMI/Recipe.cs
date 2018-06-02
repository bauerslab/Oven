using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace OvenHMI
{
    public class Recipe
    {
        public ObservableCollection<TemperatureTime> Steps { get; set; } = new ObservableCollection<TemperatureTime>();
    }
    public class TemperatureTime : INotifyPropertyChanged
    {
        /// <summary>String to represent the values in the format "hh:mm - 0000°C"</summary>
        public string FriendlyName => $"{Time:hh\\:mm} - {Temperature:0000°C}";
        /// <summary>Raw value representing time (LSB = 4s)</summary>
        public UInt16 RawTime { get; set; }
        /// <summary>Raw value representing temperature (LSB = 0.25°C)</summary>
        public Int16 RawTemp { get; set; }
        /// <summary>Time since beginning of recipe in seconds to 4s</summary>
        public TimeSpan Time
        {
            get => TimeSpan.FromSeconds(RawTime * 4u);
            set
            {
                if (value.TotalSeconds / 4 > 0xFF00)
                    RawTime = 0xFF00;
                else
                    RawTime = (UInt16)(value.TotalSeconds / 4);

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Time)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FriendlyName)));
                Temperature = Temperature + 1;
                Temperature = Temperature - 1;
            }
        }
        /// <summary>Temperature in degrees Celcius to 0.25°C</summary>
        public float Temperature
        {
            get => RawTemp / 4.0f;
            set
            {
                RawTemp = (Int16)(value * 4);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Temperature)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FriendlyName)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public override string ToString() => FriendlyName;
    }
}
