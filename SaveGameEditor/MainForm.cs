using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace CM3SaveGameEditor
{
    public partial class MainForm : Form
    {
        TextBox textBoxFile;
        Button buttonBrowse;
        Label labelStatus;

        TabControl tabs;
        ScoutingPanel playerScouting;
        ScoutingPanel staffScouting;

        // Substitutes
        CheckBox checkBoxSubs;
        TextBox textBoxSummary;
        NumericUpDown numericNamed;
        NumericUpDown numericUsed;

        CheckBox checkBoxOverwrite;
        Button buttonApply;
        Label labelNote;

        SaveGameFile save;
        List<SaveGameFile.Club> clubs = new List<SaveGameFile.Club>();
        Dictionary<int, SaveGameFile.ClubEdit> edits = new Dictionary<int, SaveGameFile.ClubEdit>();      // club id -> the club as it should be
        AttributesPanel playerAttributes;
        AttributesPanel staffAttributes;
        Dictionary<int, int[]> playerAttributeEdits = new Dictionary<int, int[]>();     // staff id -> the numbers as they should be
        Dictionary<int, int[]> staffAttributeEdits = new Dictionary<int, int[]>();
        NationalityPanel playerNations;
        NationalityPanel staffNations;
        Dictionary<int, int[]> nationEdits = new Dictionary<int, int[]>();              // staff id -> { nationality, second nationality or -1 }
        ContractsPanel playerContracts;
        ContractsPanel staffContracts;
        List<SaveGameFile.Contract> contracts = new List<SaveGameFile.Contract>();
        Dictionary<int, SaveGameFile.ContractEdit> contractEdits = new Dictionary<int, SaveGameFile.ContractEdit>();   // contract record -> new clauses

        static readonly Color StatusColor = Theme.Muted;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, string lParam);

        GradientPanel sidebar;
        GradientPanel mainArea;
        Panel topBar;
        RoundPanel searchBox;
        RoundPanel footer;
        RoundPanel contentCard;
        Panel body;
        Panel welcome;
        Panel tiles;
        Label labelWelcome;
        Label labelSub;
        Label labelHint;
        Tile tilePatcher;
        Tile tileEditing;
        Tile tileScouting;
        NavItem navHome;
        NavItem navPatcher;
        NavItem navEditing;
        NavItem navScouting;
        NavItem navExit;
        NavItem navToggle;
        NavItem navSupport;
        const string SupportUrl = "https://buymeacoffee.com/scottevanso";
        bool sidebarCompact;
        PatcherPanel patcher;
        TabControl scoutTabs;

        const int ModeHome = 0, ModePatcher = 1, ModeEditing = 2, ModeScouting = 3;

        // the display's scale (1 at 96 dpi): sizes that are set while the app runs (the others scale with the form)
        float dpiScale = 1f;
        int Scaled(int value) { return (int)Math.Round(value * dpiScale); }

        public MainForm() : this(ModeHome)
        {
        }

        // startMode: the part to open at (e.g. after restarting as administrator)
        public MainForm(int startMode)
        {
            Text = "CM3 Utils by Scottapotamus";
            Font = Theme.Normal;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1280, 800);
            MinimumSize = SizeFromClientSize(new Size(1060, 680));
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Background;
            Icon = Theme.BallIcon();
            using (Graphics g = CreateGraphics())
                dpiScale = g.DpiX / 96f;

            BuildSidebar();

            mainArea = new GradientPanel
            {
                Dock = DockStyle.Fill, From = Color.FromArgb(234, 244, 252), To = Color.FromArgb(198, 223, 246), Angle = 55f, Circles = true,
                Padding = new Padding(26, 18, 26, 18)
            };

            // the top bar: the saved game, like a search box
            topBar = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Color.Transparent };
            searchBox = new RoundPanel { Left = 0, Top = 0, Height = 58, Width = 700, Radius = 22, ShadowSize = 7, ShadowAlpha = 30 };
            searchBox.Paint += delegate(object sender, PaintEventArgs e)
            {
                int icon = Theme.Px(e.Graphics, 18);
                Theme.DrawIcon(e.Graphics, "search", new Rectangle(Theme.Px(e.Graphics, 26), (searchBox.Height - icon) / 2, icon, icon), Theme.Blue, 2f * e.Graphics.DpiX / 96f);
            };
            textBoxFile = new TextBox
            {
                Left = 58, Width = 600, BorderStyle = BorderStyle.None, BackColor = Color.White, ForeColor = Theme.Text,
                Font = new Font("Segoe UI", 10.5f), Tag = "keep"
            };
            textBoxFile.Top = (searchBox.Height - textBoxFile.Height) / 2;
            textBoxFile.TextChanged += delegate { LoadSave(); };
            textBoxFile.HandleCreated += delegate { SendMessage(textBoxFile.Handle, 0x1501, (IntPtr)1, "Choose a saved game (.sav) with Browse..."); };
            searchBox.Controls.Add(textBoxFile);
            buttonBrowse = new ModernButton { Text = "Browse...", Width = 120, Height = 42, Top = 8, Kind = ButtonKind.Primary };
            buttonBrowse.Click += buttonBrowse_Click;
            labelStatus = new Label { Left = 22, Top = 60, Width = 800, Height = 20, Text = "", ForeColor = StatusColor, BackColor = Color.Transparent };
            topBar.Controls.Add(searchBox);
            topBar.Controls.Add(buttonBrowse);
            topBar.Controls.Add(labelStatus);
            topBar.Resize += delegate
            {
                buttonBrowse.Left = topBar.ClientSize.Width - buttonBrowse.Width - Scaled(6);
                searchBox.Width = buttonBrowse.Left - Scaled(14);
                textBoxFile.Width = searchBox.Width - textBoxFile.Left - Scaled(30);
                labelStatus.Width = topBar.ClientSize.Width - Scaled(30);
            };

            // the editing tabs and the scouting tabs (and the first screen) share the middle
            body = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };

            tabs = new ModernTabs { Dock = DockStyle.Fill, Visible = false };
            BuildClubsTab();
            BuildCompetitionsTab();
            Action contractsChanged = delegate { playerContracts.RefreshEdits(); staffContracts.RefreshEdits(); };
            playerContracts = new ContractsPanel("Players", false, contractEdits, contractsChanged);
            staffContracts = new ContractsPanel("Staff", true, contractEdits, contractsChanged);
            tabs.TabPages.Add(NestedTabs("Contracts", playerContracts, staffContracts));

            Action attributesChanged = delegate { };
            playerAttributes = new AttributesPanel("Players", false, ScoutFields.Set, playerAttributeEdits, attributesChanged);
            staffAttributes = new AttributesPanel("Staff", true, StaffFields.Set, staffAttributeEdits, attributesChanged);
            tabs.TabPages.Add(NestedTabs("Attributes", playerAttributes, staffAttributes));
            Action nationsChanged = delegate { };
            playerNations = new NationalityPanel("Players", false, nationEdits, nationsChanged);
            staffNations = new NationalityPanel("Staff", true, nationEdits, nationsChanged);
            tabs.TabPages.Add(NestedTabs("Nationality", playerNations, staffNations));
            BuildPeopleTab();
            BuildSubstitutesTab();

            scoutTabs = new ModernTabs { Dock = DockStyle.Fill, Visible = false };
            playerScouting = new ScoutingPanel("Players", "players", ScoutFields.Set, "Plays as", null);
            scoutTabs.TabPages.Add(playerScouting);
            staffScouting = new ScoutingPanel("Staff", "staff", StaffFields.Set, "Job", StaffFields.Jobs);
            scoutTabs.TabPages.Add(staffScouting);
            playerScouting.ShowHiddenChanged += delegate { staffScouting.ShowHidden = playerScouting.ShowHidden; };
            staffScouting.ShowHiddenChanged += delegate { playerScouting.ShowHidden = staffScouting.ShowHidden; };

            BuildWelcome();
            patcher = new PatcherPanel { Dock = DockStyle.Fill, Visible = false };
            body.Controls.Add(patcher);
            body.Controls.Add(welcome);
            body.Controls.Add(tabs);
            body.Controls.Add(scoutTabs);

            contentCard = new RoundPanel { Dock = DockStyle.Fill, Radius = 20, ShadowSize = 9, ShadowAlpha = 40, Padding = new Padding(9 + 8) };
            contentCard.Controls.Add(body);

            // writing the changes (the editing tabs only)
            footer = new RoundPanel { Dock = DockStyle.Bottom, Height = 104, Radius = 20, ShadowSize = 8, ShadowAlpha = 34, Visible = false };
            checkBoxOverwrite = new CheckBox
            {
                Text = "Overwrite this saved game (a .bak copy is made first). Otherwise a new file is saved next to it.",
                Left = 30, Top = 20, AutoSize = true, Checked = false, BackColor = Color.White
            };
            footer.Controls.Add(checkBoxOverwrite);
            buttonApply = new ModernButton { Text = "Apply", Left = 30, Top = 50, Width = 150, Height = 40, Enabled = false, Kind = ButtonKind.Primary };
            buttonApply.Click += buttonApply_Click;
            footer.Controls.Add(buttonApply);
            labelNote = new Label
            {
                Left = 200, Top = 54, Width = 900, Height = 40, ForeColor = StatusColor, BackColor = Color.White,
                Text = "Close the game first. Everything you have changed on the Clubs, Competitions, Contracts, Attributes, Nationality, People and Substitutes tabs is written in one go."
            };
            footer.Controls.Add(labelNote);
            footer.Resize += delegate { labelNote.Width = Math.Max(Scaled(200), footer.ClientSize.Width - labelNote.Left - Scaled(30)); };

            mainArea.Controls.Add(contentCard);
            mainArea.Controls.Add(footer);
            mainArea.Controls.Add(topBar);
            Controls.Add(mainArea);
            Controls.Add(sidebar);

            Theme.Apply(this);
            ShowMode(ModeHome);
            if (startMode != ModeHome)
                ShowMode(startMode);
        }

        // Never bigger than the screen
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Rectangle area = Screen.FromControl(this).WorkingArea;
            if (Width > area.Width || Height > area.Height)
            {
                Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
                Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            }
        }

        // The sidebar: the logo and the parts of the app
        void BuildSidebar()
        {
            sidebar = new GradientPanel
            {
                Dock = DockStyle.Left, Width = 240, From = Color.FromArgb(52, 128, 208), To = Theme.Navy, Angle = 80f, Circles = true
            };
            sidebar.Paint += delegate(object sender, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                int k = Theme.Px(g, 1);
                if (sidebarCompact)
                {
                    Theme.DrawBall(g, new Rectangle((sidebar.Width - Theme.Px(g, 40)) / 2, Theme.Px(g, 24), Theme.Px(g, 40), Theme.Px(g, 40)));
                    return;
                }
                Theme.DrawBall(g, new Rectangle(Theme.Px(g, 24), Theme.Px(g, 24), Theme.Px(g, 48), Theme.Px(g, 48)));
                using (Font title = new Font("Segoe UI", 16f, FontStyle.Bold))
                    TextRenderer.DrawText(g, "CM3 Utils", title, new Point(Theme.Px(g, 82), Theme.Px(g, 22)), Color.White, TextFormatFlags.NoPadding);
                using (Font small = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                    TextRenderer.DrawText(g, "by Scottapotamus", small, new Point(Theme.Px(g, 84), Theme.Px(g, 56)), Theme.Gold, TextFormatFlags.NoPadding);
                using (Font caption = new Font("Segoe UI", 8f, FontStyle.Bold))
                    TextRenderer.DrawText(g, "MENU", caption, new Point(Theme.Px(g, 28), Theme.Px(g, 108)), Color.FromArgb(170, 205, 240), TextFormatFlags.NoPadding);
            };

            navHome = new NavItem { Text = "Home", Icon = "home", Left = 0, Top = 132, Width = 240 };
            navPatcher = new NavItem { Text = "Patcher", Icon = "patcher", Left = 0, Top = 184, Width = 240 };
            navEditing = new NavItem { Text = "Editing", Icon = "editing", Left = 0, Top = 236, Width = 240 };
            navScouting = new NavItem { Text = "Scouting", Icon = "scouting", Left = 0, Top = 288, Width = 240 };
            navExit = new NavItem { Text = "Exit", Icon = "exit", Left = 0, Width = 240, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
            navHome.Click += delegate { ShowMode(ModeHome); };
            navPatcher.Click += delegate { ChooseMode(ModePatcher); };
            navEditing.Click += delegate { ChooseMode(ModeEditing); };
            navScouting.Click += delegate { ChooseMode(ModeScouting); };
            navExit.Click += delegate { ExitApplication(); };
            navToggle = new NavItem { Text = "Hide menu", Icon = "menu", Left = 0, Width = 240, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
            navToggle.Click += delegate { SetSidebarCompact(!sidebarCompact); };
            sidebar.Controls.Add(navToggle);
            navSupport = new NavItem { Text = "Support me", Icon = "heart", Left = 0, Width = 240, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
            navSupport.Click += delegate { OpenSupportPage(); };
            sidebar.Controls.Add(navSupport);
            // the logo and the name are a link too
            Rectangle logoArea = new Rectangle(0, 0, 10000, Scaled(100));
            sidebar.MouseMove += delegate(object sender, MouseEventArgs e) { sidebar.Cursor = logoArea.Contains(e.Location) ? Cursors.Hand : Cursors.Default; };
            sidebar.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left && logoArea.Contains(e.Location)) OpenSupportPage(); };
            sidebar.Controls.Add(navHome);
            sidebar.Controls.Add(navPatcher);
            sidebar.Controls.Add(navEditing);
            sidebar.Controls.Add(navScouting);
            sidebar.Controls.Add(navExit);
            sidebar.Resize += delegate
            {
                navExit.Top = sidebar.ClientSize.Height - navExit.Height - Scaled(22);
                navToggle.Top = navExit.Top - navToggle.Height - Scaled(6);
                navSupport.Top = navToggle.Top - navSupport.Height - Scaled(6);
            };
        }

        void OpenSupportPage()
        {
            try { System.Diagnostics.Process.Start(SupportUrl); }
            catch (Exception ex) { MessageBox.Show(this, "Could not open " + SupportUrl + "\n" + ex.Message, "CM3 Utils"); }
        }

        // The sidebar either shows its names or just the icons
        void SetSidebarCompact(bool compact)
        {
            sidebarCompact = compact;
            sidebar.Width = Scaled(compact ? 72 : 240);
            foreach (NavItem item in new NavItem[] { navHome, navPatcher, navEditing, navScouting, navExit, navToggle, navSupport })
            {
                item.Compact = compact;
                item.Width = sidebar.Width;
                item.Invalidate();
            }
            navToggle.Text = compact ? "Show menu" : "Hide menu";
            sidebar.Invalidate();
        }

        // The first screen: Patcher, Editing or Scouting
        void BuildWelcome()
        {
            welcome = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            labelWelcome = new Label { Text = "Welcome to CM3 Utils", Font = Theme.Title, ForeColor = Theme.Navy, AutoSize = true, BackColor = Color.White };
            labelSub = new Label
            {
                Text = "Patch the game, edit your saved games and scout players, all in one place.",
                Font = new Font("Segoe UI", 11f), ForeColor = Theme.Muted, AutoSize = true, BackColor = Color.White
            };
            labelHint = new Label
            {
                Text = "Editing and Scouting work on a saved game: pick one with Browse above (or choose and you will be asked). Close the game first when editing or patching.",
                ForeColor = Theme.Muted, AutoSize = true, BackColor = Color.White
            };
            tiles = new Panel { Width = 1000, Height = 270, BackColor = Color.White };
            tilePatcher = new Tile
            {
                Top = 0, Height = 270, Title = "Patcher", Icon = "patcher", Accent = Theme.Gold, IconColor = Theme.Navy,
                Description = "Run Championship Manager 3 at any resolution, with modern text, attribute highlights and better rules."
            };
            tileEditing = new Tile
            {
                Top = 0, Height = 270, Title = "Editing", Icon = "editing", Accent = Theme.Blue,
                Description = "Edit clubs, competitions, contracts, attributes, nationalities and people in a saved game."
            };
            tileScouting = new Tile
            {
                Top = 0, Height = 270, Title = "Scouting", Icon = "scouting", Accent = Theme.Red,
                Description = "Search every player and member of staff in a saved game and compare their numbers."
            };
            tilePatcher.Click += delegate { ChooseMode(ModePatcher); };
            tileEditing.Click += delegate { ChooseMode(ModeEditing); };
            tileScouting.Click += delegate { ChooseMode(ModeScouting); };
            tiles.Controls.Add(tilePatcher);
            tiles.Controls.Add(tileEditing);
            tiles.Controls.Add(tileScouting);
            welcome.Controls.Add(labelWelcome);
            welcome.Controls.Add(labelSub);
            welcome.Controls.Add(tiles);
            welcome.Controls.Add(labelHint);
            welcome.Resize += delegate
            {
                int width = Math.Max(Scaled(210), Math.Min(Scaled(340), (welcome.ClientSize.Width - Scaled(40)) / 3));
                int height = Scaled(270);
                tiles.Width = 3 * width;
                tiles.Height = height;
                tilePatcher.SetBounds(0, 0, width, height);
                tileEditing.SetBounds(width, 0, width, height);
                tileScouting.SetBounds(2 * width, 0, width, height);
                int center = welcome.ClientSize.Width / 2;
                int top = Math.Max(Scaled(16), (welcome.ClientSize.Height - Scaled(450)) / 2);
                labelWelcome.Left = center - labelWelcome.Width / 2;
                labelWelcome.Top = top;
                labelSub.Left = center - labelSub.Width / 2;
                labelSub.Top = labelWelcome.Bottom + Scaled(4);
                tiles.Left = center - tiles.Width / 2;
                tiles.Top = labelSub.Bottom + Scaled(30);
                labelHint.Left = Math.Max(Scaled(10), center - labelHint.Width / 2);
                labelHint.Top = tiles.Bottom + Scaled(18);
            };
        }

        // Whether a club or a competition is being renamed
        bool NamesChanged()
        {
            if (competitionEdits.Count > 0)
                return true;
            foreach (SaveGameFile.Club club in clubs)
            {
                SaveGameFile.ClubEdit edit;
                if (edits.TryGetValue(club.Id, out edit) && (edit.Name != club.Name || edit.ShortName != club.ShortName))
                    return true;
            }
            return false;
        }

        bool HasChanges()
        {
            return edits.Count > 0 || contractEdits.Count > 0 || competitionEdits.Count > 0 || newPeople.Count > 0 || removedPeople.Count > 0 || playerAttributeEdits.Count > 0 || staffAttributeEdits.Count > 0 || nationEdits.Count > 0;
        }

        // Asks first: changes that were set but not applied are lost
        void ExitApplication()
        {
            string question = "Are you sure you want to exit?";
            if (HasChanges())
                question += "\n\nYou have changes that haven't been applied yet. They will be lost.";
            if (MessageBox.Show(this, question, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                Close();
        }

        // From the first screen or the title bar: asks for a saved game first if there isn't one yet
        void ChooseMode(int mode)
        {
            if ((mode == ModeEditing || mode == ModeScouting) && save == null && textBoxFile.Text.Length == 0)
                buttonBrowse_Click(null, EventArgs.Empty);
            ShowMode(mode);
        }

        void ShowMode(int mode)
        {
            welcome.Visible = mode == ModeHome;
            patcher.Visible = mode == ModePatcher;
            tabs.Visible = mode == ModeEditing;
            scoutTabs.Visible = mode == ModeScouting;
            footer.Visible = mode == ModeEditing;
            topBar.Visible = mode != ModePatcher;       // the patcher picks cm3.exe, not a saved game
            navHome.SetActive(mode == ModeHome);
            navPatcher.SetActive(mode == ModePatcher);
            navEditing.SetActive(mode == ModeEditing);
            navScouting.SetActive(mode == ModeScouting);
        }

        // A tab with two tabs inside
        static TabPage NestedTabs(string title, TabPage first, TabPage second)
        {
            TabPage page = new TabPage(title);
            ModernTabs inner = new ModernTabs { Dock = DockStyle.Fill };
            inner.TabPages.Add(first);
            inner.TabPages.Add(second);
            inner.ItemSize = new Size(110, 32);
            page.Controls.Add(inner);
            return page;
        }

        // The list takes all the room there is: the controls above it stay at the top, the ones below it at the bottom
        internal static void StretchList(TabPage page, ListView list)
        {
            Panel top = new Panel { Dock = DockStyle.Top, Height = list.Top };
            Panel bottom = new Panel { Dock = DockStyle.Bottom };
            int listBottom = list.Bottom;
            int lowest = listBottom;

            List<Control> above = new List<Control>();
            List<Control> below = new List<Control>();
            foreach (Control control in page.Controls)
            {
                if (control == list)
                    continue;
                (control.Top < list.Top ? above : below).Add(control);
            }
            foreach (Control control in above)
            {
                page.Controls.Remove(control);
                top.Controls.Add(control);
            }
            foreach (Control control in below)
            {
                page.Controls.Remove(control);
                control.Top -= listBottom;
                bottom.Controls.Add(control);
                lowest = Math.Max(lowest, control.Bottom + listBottom);
            }
            bottom.Height = lowest - listBottom + 6;

            page.Controls.Add(top);
            page.Controls.Add(bottom);
            list.Anchor = AnchorStyles.None;
            list.Dock = DockStyle.Fill;
            list.BringToFront();
        }

        void BuildSubstitutesTab()
        {
            TabPage page = new TabPage("Substitutes");
            tabs.TabPages.Add(page);

            checkBoxSubs = new CheckBox
            {
                Text = "Change the substitutes rules of every fixture in the save", Left = 10, Top = 12, AutoSize = true, Checked = false
            };
            checkBoxSubs.CheckedChanged += delegate { numericNamed.Enabled = numericUsed.Enabled = checkBoxSubs.Checked; };
            page.Controls.Add(checkBoxSubs);

            page.Controls.Add(new Label { Text = "Named on the bench:", Left = 30, Top = 42, AutoSize = true });
            numericNamed = new NumericUpDown { Left = 148, Top = 38, Width = 50, Minimum = 1, Maximum = SaveGameFile.MaxSubstitutes, Value = 9, Enabled = false };
            page.Controls.Add(numericNamed);
            page.Controls.Add(new Label { Text = "Substitutions allowed:", Left = 234, Top = 42, AutoSize = true });
            numericUsed = new NumericUpDown { Left = 368, Top = 38, Width = 50, Minimum = 1, Maximum = SaveGameFile.MaxSubstitutes, Value = 5, Enabled = false };
            page.Controls.Add(numericUsed);
            page.Controls.Add(new Label { Text = "Fixtures that allow no substitutes at all are left alone.", Left = 30, Top = 70, AutoSize = true });

            textBoxSummary = new TextBox
            {
                Left = 10, Top = 98, Width = 468, Height = 270, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = SystemColors.Window
            };
            page.Controls.Add(textBoxSummary);
        }

        // Saved games end up in the VirtualStore when the game is installed in Program Files
        static string DefaultFolder()
        {
            string virtualStore = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"VirtualStore\Program Files (x86)\Championship Manager 3");
            if (Directory.Exists(virtualStore))
                return virtualStore;
            if (Directory.Exists(@"E:\Championship Manager 3"))
                return @"E:\Championship Manager 3";
            string install = Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? "", "Championship Manager 3");
            return Directory.Exists(install) ? install : null;
        }

        void buttonBrowse_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Select a saved game";
                dialog.Filter = "Championship Manager 3 saved games (*.sav)|*.sav|All files (*.*)|*.*";
                if (File.Exists(textBoxFile.Text))
                    dialog.InitialDirectory = Path.GetDirectoryName(textBoxFile.Text);
                else if (DefaultFolder() != null)
                    dialog.InitialDirectory = DefaultFolder();
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    textBoxFile.Text = dialog.FileName;
            }
        }

        void LoadSave()
        {
            save = null;
            clubs.Clear();
            edits.Clear();
            contracts.Clear();
            contractEdits.Clear();
            playerScouting.SetPeople(new List<ScoutPlayer>());
            staffScouting.SetPeople(new List<ScoutPlayer>());
            buttonApply.Enabled = false;
            labelStatus.ForeColor = StatusColor;
            labelStatus.Text = "";
            textBoxSummary.Text = "";
            listClubs.Items.Clear();
            competitionEdits.Clear();
            competitions.Clear();
            FillCompetitions();
            ShowSelectedCompetition();
            UpdateCompetitionEdits();
            playerAttributeEdits.Clear();
            staffAttributeEdits.Clear();
            playerAttributes.SetPeople(new List<ScoutPlayer>());
            staffAttributes.SetPeople(new List<ScoutPlayer>());
            nationEdits.Clear();
            playerNations.SetPeople(new List<ScoutPlayer>());
            staffNations.SetPeople(new List<ScoutPlayer>());
            SetPeopleLists(new List<ScoutPlayer>(), new List<ScoutPlayer>());
            playerContracts.SetClubs(clubs);
            staffContracts.SetClubs(clubs);
            playerContracts.SetContracts(contracts);
            staffContracts.SetContracts(contracts);
            UpdateEdits();
            ShowSelectedClub();
            if (!File.Exists(textBoxFile.Text))
                return;

            Cursor = Cursors.WaitCursor;
            try
            {
                save = SaveGameFile.Load(textBoxFile.Text);

                int fixtures;
                SortedDictionary<int, int> summary = save.RulesSummary(out fixtures);
                List<SaveGameFile.Entry> lists = save.FixtureLists();
                StringBuilder text = new StringBuilder();
                text.AppendFormat("{0} fixtures in {1} fixture list(s):", fixtures, lists.Count).AppendLine();
                foreach (SaveGameFile.Entry entry in lists)
                    text.AppendLine("    " + entry.Name);
                text.AppendLine();
                text.AppendLine("Substitutes rules now:");
                foreach (KeyValuePair<int, int> pair in summary)
                {
                    if (pair.Key == 0)
                        text.AppendFormat("    no substitutes: {0} fixtures", pair.Value).AppendLine();
                    else
                        text.AppendFormat("    {0} named, {1} allowed: {2} fixtures", SaveGameFile.Named(pair.Key), SaveGameFile.Used(pair.Key), pair.Value).AppendLine();
                }
                textBoxSummary.Text = text.ToString();

                clubs = save.Clubs();
                clubs.Sort(delegate(SaveGameFile.Club a, SaveGameFile.Club b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });
                FillClubs();

                contracts = save.Contracts(false);
                contracts.Sort(delegate(SaveGameFile.Contract a, SaveGameFile.Contract b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });
                playerContracts.SetClubs(clubs);
                staffContracts.SetClubs(clubs);
                playerContracts.SetContracts(contracts);
                staffContracts.SetContracts(contracts);

                competitions = save.Competitions();
                FillCompetitions();

                List<ScoutPlayer> players = save.Players();
                playerScouting.SetPeople(players);
                playerAttributes.SetPeople(players);
                List<ScoutPlayer> staffMembers = save.StaffMembers();
                staffScouting.SetPeople(staffMembers);
                staffAttributes.SetPeople(staffMembers);
                List<KeyValuePair<int, string>> nationList = save.NationList();
                playerNations.SetNations(nationList);
                staffNations.SetNations(nationList);
                playerNations.SetPeople(players);
                staffNations.SetPeople(staffMembers);
                SetPeopleLists(players, staffMembers);

                labelStatus.Text = string.Format("{0} sub-files, {1:0.0} MB, {2} clubs, {3} contracts, {4} players, {5} staff", save.Entries.Count, new FileInfo(textBoxFile.Text).Length / 1048576.0, clubs.Count, contracts.Count, players.Count, staffMembers.Count);
                buttonApply.Enabled = true;
            }
            catch (Exception ex)
            {
                save = null;
                clubs.Clear();
                contracts.Clear();
                playerScouting.SetPeople(new List<ScoutPlayer>());
                staffScouting.SetPeople(new List<ScoutPlayer>());
                playerAttributes.SetPeople(new List<ScoutPlayer>());
                staffAttributes.SetPeople(new List<ScoutPlayer>());
                playerNations.SetPeople(new List<ScoutPlayer>());
                staffNations.SetPeople(new List<ScoutPlayer>());
                labelStatus.ForeColor = Color.Salmon;
                labelStatus.Text = "Couldn't read this saved game: " + ex.Message;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        internal static string Money(long amount)
        {
            return amount.ToString("N0", CultureInfo.CurrentCulture);
        }

        // After writing: what was changed has to be in the file the way it was asked for (the attributes the game scales with the
        // current ability can differ a little: the editor can't always hit the number exactly)
        static void CheckAttributes(Dictionary<int, int[]> wanted, Dictionary<int, ScoutPlayer> now, ScoutFieldSet set, bool player)
        {
            foreach (KeyValuePair<int, int[]> pair in wanted)
            {
                ScoutPlayer person;
                if (!now.TryGetValue(pair.Key, out person))
                    throw new InvalidDataException("The saved game was written but the check afterwards failed - don't use it.");
                for (int f = 0; f < set.All.Length; f++)
                {
                    if (f == ScoutFieldSet.Age || f == ScoutFieldSet.Wage || (player && ScoutFields.IsScaled(f)))
                        continue;
                    if (person.Values[f] != pair.Value[f])
                        throw new InvalidDataException("The saved game was written but the check afterwards failed - don't use it.");
                }
            }
        }

        void buttonApply_Click(object sender, EventArgs e)
        {
            if (save == null)
                return;

            bool changeSubs = checkBoxSubs.Checked;
            if (!changeSubs && !HasChanges())
            {
                MessageBox.Show(this, "There is nothing to change yet: change a club, a contract, a competition, someone's attributes or nationality, the people in the game, or tick the substitutes option.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (removedPeople.Count > 0 &&
                MessageBox.Show(this, string.Format("You are about to remove {0} player(s) / staff from the game. This can't be undone once it is written" +
                    (checkBoxOverwrite.Checked ? " (and you chose to overwrite the saved game; only the .bak copy keeps them)." : " (the original saved game is kept as it is).") +
                    "\n\nContinue?", removedPeople.Count), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            if (NamesChanged() &&
                MessageBox.Show(this, "You have renamed clubs or competitions. Editing names may break your saved game (for example, other " +
                    "parts of the game may still look things up by the old name).\n\nMake sure you have a backup" +
                    (checkBoxOverwrite.Checked ? " (a .bak copy is made when overwriting)" : " (the original saved game is kept as it is)") +
                    ".\n\nContinue?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            string source = textBoxFile.Text;
            int named = (int)numericNamed.Value;
            int used = (int)numericUsed.Value;
            bool overwrite = checkBoxOverwrite.Checked;
            string target = overwrite
                ? source
                : Path.Combine(Path.GetDirectoryName(source), Path.GetFileNameWithoutExtension(source) + "_edited" + Path.GetExtension(source));

            if (!overwrite && File.Exists(target) &&
                MessageBox.Show(this, Path.GetFileName(target) + " already exists. Replace it?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            Cursor = Cursors.WaitCursor;
            try
            {
                Dictionary<SaveGameFile.Entry, byte[]> replaced = new Dictionary<SaveGameFile.Entry, byte[]>();
                int fixtures = 0, fixturesChanged = 0, balancesChanged = 0, contractsChanged = 0, attributesChanged = 0, competitionsChanged = 0, peopleAdded = 0, peopleRemoved = 0, nationsChanged = 0;
                if (changeSubs)
                    fixturesChanged = save.ReplaceSubstitutes(named, used, replaced, out fixtures);
                if (edits.Count > 0)
                    balancesChanged = save.ReplaceClubs(edits, replaced);
                if (contractEdits.Count > 0)
                    contractsChanged = save.ReplaceContracts(contractEdits, replaced);
                if (competitionEdits.Count > 0)
                    competitionsChanged = save.ReplaceCompetitions(competitionEdits, replaced);
                if (newPeople.Count > 0)
                    peopleAdded = save.AddPeople(newPeople, replaced);
                if (removedPeople.Count > 0)
                    peopleRemoved = save.RemovePeople(new List<int>(removedPeople.Keys), replaced);
                if (playerAttributeEdits.Count > 0 || staffAttributeEdits.Count > 0)
                    attributesChanged = save.ReplaceAttributes(playerAttributeEdits, staffAttributeEdits, replaced);

                if (nationEdits.Count > 0)
                    nationsChanged = save.ReplaceNations(nationEdits, replaced);

                string written = overwrite ? source + ".new" : target;
                save.WriteFile(written, replaced);

                // Read it back to make sure it is a valid saved game with the changes in it
                SaveGameFile check = SaveGameFile.Load(written);
                if (changeSubs)
                {
                    int checkedFixtures;
                    SortedDictionary<int, int> summary = check.RulesSummary(out checkedFixtures);
                    int wanted = (used << 4) | named;
                    foreach (KeyValuePair<int, int> pair in summary)
                        if (pair.Key != 0 && pair.Key != wanted)
                            throw new InvalidDataException("The saved game was written but the check afterwards failed - don't use it.");
                    if (checkedFixtures != fixtures)
                        throw new InvalidDataException("The saved game was written but the check afterwards failed - don't use it.");
                }
                if (edits.Count > 0)
                {
                    Dictionary<int, SaveGameFile.Club> now = new Dictionary<int, SaveGameFile.Club>();
                    foreach (SaveGameFile.Club club in check.Clubs())
                        now[club.Id] = club;
                    foreach (KeyValuePair<int, SaveGameFile.ClubEdit> pair in edits)
                    {
                        SaveGameFile.Club club;
                        if (!now.TryGetValue(pair.Key, out club) || !pair.Value.SameAs(club))
                            throw new InvalidDataException("The saved game was written but the check afterwards failed - don't use it.");
                    }
                }

                if (contractEdits.Count > 0)
                {
                    Dictionary<int, SaveGameFile.Contract> now = new Dictionary<int, SaveGameFile.Contract>();
                    foreach (SaveGameFile.Contract contract in check.Contracts(false))
                        now[contract.Record] = contract;
                    foreach (KeyValuePair<int, SaveGameFile.ContractEdit> pair in contractEdits)
                    {
                        // a contract left with no clauses gets a fee of 0
                        SaveGameFile.ContractEdit expected = pair.Value;
                        SaveGameFile.Contract contract;
                        bool ok = now.TryGetValue(pair.Key, out contract);
                        if (ok)
                        {
                            bool any = false;
                            foreach (byte flag in expected.Flags)
                                if (flag != 0)
                                    any = true;
                            int fee = expected.Fee;
                            expected.Fee = any ? fee : 0;
                            ok = expected.SameAs(contract);
                            expected.Fee = fee;
                        }
                        if (!ok)
                            throw new InvalidDataException("The saved game was written but the check afterwards failed - don't use it.");
                    }
                }

                if (newPeople.Count > 0 || removedPeople.Count > 0)
                {
                    List<ScoutPlayer> everyone = check.Players();
                    everyone.AddRange(check.StaffMembers());
                    foreach (NewPerson person in newPeople)
                    {
                        bool found = false;
                        foreach (ScoutPlayer existing in everyone)
                            if (existing.Name == person.Name && existing.Values[ScoutFieldSet.CurrentAbility] == person.CurrentAbility && existing.Club == person.ClubName)
                                found = true;
                        if (!found)
                            throw new InvalidDataException("The saved game was written but the check afterwards failed - don't use it.");
                    }
                    foreach (ScoutPlayer existing in everyone)
                        if (removedPeople.ContainsKey(existing.StaffId))
                            throw new InvalidDataException("The saved game was written but the check afterwards failed - don't use it.");
                }

                if (competitionEdits.Count > 0)
                {
                    Dictionary<int, Competition> now = new Dictionary<int, Competition>();
                    foreach (Competition competition in check.Competitions())
                        now[competition.Id] = competition;
                    foreach (KeyValuePair<int, CompetitionEdit> pair in competitionEdits)
                    {
                        Competition competition;
                        if (!now.TryGetValue(pair.Key, out competition) || !pair.Value.SameAs(competition))
                            throw new InvalidDataException("The saved game was written but the check afterwards failed - don't use it.");
                    }
                }

                if (playerAttributeEdits.Count > 0)
                {
                    Dictionary<int, ScoutPlayer> now = new Dictionary<int, ScoutPlayer>();
                    foreach (ScoutPlayer person in check.Players())
                        now[person.StaffId] = person;
                    CheckAttributes(playerAttributeEdits, now, ScoutFields.Set, true);
                }
                if (staffAttributeEdits.Count > 0)
                {
                    Dictionary<int, ScoutPlayer> now = new Dictionary<int, ScoutPlayer>();
                    foreach (ScoutPlayer person in check.StaffMembers())
                        now[person.StaffId] = person;
                    CheckAttributes(staffAttributeEdits, now, StaffFields.Set, false);
                }

                if (nationEdits.Count > 0)
                {
                    Dictionary<int, ScoutPlayer> now = new Dictionary<int, ScoutPlayer>();
                    foreach (ScoutPlayer person in check.Players())
                        now[person.StaffId] = person;
                    foreach (ScoutPlayer person in check.StaffMembers())
                        now[person.StaffId] = person;
                    foreach (KeyValuePair<int, int[]> pair in nationEdits)
                    {
                        ScoutPlayer person;
                        if (!now.TryGetValue(pair.Key, out person) || person.NationId != pair.Value[0] || person.SecondNationId != pair.Value[1])
                            throw new InvalidDataException("The saved game was written but the check afterwards failed - don't use it.");
                    }
                }

                if (overwrite)
                {
                    string backup = source + ".bak";
                    if (!File.Exists(backup))
                        File.Copy(source, backup);
                    File.Delete(source);
                    File.Move(written, source);
                }

                StringBuilder message = new StringBuilder();
                if (changeSubs)
                    message.AppendFormat("{0} of {1} fixtures now allow {2} named and {3} substitutions.", fixturesChanged, fixtures, named, used).AppendLine();
                if (edits.Count > 0)
                    message.AppendFormat("{0} club(s) changed.", balancesChanged).AppendLine();
                if (contractEdits.Count > 0)
                    message.AppendFormat("{0} contract(s) changed.", contractsChanged).AppendLine();
                if (competitionEdits.Count > 0)
                    message.AppendFormat("{0} competition(s) renamed.", competitionsChanged).AppendLine();
                if (newPeople.Count > 0)
                    message.AppendFormat("{0} new player(s) / staff added.", peopleAdded).AppendLine();
                if (removedPeople.Count > 0)
                    message.AppendFormat("{0} player(s) / staff removed.", peopleRemoved).AppendLine();
                if (playerAttributeEdits.Count > 0 || staffAttributeEdits.Count > 0)
                    message.AppendFormat("{0} player(s) / staff changed in their attributes.", attributesChanged).AppendLine();
                if (nationEdits.Count > 0)
                    message.AppendFormat("{0} player(s) / staff changed in their nationality.", nationsChanged).AppendLine();
                message.AppendLine().AppendFormat("Saved as {0}", target);
                if (overwrite)
                    message.AppendLine().AppendFormat("(the original is kept as {0}.bak)", source);
                MessageBox.Show(this, message.ToString(), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);

                if (overwrite)
                    LoadSave();
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show(this, "Access denied. Is the saved game in a protected folder?", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }
    }
}
