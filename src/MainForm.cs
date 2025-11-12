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

        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_F1 = 1;
        private const int HOTKEY_F3 = 2;
        private const int HOTKEY_F4 = 3;
        // FIX: Added F5 and F6
        private const int HOTKEY_F5 = 4;
        private const int HOTKEY_F6 = 5;

        private const uint VK_F1 = 0x70;
        private const uint VK_F3 = 0x72;
        private const uint VK_F4 = 0x73;
        // FIX: Added F5 and F6
        private const uint VK_F5 = 0x74;
        private const uint VK_F6 = 0x75;

        private const int WS_EX_LAYERED = 0x80000;
        // --- REMOVED WS_EX_TRANSPARENT to allow clicking/dragging ---
        #endregion

        #region Fields
        private NotifyIcon trayIcon = null!;
        private ConfigManager config = null!;
        private List<string> textPages = null!;
        private int currentPage = 0;
        private Color textColor = Color.White;
        private Font textFont = null!;
        
        // FIX: Added fields for window dragging
        private bool isDragging = false;
        private Point dragStartPoint = Point.Empty;
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
                // WS_EX_LAYERED is required for TransparencyKey
                // FIX: REMOVED WS_EX_TRANSPARENT so the window can be clicked
                cp.ExStyle |= WS_EX_LAYERED; 
                return cp;
            }
        }

        private void LoadConfiguration()
        {
            config = new ConfigManager("config.ini");
            
            this.Location = new Point(config.WindowPosX, config.WindowPosY);
            this.Size = new Size(config.WindowWidth, config.WindowHeight);
            
            // FIX: Use TransparencyKey to make background invisible
            this.TransparencyKey = Color.Black;
            // FIX: Use Opacity to make text (and whole window) semi-transparent
            this.Opacity = (double)config.TextOpacity / 100.0;
            
            textFont = new Font("Arial", config.TextSize);
            textColor = Color.White;
        }

        private void SetupWindow()
        {
            this.DoubleBuffered = true;
            this.Paint += MainForm_Paint;
            
            // FIX: Add mouse handlers for dragging
            this.MouseDown += OnMouseDown;
            this.MouseMove += OnMouseMove;
            this.MouseUp += OnMouseUp;
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
            // FIX: Register F5 and F6
            RegisterHotKey(this.Handle, HOTKEY_F5, 0, VK_F5);
            RegisterHotKey(this.Handle, HOTKEY_F6, 0, VK_F6);
        }

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
                    // FIX: Handle F5 and F6
                    case HOTKEY_F5:
                        DecreaseOpacity();
                        break;
                    case HOTKEY_F6:
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

        // --- FIX: New methods for opacity control ---
        private void IncreaseOpacity()
        {
            if (this.Opacity <= 0.95) // Use <= 0.95 to avoid precision issues
            {
                this.Opacity += 0.05;
            }
        }

        private void DecreaseOpacity()
        {
            // Don't let it become completely invisible, min 5%
            if (this.Opacity >= 0.10) 
            {
                this.Opacity -= 0.05;
            }
        }
        
        // --- FIX: New methods for window dragging ---
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                isDragging = true;
                dragStartPoint = new Point(e.X, e.Y);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (isDragging)
            {
                Point currentPoint = PointToScreen(new Point(e.X, e.Y));
                this.Location = new Point(currentPoint.X - dragStartPoint.X, currentPoint.Y - dragStartPoint.Y);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                isDragging = false;
            }
        }
        // --- End of new drag methods ---

        // FIX: Made sender nullable to fix warning
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
            UnregisterHotKey(this.Handle, HOTKEY_F1);
            UnregisterHotKey(this.Handle, HOTKEY_F3);
            UnregisterHotKey(this.Handle, HOTKEY_F4);
            // FIX: Unregister F5 and F6
            UnregisterHotKey(this.Handle, HOTKEY_F5);
            UnregisterHotKey(this.Handle, HOTKEY_F6);
            
            trayIcon?.Dispose();
            
            base.OnFormClosing(e);
        }
    }
}