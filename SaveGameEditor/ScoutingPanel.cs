using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace CM3SaveGameEditor
{
    // A scouting tab: search the players (or the staff) of the save on any combination of numbers, including the ones the
    // game hides (current / potential ability, ambition, professionalism...). The search options are on the left, a table
    // of everybody who matches, with all their numbers, on the right. It only reads the save.
    public class ScoutingPanel : TabPage
    {
        class Criterion
        {
            public int Field;
            public int Min, Max;
        }

        const int MaxListed = 1000;

        // The kind of a column of the results: below zero is text, otherwise it is the index of a field
        const int KindName = -1, KindClub = -2, KindExtra = -3;

        ScoutFieldSet set;
        string extraTitle;
        string noun;

        TextBox textBoxName;
        TextBox textBoxClub;
        TextBox textBoxNation;
        ComboBox comboExtra;
        CheckBox checkBoxFreeAgents;
        ComboBox comboField;
        NumericUpDown numericMin;
        NumericUpDown numericMax;
        ListView listCriteria;
        Button buttonRemove;
        Button buttonClear;
        ListView listResults;
        Label labelCount;

        List<ScoutPlayer> everybody = new List<ScoutPlayer>();
        List<Criterion> criteria = new List<Criterion>();
        List<int> columnKinds = new List<int>();
        List<string> columnTips = new List<string>();
        ToolTip tip;
        Timer tipTimer;
        int tipColumn = -2;
        List<int> comboFields = new List<int>();    // the field of each item of the combo box
        int sortKind = ScoutFieldSet.CurrentAbility;        // what the table is sorted on (a field, or a text column)
        bool sortDescending = true;
        bool showHidden;
        CheckBox checkBoxHidden;

        // Raised when the "Show hidden attributes" box is ticked or unticked by the user
        public event EventHandler ShowHiddenChanged;

        // The game's own abbreviations for the column headings
        static readonly Dictionary<string, string> Short = new Dictionary<string, string>
        {
            { "Acceleration", "Acc" }, { "Adaptability", "Ada" }, { "Aggression", "Agg" }, { "Agility", "Agi" }, { "Anticipation", "Ant" },
            { "Balance", "Bal" }, { "Bravery", "Bra" }, { "Creativity", "Cre" }, { "Crossing", "Cro" }, { "Determination", "Det" },
            { "Dribbling", "Dri" }, { "Flair", "Fla" }, { "Handling", "Han" }, { "Heading", "Hea" }, { "Influence", "Inf" },
            { "Jumping", "Jum" }, { "Marking", "Mar" }, { "Off The Ball", "Off" }, { "Pace", "Pac" }, { "Passing", "Pas" },
            { "Positioning", "Pos" }, { "Reflexes", "Ref" }, { "Set Pieces", "Set" }, { "Shooting", "Sho" }, { "Stamina", "Sta" },
            { "Strength", "Str" }, { "Tackling", "Tac" }, { "Teamwork", "Tea" }, { "Technique", "Tec" }, { "Versatility", "Ver" },
            { "Work Rate", "Wor" },
            { "Ambition", "Amb" }, { "Loyalty", "Loy" }, { "Pressure", "Pre" }, { "Professionalism", "Pro" },
            { "Sportsmanship", "Spo" }, { "Temperament", "Tem" },
            { "Coaching Goalkeepers", "CGk" }, { "Coaching Outfield Players", "COp" }, { "Judging Player Ability", "JPA" },
            { "Judging Player Potential", "JPP" }, { "Level of Discipline", "Dis" }, { "Man Management", "MMg" },
            { "Motivating", "Mot" }, { "Physiotherapy", "Phy" }, { "Tactical Knowledge", "Tct" }, { "Working With Youngsters", "Yng" },
            { "Rating as Goalkeeper", "GK" }, { "Rating as Sweeper", "SW" }, { "Rating as Defender", "D" },
            { "Rating as Defensive Midfielder", "DM" }, { "Rating as Midfielder", "M" }, { "Rating as Attacking Midfielder", "AM" },
            { "Rating as Striker", "ST" }, { "Rating as Wing Back", "WB" }, { "Rating as Right side", "R" },
            { "Rating as Left side", "L" }, { "Rating as Centre", "C" }, { "Rating as Free role", "FR" }
        };

        // extraTitle: what the third column is called ("Plays as", "Job"); extraChoices: if given, a list to pick it from
        public ScoutingPanel(string title, string noun, ScoutFieldSet set, string extraTitle, string[] extraChoices)
        {
            Text = title;
            this.set = set;
            this.extraTitle = extraTitle;
            this.noun = noun;

            // the search options on the left, the table on the right
            Panel left = new Panel { Dock = DockStyle.Left, Width = 490 };
            Panel right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 10, 10) };
            Splitter splitter = new Splitter { Dock = DockStyle.Left, Width = 8, MinSize = 380, MinExtra = 260, BackColor = Color.White, Cursor = Cursors.VSplit };
            Controls.Add(right);
            Controls.Add(splitter);
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

            left.Controls.Add(new Label { Text = "Nation:", Left = 10, Top = 42, AutoSize = true });
            textBoxNation = new TextBox { Left = 50, Top = 38, Width = 130 };
            textBoxNation.TextChanged += delegate { Search(); };
            left.Controls.Add(textBoxNation);

            if (extraChoices != null)
            {
                left.Controls.Add(new Label { Text = extraTitle + ":", Left = 196, Top = 42, AutoSize = true });
                comboExtra = new ComboBox { Left = 230, Top = 38, Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
                comboExtra.Items.Add("(any)");
                comboExtra.Items.AddRange(extraChoices);
                comboExtra.SelectedIndex = 0;
                comboExtra.SelectedIndexChanged += delegate { Search(); };
                left.Controls.Add(comboExtra);
            }

            checkBoxHidden = new CheckBox { Text = "Show hidden attributes (slow to load when changed)", Left = 10, Top = 68, AutoSize = true, Checked = false };
            checkBoxHidden.CheckedChanged += delegate
            {
                ShowHidden = checkBoxHidden.Checked;
                if (ShowHiddenChanged != null)
                    ShowHiddenChanged(this, EventArgs.Empty);
            };
            left.Controls.Add(checkBoxHidden);

            checkBoxFreeAgents = new CheckBox { Text = extraChoices != null ? "No club" : "Free agents only", Left = 376, Top = extraChoices != null ? 12 : 40, AutoSize = true };
            checkBoxFreeAgents.CheckedChanged += delegate { Search(); };
            left.Controls.Add(checkBoxFreeAgents);

            GroupBox group = new CardBox { Text = "Only " + noun + " whose... are between", Left = 10, Top = 96, Width = 470, Height = 146 };
            left.Controls.Add(group);

            comboField = new ComboBox { Left = 10, Top = 20, Width = 190, DropDownStyle = ComboBoxStyle.DropDownList, MaxDropDownItems = 24 };
            comboField.SelectedIndexChanged += delegate { FieldChanged(); };
            group.Controls.Add(comboField);

            numericMin = new NumericUpDown { Left = 208, Top = 20, Width = 78, ThousandsSeparator = true };
            numericMax = new NumericUpDown { Left = 312, Top = 20, Width = 78, ThousandsSeparator = true };
            group.Controls.Add(numericMin);
            group.Controls.Add(new Label { Text = "and", Left = 288, Top = 24, AutoSize = true });
            group.Controls.Add(numericMax);
            Button add = new ModernButton { Text = "Add", Left = 398, Top = 18, Width = 62 };
            add.Click += delegate { AddCriterion(); };
            group.Controls.Add(add);

            listCriteria = new ListView
            {
                Left = 10, Top = 50, Width = 360, Height = 86,
                View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, GridLines = true
            };
            listCriteria.Columns.Add("Must be", 160);
            listCriteria.Columns.Add("From", 70, HorizontalAlignment.Right);
            listCriteria.Columns.Add("To", 70, HorizontalAlignment.Right);
            // the number columns are only as wide as they need to be: "Must be" takes the rest
            listCriteria.Resize += delegate { listCriteria.Columns[0].Width = Math.Max(120, listCriteria.ClientSize.Width - 140); };
            listCriteria.SelectedIndexChanged += delegate { buttonRemove.Enabled = listCriteria.SelectedItems.Count > 0; };
            group.Controls.Add(listCriteria);

            buttonRemove = new ModernButton { Text = "Remove", Left = 378, Top = 50, Width = 82, Enabled = false };
            buttonRemove.Click += delegate { RemoveCriterion(); };
            group.Controls.Add(buttonRemove);
            buttonClear = new ModernButton { Text = "Clear all", Left = 378, Top = 80, Width = 82, Enabled = false };
            buttonClear.Click += delegate { criteria.Clear(); CriteriaChanged(); };
            group.Controls.Add(buttonClear);

            labelCount = new Label { Left = 10, Top = 222, Width = 470, Height = 32, Text = "" };
            left.Controls.Add(labelCount);

            // the criteria list takes the height that is left
            left.Resize += delegate
            {
                group.Width = Math.Max(300, left.ClientSize.Width - 20);
                listCriteria.Width = group.Width - 110;
                buttonRemove.Left = buttonClear.Left = group.Width - 92;
                add.Left = group.Width - 72;
                labelCount.Width = group.Width;
                group.Height = Math.Max(146, left.ClientSize.Height - group.Top - 48);
                listCriteria.Height = group.Height - 60;
                labelCount.Top = group.Bottom + 6;
            };

            listResults = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, GridLines = true
            };
            tip = new ToolTip { InitialDelay = 200, ReshowDelay = 100, AutoPopDelay = 8000 };
            tipTimer = new Timer { Interval = 150 };
            tipTimer.Tick += delegate { UpdateColumnTip(); };
            tipTimer.Start();
            Disposed += delegate { tipTimer.Dispose(); tip.Dispose(); };
            listResults.ColumnClick += delegate(object sender, ColumnClickEventArgs e) { SortBy(e.Column); };
            right.Controls.Add(listResults);

            FillFieldCombo();
            BuildColumns();
        }

        // Whether the hidden attributes (current / potential ability, ambition...) can be searched on and are in the table
        public bool ShowHidden
        {
            get { return showHidden; }
            set
            {
                if (showHidden == value && checkBoxHidden.Checked == value)
                    return;
                showHidden = value;
                if (checkBoxHidden.Checked != value)
                    checkBoxHidden.Checked = value;     // which comes back here with the same value
                if (!value)
                    criteria.RemoveAll(delegate(Criterion c) { return set.All[c.Field].Hidden; });
                FillFieldCombo();
                BuildColumns();
                CriteriaChanged();
            }
        }

        bool Shown(int field)
        {
            return showHidden || !set.All[field].Hidden;
        }

        // The fields that can be searched on
        void FillFieldCombo()
        {
            int previous = comboField.SelectedIndex >= 0 && comboField.SelectedIndex < comboFields.Count ? comboFields[comboField.SelectedIndex] : -1;
            comboFields.Clear();
            comboField.Items.Clear();
            for (int i = 0; i < set.All.Length; i++)
            {
                if (!Shown(i))
                    continue;
                comboFields.Add(i);
                comboField.Items.Add(set.All[i].Hidden ? set.All[i].Name + " (hidden)" : set.All[i].Name);
            }
            int wanted = previous >= 0 && comboFields.Contains(previous) ? previous : (comboFields.Contains(ScoutFieldSet.PotentialAbility) ? ScoutFieldSet.PotentialAbility : ScoutFieldSet.Age);
            comboField.SelectedIndex = comboFields.IndexOf(wanted);
        }

        public void SetPeople(List<ScoutPlayer> people)
        {
            everybody = people;
            Search();
        }

        static string Money(long amount)
        {
            return amount.ToString("N0", CultureInfo.CurrentCulture);
        }

        static string ShortName(ScoutField field)
        {
            string name;
            return Short.TryGetValue(field.Name, out name) ? name : field.Name;
        }

        void FieldChanged()
        {
            if (comboField.SelectedIndex < 0)
                return;
            ScoutField field = set.All[comboFields[comboField.SelectedIndex]];
            numericMin.Minimum = numericMax.Minimum = field.Min;
            numericMin.Maximum = numericMax.Maximum = field.Max;
            numericMin.Value = field.Min;
            numericMax.Value = field.Max;
            numericMin.Increment = numericMax.Increment = field.Max > 1000 ? 1000 : 1;
        }

        void AddCriterion()
        {
            if (comboField.SelectedIndex < 0)
                return;
            Criterion criterion = new Criterion();
            criterion.Field = comboFields[comboField.SelectedIndex];
            criterion.Min = (int)Math.Min(numericMin.Value, numericMax.Value);
            criterion.Max = (int)Math.Max(numericMin.Value, numericMax.Value);

            // one range per number: a second one replaces the first
            criteria.RemoveAll(delegate(Criterion other) { return other.Field == criterion.Field; });
            criteria.Add(criterion);
            CriteriaChanged();
        }

        void RemoveCriterion()
        {
            if (listCriteria.SelectedItems.Count == 0)
                return;
            criteria.Remove((Criterion)listCriteria.SelectedItems[0].Tag);
            CriteriaChanged();
        }

        void CriteriaChanged()
        {
            listCriteria.BeginUpdate();
            listCriteria.Items.Clear();
            foreach (Criterion criterion in criteria)
            {
                ListViewItem item = new ListViewItem(set.All[criterion.Field].Name);
                item.SubItems.Add(ValueText(criterion.Field, criterion.Min));
                item.SubItems.Add(ValueText(criterion.Field, criterion.Max));
                item.Tag = criterion;
                listCriteria.Items.Add(item);
            }
            listCriteria.EndUpdate();
            buttonRemove.Enabled = false;
            buttonClear.Enabled = criteria.Count > 0;
            Search();
        }

        static string ValueText(int field, int value)
        {
            return field == ScoutFieldSet.Wage ? Money(value) : value.ToString();
        }

        // Name, club, the third column, age, current / potential ability, wages, then every attribute, the hidden ones
        // and the position ratings
        void BuildColumns()
        {
            columnKinds.Clear();
            columnTips.Clear();
            listResults.Columns.Clear();
            AddColumn("Name", KindName, 150, HorizontalAlignment.Left, "Name");
            AddColumn("Club", KindClub, 140, HorizontalAlignment.Left, "Club");
            AddColumn(extraTitle, KindExtra, 100, HorizontalAlignment.Left, extraTitle);
            AddColumn("Age", ScoutFieldSet.Age, 48, HorizontalAlignment.Right, "Age");
            if (showHidden)
            {
                AddColumn("CA", ScoutFieldSet.CurrentAbility, 48, HorizontalAlignment.Right, "Current Ability (hidden)");
                AddColumn("PA", ScoutFieldSet.PotentialAbility, 48, HorizontalAlignment.Right, "Potential Ability (hidden)");
            }
            AddColumn("Wages", ScoutFieldSet.Wage, 70, HorizontalAlignment.Right, "Wages per month");
            for (int i = set.FirstVisible; i < set.All.Length; i++)
                if (Shown(i))
                    AddColumn(ShortName(set.All[i]), i, 48, HorizontalAlignment.Right, set.All[i].Hidden ? set.All[i].Name + " (hidden)" : set.All[i].Name);
        }

        void AddColumn(string title, int kind, int width, HorizontalAlignment align, string tipText)
        {
            listResults.Columns.Add(title, width, align);
            columnKinds.Add(kind);
            columnTips.Add(tipText);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        // The column names are short: show the full name when the mouse rests on a heading
        void UpdateColumnTip()
        {
            if (!listResults.IsHandleCreated || !listResults.Visible || !listResults.FindForm().Visible)
                return;
            IntPtr header = SendMessage(listResults.Handle, 0x101F, IntPtr.Zero, IntPtr.Zero);   // LVM_GETHEADER
            RECT r;
            if (header == IntPtr.Zero || !GetWindowRect(header, out r))
                return;
            Point cursor = Cursor.Position;
            if (!new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top).Contains(cursor))
            {
                if (tipColumn != -2)
                {
                    tip.Hide(listResults);
                    tipColumn = -2;
                }
                return;
            }
            int x = cursor.X - r.Left;        // the header scrolls with the table
            int column = -1, left = 0;
            for (int i = 0; i < listResults.Columns.Count; i++)
            {
                left += listResults.Columns[i].Width;
                if (x < left) { column = i; break; }
            }
            if (column == tipColumn)
                return;
            tipColumn = column;
            if (column >= 0 && column < columnTips.Count)
            {
                Point at = listResults.PointToClient(new Point(cursor.X, r.Bottom));
                tip.Show(columnTips[column], listResults, at.X + 8, at.Y + 4, 8000);
            }
            else
                tip.Hide(listResults);
        }

        void SortBy(int column)
        {
            if (columnKinds[column] == sortKind)
                sortDescending = !sortDescending;
            else
            {
                sortKind = columnKinds[column];
                sortDescending = sortKind >= 0;       // numbers best first, text A to Z
            }
            Search();
        }

        static bool Contains(string text, string part)
        {
            return text.IndexOf(part, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        bool Matches(ScoutPlayer person, string name, string club, string nation)
        {
            if (name.Length > 0 && !Contains(person.Name, name))
                return false;
            if (club.Length > 0 && !Contains(person.Club, club))
                return false;
            if (nation.Length > 0 && !Contains(person.Nation, nation))
                return false;
            if (checkBoxFreeAgents.Checked && person.Club.Length > 0)
                return false;
            if (comboExtra != null && comboExtra.SelectedIndex > 0 && person.Positions != (string)comboExtra.SelectedItem)
                return false;
            foreach (Criterion criterion in criteria)
            {
                int value = person.Values[criterion.Field];
                if (value < criterion.Min || value > criterion.Max)
                    return false;
            }
            return true;
        }

        int Compare(ScoutPlayer a, ScoutPlayer b)
        {
            // the order that was asked for may be on a hidden column: then it is by name
            int kind = columnKinds.Contains(sortKind) ? sortKind : KindName;
            int result;
            if (kind >= 0)
                result = a.Values[kind].CompareTo(b.Values[kind]);
            else if (kind == KindClub)
                result = string.Compare(a.Club, b.Club, StringComparison.CurrentCultureIgnoreCase);
            else if (kind == KindExtra)
                result = string.Compare(a.Positions, b.Positions, StringComparison.CurrentCultureIgnoreCase);
            else
                result = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            if (sortDescending && columnKinds.Contains(sortKind))
                result = -result;
            return result != 0 ? result : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        }

        void Search()
        {
            int selected = listResults.SelectedItems.Count > 0 ? ((ScoutPlayer)listResults.SelectedItems[0].Tag).StaffId : -1;

            string name = textBoxName.Text.Trim();
            string club = textBoxClub.Text.Trim();
            string nation = textBoxNation.Text.Trim();
            List<ScoutPlayer> found = new List<ScoutPlayer>();
            foreach (ScoutPlayer person in everybody)
                if (Matches(person, name, club, nation))
                    found.Add(person);
            found.Sort(Compare);

            listResults.BeginUpdate();
            listResults.Items.Clear();
            for (int i = 0; i < found.Count && i < MaxListed; i++)
            {
                ScoutPlayer person = found[i];
                ListViewItem item = new ListViewItem(person.Name);
                for (int column = 1; column < columnKinds.Count; column++)
                {
                    int kind = columnKinds[column];
                    if (kind == KindClub)
                        item.SubItems.Add(person.Club);
                    else if (kind == KindExtra)
                        item.SubItems.Add(person.Positions);
                    else
                        item.SubItems.Add(ValueText(kind, person.Values[kind]));
                }
                item.Tag = person;
                listResults.Items.Add(item);
                if (person.StaffId == selected)
                    item.Selected = true;
            }
            listResults.EndUpdate();

            if (everybody.Count == 0)
                labelCount.Text = "";
            else if (found.Count > MaxListed)
                labelCount.Text = string.Format("{0} {1} match; showing the first {2} in this order - click a column heading to sort, or add more to the search.", found.Count, noun, MaxListed);
            else
                labelCount.Text = string.Format("{0} of {1} {2} match. Click a column heading to sort.", found.Count, everybody.Count, noun);
        }
    }
}
