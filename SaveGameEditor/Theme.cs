using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace CM3SaveGameEditor
{
    // The look of the app, taken from the box of the game: sky and royal blue, gold lettering with a navy outline, white,
    // and a touch of red from the badge on the ball. Soft white cards with rounded corners on a light blue background, a blue
    // sidebar, gold for the main actions.
    public static class Theme
    {
        // ---- the colours
        public static readonly Color Sky = Color.FromArgb(120, 190, 235);
        public static readonly Color Blue = Color.FromArgb(30, 95, 170);
        public static readonly Color DeepBlue = Color.FromArgb(20, 62, 128);
        public static readonly Color Navy = Color.FromArgb(22, 48, 100);          // writing, outlines
        public static readonly Color DarkNavy = Color.FromArgb(14, 36, 82);       // dark panels
        public static readonly Color LightNavy = Color.FromArgb(62, 110, 180);
        public static readonly Color Gold = Color.FromArgb(246, 193, 30);
        public static readonly Color DarkGold = Color.FromArgb(214, 152, 0);
        public static readonly Color Yellow = Gold;
        public static readonly Color Red = Color.FromArgb(200, 36, 46);
        public static readonly Color DarkRed = Color.FromArgb(150, 20, 30);
        public static readonly Color Background = Color.FromArgb(226, 239, 250);  // behind the cards
        public static readonly Color Page = Color.FromArgb(244, 248, 253);        // the pages of the tabs
        public static readonly Color Surface = Color.White;                       // cards
        public static readonly Color Border = Color.FromArgb(214, 226, 240);
        public static readonly Color Text = Color.FromArgb(24, 40, 72);
        public static readonly Color Muted = Color.FromArgb(104, 124, 156);
        public static readonly Color Disabled = Color.FromArgb(196, 206, 222);
        public static readonly Color Selection = Color.FromArgb(214, 232, 250);
        public static readonly Color Zebra = Color.FromArgb(247, 250, 254);

        public static Font Normal = new Font("Segoe UI", 9f);
        public static Font Bold = new Font("Segoe UI", 9f, FontStyle.Bold);
        public static Font Small = new Font("Segoe UI", 8f);
        public static Font Heading = new Font("Segoe UI", 12f, FontStyle.Bold);
        public static Font Title = new Font("Segoe UI", 22f, FontStyle.Bold);

        // ---- drawing helpers
        // pixels at 96 dpi -> pixels on this screen (the painted layouts scale with the display like the controls do)
        public static int Px(Graphics g, int value)
        {
            return (int)Math.Round(value * g.DpiX / 96f);
        }

        public static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // A soft shadow under a rounded rectangle: outlines getting fainter the further out they are
        public static void Shadow(Graphics g, Rectangle r, int radius, int size, int alpha)
        {
            for (int i = size; i >= 1; i--)
            {
                double fade = 1.0 - (double)i / (size + 1);
                int a = (int)(alpha * fade * fade);
                if (a <= 0)
                    continue;
                Rectangle outer = new Rectangle(r.Left - i, r.Top - i + size / 3, r.Width + 2 * i, r.Height + 2 * i);
                using (GraphicsPath path = RoundedRect(outer, radius + i))
                using (Pen pen = new Pen(Color.FromArgb(a, 30, 70, 130), 1.5f))
                    g.DrawPath(pen, path);
            }
        }

        public static void FillRounded(Graphics g, Rectangle r, int radius, Color color)
        {
            using (GraphicsPath path = RoundedRect(r, radius))
            using (Brush brush = new SolidBrush(color))
                g.FillPath(brush, path);
        }

        public static void DrawParentBackground(Graphics g, Control control)
        {
            if (control.Parent == null)
                return;
            ButtonRenderer.DrawParentBackground(g, control.ClientRectangle, control);
        }

        // Simple line icons: "home", "patcher", "editing", "scouting", "exit"
        public static void DrawIcon(Graphics g, string kind, Rectangle r, Color color, float width)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(color, width))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f;
                float h = Math.Min(r.Width, r.Height) / 2f - 1f;
                switch (kind)
                {
                    case "home":
                        g.DrawPolygon(pen, new PointF[] {
                            new PointF(cx - h, cy), new PointF(cx, cy - h), new PointF(cx + h, cy),
                            new PointF(cx + h * 0.7f, cy), new PointF(cx + h * 0.7f, cy + h), new PointF(cx - h * 0.7f, cy + h), new PointF(cx - h * 0.7f, cy) });
                        g.DrawLine(pen, cx, cy + h, cx, cy + h * 0.3f);
                        break;
                    case "patcher":
                        g.DrawEllipse(pen, cx - h * 0.55f, cy - h * 0.55f, h * 1.1f, h * 1.1f);
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4;
                            g.DrawLine(pen, cx + (float)Math.Cos(a) * h * 0.55f, cy + (float)Math.Sin(a) * h * 0.55f,
                                cx + (float)Math.Cos(a) * h, cy + (float)Math.Sin(a) * h);
                        }
                        break;
                    case "editing":
                        g.DrawLine(pen, cx - h * 0.9f, cy + h * 0.9f, cx + h * 0.55f, cy - h * 0.55f);
                        g.DrawLine(pen, cx + h * 0.55f, cy - h * 0.55f, cx + h * 0.95f, cy - h * 0.15f);
                        g.DrawLine(pen, cx + h * 0.95f, cy - h * 0.15f, cx - h * 0.45f, cy + h * 0.95f);
                        g.DrawLine(pen, cx - h * 0.45f, cy + h * 0.95f, cx - h * 0.9f, cy + h * 0.9f);
                        break;
                    case "scouting":
                        g.DrawEllipse(pen, cx - h, cy - h, h * 1.4f, h * 1.4f);
                        g.DrawLine(pen, cx + h * 0.2f, cy + h * 0.2f, cx + h, cy + h);
                        break;
                    case "exit":
                        g.DrawLine(pen, cx - h * 0.1f, cy - h, cx - h, cy - h);
                        g.DrawLine(pen, cx - h, cy - h, cx - h, cy + h);
                        g.DrawLine(pen, cx - h, cy + h, cx - h * 0.1f, cy + h);
                        g.DrawLine(pen, cx - h * 0.3f, cy, cx + h, cy);
                        g.DrawLine(pen, cx + h * 0.45f, cy - h * 0.5f, cx + h, cy);
                        g.DrawLine(pen, cx + h * 0.45f, cy + h * 0.5f, cx + h, cy);
                        break;
                    case "heart":
                        {
                            GraphicsPath heart = new GraphicsPath();
                            heart.AddBezier(cx, cy + h, cx - h * 1.6f, cy - h * 0.2f, cx - h * 0.7f, cy - h * 1.3f, cx, cy - h * 0.4f);
                            heart.AddBezier(cx, cy - h * 0.4f, cx + h * 0.7f, cy - h * 1.3f, cx + h * 1.6f, cy - h * 0.2f, cx, cy + h);
                            g.DrawPath(pen, heart);
                            heart.Dispose();
                        }
                        break;
                    case "menu":
                        g.DrawLine(pen, cx - h, cy - h * 0.7f, cx + h, cy - h * 0.7f);
                        g.DrawLine(pen, cx - h, cy, cx + h, cy);
                        g.DrawLine(pen, cx - h, cy + h * 0.7f, cx + h, cy + h * 0.7f);
                        break;
                    case "search":
                        g.DrawEllipse(pen, cx - h, cy - h, h * 1.4f, h * 1.4f);
                        g.DrawLine(pen, cx + h * 0.2f, cy + h * 0.2f, cx + h, cy + h);
                        break;
                }
            }
            g.SmoothingMode = old;
        }

        // The football of the box, as a small logo
        public static void DrawBall(Graphics g, Rectangle r)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle ball = new Rectangle(r.Left + 1, r.Top + 1, r.Width - 3, r.Height - 3);
            using (Brush white = new SolidBrush(Color.White))
                g.FillEllipse(white, ball);
            float cx = ball.Left + ball.Width / 2f, cy = ball.Top + ball.Height / 2f, rad = ball.Width / 2f;
            PointF[] pent = new PointF[5];
            PointF[] outer = new PointF[5];
            for (int i = 0; i < 5; i++)
            {
                double a = -Math.PI / 2 + i * 2 * Math.PI / 5;
                pent[i] = new PointF(cx + (float)Math.Cos(a) * rad * 0.36f, cy + (float)Math.Sin(a) * rad * 0.36f);
                outer[i] = new PointF(cx + (float)Math.Cos(a) * rad * 0.82f, cy + (float)Math.Sin(a) * rad * 0.82f);
            }
            using (Brush navy = new SolidBrush(DeepBlue))
                g.FillPolygon(navy, pent);
            using (Pen seam = new Pen(DeepBlue, 1.5f))
            {
                for (int i = 0; i < 5; i++)
                    g.DrawLine(seam, pent[i], outer[i]);
            }
            using (Pen edge = new Pen(Navy, 1.5f))
                g.DrawEllipse(edge, ball);
            g.SmoothingMode = old;
        }

        // The football as the window icon
        public static Icon BallIcon()
        {
            using (Bitmap bitmap = new Bitmap(48, 48))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.Transparent);
                    DrawBall(g, new Rectangle(0, 0, 48, 48));
                }
                IntPtr handle = bitmap.GetHicon();
                Icon icon = (Icon)Icon.FromHandle(handle).Clone();
                NativeMethods.DestroyIcon(handle);
                return icon;
            }
        }

        // ---- the controls
        // A button: gold for the main action, blue otherwise (the old call, kept for the code that styles its own buttons)
        public static void StyleButton(Button button, bool main)
        {
            ModernButton modern = button as ModernButton;
            if (modern != null)
            {
                modern.Kind = main ? ButtonKind.Primary : ButtonKind.Secondary;
                modern.Invalidate();
                return;
            }
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.BackColor = main ? Gold : Blue;
            button.ForeColor = main ? Navy : Color.White;
        }

        // Plain tab controls get the flat look too (the app uses ModernTabs, which draws itself)
        public static void StyleTabs(TabControl tabs)
        {
            if (tabs is ModernTabs)
                return;
            tabs.Font = Bold;
        }

        static readonly Dictionary<ListView, bool> styledLists = new Dictionary<ListView, bool>();

        // A list: no grid, quiet column headings, every other row tinted, the selected row in light blue
        public static void StyleList(ListView list)
        {
            if (styledLists.ContainsKey(list))
                return;
            styledLists[list] = true;

            list.GridLines = false;
            list.FullRowSelect = true;
            list.BorderStyle = BorderStyle.None;
            list.BackColor = Color.White;
            list.ForeColor = Text;
            list.HideSelection = false;
            PropertyInfo buffered = typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            if (buffered != null)
                buffered.SetValue(list, true, null);
            list.OwnerDraw = true;
            list.Resize += delegate { FillWidth(list); };
            list.HandleCreated += delegate { FillWidth(list); };

            list.DrawColumnHeader += delegate(object sender, DrawListViewColumnHeaderEventArgs e)
            {
                using (Brush back = new SolidBrush(Page))
                    e.Graphics.FillRectangle(back, e.Bounds);
                using (Pen line = new Pen(Border))
                    e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | Alignment(e.Header.TextAlign);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, Bold, new Rectangle(e.Bounds.Left + 8, e.Bounds.Top, e.Bounds.Width - 16, e.Bounds.Height), Muted, flags);
            };
            list.DrawItem += delegate(object sender, DrawListViewItemEventArgs e) { };
            list.DrawSubItem += delegate(object sender, DrawListViewSubItemEventArgs e)
            {
                bool selected = e.Item.Selected;
                Color back = e.ItemIndex % 2 == 1 ? Zebra : Color.White;
                if (e.Item.BackColor.ToArgb() != list.BackColor.ToArgb() && e.Item.BackColor.ToArgb() != SystemColors.Window.ToArgb())
                    back = e.Item.BackColor;
                if (selected)
                    back = Selection;
                using (Brush brush = new SolidBrush(back))
                    e.Graphics.FillRectangle(brush, e.Bounds);

                Rectangle text = new Rectangle(e.Bounds.Left + 8, e.Bounds.Top, e.Bounds.Width - 12, e.Bounds.Height);
                if (e.ColumnIndex == 0)
                {
                    if (selected)
                        using (Brush bar = new SolidBrush(Blue))
                            e.Graphics.FillRectangle(bar, e.Bounds.Left, e.Bounds.Top, 3, e.Bounds.Height);
                    if (list.CheckBoxes)
                    {
                        Point at = new Point(e.Bounds.Left + 6, e.Bounds.Top + (e.Bounds.Height - 13) / 2);
                        CheckBoxRenderer.DrawCheckBox(e.Graphics, at, e.Item.Checked ? System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal : System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal);
                        text = new Rectangle(e.Bounds.Left + 26, e.Bounds.Top, e.Bounds.Width - 30, e.Bounds.Height);
                    }
                }
                Color color = e.SubItem.ForeColor.ToArgb() == list.ForeColor.ToArgb() || e.SubItem.ForeColor.ToArgb() == SystemColors.WindowText.ToArgb() ? Text : e.SubItem.ForeColor;
                Font font = e.SubItem.Font ?? list.Font;
                TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | Alignment(e.Header.TextAlign);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, font, text, color, flags);
            };
        }

        // The last column takes the room that is left (it never gets narrower than it was)
        static void FillWidth(ListView list)
        {
            if (list.Columns.Count == 0 || list.View != View.Details)
                return;
            int used = 0;
            for (int i = 0; i < list.Columns.Count - 1; i++)
                used += list.Columns[i].Width;
            int room = list.ClientSize.Width - used;
            ColumnHeader last = list.Columns[list.Columns.Count - 1];
            if (room > last.Width)
                last.Width = room;
        }

        static TextFormatFlags Alignment(HorizontalAlignment align)
        {
            if (align == HorizontalAlignment.Right)
                return TextFormatFlags.Right;
            if (align == HorizontalAlignment.Center)
                return TextFormatFlags.HorizontalCenter;
            return TextFormatFlags.Left;
        }

        // Gives the controls inside a container the look: pages, buttons, lists, writing, text boxes
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
                else if (control is ListView)
                    StyleList((ListView)control);
                else if (control is TextBox && control.Tag as string != "keep")
                {
                    ((TextBox)control).BorderStyle = BorderStyle.FixedSingle;
                    control.ForeColor = Text;
                }
                else if (control is NumericUpDown)
                    ((NumericUpDown)control).BorderStyle = BorderStyle.FixedSingle;
                else if ((control is Label || control is CheckBox || control is RadioButton) && control.ForeColor.ToArgb() == SystemColors.ControlText.ToArgb())
                    control.ForeColor = Text;
                Apply(control);
            }
        }
    }

    static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr handle);
    }

    public enum ButtonKind { Primary, Secondary, Ghost }

    // A rounded button: gold (Primary), blue (Secondary) or white with an outline (Ghost). A Swatch shows its BackColor
    // (the colour pickers).
    public class ModernButton : Button
    {
        public ButtonKind Kind = ButtonKind.Secondary;
        public bool Swatch;
        bool hot, down;

        public ModernButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            Cursor = Cursors.Hand;
            Font = Theme.Bold;
            Tag = "styled";
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.DrawParentBackground(g, this);

            Rectangle r = new Rectangle(1, 1, Width - 3, Height - 3);
            int radius = Swatch ? Theme.Px(g, 6) : Math.Max(Theme.Px(g, 4), Math.Min(r.Height / 2, Theme.Px(g, 18)));
            Color top, bottom, border, text;

            if (!Enabled)
            {
                top = bottom = Color.FromArgb(232, 238, 246);
                border = Color.FromArgb(220, 228, 240);
                text = Color.FromArgb(150, 162, 184);
            }
            else if (Swatch)
            {
                top = bottom = BackColor;
                border = hot ? Theme.Blue : Color.FromArgb(170, 186, 210);
                text = Color.White;
            }
            else if (Kind == ButtonKind.Primary)
            {
                top = down ? Theme.DarkGold : (hot ? Color.FromArgb(255, 210, 70) : Color.FromArgb(250, 202, 50));
                bottom = down ? Theme.DarkGold : (hot ? Color.FromArgb(240, 180, 20) : Theme.Gold);
                border = Theme.DarkGold;
                text = Theme.Navy;
            }
            else if (Kind == ButtonKind.Ghost)
            {
                top = bottom = down ? Theme.Selection : (hot ? Color.FromArgb(240, 246, 253) : Color.White);
                border = hot ? Theme.Blue : Theme.Border;
                text = Theme.Blue;
            }
            else
            {
                top = down ? Theme.DeepBlue : (hot ? Color.FromArgb(60, 130, 205) : Color.FromArgb(46, 112, 190));
                bottom = down ? Theme.Navy : (hot ? Theme.Blue : Theme.DeepBlue);
                border = Theme.DeepBlue;
                text = Color.White;
            }

            using (GraphicsPath path = Theme.RoundedRect(r, radius))
            {
                if (Enabled && !Swatch && r.Height > 2)
                {
                    using (LinearGradientBrush brush = new LinearGradientBrush(r, top, bottom, 90f))
                        g.FillPath(brush, path);
                }
                else
                    using (Brush brush = new SolidBrush(top))
                        g.FillPath(brush, path);
                using (Pen pen = new Pen(border))
                    g.DrawPath(pen, path);
            }

            if (!Swatch)
                TextRenderer.DrawText(g, Text, Font, new Rectangle(4, 0, Width - 8, Height - 1), text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    // A group of controls on a white rounded card with a title
    public class CardBox : GroupBox
    {
        public CardBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Parent != null)
                using (Brush back = new SolidBrush(Parent.BackColor))
                    g.FillRectangle(back, ClientRectangle);

            Rectangle r = new Rectangle(0, Theme.Px(g, 2), Width - 1, Height - 2 * Theme.Px(g, 2));
            using (GraphicsPath path = Theme.RoundedRect(r, Theme.Px(g, 12)))
            {
                using (Brush fill = new SolidBrush(Color.White))
                    g.FillPath(fill, path);
                using (Pen border = new Pen(Theme.Border))
                    g.DrawPath(border, path);
            }
            if (Text.Length > 0)
            {
                Color color = ForeColor.ToArgb() == SystemColors.ControlText.ToArgb() ? Theme.Blue : ForeColor;
                using (Font caption = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                TextRenderer.DrawText(g, Text.ToUpperInvariant(), caption, new Rectangle(Theme.Px(g, 14), Theme.Px(g, 5), Width - 2 * Theme.Px(g, 14), Theme.Px(g, 14)), color,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }

    // Tabs as plain text with a gold line under the open one (drawn by the control itself)
    public class ModernTabs : TabControl
    {
        int hover = -1;

        public ModernTabs()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SizeMode = TabSizeMode.Fixed;
            ItemSize = new Size(130, 38);
            Padding = new Point(12, 4);
            Font = Theme.Bold;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int found = -1;
            for (int i = 0; i < TabPages.Count; i++)
                if (GetTabRect(i).Contains(e.Location))
                    found = i;
            if (found != hover)
            {
                hover = found;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Brush back = new SolidBrush(Theme.Page))
                g.FillRectangle(back, ClientRectangle);

            int strip = ItemSize.Height + 2;
            using (Brush white = new SolidBrush(Color.White))
                g.FillRectangle(white, 0, 0, Width, strip);
            using (Pen line = new Pen(Theme.Border))
                g.DrawLine(line, 0, strip, Width, strip);

            for (int i = 0; i < TabPages.Count; i++)
            {
                Rectangle bounds = GetTabRect(i);
                bool selected = i == SelectedIndex;
                Color color = selected ? Theme.Blue : (i == hover ? Theme.Navy : Theme.Muted);
                TextRenderer.DrawText(g, TabPages[i].Text, Font, new Rectangle(bounds.Left, bounds.Top, bounds.Width, bounds.Height),
                    color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if (selected)
                    using (GraphicsPath bar = Theme.RoundedRect(new Rectangle(bounds.Left + Theme.Px(g, 14), strip - Theme.Px(g, 3), bounds.Width - 2 * Theme.Px(g, 14), Theme.Px(g, 4)), Theme.Px(g, 2)))
                    using (Brush gold = new SolidBrush(Theme.Gold))
                        g.FillPath(gold, bar);
            }
        }
    }

    // A panel painted with a gradient (and a few soft circles for depth)
    public class GradientPanel : Panel
    {
        public Color From = Theme.Background;
        public Color To = Theme.Background;
        public float Angle = 90f;
        public bool Circles;

        public GradientPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (Width <= 0 || Height <= 0)
                return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width, Height);
            using (LinearGradientBrush brush = new LinearGradientBrush(r, From, To, Angle))
                g.FillRectangle(brush, r);
            if (Circles)
            {
                using (Brush soft = new SolidBrush(Color.FromArgb(22, 255, 255, 255)))
                {
                    g.FillEllipse(soft, Width * 0.45f, -Height * 0.25f, Width * 1.1f, Width * 1.1f);
                    g.FillEllipse(soft, -Width * 0.5f, Height * 0.62f, Width * 1.2f, Width * 1.2f);
                }
            }
        }

    }

    // A rounded panel (the search box, the footer); with a shadow it is a card
    public class RoundPanel : Panel
    {
        public Color Fill = Color.White;
        public Color Outline = Theme.Border;
        public int Radius = 14;
        public int ShadowSize;
        public int ShadowAlpha = 34;

        public RoundPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int shadow = Theme.Px(g, ShadowSize);
            int radius = Theme.Px(g, Radius);
            Rectangle r = new Rectangle(shadow, shadow, Width - 1 - 2 * shadow, Height - 1 - 2 * shadow);
            if (shadow > 0)
                Theme.Shadow(g, r, radius, shadow, ShadowAlpha);
            using (GraphicsPath path = Theme.RoundedRect(r, radius))
            {
                using (Brush brush = new SolidBrush(Fill))
                    g.FillPath(brush, path);
                if (Outline.A > 0)
                    using (Pen pen = new Pen(Outline))
                        g.DrawPath(pen, path);
            }
            base.OnPaint(e);
        }
    }

    // An item of the sidebar: an icon and a name, lit up when it is the open part
    public class NavItem : Control
    {
        public string Icon = "home";
        public bool Active;
        public bool Compact;        // only the icon
        bool hot;

        public NavItem()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Height = 44;
            Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

        public void SetActive(bool active)
        {
            Active = active;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int k10 = Theme.Px(g, 10);
            Rectangle r = new Rectangle(k10, Theme.Px(g, 2), Width - 2 * k10, Height - Theme.Px(g, 5));
            if (Active || hot)
                Theme.FillRounded(g, r, Theme.Px(g, 12), Color.FromArgb(Active ? 52 : 26, 255, 255, 255));
            if (Active)
                Theme.FillRounded(g, new Rectangle(Theme.Px(g, 4), Theme.Px(g, 10), Theme.Px(g, 4), Height - Theme.Px(g, 22)), Theme.Px(g, 2), Theme.Gold);
            Color color = Active ? Color.White : Color.FromArgb(225, 236, 250);
            int icon = Theme.Px(g, 20);
            int iconLeft = Compact ? (Width - icon) / 2 : Theme.Px(g, 26);
            Theme.DrawIcon(g, Icon, new Rectangle(iconLeft, (Height - icon) / 2, icon, icon), Active ? Theme.Gold : color, 1.8f * g.DpiX / 96f);
            if (!Compact)
                TextRenderer.DrawText(g, Text, Font, new Rectangle(Theme.Px(g, 58), 0, Width - Theme.Px(g, 66), Height - 1), color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    // The big choices of the first screen
    public class Tile : Button
    {
        public string Title = "";
        public string Description = "";
        public string Icon = "home";
        public Color Accent = Theme.Blue;
        public Color IconColor = Color.White;
        bool hot;

        public Tile()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.DrawParentBackground(g, this);

            int pad = Theme.Px(g, 14);
            Rectangle card = new Rectangle(pad, pad - (hot ? Theme.Px(g, 4) : 0), Width - 2 * pad - 1, Height - 2 * pad - 1);
            Theme.Shadow(g, card, Theme.Px(g, 18), Theme.Px(g, hot ? 14 : 10), hot ? 60 : 38);
            using (GraphicsPath path = Theme.RoundedRect(card, Theme.Px(g, 18)))
            {
                using (Brush fill = new SolidBrush(Color.White))
                    g.FillPath(fill, path);
                using (Pen border = new Pen(hot ? Theme.Blue : Theme.Border, hot ? 2f : 1f))
                    g.DrawPath(border, path);
            }

            int margin = Theme.Px(g, 24);
            int size = Theme.Px(g, 58);
            Rectangle bubble = new Rectangle(card.Left + margin, card.Top + margin, size, size);
            using (LinearGradientBrush brush = new LinearGradientBrush(bubble, Accent, ControlPaint.Dark(Accent, 0.15f), 45f))
                g.FillEllipse(brush, bubble);
            int inner = Theme.Px(g, 28);
            Theme.DrawIcon(g, Icon, new Rectangle(bubble.Left + (size - inner) / 2, bubble.Top + (size - inner) / 2, inner, inner), IconColor, 2.4f * g.DpiX / 96f);

            using (Font titleFont = new Font("Segoe UI", 15f, FontStyle.Bold))
                TextRenderer.DrawText(g, Title, titleFont, new Rectangle(card.Left + margin, card.Top + Theme.Px(g, 96), card.Width - 2 * margin, Theme.Px(g, 34)), Theme.Navy, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, Description, Theme.Normal, new Rectangle(card.Left + margin, card.Top + Theme.Px(g, 134), card.Width - 2 * margin, card.Height - Theme.Px(g, 184)), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "Open  \u2192", Theme.Bold, new Rectangle(card.Left + margin, card.Bottom - Theme.Px(g, 42), card.Width - 2 * margin, Theme.Px(g, 26)), Theme.Blue, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}
