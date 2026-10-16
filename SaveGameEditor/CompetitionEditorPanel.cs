using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using CM3ResolutionPatcher;

namespace CM3SaveGameEditor
{
    // The Competition Editor: renames the leagues and cups. The game looks them up by name (cm3.exe has a table of names and a
    // saved game that doesn't agree with it stops with "v3.04e index.cpp 1635"), so the name is changed in the game (cm3.exe, its
    // tables, language files and picture list) and in the saved game together. See CompetitionRenamer.
    public class CompetitionEditorPanel : Panel
    {
        const string Title = "CM3 Utils";

        Func<string> savePath;                  // the saved game chosen at the top of the window ("" = none)
        public Action<string> SaveWritten;      // called with the saved game that was written

        TextBox textBoxExe;
        Label labelExe;
        CheckBox checkBoxBackup;
        TextBox textBoxFilter;
        ListView listComps;
        TextBox textBoxName;
        TextBox textBoxShort;
        Button buttonSet;
        Button buttonUndo;
        Button buttonOriginal;
        Button buttonClear;
        Label labelEdits;
        Label labelInfo;
        Label labelSave;
        CheckBox checkBoxSave;
        CheckBox checkBoxOverwrite;
        Button buttonApply;

        List<GameCompetition> comps = new List<GameCompetition>();
        Dictionary<int, GameCompetitionChange> pending = new Dictionary<int, GameCompetitionChange>();
        string compsRead = "";          // the exe the list was read from

        public CompetitionEditorPanel(Func<string> savePath)
        {
            this.savePath = savePath;
            BackColor = Theme.Page;
            Font = Theme.Normal;

            // the game's exe, above everything
            Panel top = new Panel { Dock = DockStyle.Top, Height = 96, BackColor = Theme.Page };
            top.Controls.Add(new Label { Text = "Championship Manager 3 exe (cm3.exe):", Left = 12, Top = 10, AutoSize = true, Font = Theme.Bold });
            textBoxExe = new TextBox { Left = 12, Top = 30, Width = 420 };
            textBoxExe.TextChanged += delegate { compsRead = ""; if (Visible) LoadComps(); };
            Button browse = new ModernButton { Text = "Browse...", Left = 440, Top = 28, Width = 76, Height = 24 };
            browse.Click += delegate
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = "Select cm3.exe";
                    dialog.Filter = "CM3 executable (cm3.exe)|cm3.exe|Executables (*.exe)|*.exe";
                    if (File.Exists(textBoxExe.Text))
                        dialog.InitialDirectory = Path.GetDirectoryName(textBoxExe.Text);
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        textBoxExe.Text = dialog.FileName;
                }
            };
            top.Controls.Add(textBoxExe);
            top.Controls.Add(browse);
            labelExe = new Label { Left = 12, Top = 58, Width = 700, Height = 18, Text = "" };
            top.Controls.Add(labelExe);
            checkBoxBackup = new CheckBox { Text = "Keep a copy of the original exe (cm3_original.exe) the first time", Left = 12, Top = 74, AutoSize = true, Checked = true };
            top.Controls.Add(checkBoxBackup);
            top.Resize += delegate
            {
                browse.Left = top.ClientSize.Width - browse.Width - 12;
                textBoxExe.Width = browse.Left - 8 - textBoxExe.Left;
                labelExe.Width = top.ClientSize.Width - 24;
            };

