using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using CM3ResolutionPatcher;

namespace CM3SaveGameEditor
{
    // Everything the patcher can set, as it is in the game right now (read from cm3.exe and the game's folder)
    public class PatchSettings
    {
        // Graphics
        public int Width = 1600, Height = 1200;
        public int Scaling;                         // index into PatcherPanel.ScalingNames
        public int Fonts;                           // index into PatcherPanel.FontNames
        public bool ModernText;
        public string TextFont = TextOverlayInstaller.DefaultFont;
        // Interface
        public bool ButtonFix = true;
        public int ButtonDelay = 60;
        // Game rules
        public bool Subs;
        public int SubsNamed = 9, SubsUsed = 5;
        // Enhancements (they are drawn by the modern text overlay)
        public bool Highlight = true;
        public bool ByValue = true;
        public int HighlightFrom = TextOverlayInstaller.DefaultHighlightFrom;
        public Color Key = TextOverlayInstaller.DefaultKeyColour;
        public Color Secondary = TextOverlayInstaller.DefaultSecondaryColour;
        public Color Other = TextOverlayInstaller.DefaultOtherColour;
        public Color BorderColour = TextOverlayInstaller.DefaultBorderColour;
        public bool Border;
        public bool ClearButton = true;

        const string UtilsIni = "cm3utils.ini";

        // What cm3.exe, its Data folder and cm3text.ini say; what can't be read from there (the scaling filter and the font
        // sharpening strength) is remembered in cm3utils.ini next to the exe.
        public static PatchSettings Read(string exe)
        {
            PatchSettings s = new PatchSettings();
            string folder = Path.GetDirectoryName(exe);
            string utils = Path.Combine(folder, UtilsIni);

            int width, height;
            CM3ResolutionChanger.GetResolution(exe, out width, out height);
            s.Width = width;
            s.Height = height;

            int delay = CM3ResolutionChanger.GetButtonRepeatDelay(exe);
            s.ButtonFix = delay > 0;
            if (delay > 0)
                s.ButtonDelay = Math.Max(10, Math.Min(500, delay));

            int named, used;
            CM3ResolutionChanger.GetSubstitutes(exe, out named, out used);
            s.Subs = named > 0 || used > 0;
            if (named > 0)
                s.SubsNamed = Math.Min(CM3ResolutionChanger.MaxSubstitutes, named);
            if (used > 0)
                s.SubsUsed = Math.Min(CM3ResolutionChanger.MaxSubstitutes, used);

            s.ModernText = CM3ResolutionChanger.GetTextOverlay(exe);
            if (TextOverlayInstaller.IsInstalled(folder))
            {
                s.TextFont = TextOverlayInstaller.ReadFont(folder);
                s.Highlight = TextOverlayInstaller.ReadHighlightAttributes(folder);
                s.HighlightFrom = TextOverlayInstaller.ReadHighlightFrom(folder);
                s.ByValue = TextOverlayInstaller.ReadByValue(folder);
                s.Border = TextOverlayInstaller.ReadBorder(folder);
                s.Key = TextOverlayInstaller.ReadKeyColour(folder);
                s.Secondary = TextOverlayInstaller.ReadSecondaryColour(folder);
                s.Other = TextOverlayInstaller.ReadOtherColour(folder);
                s.BorderColour = TextOverlayInstaller.ReadBorderColour(folder);
                s.ClearButton = TextOverlayInstaller.ReadClearButton(folder);
            }

            int value;
            string text = TextOverlayInstaller.GetValue(utils, "patcher", "scaling");
            if (text != null && int.TryParse(text, out value) && value >= 0 && value < PatcherPanel.ScalingNames.Length)
                s.Scaling = value;
            text = TextOverlayInstaller.GetValue(utils, "patcher", "fonts");
            if (text != null && int.TryParse(text, out value) && value >= 0 && value < PatcherPanel.FontNames.Length)
                s.Fonts = value;
            if (!FontSharpener.IsSharpened(Path.Combine(folder, "Data")))
                s.Fonts = 2;
            return s;
        }

