using Microsoft.Win32;

namespace CalendarWidget;

public class SettingsForm : Form
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "CalendarWidget";

    private readonly MainForm main;
    private readonly Button btnAccount;

    private static readonly Color Back = Color.FromArgb(32, 33, 36);
    private static readonly Color CardBack = Color.FromArgb(48, 49, 52);
    private static readonly Color Border = Color.FromArgb(94, 99, 104);
    private static readonly Color Fore = Color.FromArgb(232, 234, 237);
    private static readonly Color Muted = Color.FromArgb(154, 160, 166);
    private static readonly Color Accent = Color.FromArgb(138, 180, 248);

    private static readonly string[] CornerIds = ["BottomRight", "BottomLeft", "TopRight", "TopLeft"];
    private static readonly string[] CornerNames = ["Bottom right", "Bottom left", "Top right", "Top left"];

    public SettingsForm(MainForm main, AppSettings settings)
    {
        this.main = main;
        Text = "Google Calendar Desktop Widget";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Back;
        ForeColor = Fore;
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(340, 416);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* keep default */ }

        // ---- transparency ----
        var valAlpha = ValueLabel(settings.TransparencyPercent + "%", 16);
        Controls.Add(SectionLabel("TRANSPARENCY", 16));
        Controls.Add(valAlpha);
        var sldAlpha = Slider(40, 0, 95, settings.TransparencyPercent);
        sldAlpha.ValueChanged += (_, _) =>
        {
            valAlpha.Text = sldAlpha.Value + "%";
            main.SetTransparencyPercent(sldAlpha.Value);
        };
        Controls.Add(sldAlpha);

        // ---- live-wallpaper mode ----
        var cbBehind = new CheckBox
        {
            Text = "Sit behind desktop icons (live wallpaper)",
            AutoSize = true,
            Location = new Point(20, 96),
            ForeColor = Fore,
            Checked = settings.BehindDesktopIcons,
        };
        cbBehind.CheckedChanged += (_, _) => main.SetBehindDesktopIcons(cbBehind.Checked);
        Controls.Add(cbBehind);

        // ---- corner hover panel ----
        Controls.Add(SectionLabel("HOVER PANEL", 140));
        var cbCorner = new CheckBox
        {
            Text = "Show hover panel in a screen corner",
            AutoSize = true,
            Location = new Point(20, 164),
            ForeColor = Fore,
            Checked = settings.CornerPanelEnabled,
        };
        Controls.Add(cbCorner);
        var cmbCorner = new ComboBox
        {
            Location = new Point(20, 196),
            Enabled = settings.CornerPanelEnabled,
            Width = 300,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = CardBack,
            ForeColor = Fore,
        };
        cmbCorner.Items.AddRange(CornerNames);
        cmbCorner.SelectedIndex = Math.Max(0, Array.IndexOf(CornerIds, settings.PanelCorner));
        cmbCorner.SelectedIndexChanged += (_, _) => main.SetPanelCorner(CornerIds[cmbCorner.SelectedIndex]);
        Controls.Add(cmbCorner);
        cbCorner.CheckedChanged += (_, _) =>
        {
            cmbCorner.Enabled = cbCorner.Checked;
            main.SetCornerPanelEnabled(cbCorner.Checked);
        };

        // ---- startup ----
        var cbStartup = new CheckBox
        {
            Text = "Start with Windows",
            AutoSize = true,
            Location = new Point(20, 240),
            ForeColor = Fore,
        };
        cbStartup.Checked = IsStartupEnabled();
        cbStartup.CheckedChanged += (_, _) => SetStartup(cbStartup.Checked);
        Controls.Add(cbStartup);

        // ---- tip + exit ----
        Controls.Add(new Label
        {
            Text = "Tip: for a dark widget, enable dark mode inside Google Calendar's own settings (gear icon).",
            ForeColor = Muted,
            Font = new Font("Segoe UI", 9f),
            Location = new Point(20, 272),
            Size = new Size(300, 40),
        });

        // ---- support the project ----
        var btnDonate = new Button
        {
            Text = "♥  Support development (PayPal)",
            Location = new Point(20, 316),
            Size = new Size(300, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = CardBack,
            ForeColor = Color.FromArgb(242, 139, 130),  // soft red, matching the dark palette
        };
        btnDonate.FlatAppearance.BorderColor = Border;
        btnDonate.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 64, 67);
        btnDonate.Click += (_, _) => System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo("https://paypal.me/MahdiKazemiesfahani") { UseShellExecute = true });
        Controls.Add(btnDonate);

        var btnExit = new Button
        {
            Text = "Exit widget",
            Location = new Point(20, 364),
            Size = new Size(140, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = CardBack,
            ForeColor = Fore,
        };
        btnExit.FlatAppearance.BorderColor = Border;
        btnExit.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 64, 67);
        btnExit.Click += (_, _) => Application.Exit();
        Controls.Add(btnExit);

        btnAccount = new Button
        {
            Text = "Sign in",  // refreshed from the real cookie state whenever the form is shown
            Location = new Point(180, 364),
            Size = new Size(140, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = CardBack,
            ForeColor = Fore,
        };
        btnAccount.FlatAppearance.BorderColor = Border;
        btnAccount.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 64, 67);
        btnAccount.Click += OnAccountClick;
        Controls.Add(btnAccount);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
            RefreshAccountButton();
    }

    private async void RefreshAccountButton()
    {
        try
        {
            btnAccount.Text = await main.IsSignedInAsync() ? "Sign out" : "Sign in";
        }
        catch
        {
            btnAccount.Text = "Sign in";  // webview not ready yet; signing in is the sane default
        }
    }

    private async void OnAccountClick(object? sender, EventArgs e)
    {
        if (!await main.IsSignedInAsync())
        {
            main.BeginSignIn();
            Hide();  // get out of the way of the sign-in page
            return;
        }

        var keepBtn = new TaskDialogButton("Sign out, keep account");
        var wipeBtn = new TaskDialogButton("Sign out & delete all data");
        var page = new TaskDialogPage
        {
            Caption = "Google Calendar Desktop Widget",
            Heading = "Sign out of Google?",
            Text = "Keep account: Google remembers this account, so signing back in is quicker.\n\n"
                 + "Delete all data: wipes the widget's entire browser profile (accounts, cookies, cache) — "
                 + "like a fresh install.",
            Icon = TaskDialogIcon.ShieldBlueBar,
            Buttons = { keepBtn, wipeBtn, TaskDialogButton.Cancel },
            AllowCancel = true,
        };

        var result = TaskDialog.ShowDialog(this, page);
        if (result == keepBtn)
            await main.SignOutKeepAccountAsync();
        else if (result == wipeBtn)
            await main.SignOutAsync();
        else
            return;
        RefreshAccountButton();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.ApplyDarkTitleBar(Handle);
    }

    // closing the settings window only hides it; the widget keeps running
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }

    private Label SectionLabel(string text, int y) => new()
    {
        Text = text,
        ForeColor = Muted,
        Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
        AutoSize = true,
        Location = new Point(20, y),
    };

    private Label ValueLabel(string text, int y) => new()
    {
        Text = text,
        ForeColor = Accent,
        Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
        AutoSize = true,
        Anchor = AnchorStyles.Top | AnchorStyles.Right,
        Location = new Point(290, y),
    };

    private TrackBar Slider(int y, int min, int max, int value) => new()
    {
        Location = new Point(14, y),
        Width = 312,
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(value, min, max),
        TickStyle = TickStyle.None,
        BackColor = Back,
    };

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValueName) is not null;
    }

    private static void SetStartup(bool enable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enable)
            key.SetValue(RunValueName, $"\"{Application.ExecutablePath}\"");
        else
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
    }
}