            Panel bar = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Theme.Page };
            buttonApply = new ModernButton { Text = "Apply", Left = 12, Top = 10, Width = 140, Height = 32, Font = Theme.Bold, Kind = ButtonKind.Primary };
            buttonApply.Click += delegate { ApplyChanges(); };
            bar.Controls.Add(buttonApply);
            bar.Controls.Add(new Label { Text = "Renames the leagues and cups in the game and in the saved game together. Close the game first.", Left = 164, Top = 18, AutoSize = true, ForeColor = Color.FromArgb(80, 80, 110) });

            Panel content = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Page };
            Controls.Add(content);
            Controls.Add(bar);
            Controls.Add(top);
            content.BringToFront();

            GroupBox card = new CardBox { Text = "Leagues and cups", Left = 12, Top = 12, Width = 740, Height = 352, ForeColor = Theme.Navy };
            content.Controls.Add(card);
            card.Controls.Add(new Label { Text = "Find:", Left = 12, Top = 28, AutoSize = true });
            textBoxFilter = new TextBox { Left = 56, Top = 24, Width = 200 };
            textBoxFilter.TextChanged += delegate { FillComps(); };
            card.Controls.Add(textBoxFilter);

            listComps = new ListView
            {
                Left = 12, Top = 54, Width = 716, Height = 112, View = View.Details, FullRowSelect = true, HideSelection = false,
                MultiSelect = false, GridLines = true, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            listComps.Columns.Add("In the game now", 260);
            listComps.Columns.Add("Short name", 170);
            listComps.Columns.Add("Original name", 260);
            listComps.SelectedIndexChanged += delegate { ShowSelected(); };
            card.Controls.Add(listComps);

            card.Controls.Add(new Label { Text = "Name:", Left = 12, Top = 184, AutoSize = true });
            textBoxName = new TextBox { Left = 100, Top = 180, Width = 330, MaxLength = CompetitionRenamer.MaxName, Enabled = false };
            card.Controls.Add(textBoxName);
            card.Controls.Add(new Label { Text = "Short name:", Left = 12, Top = 216, AutoSize = true });
            textBoxShort = new TextBox { Left = 100, Top = 212, Width = 190, MaxLength = CompetitionRenamer.MaxShort, Enabled = false };
            card.Controls.Add(textBoxShort);

            buttonSet = new ModernButton { Text = "Set", Left = 450, Top = 178, Width = 70, Enabled = false };
            buttonSet.Click += delegate { SetSelected(); };
            card.Controls.Add(buttonSet);
            buttonUndo = new ModernButton { Text = "Undo", Left = 528, Top = 178, Width = 70, Enabled = false };
            buttonUndo.Click += delegate { UndoSelected(); };
            card.Controls.Add(buttonUndo);
            buttonOriginal = new ModernButton { Text = "Original", Left = 450, Top = 210, Width = 148, Enabled = false };
            buttonOriginal.Click += delegate { UseOriginalNames(); };
            card.Controls.Add(buttonOriginal);

            labelInfo = new Label { Left = 12, Top = 248, Width = 716, Height = 22, ForeColor = Color.FromArgb(90, 90, 120) };
            card.Controls.Add(labelInfo);
            labelEdits = new Label { Left = 12, Top = 276, Width = 420, Height = 18, Text = "" };
            card.Controls.Add(labelEdits);
            buttonClear = new ModernButton { Text = "Clear all", Left = 654, Top = 272, Width = 74, Enabled = false, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            buttonClear.Click += delegate { pending.Clear(); FillComps(); ShowSelected(); UpdateEdits(); };
            card.Controls.Add(buttonClear);
            card.Controls.Add(new Label
            {
                Left = 12, Top = 304, Width = 716, Height = 40, ForeColor = Color.FromArgb(90, 90, 120),
                Text = "The game looks leagues and cups up by name, so the name is changed in the game and in the saved game together (otherwise the " +
                       "save won't load)."
            });

            GroupBox save = new CardBox { Text = "The saved game", Left = 12, Top = 376, Width = 740, Height = 110, ForeColor = Theme.Navy };
            content.Controls.Add(save);
            labelSave = new Label { Left = 12, Top = 26, Width = 716, Height = 18, Text = "" };
            save.Controls.Add(labelSave);
            checkBoxSave = new CheckBox { Text = "Make this saved game use the same names (also renames it when you have changed some above)", Left = 12, Top = 52, AutoSize = true, Checked = true };
            save.Controls.Add(checkBoxSave);
            checkBoxOverwrite = new CheckBox
            {
                Text = "Overwrite the saved game (a .bak copy is made first). Otherwise a new file is saved next to it as <name>_renamed.sav.",
                Left = 12, Top = 80, AutoSize = true, Checked = false
            };
            save.Controls.Add(checkBoxOverwrite);

            content.Resize += delegate
            {
                int width = Math.Min(980, Math.Max(520, content.ClientSize.Width - 28));
                card.Width = width;
                save.Width = width;
            };

            textBoxExe.Text = PatcherPanel.FindDefaultExe();
            UpdateEdits();
            RefreshSave();
            VisibleChanged += delegate { if (Visible) { RefreshSave(); LoadComps(); } };
        }

        // The saved game at the top of the window has changed
        public void RefreshSave()
        {
            string path = savePath();
            if (path.Length > 0 && File.Exists(path))
            {
                labelSave.Text = path;
                checkBoxSave.Enabled = true;
            }
            else
            {
                labelSave.Text = "No saved game chosen (use Browse at the top of the window).";
                checkBoxSave.Enabled = false;
            }
        }

        // Reads the competitions of the game of the exe that is selected
        void LoadComps()
        {
            string exe = textBoxExe.Text;
            labelExe.ForeColor = Color.FromArgb(30, 30, 60);
            if (!File.Exists(exe))
            {
                comps.Clear();
                compsRead = "";
                labelExe.Text = "";
                FillComps();
                return;
            }
            if (compsRead == exe && comps.Count > 0)
                return;
            try
            {
                if (!CM3ResolutionChanger.IsCM3Exe(exe))
                {
                    comps.Clear();
                    compsRead = "";
                    labelExe.ForeColor = Color.Firebrick;
                    labelExe.Text = "This isn't a supported CM3 v3.04e cm3.exe.";
                    FillComps();
                    return;
                }
                comps = CompetitionRenamer.Read(exe);
                compsRead = exe;
                pending.Clear();
                labelExe.Text = comps.Count == 0 ? "No competitions were found in the game's Data folder." : "";
            }
            catch (Exception ex)
            {
                comps.Clear();
                compsRead = "";
                labelExe.ForeColor = Color.Firebrick;
                labelExe.Text = "Couldn't read the game's competitions: " + ex.Message;
            }
            FillComps();
            ShowSelected();
            UpdateEdits();
        }

        GameCompetition Selected()
        {
            return listComps.SelectedItems.Count == 0 ? null : (GameCompetition)listComps.SelectedItems[0].Tag;
        }

        void FillComps()
        {
            string filter = textBoxFilter.Text.Trim();
            GameCompetition selected = Selected();
            listComps.BeginUpdate();
            listComps.Items.Clear();
            foreach (GameCompetition c in comps)
            {
                if (!c.Renameable)
                    continue;
                GameCompetitionChange change;
                bool isPending = pending.TryGetValue(c.Id, out change);
                string name = isPending ? change.Name : c.Name;
                string shortName = isPending ? change.Short : c.Short;
                if (filter.Length > 0 && name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0 &&
                    c.OriginalName.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0)
                    continue;
                ListViewItem item = new ListViewItem(name);
                item.SubItems.Add(shortName);
                item.SubItems.Add(c.OriginalName == c.Name && !isPending ? "" : c.OriginalName);
                item.Tag = c;
                item.BackColor = isPending ? Color.LightYellow : (c.InSync ? Color.White : Color.MistyRose);
                listComps.Items.Add(item);
                if (selected != null && c.Id == selected.Id && c.National == selected.National)
                    item.Selected = true;
            }
            listComps.EndUpdate();
        }

        void ShowSelected()
        {
            GameCompetition c = Selected();
            bool any = c != null;
            textBoxName.Enabled = textBoxShort.Enabled = buttonSet.Enabled = any;
            buttonOriginal.Enabled = any && (c.OriginalName != c.Name || c.OriginalShort != c.Short || pending.ContainsKey(c.Id));
            if (!any)
            {
                buttonUndo.Enabled = false;
                labelInfo.Text = "";
                return;
            }
            GameCompetitionChange change;
            bool isPending = pending.TryGetValue(c.Id, out change);
            textBoxName.Text = isPending ? change.Name : c.Name;
            textBoxShort.Text = isPending ? change.Short : c.Short;
            buttonUndo.Enabled = isPending;
            labelInfo.Text = c.InSync
                ? "The name can be up to " + CompetitionRenamer.MaxName + " characters, the short name up to " + CompetitionRenamer.MaxShort + "."
                : "cm3.exe still has the old name \"" + c.ExeName + "\" (a rename stopped half way). Press Set, then Apply, to finish it.";
        }

        void SetSelected()
        {
            GameCompetition c = Selected();
            if (c == null)
                return;
            string name = textBoxName.Text.Trim();
            string shortName = textBoxShort.Text.Trim();
            string why = CompetitionRenamer.Problem(c, name, shortName, comps, pending);
            if (why != null)
            {
                MessageBox.Show(this, why, Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (name == c.Name && shortName == c.Short && c.InSync)
                pending.Remove(c.Id);
            else
            {
                GameCompetitionChange change = new GameCompetitionChange();
                change.Competition = c;
                change.Name = name;
                change.Short = shortName;
                pending[c.Id] = change;
            }
            FillComps();
            ShowSelected();
            UpdateEdits();
        }

        void UndoSelected()
        {
            GameCompetition c = Selected();
            if (c == null)
                return;
            pending.Remove(c.Id);
            FillComps();
            ShowSelected();
            UpdateEdits();
        }

        // Puts the game's own names in the boxes (press Set to keep them)
        void UseOriginalNames()
        {
            GameCompetition c = Selected();
            if (c == null)
                return;
            textBoxName.Text = c.OriginalName;
            textBoxShort.Text = c.OriginalShort;
        }

        void UpdateEdits()
        {
            labelEdits.Text = pending.Count == 0 ? "No names changed yet." : pending.Count + " competition" + (pending.Count == 1 ? "" : "s") + " to rename, not applied yet.";
            buttonClear.Enabled = pending.Count > 0;
        }

        // cm3.exe is normally in Program Files, which needs administrator rights to write to
        void OfferToRestartAsAdministrator()
        {
            if (MessageBox.Show(this, "CM3 Utils can't write to the game's folder without administrator rights (the game is usually in Program Files).\r\n\r\nRestart CM3 Utils as administrator now?",
                    Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            try
            {
                ProcessStartInfo start = new ProcessStartInfo(Application.ExecutablePath, "--competitions");
                start.Verb = "runas";
                start.UseShellExecute = true;
                Process.Start(start);
                Application.Exit();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // the user said no to the Windows prompt
            }
        }

        void ApplyChanges()
        {
            string file = textBoxExe.Text;
            if (!File.Exists(file))
            {
                MessageBox.Show(this, "Please select cm3.exe first.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string source = savePath();
            bool includeSave = checkBoxSave.Enabled && checkBoxSave.Checked && source.Length > 0 && File.Exists(source);
            if (pending.Count == 0 && !includeSave)
            {
                MessageBox.Show(this, "Choose a competition, change its name and press Set first.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (pending.Count > 0 && !includeSave &&
                MessageBox.Show(this, "No saved game is included. A saved game made with the old names will stop loading in the game once it is renamed (the game " +
                    "stops with \"index.cpp 1635\" or \"comp_stats.CPP 5382\"), unless you rename it too.\r\n\r\nRename the game only?", Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            string tempSave = null;
            try
            {
                if (!CM3ResolutionChanger.IsCM3Exe(file))
                {
                    MessageBox.Show(this, "This isn't a supported CM3 v3.04e cm3.exe.", Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                List<GameCompetitionChange> changes = new List<GameCompetitionChange>(pending.Values);

                // the saved game first, into a temporary file: if anything is wrong with it nothing has been changed yet
                string target = null;
                int savedRenamed = 0;
                if (includeSave)
                {
                    SaveGameFile save = SaveGameFile.Load(source);
                    Dictionary<int, CompetitionEdit> edits = new Dictionary<int, CompetitionEdit>();
                    foreach (GameCompetitionChange change in changes)
                    {
                        CompetitionEdit edit = new CompetitionEdit();
                        edit.Name = change.Name;
                        edit.ShortName = change.Short;
                        edits[change.Competition.Id] = edit;
                    }

                    // a saved game that doesn't use the game's names (for example one made before the game was renamed) is made to match
                    Dictionary<int, Competition> inSave = new Dictionary<int, Competition>();
                    foreach (Competition competition in save.Competitions())
                        inSave[competition.Id] = competition;
                    foreach (GameCompetition c in comps)
                    {
                        Competition there;
                        if (!c.Renameable || !c.InSync || edits.ContainsKey(c.Id) || !inSave.TryGetValue(c.Id, out there))
                            continue;
                        if (there.Name == c.Name && there.ShortName == c.Short)
                            continue;
                        CompetitionEdit edit = new CompetitionEdit();
                        edit.Name = c.Name;
                        edit.ShortName = c.Short;
                        edits[c.Id] = edit;
                    }
                    if (edits.Count == 0)
                    {
                        MessageBox.Show(this, pending.Count == 0 ? "The saved game already has the game's competition names." : "Nothing to change.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    Dictionary<SaveGameFile.Entry, byte[]> replaced = new Dictionary<SaveGameFile.Entry, byte[]>();
                    savedRenamed = save.ReplaceCompetitions(edits, replaced);

                    target = checkBoxOverwrite.Checked
                        ? source
                        : Path.Combine(Path.GetDirectoryName(source), Path.GetFileNameWithoutExtension(source) + "_renamed" + Path.GetExtension(source));
                    if (!checkBoxOverwrite.Checked && File.Exists(target) &&
                        MessageBox.Show(this, Path.GetFileName(target) + " already exists. Replace it?", Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                        return;

                    tempSave = target + ".new";
                    save.WriteFile(tempSave, replaced);

                    // read it back: every competition has to have its new name
                    SaveGameFile check = SaveGameFile.Load(tempSave);
                    Dictionary<int, Competition> now = new Dictionary<int, Competition>();
                    foreach (Competition competition in check.Competitions())
                        now[competition.Id] = competition;
                    foreach (KeyValuePair<int, CompetitionEdit> pair in edits)
                    {
                        Competition competition;
                        if (!now.TryGetValue(pair.Key, out competition) || !pair.Value.SameAs(competition) || !competition.HasFile)
                            throw new InvalidDataException("The renamed saved game didn't pass the check afterwards; nothing has been changed.");
                    }
                }

                // the game
                string report = "";
                bool backedUp = false;
                string backup = Path.Combine(Path.GetDirectoryName(file), "cm3_original.exe");
                if (changes.Count > 0)
                {
                    // keep a copy of the untouched exe the first time
                    if (checkBoxBackup.Checked && !File.Exists(backup))
                    {
                        File.Copy(file, backup);
                        File.SetAttributes(backup, FileAttributes.Normal);
                        CM3ResolutionChanger.Apply(backup, 800, 600, CM3ResolutionChanger.Smoothing.HighQuality, false, 0, 0, 0);
                        backedUp = true;
                    }
                    report = CompetitionRenamer.Apply(file, changes);
                }

                // the game is done: now the saved game
                string savedAs = null;
                if (target != null)
                {
                    if (checkBoxOverwrite.Checked)
                        File.Copy(source, source + ".bak", true);
                    if (File.Exists(target))
                        File.Delete(target);
                    File.Move(tempSave, target);
                    tempSave = null;
                    savedAs = target;
                }

                compsRead = "";
                LoadComps();

                string text = (changes.Count > 0 ? "Renamed " + changes.Count + " competition" + (changes.Count == 1 ? "" : "s") + " in the game.\r\n\r\n" + report : "") +
                              (savedAs != null ? "\r\n\r\n" + savedRenamed + " competition" + (savedRenamed == 1 ? "" : "s") + " renamed in the saved game, saved as " + Path.GetFileName(savedAs) + "." : "") +
                              (changes.Count > 0 ? "\r\n\r\nStart a new game for the game's own names to apply." : "");
                MessageBox.Show(this, text.Trim(), Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                if (backedUp)
                    MessageBox.Show(this, "A copy of the original game exe was saved as\r\n" + backup, Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                if (savedAs != null && SaveWritten != null)
                    SaveWritten(savedAs);
            }
            catch (UnauthorizedAccessException)
            {
                OfferToRestartAsAdministrator();
            }
            catch (IOException ex)
            {
                MessageBox.Show(this, "Couldn't write the file (is CM3 still running?):\r\n\r\n" + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (tempSave != null && File.Exists(tempSave))
                    File.Delete(tempSave);
            }
        }
    }
}
