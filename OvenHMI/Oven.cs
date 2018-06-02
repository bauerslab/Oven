using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using Windows.Storage.Streams;

namespace OvenHMI
{
    public static class Oven
    {
        //Constants
        public const float MaxPower = 3500f;
        public const byte PWMSteps = 120;
        private const int Timeout = 1000;
        /// <summary>Contains the most recent exception message if any</summary>
        public static string ErrorMessage { get; set; }
        public static bool Connected { get; set; }

        private static SerialDevice Device = null;
        private static DataWriter Writer = null;
        private static DataReader Reader = null;
        /// <summary>Enforces queue for Serial port so it can only send one request at a time</summary>
        private static SemaphoreSlim Busy { get; set; } = new SemaphoreSlim(1, 1);
        
        /// <summary>Finds the device and connects to it</summary>
        private static async Task<SerialDevice> GetDevice()
        {
            if (Device == null)
            {
                //It should be the only device that contains "USB"
                var devices = await DeviceInformation.FindAllAsync(SerialDevice.GetDeviceSelector());
                var deviceId = devices.FirstOrDefault(x => x.Id.Contains("USB"))?.Id;

                if (deviceId == null)
                {
                    Connected = false;
                    return null;
                }

                Device = await SerialDevice.FromIdAsync(deviceId);

                if (Device == null)
                {
                    Connected = false;
                    return null;
                }

                Device.BaudRate = 115200;
                Device.StopBits = SerialStopBitCount.One;
                Device.Parity = SerialParity.None;
                Device.DataBits = 8;
                Device.Handshake = SerialHandshake.None;
                Device.ReadTimeout = TimeSpan.FromMilliseconds(Timeout);
                Device.WriteTimeout = TimeSpan.FromMilliseconds(Timeout);
            }

            Connected = true;
            return Device;
        }
        /// <summary>Creates a reader for the device's input stream</summary>
        private static async Task<DataReader> GetReader()
        {
            if (Reader == null)
            {
                var device = await GetDevice();
                if (device == null)
                    return null;

                Reader = new DataReader(device.InputStream);
            }
            return Reader;
        }
        /// <summary>Creates a writer for the device's output stream</summary>
        private static async Task<DataWriter> GetWriter()
        {
            if (Writer == null)
            {
                var device = await GetDevice();
                if (device == null)
                    return null;

                Writer = new DataWriter(device.OutputStream);
            }
            return Writer;
        }
        /// <summary>Main Serial wrapper function that handles sending/receiving and any error conditions</summary>
        private static async Task<List<byte>> SendReceive(IEnumerable<byte> send, bool skipReceive = false)
        {
            await Busy.WaitAsync();
            try
            {
                await GetDevice();
                if (!Connected)
                    return null;

                //Send
                var writer = await GetWriter();
                writer.WriteBuffer(send.ToArray().AsBuffer());
                await writer.StoreAsync();

                //Receive
                List<byte> buffer = new List<byte>();
                if (skipReceive)
                    return buffer;

                var reader = await GetReader();
                await reader.LoadAsync(1);
                while (reader.UnconsumedBufferLength > 0)
                {
                    buffer.Add(reader.ReadByte());
                    await reader.LoadAsync(1);
                }

                return buffer;
            }
            catch (Exception x)
            {
                ErrorMessage = x.Message;
                Connected = false;
                return null;
            }
            finally
            { Busy.Release(); }
        }
        /// <summary>Overload to allow automatic conversion of command to byte[]</summary>
        private static async Task<List<byte>> SendReceive(OvenCommand command, bool skipReceive = false) => await SendReceive(new byte[] { (byte)command }, skipReceive);
        /// <summary>Sends a message, but does not wait for a response</summary>
        private static async Task<bool> Send(IEnumerable<byte> bytes) => await SendReceive(bytes, true) != null;
        /// <summary>Sends a command and receives a status in response</summary>
        private static async Task<OvenStatus> SendStatusCommand(OvenCommand command)
        {
            var buffer = await SendReceive(command);
            if (buffer?.Count != 1)
                return OvenStatus.NotConnected;
            return (OvenStatus)buffer[0];
        }

