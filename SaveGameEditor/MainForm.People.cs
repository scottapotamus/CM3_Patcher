using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CM3SaveGameEditor
{
    // The "People" tab: add new players and staff to the saved game and take others out of it
    public partial class MainForm
    {
        List<NewPerson> newPeople = new List<NewPerson>();
        Dictionary<int, bool> removedPeople = new Dictionary<int, bool>();     // staff id -> true
        List<ScoutPlayer> allPlayers = new List<ScoutPlayer>();
        List<ScoutPlayer> allStaff = new List<ScoutPlayer>();
        List<KeyValuePair<int, string>> nationList = new List<KeyValuePair<int, string>>();
        List<int> addClubIds = new List<int>();

        // add
        RadioButton radioPlayer;
        RadioButton radioStaffMember;
        TextBox textBoxFirstName;
        TextBox textBoxSurname;
        ComboBox comboNation;
        DateTimePicker dateBorn;
        Label labelJobOrPosition;
        ComboBox comboJob;
        ComboBox comboPosition;
        ComboBox comboNewClub;
        NumericUpDown numericNewWage;
        DateTimePicker dateNewExpires;
        NumericUpDown numericAllAttributes;
        AttributeEditor newPlayerEditor;
        AttributeEditor newStaffEditor;
        int[] newPlayerValues;
        int[] newStaffValues;
        Label labelNewTitle;
        Label labelNewSub;
        ListView listNewPeople;
        Button buttonAddPerson;
        Button buttonUndoAdd;
        DateTime gameDate = new DateTime(2001, 1, 1);

        // remove
        RadioButton radioRemovePlayers;
        RadioButton radioRemoveStaff;
        TextBox textBoxRemoveName;
        TextBox textBoxRemoveClub;
        ListView listRemove;
        Label labelRemoved;
        bool fillingRemove;

        void BuildPeopleTab()
        {
            TabPage page = NestedTabs("People", BuildAddPage(), BuildRemovePage());
            tabs.TabPages.Add(page);
        }

        TabPage BuildAddPage()
        {
            TabPage page = new TabPage("Add");

            // the form and the list of who will be added on the left, the numbers on the right (like the Attributes tab)
            Panel left = new Panel { Dock = DockStyle.Left, Width = 470 };
            Panel right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.DarkNavy };
            page.Controls.Add(right);
            page.Controls.Add(left);
            right.BringToFront();

            GroupBox who = new CardBox { Text = "A new person", Left = 10, Top = 8, Width = 450, Height = 300 };
            left.Controls.Add(who);

            radioPlayer = new RadioButton { Text = "Player", Left = 12, Top = 22, AutoSize = true, Checked = true };
            radioStaffMember = new RadioButton { Text = "Member of staff", Left = 100, Top = 22, AutoSize = true };
            radioPlayer.CheckedChanged += delegate { UpdateAddKind(); };
            who.Controls.Add(radioPlayer);
            who.Controls.Add(radioStaffMember);

            who.Controls.Add(new Label { Text = "First name:", Left = 12, Top = 56, AutoSize = true });
            textBoxFirstName = new TextBox { Left = 100, Top = 52, Width = 130, MaxLength = 50 };
            textBoxFirstName.TextChanged += delegate { ShowNewTitle(); };
            who.Controls.Add(textBoxFirstName);
            who.Controls.Add(new Label { Text = "Surname:", Left = 244, Top = 56, AutoSize = true });
            textBoxSurname = new TextBox { Left = 306, Top = 52, Width = 132, MaxLength = 50 };
            textBoxSurname.TextChanged += delegate { ShowNewTitle(); };
            who.Controls.Add(textBoxSurname);

            who.Controls.Add(new Label { Text = "Nation:", Left = 12, Top = 88, AutoSize = true });
            comboNation = new ComboBox { Left = 100, Top = 84, Width = 200, DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
            who.Controls.Add(comboNation);
            who.Controls.Add(new Label { Text = "Born:", Left = 12, Top = 120, AutoSize = true });
            dateBorn = new DateTimePicker { Left = 100, Top = 116, Width = 120, Format = DateTimePickerFormat.Short, MinDate = new DateTime(1900, 1, 1), MaxDate = new DateTime(2099, 12, 31) };
            who.Controls.Add(dateBorn);

            labelJobOrPosition = new Label { Text = "Main position:", Left = 12, Top = 152, AutoSize = true };
            who.Controls.Add(labelJobOrPosition);
            comboPosition = new ComboBox { Left = 100, Top = 148, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (SaveGameFile.PositionPreset preset in SaveGameFile.PositionPresets)
                comboPosition.Items.Add(preset.Name);
            comboPosition.SelectedIndex = 5;
            comboPosition.SelectedIndexChanged += delegate { ApplyPositionPreset(); };
            who.Controls.Add(comboPosition);
            comboJob = new ComboBox { Left = 100, Top = 148, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, Visible = false };
            foreach (int job in SaveGameFile.StaffJobChoices)
                comboJob.Items.Add(StaffFields.Jobs[job]);
            comboJob.SelectedIndex = 2;
            comboJob.SelectedIndexChanged += delegate { ShowNewTitle(); };
            who.Controls.Add(comboJob);

            who.Controls.Add(new Label { Text = "Club:", Left = 12, Top = 188, AutoSize = true });
            comboNewClub = new ComboBox { Left = 100, Top = 184, Width = 260, DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
            comboNewClub.TextChanged += delegate { UpdateAddKind(); };
            who.Controls.Add(comboNewClub);
            who.Controls.Add(new Label { Text = "Wages/month:", Left = 12, Top = 220, AutoSize = true });
            numericNewWage = new NumericUpDown { Left = 100, Top = 216, Width = 100, Minimum = 0, Maximum = 500000000, ThousandsSeparator = true, Increment = 1000, Value = 2000 };
            who.Controls.Add(numericNewWage);
            who.Controls.Add(new Label { Text = "Contract ends:", Left = 222, Top = 220, AutoSize = true });
            dateNewExpires = new DateTimePicker { Left = 310, Top = 216, Width = 120, Format = DateTimePickerFormat.Short, MinDate = new DateTime(1900, 1, 1), MaxDate = new DateTime(2099, 12, 31) };
            who.Controls.Add(dateNewExpires);

            who.Controls.Add(new Label { Text = "Set every attribute to:", Left = 12, Top = 256, AutoSize = true });
            numericAllAttributes = new NumericUpDown { Left = 140, Top = 252, Width = 56, Minimum = 1, Maximum = 20, Value = 10 };
            who.Controls.Add(numericAllAttributes);
            Button setAll = new ModernButton { Text = "Set", Left = 204, Top = 250, Width = 60 };
            setAll.Click += delegate { (radioPlayer.Checked ? newPlayerEditor : newStaffEditor).SetAllAttributes((int)numericAllAttributes.Value); };
            who.Controls.Add(setAll);
            buttonAddPerson = new ModernButton { Text = "Add to the list", Left = 300, Top = 250, Width = 130 };
            buttonAddPerson.Click += delegate { AddPersonToList(); };
            who.Controls.Add(buttonAddPerson);

            // the list of who will be added
            left.Controls.Add(new Label { Text = "To be added when you press Apply (they show up on the other tabs when the saved game is opened again):", Left = 10, Top = 316, Width = 450, Height = 30 });
            listNewPeople = new ListView
            {
                Left = 10, Top = 350, Width = 450, Height = 80,
                View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, GridLines = true
            };
            listNewPeople.Columns.Add("Name", 150);
            listNewPeople.Columns.Add("Kind", 120);
            listNewPeople.Columns.Add("Club", 110);
            listNewPeople.Columns.Add("Born", 70);
            listNewPeople.SelectedIndexChanged += delegate { buttonUndoAdd.Enabled = listNewPeople.SelectedItems.Count > 0; };
            left.Controls.Add(listNewPeople);
            buttonUndoAdd = new ModernButton { Text = "Take off the list", Left = 10, Top = 438, Width = 130, Enabled = false };
            buttonUndoAdd.Click += delegate { UndoAddPerson(); };
            left.Controls.Add(buttonUndoAdd);
            left.Resize += delegate
            {
                listNewPeople.Height = Math.Max(60, left.ClientSize.Height - listNewPeople.Top - 44);
                buttonUndoAdd.Top = listNewPeople.Bottom + 6;
            };

            // a red title like the Attributes tab's, the numbers under it
            Panel header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Theme.Blue };
            labelNewTitle = new Label { Left = 14, Top = 6, Height = 30, AutoSize = true, ForeColor = Color.White, Font = new Font("Arial", 16f, FontStyle.Bold), Text = "A new player" };
            labelNewSub = new Label { Left = 16, Top = 40, AutoSize = true, ForeColor = Theme.Yellow, Font = Theme.Bold, Text = "Set the numbers, then press \"Add to the list\"" };
            header.Controls.Add(labelNewTitle);
            header.Controls.Add(labelNewSub);

            newPlayerEditor = new AttributeEditor(ScoutFields.Set, false, AttributesPanel.AttributeNote(false)) { Dock = DockStyle.Fill };
            newStaffEditor = new AttributeEditor(StaffFields.Set, true, AttributesPanel.AttributeNote(true)) { Dock = DockStyle.Fill, Visible = false };
            newPlayerEditor.RowValueChanged += delegate(AttributeRow row)
            {
                if (row.Field == ScoutFields.FirstPositionField)
                    newPlayerEditor.RefreshApplicable(newPlayerEditor.Read(newPlayerValues), true);
            };
            right.Controls.Add(newPlayerEditor);
            right.Controls.Add(newStaffEditor);
            right.Controls.Add(header);
            newPlayerEditor.BringToFront();
            newStaffEditor.BringToFront();

            ResetNewEditors();
            UpdateAddKind();
            return page;
        }

        // The numbers a new person starts with
        void ResetNewEditors()
        {
            NewPerson blank = new NewPerson();
            blank.Position = Math.Max(0, comboPosition.SelectedIndex);
            newPlayerValues = SaveGameFile.DefaultPlayerValues(blank);
            newStaffValues = SaveGameFile.DefaultStaffValues(blank);
            newPlayerEditor.Load(newPlayerValues, null, true);
            newStaffEditor.Load(newStaffValues, null, true);
        }

        // A different main position: its ratings go into the numbers
        void ApplyPositionPreset()
        {
            if (newPlayerEditor == null || comboPosition.SelectedIndex < 0)
                return;
            int[] values = newPlayerEditor.Read(newPlayerValues);
            SaveGameFile.PositionPreset preset = SaveGameFile.PositionPresets[comboPosition.SelectedIndex];
            for (int i = 0; i < preset.Ratings.Length; i++)
                values[ScoutFields.FirstPositionField + i] = preset.Ratings[i];
            newPlayerEditor.Load(values, null, true);
        }

        void ShowNewTitle()
        {
            if (labelNewTitle == null)
                return;
            string name = (textBoxFirstName.Text.Trim() + " " + textBoxSurname.Text.Trim()).Trim();
            labelNewTitle.Text = name.Length > 0 ? name : (radioPlayer.Checked ? "A new player" : "A new member of staff");
        }

        TabPage BuildRemovePage()
        {
            TabPage page = new TabPage("Remove");

            radioRemovePlayers = new RadioButton { Text = "Players", Left = 10, Top = 12, AutoSize = true, Checked = true };
            radioRemoveStaff = new RadioButton { Text = "Staff", Left = 90, Top = 12, AutoSize = true };
            radioRemovePlayers.CheckedChanged += delegate { FillRemoveList(); };
            page.Controls.Add(radioRemovePlayers);
            page.Controls.Add(radioRemoveStaff);
            page.Controls.Add(new Label { Text = "Name:", Left = 180, Top = 14, AutoSize = true });
            textBoxRemoveName = new TextBox { Left = 222, Top = 10, Width = 150 };
            textBoxRemoveName.TextChanged += delegate { FillRemoveList(); };
            page.Controls.Add(textBoxRemoveName);
            page.Controls.Add(new Label { Text = "Club:", Left = 390, Top = 14, AutoSize = true });
            textBoxRemoveClub = new TextBox { Left = 424, Top = 10, Width = 150 };
            textBoxRemoveClub.TextChanged += delegate { FillRemoveList(); };
            page.Controls.Add(textBoxRemoveClub);

            listRemove = new ListView
            {
                Left = 10, Top = 40, Width = 700, Height = 360, CheckBoxes = true,
                View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, GridLines = true
            };
            listRemove.Columns.Add("Name", 200);
            listRemove.Columns.Add("Club", 180);
            listRemove.Columns.Add("Plays as / Job", 140);
            listRemove.Columns.Add("Age", 50, HorizontalAlignment.Right);
            listRemove.Columns.Add("CA", 50, HorizontalAlignment.Right);
            listRemove.Columns.Add("PA", 50, HorizontalAlignment.Right);
            listRemove.ItemChecked += delegate(object sender, ItemCheckedEventArgs e) { RemoveChecked(e.Item); };
            page.Controls.Add(listRemove);

            page.Controls.Add(new Label
            {
                Left = 10, Top = 408, Width = 700, Height = 36,
                Text = "Tick the people to take out of the game. They leave their club and lose their contract, and are retired the way " +
                       "the game retires people. Nothing is changed in the saved game until you press Apply."
            });
            labelRemoved = new Label { Left = 10, Top = 452, Width = 500, Height = 18, Text = "" };
            page.Controls.Add(labelRemoved);

            StretchList(page, listRemove);
            UpdateRemoved();
            return page;
        }

        // The people of the loaded saved game (and what to choose from)
        void SetPeopleLists(List<ScoutPlayer> players, List<ScoutPlayer> staffMembers)
        {
            allPlayers = players;
            allStaff = staffMembers;
            newPeople.Clear();
            removedPeople.Clear();
            FillNewPeople();
            FillRemoveList();
            UpdateRemoved();

            comboNation.Items.Clear();
            nationList = new List<KeyValuePair<int, string>>();
            comboNewClub.Items.Clear();
            addClubIds.Clear();
            comboNewClub.Items.Add("(no club - a free agent)");
            addClubIds.Add(-1);
            if (save != null)
            {
                try
                {
                    nationList = save.NationList();
                    gameDate = save.GameDate();
                }
                catch (Exception)
                {
                }
                foreach (KeyValuePair<int, string> nation in nationList)
                    comboNation.Items.Add(nation.Value);
                int england = nationList.FindIndex(delegate(KeyValuePair<int, string> n) { return n.Value == "England"; });
                comboNation.SelectedIndex = england >= 0 ? england : (nationList.Count > 0 ? 0 : -1);

                List<SaveGameFile.Club> sorted = new List<SaveGameFile.Club>(clubs);
                sorted.Sort(delegate(SaveGameFile.Club a, SaveGameFile.Club b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });
                foreach (SaveGameFile.Club club in sorted)
                {
                    comboNewClub.Items.Add(club.Name);
                    addClubIds.Add(club.Id);
                }
                dateBorn.Value = Clamp(gameDate.AddYears(-20), dateBorn);
                dateNewExpires.Value = Clamp(gameDate.AddYears(3), dateNewExpires);
            }
            comboNewClub.SelectedIndex = 0;
            UpdateAddKind();
        }

        static DateTime Clamp(DateTime value, DateTimePicker picker)
        {
            return value < picker.MinDate ? picker.MinDate : (value > picker.MaxDate ? picker.MaxDate : value);
        }

        void UpdateAddKind()
        {
            bool player = radioPlayer.Checked;
            labelJobOrPosition.Text = player ? "Main position:" : "Job:";
            comboPosition.Visible = player;
            comboJob.Visible = !player;
            if (newPlayerEditor != null)
            {
                newPlayerEditor.Visible = player;
                newStaffEditor.Visible = !player;
                ShowNewTitle();
            }
            bool club = comboNewClub.Text.Length > 0 && comboNewClub.FindStringExact(comboNewClub.Text) > 0;
            numericNewWage.Enabled = dateNewExpires.Enabled = club;
        }

        void AddPersonToList()
        {
            if (save == null)
            {
                MessageBox.Show(this, "Open a saved game first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            NewPerson person = new NewPerson();
            person.IsStaff = radioStaffMember.Checked;
            person.FirstName = textBoxFirstName.Text.Trim();
            person.Surname = textBoxSurname.Text.Trim();
            if (person.FirstName.Length == 0 || person.Surname.Length == 0)
            {
                MessageBox.Show(this, "Give the new person a first name and a surname.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int nation = comboNation.FindStringExact(comboNation.Text);
            if (nation < 0)
            {
                MessageBox.Show(this, "Pick the nation from the list.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            person.NationId = nationList[nation].Key;
            person.NationName = nationList[nation].Value;
            person.Born = dateBorn.Value.Date;
            if (person.Born >= gameDate)
            {
                MessageBox.Show(this, "The date of birth has to be before the game's date (" + gameDate.ToShortDateString() + ").", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int club = comboNewClub.Text.Length == 0 ? 0 : comboNewClub.FindStringExact(comboNewClub.Text);
            if (club < 0)
            {
                MessageBox.Show(this, "Pick the club from the list (or the first line for no club).", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            person.ClubId = addClubIds[club];
            person.ClubName = club > 0 ? comboNewClub.Items[club].ToString() : "";
            person.Wage = (int)numericNewWage.Value;
            person.Expires = dateNewExpires.Value.Date;
            if (person.ClubId >= 0 && person.Expires <= gameDate)
            {
                MessageBox.Show(this, "The contract has to end after the game's date (" + gameDate.ToShortDateString() + ").", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            person.Values = person.IsStaff ? newStaffEditor.Read(newStaffValues) : newPlayerEditor.Read(newPlayerValues);
            person.CurrentAbility = person.Values[ScoutFieldSet.CurrentAbility];
            person.PotentialAbility = person.Values[ScoutFieldSet.PotentialAbility];
            person.Job = person.IsStaff ? SaveGameFile.StaffJobChoices[Math.Max(0, comboJob.SelectedIndex)] : 11;
            person.Position = Math.Max(0, comboPosition.SelectedIndex);

            newPeople.Add(person);
            FillNewPeople();
            textBoxFirstName.Text = "";
            textBoxSurname.Text = "";
            ResetNewEditors();
            textBoxFirstName.Focus();
        }

        void UndoAddPerson()
        {
            if (listNewPeople.SelectedItems.Count == 0)
                return;
            newPeople.Remove((NewPerson)listNewPeople.SelectedItems[0].Tag);
            FillNewPeople();
        }

        void FillNewPeople()
        {
            listNewPeople.Items.Clear();
            foreach (NewPerson person in newPeople)
            {
                ListViewItem item = new ListViewItem(person.Name);
                item.SubItems.Add(person.IsStaff ? StaffFields.Jobs[person.Job] : "Player (" + SaveGameFile.PositionPresets[person.Position].Name + ")");
                item.SubItems.Add(person.ClubName.Length == 0 ? "free agent" : person.ClubName);
                item.SubItems.Add(person.Born.ToShortDateString());
                item.Tag = person;
                listNewPeople.Items.Add(item);
            }
            buttonUndoAdd.Enabled = false;
        }

        void FillRemoveList()
        {
            if (listRemove == null)
                return;
            List<ScoutPlayer> source = radioRemovePlayers.Checked ? allPlayers : allStaff;
            string name = textBoxRemoveName.Text.Trim();
            string club = textBoxRemoveClub.Text.Trim();
            List<ScoutPlayer> found = new List<ScoutPlayer>();
            foreach (ScoutPlayer person in source)
            {
                if (name.Length > 0 && person.Name.IndexOf(name, StringComparison.CurrentCultureIgnoreCase) < 0)
                    continue;
                if (club.Length > 0 && person.Club.IndexOf(club, StringComparison.CurrentCultureIgnoreCase) < 0)
                    continue;
                found.Add(person);
            }
            found.Sort(delegate(ScoutPlayer a, ScoutPlayer b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });

            fillingRemove = true;
            listRemove.BeginUpdate();
            listRemove.Items.Clear();
            for (int i = 0; i < found.Count && i < 500; i++)
            {
                ScoutPlayer person = found[i];
                ListViewItem item = new ListViewItem(person.Name);
                item.SubItems.Add(person.Club);
                item.SubItems.Add(person.Positions);
                item.SubItems.Add(person.Values[ScoutFieldSet.Age].ToString());
                item.SubItems.Add(person.Values[ScoutFieldSet.CurrentAbility].ToString());
                item.SubItems.Add(person.Values[ScoutFieldSet.PotentialAbility].ToString());
                item.Tag = person;
                item.Checked = removedPeople.ContainsKey(person.StaffId);
                listRemove.Items.Add(item);
            }
            listRemove.EndUpdate();
            fillingRemove = false;
        }

        void RemoveChecked(ListViewItem item)
        {
            if (fillingRemove)
                return;
            ScoutPlayer person = (ScoutPlayer)item.Tag;
            if (item.Checked)
                removedPeople[person.StaffId] = true;
            else
                removedPeople.Remove(person.StaffId);
            UpdateRemoved();
        }

        void UpdateRemoved()
        {
            labelRemoved.Text = removedPeople.Count == 0 ? "Nobody ticked yet." : string.Format("{0} ticked to be removed (not saved yet).", removedPeople.Count);
        }
    }
}
