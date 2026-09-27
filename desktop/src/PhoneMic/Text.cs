using System.Globalization;

namespace PhoneMic;

/// <summary>
/// Every string the PC app shows, in English and Russian side by side.
///
/// Two languages do not need resource files and satellite assemblies; keeping
/// each pair on one line makes a missing or stale translation obvious in
/// review. The language follows Windows and is fixed at startup, since XAML
/// reads these through x:Static.
/// </summary>
public static class Text
{
    /// <summary>Russian when Windows is in Russian, English otherwise. `--lang=en|ru` overrides it.</summary>
    public static bool Russian { get; set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";

    private static string T(string en, string ru) => Russian ? ru : en;

    // Header
    public static string Connected => T("Connected", "Подключено");
    public static string WaitingForPhone => T("Waiting for a phone", "Ожидание телефона");

    // Pairing card
    public static string ScanPrompt => T("Scan the code in the PhoneMic app", "Отсканируйте код в приложении PhoneMic");
    public static string UsbFindsItself => T(
        "A phone plugged in with USB debugging on is found automatically.",
        "Телефон, подключённый кабелем с отладкой по USB, найдётся сам.");
    public static string HideQr => T("Hide QR code", "Скрыть QR-код");
    public static string ShowQr => T("Show QR code", "Показать QR-код");
    public static string NoNetwork => T("This computer is not on a network", "Компьютер не подключён к сети");
    public static string Port(int port) => T($"port {port}", $"порт {port}");

    // Live card
    public static string UsbCable => T("USB cable", "USB-кабель");
    public static string Stats(double bufferMs, double lossPercent) =>
        T($"Buffer {bufferMs:F0} ms · loss {lossPercent:F1}%", $"Буфер {bufferMs:F0} мс · потери {lossPercent:F1}%");
    public static string Clipping => T("Clipping: turn the volume down", "Перегруз: уменьшите громкость");

    // VB-Cable missing
    public static string CableMissing => T("VB-Audio Virtual Cable not found", "Не найден VB-Audio Virtual Cable");
    public static string CableExplained => T(
        "It turns the sound from the phone into a microphone that Discord, Zoom and other programs can see. Install it and restart PhoneMic.",
        "Он превращает звук с телефона в микрофон, который видят Discord, Zoom и другие программы. Установите его и перезапустите PhoneMic.");
    public static string CableDownload => T("Download from vb-audio.com", "Скачать с vb-audio.com");

    // Settings
    public static string OutputDevice => T("Send the sound to", "Куда выводить звук");
    public static string NoDevice => T("Pick a device, or the sound goes nowhere.", "Выберите устройство, иначе звук никуда не пойдёт.");
    public static string CableChosen => T(
        "In Discord, Zoom, OBS and other programs, choose the microphone “CABLE Output”.",
        "В Discord, Zoom, OBS и других программах выберите микрофон «CABLE Output».");
    public static string OtherDeviceChosen => T(
        "You will hear the phone on this device. For other programs to see it as a microphone, choose “CABLE Input”.",
        "Звук телефона будет слышен в этом устройстве. Чтобы программы видели его как микрофон, выберите «CABLE Input».");
    public static string OutputOpenFailed => T("Could not open the output device", "Не удалось открыть устройство вывода");
    public static string OutputLost => T("The output device went away, reconnecting…", "Устройство вывода отключилось, переподключаюсь…");
    public static string Volume => T("Volume", "Громкость");
    public static string Buffer => T("Buffer", "Буфер");
    public static string ModeLow => T("Low latency", "Мин. задержка");
    public static string ModeBalanced => T("Balanced", "Баланс");
    public static string ModeStable => T("Stable", "Надёжный");
    public static string ModeLowHint => T(
        "20–40 ms of delay. For USB and excellent Wi-Fi; on poor Wi-Fi expect clicks.",
        "Задержка 20–40 мс. Для USB и отличного Wi-Fi, на плохом будут щелчки.");
    public static string ModeBalancedHint => T("Adapts to the network. Right almost always.", "Сам подстраивается под сеть. Подходит почти всегда.");
    public static string ModeStableHint => T(
        "About 100–150 ms of delay, but no dropouts even on poor Wi-Fi.",
        "Задержка около 100–150 мс, зато без обрывов даже на плохом Wi-Fi.");
    public static string StartWithWindows => T("Start with Windows", "Запускать вместе с Windows");
    public static string ResetPairing => T("Reset pairing", "Сбросить сопряжение");
    public static string ResetPairingTip => T(
        "A new QR code. Phones paired before will stop connecting.",
        "Новый QR-код. Телефоны, сопряжённые раньше, перестанут подключаться.");
    public static string ResetPairingQuestion => T("Reset pairing?", "Сбросить сопряжение?");
    public static string ResetPairingExplained => T(
        "A new QR code appears, and phones paired before stop connecting until they scan it.",
        "Появится новый QR-код, а телефоны, сопряжённые раньше, перестанут подключаться, пока не отсканируют его.");
    public static string OpenLog => T("Open log", "Открыть журнал");

    // Tray and messages
    public static string TrayOpen => T("Open PhoneMic", "Открыть PhoneMic");
    public static string TrayExit => T("Exit", "Выход");
    public static string TrayWaiting => T("PhoneMic — waiting for a phone", "PhoneMic — ожидание телефона");
    public static string StillRunning => T(
        "PhoneMic keeps running here. Exit from this icon's menu.",
        "PhoneMic продолжает работать здесь. Выход — через меню значка.");
    public static string PortBusy(int port) => T(
        $"Port {port} is taken by another program. Close it and start PhoneMic again.",
        $"Порт {port} уже занят другой программой. Закройте её и запустите PhoneMic снова.");
}