        // Puts these settings into cm3.exe, its Data folder and the game's folder (the whole lot, whatever changed)
        public void Write(string exe)
        {
            string folder = Path.GetDirectoryName(exe);

            CM3ResolutionChanger.Smoothing smoothing = CM3ResolutionChanger.Smoothing.HighQuality;
            if (Scaling == 1)
                smoothing = CM3ResolutionChanger.Smoothing.Crisp;
            else if (Scaling == 2)
                smoothing = CM3ResolutionChanger.Smoothing.Halftone;
            else if (Scaling == 3)
                smoothing = CM3ResolutionChanger.Smoothing.None;

            bool modern = ModernText && !(Width == 800 && Height == 600);
            if (modern && smoothing == CM3ResolutionChanger.Smoothing.None)
            {
                smoothing = CM3ResolutionChanger.Smoothing.HighQuality;
                Scaling = 0;
            }

            CM3ResolutionChanger.Apply(exe, Width, Height, smoothing, modern, ButtonFix ? ButtonDelay : 0, Subs ? SubsNamed : 0, Subs ? SubsUsed : 0);

            if (modern)
                TextOverlayInstaller.Install(folder, TextFont.Trim(), Highlight, HighlightFrom, ByValue, Border, Key, Secondary, Other, BorderColour, ClearButton);
            else
                TextOverlayInstaller.Remove(folder);

            FontSharpener.Strength strength = FontSharpener.Strength.Medium;
            if (Fonts == 1)
                strength = FontSharpener.Strength.Strong;
            else if (Fonts == 2)
                strength = FontSharpener.Strength.None;
            string data = Path.Combine(folder, "Data");
            if (FontSharpener.HasFonts(data))
                FontSharpener.Apply(data, strength);

            string utils = Path.Combine(folder, UtilsIni);
            TextOverlayInstaller.SetValue(utils, "patcher", "scaling", Scaling.ToString());
            TextOverlayInstaller.SetValue(utils, "patcher", "fonts", Fonts.ToString());
        }
    }

    // The patcher: lets Championship Manager 3 v3.04e (cm3.exe) run at other resolutions, with sharper fonts, modern text, the
    // fixed + / - buttons and modern substitutes rules. One tab for each kind of setting, each with its own Apply button that
    // changes only the settings of its tab.
    public class PatcherPanel : Panel
    {
        const string Title = "CM3 Utils";

        static readonly string[] Presets = {
            "800x600 (original)", "1024x768", "1152x864", "1280x960", "1280x1024", "1600x1200", "1920x1080", "1920x1200", "2560x1440", "2560x1600",
        };

        public static readonly string[] ScalingNames = {
            "High quality (bicubic, smoothest)", "Crisp (sharper bicubic)", "Smooth (halftone)", "Sharp (blocky pixels)"
        };

        public static readonly string[] FontNames = { "Medium (recommended)", "Strong", "None (original fonts)" };

        // Fonts to offer (the ones that are installed are listed; any other can be typed in)
        static readonly string[] FontChoices = {
            "Segoe UI", "Arial", "Tahoma", "Verdana", "Calibri", "Trebuchet MS", "Bahnschrift", "Candara", "Corbel", "Consolas",
            "Century Gothic", "Franklin Gothic Medium", "Gill Sans MT", "Lucida Sans Unicode", "Microsoft Sans Serif", "Georgia", "Cambria"
        };

        // the controls
        TextBox textBoxFile;
        Button buttonBrowse;
        Label labelCurrent;
        CheckBox checkBoxBackup;
        TabControl tabs;

        ComboBox comboBoxResolution;
        ComboBox comboBoxScaling;
        ComboBox comboBoxFonts;
        Label labelFonts;
        CheckBox checkBoxModernText;
        ComboBox comboBoxTextFont;
        Label labelFontPreview;

        CheckBox checkBoxButtonFix;
        NumericUpDown numericButtonDelay;

        CheckBox checkBoxSubs;
        NumericUpDown numericSubsNamed;
        NumericUpDown numericSubsUsed;

        Label labelNeedModern;
        CheckBox checkBoxAttributes;
        ComboBox comboBoxHighlightMode;
        NumericUpDown numericHighlightFrom;
        Button buttonKeyColour;
        Button buttonSecondaryColour;
        Button buttonOtherColour;
        CheckBox checkBoxBorder;
        Button buttonBorderColour;
        CheckBox checkBoxClearButton;

        List<Panel> pages = new List<Panel>();
        List<GroupBox> cards = new List<GroupBox>();
        PatchSettings loaded;       // what is in the game now

