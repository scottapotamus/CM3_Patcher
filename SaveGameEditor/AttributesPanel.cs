using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CM3SaveGameEditor
{
    // One number of a player or a member of staff, drawn the way the game draws an attribute: its name and a red or green
    // bar, with a box to type the number in
    public class AttributeRow : Control
    {
        const int NameWidth = 118;
        const int BoxWidth = 52;

        public int Field;
        public readonly int Min, Max;
        readonly string title;
        readonly NumericUpDown box;
        bool edited;
        bool suppress;

        public event EventHandler ValueChanged;

        public AttributeRow(int field, string title, int min, int max)
        {
            Field = field;
            Min = min;
            Max = max;
            this.title = title;
            Height = 24;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            box = new NumericUpDown { Minimum = min, Maximum = max, Width = BoxWidth, TextAlign = HorizontalAlignment.Right };
            box.ValueChanged += delegate
            {
                Invalidate();
                if (!suppress && ValueChanged != null)
                    ValueChanged(this, EventArgs.Empty);
            };
            Controls.Add(box);
            Resize += delegate { box.Left = Width - BoxWidth; box.Top = (Height - box.Height) / 2; };
        }

        // Setting it doesn't raise the event
        public int Value
        {
            get { return (int)box.Value; }
            set
            {
                suppress = true;
                box.Value = Math.Max(box.Minimum, Math.Min(box.Maximum, value));
                suppress = false;
                Invalidate();
            }
        }

        public bool Edited
        {
            get { return edited; }
            set { edited = value; Invalidate(); }
        }

        public new bool Enabled
        {
            get { return box.Enabled; }
            set { box.Enabled = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Rectangle bar = new Rectangle(NameWidth, 3, Width - NameWidth - BoxWidth - 6, Height - 6);
            using (Brush back = new SolidBrush(Color.FromArgb(34, 38, 84)))
                g.FillRectangle(back, bar);

            double ratio = Max == Min ? 0 : (double)(Value - Min) / (Max - Min);
            Color fill = !Enabled ? Color.FromArgb(90, 94, 120) : ratio >= 0.7 ? Color.FromArgb(205, 40, 44) : ratio >= 0.2 ? Color.FromArgb(40, 150, 56) : Color.FromArgb(110, 112, 130);
            using (Brush brush = new SolidBrush(fill))
                g.FillRectangle(brush, bar.Left, bar.Top, (int)(bar.Width * Math.Max(0.04, ratio)), bar.Height);
            using (Pen border = new Pen(Color.FromArgb(80, 86, 150)))
                g.DrawRectangle(border, bar);

            TextRenderer.DrawText(g, title, Font, new Rectangle(0, 0, NameWidth, Height), edited ? Theme.Yellow : (Enabled ? Color.White : Color.FromArgb(150, 154, 180)),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
