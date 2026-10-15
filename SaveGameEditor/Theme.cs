using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CM3SaveGameEditor
{
    // The look of the editor, loosely based on the game: a red title bar, navy blue buttons and tabs with yellow writing
    public static class Theme
    {
        public static readonly Color Red = Color.FromArgb(200, 16, 24);
        public static readonly Color DarkRed = Color.FromArgb(150, 10, 16);
        public static readonly Color Navy = Color.FromArgb(0, 0, 128);
        public static readonly Color DarkNavy = Color.FromArgb(10, 14, 56);
        public static readonly Color LightNavy = Color.FromArgb(70, 80, 150);
        public static readonly Color Yellow = Color.FromArgb(255, 220, 40);
        public static readonly Color Page = Color.FromArgb(234, 237, 247);
        public static readonly Color Disabled = Color.FromArgb(190, 194, 210);

        public static Font Normal = new Font("Arial", 8.25f);
        public static Font Bold = new Font("Arial", 8.25f, FontStyle.Bold);

        // A navy button with yellow writing; red for the main action
        public static void StyleButton(Button button, bool main)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.FlatAppearance.BorderSize = 1;
            button.Cursor = Cursors.Hand;
            Color back = main ? Red : Navy;
            Action update = delegate
            {
                button.BackColor = button.Enabled ? back : Disabled;
                button.ForeColor = button.Enabled ? Yellow : Color.FromArgb(120, 124, 140);
                button.FlatAppearance.BorderColor = button.Enabled ? Yellow : Disabled;
                button.FlatAppearance.MouseOverBackColor = main ? DarkRed : LightNavy;
            };
            update();
            button.EnabledChanged += delegate { update(); };
        }

        // Navy tabs, the selected one lit up with a yellow edge
        public static void StyleTabs(TabControl tabs)
        {
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.SizeMode = TabSizeMode.Fixed;
            tabs.ItemSize = new Size(140, 30);
            tabs.Padding = new Point(12, 4);
            tabs.Font = Bold;
            tabs.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                bool selected = e.Index == tabs.SelectedIndex;
                Rectangle bounds = tabs.GetTabRect(e.Index);
                using (Brush back = new SolidBrush(selected ? Navy : LightNavy))
                    e.Graphics.FillRectangle(back, bounds);
                if (selected)
                    using (Brush edge = new SolidBrush(Yellow))
                        e.Graphics.FillRectangle(edge, bounds.Left, bounds.Bottom - 4, bounds.Width, 4);
                TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, bounds, selected ? Yellow : Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
        }

        // Gives the controls inside a container the look: pages, group boxes, buttons
        public static void Apply(Control root)
        {
            foreach (Control control in root.Controls)
            {
                if (control is Button && !(control is Tile) && control.Tag as string != "styled")
                {
                    StyleButton((Button)control, false);
                    control.Tag = "styled";
                }
                else if (control is TabPage)
                {
                    control.BackColor = Page;
                    control.Font = Normal;
                }
                else if (control is GroupBox)
                    control.ForeColor = Navy;
                else if (control is ListView)
                {
                    ListView list = (ListView)control;
                    list.BackColor = Color.White;
                    list.BorderStyle = BorderStyle.FixedSingle;
                }
                if (control is GroupBox)
                    foreach (Control inner in control.Controls)
                        if (!(inner is Button) && !(inner is ListView) && inner.Tag as string != "keep")
                            inner.ForeColor = Color.FromArgb(30, 30, 60);
                Apply(control);
            }
        }
    }

    // The big "Editing" / "Scouting" choices of the first screen
    public class Tile : Button
    {
        public string Title = "";
        bool hot;

        public Tile()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (LinearGradientBrush back = new LinearGradientBrush(bounds, hot ? Theme.LightNavy : Theme.Navy, Theme.DarkNavy, 90f))
                g.FillRectangle(back, bounds);
            using (Brush bar = new SolidBrush(Theme.Red))
                g.FillRectangle(bar, 0, 0, Width, 10);
            using (Pen border = new Pen(hot ? Theme.Yellow : Theme.LightNavy, hot ? 3 : 1))
                g.DrawRectangle(border, bounds);

            using (Font titleFont = new Font("Arial", 26f, FontStyle.Bold))
                TextRenderer.DrawText(g, Title, titleFont, new Rectangle(20, 10, Width - 40, Height - 10), Theme.Yellow, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }
}
