using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
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
        private IDirect3DDevice captureDevice;
        #endregion

        public MainForm()
        {
            InitializeComponent();
            LoadConfiguration();
            SetupWindow();
            SetupTrayIcon();
            RegisterHotkeys();
            InitializeD3D();
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
            // Create transparent icon programmatically
            Bitmap bmp = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                // Draw a simple "C" for Camo-Reader
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

        private void InitializeD3D()
        {
            try
            {
                d3dDevice = new Device(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.BgraSupport);
                captureDevice = Direct3D11Helper.CreateDevice(d3dDevice);
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
            if (!CheckWindowsCaptureSupport())
            {
                textColor = Color.White;
                this.Invalidate();
                return;
            }

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

        private bool CheckWindowsCaptureSupport()
        {
            var version = Environment.OSVersion.Version;
            // Windows 11 or Windows 10 >= 2104 (build 19041.928+)
            return version.Major >= 10 && version.Build >= 19041;
        }

        private async Task<double> CaptureAndAnalyzeBackground()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var picker = new GraphicsCapturePicker();
                    var item = GraphicsCaptureItem.CreateFromMonitorHandle(GetPrimaryMonitorHandle());
                    
                    if (item == null) return 127;

                    using (var framePool = Direct3D11CaptureFramePool.Create(
                        captureDevice,
                        DirectXPixelFormat.B8G8R8A8UIntNormalized,
                        1,
                        item.Size))
                    {
                        var session = framePool.CreateCaptureSession(item);
                        
                        // Disable yellow border on supported versions
                        try
                        {
                            var session3 = session as IGraphicsCaptureSession3;
                            if (session3 != null)
                            {
                                session3.IsBorderRequired = false;
                            }
                        }
                        catch { }

                        session.StartCapture();
                        
                        using (var frame = framePool.TryGetNextFrame())
                        {
                            if (frame == null) return 127;

                            var texture = Direct3D11Helper.CreateSharpDXTexture2D(frame.Surface);
                            var brightness = CalculateBrightness(texture);
                            
                            session.Dispose();
                            return brightness;
                        }
                    }
                }
                catch
                {
                    return 127;
                }
            });
        }

        private IntPtr GetPrimaryMonitorHandle()
        {
            return Screen.PrimaryScreen.GetHashCode();
        }

        private double CalculateBrightness(Texture2D texture)
        {
            var desc = texture.Description;
            var context = d3dDevice.ImmediateContext;
            
            // Create staging texture for CPU read
            var stagingDesc = new Texture2DDescription
            {
                Width = desc.Width,
                Height = desc.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = desc.Format,
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                CpuAccessFlags = CpuAccessFlags.Read,
                SampleDescription = new SampleDescription(1, 0)
            };

            using (var staging = new Texture2D(d3dDevice, stagingDesc))
            {
                context.CopyResource(texture, staging);
                
                var dataBox = context.MapSubresource(staging, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
                
                long totalBrightness = 0;
                int pixelCount = 0;
                
                unsafe
                {
                    byte* ptr = (byte*)dataBox.DataPointer;
                    int stride = dataBox.RowPitch;
                    
                    for (int y = 0; y < desc.Height; y++)
                    {
                        byte* row = ptr + (y * stride);
                        for (int x = 0; x < desc.Width; x++)
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
                
                context.UnmapSubresource(staging, 0);
                
                return pixelCount > 0 ? (double)totalBrightness / pixelCount : 127;
            }
        }

        private void MainForm_Paint(object sender, PaintEventArgs e)
        {
            if (textPages.Count == 0) return;

            using (SolidBrush brush = new SolidBrush(textColor))
            {
                string pageText = textPages[currentPage];
                e.Graphics.DrawString(pageText, textFont, brush, new RectangleF(20, 20, this.Width - 40, this.Height - 40));
            }

            // Page indicator
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
            captureDevice?.Dispose();
            
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

    #region Helper Classes
    public class ConfigManager
    {
        private string filePath;
        private Dictionary<string, string> settings;

        public int WindowPosX { get; private set; }
        public int WindowPosY { get; private set; }
        public int WindowWidth { get; private set; }
        public int WindowHeight { get; private set; }
        public int TextSize { get; private set; }
        public string TextFilePath { get; private set; }
        public int BrightnessShiftRatio { get; private set; }
        public int ColorShiftRatio { get; private set; }

        public ConfigManager(string path)
        {
            filePath = path;
            settings = new Dictionary<string, string>();
            LoadConfig();
        }

        private void LoadConfig()
        {
            if (!File.Exists(filePath))
            {
                CreateDefaultConfig();
            }

            foreach (string line in File.ReadAllLines(filePath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith(";"))
                    continue;

                string[] parts = line.Split(new[] { '=' }, 2);
                if (parts.Length == 2)
                {
                    settings[parts[0].Trim()] = parts[1].Trim();
                }
            }

            WindowPosX = GetInt("WindowPosX", 100);
            WindowPosY = GetInt("WindowPosY", 100);
            WindowWidth = GetInt("WindowWidth", 800);
            WindowHeight = GetInt("WindowHeight", 200);
            TextSize = GetInt("TextSize", 14);
            TextFilePath = GetString("TextFilePath", "book.txt");
            BrightnessShiftRatio = GetInt("BrightnessShiftRatio", 50);
            ColorShiftRatio = GetInt("ColorShiftRatio", 50);
        }

        private void CreateDefaultConfig()
        {
            string defaultConfig = @"; Camo-Reader Configuration
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

    public static class Direct3D11Helper
    {
        [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice")]
        private static extern uint CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

        public static IDirect3DDevice CreateDevice(Device device)
        {
            using (var dxgiDevice = device.QueryInterface<SharpDX.DXGI.Device>())
            {
                CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out IntPtr pUnknown);
                var d3dDevice = Marshal.GetObjectForIUnknown(pUnknown) as IDirect3DDevice;
                Marshal.Release(pUnknown);
                return d3dDevice;
            }
        }

        public static Texture2D CreateSharpDXTexture2D(IDirect3DSurface surface)
        {
            var access = surface as Windows.Graphics.DirectX.Direct3D11.IDirect3DDxgiInterfaceAccess;
            var pResource = access.GetInterface(new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c"));
            return new Texture2D(pResource);
        }
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    }

    public interface IGraphicsCaptureSession3
    {
        bool IsBorderRequired { get; set; }
    }
    #endregion
}