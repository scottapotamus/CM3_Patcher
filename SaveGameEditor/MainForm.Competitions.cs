using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CM3SaveGameEditor
{
    // The "Competitions" tab: rename leagues and cups (name and short name)
    public partial class MainForm
    {
        TextBox textBoxCompetitionFilter;
        ListView listCompetitions;
        TextBox textBoxCompetitionName;
        TextBox textBoxCompetitionShort;
        Button buttonSetCompetition;
        Button buttonUndoCompetition;
        Button buttonClearCompetitions;
        Label labelCompetitionEdits;

        List<Competition> competitions = new List<Competition>();
        Dictionary<int, CompetitionEdit> competitionEdits = new Dictionary<int, CompetitionEdit>();

        void BuildCompetitionsTab()
        {
            TabPage page = new TabPage("Competitions");
            tabs.TabPages.Add(page);

            page.Controls.Add(new Label { Text = "Find a competition:", Left = 10, Top = 14, AutoSize = true });
            textBoxCompetitionFilter = new TextBox { Left = 120, Top = 10, Width = 200 };
            textBoxCompetitionFilter.TextChanged += delegate { FillCompetitions(); };
            page.Controls.Add(textBoxCompetitionFilter);

            listCompetitions = new ListView
            {
                Left = 10, Top = 38, Width = 700, Height = 200,
                View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, GridLines = true
            };
            listCompetitions.Columns.Add("Competition", 300);
            listCompetitions.Columns.Add("Short name", 170);
            listCompetitions.Columns.Add("Kind", 110);
            listCompetitions.Columns.Add("Nation", 130);
            listCompetitions.SelectedIndexChanged += delegate { ShowSelectedCompetition(); };
            page.Controls.Add(listCompetitions);

            GroupBox group = new GroupBox { Text = "The selected competition", Left = 10, Top = 248, Width = 700, Height = 100 };
            page.Controls.Add(group);
            group.Controls.Add(new Label { Text = "Name:", Left = 10, Top = 28, AutoSize = true });
            textBoxCompetitionName = new TextBox { Left = 100, Top = 24, Width = 330, MaxLength = SaveGameFile.MaxCompetitionName, Enabled = false };
            group.Controls.Add(textBoxCompetitionName);
            group.Controls.Add(new Label { Text = "Short name:", Left = 10, Top = 62, AutoSize = true });
            textBoxCompetitionShort = new TextBox { Left = 100, Top = 58, Width = 190, MaxLength = SaveGameFile.MaxCompetitionShortName, Enabled = false };
            group.Controls.Add(textBoxCompetitionShort);
            buttonSetCompetition = new Button { Text = "Set", Left = 450, Top = 22, Width = 80, Enabled = false };
            buttonSetCompetition.Click += delegate { SetSelectedCompetition(); };
            group.Controls.Add(buttonSetCompetition);
            buttonUndoCompetition = new Button { Text = "Undo", Left = 540, Top = 22, Width = 80, Enabled = false };
            buttonUndoCompetition.Click += delegate { UndoSelectedCompetition(); };
            group.Controls.Add(buttonUndoCompetition);

            page.Controls.Add(new Label
            {
                Left = 10, Top = 356, Width = 700, Height = 52,
                Text = "A competition's saved data is stored under its name, so renaming it renames that too. Names are changed in this " +
                       "saved game only: a new game starts with the original names. The prize money of a competition isn't stored in " +
                       "the saved game, so it can't be changed here."
            });

            labelCompetitionEdits = new Label { Left = 10, Top = 452, Width = 380, Height = 18, Text = "" };
            page.Controls.Add(labelCompetitionEdits);
            buttonClearCompetitions = new Button { Text = "Clear all", Left = 636, Top = 446, Width = 74, Enabled = false };
            buttonClearCompetitions.Click += delegate { competitionEdits.Clear(); FillCompetitions(); ShowSelectedCompetition(); UpdateCompetitionEdits(); };
            page.Controls.Add(buttonClearCompetitions);

            StretchList(page, listCompetitions);
        }

        CompetitionEdit CompetitionEditOf(Competition competition)
        {
            CompetitionEdit edit;
            if (competitionEdits.TryGetValue(competition.Id, out edit))
                return edit;
            edit = new CompetitionEdit();
            edit.Name = competition.Name;
            edit.ShortName = competition.ShortName;
            return edit;
        }

        void FillCompetitions()
        {
            string filter = textBoxCompetitionFilter.Text.Trim();
            int selected = SelectedCompetitionId();

            listCompetitions.BeginUpdate();
            listCompetitions.Items.Clear();
            foreach (Competition competition in competitions)
            {
                CompetitionEdit edit = CompetitionEditOf(competition);
                if (filter.Length > 0 && edit.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0 &&
                    competition.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0)
                    continue;
                ListViewItem item = new ListViewItem(edit.Name);
                item.SubItems.Add(edit.ShortName);
                item.SubItems.Add(competition.National ? "National teams" : "Clubs");
                item.SubItems.Add(competition.Nation);
                item.Tag = competition;
                item.BackColor = competitionEdits.ContainsKey(competition.Id) ? Color.LightYellow : Color.White;
                listCompetitions.Items.Add(item);
                if (competition.Id == selected)
                    item.Selected = true;
            }
            listCompetitions.EndUpdate();
        }

        int SelectedCompetitionId()
        {
            return listCompetitions.SelectedItems.Count == 0 ? -1 : ((Competition)listCompetitions.SelectedItems[0].Tag).Id;
        }

        void ShowSelectedCompetition()
        {
            bool any = listCompetitions.SelectedItems.Count > 0;
            textBoxCompetitionName.Enabled = textBoxCompetitionShort.Enabled = buttonSetCompetition.Enabled = any;
            if (!any)
            {
                buttonUndoCompetition.Enabled = false;
                return;
            }
            Competition competition = (Competition)listCompetitions.SelectedItems[0].Tag;
            CompetitionEdit edit = CompetitionEditOf(competition);
            textBoxCompetitionName.Text = edit.Name;
            textBoxCompetitionShort.Text = edit.ShortName;
            buttonUndoCompetition.Enabled = competitionEdits.ContainsKey(competition.Id);
        }

        void SetSelectedCompetition()
        {
            if (listCompetitions.SelectedItems.Count == 0)
                return;
            Competition competition = (Competition)listCompetitions.SelectedItems[0].Tag;
            string name = textBoxCompetitionName.Text.Trim();
            string shortName = textBoxCompetitionShort.Text.Trim();
            if (name.Length == 0 || shortName.Length == 0)
            {
                MessageBox.Show(this, "The names can't be empty.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            CompetitionEdit edit = new CompetitionEdit();
            edit.Name = name;
            edit.ShortName = shortName;
            if (edit.SameAs(competition))
                competitionEdits.Remove(competition.Id);
            else
                competitionEdits[competition.Id] = edit;
            FillCompetitions();
            ShowSelectedCompetition();
            UpdateCompetitionEdits();
        }

        void UndoSelectedCompetition()
        {
            if (listCompetitions.SelectedItems.Count == 0)
                return;
            competitionEdits.Remove(((Competition)listCompetitions.SelectedItems[0].Tag).Id);
            FillCompetitions();
            ShowSelectedCompetition();
            UpdateCompetitionEdits();
        }

        void UpdateCompetitionEdits()
        {
            labelCompetitionEdits.Text = competitionEdits.Count == 0 ? "No competitions changed yet." : string.Format("{0} competition(s) changed, not saved yet.", competitionEdits.Count);
            buttonClearCompetitions.Enabled = competitionEdits.Count > 0;
        }
    }
}
