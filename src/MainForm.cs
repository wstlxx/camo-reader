using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// --- REMOVED ALL SHARPDX and WINRT 'using' statements ---

namespace CamoReader
{
    public partial class MainForm : Form
    {
        #region WinAPI Imports
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_F1 = 1;
        private const int HOTKEY_F3 = 2;
        private const int HOTKEY_F4 = 3;
        private const uint VK_F1 = 0x70;
        private const uint VK_F3 = 0x72;
        private const uint VK_F4 = 0x73;

        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;
        #endregion

        #region Fields
        // FIX: Initialized fields to `null!` to satisfy nullability warnings
        private NotifyIcon trayIcon = null!;
        private ConfigManager config = null!;
        private List<string> textPages = null!;
        private int currentPage = 0;
        private Color textColor = Color.White; // Default to white
        private Font textFont = null!;
        // --- REMOVED d3dDevice field ---
        #endregion

        public MainForm()
        {
            InitializeComponent();
            LoadConfiguration();
            SetupWindow();
            SetupTrayIcon();
            RegisterHotkeys();
            // --- REMOVED InitializeD3D() ---
            LoadAndPaginateText();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.AutoScaleDimensions = new SizeF(6F, 13F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.BackColor = Color.Black; // This will be the transparent color
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
                // WS_EX_LAYERED is required for TransparencyKey
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT;
                return cp;
            }
        }

        private void LoadConfiguration()
        {
            config = new ConfigManager("config.ini");
            
            this.Location = new Point(config.WindowPosX, config.WindowPosY);
            this.Size = new Size(config.WindowWidth, config.WindowHeight);
            
            // --- FIX: Use TransparencyKey for a fully transparent background ---
            this.TransparencyKey = Color.Black;
            // --- REMOVED Opacity property ---
            
            textFont = new Font("Arial", config.TextSize);
            
            // --- FIX: Set a default text color (no longer adaptive) ---
            textColor = Color.White;
        }

        private void SetupWindow()
        {
            this.DoubleBuffered = true;
            this.Paint += MainForm_Paint;
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
            RegisterHotKey(this.Handle, HOTKEY_F1, 0, VK_F1);
            RegisterHotKey(this.Handle, HOTKEY_F3, 0, VK_F3);
            RegisterHotKey(this.Handle, HOTKEY_F4, 0, VK_F4);
        }

        // --- FIX: Changed back to 'Message' as SharpDX ambiguity is gone ---
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                switch (id)
                {
                    case HOTKEY_F1:
                        ToggleVisibility();
                        break;
                    case HOTKEY_F3:
                        PreviousPage();
                        break;
                    case HOTKEY_F4:
                        NextPage();
                        break;
                }
            }
        }

        // --- DELETED InitializeD3D() method ---

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
            // --- REMOVED UpdateTextColorAsync() call ---
        }

        private void PreviousPage()
        {
            if (textPages == null || textPages.Count == 0) return;
            currentPage = (currentPage - 1 + textPages.Count) % textPages.Count;
            this.Invalidate(); // Redraw with the new page
        }

        private void NextPage()
        {
            if (textPages == null || textPages.Count == 0) return;
            currentPage = (currentPage + 1) % textPages.Count;
            this.Invalidate(); // Redraw with the new page
        }

        // --- DELETED UpdateTextColorAsync() method ---
        // --- DELETED CaptureAndAnalyzeBackground() method ---
        // --- DELETED AnalyzeTexture() method ---

        // FIX: Made sender nullable to fix nullability warning
        private void MainForm_Paint(object? sender, PaintEventArgs e)
        {
            if (textPages == null || textPages.Count == 0) return;

            // Prevent crash if textPages is empty or index is out of bounds
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
            UnregisterHotKey(this.Handle, HOTKEY_F1);
            UnregisterHotKey(this.Handle, HOTKEY_F3);
            UnregisterHotKey(this.Handle, HOTKEY_F4);
            
            trayIcon?.Dispose();
            // --- REMOVED d3dDevice?.Dispose() ---
            
            base.OnFormClosing(e);
        }

        // --- DELETED duplicate Main() method ---
    }
}