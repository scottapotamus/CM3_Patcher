using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CM3SaveGameEditor
{
    // The numbers of a player or of a member of staff laid out the way the game's profile screen does it: the attributes in the
    // game's three alphabetical columns with red / green bars, then the numbers the game hides and a player's position ratings
    public class AttributeEditor : Panel
    {
        const int RowHeight = 26;
        const int ColumnWidth = 240;

        readonly ScoutFieldSet set;
        readonly bool staff;
        readonly Dictionary<int, AttributeRow> rows = new Dictionary<int, AttributeRow>();
        readonly Label labelNote;
        bool loading;

        // Raised when the user changes a number
        public event Action<AttributeRow> RowValueChanged;

        public AttributeEditor(ScoutFieldSet set, bool staff, string note)
        {
            this.set = set;
            this.staff = staff;
            AutoScroll = true;
            BackColor = Theme.DarkNavy;

            int perColumn = staff ? 4 : 12;
            int top = 12;
            for (int i = 0; i < set.VisibleCount; i++)
                AddRow(set.FirstVisible + i, set.All[set.FirstVisible + i].Name, 1, 20, i / perColumn, i % perColumn, top);
            top += perColumn * RowHeight + 16;

            Heading("Hidden by the game", top);
            top += 24;
            AddRow(ScoutFieldSet.CurrentAbility, "Current ability", 0, 200, 0, 0, top);
            AddRow(ScoutFieldSet.PotentialAbility, "Potential ability", 0, 200, 0, 1, top);
            for (int i = 0; i < set.HiddenCount; i++)
            {
                int index = i + 2;           // after the two abilities
                AddRow(set.FirstHidden + i, set.All[set.FirstHidden + i].Name, 0, 20, index / 3, index % 3, top);
            }
            top += 3 * RowHeight + 16;

            if (set.PositionCount > 0)
            {
                Heading("Where he plays (0-20)", top);
                top += 24;
                for (int i = 0; i < set.PositionCount; i++)
                    AddRow(set.FirstPosition + i, set.All[set.FirstPosition + i].Name.Substring("Rating as ".Length), 0, 20, i % 3, i / 3, top);
                top += 4 * RowHeight + 16;
            }

            labelNote = new Label { Left = 14, Top = top, Width = 3 * ColumnWidth - 14, Height = 64, ForeColor = Theme.Disabled, Text = note };
            Controls.Add(labelNote);

            Resize += delegate { SpreadColumns(); };
            SpreadColumns();
        }

        public IDictionary<int, AttributeRow> Rows { get { return rows; } }

        void AddRow(int field, string title, int min, int max, int column, int row, int top)
        {
            AttributeRow r = new AttributeRow(field, title, min, max)
            {
                Left = 14 + column * ColumnWidth, Top = top + row * RowHeight, Width = ColumnWidth - 14, BackColor = Theme.DarkNavy, Font = Theme.Normal, Enabled = false
            };
            r.ValueChanged += delegate
            {
                if (!loading && RowValueChanged != null)
                    RowValueChanged(r);
            };
            r.Tag = column;
            Controls.Add(r);
            rows[field] = r;
        }

        void Heading(string text, int top)
        {
            Controls.Add(new Label { Text = text, Left = 14, Top = top, AutoSize = true, ForeColor = Theme.Yellow, Font = new Font("Arial", 9f, FontStyle.Bold) });
        }

        // The three columns share the width there is
        void SpreadColumns()
        {
            int width = Math.Max(ColumnWidth, (ClientSize.Width - 20) / 3);
            foreach (AttributeRow row in rows.Values)
            {
                row.Left = 14 + (int)row.Tag * width;
                row.Width = width - 14;
            }
            labelNote.Width = Math.Max(200, 3 * width - 14);
        }

        // Shows the numbers (without raising events); the ones that differ from "original" are marked
        public void Load(int[] values, int[] original, bool enabled)
        {
            loading = true;
            foreach (KeyValuePair<int, AttributeRow> pair in rows)
            {
                pair.Value.Value = values[pair.Key];
                pair.Value.Edited = original != null && values[pair.Key] != original[pair.Key];
            }
            loading = false;
            RefreshApplicable(values, enabled);
        }

        // A goalkeeper's attackers' skills and the others' goalkeeping can't be changed
        public void RefreshApplicable(int[] values, bool enabled)
        {
            foreach (KeyValuePair<int, AttributeRow> pair in rows)
                pair.Value.Enabled = enabled && (staff || ScoutFields.Applies(pair.Key, values));
        }

        // The numbers as they are on the screen, on top of "template"
        public int[] Read(int[] template)
        {
            int[] values = (int[])template.Clone();
            foreach (KeyValuePair<int, AttributeRow> pair in rows)
                values[pair.Key] = pair.Value.Value;
            return values;
        }

        // Gives every attribute the game shows (not the hidden ones) the same number
        public void SetAllAttributes(int value)
        {
            for (int i = 0; i < set.VisibleCount; i++)
            {
                AttributeRow row = rows[set.FirstVisible + i];
                if (row.Enabled)
                    row.Value = Math.Max(row.Min, Math.Min(row.Max, value));
            }
        }
    }

    // An "Attributes" tab: pick a player (or a member of staff) and change his numbers, laid out like the game's profile screen
    public class AttributesPanel : TabPage
    {
        const int MaxListed = 500;

        ScoutFieldSet set;
        bool staff;
        Dictionary<int, int[]> edits;
        Action changed;

        TextBox textBoxName;
        TextBox textBoxClub;
        ListView listPeople;
        Label labelHeader;
        Label labelSub;
        Button buttonReset;
        AttributeEditor editor;
        List<ScoutPlayer> everybody = new List<ScoutPlayer>();
        ScoutPlayer current;

        public AttributesPanel(string title, bool staff, ScoutFieldSet set, Dictionary<int, int[]> edits, Action changed)
        {
            Text = title;
            this.staff = staff;
            this.set = set;
            this.edits = edits;
            this.changed = changed;

            Panel left = new Panel { Dock = DockStyle.Left, Width = 420 };
            Panel right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.DarkNavy };
            Controls.Add(right);
            Controls.Add(left);
            right.BringToFront();

            left.Controls.Add(new Label { Text = "Name:", Left = 10, Top = 14, AutoSize = true });
            textBoxName = new TextBox { Left = 50, Top = 10, Width = 130 };
            textBoxName.TextChanged += delegate { Search(); };
            left.Controls.Add(textBoxName);
            left.Controls.Add(new Label { Text = "Club:", Left = 196, Top = 14, AutoSize = true });
            textBoxClub = new TextBox { Left = 230, Top = 10, Width = 130 };
            textBoxClub.TextChanged += delegate { Search(); };
            left.Controls.Add(textBoxClub);

            listPeople = new ListView
            {
                Left = 10, Top = 40, Width = 400, Height = 400,
                View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, GridLines = true
            };
            listPeople.Columns.Add("Name", 150);
            listPeople.Columns.Add("Club", 130);
            listPeople.Columns.Add(staff ? "Job" : "Plays as", 100);
            listPeople.SelectedIndexChanged += delegate { LoadSelected(); };
            left.Controls.Add(listPeople);
            left.Resize += delegate { listPeople.Height = Math.Max(100, left.ClientSize.Height - listPeople.Top - 8); };

            // a red title like the game's, the numbers under it
            Panel header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Theme.Blue };
            labelHeader = new Label
            {
                Left = 14, Top = 6, Height = 30, AutoSize = true, ForeColor = Color.White, Font = new Font("Arial", 16f, FontStyle.Bold),
                Text = "Select " + (staff ? "a member of staff" : "a player") + " on the left"
            };
            labelSub = new Label { Left = 16, Top = 40, AutoSize = true, ForeColor = Theme.Yellow, Font = Theme.Bold, Text = "" };
            buttonReset = new ModernButton { Text = "Undo changes", Width = 120, Height = 28, Top = 18, Enabled = false };
            buttonReset.Click += delegate { Reset(); };
            header.Controls.Add(labelHeader);
            header.Controls.Add(labelSub);
            header.Controls.Add(buttonReset);
            header.Resize += delegate { buttonReset.Left = header.ClientSize.Width - buttonReset.Width - 16; };
            Theme.StyleButton(buttonReset, false);
            buttonReset.Tag = "styled";

            editor = new AttributeEditor(set, staff, AttributeNote(staff)) { Dock = DockStyle.Fill };
            editor.RowValueChanged += RowChanged;
            right.Controls.Add(editor);
            right.Controls.Add(header);
            editor.BringToFront();
        }

        public static string AttributeNote(bool staff)
        {
            return staff
                ? "The changes are written when you press Apply."
                : "The changes are written when you press Apply. In the game some attributes (Anticipation, Flair, Heading, Passing, Positioning, Tackling and the attackers' skills) depend on the current ability: the numbers here are the ones the game shows, and they stay as you set them if you change the current ability. A goalkeeper shows 1 in the attackers' skills, everybody else shows 1 in Handling and Reflexes (those can't be changed).";
        }

        public void SetPeople(List<ScoutPlayer> people)
        {
            everybody = people;
            current = null;
            Search();
            LoadSelected();
        }

        // The records with their changes
        int[] ValuesOf(ScoutPlayer person)
        {
            int[] values;
            return edits.TryGetValue(person.StaffId, out values) ? values : person.Values;
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
                ScoutPlayer person = found[i];
                ListViewItem item = new ListViewItem(person.Name);
                item.SubItems.Add(person.Club);
                item.SubItems.Add(person.Positions);
                item.Tag = person;
                item.BackColor = edits.ContainsKey(person.StaffId) ? Color.LightYellow : Color.White;
                listPeople.Items.Add(item);
                if (person.StaffId == selected)
                    item.Selected = true;
            }
            listPeople.EndUpdate();
        }

        void LoadSelected()
        {
            current = listPeople.SelectedItems.Count > 0 ? (ScoutPlayer)listPeople.SelectedItems[0].Tag : null;
            if (current == null)
            {
                editor.Load(new int[set.All.Length], null, false);
                labelHeader.Text = "Select " + (staff ? "a member of staff" : "a player") + " on the left";
                labelSub.Text = "";
                buttonReset.Enabled = false;
                return;
            }
            editor.Load(ValuesOf(current), current.Values, true);
            ShowHeader();
        }

        void ShowHeader()
        {
            labelHeader.Text = current.Name + (current.Club.Length > 0 ? " (" + current.Club + ")" : "");
            labelSub.Text = current.Nation + ", age " + current.Values[ScoutFieldSet.Age] + ". " + (staff ? "Job" : "Plays as") + ": " + (current.Positions.Length > 0 ? current.Positions : "?");
            buttonReset.Enabled = edits.ContainsKey(current.StaffId);
        }

        void RowChanged(AttributeRow row)
        {
            if (current == null)
                return;
            int[] values;
            if (!edits.TryGetValue(current.StaffId, out values))
                values = (int[])current.Values.Clone();
            values[row.Field] = row.Value;
            row.Edited = values[row.Field] != current.Values[row.Field];

            bool same = true;
            for (int i = 0; i < values.Length && same; i++)
                if (values[i] != current.Values[i])
                    same = false;
            if (same)
                edits.Remove(current.StaffId);
            else
                edits[current.StaffId] = values;

            if (!staff && row.Field == set.FirstPosition)
                editor.RefreshApplicable(values, true);
            if (listPeople.SelectedItems.Count > 0)
                listPeople.SelectedItems[0].BackColor = same ? Color.White : Color.LightYellow;
            buttonReset.Enabled = !same;
            changed();
        }

        void Reset()
        {
            if (current == null)
                return;
            edits.Remove(current.StaffId);
            if (listPeople.SelectedItems.Count > 0)
                listPeople.SelectedItems[0].BackColor = Color.White;
            LoadSelected();
            changed();
        }

        // After the shared list of changes changed (e.g. "Clear all")
        public void RefreshEdits()
        {
            Search();
            LoadSelected();
        }
    }
}
