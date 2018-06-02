using Syncfusion.UI.Xaml.Controls.Input;
using System;

namespace OvenHMI
{
    static class Helper
    {
        public static bool TryValueTimeSpan(this SfTimePicker control, out TimeSpan value)
        {
            value = default(TimeSpan);
            if (control.Value is TimeSpan newTimeSpan)
                value = newTimeSpan;
            else if (control.Value is DateTime newDateTime)
                value = newDateTime.TimeOfDay;
            else if (control.Value is null)
                return false;
            else
                throw new Exception("Unknown value type");
            return true;
        }
        public static bool TryValueFloat(this SfNumericUpDown control, out float value)
        {
            value = float.NaN;
            if (control.Value is string s)
            {
                if (float.TryParse(s, out float sf))
                    value = sf;
                else
                    return false;
            }
            else if (control.Value is decimal c)
                value = (float)c;
            else if (control.Value is double d)
                value = (float)d;
            else if (control.Value is float f)
                value = f;
            else if (control.Value is int i)
                value = i;
            else if (control.Value is null)
                return false;
            else
                throw new Exception("Unknown value type");
            return true;
        }
        public static float ValueFloat(this SfNumericTextBox control)
        {
            if (control.Value is string s)
            {
                if (float.TryParse(s, out float sf))
                    return sf;
                else
                    return default(float);
            }
            if (control.Value is decimal c)
                return (float)c;
            if (control.Value is double d)
                return (float)d;
            if (control.Value is float f)
                return f;
            if (control.Value is int i)
                return i;
            if (control.Value is null)
                return default(float);
            else
                throw new Exception("Unknown value type");
        }
        public static int ValueInt(this SfNumericTextBox control)
        {
            if (control.Value is string s)
            {
                if (int.TryParse(s, out int si))
                    return si;
                if (decimal.TryParse(s, out decimal sc))
                    return (int)sc;
                else
                    return default(int);
            }
            if (control.Value is int i)
                return i;
            if (control.Value is decimal c)
                return (int)c;
            if (control.Value is double d)
                return (int)d;
            if (control.Value is float f)
                return (int)f;
            if (control.Value is null)
                return default(int);
            else
                throw new Exception("Unknown value type");
        }
    }
}
