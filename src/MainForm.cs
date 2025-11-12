using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// --- REMOVED WinRT 'using' statements ---
// using Windows.Graphics.Capture;
// using Windows.Graphics.DirectX;
// using Windows.Graphics.DirectX.Direct3D11;

using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;

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
        private NotifyIcon trayIcon;
        private ConfigManager config;
        private List<string> textPages;
        private int currentPage = 0;
        private Color textColor = Color.White;
        private Font textFont;
        private Device d3dDevice;
        // --- REMOVED captureDevice field ---
        // private IDirect3DDevice captureDevice;
        #endregion

        public MainForm()
        {
            InitializeComponent();
            LoadConfiguration();
            SetupWindow();
            SetupTrayIcon();
            RegisterHotkeys();
            InitializeD3D(); // This is still needed for SharpDX
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
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT;
                return cp;
            }
        }

        private void LoadConfiguration()
        {
            config = new ConfigManager("config.ini");
            
            this.Location = new Point(config.WindowPosX, config.WindowPosY);
            this.Size = new Size(config.WindowWidth, config.WindowHeight);
            this.Opacity = 0.75;
            
            textFont = new Font("Arial", config.TextSize);
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

        protected override void WndProc(ref System.Windows.Forms.Message m)
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

        private void InitializeD3D()
        {
            try
            {
                d3dDevice = new Device(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.BgraSupport);
                // --- REMOVED problematic line ---
                // captureDevice = Direct3D11Helper.CreateDevice(d3dDevice); 
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize Direct3D: {ex.Message}", "Error");
            }
        }

        private void LoadAndPaginateText()
        {
            textPages = new List<string>();

            if (!File.Exists(config.TextFilePath))
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
            if (this.Visible)
            {
                UpdateTextColorAsync();
            }
        }

        private void PreviousPage()
        {
            if (textPages.Count == 0) return;
            currentPage = (currentPage - 1 + textPages.Count) % textPages.Count;
            UpdateTextColorAsync();
        }

        private void NextPage()
        {
            if (textPages.Count == 0) return;
            currentPage = (currentPage + 1) % textPages.Count;
            UpdateTextColorAsync();
        }

        private async void UpdateTextColorAsync()
        {
            try
            {
                double avgBrightness = await CaptureAndAnalyzeBackground();
                double threshold = 255 * (config.BrightnessShiftRatio / 100.0);
                
                textColor = avgBrightness > threshold ? Color.Black : Color.White;
            }
            catch
            {
                textColor = Color.White;
            }

            this.Invalidate();
        }

        // --- REMOVED CheckWindowsCaptureSupport() as it's no longer needed ---

        // --- COMPLETELY REWRITTEN to use SharpDX.DXGI ---
        private async Task<double> CaptureAndAnalyzeBackground()
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Factory1 to create device and adapter
                    using (var factory = new Factory1())
                    // Get first adapter
                    using (var adapter = factory.GetAdapter1(0))
                    // Get first output (monitor)
                    using (var output = adapter.GetOutput(0))
                    using (var output1 = output.QueryInterface<Output1>())
                    {
                        // Create a staging texture description
                        var textureDesc = new Texture2DDescription
                        {
                            CpuAccessFlags = CpuAccessFlags.Read,
                            BindFlags = BindFlags.None,
                            Format = Format.B8G8R8A8_UNorm,
                            Width = output1.Description.DesktopBounds.Width,
                            Height = output1.Description.DesktopBounds.Height,
                            OptionFlags = ResourceOptionFlags.None,
                            MipLevels = 1,
                            ArraySize = 1,
                            SampleDescription = { Count = 1, Quality = 0 },
                            Usage = ResourceUsage.Staging
                        };

                        using (var stagingTexture = new Texture2D(d3dDevice, textureDesc))
                        // Duplicate the output
                        using (var duplicatedOutput = output1.DuplicateOutput(d3dDevice))
                        {
                            SharpDX.DXGI.Resource screenResource = null;
                            try
                            {
                                // Try to get a frame
                                var result = duplicatedOutput.TryAcquireNextFrame(100, out _, out screenResource);
                                
                                if (!result.Success || screenResource == null)
                                {
                                    duplicatedOutput.ReleaseFrame();
                                    return 127; // Default brightness if capture failed
                                }

                                // Copy the captured texture to the staging texture
                                using (var screenTexture = screenResource.QueryInterface<Texture2D>())
                                {
                                    d3dDevice.ImmediateContext.CopyResource(screenTexture, stagingTexture);
                                }

                                // Analyze the staging texture
                                var brightness = CalculateBrightness(stagingTexture);
                                
                                duplicatedOutput.ReleaseFrame();
                                return brightness;
                            }
                            finally
                            {
                                screenResource?.Dispose();
                            }
                        }
                    }
                }
                catch
                {
                    return 127; // Default brightness on any error
                }
            });
        }
        
        // --- REMOVED GetPrimaryMonitorHandle() ---

        private double CalculateBrightness(Texture2D texture)
        {
            var desc = texture.Description;
            var context = d3dDevice.ImmediateContext;
            
            var dataBox = context.MapSubresource(texture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
            
            long totalBrightness = 0;
            int pixelCount = 0;
            
            unsafe
            {
                byte* ptr = (byte*)dataBox.DataPointer;
                int stride = dataBox.RowPitch;
                
                // --- MODIFIED: Sample a grid instead of every pixel for speed ---
                int step = Math.Max(1, Math.Min(desc.Width, desc.Height) / 100); // Sample ~10,000 pixels
                
                for (int y = 0; y < desc.Height; y += step)
                {
                    byte* row = ptr + (y * stride);
                    for (int x = 0; x < desc.Width; x += step)
                    {
                        byte b = row[x * 4];
                        byte g = row[x * 4 + 1];
                        byte r = row[x * 4 + 2];
                        
                        int brightness = (r * 299 + g * 587 + b * 114) / 1000;
                        totalBrightness += brightness;
                        pixelCount++;
                    }
                }
            }
            
            context.UnmapSubresource(texture, 0);
            
            return pixelCount > 0 ? (double)totalBrightness / pixelCount : 127;
        }

        private void MainForm_Paint(object sender, PaintEventArgs e)
        {
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
            
            trayIcon.Visible = false;
            trayIcon.Dispose();
            
            d3dDevice?.Dispose();
            // --- REMOVED captureDevice dispose ---
            
            base.OnFormClosing(e);
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }


WindowPosX = 100
WindowPosY = 100
WindowWidth = 800
WindowHeight = 200
TextSize = 14
TextFilePath = book.txt
BrightnessShiftRatio = 50
ColorShiftRatio = 50";
            File.WriteAllText(filePath, defaultConfig);
        }

        private int GetInt(string key, int defaultValue)
        {
            return settings.ContainsKey(key) && int.TryParse(settings[key], out int val) ? val : defaultValue;
        }

        private string GetString(string key, string defaultValue)
        {
            return settings.ContainsKey(key) ? settings[key] : defaultValue;
        }
    }
    
    // --- REMOVED ALL WinRT HELPER CLASSES ---
    // (Direct3D11Helper, IDirect3DDxgiInterfaceAccess, IGraphicsCaptureSession3)
    
    #endregion
}