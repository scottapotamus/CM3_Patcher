using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace CM3SaveGameEditor
{
    // A contracts tab: the contracts of the players or of the staff of the save. The changes are kept in a list that both
    // tabs share (by contract number) and are written when Apply is pressed.
    public class ContractsPanel : TabPage
    {
        bool staff;
        Dictionary<int, SaveGameFile.ContractEdit> contractEdits;
        Action changed;
        List<SaveGameFile.Contract> contracts = new List<SaveGameFile.Contract>();

        TextBox textBoxContractFilter;
        CheckBox checkBoxOnlyClauses;
        Label labelContractCount;
        ListView listContracts;
        ComboBox comboPartFull;
        ComboBox comboKind;
        NumericUpDown numericWage;
        DateTimePicker dateExpiry;
        ComboBox comboStatus;
        CheckBox[] checkBoxBonus = new CheckBox[SaveGameFile.BonusTypes];
        NumericUpDown[] numericBonus = new NumericUpDown[SaveGameFile.BonusTypes];
        CheckBox[] checkBoxClauses = new CheckBox[SaveGameFile.ClauseTypes];
        NumericUpDown numericFee;
        Button buttonSetContract;
        ComboBox comboClub;
        List<int> clubIds = new List<int>();            // the club of each item of the club box
        Dictionary<int, string> clubNames = new Dictionary<int, string>();
        Button buttonClearContracts;
        Label labelContractEdits;


        static readonly string[] ShortClauseNames = { "Non Promotion", "Min Fee Release", "Non Playing", "Relegation", "Match Highest Wage" };

        // contract type: bits 0-5 of the contract's type byte (bit 7 = part time)
        static readonly string[] KindNames = { "Contract", "Monthly Contract", "Trial Contract", "Loan Contract" };
        static readonly int[] KindValues = { 2, 1, 5, 6 };
        static readonly string[] StatusNames = {
            "Uncertain", "Indispensable to the club", "Important first team player", "Used in a squad rotation system",
            "Backup for the first team", "Hot prospect for the future", "Decent young player", "Not needed by the club"
        };
        static readonly string[] BonusNames = { "Goal bonus", "Assist bonus", "Clean sheet bonus" };

        const int MaxListedContracts = 300;

        // The clubs a contract can be moved to
        public void SetClubs(List<SaveGameFile.Club> clubs)
        {
            List<SaveGameFile.Club> sorted = new List<SaveGameFile.Club>(clubs);
            sorted.Sort(delegate(SaveGameFile.Club a, SaveGameFile.Club b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });
            clubIds.Clear();
            clubNames.Clear();
            comboClub.Items.Clear();
            foreach (SaveGameFile.Club club in sorted)
            {
                clubIds.Add(club.Id);
                clubNames[club.Id] = club.Name;
                comboClub.Items.Add(club.Name);
            }
        }

        string ClubText(int clubId, string original)
        {
            string name;
            return clubNames.TryGetValue(clubId, out name) ? name : original;
        }

        public void SetContracts(List<SaveGameFile.Contract> all)
        {
            contracts = all.FindAll(delegate(SaveGameFile.Contract contract) { return contract.IsStaff == staff; });
            FillContracts();
            UpdateContractEdits();
            ShowSelectedContract();
        }

        // After the shared list of changes changed
        public void RefreshEdits()
        {
            FillContracts();
            UpdateContractEdits();
        }

        public ContractsPanel(string title, bool staff, Dictionary<int, SaveGameFile.ContractEdit> contractEdits, Action changed)
        {
            Text = title;
            this.staff = staff;
            this.contractEdits = contractEdits;
            this.changed = changed;
            TabPage page = this;

            page.Controls.Add(new Label { Text = "Find a player or club:", Left = 10, Top = 14, AutoSize = true });
            textBoxContractFilter = new TextBox { Left = 130, Top = 10, Width = 170 };
            textBoxContractFilter.TextChanged += delegate { FillContracts(); };
            page.Controls.Add(textBoxContractFilter);
            checkBoxOnlyClauses = new CheckBox { Text = "Only with clauses", Left = 316, Top = 12, AutoSize = true };
            checkBoxOnlyClauses.CheckedChanged += delegate { FillContracts(); };
            page.Controls.Add(checkBoxOnlyClauses);

            listContracts = new ListView
            {
                Left = 10, Top = 38, Width = 468, Height = 130, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, GridLines = true
            };
            listContracts.Columns.Add(staff ? "Name" : "Player", 120);
            listContracts.Columns.Add("Club", 100);
            listContracts.Columns.Add("Per month", 70, HorizontalAlignment.Right);
            listContracts.Columns.Add("Expires", 60);
            listContracts.Columns.Add(staff ? "Job" : "Clauses", 100);
            listContracts.SelectedIndexChanged += delegate { ShowSelectedContract(); };
            page.Controls.Add(listContracts);

            labelContractCount = new Label { Left = 10, Top = 170, Width = 468, Height = 16, Text = "" };
            page.Controls.Add(labelContractCount);

            GroupBox group = new CardBox { Text = "The selected contract", Left = 10, Top = 188, Width = 468, Height = 262 };
            page.Controls.Add(group);

            group.Controls.Add(new Label { Text = "Type:", Left = 10, Top = 24, AutoSize = true });
            comboPartFull = new ComboBox { Left = 70, Top = 20, Width = 90, DropDownStyle = ComboBoxStyle.DropDownList, Enabled = false };
            comboPartFull.Items.AddRange(new object[] { "Full Time", "Part Time" });
            group.Controls.Add(comboPartFull);
            comboKind = new ComboBox { Left = 168, Top = 20, Width = 150, DropDownStyle = ComboBoxStyle.DropDownList, Enabled = false };
            comboKind.Items.AddRange(KindNames);
            group.Controls.Add(comboKind);

            group.Controls.Add(new Label { Text = "Wages per month:", Left = 10, Top = 52, AutoSize = true });
            numericWage = new NumericUpDown { Left = 120, Top = 48, Width = 100, Minimum = 0, Maximum = 500000000, ThousandsSeparator = true, Increment = 1000, Enabled = false };
            group.Controls.Add(numericWage);
            group.Controls.Add(new Label { Text = "Expires:", Left = 250, Top = 52, AutoSize = true });
            dateExpiry = new DateTimePicker
            {
                Left = 304, Top = 48, Width = 90, Format = DateTimePickerFormat.Custom, CustomFormat = "d.M.yy",
                MinDate = new DateTime(1900, 1, 1), MaxDate = new DateTime(2099, 12, 31), Enabled = false
            };
            group.Controls.Add(dateExpiry);

            group.Controls.Add(new Label { Text = "Squad status:", Left = 10, Top = 80, AutoSize = true });
            comboStatus = new ComboBox { Left = 100, Top = 76, Width = 250, DropDownStyle = ComboBoxStyle.DropDownList, Enabled = false };
            comboStatus.Items.AddRange(StatusNames);
            group.Controls.Add(comboStatus);

            group.Controls.Add(new Label { Text = "Bonuses (the game rounds what it shows):", Left = 10, Top = 108, AutoSize = true });
            for (int t = 0; t < SaveGameFile.BonusTypes; t++)
            {
                checkBoxBonus[t] = new CheckBox { Text = BonusNames[t], Left = 20, Top = 128 + t * 24, Width = 130, Enabled = false };
                numericBonus[t] = new NumericUpDown { Left = 154, Top = 126 + t * 24, Width = 90, Minimum = 0, Maximum = 2000000000, ThousandsSeparator = true, Increment = 10, Enabled = false };
                group.Controls.Add(checkBoxBonus[t]);
                group.Controls.Add(numericBonus[t]);
            }

            group.Controls.Add(new Label { Text = "Clauses:", Left = 270, Top = 108, AutoSize = true });
            for (int t = 0; t < SaveGameFile.ClauseTypes; t++)
            {
                checkBoxClauses[t] = new CheckBox { Text = SaveGameFile.ClauseNames[t], Left = 270, Top = 126 + t * 20, Width = 190, Enabled = false };
                group.Controls.Add(checkBoxClauses[t]);
            }

            group.Controls.Add(new Label { Text = "Release fee (shared by the clauses):", Left = 10, Top = 232, AutoSize = true });
            numericFee = new NumericUpDown { Left = 200, Top = 228, Width = 110, Minimum = 0, Maximum = 2000000000, ThousandsSeparator = true, Increment = 10000, Enabled = false };
            group.Controls.Add(numericFee);
            buttonSetContract = new ModernButton { Text = "Set", Left = 330, Top = 226, Width = 60, Enabled = false };
            buttonSetContract.Click += delegate { SetSelectedContract(); };
            group.Controls.Add(buttonSetContract);

            // the club: the rows from the squad status down move down to make room
            foreach (Control control in group.Controls)
                if (control.Top >= 76)
                    control.Top += 28;
            group.Height += 28;
            group.Controls.Add(new Label { Text = "Club:", Left = 10, Top = 84, AutoSize = true });
            comboClub = new ComboBox
            {
                Left = 100, Top = 80, Width = 300, DropDownStyle = ComboBoxStyle.DropDown, Enabled = false,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems
            };
            group.Controls.Add(comboClub);

            labelContractEdits = new Label { Left = 10, Top = 490, Width = 380, Height = 18, Text = "" };
            page.Controls.Add(labelContractEdits);
            buttonClearContracts = new ModernButton { Text = "Clear all", Left = 404, Top = 486, Width = 74, Enabled = false };
            buttonClearContracts.Click += delegate { contractEdits.Clear(); this.changed(); };
            page.Controls.Add(buttonClearContracts);
            if (staff)
            {
                // only the contract type, the wages and the expiry date mean something for staff
                foreach (Control control in group.Controls)
                    if (control != buttonSetContract && control != comboClub && control.Top >= 104 && !(control is Label && control.Text == "Club:"))
                        control.Visible = false;
                buttonSetContract.Top = 108;
                group.Height = 148;
                checkBoxOnlyClauses.Visible = false;
            }
            MainForm.StretchList(page, listContracts);
        }

        static string ClauseText(byte[] flags)
        {
            StringBuilder text = new StringBuilder();
            for (int t = 0; t < flags.Length; t++)
            {
                if (flags[t] == 0)
                    continue;
                if (text.Length > 0)
                    text.Append(", ");
                text.Append(ShortClauseNames[t]);
            }
            return text.Length == 0 ? "" : text.ToString();
        }

        // day of the year (counted from 0) and year <-> a date
        static DateTime ExpiryDate(int day, int year)
        {
            try
            {
                return new DateTime(Math.Max(1900, Math.Min(2099, year)), 1, 1).AddDays(day);
            }
            catch (ArgumentException)
            {
                return new DateTime(1999, 1, 1);
            }
        }

        static int KindIndex(int typeByte)
        {
            return Array.IndexOf(KindValues, typeByte & 0x3F);
        }

        // The editor's controls as an edit of the contract
        SaveGameFile.ContractEdit EditFromControls(SaveGameFile.Contract contract)
        {
            SaveGameFile.ContractEdit edit = contract.ToEdit();

            // contract type: keep the game's own value if the kind wasn't changed (e.g. 7 is also a loan)
            int low = contract.TypeByte & 0x3F;
            if (comboKind.SelectedIndex >= 0 && comboKind.SelectedIndex != KindIndex(contract.TypeByte))
                low = KindValues[comboKind.SelectedIndex];
            edit.TypeByte = (byte)((contract.TypeByte & 0x40) | (comboPartFull.SelectedIndex == 1 ? 0x80 : 0) | low);

            edit.Wage = (int)Math.Round((double)numericWage.Value / 4.0);
            DateTime date = dateExpiry.Value.Date;
            edit.ExpiryYear = date.Year;
            edit.ExpiryDay = (date - new DateTime(date.Year, 1, 1)).Days;

            if (comboStatus.SelectedIndex >= 0)
                edit.StatusByte = (byte)((contract.StatusByte & 0x0F) | (comboStatus.SelectedIndex << 4));

            for (int t = 0; t < SaveGameFile.BonusTypes; t++)
                edit.Bonuses[t] = checkBoxBonus[t].Checked ? (int)numericBonus[t].Value : -1;

            bool any = false;
            for (int t = 0; t < SaveGameFile.ClauseTypes; t++)
            {
                // keep the game's own state (e.g. "triggered") for a clause that stays, a new clause starts as 1
                edit.Flags[t] = !checkBoxClauses[t].Checked ? (byte)0 : (contract.Flags[t] != 0 ? contract.Flags[t] : (byte)1);
                if (edit.Flags[t] != 0)
                    any = true;
            }
            edit.Fee = any ? (int)numericFee.Value : 0;
            int club = comboClub.FindStringExact(comboClub.Text);
            if (club >= 0)
                edit.ClubId = clubIds[club];
            else if (comboClub.Text.Length > 0)
                throw new InvalidOperationException("There is no club called \"" + comboClub.Text + "\".");
            return edit;
        }

        void FillContracts()
        {
            string filter = textBoxContractFilter.Text.Trim();
            int selected = SelectedContractRecord();
            int matches = 0;

            listContracts.BeginUpdate();
            listContracts.Items.Clear();
            foreach (SaveGameFile.Contract contract in contracts)
            {
                if (checkBoxOnlyClauses.Checked && !contract.HasClauses)
                    continue;
                if (filter.Length > 0 && contract.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0 &&
                    contract.Club.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0)
                    continue;
                matches++;
                if (matches > MaxListedContracts && contract.Record != selected)
                    continue;

                SaveGameFile.ContractEdit edit;
                bool edited = contractEdits.TryGetValue(contract.Record, out edit);
                int wage = edited ? edit.Wage : contract.Wage;
                DateTime expiry = edited ? ExpiryDate(edit.ExpiryDay, edit.ExpiryYear) : ExpiryDate(contract.ExpiryDay, contract.ExpiryYear);

                ListViewItem item = new ListViewItem(contract.Name);
                item.SubItems.Add(ClubText(edited ? edit.ClubId : contract.ClubId, contract.Club));
                item.SubItems.Add(MainForm.Money((long)wage * 4));
                item.SubItems.Add(expiry.ToString("d.M.yy"));
                item.SubItems.Add(staff ? contract.Job : ClauseText(edited ? edit.Flags : contract.Flags));
                item.Tag = contract;
                if (edited)
                    item.BackColor = Color.LightYellow;
                listContracts.Items.Add(item);
                if (contract.Record == selected)
                    item.Selected = true;
            }
            listContracts.EndUpdate();

            labelContractCount.Text = matches > MaxListedContracts
                ? string.Format("Showing the first {0} of {1} contracts - type a name or club to narrow it down.", MaxListedContracts, matches)
                : string.Format("{0} contract(s).", matches);
        }

        int SelectedContractRecord()
        {
            if (listContracts.SelectedItems.Count == 0)
                return -1;
            return ((SaveGameFile.Contract)listContracts.SelectedItems[0].Tag).Record;
        }

        void ShowSelectedContract()
        {
            bool any = listContracts.SelectedItems.Count > 0;
            comboPartFull.Enabled = comboKind.Enabled = numericWage.Enabled = dateExpiry.Enabled = comboStatus.Enabled = any;
            numericFee.Enabled = buttonSetContract.Enabled = comboClub.Enabled = any;
            foreach (CheckBox box in checkBoxBonus)
                box.Enabled = any;
            foreach (NumericUpDown box in numericBonus)
                box.Enabled = any;
            foreach (CheckBox box in checkBoxClauses)
                box.Enabled = any;
            if (!any)
                return;

            SaveGameFile.Contract contract = (SaveGameFile.Contract)listContracts.SelectedItems[0].Tag;
            SaveGameFile.ContractEdit edit;
            if (!contractEdits.TryGetValue(contract.Record, out edit))
                edit = contract.ToEdit();

            comboPartFull.SelectedIndex = (edit.TypeByte & 0x80) != 0 ? 1 : 0;
            comboKind.SelectedIndex = KindIndex(edit.TypeByte);
            numericWage.Value = Math.Min(numericWage.Maximum, Math.Max(numericWage.Minimum, (decimal)edit.Wage * 4));
            dateExpiry.Value = ExpiryDate(edit.ExpiryDay, edit.ExpiryYear);
            int status = edit.StatusByte >> 4;
            comboStatus.SelectedIndex = status < StatusNames.Length ? status : -1;
            for (int t = 0; t < SaveGameFile.BonusTypes; t++)
            {
                checkBoxBonus[t].Checked = edit.Bonuses[t] >= 0;
                numericBonus[t].Value = Math.Min(numericBonus[t].Maximum, Math.Max(numericBonus[t].Minimum, edit.Bonuses[t]));
            }
            for (int t = 0; t < SaveGameFile.ClauseTypes; t++)
                checkBoxClauses[t].Checked = edit.Flags[t] != 0;
            numericFee.Value = Math.Min(numericFee.Maximum, Math.Max(numericFee.Minimum, edit.Fee));
            comboClub.SelectedIndex = clubIds.IndexOf(edit.ClubId);
        }

        void SetSelectedContract()
        {
            if (listContracts.SelectedItems.Count == 0)
                return;
            SaveGameFile.Contract contract = (SaveGameFile.Contract)listContracts.SelectedItems[0].Tag;
            SaveGameFile.ContractEdit edit;
            try
            {
                edit = EditFromControls(contract);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this, ex.Message, "Contracts", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (edit.SameAs(contract))
                contractEdits.Remove(contract.Record);
            else
                contractEdits[contract.Record] = edit;
            changed();
        }

        void UpdateContractEdits()
        {
            labelContractEdits.Text = contractEdits.Count == 0 ? "No contracts changed yet." : string.Format("{0} contract(s) changed, not saved yet.", contractEdits.Count);
            buttonClearContracts.Enabled = contractEdits.Count > 0;
        }
    }
}
