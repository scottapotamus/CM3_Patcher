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

        static readonly Color StatusColor = Color.FromArgb(205, 210, 235);

        Panel header;
        Panel fileBar;
        Panel footer;
        Panel body;
        Panel welcome;
        Panel tiles;
        Label labelWelcome;
        Label labelHint;
        Button buttonPatcher;
        PatcherPanel patcher;
        Button buttonEditing;
        Button buttonScouting;
        Button buttonExit;
        TabControl scoutTabs;

        const int ModeHome = 0, ModePatcher = 1, ModeEditing = 2, ModeScouting = 3;

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
            ClientSize = new Size(1240, 780);
            MinimumSize = SizeFromClientSize(new Size(1000, 640));
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.DarkNavy;

            // the title bar, with the two parts of the editor
            header = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Theme.Red };
            Label title = new Label
            {
                Text = "CM3 Utils", ForeColor = Color.White, Font = new Font("Arial", 18f, FontStyle.Bold),
                Left = 16, Top = 10, AutoSize = true, Cursor = Cursors.Hand
            };
            title.Click += delegate { ShowMode(ModeHome); };
            header.Controls.Add(title);
            header.Controls.Add(new Label
            {
                Text = "by Scottapotamus", ForeColor = Theme.Yellow, Font = new Font("Arial", 9f, FontStyle.Bold),
                Left = 148, Top = 24, AutoSize = true, Cursor = Cursors.Hand
            });
            buttonPatcher = new Button { Text = "PATCHER", Width = 130, Height = 36, Top = 11, Font = new Font("Arial", 9.5f, FontStyle.Bold) };
            buttonEditing = new Button { Text = "EDITING", Width = 130, Height = 36, Top = 11, Font = new Font("Arial", 9.5f, FontStyle.Bold) };
            buttonScouting = new Button { Text = "SCOUTING", Width = 130, Height = 36, Top = 11, Font = new Font("Arial", 9.5f, FontStyle.Bold) };
            buttonExit = new Button { Text = "EXIT", Width = 80, Height = 36, Top = 11, Font = new Font("Arial", 9.5f, FontStyle.Bold) };
            buttonPatcher.Click += delegate { ChooseMode(ModePatcher); };
            buttonEditing.Click += delegate { ChooseMode(ModeEditing); };
            buttonScouting.Click += delegate { ChooseMode(ModeScouting); };
            buttonExit.Click += delegate { ExitApplication(); };
            header.Controls.Add(buttonPatcher);
            header.Controls.Add(buttonEditing);
            header.Controls.Add(buttonScouting);
            header.Controls.Add(buttonExit);
            header.Resize += delegate
            {
                buttonExit.Left = header.ClientSize.Width - buttonExit.Width - 16;
                buttonScouting.Left = buttonExit.Left - buttonScouting.Width - 24;
                buttonEditing.Left = buttonScouting.Left - buttonEditing.Width - 8;
                buttonPatcher.Left = buttonEditing.Left - buttonPatcher.Width - 8;
            };

            // the saved game
            fileBar = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Theme.DarkNavy };
            fileBar.Controls.Add(new Label { Text = "Saved game (.sav):", ForeColor = Color.White, Font = Theme.Bold, Left = 16, Top = 10, AutoSize = true });
            textBoxFile = new TextBox { Left = 16, Top = 30, Width = 1000 };
            textBoxFile.TextChanged += delegate { LoadSave(); };
            buttonBrowse = new Button { Text = "Browse...", Top = 28, Width = 90, Height = 24 };
            buttonBrowse.Click += buttonBrowse_Click;
            fileBar.Controls.Add(textBoxFile);
            fileBar.Controls.Add(buttonBrowse);
            labelStatus = new Label { Left = 16, Top = 56, Width = 1000, Height = 18, Text = "", ForeColor = StatusColor };
            fileBar.Controls.Add(labelStatus);
            fileBar.Resize += delegate
            {
                buttonBrowse.Left = fileBar.ClientSize.Width - buttonBrowse.Width - 16;
                textBoxFile.Width = buttonBrowse.Left - 8 - textBoxFile.Left;
                labelStatus.Width = fileBar.ClientSize.Width - 32;
            };

            // the editing tabs and the scouting tabs (and the first screen) share the middle
            body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 12, 12, 8), BackColor = Theme.DarkNavy };

            tabs = new TabControl { Dock = DockStyle.Fill, Visible = false };
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
            Theme.StyleTabs(tabs);

            scoutTabs = new TabControl { Dock = DockStyle.Fill, Visible = false };
            playerScouting = new ScoutingPanel("Players", "players", ScoutFields.Set, "Plays as", null);
            scoutTabs.TabPages.Add(playerScouting);
            staffScouting = new ScoutingPanel("Staff", "staff", StaffFields.Set, "Job", StaffFields.Jobs);
            scoutTabs.TabPages.Add(staffScouting);
            playerScouting.ShowHiddenChanged += delegate { staffScouting.ShowHidden = playerScouting.ShowHidden; };
            staffScouting.ShowHiddenChanged += delegate { playerScouting.ShowHidden = staffScouting.ShowHidden; };
            Theme.StyleTabs(scoutTabs);

            BuildWelcome();
            patcher = new PatcherPanel { Dock = DockStyle.Fill, Visible = false };
            body.Controls.Add(patcher);
            body.Controls.Add(welcome);
            body.Controls.Add(tabs);
            body.Controls.Add(scoutTabs);

            // writing the changes (the editing tabs only)
            footer = new Panel { Dock = DockStyle.Bottom, Height = 74, BackColor = Theme.DarkNavy, Visible = false };
            checkBoxOverwrite = new CheckBox
            {
                Text = "Overwrite this saved game (a .bak copy is made first). Otherwise a new file is saved next to it.",
                Left = 16, Top = 8, AutoSize = true, Checked = false, ForeColor = Color.White
            };
            footer.Controls.Add(checkBoxOverwrite);
            buttonApply = new Button { Text = "Apply", Left = 16, Top = 34, Width = 140, Height = 30, Enabled = false, Font = Theme.Bold };
            buttonApply.Click += buttonApply_Click;
            footer.Controls.Add(buttonApply);
            labelNote = new Label
            {
                Left = 172, Top = 34, Width = 900, Height = 34, ForeColor = StatusColor,
                Text = "Close the game first. Everything you have changed on the Clubs, Competitions, Contracts, Attributes, Nationality, People and Substitutes tabs is written in one go."
            };
            footer.Controls.Add(labelNote);

            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(fileBar);
            Controls.Add(header);

            Theme.StyleButton(buttonBrowse, true);
            Theme.StyleButton(buttonApply, true);
            Theme.StyleButton(buttonPatcher, false);
            Theme.StyleButton(buttonEditing, false);
            Theme.StyleButton(buttonScouting, false);
            Theme.StyleButton(buttonExit, true);
            buttonBrowse.Tag = buttonApply.Tag = buttonPatcher.Tag = buttonEditing.Tag = buttonScouting.Tag = buttonExit.Tag = "styled";
            Theme.Apply(this);
            ShowMode(ModeHome);
            if (startMode != ModeHome)
                ShowMode(startMode);
        }

        // The first screen: Editing or Scouting
        void BuildWelcome()
        {
            welcome = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Page };
            labelWelcome = new Label
            {
                Text = "What would you like to do?", Font = new Font("Arial", 18f, FontStyle.Bold), ForeColor = Theme.Navy, AutoSize = true
            };
            labelHint = new Label
            {
                Text = "Editing and Scouting work on a saved game: pick one with Browse above (or choose and you will be asked). Close the game first when editing or patching.",
                ForeColor = Color.FromArgb(70, 70, 100), AutoSize = true
            };
            tiles = new Panel { Width = 960, Height = 130, BackColor = Theme.Page };
            Tile patching = new Tile
            {
                Left = 0, Top = 0, Width = 306, Height = 130, Title = "PATCHER"
            };
            Tile editing = new Tile
            {
                Left = 327, Top = 0, Width = 306, Height = 130, Title = "EDITING"
            };
            Tile scouting = new Tile
            {
                Left = 654, Top = 0, Width = 306, Height = 130, Title = "SCOUTING"
            };
            patching.Click += delegate { ChooseMode(ModePatcher); };
            editing.Click += delegate { ChooseMode(ModeEditing); };
            scouting.Click += delegate { ChooseMode(ModeScouting); };
            tiles.Controls.Add(patching);
            tiles.Controls.Add(editing);
            tiles.Controls.Add(scouting);
            welcome.Controls.Add(labelWelcome);
            welcome.Controls.Add(tiles);
            welcome.Controls.Add(labelHint);
            welcome.Resize += delegate
            {
                int center = welcome.ClientSize.Width / 2;
                int top = Math.Max(20, (welcome.ClientSize.Height - 320) / 2);
                labelWelcome.Left = center - labelWelcome.Width / 2;
                labelWelcome.Top = top;
                tiles.Left = center - tiles.Width / 2;
                tiles.Top = top + 60;
                labelHint.Left = center - labelHint.Width / 2;
                labelHint.Top = tiles.Bottom + 20;
            };
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
            fileBar.Visible = mode != ModePatcher;      // the patcher picks cm3.exe, not a saved game
            ModeButton(buttonPatcher, mode == ModePatcher);
            ModeButton(buttonEditing, mode == ModeEditing);
            ModeButton(buttonScouting, mode == ModeScouting);
        }

        static void ModeButton(Button button, bool active)
        {
            button.BackColor = active ? Theme.Yellow : Theme.Navy;
            button.ForeColor = active ? Theme.Navy : Theme.Yellow;
        }

        // A tab with two tabs inside
        static TabPage NestedTabs(string title, TabPage first, TabPage second)
        {
            TabPage page = new TabPage(title);
            TabControl inner = new TabControl { Dock = DockStyle.Fill };
            inner.TabPages.Add(first);
            inner.TabPages.Add(second);
            Theme.StyleTabs(inner);
            inner.ItemSize = new Size(110, 26);
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
