using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CM3SaveGameEditor
{
    // Nationality and second nationality of players or staff. A second nationality (say an EU one) is what gets a player
    // out of needing a work permit; the game works the permit out from the nationalities.
    public class NationalityPanel : TabPage
    {
        const int MaxListed = 500;

        bool staff;
        Dictionary<int, int[]> edits;           // staff id -> { nationality, second nationality or -1 }; shared by both tabs
        Action changed;

        TextBox textBoxName;
        TextBox textBoxClub;
        ListView listPeople;
        ComboBox comboNation;
        ComboBox comboSecond;
        Label labelWho;
        Button buttonSet;
        Button buttonUndo;
        List<ScoutPlayer> everybody = new List<ScoutPlayer>();
        List<KeyValuePair<int, string>> nations = new List<KeyValuePair<int, string>>();
        ScoutPlayer current;

        public NationalityPanel(string title, bool staff, Dictionary<int, int[]> edits, Action changed)
        {
            Text = title;
            this.staff = staff;
            this.edits = edits;
            this.changed = changed;

            Panel left = new Panel { Dock = DockStyle.Left, Width = 520 };
            Panel right = new Panel { Dock = DockStyle.Fill };
            Controls.Add(right);
            Controls.Add(left);
            right.BringToFront();

            left.Controls.Add(new Label { Text = "Name:", Left = 10, Top = 14, AutoSize = true });
            textBoxName = new TextBox { Left = 50, Top = 10, Width = 150 };
            textBoxName.TextChanged += delegate { Search(); };
            left.Controls.Add(textBoxName);
            left.Controls.Add(new Label { Text = "Club:", Left = 216, Top = 14, AutoSize = true });
            textBoxClub = new TextBox { Left = 250, Top = 10, Width = 150 };
            textBoxClub.TextChanged += delegate { Search(); };
            left.Controls.Add(textBoxClub);

            listPeople = new ListView
            {
                Left = 10, Top = 40, Width = 500, Height = 400,
                View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, GridLines = true
            };
            listPeople.Columns.Add("Name", 150);
            listPeople.Columns.Add("Club", 110);
            listPeople.Columns.Add("Nationality", 110);
            listPeople.Columns.Add("Second", 110);
            listPeople.SelectedIndexChanged += delegate { LoadSelected(); };
            left.Controls.Add(listPeople);
            left.Resize += delegate { listPeople.Height = Math.Max(100, left.ClientSize.Height - listPeople.Top - 8); };

            labelWho = new Label { Left = 16, Top = 14, Width = 400, Height = 22, Font = Theme.Bold, Text = "Select " + (staff ? "a member of staff" : "a player") + " on the left" };
            right.Controls.Add(labelWho);

            right.Controls.Add(new Label { Text = "Nationality:", Left = 16, Top = 56, AutoSize = true });
            comboNation = NewCombo(140, 52);
            right.Controls.Add(comboNation);
            right.Controls.Add(new Label { Text = "Second nationality:", Left = 16, Top = 90, AutoSize = true });
            comboSecond = NewCombo(140, 86);
            right.Controls.Add(comboSecond);

            buttonSet = new Button { Text = "Set", Left = 16, Top = 124, Width = 90, Enabled = false };
            buttonSet.Click += delegate { SetCurrent(); };
            right.Controls.Add(buttonSet);
            buttonUndo = new Button { Text = "Undo changes", Left = 116, Top = 124, Width = 110, Enabled = false };
            buttonUndo.Click += delegate { Undo(); };
            right.Controls.Add(buttonUndo);

            right.Controls.Add(new Label
            {
                Left = 16, Top = 164, Width = 420, Height = 110,
                Text = "In the game a player from outside the EU needs a work permit to play in England. Giving him a second nationality " +
                       "from an EU country (or changing his nationality to one) is how to get him out of needing one. Choose None to remove the second nationality.\r\n\r\n" +
                       "The changes are written when you press Apply."
            });
        }

        ComboBox NewCombo(int left, int top)
        {
            return new ComboBox { Left = left, Top = top, Width = 220, DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems, Enabled = false };
        }

        public void SetNations(List<KeyValuePair<int, string>> list)
        {
            nations = list;
            comboNation.Items.Clear();
            comboSecond.Items.Clear();
            comboSecond.Items.Add("None");
            foreach (KeyValuePair<int, string> nation in nations)
            {
                comboNation.Items.Add(nation.Value);
                comboSecond.Items.Add(nation.Value);
            }
        }

        public void SetPeople(List<ScoutPlayer> people)
        {
            everybody = people;
            current = null;
            Search();
            LoadSelected();
        }

        string NameOf(int id)
        {
            if (id < 0)
                return "";
            foreach (KeyValuePair<int, string> nation in nations)
                if (nation.Key == id)
                    return nation.Value;
            return "";
        }

        // { nationality, second } as they should be (with the changes that haven't been applied yet)
        int[] NationsOf(ScoutPlayer person)
        {
            int[] value;
            return edits.TryGetValue(person.StaffId, out value) ? value : new int[] { person.NationId, person.SecondNationId };
        }

        void Search()
        {
            string name = textBoxName.Text.Trim();
            string club = textBoxClub.Text.Trim();
            List<ScoutPlayer> found = new List<ScoutPlayer>();
            foreach (ScoutPlayer person in everybody)
            {
                if (name.Length > 0 && person.Name.IndexOf(name, StringComparison.CurrentCultureIgnoreCase) < 0)
                    continue;
                if (club.Length > 0 && person.Club.IndexOf(club, StringComparison.CurrentCultureIgnoreCase) < 0)
                    continue;
                found.Add(person);
            }
            found.Sort(delegate(ScoutPlayer a, ScoutPlayer b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });

            int selected = current != null ? current.StaffId : -1;
            listPeople.BeginUpdate();
            listPeople.Items.Clear();
            for (int i = 0; i < found.Count && i < MaxListed; i++)
            {
                ListViewItem item = new ListViewItem(found[i].Name);
                item.SubItems.Add(found[i].Club);
                item.SubItems.Add("");
                item.SubItems.Add("");
                item.Tag = found[i];
                ShowRow(item);
                listPeople.Items.Add(item);
                if (found[i].StaffId == selected)
                    item.Selected = true;
            }
            listPeople.EndUpdate();
        }

        void ShowRow(ListViewItem item)
        {
            ScoutPlayer person = (ScoutPlayer)item.Tag;
            int[] nowNations = NationsOf(person);
            item.SubItems[2].Text = NameOf(nowNations[0]);
            item.SubItems[3].Text = NameOf(nowNations[1]);
            item.BackColor = edits.ContainsKey(person.StaffId) ? Color.LightYellow : Color.White;
        }

        void LoadSelected()
        {
            current = listPeople.SelectedItems.Count > 0 ? (ScoutPlayer)listPeople.SelectedItems[0].Tag : null;
            comboNation.Enabled = comboSecond.Enabled = buttonSet.Enabled = current != null;
            if (current == null)
            {
                labelWho.Text = "Select " + (staff ? "a member of staff" : "a player") + " on the left";
                comboNation.SelectedIndex = comboSecond.SelectedIndex = -1;
                comboNation.Text = comboSecond.Text = "";
                buttonUndo.Enabled = false;
                return;
            }
            labelWho.Text = current.Name + (current.Club.Length > 0 ? " (" + current.Club + ")" : "");
            int[] nowNations = NationsOf(current);
            comboNation.SelectedIndex = nations.FindIndex(delegate(KeyValuePair<int, string> n) { return n.Key == nowNations[0]; });
            int second = nations.FindIndex(delegate(KeyValuePair<int, string> n) { return n.Key == nowNations[1]; });
            comboSecond.SelectedIndex = second < 0 ? 0 : second + 1;
            buttonUndo.Enabled = edits.ContainsKey(current.StaffId);
        }

        void SetCurrent()
        {
            if (current == null)
                return;
            int nation = comboNation.FindStringExact(comboNation.Text);
            int second = comboSecond.FindStringExact(comboSecond.Text);
            if (nation < 0 || second < 0)
            {
                MessageBox.Show(this, "Pick the nations from the lists.", "CM3 Utils", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int[] wanted = { nations[nation].Key, second == 0 ? -1 : nations[second - 1].Key };
            if (wanted[1] == wanted[0])
                wanted[1] = -1;
            if (wanted[0] == current.NationId && wanted[1] == current.SecondNationId)
                edits.Remove(current.StaffId);
            else
                edits[current.StaffId] = wanted;
            if (listPeople.SelectedItems.Count > 0)
                ShowRow(listPeople.SelectedItems[0]);
            LoadSelected();
            changed();
        }

        void Undo()
        {
            if (current == null)
                return;
            edits.Remove(current.StaffId);
            if (listPeople.SelectedItems.Count > 0)
                ShowRow(listPeople.SelectedItems[0]);
            LoadSelected();
            changed();
        }

        public void RefreshEdits()
        {
            Search();
            LoadSelected();
        }
    }
}
