using PhoneMic.Core;

namespace PhoneMic;

/// <summary>Everything that runs regardless of whether the window is open.</summary>
public sealed class Engine : IDisposable
{
    public Settings Settings { get; }
    public JitterBuffer Buffer { get; } = new();
    public AudioServer Server { get; }
    public AudioOutput Output { get; }
    public UsbBridge? Usb { get; }

    /// <param name="audio">False for the design preview: nothing listens, plays or touches adb.</param>
    public Engine(Settings settings, bool audio = true)
    {
        Settings = settings;
        Buffer.Gain = settings.Gain;
        Buffer.Profile = BufferProfile.For(settings.BufferMode);
        Server = new AudioServer(Buffer, () => Settings.Token, settings.Port);
        Output = new AudioOutput(Buffer);
        if (!audio) return;

        Server.Start();
        Usb = new UsbBridge(settings.Port);
        var device = PickDevice(AudioOutput.ListDevices());
        if (device != null) Output.Start(device.Id);
    }

    /// <summary>
    /// The saved choice if it still exists, else VB-Cable. Never falls back to
    /// the speakers on its own: the phone next to them would start howling.
    /// </summary>
    public OutputDevice? PickDevice(IReadOnlyList<OutputDevice> devices) =>
        devices.FirstOrDefault(d => d.Id == Settings.OutputDeviceId)
        ?? devices.FirstOrDefault(d => d.IsVirtualCable);

    public void SelectDevice(OutputDevice device)
    {
        Settings.OutputDeviceId = device.Id;
        Settings.Save();
        Output.Start(device.Id);
    }

    public void SetGain(float gain)
    {
        Buffer.Gain = gain;
        Settings.Gain = gain;
    }

    public void SetBufferMode(BufferMode mode)
    {
        Buffer.Profile = BufferProfile.For(mode);
        Settings.BufferMode = mode;
        Settings.Save();
    }

    public void ResetPairing()
    {
        Settings.Token = Pairing.NewToken();
        Settings.Save();
        Log.Info("Pairing token reset");
    }

    public string PairingUri(out IReadOnlyList<System.Net.IPAddress> addresses)
    {
        addresses = Pairing.LocalAddresses();
        return Pairing.Uri(addresses, Settings.Port, Settings.Token, Environment.MachineName);
    }

    public void Dispose()
    {
        Settings.Save();
        Usb?.Dispose();
        Output.Dispose();
        Server.Dispose();
    }
}