        /// <summary>Tell the microcontroller to start the recipe</summary>
        public static async Task<OvenStatus> Start() => await SendStatusCommand(OvenCommand.Start);
        /// <summary>Tell the microcontroller to stop the recipe and turn off the oven</summary>
        public static async Task<OvenStatus> Stop() => await SendStatusCommand(OvenCommand.Stop);
        /// <summary>Gets the device's current status code (see OvenStatus enum)</summary>
        public static async Task<OvenStatus> GetStatus() => await SendStatusCommand(OvenCommand.GetStatus);
        /// <summary>Sends the recipe to the device and returns whether the device correctly echoed it back</summary>
        public static async Task<bool> SetRecipe(Recipe recipe)
        {
            var message = recipe.Steps
                .SelectMany(x =>
                {
                    List<byte> bytes = new List<byte>();
                    bytes.AddRange(BitConverter.GetBytes(x.RawTime).Reverse());
                    bytes.AddRange(BitConverter.GetBytes(x.RawTemp).Reverse());
                    return bytes;
                })
                .ToList();
            message.Insert(0, (byte)OvenCommand.StartRecipe);
            message.Add((byte)OvenCommand.EndRecipe);
            
            var buffer = await SendReceive(message);
            if (buffer?.Count != message.Count)
                return false;

            for (byte i = 0; i < message.Count; i++)
                if (message[i] != buffer[i])
                    return false;
            return true;
        }
        /// <summary>Sends the ambient temperature to the device</summary>
        public static async Task<bool> SetAmbient(double ambient)
        {
            List<byte> message = new List<byte> { (byte)OvenCommand.SetAmbient };
            message.AddRange(BitConverter.GetBytes((Int16)(ambient * 4)).Reverse());
            return await Send(message);
        }
        /// <summary>Sends the PID coefficients to the device and returns the values echoed back</summary>
        public static async Task<PID> SetPID(PID pid)
        {
            List<byte> message = new List<byte> { (byte)OvenCommand.SetPID };
            message.AddRange(BitConverter.GetBytes(pid.Proportional).Reverse());
            message.AddRange(BitConverter.GetBytes(pid.Integral).Reverse());
            message.AddRange(BitConverter.GetBytes(pid.Derivative).Reverse());

            return ParsePid(await SendReceive(message));
        }
        /// <summary>Returns the device's current PID coefficients</summary>
        public static async Task<PID> GetPID() => ParsePid(await SendReceive(OvenCommand.GetPID));
        /// <summary>Returns the device's current measurements as a sample</summary>
        public static async Task<Sample> GetCurrentSample()
        {
            var buffer = await SendReceive(OvenCommand.GetCurrentSample);
            if (buffer?.Count != 7)
                return null;

            var sample = new Sample
            {
                RawTime =   (UInt16)(buffer[0] * 0x100 + buffer[1]),
                RawTemp =    (Int16)(buffer[2] * 0x100 + buffer[3]),
                RawAmbient = (Int16)(buffer[4] * 0x100 + buffer[5]),
                RawOutput =          buffer[6]
            };
            return sample;
        }

        /// <summary>Parses PID coefficient floating point values from the raw bytes</summary>
        private static PID ParsePid(List<byte> pidRaw)
        {
            if (pidRaw?.Count() != 12)
                return (0,0,0);

            byte[] raw = new byte[12];
            for (uint x = 0; x < 12; x++)
                raw[x] = pidRaw[(int)(4*(x/4) + 3 - x%4)];

            float p = BitConverter.Int32BitsToSingle(BitConverter.ToInt32(raw, 0));
            float i = BitConverter.Int32BitsToSingle(BitConverter.ToInt32(raw, 4));
            float d = BitConverter.Int32BitsToSingle(BitConverter.ToInt32(raw, 8));

            return (p, i, d);
        }
    }

    public enum OvenStatus
    {
        /// <summary>No recipe loaded</summary>
        WaitingForRecipe,
        /// <summary>Ready to start running</summary>
        Standby,
        /// <summary>Running the recipe</summary>
        Running,
        /// <summary>Microcontroller has faulted</summary>
        Faulted,
        /// <summary>First status request will return this</summary>
        NeedRestart,
        /// <summary>Local-only value to indicate that the connection is in use</summary>
        Busy = 0xFE,
        /// <summary>local-only value to indicate that serial port is not connected</summary>
        NotConnected = 0xFF,
    }
    public enum OvenCommand
    {
        /// <summary>Start executing the recipe</summary>
        Start = 1,
        /// <summary>Stop running</summary>
        Stop,
        /// <summary>Signals the start of a recipe send message</summary>
        StartRecipe,
        /// <summary>Get current data</summary>
        GetCurrentSample,
        /// <summary>Get current status code</summary>
        GetStatus,
        /// <summary>Set Ambient temperature for thermal model</summary>
        SetAmbient,
        /// <summary>Set PID coefficients</summary>
        SetPID,
        /// <summary>Get currently set PID coefficients</summary>
        GetPID,
        /// <summary>Signals the end of a recipe send message</summary>
        EndRecipe = 0xFF,
    }
    public struct PID
    {
        /// <summary>Proportional</summary>
        public float Proportional;
        public float Integral;
        public float Derivative;
        public static implicit operator PID((float p, float i, float d) anon)
        {
            return new PID
            {
                Proportional = anon.p,
                Integral = anon.i,
                Derivative = anon.d
            };
        }
        public static bool operator ==(PID a, PID b) => a.Proportional == b.Proportional && a.Integral == b.Integral && a.Derivative == b.Derivative;
        public static bool operator !=(PID a, PID b) => a.Proportional != b.Proportional || a.Integral != b.Integral || a.Derivative != b.Derivative;
        public static PID FromString(string s)
        {
            var pid = new PID();
            var parts = s?.Split(',');
            if (parts?.Length == 3)
            {
                if (float.TryParse(parts[0], out float p))
                    pid.Proportional = p;
                if (float.TryParse(parts[1], out float i))
                    pid.Integral = i;
                if (float.TryParse(parts[2], out float d))
                    pid.Derivative = d;
            }
            return pid;
        }
        public override string ToString() => $"{Proportional},{Integral},{Derivative}";
        public override bool Equals(object obj)
        {
            if (obj is PID val)
                return this == val;
            return false;
        }
        public override int GetHashCode()
        {
            return Proportional.GetHashCode() ^ Integral.GetHashCode() ^ Derivative.GetHashCode();
        }
    }
}
