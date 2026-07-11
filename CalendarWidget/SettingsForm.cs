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
        Text = "Calendar widget";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Back;
        ForeColor = Fore;
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(340, 400);

        // ---- transparency ----
        var valAlpha = ValueLabel(settings.Transparency.ToString(), 16);
        Controls.Add(SectionLabel("TRANSPARENCY", 16));
        Controls.Add(valAlpha);
        var sldAlpha = Slider(40, 80, 255, settings.Transparency);
        sldAlpha.ValueChanged += (_, _) =>
        {
            valAlpha.Text = sldAlpha.Value.ToString();
            main.SetTransparency(sldAlpha.Value);
        };
        Controls.Add(sldAlpha);

        // ---- dim ----
        var valDim = ValueLabel(settings.Dim + "%", 100);
        Controls.Add(SectionLabel("DIM", 100));
        Controls.Add(valDim);
        var sldDim = Slider(124, 0, 90, settings.Dim);
        sldDim.ValueChanged += (_, _) =>
        {
            valDim.Text = sldDim.Value + "%";
            main.SetDim(sldDim.Value);
        };
        Controls.Add(sldDim);

        // ---- hover panel corner ----
        Controls.Add(SectionLabel("PANEL CORNER", 184));
        var cmbCorner = new ComboBox
        {
            Location = new Point(20, 208),
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

        // ---- startup ----
        var cbStartup = new CheckBox
        {
            Text = "Start with Windows",
            AutoSize = true,
            Location = new Point(20, 252),
            ForeColor = Fore,
        };
        cbStartup.Checked = IsStartupEnabled();
        cbStartup.CheckedChanged += (_, _) => SetStartup(cbStartup.Checked);
        Controls.Add(cbStartup);

        // ---- tip + exit ----
        Controls.Add(new Label
        {
            Text = "Tip: for a real dark theme, enable dark mode inside Google Calendar's own settings (gear icon).",
            ForeColor = Muted,
            Font = new Font("Segoe UI", 9f),
            Location = new Point(20, 288),
            Size = new Size(300, 40),
        });

        var btnExit = new Button
        {
            Text = "Exit widget",
            Location = new Point(20, 340),
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
            Location = new Point(180, 340),
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
        if (await main.IsSignedInAsync())
        {
            if (MessageBox.Show(
                    "Sign out of Google? This clears the widget's saved session; you'll need to sign in again to see your calendar.",
                    "Calendar widget", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                await main.SignOutAsync();
                RefreshAccountButton();
            }
        }
        else
        {
            main.BeginSignIn();
            Hide();  // get out of the way of the sign-in page
        }
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
