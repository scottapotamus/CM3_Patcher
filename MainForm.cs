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
        CheckBox checkBoxRoleAware;
        NumericUpDown numericHighlightFrom;
        CheckBox checkBoxButtonFix;
        NumericUpDown numericButtonDelay;
        CheckBox checkBoxBackup;
        Button buttonPatch;
        Label labelNote;

        public MainForm()
        {
            Text = "Scottapotamus CM3 Patcher";
            ClientSize = new Size(520, 478);
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

            Controls.Add(new Label { Text = "Resolution (pick one or type WIDTHxHEIGHT):", Left = 12, Top = 94, AutoSize = true });
            comboBoxResolution = new ComboBox { Left = 12, Top = 114, Width = 200, DropDownStyle = ComboBoxStyle.DropDown };
            comboBoxResolution.Items.AddRange(Presets);
            comboBoxResolution.Text = "1600x1200";
            Controls.Add(comboBoxResolution);

            Controls.Add(new Label { Text = "Scaling:", Left = 224, Top = 117, AutoSize = true });
            comboBoxScaling = new ComboBox { Left = 276, Top = 114, Width = 232, DropDownStyle = ComboBoxStyle.DropDownList };
            comboBoxScaling.Items.AddRange(new object[] { "High quality (bicubic, smoothest)", "Crisp (sharper bicubic)", "Smooth (halftone)", "Sharp (blocky pixels)" });
            comboBoxScaling.SelectedIndex = 0;
            Controls.Add(comboBoxScaling);

            Controls.Add(new Label { Text = "Font sharpening (tightens the soft edges of the game's text):", Left = 12, Top = 148, AutoSize = true });
            comboBoxFonts = new ComboBox { Left = 12, Top = 168, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            comboBoxFonts.Items.AddRange(new object[] { "Medium (recommended)", "Strong", "None (original fonts)" });
            comboBoxFonts.SelectedIndex = 0;
            Controls.Add(comboBoxFonts);
            labelFonts = new Label { Left = 224, Top = 171, Width = 284, Height = 18, Text = "" };
            Controls.Add(labelFonts);

            checkBoxModernText = new CheckBox
            {
                Text = "Modern text: draw all the game's text with a TrueType font at full resolution (experimental)",
                Left = 12, Top = 204, AutoSize = true, Checked = false
            };
            checkBoxModernText.CheckedChanged += delegate
            {
                comboBoxTextFont.Enabled = checkBoxModernText.Checked;
                checkBoxAttributes.Enabled = checkBoxModernText.Checked;
                numericHighlightFrom.Enabled = checkBoxModernText.Checked && checkBoxAttributes.Checked;
                checkBoxRoleAware.Enabled = checkBoxModernText.Checked && checkBoxAttributes.Checked;
            };
            Controls.Add(checkBoxModernText);

            Controls.Add(new Label { Text = "Text font:", Left = 32, Top = 232, AutoSize = true });
            comboBoxTextFont = new ComboBox { Left = 100, Top = 228, Width = 200, DropDownStyle = ComboBoxStyle.DropDown, Enabled = false };
            comboBoxTextFont.Items.AddRange(new object[] { "Segoe UI", "Arial", "Tahoma", "Verdana", "Calibri", "Trebuchet MS", "Bahnschrift" });
            comboBoxTextFont.Text = TextOverlayInstaller.DefaultFont;
            Controls.Add(comboBoxTextFont);

            checkBoxAttributes = new CheckBox
            {
                Text = "Colour player attributes by value and highlight the best (rated at or above:)",
                Left = 32, Top = 258, AutoSize = true, Checked = true, Enabled = false
            };
            checkBoxAttributes.CheckedChanged += delegate
            {
                numericHighlightFrom.Enabled = checkBoxModernText.Checked && checkBoxAttributes.Checked;
                checkBoxRoleAware.Enabled = checkBoxModernText.Checked && checkBoxAttributes.Checked;
            };
            Controls.Add(checkBoxAttributes);

            numericHighlightFrom = new NumericUpDown { Left = 440, Top = 255, Width = 50, Minimum = 1, Maximum = 20, Value = TextOverlayInstaller.DefaultHighlightFrom, Enabled = false };
            Controls.Add(numericHighlightFrom);

            checkBoxRoleAware = new CheckBox
            {
                Text = "Only highlight the attributes that matter for the player's position (blue = key but weak)",
                Left = 52, Top = 282, AutoSize = true, Checked = true, Enabled = false
            };
            Controls.Add(checkBoxRoleAware);

            checkBoxButtonFix = new CheckBox
            {
                Text = "Fix over-fast + / - and scroll buttons (a short click currently repeats many times)",
                Left = 12, Top = 314, AutoSize = true, Checked = true
            };
            checkBoxButtonFix.CheckedChanged += delegate { numericButtonDelay.Enabled = checkBoxButtonFix.Checked; };
            Controls.Add(checkBoxButtonFix);

            Controls.Add(new Label { Text = "Wait per step while a button is held (ms):", Left = 32, Top = 342, AutoSize = true });
            numericButtonDelay = new NumericUpDown { Left = 270, Top = 338, Width = 60, Minimum = 10, Maximum = 500, Value = 60, Increment = 10 };
            Controls.Add(numericButtonDelay);

            checkBoxBackup = new CheckBox { Text = "Make a backup of the original exe (cm3_original.exe) first", Left = 12, Top = 372, AutoSize = true, Checked = true };
            Controls.Add(checkBoxBackup);

            buttonPatch = new Button { Text = "Apply", Left = 12, Top = 406, Width = 140, Height = 28 };
            buttonPatch.Click += buttonPatch_Click;
            Controls.Add(buttonPatch);

            labelNote = new Label
            {
                Left = 164, Top = 402, Width = 344, Height = 66,
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
                    checkBoxRoleAware.Checked = TextOverlayInstaller.ReadRoleAware(gameFolder);
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
                    TextOverlayInstaller.Install(gameFolder, comboBoxTextFont.Text.Trim(), checkBoxAttributes.Checked, (int)numericHighlightFrom.Value, checkBoxRoleAware.Checked);
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
