using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CamoReader
{
    public partial class MainForm : Form
    {
        #region WinAPI Imports
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

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
        private const int WS_EX_TRANSPARENT = 0x20; // Makes window click-through
        #endregion

        #region Fields
        private NotifyIcon trayIcon = null!;
        private ConfigManager config = null!;
        private List<string> textPages = null!;
        private int currentPage = 0;
        private Color textColor = Color.White;
        private Font textFont = null!;
        
        // --- REMOVED Dragging fields ---
        #endregion

        public MainForm()
        {
            InitializeComponent();
            LoadConfiguration();
            SetupWindow();
            SetupTrayIcon();
            RegisterHotkeys();
            LoadAndPaginateText();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.AutoScaleDimensions = new SizeF(6F, 13F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.BackColor = Color.Black; 
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
                // FIX: Re-added WS_EX_TRANSPARENT to make window click-through
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT; 
                return cp;
            }
        }

        private void LoadConfiguration()
        {
            config = new ConfigManager("config.ini");
            
            this.Location = new Point(config.WindowPosX, config.WindowPosY);
            this.Size = new Size(config.WindowWidth, config.WindowHeight);
            
            this.TransparencyKey = Color.Black;
            this.Opacity = (double)config.TextOpacity / 100.0;
            
            textFont = new Font("Arial", config.TextSize);
            textColor = Color.White;
        }

        private void SetupWindow()
        {
            this.DoubleBuffered = true;
            this.Paint += MainForm_Paint;
            
            // --- REMOVED Mouse handlers for dragging ---
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
                    // Tray icon click still toggles
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
            
            // FIX: Register F2 and Ctrl+F1
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
                    // FIX: Changed to explicit Show/Hide
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
                        DecreaseOpacity();
                        break;
                    case HOTKEY_F6_OPACITY_UP:
                        IncreaseOpacity();
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

        private void IncreaseOpacity()
        {
            if (this.Opacity <= 0.95) 
            {
                this.Opacity += 0.05;
            }
        }

        private void DecreaseOpacity()
        {
            if (this.Opacity >= 0.10) 
            {
                this.Opacity -= 0.05;
            }
        }
        
        // --- FIX: New method to teleport window ---
        private void TeleportToCursor()
        {
            // Center the window on the cursor
            int newX = Cursor.Position.X - (this.Width / 2);
            int newY = Cursor.Position.Y - (this.Height / 2);
            this.Location = new Point(newX, newY);
            
            // Ensure the window is visible if we teleport it
            if (!this.Visible)
            {
                this.Show();
            }
        }
        // --- End of new method ---
        
        // --- REMOVED Mouse drag methods ---

        private void MainForm_Paint(object? sender, PaintEventArgs e)
        {
            if (textPages == null || textPages.Count == 0) return;

            if (currentPage >= textPages.Count) currentPage = 0; 
            if (textPages.Count == 0) return; 

            using (SolidBrush brush = new SolidBrush(textColor))
            {
                string pageText = textPages[currentPage];
                e.Graphics.DrawString(pageText, textFont, brush, new RectangleF(20, 20, this.Width - 40, this.Height - 40));
            }

            string pageInfo = $"Page {currentPage + 1}/{textPages.Count}";
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(128, textColor)))
            {
                e.Graphics.DrawString(pageInfo, new Font("Arial", 8), brush, this.Width - 100, this.Height - 25);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            UnregisterHotKey(this.Handle, HOTKEY_F1_HIDE);
            UnregisterHotKey(this.Handle, HOTKEY_F3_PREV);
            UnregisterHotKey(this.Handle, HOTKEY_F4_NEXT);
            UnregisterHotKey(this.Handle, HOTKEY_F5_OPACITY_DOWN);
            UnregisterHotKey(this.Handle, HOTKEY_F6_OPACITY_UP);
            
            // FIX: Unregister new hotkeys
            UnregisterHotKey(this.Handle, HOTKEY_F2_TELEPORT);
            UnregisterHotKey(this.Handle, HOTKEY_CTRL_F1_SHOW);
            
            trayIcon?.Dispose();
            
            base.OnFormClosing(e);
        }
    }
}