        public PatcherPanel()
        {
            BackColor = Theme.Page;
            Font = Theme.Normal;

            // the game's exe, above the tabs
            Panel top = new Panel { Dock = DockStyle.Top, Height = 96, BackColor = Theme.Page };
            top.Controls.Add(new Label { Text = "Championship Manager 3 exe (cm3.exe):", Left = 12, Top = 10, AutoSize = true, Font = Theme.Bold });
            textBoxFile = new TextBox { Left = 12, Top = 30, Width = 420 };
            textBoxFile.TextChanged += delegate { RefreshCurrent(); };
            buttonBrowse = new ModernButton { Text = "Browse...", Left = 440, Top = 28, Width = 76, Height = 24 };
            buttonBrowse.Click += buttonBrowse_Click;
            top.Controls.Add(textBoxFile);
            top.Controls.Add(buttonBrowse);
            labelCurrent = new Label { Left = 12, Top = 58, Width = 700, Height = 18, Text = "" };
            top.Controls.Add(labelCurrent);
            checkBoxBackup = new CheckBox { Text = "Keep a copy of the original exe (cm3_original.exe) the first time", Left = 12, Top = 74, AutoSize = true, Checked = true };
            top.Controls.Add(checkBoxBackup);
            top.Resize += delegate
            {
                buttonBrowse.Left = top.ClientSize.Width - buttonBrowse.Width - 12;
                textBoxFile.Width = buttonBrowse.Left - 8 - textBoxFile.Left;
                labelCurrent.Width = top.ClientSize.Width - 24;
            };

            tabs = new ModernTabs { Dock = DockStyle.Fill };
            Controls.Add(tabs);
            Controls.Add(top);
            tabs.BringToFront();

            BuildGraphicsTab();
            BuildInterfaceTab();
            BuildRulesTab();
            BuildEnhancementsTab();
            Theme.StyleTabs(tabs);

            textBoxFile.Text = FindDefaultExe();
            UpdateEnabled();
        }

        // ---------------------------------------------------------------- building blocks

        // A tab: a scrolling page of cards and, under it, the Apply button of the tab
        Panel NewTab(string title, string applyHint, EventHandler apply)
        {
            TabPage page = new TabPage(title);
            tabs.TabPages.Add(page);

            Panel bar = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Theme.Page };
            Button button = new ModernButton { Text = "Apply", Left = 12, Top = 10, Width = 140, Height = 32, Font = Theme.Bold };
            button.Click += apply;
            bar.Controls.Add(button);
            bar.Controls.Add(new Label { Text = applyHint, Left = 164, Top = 18, AutoSize = true, ForeColor = Color.FromArgb(80, 80, 110) });
            Theme.StyleButton(button, true);
            button.Tag = "styled";

