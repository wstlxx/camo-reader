using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace CamoReader
{
    public partial class MainForm : Form
    {
        #region WinAPI Imports
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

        // --- MODIFIERS ---
        private const uint MOD_NONE = 0x0000;
        private const uint MOD_CONTROL = 0x0002;

        // --- HOTKEY IDs ---
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_F1_HIDE = 1;
        private const int HOTKEY_F3_PREV = 2;
        private const int HOTKEY_F4_NEXT = 3;
        private const int HOTKEY_F5_OPACITY_DOWN = 4;
        private const int HOTKEY_F6_OPACITY_UP = 5;
        private const int HOTKEY_F2_TELEPORT = 6;
        private const int HOTKEY_CTRL_F1_SHOW = 7;

        // --- VIRTUAL KEY CODES ---
        private const uint VK_F1 = 0x70;
        private const uint VK_F2 = 0x71;
        private const uint VK_F3 = 0x72;
        private const uint VK_F4 = 0x73;
        private const uint VK_F5 = 0x74;
        private const uint VK_F6 = 0x75;
        
        // --- WINDOWS STYLES ---
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;
        #endregion

        #region Fields
        private NotifyIcon trayIcon = null!;
        private ConfigManager config = null!;
        private List<string> textPages = null!;
        private int currentPage = 0;
        private Font textFont = null!;
        private System.Windows.Forms.Timer refreshTimer = null!;
        #endregion

        public MainForm()
        {
            InitializeComponent();
            LoadConfiguration();
            SetupWindow();
            SetupTrayIcon();
            RegisterHotkeys();
            LoadAndPaginateText();
            SetupRefreshTimer();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.AutoScaleDimensions = new SizeF(6F, 13F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.BackColor = Color.Magenta;
            this.ClientSize = new Size(800, 200);
            this.FormBorderStyle = FormBorderStyle.None;
            this.Name = "MainForm";
            this.Text = "Camo-Reader";
            this.TopMost = true;
            this.ResumeLayout(false);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT;
                return cp;
            }
        }

        private void LoadConfiguration()
        {
            config = new ConfigManager("config.ini");
            
            this.Location = new Point(config.WindowPosX, config.WindowPosY);
            this.Size = new Size(config.WindowWidth, config.WindowHeight);
            
            // Use Magenta as transparency key
            this.TransparencyKey = Color.Magenta;
            this.Opacity = 1.0; // Keep window fully opaque, we'll handle transparency in drawing
            
            textFont = new Font("Arial", config.TextSize, FontStyle.Bold);
        }

        private void SetupWindow()
        {
            this.DoubleBuffered = true;
            this.Paint += MainForm_Paint;
        }

        private void SetupRefreshTimer()
        {
            refreshTimer = new System.Windows.Forms.Timer();
            refreshTimer.Interval = 100; // Refresh every 100ms to adapt to background
            refreshTimer.Tick += (s, e) => this.Invalidate();
            refreshTimer.Start();
        }

        private void SetupTrayIcon()
        {
            Bitmap bmp = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                using (Pen pen = new Pen(Color.White, 3))
                {
                    g.DrawArc(pen, 8, 8, 16, 16, 45, 270);
                }
            }
            Icon icon = Icon.FromHandle(bmp.GetHicon());

            trayIcon = new NotifyIcon
            {
                Icon = icon,
                Visible = true,
                Text = "Camo-Reader"
            };

            trayIcon.Click += (s, e) =>
            {
                if (((MouseEventArgs)e).Button == MouseButtons.Left)
                {
                    ToggleVisibility();
                }
            };

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Show/Hide", null, (s, e) => ToggleVisibility());
            menu.Items.Add("Exit", null, (s, e) => Application.Exit());
            trayIcon.ContextMenuStrip = menu;
        }

        private void RegisterHotkeys()
        {
            RegisterHotKey(this.Handle, HOTKEY_F1_HIDE, MOD_NONE, VK_F1);
            RegisterHotKey(this.Handle, HOTKEY_F3_PREV, MOD_NONE, VK_F3);
            RegisterHotKey(this.Handle, HOTKEY_F4_NEXT, MOD_NONE, VK_F4);
            RegisterHotKey(this.Handle, HOTKEY_F5_OPACITY_DOWN, MOD_NONE, VK_F5);
            RegisterHotKey(this.Handle, HOTKEY_F6_OPACITY_UP, MOD_NONE, VK_F6);
            RegisterHotKey(this.Handle, HOTKEY_F2_TELEPORT, MOD_NONE, VK_F2);
            RegisterHotKey(this.Handle, HOTKEY_CTRL_F1_SHOW, MOD_CONTROL, VK_F1);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                switch (id)
                {
                    case HOTKEY_F1_HIDE:
                        this.Hide();
                        break;
                    case HOTKEY_CTRL_F1_SHOW:
                        this.Show();
                        break;
                    case HOTKEY_F2_TELEPORT:
                        TeleportToCursor();
                        break;
                    case HOTKEY_F3_PREV:
                        PreviousPage();
                        break;
                    case HOTKEY_F4_NEXT:
                        NextPage();
                        break;
                    case HOTKEY_F5_OPACITY_DOWN:
                        DecreaseShiftRatio();
                        break;
                    case HOTKEY_F6_OPACITY_UP:
                        IncreaseShiftRatio();
                        break;
                }
            }
        }

        private void LoadAndPaginateText()
        {
            textPages = new List<string>();

            if (config == null || !File.Exists(config.TextFilePath))
            {
                textPages.Add("Text file not found.\nCheck config.ini");
                return;
            }

            try
            {
                string fullText = File.ReadAllText(config.TextFilePath);
                textPages = PaginateText(fullText);
            }
            catch (Exception ex)
            {
                textPages.Add($"Error loading text:\n{ex.Message}");
            }
        }

        private List<string> PaginateText(string text)
        {
            List<string> pages = new List<string>();
            using (Graphics g = this.CreateGraphics())
            {
                SizeF maxSize = new SizeF(this.Width - 40, this.Height - 40);
                string[] words = text.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                
                string currentPage = "";
                foreach (string word in words)
                {
                    string testPage = currentPage + (currentPage.Length > 0 ? " " : "") + word;
                    SizeF size = g.MeasureString(testPage, textFont, (int)maxSize.Width);
                    
                    if (size.Height > maxSize.Height && currentPage.Length > 0)
                    {
                        pages.Add(currentPage);
                        currentPage = word;
                    }
                    else
                    {
                        currentPage = testPage;
                    }
                }
                
                if (currentPage.Length > 0)
                {
                    pages.Add(currentPage);
                }
            }

            return pages.Count > 0 ? pages : new List<string> { "No text to display" };
        }

        private Color SampleScreenColor(int x, int y)
        {
            IntPtr hdc = GetDC(IntPtr.Zero);
            uint pixel = GetPixel(hdc, x, y);
            ReleaseDC(IntPtr.Zero, hdc);
            
            Color color = Color.FromArgb(
                (int)(pixel & 0x000000FF),
                (int)((pixel & 0x0000FF00) >> 8),
                (int)((pixel & 0x00FF0000) >> 16)
            );
            
            return color;
        }

        private Color GetAdaptiveTextColor(int screenX, int screenY)
        {
            Color bgColor = SampleScreenColor(screenX, screenY);
            
            // Calculate perceived brightness (0-255)
            float brightness = (bgColor.R * 0.299f + bgColor.G * 0.587f + bgColor.B * 0.114f);
            
            // Shift ratios from config
            float brightnessShift = config.BrightnessShiftRatio / 100f;
            float colorShift = config.ColorShiftRatio / 100f;
            
            // Invert brightness direction
            float targetBrightness = brightness < 128 ? 255 : 0;
            float newBrightness = brightness + (targetBrightness - brightness) * brightnessShift;
            
            // Shift color away from background
            int r = (int)Math.Clamp(bgColor.R + (255 - bgColor.R * 2) * colorShift, 0, 255);
            int g = (int)Math.Clamp(bgColor.G + (255 - bgColor.G * 2) * colorShift, 0, 255);
            int b = (int)Math.Clamp(bgColor.B + (255 - bgColor.B * 2) * colorShift, 0, 255);
            
            // Normalize to target brightness
            float currentBrightness = (r * 0.299f + g * 0.587f + b * 0.114f);
            if (currentBrightness > 0)
            {
                float scale = newBrightness / currentBrightness;
                r = (int)Math.Clamp(r * scale, 0, 255);
                g = (int)Math.Clamp(g * scale, 0, 255);
                b = (int)Math.Clamp(b * scale, 0, 255);
            }
            
            return Color.FromArgb(255, r, g, b);
        }

        private void ToggleVisibility()
        {
            this.Visible = !this.Visible;
        }

        private void PreviousPage()
        {
            if (textPages == null || textPages.Count == 0) return;
            currentPage = (currentPage - 1 + textPages.Count) % textPages.Count;
            this.Invalidate();
        }

        private void NextPage()
        {
            if (textPages == null || textPages.Count == 0) return;
            currentPage = (currentPage + 1) % textPages.Count;
            this.Invalidate();
        }

        private void IncreaseShiftRatio()
        {
            config.IncreaseBrightnessShift();
            this.Invalidate();
        }

        private void DecreaseShiftRatio()
        {
            config.DecreaseBrightnessShift();
            this.Invalidate();
        }
        
        private void TeleportToCursor()
        {
            int newX = Cursor.Position.X - (this.Width / 2);
            int newY = Cursor.Position.Y - (this.Height / 2);
            this.Location = new Point(newX, newY);
            
            if (!this.Visible)
            {
                this.Show();
            }
        }

        private void MainForm_Paint(object? sender, PaintEventArgs e)
        {
            if (textPages == null || textPages.Count == 0) return;

            if (currentPage >= textPages.Count) currentPage = 0;
            if (textPages.Count == 0) return;

            // Fill background with transparency key
            e.Graphics.Clear(Color.Magenta);

            string pageText = textPages[currentPage];
            
            // Sample multiple points and average for more stable color
            int centerX = this.Left + this.Width / 2;
            int centerY = this.Top + this.Height / 2;
            
            List<Color> samples = new List<Color>();
            for (int i = -1; i <= 1; i++)
            {
                for (int j = -1; j <= 1; j++)
                {
                    int sampleX = centerX + i * (this.Width / 4);
                    int sampleY = centerY + j * (this.Height / 4);
                    samples.Add(GetAdaptiveTextColor(sampleX, sampleY));
                }
            }
            
            // Average the colors
            int avgR = (int)samples.Average(c => c.R);
            int avgG = (int)samples.Average(c => c.G);
            int avgB = (int)samples.Average(c => c.B);
            Color textColor = Color.FromArgb(255, avgR, avgG, avgB);

            // Draw text with outline for better visibility
            using (GraphicsPath path = new GraphicsPath())
            using (Pen outlinePen = new Pen(Color.FromArgb(128, 
                textColor.R > 128 ? 0 : 255, 
                textColor.G > 128 ? 0 : 255, 
                textColor.B > 128 ? 0 : 255), 3))
            using (SolidBrush textBrush = new SolidBrush(textColor))
            {
                RectangleF textRect = new RectangleF(20, 20, this.Width - 40, this.Height - 40);
                
                // Create text path
                path.AddString(pageText, textFont.FontFamily, (int)textFont.Style, 
                    e.Graphics.DpiY * textFont.SizeInPoints / 72, textRect, 
                    StringFormat.GenericDefault);
                
                // Draw outline then fill
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.DrawPath(outlinePen, path);
                e.Graphics.FillPath(textBrush, path);
            }

            // Page info
            string pageInfo = $"Page {currentPage + 1}/{textPages.Count} | Shift: {config.BrightnessShiftRatio}%";
            using (SolidBrush brush = new SolidBrush(textColor))
            {
                e.Graphics.DrawString(pageInfo, new Font("Arial", 8), brush, this.Width - 180, this.Height - 25);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            refreshTimer?.Stop();
            refreshTimer?.Dispose();
            
            UnregisterHotKey(this.Handle, HOTKEY_F1_HIDE);
            UnregisterHotKey(this.Handle, HOTKEY_F3_PREV);
            UnregisterHotKey(this.Handle, HOTKEY_F4_NEXT);
            UnregisterHotKey(this.Handle, HOTKEY_F5_OPACITY_DOWN);
            UnregisterHotKey(this.Handle, HOTKEY_F6_OPACITY_UP);
            UnregisterHotKey(this.Handle, HOTKEY_F2_TELEPORT);
            UnregisterHotKey(this.Handle, HOTKEY_CTRL_F1_SHOW);
            
            trayIcon?.Dispose();
            
            base.OnFormClosing(e);
        }
    }
}