using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CM3ResolutionPatcher
{
    public class MainForm : Form
    {
        static readonly string[] Presets = {
            "800x600 (original)",
            "1024x768",
            "1152x864",
            "1280x960",
            "1280x1024",
            "1600x1200",
            "1920x1080",
            "1920x1200",
            "2560x1440",
            "2560x1600",
        };

        TextBox textBoxFile;
        Button buttonBrowse;
        Label labelCurrent;
        ComboBox comboBoxResolution;
        ComboBox comboBoxScaling;
        ComboBox comboBoxFonts;
        Label labelFonts;
        CheckBox checkBoxModernText;
        ComboBox comboBoxTextFont;
        CheckBox checkBoxAttributes;
        ComboBox comboBoxHighlightMode;
        NumericUpDown numericHighlightFrom;
        Button buttonKeyColour;
        Button buttonSecondaryColour;
        Button buttonOtherColour;
        CheckBox checkBoxBorder;
        Button buttonBorderColour;
        CheckBox checkBoxButtonFix;
        NumericUpDown numericButtonDelay;
        CheckBox checkBoxBackup;
        Button buttonPatch;
        Label labelNote;

        public MainForm()
        {
            Text = "Scottapotamus CM3 Patcher";
            ClientSize = new Size(520, 642);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            Controls.Add(new Label { Text = "Championship Manager 3 exe (cm3.exe):", Left = 12, Top = 14, AutoSize = true });
            textBoxFile = new TextBox { Left = 12, Top = 34, Width = 420 };
            textBoxFile.TextChanged += delegate { RefreshCurrent(); };
            buttonBrowse = new Button { Text = "Browse...", Left = 440, Top = 32, Width = 68 };
            buttonBrowse.Click += buttonBrowse_Click;
            Controls.Add(textBoxFile);
            Controls.Add(buttonBrowse);

            labelCurrent = new Label { Left = 12, Top = 66, Width = 496, Height = 18, Text = "" };
            Controls.Add(labelCurrent);

            // ---- GFX fixes ----
            GroupBox groupGfx = new GroupBox { Text = "GFX fixes", Left = 12, Top = 92, Width = 496, Height = 184 };
            Controls.Add(groupGfx);

            groupGfx.Controls.Add(new Label { Text = "Resolution (pick one or type WIDTHxHEIGHT):", Left = 12, Top = 22, AutoSize = true });
            comboBoxResolution = new ComboBox { Left = 12, Top = 42, Width = 200, DropDownStyle = ComboBoxStyle.DropDown };
            comboBoxResolution.Items.AddRange(Presets);
            comboBoxResolution.Text = "1600x1200";
            groupGfx.Controls.Add(comboBoxResolution);

            groupGfx.Controls.Add(new Label { Text = "Scaling:", Left = 224, Top = 45, AutoSize = true });
            comboBoxScaling = new ComboBox { Left = 276, Top = 42, Width = 208, DropDownStyle = ComboBoxStyle.DropDownList };
            comboBoxScaling.Items.AddRange(new object[] { "High quality (bicubic, smoothest)", "Crisp (sharper bicubic)", "Smooth (halftone)", "Sharp (blocky pixels)" });
            comboBoxScaling.SelectedIndex = 0;
            groupGfx.Controls.Add(comboBoxScaling);

            groupGfx.Controls.Add(new Label { Text = "Font sharpening (tightens the soft edges of the game's text):", Left = 12, Top = 74, AutoSize = true });
            comboBoxFonts = new ComboBox { Left = 12, Top = 94, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            comboBoxFonts.Items.AddRange(new object[] { "Medium (recommended)", "Strong", "None (original fonts)" });
            comboBoxFonts.SelectedIndex = 0;
            groupGfx.Controls.Add(comboBoxFonts);
            labelFonts = new Label { Left = 224, Top = 97, Width = 260, Height = 18, Text = "" };
            groupGfx.Controls.Add(labelFonts);

            checkBoxModernText = new CheckBox
            {
                Text = "Modern text: draw the game's text with a TrueType font at full resolution (experimental)",
                Left = 12, Top = 126, AutoSize = true, Checked = false
            };
            checkBoxModernText.CheckedChanged += delegate { UpdateEnabled(); };
            groupGfx.Controls.Add(checkBoxModernText);

            groupGfx.Controls.Add(new Label { Text = "Text font:", Left = 32, Top = 154, AutoSize = true });
            comboBoxTextFont = new ComboBox { Left = 100, Top = 150, Width = 200, DropDownStyle = ComboBoxStyle.DropDown, Enabled = false };
            comboBoxTextFont.Items.AddRange(new object[] { "Segoe UI", "Arial", "Tahoma", "Verdana", "Calibri", "Trebuchet MS", "Bahnschrift" });
            comboBoxTextFont.Text = TextOverlayInstaller.DefaultFont;
            groupGfx.Controls.Add(comboBoxTextFont);

            // ---- UI fixes ----
            GroupBox groupUi = new GroupBox { Text = "UI fixes", Left = 12, Top = 284, Width = 496, Height = 80 };
            Controls.Add(groupUi);

            checkBoxButtonFix = new CheckBox
            {
                Text = "Fix over-fast + / - and scroll buttons (a short click currently repeats many times)",
                Left = 12, Top = 20, AutoSize = true, Checked = true
            };
            checkBoxButtonFix.CheckedChanged += delegate { numericButtonDelay.Enabled = checkBoxButtonFix.Checked; };
            groupUi.Controls.Add(checkBoxButtonFix);

            groupUi.Controls.Add(new Label { Text = "Wait per step while a button is held (ms):", Left = 32, Top = 50, AutoSize = true });
            numericButtonDelay = new NumericUpDown { Left = 270, Top = 46, Width = 60, Minimum = 10, Maximum = 500, Value = 60, Increment = 10 };
            groupUi.Controls.Add(numericButtonDelay);

            // ---- Game enhancements (these are drawn by the modern text overlay, so they need it switched on) ----
            GroupBox groupGame = new GroupBox { Text = "Game enhancements (need Modern text)", Left = 12, Top = 372, Width = 496, Height = 172 };
            Controls.Add(groupGame);

            checkBoxAttributes = new CheckBox
            {
                Text = "Highlight player attributes (the bar fills in proportion to the value)",
                Left = 12, Top = 20, AutoSize = true, Checked = true, Enabled = false
            };
            checkBoxAttributes.CheckedChanged += delegate { UpdateEnabled(); };
            groupGame.Controls.Add(checkBoxAttributes);

            groupGame.Controls.Add(new Label { Text = "Colour them by:", Left = 32, Top = 50, AutoSize = true });
            comboBoxHighlightMode = new ComboBox { Left = 130, Top = 46, Width = 350, DropDownStyle = ComboBoxStyle.DropDownList, Enabled = false };
            comboBoxHighlightMode.Items.AddRange(new object[] {
                "The player's position (key / secondary / other for that role)",
                "The value (15+ key, 10-14 secondary, below 10 other)" });
            comboBoxHighlightMode.SelectedIndex = 1;
            comboBoxHighlightMode.SelectedIndexChanged += delegate { UpdateEnabled(); };
            groupGame.Controls.Add(comboBoxHighlightMode);

            groupGame.Controls.Add(new Label { Text = "If the position isn't known, highlight values at or above:", Left = 32, Top = 78, AutoSize = true });
            numericHighlightFrom = new NumericUpDown { Left = 340, Top = 74, Width = 50, Minimum = 1, Maximum = 20, Value = TextOverlayInstaller.DefaultHighlightFrom, Enabled = false };
            groupGame.Controls.Add(numericHighlightFrom);

            groupGame.Controls.Add(new Label { Text = "Highlight colours - key:", Left = 32, Top = 108, AutoSize = true });
            buttonKeyColour = MakeColourButton(158, 104, TextOverlayInstaller.DefaultKeyColour);
            groupGame.Controls.Add(buttonKeyColour);
            groupGame.Controls.Add(new Label { Text = "secondary:", Left = 226, Top = 108, AutoSize = true });
            buttonSecondaryColour = MakeColourButton(292, 104, TextOverlayInstaller.DefaultSecondaryColour);
            groupGame.Controls.Add(buttonSecondaryColour);
            groupGame.Controls.Add(new Label { Text = "other:", Left = 360, Top = 108, AutoSize = true });
            buttonOtherColour = MakeColourButton(404, 104, TextOverlayInstaller.DefaultOtherColour);
            groupGame.Controls.Add(buttonOtherColour);

            checkBoxBorder = new CheckBox { Text = "Outline the key attributes - colour:", Left = 32, Top = 138, AutoSize = true, Checked = false, Enabled = false };
            checkBoxBorder.CheckedChanged += delegate { UpdateEnabled(); };
            groupGame.Controls.Add(checkBoxBorder);
            buttonBorderColour = MakeColourButton(260, 136, TextOverlayInstaller.DefaultBorderColour);
            groupGame.Controls.Add(buttonBorderColour);

            checkBoxBackup = new CheckBox { Text = "Make a backup of the original exe (cm3_original.exe) first", Left = 12, Top = 554, AutoSize = true, Checked = false };
            Controls.Add(checkBoxBackup);

            buttonPatch = new Button { Text = "Apply", Left = 12, Top = 586, Width = 140, Height = 28 };
            buttonPatch.Click += buttonPatch_Click;
            Controls.Add(buttonPatch);

            labelNote = new Label
            {
                Left = 164, Top = 582, Width = 344, Height = 52,
                Text = "CM3 is a 4:3 game: other shapes get black bars at the sides. " +
                       "Windowed mode needs a screen taller than the chosen height. Choose 800x600 to restore the original."
            };
            Controls.Add(labelNote);

            textBoxFile.Text = FindDefaultExe();
        }

        static string FindDefaultExe()
        {
            string[] candidates = {
                Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? "", @"Championship Manager 3\cm3.exe"),
                Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles") ?? "", @"Championship Manager 3\cm3.exe"),
                @"C:\Championship Manager 3\cm3.exe",
            };
            foreach (string candidate in candidates)
                if (File.Exists(candidate))
                    return candidate;
            return "";
        }

        void buttonBrowse_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Select cm3.exe";
                dialog.Filter = "CM3 executable (cm3.exe)|cm3.exe|Executables (*.exe)|*.exe";
                if (File.Exists(textBoxFile.Text))
                    dialog.InitialDirectory = Path.GetDirectoryName(textBoxFile.Text);
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    textBoxFile.Text = dialog.FileName;
            }
        }

        // A small button showing a colour; clicking it opens the colour picker
        Button MakeColourButton(int left, int top, Color colour)
        {
            Button button = new Button { Left = left, Top = top, Width = 60, Height = 22, BackColor = colour, FlatStyle = FlatStyle.Flat, Enabled = false };
            button.Click += delegate
            {
                using (ColorDialog dialog = new ColorDialog { Color = button.BackColor, FullOpen = true })
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        button.BackColor = dialog.Color;
            };
            return button;
        }

        // The game enhancements are drawn by the modern text overlay, and each option depends on the ones above it
        void UpdateEnabled()
        {
            bool modern = checkBoxModernText.Checked;
            bool attributes = modern && checkBoxAttributes.Checked;
            comboBoxTextFont.Enabled = modern;
            checkBoxAttributes.Enabled = modern;
            comboBoxHighlightMode.Enabled = attributes;
            numericHighlightFrom.Enabled = attributes && comboBoxHighlightMode.SelectedIndex == 0;
            buttonKeyColour.Enabled = attributes;
            buttonSecondaryColour.Enabled = attributes;
            buttonOtherColour.Enabled = attributes;
            checkBoxBorder.Enabled = attributes;
            buttonBorderColour.Enabled = attributes && checkBoxBorder.Checked;
        }

        void RefreshCurrent()
        {
            labelCurrent.ForeColor = SystemColors.ControlText;
            labelFonts.Text = "";
            if (!File.Exists(textBoxFile.Text))
            {
                labelCurrent.Text = "";
                return;
            }

            try
            {
                if (!CM3ResolutionChanger.IsCM3Exe(textBoxFile.Text))
                {
                    labelCurrent.ForeColor = Color.Firebrick;
                    labelCurrent.Text = "This isn't a supported CM3 v3.04e cm3.exe.";
                    return;
                }

                int width, height;
                CM3ResolutionChanger.GetResolution(textBoxFile.Text, out width, out height);
                labelCurrent.Text = string.Format("Current resolution: {0}x{1}", width, height);

                string gameFolder = Path.GetDirectoryName(textBoxFile.Text);
                string dataFolder = Path.Combine(gameFolder, "Data");
                labelFonts.Text = FontSharpener.IsSharpened(dataFolder) ? "Currently: sharpened" : "Currently: original fonts";

                int buttonDelay = CM3ResolutionChanger.GetButtonRepeatDelay(textBoxFile.Text);
                checkBoxButtonFix.Checked = buttonDelay > 0;
                if (buttonDelay > 0)
                    numericButtonDelay.Value = Math.Max(numericButtonDelay.Minimum, Math.Min(numericButtonDelay.Maximum, buttonDelay));

                checkBoxModernText.Checked = CM3ResolutionChanger.GetTextOverlay(textBoxFile.Text);
                if (TextOverlayInstaller.IsInstalled(gameFolder))
                {
                    comboBoxTextFont.Text = TextOverlayInstaller.ReadFont(gameFolder);
                    checkBoxAttributes.Checked = TextOverlayInstaller.ReadHighlightAttributes(gameFolder);
                    numericHighlightFrom.Value = TextOverlayInstaller.ReadHighlightFrom(gameFolder);
                    comboBoxHighlightMode.SelectedIndex = TextOverlayInstaller.ReadByValue(gameFolder) ? 1 : 0;
                    checkBoxBorder.Checked = TextOverlayInstaller.ReadBorder(gameFolder);
                    buttonKeyColour.BackColor = TextOverlayInstaller.ReadKeyColour(gameFolder);
                    buttonSecondaryColour.BackColor = TextOverlayInstaller.ReadSecondaryColour(gameFolder);
                    buttonOtherColour.BackColor = TextOverlayInstaller.ReadOtherColour(gameFolder);
                    buttonBorderColour.BackColor = TextOverlayInstaller.ReadBorderColour(gameFolder);
                }
            }
            catch (Exception ex)
            {
                labelCurrent.ForeColor = Color.Firebrick;
                labelCurrent.Text = "Couldn't read the file: " + ex.Message;
            }
        }

        static bool TryParseResolution(string text, out int width, out int height)
        {
            width = height = 0;
            string[] parts = text.Split(new char[] { 'x', 'X', ' ', '*' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 && int.TryParse(parts[0], out width) && int.TryParse(parts[1], out height);
        }

        void buttonPatch_Click(object sender, EventArgs e)
        {
            string file = textBoxFile.Text;
            if (!File.Exists(file))
            {
                MessageBox.Show(this, "Please select cm3.exe first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int width, height;
            if (!TryParseResolution(comboBoxResolution.Text, out width, out height))
            {
                MessageBox.Show(this, "Couldn't understand that resolution. Use the form WIDTHxHEIGHT, e.g. 1600x1200.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                if (!CM3ResolutionChanger.IsCM3Exe(file))
                {
                    MessageBox.Show(this, "This isn't a supported CM3 v3.04e cm3.exe.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                int oldWidth, oldHeight;
                CM3ResolutionChanger.GetResolution(file, out oldWidth, out oldHeight);

                string backup = Path.Combine(Path.GetDirectoryName(file), "cm3_original.exe");
                if (checkBoxBackup.Checked && oldWidth == 800 && oldHeight == 600 && CM3ResolutionChanger.GetButtonRepeatDelay(file) == 0 && !File.Exists(backup))
                    File.Copy(file, backup);

                CM3ResolutionChanger.Smoothing smoothing = CM3ResolutionChanger.Smoothing.HighQuality;
                if (comboBoxScaling.SelectedIndex == 1)
                    smoothing = CM3ResolutionChanger.Smoothing.Crisp;
                else if (comboBoxScaling.SelectedIndex == 2)
                    smoothing = CM3ResolutionChanger.Smoothing.Halftone;
                else if (comboBoxScaling.SelectedIndex == 3)
                    smoothing = CM3ResolutionChanger.Smoothing.None;

                bool modernText = checkBoxModernText.Checked && !(width == 800 && height == 600);
                if (modernText && smoothing == CM3ResolutionChanger.Smoothing.None)
                {
                    smoothing = CM3ResolutionChanger.Smoothing.HighQuality;
                    comboBoxScaling.SelectedIndex = 0;
                }

                int buttonDelayMs = checkBoxButtonFix.Checked ? (int)numericButtonDelay.Value : 0;
                CM3ResolutionChanger.Apply(file, width, height, smoothing, modernText, buttonDelayMs);

                string gameFolder = Path.GetDirectoryName(file);
                if (modernText)
                    TextOverlayInstaller.Install(gameFolder, comboBoxTextFont.Text.Trim(), checkBoxAttributes.Checked, (int)numericHighlightFrom.Value, comboBoxHighlightMode.SelectedIndex == 1, checkBoxBorder.Checked, buttonKeyColour.BackColor, buttonSecondaryColour.BackColor, buttonOtherColour.BackColor, buttonBorderColour.BackColor);
                else
                    TextOverlayInstaller.Remove(gameFolder);

                FontSharpener.Strength fonts = FontSharpener.Strength.Medium;
                if (comboBoxFonts.SelectedIndex == 1)
                    fonts = FontSharpener.Strength.Strong;
                else if (comboBoxFonts.SelectedIndex == 2)
                    fonts = FontSharpener.Strength.None;

                string dataFolder = Path.Combine(Path.GetDirectoryName(file), "Data");
                if (FontSharpener.HasFonts(dataFolder))
                    FontSharpener.Apply(dataFolder, fonts);

                RefreshCurrent();

                MessageBox.Show(this,
                    (width == 800 && height == 600
                        ? "cm3.exe is at its original 800x600."
                        : string.Format("cm3.exe now runs at {0}x{1}.\r\n\r\nIf the game is in full screen, your graphics driver must support that 16-bit mode; otherwise use the -windowed option / Game Settings.", width, height))
                    + (buttonDelayMs > 0 ? "\r\n\r\nThe + / - and scroll button fix is on." : ""),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show(this, "Access denied. Run this program as Administrator (the game is usually in Program Files).", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (IOException ex)
            {
                MessageBox.Show(this, "Couldn't write the file (is CM3 still running?):\r\n\r\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
