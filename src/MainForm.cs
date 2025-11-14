using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

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

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height, IntPtr hdcSrc, int xSrc, int ySrc, int rop);

        private const int SRCCOPY = 0x00CC0020;

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
        private bool isRendering = false;
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
            
            this.TransparencyKey = Color.Magenta;
            this.Opacity = 1.0;
            
            textFont = new Font("Arial", config.TextSize, FontStyle.Regular);
        }

        private void SetupWindow()
        {
            this.DoubleBuffered = true;
            this.Paint += MainForm_Paint;
        }

        private void SetupRefreshTimer()
        {
            refreshTimer = new System.Windows.Forms.Timer();
            refreshTimer.Interval = 100;
            refreshTimer.Tick += (s, e) => 
            {
                if (!isRendering)
                {
                    this.Invalidate();
                }
            };
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

        private Bitmap CaptureScreenRegion(int x, int y, int width, int height)
        {
            try
            {
                Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
                }
                return bmp;
            }
            catch
            {
                Bitmap fallback = new Bitmap(width, height);
                using (Graphics g = Graphics.FromImage(fallback))
                {
                    g.Clear(Color.Black);
                }
                return fallback;
            }
        }

        private Color AdaptColor(Color bgColor)
        {
            float brightnessShift = config.BrightnessShiftRatio / 100f;
            float colorShift = config.ColorShiftRatio / 100f;
            
            if (brightnessShift == 0 && colorShift == 0)
            {
                return bgColor;
            }
            
            float bgBrightness = (bgColor.R * 0.299f + bgColor.G * 0.587f + bgColor.B * 0.114f) / 255f;
            
            float r = bgColor.R / 255f;
            float g = bgColor.G / 255f;
            float b = bgColor.B / 255f;
            
            if (brightnessShift > 0)
            {
                float targetBrightness = bgBrightness < 0.5f ? 1.0f : 0.0f;
                float brightnessDelta = (targetBrightness - bgBrightness) * brightnessShift;
                
                r += brightnessDelta;
                g += brightnessDelta;
                b += brightnessDelta;
            }
            
            if (colorShift > 0)
            {
                r = r + ((1.0f - r) - r) * colorShift;
                g = g + ((1.0f - g) - g) * colorShift;
                b = b + ((1.0f - b) - b) * colorShift;
            }
            
            r = Math.Clamp(r, 0f, 1f);
            g = Math.Clamp(g, 0f, 1f);
            b = Math.Clamp(b, 0f, 1f);
            
            return Color.FromArgb(255, (int)(r * 255), (int)(g * 255), (int)(b * 255));
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
            if (isRendering) return;
            isRendering = true;

            try
            {
                if (textPages == null || textPages.Count == 0) return;
                if (currentPage >= textPages.Count) currentPage = 0;

                string pageText = textPages[currentPage];
                
                // CRITICAL: Capture screen FRESH every paint - get current window position
                int captureX = this.Left;
                int captureY = this.Top;
                int captureW = this.Width;
                int captureH = this.Height;
                
                // Hide window briefly to capture what's behind it
                this.Visible = false;
                Thread.Sleep(10); // Small delay to let window disappear
                
                using (Bitmap screenCapture = CaptureScreenRegion(captureX, captureY, captureW, captureH))
                {
                    this.Visible = true; // Show window again
                    
                    using (Bitmap output = new Bitmap(captureW, captureH, PixelFormat.Format32bppArgb))
                    {
                        // Create text mask
                        using (Bitmap textMask = new Bitmap(captureW, captureH, PixelFormat.Format32bppArgb))
                        {
                            using (Graphics gMask = Graphics.FromImage(textMask))
                            {
                                gMask.Clear(Color.Transparent);
                                gMask.SmoothingMode = SmoothingMode.AntiAlias;
                                gMask.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                                
                                RectangleF textRect = new RectangleF(20, 20, captureW - 40, captureH - 40);
                                using (SolidBrush whiteBrush = new SolidBrush(Color.White))
                                {
                                    gMask.DrawString(pageText, textFont, whiteBrush, textRect);
                                }
                            }
                            
                            // Lock both bitmaps for pixel processing
                            BitmapData maskData = textMask.LockBits(
                                new Rectangle(0, 0, captureW, captureH),
                                ImageLockMode.ReadOnly,
                                PixelFormat.Format32bppArgb);
                            
                            BitmapData screenData = screenCapture.LockBits(
                                new Rectangle(0, 0, captureW, captureH),
                                ImageLockMode.ReadOnly,
                                PixelFormat.Format32bppArgb);
                            
                            BitmapData outputData = output.LockBits(
                                new Rectangle(0, 0, captureW, captureH),
                                ImageLockMode.WriteOnly,
                                PixelFormat.Format32bppArgb);
                            
                            unsafe
                            {
                                byte* maskPtr = (byte*)maskData.Scan0;
                                byte* screenPtr = (byte*)screenData.Scan0;
                                byte* outPtr = (byte*)outputData.Scan0;
                                int stride = outputData.Stride;
                                
                                for (int y = 0; y < captureH; y++)
                                {
                                    for (int x = 0; x < captureW; x++)
                                    {
                                        int idx = y * stride + x * 4;
                                        
                                        // Check if this pixel is part of text
                                        byte alpha = maskPtr[idx + 3];
                                        
                                        if (alpha > 30) // Text pixel
                                        {
                                            // Get the background color at THIS specific pixel
                                            Color bgPixel = Color.FromArgb(
                                                screenPtr[idx + 2], 
                                                screenPtr[idx + 1], 
                                                screenPtr[idx]
                                            );
                                            
                                            // Adapt this specific pixel's color
                                            Color adaptedPixel = AdaptColor(bgPixel);
                                            
                                            // Blend based on text alpha for smooth edges
                                            float alphaRatio = alpha / 255f;
                                            outPtr[idx] = (byte)(bgPixel.B * (1 - alphaRatio) + adaptedPixel.B * alphaRatio);
                                            outPtr[idx + 1] = (byte)(bgPixel.G * (1 - alphaRatio) + adaptedPixel.G * alphaRatio);
                                            outPtr[idx + 2] = (byte)(bgPixel.R * (1 - alphaRatio) + adaptedPixel.R * alphaRatio);
                                            outPtr[idx + 3] = 255;
                                        }
                                        else // Background pixel - keep original
                                        {
                                            outPtr[idx] = screenPtr[idx];
                                            outPtr[idx + 1] = screenPtr[idx + 1];
                                            outPtr[idx + 2] = screenPtr[idx + 2];
                                            outPtr[idx + 3] = 255;
                                        }
                                    }
                                }
                            }
                            
                            textMask.UnlockBits(maskData);
                            screenCapture.UnlockBits(screenData);
                            output.UnlockBits(outputData);
                        }
                        
                        // Draw page info
                        using (Graphics gOutput = Graphics.FromImage(output))
                        {
                            string pageInfo = $"Page {currentPage + 1}/{textPages.Count} | B:{config.BrightnessShiftRatio}% C:{config.ColorShiftRatio}%";
                            
                            int infoX = 10;
                            int infoY = captureH - 25;
                            Color infoColor = Color.White;
                            
                            if (infoX >= 0 && infoX < screenCapture.Width && infoY >= 0 && infoY < screenCapture.Height)
                            {
                                infoColor = AdaptColor(screenCapture.GetPixel(infoX, infoY));
                            }
                            
                            using (SolidBrush brush = new SolidBrush(Color.FromArgb(200, infoColor)))
                            {
                                gOutput.DrawString(pageInfo, new Font("Arial", 8), brush, infoX, infoY);
                            }
                        }
                        
                        // Clear to transparency key then draw result
                        e.Graphics.Clear(Color.Magenta);
                        e.Graphics.DrawImage(output, 0, 0);
                    }
                }
            }
            catch (Exception ex)
            {
                this.Visible = true; // Ensure window is visible on error
                e.Graphics.Clear(Color.Magenta);
                using (SolidBrush brush = new SolidBrush(Color.White))
                {
                    e.Graphics.DrawString($"Error: {ex.Message}", this.Font, brush, 10, 10);
                }
            }
            finally
            {
                isRendering = false;
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