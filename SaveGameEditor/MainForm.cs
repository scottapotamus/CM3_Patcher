using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace CM3SaveGameEditor
{
    public class MainForm : Form
    {
        // The game's amounts are mostly 32 bit numbers, so stay inside that
        const decimal MaxBalance = 2000000000m;

        TextBox textBoxFile;
        Button buttonBrowse;
        Label labelStatus;

        TabControl tabs;

        // Club finances
        TextBox textBoxFilter;
        ListView listClubs;
        NumericUpDown numericBalance;
        Button buttonSetBalance;
        Button buttonAdd1;
        Button buttonAdd10;
        Button buttonAdd50;
        Button buttonClearEdits;
        Label labelEdits;

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
        Dictionary<int, long> edits = new Dictionary<int, long>();      // club id -> new balance

        public MainForm()
        {
            Text = "Scottapotamus CM3 Save Game Editor";
            ClientSize = new Size(520, 586);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            Controls.Add(new Label { Text = "Saved game (.sav):", Left = 12, Top = 14, AutoSize = true });
            textBoxFile = new TextBox { Left = 12, Top = 34, Width = 420 };
            textBoxFile.TextChanged += delegate { LoadSave(); };
            buttonBrowse = new Button { Text = "Browse...", Left = 440, Top = 32, Width = 68 };
            buttonBrowse.Click += buttonBrowse_Click;
            Controls.Add(textBoxFile);
            Controls.Add(buttonBrowse);

            labelStatus = new Label { Left = 12, Top = 66, Width = 496, Height = 18, Text = "" };
            Controls.Add(labelStatus);

            tabs = new TabControl { Left = 12, Top = 90, Width = 496, Height = 390 };
            Controls.Add(tabs);

            BuildFinanceTab();
            BuildSubstitutesTab();

            checkBoxOverwrite = new CheckBox
            {
                Text = "Overwrite this saved game (a .bak copy is made first). Otherwise a new file is saved next to it.",
                Left = 12, Top = 490, AutoSize = true, Checked = false
            };
            Controls.Add(checkBoxOverwrite);

            buttonApply = new Button { Text = "Apply", Left = 12, Top = 522, Width = 140, Height = 28, Enabled = false };
            buttonApply.Click += buttonApply_Click;
            Controls.Add(buttonApply);

            labelNote = new Label
            {
                Left = 164, Top = 518, Width = 344, Height = 64,
                Text = "Close the game first. Everything you have changed on both tabs is written in one go."
            };
            Controls.Add(labelNote);
        }

        void BuildFinanceTab()
        {
            TabPage page = new TabPage("Club finances");
            tabs.TabPages.Add(page);

            page.Controls.Add(new Label { Text = "Find a club:", Left = 10, Top = 14, AutoSize = true });
            textBoxFilter = new TextBox { Left = 80, Top = 10, Width = 200 };
            textBoxFilter.TextChanged += delegate { FillClubs(); };
            page.Controls.Add(textBoxFilter);

            listClubs = new ListView
            {
                Left = 10, Top = 38, Width = 468, Height = 200,
                View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, GridLines = true
            };
            listClubs.Columns.Add("Club", 210);
            listClubs.Columns.Add("Balance", 120, HorizontalAlignment.Right);
            listClubs.Columns.Add("New balance", 120, HorizontalAlignment.Right);
            listClubs.SelectedIndexChanged += delegate { ShowSelectedClub(); };
            page.Controls.Add(listClubs);

            page.Controls.Add(new Label { Text = "Balance:", Left = 10, Top = 252, AutoSize = true });
            numericBalance = new NumericUpDown
            {
                Left = 66, Top = 248, Width = 130, Minimum = -MaxBalance, Maximum = MaxBalance,
                ThousandsSeparator = true, Increment = 100000, Enabled = false
            };
            page.Controls.Add(numericBalance);
            buttonSetBalance = new Button { Text = "Set", Left = 204, Top = 246, Width = 50, Enabled = false };
            buttonSetBalance.Click += delegate { SetSelectedBalance(numericBalance.Value); };
            page.Controls.Add(buttonSetBalance);

            page.Controls.Add(new Label { Text = "Add:", Left = 268, Top = 252, AutoSize = true });
            buttonAdd1 = new Button { Text = "+1m", Left = 300, Top = 246, Width = 52, Enabled = false };
            buttonAdd10 = new Button { Text = "+10m", Left = 356, Top = 246, Width = 52, Enabled = false };
            buttonAdd50 = new Button { Text = "+50m", Left = 412, Top = 246, Width = 52, Enabled = false };
            buttonAdd1.Click += delegate { AddToSelectedBalance(1000000m); };
            buttonAdd10.Click += delegate { AddToSelectedBalance(10000000m); };
            buttonAdd50.Click += delegate { AddToSelectedBalance(50000000m); };
            page.Controls.Add(buttonAdd1);
            page.Controls.Add(buttonAdd10);
            page.Controls.Add(buttonAdd50);

            labelEdits = new Label { Left = 10, Top = 284, Width = 380, Height = 18, Text = "" };
            page.Controls.Add(labelEdits);
            buttonClearEdits = new Button { Text = "Clear all", Left = 404, Top = 280, Width = 74, Enabled = false };
            buttonClearEdits.Click += delegate { edits.Clear(); FillClubs(); UpdateEdits(); };
            page.Controls.Add(buttonClearEdits);

            page.Controls.Add(new Label
            {
                Left = 10, Top = 312, Width = 468, Height = 64,
                Text = "Transfer Funds aren't stored in the save: the game works them out from the balance (and the club's wages and " +
                       "reputation), so give a club a bigger balance to give it more to spend. In one test a club's Transfer Funds went " +
                       "from 30,000 to 2,300,000 when its balance went from 297,435 to 5,297,435."
            });
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
                Left = 10, Top = 98, Width = 468, Height = 270,
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
            buttonApply.Enabled = false;
            labelStatus.ForeColor = SystemColors.ControlText;
            labelStatus.Text = "";
            textBoxSummary.Text = "";
            listClubs.Items.Clear();
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

                labelStatus.Text = string.Format("{0} sub-files, {1:0.0} MB, {2} clubs", save.Entries.Count, new FileInfo(textBoxFile.Text).Length / 1048576.0, clubs.Count);
                buttonApply.Enabled = true;
            }
            catch (Exception ex)
            {
                save = null;
                clubs.Clear();
                labelStatus.ForeColor = Color.Firebrick;
                labelStatus.Text = "Couldn't read this saved game: " + ex.Message;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        static string Money(long amount)
        {
            return amount.ToString("N0", CultureInfo.CurrentCulture);
        }

        // The clubs matching the filter
        void FillClubs()
        {
            string filter = textBoxFilter.Text.Trim();
            int selectedId = SelectedClubId();

            listClubs.BeginUpdate();
            listClubs.Items.Clear();
            foreach (SaveGameFile.Club club in clubs)
            {
                if (filter.Length > 0 && club.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0)
                    continue;
                ListViewItem item = new ListViewItem(club.Name);
                item.SubItems.Add(Money(club.Balance));
                long edited;
                item.SubItems.Add(edits.TryGetValue(club.Id, out edited) ? Money(edited) : "");
                item.Tag = club;
                listClubs.Items.Add(item);
                if (club.Id == selectedId)
                    item.Selected = true;
            }
            listClubs.EndUpdate();
        }

        int SelectedClubId()
        {
            if (listClubs.SelectedItems.Count == 0)
                return -1;
            return ((SaveGameFile.Club)listClubs.SelectedItems[0].Tag).Id;
        }

        void ShowSelectedClub()
        {
            bool any = listClubs.SelectedItems.Count > 0;
            numericBalance.Enabled = buttonSetBalance.Enabled = buttonAdd1.Enabled = buttonAdd10.Enabled = buttonAdd50.Enabled = any;
            if (!any)
                return;

            SaveGameFile.Club club = (SaveGameFile.Club)listClubs.SelectedItems[0].Tag;
            long balance;
            if (!edits.TryGetValue(club.Id, out balance))
                balance = club.Balance;
            numericBalance.Value = Math.Max(numericBalance.Minimum, Math.Min(numericBalance.Maximum, balance));
        }

        void SetSelectedBalance(decimal value)
        {
            if (listClubs.SelectedItems.Count == 0)
                return;
            ListViewItem item = listClubs.SelectedItems[0];
            SaveGameFile.Club club = (SaveGameFile.Club)item.Tag;
            long balance = (long)Math.Max(-MaxBalance, Math.Min(MaxBalance, value));

            if (balance == club.Balance)
            {
                edits.Remove(club.Id);
                item.SubItems[2].Text = "";
            }
            else
            {
                edits[club.Id] = balance;
                item.SubItems[2].Text = Money(balance);
            }
            numericBalance.Value = balance;
            UpdateEdits();
        }

        void AddToSelectedBalance(decimal amount)
        {
            if (listClubs.SelectedItems.Count == 0)
                return;
            SetSelectedBalance(numericBalance.Value + amount);
        }

        void UpdateEdits()
        {
            labelEdits.Text = edits.Count == 0 ? "No balances changed yet." : string.Format("{0} club balance(s) changed, not saved yet.", edits.Count);
            buttonClearEdits.Enabled = edits.Count > 0;
        }

        void buttonApply_Click(object sender, EventArgs e)
        {
            if (save == null)
                return;

            bool changeSubs = checkBoxSubs.Checked;
            if (!changeSubs && edits.Count == 0)
            {
                MessageBox.Show(this, "There is nothing to change yet: set a club's balance, or tick the substitutes option.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

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
                int fixtures = 0, fixturesChanged = 0, balancesChanged = 0;
                if (changeSubs)
                    fixturesChanged = save.ReplaceSubstitutes(named, used, replaced, out fixtures);
                if (edits.Count > 0)
                    balancesChanged = save.ReplaceBalances(edits, replaced);

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
                    Dictionary<int, long> now = new Dictionary<int, long>();
                    foreach (SaveGameFile.Club club in check.Clubs())
                        now[club.Id] = club.Balance;
                    foreach (KeyValuePair<int, long> pair in edits)
                    {
                        long balance;
                        if (!now.TryGetValue(pair.Key, out balance) || balance != pair.Value)
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
                    message.AppendFormat("{0} club balance(s) changed.", balancesChanged).AppendLine();
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
