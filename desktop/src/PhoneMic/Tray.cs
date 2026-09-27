using System.Windows;
using PhoneMic.Core;
using Forms = System.Windows.Forms;

namespace PhoneMic;

/// <summary>Notification area icon. Green while a phone is streaming, grey otherwise.</summary>
public sealed class Tray : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon _live = LoadIcon("app.ico");
    private readonly System.Drawing.Icon _idle = LoadIcon("idle.ico");

    public Tray(Action show, Action exit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть PhoneMic", null, (_, _) => show());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => exit());

        _icon = new Forms.NotifyIcon
        {
            Icon = _idle,
            Text = "PhoneMic — ожидание телефона",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) show();
        };
    }

    public void SetConnected(SessionInfo? session)
    {
        _icon.Icon = session == null ? _idle : _live;
        var text = session == null ? "PhoneMic — ожидание телефона" : $"PhoneMic — {session.DeviceName}";
        _icon.Text = text.Length > 63 ? text[..63] : text;
    }

    public void ShowHint(string text) => _icon.ShowBalloonTip(3000, "PhoneMic", text, Forms.ToolTipIcon.None);

    private static System.Drawing.Icon LoadIcon(string name)
    {
        var stream = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{name}"))!.Stream;
        return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
