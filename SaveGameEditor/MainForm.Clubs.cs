using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace CM3SaveGameEditor
{
    // The "Clubs" tab: club name, short name, reputation, bank balance and the club's stadium (name and size)
    public partial class MainForm
    {
        // The game's amounts are mostly 32 bit numbers, so stay inside that
        const decimal MaxBalance = 2000000000m;
        const decimal MaxCapacity = 1000000m;

        TextBox textBoxFilter;
        ListView listClubs;
        TextBox textBoxClubName;
        TextBox textBoxShortName;
        NumericUpDown numericReputation;
        NumericUpDown numericBalance;
        Button buttonAdd1;
        Button buttonAdd10;
        Button buttonAdd50;
        TextBox textBoxStadium;
        NumericUpDown numericCapacity;
        NumericUpDown numericSeated;
        Label labelStadiumNote;
        Button buttonSetClub;
        Button buttonClearEdits;
        Label labelEdits;

        void BuildClubsTab()
        {
            TabPage page = new TabPage("Clubs");
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
            listClubs.Columns.Add("Club", 230);
            listClubs.Columns.Add("Reputation", 80, HorizontalAlignment.Right);
            listClubs.Columns.Add("Stadium", 190);
            listClubs.Columns.Add("Capacity", 80, HorizontalAlignment.Right);
            listClubs.Columns.Add("Balance", 110, HorizontalAlignment.Right);
            listClubs.SelectedIndexChanged += delegate { ShowSelectedClub(); };
            page.Controls.Add(listClubs);

            GroupBox group = new CardBox { Text = "The selected club  -  warning: renaming a club may break your saved game", Left = 10, Top = 248, Width = 760, Height = 190 };
            page.Controls.Add(group);

            group.Controls.Add(new Label { Text = "Club name:", Left = 10, Top = 28, AutoSize = true });
            textBoxClubName = new TextBox { Left = 96, Top = 24, Width = 300, MaxLength = SaveGameFile.MaxClubName, Enabled = false };
            group.Controls.Add(textBoxClubName);
            group.Controls.Add(new Label { Text = "Short name:", Left = 420, Top = 28, AutoSize = true });
            textBoxShortName = new TextBox { Left = 496, Top = 24, Width = 170, MaxLength = SaveGameFile.MaxShortName, Enabled = false };
            group.Controls.Add(textBoxShortName);

            group.Controls.Add(new Label { Text = "Reputation:", Left = 10, Top = 60, AutoSize = true });
            numericReputation = new NumericUpDown { Left = 96, Top = 56, Width = 90, Minimum = short.MinValue, Maximum = short.MaxValue, Enabled = false };
            group.Controls.Add(numericReputation);
            group.Controls.Add(new Label { Text = "(0 to 10,000: the biggest clubs are over 9,000)", Left = 196, Top = 60, AutoSize = true });

            group.Controls.Add(new Label { Text = "Bank balance:", Left = 10, Top = 92, AutoSize = true });
            numericBalance = new NumericUpDown
            {
                Left = 96, Top = 88, Width = 130, Minimum = -MaxBalance, Maximum = MaxBalance,
                ThousandsSeparator = true, Increment = 100000, Enabled = false
            };
            group.Controls.Add(numericBalance);
            group.Controls.Add(new Label { Text = "Add:", Left = 244, Top = 92, AutoSize = true });
            buttonAdd1 = new ModernButton { Text = "+1m", Left = 276, Top = 86, Width = 52, Enabled = false };
            buttonAdd10 = new ModernButton { Text = "+10m", Left = 332, Top = 86, Width = 52, Enabled = false };
            buttonAdd50 = new ModernButton { Text = "+50m", Left = 388, Top = 86, Width = 52, Enabled = false };
            buttonAdd1.Click += delegate { AddToBalance(1000000m); };
            buttonAdd10.Click += delegate { AddToBalance(10000000m); };
            buttonAdd50.Click += delegate { AddToBalance(50000000m); };
            group.Controls.Add(buttonAdd1);
            group.Controls.Add(buttonAdd10);
            group.Controls.Add(buttonAdd50);

            group.Controls.Add(new Label { Text = "Stadium:", Left = 10, Top = 124, AutoSize = true });
            textBoxStadium = new TextBox { Left = 96, Top = 120, Width = 300, MaxLength = SaveGameFile.MaxStadiumName, Enabled = false };
            group.Controls.Add(textBoxStadium);
            group.Controls.Add(new Label { Text = "Capacity:", Left = 420, Top = 124, AutoSize = true });
            numericCapacity = new NumericUpDown { Left = 476, Top = 120, Width = 90, Minimum = 0, Maximum = MaxCapacity, ThousandsSeparator = true, Increment = 500, Enabled = false };
            group.Controls.Add(numericCapacity);
            group.Controls.Add(new Label { Text = "Seated:", Left = 580, Top = 124, AutoSize = true });
            numericSeated = new NumericUpDown { Left = 628, Top = 120, Width = 90, Minimum = 0, Maximum = MaxCapacity, ThousandsSeparator = true, Increment = 500, Enabled = false };
            group.Controls.Add(numericSeated);
            labelStadiumNote = new Label { Left = 96, Top = 148, Width = 650, Height = 34, Text = "" };
            group.Controls.Add(labelStadiumNote);

            buttonSetClub = new ModernButton { Text = "Set", Left = 10, Top = 446, Width = 80, Enabled = false };
            buttonSetClub.Click += delegate { SetSelectedClub(); };
            page.Controls.Add(buttonSetClub);
            labelEdits = new Label { Left = 100, Top = 452, Width = 380, Height = 18, Text = "" };
            page.Controls.Add(labelEdits);
            buttonClearEdits = new ModernButton { Text = "Clear all", Left = 500, Top = 446, Width = 74, Enabled = false };
            buttonClearEdits.Click += delegate { edits.Clear(); FillClubs(); ShowSelectedClub(); UpdateEdits(); };
            page.Controls.Add(buttonClearEdits);

            StretchList(page, listClubs);
        }

        SaveGameFile.ClubEdit EditOf(SaveGameFile.Club club)
        {
            SaveGameFile.ClubEdit edit;
            return edits.TryGetValue(club.Id, out edit) ? edit : club.ToEdit();
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
                listClubs.Items.Add(item);
                ShowClubRow(item, club);
                if (club.Id == selectedId)
                    item.Selected = true;
            }
            listClubs.EndUpdate();
        }

        // A row of the list, with the changes that haven't been applied yet
        void ShowClubRow(ListViewItem item, SaveGameFile.Club club)
        {
            SaveGameFile.ClubEdit edit = EditOf(club);
            item.Tag = club;
            item.SubItems.Clear();
            item.Text = edit.Name;
            item.SubItems.Add(edit.Reputation.ToString("N0", CultureInfo.CurrentCulture));
            item.SubItems.Add(club.StadiumId < 0 ? "" : edit.StadiumName);
            item.SubItems.Add(club.StadiumId < 0 ? "" : Money(edit.Capacity));
            item.SubItems.Add(Money(edit.Balance));
            item.BackColor = edits.ContainsKey(club.Id) ? Color.LightYellow : Color.White;
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
            textBoxClubName.Enabled = textBoxShortName.Enabled = numericReputation.Enabled = numericBalance.Enabled = any;
            buttonAdd1.Enabled = buttonAdd10.Enabled = buttonAdd50.Enabled = buttonSetClub.Enabled = any;
            if (!any)
            {
                textBoxStadium.Enabled = numericCapacity.Enabled = numericSeated.Enabled = false;
                labelStadiumNote.Text = "";
                return;
            }

            SaveGameFile.Club club = (SaveGameFile.Club)listClubs.SelectedItems[0].Tag;
            SaveGameFile.ClubEdit edit = EditOf(club);
            textBoxClubName.Text = edit.Name;
            textBoxShortName.Text = edit.ShortName;
            numericReputation.Value = Math.Max(numericReputation.Minimum, Math.Min(numericReputation.Maximum, edit.Reputation));
            numericBalance.Value = Math.Max(numericBalance.Minimum, Math.Min(numericBalance.Maximum, edit.Balance));

            bool hasStadium = club.StadiumId >= 0;
            textBoxStadium.Enabled = numericCapacity.Enabled = numericSeated.Enabled = hasStadium;
            textBoxStadium.Text = hasStadium ? edit.StadiumName : "";
            numericCapacity.Value = hasStadium ? Math.Min(numericCapacity.Maximum, edit.Capacity) : 0;
            numericSeated.Value = hasStadium ? Math.Min(numericSeated.Maximum, edit.Seated) : 0;
            if (!hasStadium)
                labelStadiumNote.Text = "This club has no stadium in the save.";
            else if (club.StadiumClubs > 1)
                labelStadiumNote.Text = string.Format("{0} clubs use this stadium: a change to it applies to all of them. The game shows \"all seated\" when the seated number is not below the capacity.", club.StadiumClubs);
            else
                labelStadiumNote.Text = "The game shows \"all seated\" when the seated number is not below the capacity.";
        }

        void AddToBalance(decimal amount)
        {
            numericBalance.Value = Math.Min(numericBalance.Maximum, Math.Max(numericBalance.Minimum, numericBalance.Value + amount));
            SetSelectedClub();
        }

        // Keeps the club as it is in the boxes (until Apply writes it)
        void SetSelectedClub()
        {
            if (listClubs.SelectedItems.Count == 0)
                return;
            ListViewItem item = listClubs.SelectedItems[0];
            SaveGameFile.Club club = (SaveGameFile.Club)item.Tag;

            string name = textBoxClubName.Text.Trim();
            string shortName = textBoxShortName.Text.Trim();
            string stadium = textBoxStadium.Text.Trim();
            if (name.Length == 0 || shortName.Length == 0 || (club.StadiumId >= 0 && stadium.Length == 0))
            {
                MessageBox.Show(this, "The names can't be empty.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SaveGameFile.ClubEdit edit = new SaveGameFile.ClubEdit();
            edit.Name = name;
            edit.ShortName = shortName;
            edit.Reputation = (int)numericReputation.Value;
            edit.Balance = (long)numericBalance.Value;
            edit.StadiumName = club.StadiumId >= 0 ? stadium : club.StadiumName;
            edit.Capacity = club.StadiumId >= 0 ? (int)numericCapacity.Value : club.Capacity;
            edit.Seated = club.StadiumId >= 0 ? (int)numericSeated.Value : club.Seated;

            StoreEdit(club, edit);

            // clubs that share the stadium get the same stadium
            if (club.StadiumId >= 0 && club.StadiumClubs > 1)
            {
                foreach (SaveGameFile.Club other in clubs)
                {
                    if (other.Id == club.Id || other.StadiumId != club.StadiumId)
                        continue;
                    SaveGameFile.ClubEdit otherEdit = EditOf(other);
                    otherEdit.StadiumName = edit.StadiumName;
                    otherEdit.Capacity = edit.Capacity;
                    otherEdit.Seated = edit.Seated;
                    StoreEdit(other, otherEdit);
                }
                FillClubs();
            }
            else
                ShowClubRow(item, club);
            UpdateEdits();
        }

        void StoreEdit(SaveGameFile.Club club, SaveGameFile.ClubEdit edit)
        {
            if (edit.SameAs(club))
                edits.Remove(club.Id);
            else
                edits[club.Id] = edit;
        }

        void UpdateEdits()
        {
            labelEdits.Text = edits.Count == 0 ? "No clubs changed yet." : string.Format("{0} club(s) changed, not saved yet.", edits.Count);
            buttonClearEdits.Enabled = edits.Count > 0;
        }
    }
}
