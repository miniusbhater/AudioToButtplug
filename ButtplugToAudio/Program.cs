using System;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Buttplug.Client;
using Buttplug.Core;
using Buttplug.Core.Messages;

// some code taken from https://buttplug.io/docs/dev-guide/writing-buttplug-applications/application
// might regret releasing this shit if i ever do :sob:
// very aware of my shitty code, shut up
// thinking now that "pear" is a weird word isnt it?
// imagine if i added like 1000 lines of comments lol
// okay i'll shut up now

class Program
{
    static async Task Main()
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("AudioToButtplug - By miniusbhater :3");
        Console.WriteLine("Use 100% volume for best results");
        Console.WriteLine("You must have Intiface Central installed");

        // user settings
        Console.Write("Enable vibration? (y/n): ");
        bool enableVibration = Console.ReadLine()?.Trim().ToLower() == "y";

        Console.Write("Minimum RMS (0.0 - 1.0) [Recommended 0.0]: ");
        double minRms = double.TryParse(Console.ReadLine(), out var tempMin) ? tempMin : 0.0;

        Console.Write("Maximum RMS (0.1 - 1.0) [Recommended 0.5]: ");
        double maxRms = double.TryParse(Console.ReadLine(), out var tempMax) ? tempMax : 0.5;

        Console.Write("Intensity (0.0 - 1.0) [Recommended 1.0]: ");
        double intensitySetting = double.TryParse(Console.ReadLine(), out var tempIntensity) ? tempIntensity : 1.0;

        Console.WriteLine("\nStarting audio vibration...\n");

        var client = new ButtplugClient("AudioToButtplug");

        client.DeviceAdded += (_, args) =>
            Console.WriteLine($"[+] Device connected: {args.Device.DisplayName}");
        client.DeviceRemoved += (_, args) =>
            Console.WriteLine($"[-] Device disconnected: {args.Device.DisplayName}");
        client.ServerDisconnect += (_, _) =>
            Console.WriteLine("[!] Server connection lost!");
        client.ErrorReceived += (_, args) =>
            Console.WriteLine($"[!] Error: {args.Exception.Message}");

        try
        {
            await client.ConnectAsync(new ButtplugWebsocketConnector(new Uri("ws://127.0.0.1:12345")));
        }
        catch (ButtplugClientConnectorException)
        {
            Console.WriteLine("ERROR: Could not connect to Intiface Central!");
            return;
        }

        await client.StartScanningAsync();
        await Task.Delay(1000); // wait for devices to connect
        await client.StopScanningAsync();

        var devices = client.Devices;
        if (devices.Length == 0)
        {
            Console.WriteLine("No devices found.");
            await client.DisconnectAsync();
            return;
        }

        // measuring volume with naudio
        var deviceEnumerator = new MMDeviceEnumerator();
        var audioDevice = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

        var capture = new WasapiLoopbackCapture();
        capture.DataAvailable += async (s, e) =>
        {
            int BPS = 4;
            int SC = e.BytesRecorded / BPS;
            double sum = 0;

            for (int i = 0; i < e.BytesRecorded; i += 4)
            {
                float sample = BitConverter.ToSingle(e.Buffer, i);
                sum += sample * sample;
            }

            double rms = Math.Sqrt(sum / SC);
            float systemVolume = audioDevice.AudioEndpointVolume.MasterVolumeLevelScalar;
            if (systemVolume > 0)
                rms /= systemVolume;

            if (enableVibration && rms >= minRms)
            {
                double percent = Math.Min((rms / maxRms) * 100.0, 100.0);
                double intensity = (percent / 100.0) * intensitySetting;

                foreach (var device in devices)
                {
                    if (device.HasOutput(OutputType.Vibrate))
                    {
                        await device.RunOutputAsync(DeviceOutput.Vibrate.Percent(intensity));
                    }
                }
            }
        };

        capture.StartRecording();

        Console.WriteLine("Press enter to stop...");
        Console.ReadLine();

        // cleanup
        capture.StopRecording();
        capture.Dispose();

        foreach (var device in devices)
        {
            if (device.HasOutput(OutputType.Vibrate))
                await device.RunOutputAsync(DeviceOutput.Vibrate.Percent(0.0));
        }

        await client.DisconnectAsync();
        Console.WriteLine("Goodbye!");
    }
}