            Panel content = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Page };
            page.Controls.Add(content);
            page.Controls.Add(bar);
            content.BringToFront();
            pages.Add(content);
            content.Resize += delegate { LayoutCards(content); };
            return content;
        }

        // Cards are stacked down the page; this is where the next one goes
        int NextTop(Panel content)
        {
            int bottom = 12;
            foreach (Control control in content.Controls)
                if (control is GroupBox)
                    bottom = Math.Max(bottom, control.Bottom + 12);
            return bottom;
        }

        GroupBox NewCard(Panel content, string title, int height)
        {
            GroupBox card = new CardBox { Text = title, Left = 12, Top = NextTop(content), Width = 680, Height = height, ForeColor = Theme.Navy };
            content.Controls.Add(card);
            cards.Add(card);
            return card;
        }

        // Small grey explanation under a control
        static Label Help(GroupBox card, string text, int left, int top, int width)
        {
            Label label = new Label { Text = text, Left = left, Top = top, Width = width, Height = 32, ForeColor = Color.FromArgb(90, 90, 120) };
            card.Controls.Add(label);
            return label;
        }

        static Label Caption(GroupBox card, string text, int left, int top)
        {
            Label label = new Label { Text = text, Left = left, Top = top, AutoSize = true, ForeColor = Color.FromArgb(30, 30, 60), Font = Theme.Bold };
            card.Controls.Add(label);
            return label;
        }

        void LayoutCards(Panel content)
        {
            int width = Math.Min(900, Math.Max(480, content.ClientSize.Width - 28));
            foreach (Control control in content.Controls)
                if (control is GroupBox)
                    control.Width = width;
        }

        // ---------------------------------------------------------------- the tabs

        void BuildGraphicsTab()
        {
            Panel page = NewTab("Graphics", "Applies the resolution, scaling and text settings on this tab.", delegate { ApplyTab(0); });

            GroupBox screen = NewCard(page, "Screen", 146);
            Caption(screen, "Resolution", 12, 24);
            comboBoxResolution = new ComboBox { Left = 12, Top = 44, Width = 200, DropDownStyle = ComboBoxStyle.DropDown };
            comboBoxResolution.Items.AddRange(Presets);
            comboBoxResolution.Text = "1600x1200";
            screen.Controls.Add(comboBoxResolution);
            Help(screen, "Pick one or type your own as WIDTHxHEIGHT. CM3 is a 4:3 game: other shapes get black bars. Choose 800x600 to put the original back.", 12, 72, 316).Height = 60;
            Caption(screen, "Scaling", 340, 24);
            comboBoxScaling = new ComboBox { Left = 340, Top = 44, Width = 290, DropDownStyle = ComboBoxStyle.DropDownList };
            comboBoxScaling.Items.AddRange(ScalingNames);
            comboBoxScaling.SelectedIndex = 0;
            screen.Controls.Add(comboBoxScaling);
            Help(screen, "How the game's 800x600 picture is stretched to the new size. High quality is the smoothest.", 340, 72, 300);

            GroupBox text = NewCard(page, "Text", 330);
            Caption(text, "Font sharpening", 12, 24);
            comboBoxFonts = new ComboBox { Left = 12, Top = 44, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            comboBoxFonts.Items.AddRange(FontNames);
            comboBoxFonts.SelectedIndex = 0;
            text.Controls.Add(comboBoxFonts);
            labelFonts = new Label { Left = 224, Top = 48, Width = 300, Height = 18, Text = "" };
            text.Controls.Add(labelFonts);
            Help(text, "Tightens the soft edges of the game's own text when it is enlarged.", 12, 72, 620);

            checkBoxModernText = new CheckBox
            {
                Text = "Modern text: draw the game's text with a TrueType font at the full resolution (experimental)",
                Left = 12, Top = 118, AutoSize = true, Checked = false, Font = Theme.Bold
            };
            checkBoxModernText.CheckedChanged += delegate { UpdateEnabled(); };
            text.Controls.Add(checkBoxModernText);
            Help(text, "Much sharper text. Needed for the attribute highlights and the squad Clear button on the Enhancements tab. Not available at 800x600.", 32, 142, 600);

            Caption(text, "Text font", 32, 186);
            comboBoxTextFont = new ComboBox
            {
                Left = 32, Top = 206, Width = 260, DropDownStyle = ComboBoxStyle.DropDown, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 26, MaxDropDownItems = 12
            };
            FontFamily[] installed = FontFamily.Families;
            foreach (string name in FontChoices)
                foreach (FontFamily family in installed)
                    if (string.Equals(family.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        comboBoxTextFont.Items.Add(family.Name);
                        break;
                    }
            comboBoxTextFont.DrawItem += DrawFontItem;
            comboBoxTextFont.TextChanged += delegate { ShowFontPreview(); };
            comboBoxTextFont.Text = TextOverlayInstaller.DefaultFont;
            text.Controls.Add(comboBoxTextFont);
            Help(text, "Every font in the list is shown in its own face. You can also type the name of any other installed font.", 304, 206, 330);

            labelFontPreview = new Label
            {
                Left = 32, Top = 244, Width = 590, Height = 70, BackColor = Theme.DarkNavy, ForeColor = Theme.Yellow,
                Text = "Crewe Alexandra 3 - 1 Man City\r\nThe quick brown fox jumps over the lazy dog 0123456789",
                Padding = new Padding(8, 6, 8, 6), Tag = "keep"
            };
            text.Controls.Add(labelFontPreview);
            ShowFontPreview();
        }

        void BuildInterfaceTab()
        {
            Panel page = NewTab("Interface", "Applies the button fix.", delegate { ApplyTab(1); });

            GroupBox buttons = NewCard(page, "Buttons", 130);
            checkBoxButtonFix = new CheckBox
            {
                Text = "Fix the over-fast + / - and scroll buttons", Left = 12, Top = 26, AutoSize = true, Checked = true, Font = Theme.Bold
            };
            checkBoxButtonFix.CheckedChanged += delegate { UpdateEnabled(); };
            buttons.Controls.Add(checkBoxButtonFix);
            Help(buttons, "In the original game a short click on + / - or a scroll arrow repeats many times. With the fix it waits a moment on each repeat while the mouse button is held.", 32, 50, 600);
            buttons.Controls.Add(new Label { Text = "Wait on each repeat (milliseconds):", Left = 32, Top = 96, AutoSize = true });
            numericButtonDelay = new NumericUpDown { Left = 250, Top = 92, Width = 64, Minimum = 10, Maximum = 500, Value = 60, Increment = 10 };
            buttons.Controls.Add(numericButtonDelay);
        }

        void BuildRulesTab()
        {
            Panel page = NewTab("Game rules", "Applies the substitutes rules. They are used by new games only.", delegate { ApplyTab(2); });

            GroupBox subs = NewCard(page, "Substitutes", 140);
            checkBoxSubs = new CheckBox
            {
                Text = "Modern substitutes rules for every competition", Left = 12, Top = 26, AutoSize = true, Checked = false, Font = Theme.Bold
            };
            checkBoxSubs.CheckedChanged += delegate { UpdateEnabled(); };
            subs.Controls.Add(checkBoxSubs);
            Help(subs, "Choose how many players are named on the bench and how many substitutions a team may make. Only games you start afterwards use it (the Editing part changes saved games).", 32, 50, 600);
            subs.Controls.Add(new Label { Text = "Named on the bench:", Left = 32, Top = 100, AutoSize = true });
            numericSubsNamed = new NumericUpDown { Left = 160, Top = 96, Width = 56, Minimum = 1, Maximum = CM3ResolutionChanger.MaxSubstitutes, Value = 9, Enabled = false };
            subs.Controls.Add(numericSubsNamed);
            subs.Controls.Add(new Label { Text = "Substitutions allowed:", Left = 260, Top = 100, AutoSize = true });
            numericSubsUsed = new NumericUpDown { Left = 392, Top = 96, Width = 56, Minimum = 1, Maximum = CM3ResolutionChanger.MaxSubstitutes, Value = 5, Enabled = false };
            subs.Controls.Add(numericSubsUsed);
        }

        void BuildEnhancementsTab()
        {
            Panel page = NewTab("Enhancements", "Applies the attribute highlights and the Clear button.", delegate { ApplyTab(3); });

            labelNeedModern = new Label
            {
                Left = 12, Top = 12, Width = 660, Height = 22, ForeColor = Color.FromArgb(170, 30, 30), Font = Theme.Bold,
                Text = "These need Modern text: switch it on and apply it on the Graphics tab first."
            };
            page.Controls.Add(labelNeedModern);

            GroupBox highlights = NewCard(page, "Player attribute highlights", 232);
            highlights.Top = 44;
            checkBoxAttributes = new CheckBox
            {
                Text = "Highlight player attributes", Left = 12, Top = 26, AutoSize = true, Checked = true, Font = Theme.Bold
            };
            checkBoxAttributes.CheckedChanged += delegate { UpdateEnabled(); };
            highlights.Controls.Add(checkBoxAttributes);
            Help(highlights, "A coloured bar behind each attribute on the player screens; it fills in proportion to the value.", 32, 48, 600);

            highlights.Controls.Add(new Label { Text = "Colour them by:", Left = 32, Top = 86, AutoSize = true });
            comboBoxHighlightMode = new ComboBox { Left = 130, Top = 82, Width = 440, DropDownStyle = ComboBoxStyle.DropDownList };
            comboBoxHighlightMode.Items.AddRange(new object[] {
                "The player's position (key / secondary / other for that role)",
                "The value (15+ key, 10-14 secondary, below 10 other)" });
            comboBoxHighlightMode.SelectedIndex = 1;
            comboBoxHighlightMode.SelectedIndexChanged += delegate { UpdateEnabled(); };
            highlights.Controls.Add(comboBoxHighlightMode);

            highlights.Controls.Add(new Label { Text = "If the position isn't known, highlight values from:", Left = 32, Top = 118, AutoSize = true });
            numericHighlightFrom = new NumericUpDown { Left = 320, Top = 114, Width = 56, Minimum = 1, Maximum = 20, Value = TextOverlayInstaller.DefaultHighlightFrom, Enabled = false };
            highlights.Controls.Add(numericHighlightFrom);

            highlights.Controls.Add(new Label { Text = "Colours  -  key:", Left = 32, Top = 156, AutoSize = true });
            buttonKeyColour = MakeColourButton(130, 152, TextOverlayInstaller.DefaultKeyColour);
            highlights.Controls.Add(buttonKeyColour);
            highlights.Controls.Add(new Label { Text = "secondary:", Left = 206, Top = 156, AutoSize = true });
            buttonSecondaryColour = MakeColourButton(276, 152, TextOverlayInstaller.DefaultSecondaryColour);
            highlights.Controls.Add(buttonSecondaryColour);
            highlights.Controls.Add(new Label { Text = "other:", Left = 352, Top = 156, AutoSize = true });
            buttonOtherColour = MakeColourButton(396, 152, TextOverlayInstaller.DefaultOtherColour);
            highlights.Controls.Add(buttonOtherColour);

            checkBoxBorder = new CheckBox { Text = "Outline the key attributes - colour:", Left = 32, Top = 192, AutoSize = true, Checked = false, Enabled = false };
            checkBoxBorder.CheckedChanged += delegate { UpdateEnabled(); };
            highlights.Controls.Add(checkBoxBorder);
            buttonBorderColour = MakeColourButton(260, 188, TextOverlayInstaller.DefaultBorderColour);
            highlights.Controls.Add(buttonBorderColour);

            GroupBox squad = NewCard(page, "Squad screen", 100);
            checkBoxClearButton = new CheckBox
            {
                Text = "Add a Clear button that unselects the whole team", Left = 12, Top = 26, AutoSize = true, Checked = true, Enabled = false, Font = Theme.Bold
            };
            squad.Controls.Add(checkBoxClearButton);
            Help(squad, "The game only lets you unselect the team one position at a time. The button sits next to Sort By and clicks every position for you.", 32, 50, 600);
        }

        // ---------------------------------------------------------------- font list drawn in the fonts themselves

        void DrawFontItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
                return;
            e.DrawBackground();
            string name = comboBoxTextFont.Items[e.Index].ToString();
            Font font = null;
            try
            {
                font = new Font(name, 12f);
            }
            catch (ArgumentException)
            {
                font = new Font(Font.FontFamily, 12f);
            }
            using (font)
                TextRenderer.DrawText(e.Graphics, name, font, e.Bounds, e.ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            e.DrawFocusRectangle();
        }

        // The chosen font, in itself
        void ShowFontPreview()
        {
            if (labelFontPreview == null)
                return;
            Font font;
            try
            {
                font = new Font(comboBoxTextFont.Text.Trim(), 14f);
                if (!string.Equals(font.Name, comboBoxTextFont.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    // not an installed font: the preview says so
                    font = new Font(Font.FontFamily, 11f);
                    labelFontPreview.Text = "(this font isn't installed on this computer)";
                    labelFontPreview.Font = font;
                    return;
                }
            }
            catch (ArgumentException)
            {
                return;
            }
            labelFontPreview.Text = "Crewe Alexandra 3 - 1 Man City\r\nThe quick brown fox jumps over the lazy dog 0123456789";
            labelFontPreview.Font = font;
        }

        // ---------------------------------------------------------------- the exe

        public static string FindDefaultExe()
        {
            string[] candidates = {
                @"E:\Championship Manager 3\cm3.exe",
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
            Button button = new ModernButton { Left = left, Top = top, Width = 60, Height = 24, BackColor = colour, Swatch = true, Enabled = false };
            button.Click += delegate
            {
                using (ColorDialog dialog = new ColorDialog { Color = button.BackColor, FullOpen = true })
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        button.BackColor = dialog.Color;
            };
            return button;
        }

        // Each option depends on the ones above it; the enhancements are drawn by the modern text overlay
        void UpdateEnabled()
        {
            if (checkBoxModernText == null || checkBoxClearButton == null || checkBoxBorder == null)
                return;
            comboBoxTextFont.Enabled = checkBoxModernText.Checked;
            labelFontPreview.Enabled = checkBoxModernText.Checked;
            numericButtonDelay.Enabled = checkBoxButtonFix.Checked;
            numericSubsNamed.Enabled = numericSubsUsed.Enabled = checkBoxSubs.Checked;

            // the enhancements need modern text to be in the game already (not just ticked)
            bool modernInGame = loaded != null && loaded.ModernText;
            labelNeedModern.Visible = !modernInGame;
            checkBoxAttributes.Enabled = modernInGame;
            checkBoxClearButton.Enabled = modernInGame;
            bool attributes = modernInGame && checkBoxAttributes.Checked;
            comboBoxHighlightMode.Enabled = attributes;
            numericHighlightFrom.Enabled = attributes && comboBoxHighlightMode.SelectedIndex == 0;
            buttonKeyColour.Enabled = attributes;
            buttonSecondaryColour.Enabled = attributes;
            buttonOtherColour.Enabled = attributes;
            checkBoxBorder.Enabled = attributes;
            buttonBorderColour.Enabled = attributes && checkBoxBorder.Checked;
        }

        // ---------------------------------------------------------------- what is in the game now

        void ShowSettings(PatchSettings s)
        {
            string preset = s.Width + "x" + s.Height;
            comboBoxResolution.Text = s.Width == 800 && s.Height == 600 ? Presets[0] : preset;
            comboBoxScaling.SelectedIndex = s.Scaling;
            comboBoxFonts.SelectedIndex = s.Fonts;
            checkBoxModernText.Checked = s.ModernText;
            comboBoxTextFont.Text = s.TextFont;

            checkBoxButtonFix.Checked = s.ButtonFix;
            numericButtonDelay.Value = s.ButtonDelay;

            checkBoxSubs.Checked = s.Subs;
            numericSubsNamed.Value = s.SubsNamed;
            numericSubsUsed.Value = s.SubsUsed;

            checkBoxAttributes.Checked = s.Highlight;
            comboBoxHighlightMode.SelectedIndex = s.ByValue ? 1 : 0;
            numericHighlightFrom.Value = s.HighlightFrom;
            buttonKeyColour.BackColor = s.Key;
            buttonSecondaryColour.BackColor = s.Secondary;
            buttonOtherColour.BackColor = s.Other;
            checkBoxBorder.Checked = s.Border;
            buttonBorderColour.BackColor = s.BorderColour;
            checkBoxClearButton.Checked = s.ClearButton;
        }

        void RefreshCurrent()
        {
            labelCurrent.ForeColor = Color.FromArgb(30, 30, 60);
            labelFonts.Text = "";
            loaded = null;
            if (!File.Exists(textBoxFile.Text))
            {
                labelCurrent.Text = "";
                UpdateEnabled();
                return;
            }

            try
            {
                if (!CM3ResolutionChanger.IsCM3Exe(textBoxFile.Text))
                {
                    labelCurrent.ForeColor = Color.Firebrick;
                    labelCurrent.Text = "This isn't a supported CM3 v3.04e cm3.exe.";
                    UpdateEnabled();
                    return;
                }

                loaded = PatchSettings.Read(textBoxFile.Text);
                labelCurrent.Text = string.Format("In the game now: {0}x{1}{2}{3}", loaded.Width, loaded.Height,
                    loaded.ModernText ? ", modern text" : "", loaded.ButtonFix ? ", button fix" : "");
                labelFonts.Text = loaded.Fonts == 2 ? "Currently: original fonts" : "Currently: sharpened";
                ShowSettings(loaded);
            }
            catch (Exception ex)
            {
                labelCurrent.ForeColor = Color.Firebrick;
                labelCurrent.Text = "Couldn't read the file: " + ex.Message;
            }
            UpdateEnabled();
        }

        static bool TryParseResolution(string text, out int width, out int height)
        {
            width = height = 0;
            string[] parts = text.Split(new char[] { 'x', 'X', ' ', '*' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 && int.TryParse(parts[0], out width) && int.TryParse(parts[1], out height);
        }

        // ---------------------------------------------------------------- applying

        // cm3.exe is normally in Program Files, which needs administrator rights to write to. The utilities don't need them for
        // anything else, so ask for them when they are needed.
        void OfferToRestartAsAdministrator()
        {
            if (MessageBox.Show(this, "CM3 Utils can't write to the game's folder without administrator rights (the game is usually in Program Files).\r\n\r\nRestart CM3 Utils as administrator now?",
                    Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            try
            {
                ProcessStartInfo start = new ProcessStartInfo(Application.ExecutablePath, "--patcher");
                start.Verb = "runas";
                start.UseShellExecute = true;
                Process.Start(start);
                Application.Exit();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // the user said no to the Windows prompt
            }
        }

        // The game's settings as they are now with the settings of one tab replaced by what is on the screen
        // (0 = Graphics, 1 = Interface, 2 = Game rules, 3 = Enhancements)
        PatchSettings Combine(int tab, PatchSettings now)
        {
            PatchSettings s = now;
            if (tab == 0)
            {
                int width, height;
                if (!TryParseResolution(comboBoxResolution.Text, out width, out height))
                    throw new FormatException("Couldn't understand that resolution. Use the form WIDTHxHEIGHT, e.g. 1600x1200.");
                s.Width = width;
                s.Height = height;
                s.Scaling = comboBoxScaling.SelectedIndex;
                s.Fonts = comboBoxFonts.SelectedIndex;
                s.ModernText = checkBoxModernText.Checked;
                s.TextFont = comboBoxTextFont.Text.Trim();
            }
            else if (tab == 1)
            {
                s.ButtonFix = checkBoxButtonFix.Checked;
                s.ButtonDelay = (int)numericButtonDelay.Value;
            }
            else if (tab == 2)
            {
                s.Subs = checkBoxSubs.Checked;
                s.SubsNamed = (int)numericSubsNamed.Value;
                s.SubsUsed = (int)numericSubsUsed.Value;
            }
            else
            {
                s.Highlight = checkBoxAttributes.Checked;
                s.ByValue = comboBoxHighlightMode.SelectedIndex == 1;
                s.HighlightFrom = (int)numericHighlightFrom.Value;
                s.Key = buttonKeyColour.BackColor;
                s.Secondary = buttonSecondaryColour.BackColor;
                s.Other = buttonOtherColour.BackColor;
                s.Border = checkBoxBorder.Checked;
                s.BorderColour = buttonBorderColour.BackColor;
                s.ClearButton = checkBoxClearButton.Checked;
            }
            return s;
        }

        void ApplyTab(int tab)
        {
            string file = textBoxFile.Text;
            if (!File.Exists(file))
            {
                MessageBox.Show(this, "Please select cm3.exe first.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                if (!CM3ResolutionChanger.IsCM3Exe(file))
                {
                    MessageBox.Show(this, "This isn't a supported CM3 v3.04e cm3.exe.", Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                PatchSettings now = PatchSettings.Read(file);
                if (tab == 3 && !now.ModernText)
                {
                    MessageBox.Show(this, "The attribute highlights and the Clear button are drawn by Modern text. Switch Modern text on and apply it on the Graphics tab first.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                PatchSettings wanted = Combine(tab, now);

                // keep a copy of the untouched exe the first time
                string backup = Path.Combine(Path.GetDirectoryName(file), "cm3_original.exe");
                bool backedUp = false;
                if (checkBoxBackup.Checked && !File.Exists(backup))
                {
                    // An exe that has been patched before is put back to the game's own behaviour in the copy (the patches are
                    // all switched off); the copy is the original game as far as playing it goes
                    File.Copy(file, backup);
                    File.SetAttributes(backup, FileAttributes.Normal);
                    CM3ResolutionChanger.Apply(backup, 800, 600, CM3ResolutionChanger.Smoothing.HighQuality, false, 0, 0, 0);
                    backedUp = true;
                }

                wanted.Write(file);
                RefreshCurrent();
                ShowDone(tab, wanted);
                if (backedUp)
                    MessageBox.Show(this, "A copy of the original game exe was saved as\r\n" + backup, Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (FormatException ex)
            {
                MessageBox.Show(this, ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (UnauthorizedAccessException)
            {
                OfferToRestartAsAdministrator();
            }
            catch (IOException ex)
            {
                MessageBox.Show(this, "Couldn't write the file (is CM3 still running?):\r\n\r\n" + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void ShowDone(int tab, PatchSettings s)
        {
            string text;
            if (tab == 0)
                text = (s.Width == 800 && s.Height == 600
                    ? "cm3.exe is at its original 800x600."
                    : string.Format("cm3.exe now runs at {0}x{1}.\r\n\r\nIf the game is in full screen, your graphics driver must support that 16-bit mode; otherwise use the -windowed option / Game Settings.", s.Width, s.Height))
                    + (s.ModernText && !(s.Width == 800 && s.Height == 600) ? "\r\n\r\nModern text is on (" + s.TextFont + ")." : "");
            else if (tab == 1)
                text = s.ButtonFix ? "The + / - and scroll button fix is on." : "The + / - and scroll buttons are back to the game's own behaviour.";
            else if (tab == 2)
                text = s.Subs ? string.Format("Substitutes: {0} named, {1} may be used (start a new game for it to apply).", s.SubsNamed, s.SubsUsed) : "The game's own substitutes rules are back.";
            else
                text = "The enhancements are set.";
            MessageBox.Show(this, text, Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
